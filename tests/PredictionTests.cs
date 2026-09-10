using System;
using System.Collections.Generic;
using System.Globalization;
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
    internal static class PredictionTests
    {
        private static int count;
        private static void Check(bool value, string message) { if (!value) throw new Exception("Dollar analysis: " + message); count++; }
        private static T Field<T>(object obj, string name) { return (T)obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj); }
        private static PredictionTarget Target(SourceKind kind, string code, string label) { return new PredictionTarget(new SymbolDef(kind, code, label)); }
        private static DollarNews News(PredictionTarget target, string title) { return new DollarNews { Target = target, Title = title, Source = "BBC", PublishedUtc = DateTime.UtcNow, Url = "https://news.google.com/articles/test" }; }
        private static void Reject(Action run, string message) { bool rejected = false; try { run(); } catch (InvalidDataException) { rejected = true; } Check(rejected, message); }

        internal static int Run(string work, bool preview = false)
        {
            count = 0;
            if (Application.Current == null) Theme.Apply(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
            var dollar = Target(SourceKind.Fx, "FX_USDKRW", "달러");
            var tree = Target(SourceKind.WorldStock, "DLTR.O", "달러트리");
            var yen = Target(SourceKind.Fx, "FX_JPYKRW", "엔화 100");
            var en = Target(SourceKind.WorldStock, "4849.T", "엔");
            var btc = Target(SourceKind.Coin, "KRW-BTC", "비트코인");
            var index = Target(SourceKind.Index, "KOSPI", "코스피");
            var samsung = Target(SourceKind.DomesticStock, "005930", "삼성전자");
            var us = Target(SourceKind.Ecos, "INTL:US", "미국 정책금리");
            var kr = Target(SourceKind.Ecos, "INTL:KR", "한국 기준금리");
            Check(dollar.Dollar && !tree.Dollar && !yen.Dollar && !en.Dollar, "target identity uses label instead of kind/code");
            Check(Config.AppVersion == "1.047" && Config.IsNewer("1.048") && !Config.IsNewer("1.047") && !Config.IsNewer("1.020"), "thousandth version update not detected");
            Check(tree.Format(131.42, new Quote { Unit = "USD" }) == "$131.42", "world price formatted as won");
            Check(en.Format(1581, new Quote { Unit = "JPY" }).EndsWith("JPY") && Sources.WorldPrice("1,581.0", "JPY") == "1,581.0 JPY", "Japanese stock incorrectly marked dollars");
            Check(yen.Format(871.63, null) == "871.63원" && index.Format(2636.46, null).EndsWith("pt"), "yen 100 or index unit converted incorrectly");
            Check(btc.Steps(5) == 7 && btc.Steps(20) == 30 && tree.Steps(20) == 20, "coin month incorrectly uses 20 calendar days");
            Check(us.Change(4.5, 4.25, new Quote { Unit = "%" }) == "-0.25%p\n기준 대비", "policy change shown as relative return or won");
            Check(PredictionTarget.Number(new Quote { Ok = true, Price = "$131.42" }) == 131.42 && PredictionTarget.Number(new Quote { Ok = true, Price = "1,581.0 JPY" }) == 1581, "currency price parsing changed amount");
            var definition = new SymbolDef(SourceKind.WorldStock, "DLTR.O", "달러트리"); var copied = new PredictionTarget(definition); definition.Code = "4849.T";
            Check(copied.Key == "wstock:DLTR.O", "target mutated after opening");

            DateTime today = DollarAnalysis.KoreaDate(DateTime.UtcNow);
            string chart = "<protocol><chartdata symbol='005930'><item data='" + today.AddDays(-1).ToString("yyyyMMdd") + "|100|110|90|105|10'/></chartdata></protocol>";
            Check(PredictionData.ParseChart(chart, "005930", today).Single().Value == 105, "history did not use correct closing price");
            Check(PredictionData.ParseChart(chart, "KOSPI", today).Count == 0, "another symbol history accepted");
            Check(PredictionData.ParseChart("<!DOCTYPE x [<!ENTITY evil SYSTEM 'file:///bad'>]>" + chart, "005930", today).Count == 0, "history XML external entity accepted");
            var values = new List<DollarRate> { new DollarRate { Date = today.AddDays(-2), Value = 100 }, new DollarRate { Date = today.AddDays(-1), Value = 101 }, new DollarRate { Date = today, Value = 102 }, new DollarRate { Date = today.AddDays(1), Value = 103 } };
            Check(PredictionData.Validate(values, today).Count == 2, "unfinished or future bars entered history");
            values.Add(new DollarRate { Date = today.AddDays(-1), Value = 99 });
            Check(PredictionData.Validate(values, today).Count == 0, "conflicting historical closes accepted");
            values.RemoveAt(values.Count - 1); values[1].Value = 50;
            Check(PredictionData.Validate(values, today).Count == 0, "unadjusted split treated as prediction signal");
            string candle = "[{\"market\":\"KRW-DOGE\",\"trade_price\":150,\"candle_date_time_utc\":\"" + today.AddDays(-1).ToString("yyyy-MM-dd") + "T00:00:00\"}]";
            Check(PredictionData.ParsePrices(Json.Parse(candle), today, "KRW-BTC").Count == 0, "other coin history accepted");
            int pages = 0;
            var paged = PredictionData.HistoryAsync(yen, "HANA", DateTime.UtcNow, CancellationToken.None, (url, ct) => {
                int start = pages++ * 60;
                if (pages > 2) return Task.FromResult(Json.Parse("{\"result\":[]}"));
                return Task.FromResult(Json.Parse("{\"result\":[" + string.Join(",", Enumerable.Range(start, 60).Select(i => "{\"localTradedAt\":\"" + today.AddDays(-i).ToString("yyyy-MM-dd") + "\",\"closePrice\":870}")) + "]}"));
            }).GetAwaiter().GetResult();
            Check(pages == 3 && paged.Count == 119, "filtering current FX bar stopped history pagination");

            Check(DollarFactors.Analyze(News(tree, "연준 금리 인상 결정")).Any(f => f.Direction < 0), "Fed hike stock effect copied from dollar");
            Check(DollarFactors.Analyze(News(btc, "연준 금리 인상 결정")).Any(f => f.Direction < 0), "Fed hike coin effect copied from dollar");
            Check(DollarFactors.Analyze(News(us, "한국은행 금리 인상 결정")).Count == 0, "Korean hike assigned to US policy rate");
            Check(DollarFactors.Analyze(News(kr, "연준 금리 인하 결정")).Count == 0, "Fed cut assigned to Korean policy rate");
            Check(DollarFactors.Analyze(News(us, "연준 금리 인상 결정")).Any(f => f.Direction > 0), "own policy rate hike missed");
            Check(DollarFactors.Analyze(News(us, "연준 금리 안 올린다")).All(f => f.Direction == 0), "negated policy becomes hike");
            Check(DollarFactors.Analyze(News(yen, "일본은행 금리 인상 결정")).Any(f => f.Direction > 0), "BOJ hike not linked to yen target");
            Check(DollarFactors.Analyze(News(tree, "달러 환율 상승 소식")).Count == 0, "Dollar Tree confused with dollar FX");

            var source = new DollarAnalysisResult { Target = us, Quote = new Quote { Ok = true, Price = "4.50 %", Unit = "%" }, CheckedUtc = DateTime.UtcNow };
            source.News.Add(News(us, "Federal Reserve raises interest rates"));
            string input = DollarSpark.Input(source, source.News, DateTime.UtcNow);
            Check(Json.Parse(input)["target"]["key"].S == us.Key && Json.Parse(input)["target"]["unit"].S == "%", "AI target/unit absent");
            var schema = Json.Parse(DollarSpark.Schema(true));
            Check(schema["properties"]["target_key"]["type"].S == "string" && schema["properties"]["periods"]["items"]["properties"]["value_change"]["type"].Count == 2, "instrument schema invalid");
            string prompt = DollarSpark.BuildPrompt(source, source.News, DateTime.UtcNow);
            Check(prompt.Contains("해당 중앙은행") && !prompt.StartsWith("수집된 자료로 USD/KRW"), "generic analysis retains dollar-only instructions");
            string response = Response(us.Key, 0.25);
            source.Spark = DollarSpark.Parse(response, source, source.News, DateTime.UtcNow);
            source.News.Add(News(tree, "Dollar Tree shares rise after earnings beat"));
            Check(DollarSpark.SelectNews(source, DateTime.UtcNow).All(n => n.Target.Key == us.Key), "foreign target news sent to AI");
            source.News.RemoveAt(1);
            Check(source.Spark.TargetKey == us.Key && source.Spark.Periods[2].ValueChange == 0.25, "policy AI value missing");
            Reject(() => DollarSpark.Parse(response.Replace(us.Key, kr.Key), source, source.News, DateTime.UtcNow), "other target AI response accepted");
            Reject(() => DollarSpark.Parse(Response(us.Key, -0.25), source, source.News, DateTime.UtcNow), "price change contradicts displayed direction");
            Reject(() => DollarSpark.Parse(Response(us.Key, 6), source, source.News, DateTime.UtcNow), "extreme policy movement outside guard accepted");
            source.Spark.TargetKey = tree.Key;
            Check(!DollarAnalysis.Score(source, 1, DateTime.UtcNow).IsAi, "cross-target AI score reused");
            source.Spark = DollarSpark.Parse(response, source, source.News, DateTime.UtcNow);

            var cfg = new Config(Path.Combine(work, "prediction.json")); cfg.Load(); cfg.Bank = "HANA";
            var window = new DollarAnalysisWindow(cfg, ct => Task.FromResult(source), ct => Task.FromResult(true), ct => Task.FromResult(0), us);
            source.Extreme = true; source.Spark.Extreme = true;
            Field<System.Windows.Controls.Primitives.ToggleButton>(window, "_analysisMode").IsChecked = true;
            window.Render(source);
            Check(Field<TextBlock[]>(window, "_forecastValues")[0].Text == "약 4.75%" && Field<TextBlock[]>(window, "_forecastChanges")[0].Text.Contains("+0.25%p"), "policy forecast UI uses won or wrong baseline");
            var body = (FrameworkElement)window.Content;
            foreach (int width in new[] { 340, 440 })
            {
                window.Width = width; body.Measure(new Size(width, 820)); body.Arrange(new Rect(0, 0, width, 820)); body.UpdateLayout();
                var help = Field<Button>(window, "_sparkHelp"); var check = Field<TextBlock>(window, "_sparkLabel");
                Point h = help.TransformToAncestor(body).Transform(new Point()), c = check.TransformToAncestor(body).Transform(new Point());
                Check(help.Content.ToString() == "?" && help.ActualWidth == 21 && h.X > c.X + check.ActualWidth && Math.Abs(h.Y - c.Y) < 10, "help not compact beside Spark");
                help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Field<TextBlock>(window, "_loginHelp").Visibility == Visibility.Visible, "help button fails to open");
                help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!window.IsLoaded, "native window shown during tests");
            }
            if (preview) Save(body, work, "prediction-policy.png", 440, 820);
            window.Close();
            var weather = new PredictionTarget(new SymbolDef(SourceKind.Weather, "TEST", "검사용 지역") { Lat = 37.5, Lon = 127 });
            var forecast = new DollarAnalysisResult { Target = weather, CheckedUtc = DateTime.UtcNow, Quote = new Quote { Ok = true, Price = "-2.0°", Unit = "°C", Value = -2 } };
            forecast.DirectForecasts.Add(new PredictionPoint { Horizon = 1, Date = today.AddDays(1), Value = -3 });
            forecast.DirectForecasts.Add(new PredictionPoint { Horizon = 5, Date = today.AddDays(7), Value = 2 });
            var weatherWindow = new DollarAnalysisWindow(cfg, ct => Task.FromResult(forecast), null, null, weather); weatherWindow.Render(forecast);
            Check(Field<TextBlock[]>(weatherWindow, "_forecastValues")[0].Text.Contains("-3.00") && Field<TextBlock[]>(weatherWindow, "_forecastValues")[2].Text == "—", "temperature sign or unavailable month fabricated");
            Check(Field<StackPanel[]>(weatherWindow, "_evidenceLists")[0].Children.OfType<TextBlock>().Any(t => t.Text.Contains("Open-Meteo") && t.Text.Contains(today.AddDays(1).ToString("yyyy-MM-dd"))), "weather evidence count has no matching observation");
            weatherWindow.Close();

            // Simulate two independent fetches completing in the reverse order, without network or UI control.
            var payload = Sample(dollar); var old = payload.Quote;
            string openedPrediction = null;
            var main = new WidgetWindow(cfg, d => { openedPrediction = d.Key; });
            main.OpenQuotePrediction(dollar.Key);
            Check(openedPrediction == dollar.Key, "double click does not open prediction");
            Check(tree.SiteLink(null, "HANA").Contains("/worldstock/stock/DLTR.O/total"), "world stock destination wrong");
            Check(dollar.SiteLink(new Quote { Link = "javascript:alert(1)" }, "SHB").EndsWith("FX_USDKRW_SHB"), "unsafe destination or bank fallback wrong");
            Check(PredictionTarget.SiteName("https://naver.com.evil.example/") == "사이트 ↗", "spoofed site label");
            var fxWindow = new DollarAnalysisWindow(cfg, ct => Task.FromResult(payload));
            fxWindow.Render(payload);
            var latest = new Quote { Ok = true, Price = "1,340.60", Ratio = "-0.44", Dir = -1, Source = "예시 공유 시세", Time = "12:00" };
            Sources.PublishQuote(dollar.Def, "HANA", latest);
            Check(ReferenceEquals(Field<Dictionary<string, Quote>>(main, "_quotes")[dollar.Key], latest) && ReferenceEquals(payload.Quote, latest), "main and analysis quote not shared");
            Check(Field<TextBlock>(fxWindow, "_price").Text == "1,340.60원" && Field<TextBlock>(fxWindow, "_quoteRatio").Text == "▼ -0.44%" && Field<TextBlock>(fxWindow, "_quoteRatio").Foreground == Palette.Down, "analysis price or blue decline differs from main");
            var latePayload = Sample(dollar); latePayload.Quote = old; fxWindow.Render(latePayload);
            Check(ReferenceEquals(latePayload.Quote, latest), "late fetch overwrote newer shared quote");
            Check(ReferenceEquals(Sources.FetchPredictionQuoteAsync(dollar.Def, "HANA", CancellationToken.None).GetAwaiter().GetResult(), latest), "prediction refetched rather than using current main quote");
            Sources.PublishQuote(dollar.Def, "SHB", new Quote { Ok = true, Price = "999", Ratio = "99", Dir = 1 });
            Check(Field<TextBlock>(fxWindow, "_price").Text == "1,340.60원", "another bank quote leaked into analysis");
            Check(Field<TextBlock>(fxWindow, "_forecastBasis").Text.Contains("1,340.60원"), "forecast still anchored to ECB instead of shared quote");
            Sources.PublishQuote(dollar.Def, "HANA", new Quote { Ok = true, Price = "1,341.00", Ratio = "0.29", Dir = 1, Source = "예시 공유 시세", Time = "12:01" });
            Check(Field<TextBlock>(fxWindow, "_quoteRatio").Text == "▲ +0.29%" && Field<TextBlock>(fxWindow, "_quoteRatio").Foreground == Palette.Up, "rise not small/red/signed");
            fxWindow.Width = 440; body = (FrameworkElement)fxWindow.Content; body.Measure(new Size(440, 820)); body.Arrange(new Rect(0, 0, 440, 820)); body.UpdateLayout();
            if (preview) Save(body, work, "prediction-dollar.png", 440, 820);
            var priceText = Field<TextBlock>(fxWindow, "_price"); var ratioText = Field<TextBlock>(fxWindow, "_quoteRatio");
            var quoteRow = (Grid)priceText.Parent;
            Check(quoteRow.HorizontalAlignment == HorizontalAlignment.Left && quoteRow.ColumnDefinitions[0].Width.IsAuto && ratioText.FontSize == 14 && ratioText.Margin.Left == 10, "quote percentage not adjacent/enlarged");
            Sources.PublishQuote(dollar.Def, "HANA", new Quote { Ok = true, Price = "123", Ratio = "0.001", Dir = 1 });
            Check(ratioText.Text == "0.00%" && ratioText.Foreground == Palette.TextDim, "rounded zero shows false arrow");
            fxWindow.Render(Sample(tree));
            Check(Field<TextBlock>(fxWindow, "_price").Text == "—", "other item rendered inside dollar window");
            fxWindow.Close(); main.Close();

            string chosen = null;
            var targets = new[] { dollar, yen, index, tree, en, btc, samsung, us, kr };
            cfg.Symbols = targets.Select(t => t.Def).ToList(); cfg.Expanded = true; cfg.GridView = true;
            cfg.Scale = 1; cfg.ShowClock = cfg.ShowWeather = cfg.ShowApps = false;
            var widget = new WidgetWindow(cfg, d => chosen = d.Key);
            foreach (var target in targets)
            {
                var tile = typeof(WidgetWindow).GetMethod("BuildTile", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(widget, new object[] { target.Def });
                var root = Field<Border>(tile, "Root"); root.Measure(new Size(127, 97)); root.Arrange(new Rect(0, 0, 127, 97)); root.UpdateLayout();
                var button = Descendants(root).OfType<Button>().Single(b => (string)b.Content == "예측");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(chosen == target.Key && Math.Abs(button.ActualWidth - 19.6) <= 0.5 && Math.Abs(button.ActualHeight - 11.9) <= 0.5, "tile button target/size incorrect: " + target.Key);
                Check(Math.Abs(button.FontSize - 6.3) < 0.01 && System.Windows.Automation.AutomationProperties.GetName(button).Contains(target.Name), "small prediction button text or accessible name wrong");
                Check((string)button.Tag == target.Key, "prediction button identity missing");
                var row = typeof(WidgetWindow).GetMethod("BuildRow", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(widget, new object[] { target.Def });
                var rowRoot = Field<Border>(row, "Root"); rowRoot.Measure(new Size(252, 50)); rowRoot.Arrange(new Rect(0, 0, 252, 50)); rowRoot.UpdateLayout();
                var rowButton = Descendants(rowRoot).OfType<Button>().Single(); chosen = null;
                rowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(chosen == target.Key && Math.Abs(rowButton.ActualWidth - 19.6) <= 0.5, "list prediction click mapped to another item");
            }
            foreach (bool vertical in new[] { false, true })
            {
                var built = typeof(WidgetWindow).GetMethod("BuildDockQuote", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(widget, new object[] { tree.Def, new Quote { Ok = true, Price = "$131.42", Ratio = "0.29", Dir = 1 }, vertical });
                var root = (FrameworkElement)built.GetType().GetProperty("Item2").GetValue(built, null);
                root.Measure(new Size(vertical ? 90 : 600, 200)); root.Arrange(new Rect(new Point(), root.DesiredSize)); root.UpdateLayout();
                var button = Descendants(root).OfType<Button>().Single(); chosen = null; button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(chosen == tree.Key && button.Content.ToString() == "⋮" && button.ActualWidth <= 10.5 && button.ActualWidth >= 9.5,
                    "dock prediction button is not compact vertical dots");
                var dots = Descendants(button).OfType<System.Windows.Shapes.Ellipse>().ToArray();
                Check(dots.Length == 3 && dots.All(d => d.ActualWidth > 0 && d.ActualWidth <= 2), "dock dots clipped or absent");
                var centers = dots.Select(d => d.TransformToAncestor(button).Transform(new Point(d.ActualWidth / 2, d.ActualHeight / 2))).ToArray();
                Check(centers.All(p => Math.Abs(p.X - centers[0].X) < 0.1) && centers[0].Y < centers[1].Y && centers[1].Y < centers[2].Y, "dock dots not vertically aligned");
                if (preview) Save(root, work, vertical ? "prediction-dock-vertical.png" : "prediction-dock-horizontal.png", root.ActualWidth, root.ActualHeight);
            }
            if (preview)
            {
                foreach (var target in targets) Sources.PublishQuote(target.Def, cfg.Bank, Sample(target).Quote);
                body = (FrameworkElement)widget.Content; body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); body.Arrange(new Rect(new Point(), body.DesiredSize)); body.UpdateLayout();
                Save(body, work, "prediction-widget.png", body.ActualWidth, body.ActualHeight);
            }
            Check(!widget.IsLoaded, "widget preview showed a native window"); widget.Close();
            return count;
        }
        private static string Response(string key, double delta)
        {
            return "{\"target_key\":\"" + key + "\",\"style\":\"basic\",\"periods\":[" + string.Join(",", new[] { 1, 5, 20 }.Select(h =>
                "{\"horizon\":" + h + ",\"news_score\":20,\"history_score\":0,\"value_change\":" + delta.ToString(CultureInfo.InvariantCulture) + ",\"reason\":\"검사용 정책 시나리오\",\"counter\":\"물가 둔화 시 반대\",\"change\":\"회의 의결을 확인\",\"confidence\":\"low\",\"evidence\":[{\"article_id\":1,\"quote\":\"Federal Reserve raises interest rates\",\"role\":\"support\"}]}")) + "]}";
        }
        private static DollarAnalysisResult Sample(PredictionTarget target)
        {
            var result = new DollarAnalysisResult { Target = target, CheckedUtc = DateTime.UtcNow, Quote = new Quote { Ok = true, Price = target.Dollar ? "1,345.90" : target.Policy ? "4.50 %" : target.Def.Code == "4849.T" ? "1,581.0 JPY" : target.Def.Kind == SourceKind.WorldStock ? "$131.42" : "1,230.40", Unit = target.Policy ? "%" : target.Def.Code == "4849.T" ? "JPY" : target.Def.Kind == SourceKind.WorldStock ? "USD" : null, Ratio = target.Policy ? "26.07" : "0.29", RatioSuffix = target.Policy ? "" : "%", Dir = target.Policy ? 0 : 1, Source = "검사용 예시 자료", Time = "12:00" } };
            foreach (int h in new[] { 1, 5, 20 })
            {
                var p = new DollarPattern { Horizon = h, LatestDate = DollarAnalysis.KoreaDate(DateTime.UtcNow), Up = 20, Down = 10 };
                for (int i = 0; i < 30; i++) p.Returns.Add((i - 14) * 0.001 * Math.Sqrt(h));
                result.Patterns.Add(p);
            }
            result.Pattern = result.Patterns[0]; result.Rates.Add(new DollarRate { Date = result.Pattern.LatestDate.AddDays(-1), Value = 1347 }); result.Rates.Add(new DollarRate { Date = result.Pattern.LatestDate, Value = 1348 });
            return result;
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var node in Descendants(child)) yield return node; } }
        private static void Save(FrameworkElement root, string work, string name, double width, double height)
        {
            var bitmap = new RenderTargetBitmap((int)(width * 1.5), (int)(height * 1.5), 144, 144, PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); string path = Path.Combine(work, name);
            using (var stream = File.Create(path)) encoder.Save(stream); Console.WriteLine("PREVIEW: " + path);
        }
        internal static int Live(string root)
        {
            var cfg = new Config(Path.Combine(root, "config.json")); cfg.Load(); Sources.EcosKey = cfg.EcosKey;
            int checkedCount = 0;
            foreach (var def in cfg.Symbols)
            {
                var result = PredictionData.FetchAsync(new PredictionTarget(def), cfg.Bank, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine("LIVE " + def.Key + " quote=" + (result.Quote != null && result.Quote.Ok) + " unit=" + result.Target.Unit(result.Quote) + " history=" + result.Rates.Count + " periods=" + result.Patterns.Count + " news=" + result.News.Count + " directional=" + DollarAnalysis.Score(result, 1, DateTime.UtcNow).DirectionalCount);
                if (result.Quote == null || !result.Quote.Ok || result.Rates.Count == 0 || result.News.Count == 0 || (!result.Target.Economic && result.Patterns.Count != 3)) throw new Exception("Live prediction source incomplete: " + def.Key);
                checkedCount++;
            }
            return checkedCount;
        }
    }
}
