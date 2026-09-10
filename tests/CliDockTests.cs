using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskWidget
{
    internal static class CliDockTests
    {
        private static int checks;
        private static void Check(bool value, string message) { if (!value) throw new Exception("CLI/side: " + message); checks++; }
        private static object Field(object obj, string name) { return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj); }
        private static void Call(object obj, string name) { obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, null); }
        internal static int Live()
        {
            string selected = DollarSpark.FindExecutableAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (selected == null) throw new Exception("No usable CLI found");
            Console.WriteLine("CLI: " + selected);
            Console.WriteLine(DollarSpark.RunProcess(selected, "--version", null, Path.GetDirectoryName(selected), CancellationToken.None).GetAwaiter().GetResult());
            return 1; // Only --version is run. No login, model request, or UI interaction.
        }
        internal static int Run(string work)
        {
            checks = 0;
            string local = Path.Combine(work, "local"), roaming = Path.Combine(work, "roaming");
            string desktop = Path.Combine(local, @"OpenAI\Codex\bin\new-install\codex.exe");
            string npm = Path.Combine(roaming, @"npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(desktop)); File.WriteAllText(desktop, "fixture");
            Directory.CreateDirectory(Path.GetDirectoryName(npm)); File.WriteAllText(npm, "fixture");
            var paths = DollarSpark.ExecutableCandidates("", local, roaming);
            Check(paths.Contains(desktop) && paths.Contains(npm), "desktop CLI absent without inherited PATH");
            Check(DollarSpark.ExecutableCandidates(Path.GetDirectoryName(desktop), local, roaming).Count(p => p == desktop) == 1, "duplicate CLI probes");
            var versions = new Dictionary<string, string> { { npm, "codex-cli 0.142.4\n" }, { desktop, "codex-cli 0.153.4\n" } };
            Func<string, CancellationToken, Task<string>> probe = (path, ct) => Task.FromResult(versions[path]);
            Check(DollarSpark.SelectExecutableAsync(new[] { npm, desktop }, probe, CancellationToken.None).Result == desktop, "old PATH CLI won over newer desktop");
            versions[npm] = "codex-cli 0.154.0";
            Check(DollarSpark.SelectExecutableAsync(paths, probe, CancellationToken.None).Result == npm, "newer npm CLI was ignored");
            versions[npm] = "unexpected version response";
            Check(DollarSpark.SelectExecutableAsync(paths, probe, CancellationToken.None).Result == desktop, "invalid CLI version selected");
            Check(DollarSpark.SelectExecutableAsync(new[] { npm }, probe, CancellationToken.None).Result == null, "unverified CLI used");
            using (var cancel = new CancellationTokenSource()) {
                cancel.Cancel(); bool stopped = false;
                try { DollarSpark.SelectExecutableAsync(paths, probe, cancel.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { stopped = true; }
                Check(stopped, "cancelled CLI discovery continued");
            }
            Check(DollarSpark.CliVersion("0.153.4") == null && DollarSpark.CliVersion("codex-cli 0.153.4") == new Version(0,153,4), "version identity not checked");
            foreach (var model in new[] { DollarSpark.Model, "gpt-6-astra" })
                Check(DollarSpark.ModelLabel(model).EndsWith(" · High") && DollarSpark.Arguments(work, model).Contains("model_reasoning_effort=high "), "effort label disagrees with actual request");

            if (Application.Current == null) Theme.Apply(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
            var cfg = new Config(Path.Combine(work, "side.json")) { Separated = true, ShowApps = false, ShowWeather = false, ShowClock = false, DockScale = 1.575 };
            cfg.Symbols.Clear();
            var yen = new SymbolDef(SourceKind.Fx, "FX_JPYKRW", "엔"); cfg.Symbols.Add(yen); cfg.Symbol = yen.Key;
            var window = new WidgetWindow(cfg, d => { });
            try {
                window.SizeToContent = SizeToContent.Manual; window.Width = 110; window.Height = 500; cfg.DockedEdge = DockEdge.Left;
                var quotes = (Dictionary<string, Quote>)Field(window, "_quotes");
                quotes[yen.Key] = new Quote { Ok = true, Price = "1,569.0 JPY", Ratio = "+0.19", RatioSuffix = "%", Dir = 1, ReceivedUtc = DateTime.UtcNow };
                Call(window, "BuildDockBar");
                var content = (StackPanel)Field(window, "_dockContent"); content.Orientation = Orientation.Vertical; content.Margin = new Thickness(0, 9, 0, 0); content.HorizontalAlignment = HorizontalAlignment.Stretch; content.VerticalAlignment = VerticalAlignment.Top;
                Call(window, "RefreshDockBar");
                window.Width = 78; Call(window, "RefreshDockBar");
                var items = (StackPanel)Field(window, "_dockItems");
                Check(items.Width <= 72, "same-height side resize kept stale text width");
                var bar = (Border)Field(window, "_dockBar");
                bar.Measure(new Size(78,500)); bar.Arrange(new Rect(0,0,78,500)); bar.UpdateLayout();
                var card = (Border)items.Children[0]; var inner = (StackPanel)card.Child;
                var nameRow = (Grid)inner.Children[0]; var name = (BriefTextBlock)nameRow.Children[0]; var price = (BriefTextBlock)inner.Children[1];
                var nameCenter = name.TranslatePoint(new Point(name.ActualWidth/2,0), bar);
                var priceCenter = price.TranslatePoint(new Point(price.ActualWidth/2,0), bar);
                Check(Math.Abs(nameCenter.X-priceCenter.X) <= 1, "side name and price center lines differ");
                var priceLeft = price.TranslatePoint(new Point(0,0), bar);
                Check(priceLeft.X >= 3 && priceLeft.X+price.ActualWidth <= 75, "price touches side boundary");
                Check(price.Lines(price.ActualWidth).Contains("JPY"), "long currency unit did not wrap at a word boundary");
                var bitmap = new RenderTargetBitmap(78, (int)Math.Ceiling(card.ActualHeight+18),96,96,PixelFormats.Pbgra32); bitmap.Render(bar);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(work,"side-width.png"))) encoder.Save(stream);
                Console.WriteLine("PREVIEW: " + Path.Combine(work,"side-width.png"));
            } finally { window.Close(); }

            SideCrowding(work);
            return checks;
        }

        /// <summary>
        /// ★ 다 켠 세로 바에서 시계가 화면 밖으로 밀려나 있었다 ★ (2026-09-10, 사용자 제보)
        ///   DockAppsRoom 이 세로일 때 0 을 돌려줘서 즐겨찾기 높이를 아예 예약하지 않았다.
        ///   시세가 그 자리까지 먹고 뒤엣것이 차례로 밀렸다 - 즐겨찾기 7개 중 5개만 보이고
        ///   시계는 아예 안 보였다. Visibility 는 내내 Visible 이었다.
        ///   그래서 '보이도록 설정됐나' 가 아니라 '바 안에 앉았나' 를 잰다.
        /// </summary>
        private static void SideCrowding(string work)
        {
            if (Application.Current == null) Theme.Apply(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
            var cfg = new Config(Path.Combine(work, "side-crowd.json"))
            { Separated = false, ShowApps = true, ShowWeather = true, ShowClock = true, DockScale = 1.5 };
            cfg.Symbols.Clear();
            for (int i = 0; i < 10; i++)
                cfg.Symbols.Add(new SymbolDef(SourceKind.Coin, "KRW-T" + i, "종목" + i));
            cfg.Symbol = cfg.Symbols[0].Key;
            var region = new SymbolDef(SourceKind.Weather, "1100", "서울"); region.Lat = 37.5665; region.Lon = 126.978;
            cfg.Weathers.Add(region);
            for (int i = 0; i < 7; i++) cfg.Apps.Add(new AppDef { File = "app" + i + ".lnk", Label = "앱" + i });

            var window = new WidgetWindow(cfg, d => { });
            try
            {
                window.SizeToContent = SizeToContent.Manual;
                window.Width = 110; window.Height = 700;
                cfg.DockedEdge = DockEdge.Left;
                var quotes = (Dictionary<string, Quote>)Field(window, "_quotes");
                foreach (var def in cfg.Symbols)
                    quotes[def.Key] = new Quote { Ok = true, Price = "1,234", Ratio = "+0.10", RatioSuffix = "%", Dir = 1, ReceivedUtc = DateTime.UtcNow };

                Call(window, "BuildDockBar");
                var content = (StackPanel)Field(window, "_dockContent");
                content.Orientation = Orientation.Vertical;
                content.HorizontalAlignment = HorizontalAlignment.Stretch;
                content.VerticalAlignment = VerticalAlignment.Top;
                Method(window, "OrderDockSections").Invoke(window, new object[] { true });

                // ★ 사용자가 정한 차례: 위에 즐겨찾기 → 시세, 바닥에 날씨 → 시계 ★
                var apps = (WrapPanel)Field(window, "_dockApps");
                var clip = (Border)Field(window, "_dockClip");
                var weather = (Border)Field(window, "_dockWeather");
                var clock = (TextBlock)Field(window, "_dockClock");
                var bottom = (StackPanel)Field(window, "_dockBottom");
                Check(content.Children.IndexOf(apps) < content.Children.IndexOf(clip), "side bar puts quotes above favourites");
                Check(content.Children.Contains(weather) == false && bottom.Children.Contains(weather),
                    "weather is not in the bottom group");
                Check(bottom.Children.IndexOf(weather) < bottom.Children.IndexOf(clock), "the clock sits above weather");
                Check(bottom.VerticalAlignment == VerticalAlignment.Bottom, "the bottom group is not anchored to the bottom");

                Call(window, "RefreshDockBar");
                var bar = (Border)Field(window, "_dockBar");
                bar.Measure(new Size(110, 700)); bar.Arrange(new Rect(0, 0, 110, 700)); bar.UpdateLayout();

                // 시계가 '보이기로 돼 있다' 가 아니라 바 안에 실제로 앉았는지 본다.
                Check(clock.Visibility == Visibility.Visible, "the side bar clock was collapsed");
                Check(clock.ActualHeight > 0 && !string.IsNullOrEmpty(clock.Text), "the side bar clock has no text");
                double clockBottom = clock.TranslatePoint(new Point(0, clock.ActualHeight), bar).Y;
                Check(clockBottom <= 700, "the side bar clock was pushed past the bottom of the bar: " + clockBottom);
                // ★ 바닥면 기준 ★ 시세 개수가 몇이든 시계는 바 아래쪽에 앉아야 한다.
                //   시세 밑에 따라붙으면 종목 수에 따라 자리가 매번 달라져 눈이 찾지 못한다.
                Check(clockBottom >= 600, "the clock floated up with the quotes instead of sticking to the bottom: " + clockBottom);
                double quotesBottom = clip.TranslatePoint(new Point(0, clip.ActualHeight), bar).Y;
                double weatherTop = weather.TranslatePoint(new Point(0, 0), bar).Y;
                Check(weatherTop > quotesBottom, "weather overlapped the quotes");

                // 즐겨찾기도 마지막 아이콘까지 들어와야 한다.
                Check(apps.Children.Count >= 7, "not every favourite was built: " + apps.Children.Count);
                double appsBottom = apps.TranslatePoint(new Point(0, apps.ActualHeight), bar).Y;
                Check(appsBottom <= 700, "the favourites ran past the bottom of the bar: " + appsBottom);

                // 자리를 내주느라 시세는 10개보다 적게 담긴다. 그게 사용자가 고른 방식이다.
                var items = (StackPanel)Field(window, "_dockItems");
                Check(items.Children.Count < 10, "quotes did not give way when the bar was full: " + items.Children.Count);
                Check(items.Children.Count > 0, "every quote was dropped from a 700px bar");
            }
            finally { window.Close(); }
        }

        private static System.Reflection.MethodInfo Method(object o, string name)
        { return o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); }
    }
}
