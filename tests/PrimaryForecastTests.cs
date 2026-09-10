using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DeskWidget
{
    internal static class PrimaryForecastTests
    {
        private static int checks;
        private static void Check(bool ok, string why) { if (!ok) throw new Exception("Primary forecast: " + why); checks++; }
        private static T Field<T>(DollarAnalysisWindow w, string name)
        { return (T)typeof(DollarAnalysisWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w); }
        private static DollarAnalysisResult Sample()
        {
            var now = DateTime.UtcNow;
            var r = new DollarAnalysisResult { Extreme = true, CheckedUtc = now, DomesticAvailable = true, GlobalAvailable = true,
                Quote = new Quote { Ok = true, Value = 1342, Price = "1,342.00", Ratio = "-0.33", RatioSuffix = "%", Source = "화면 검사용 가상 자료", Time = "12:00" },
                Spark = new DollarSparkResult { Extreme = true, CheckedUtc = now, SubmittedCount = 2 } };
            r.News.Add(new DollarNews { Title = "금리 전망 검사용 가상 기사", Source = "fixture", PublishedUtc = now, Url = "https://news.google.com/articles/primary-fixture" });
            r.Rates.Add(new DollarRate { Date = DollarAnalysis.KoreaDate(now).AddDays(-1), Value = 1342 });
            r.Rates.Add(new DollarRate { Date = DollarAnalysis.KoreaDate(now), Value = 1342 });
            foreach (int h in new[] { 1, 5, 20 }) {
                var p = new DollarPattern { Horizon = h, Up = 15, Down = 15, LatestDate = DollarAnalysis.KoreaDate(now) };
                for (int i = 0; i < 30; i++) p.Returns.Add(i < 15 ? -.01 : .01);
                r.Patterns.Add(p);
                var ai = new DollarSparkPeriod { Horizon = h, NewsScore = h == 1 ? 1 : h == 5 ? -60 : 70, Confidence = "high",
                    Reason = "가상 근거: 해당 기간의 수급과 정책 기대를 함께 판단합니다.",
                    Counter = "가상 반박: 반대 방향의 자금 이동이 나타날 수 있습니다.",
                    Change = "가상 전환: 반대 근거가 확인되면 판단을 다시 검토합니다." };
                ai.Citations.Add(new DollarSparkCitation { News = r.News[0], Quote = r.News[0].Title, Role = "mixed" });
                r.Spark.Periods.Add(ai);
            }
            r.Pattern = r.Patterns[0]; return r;
        }
        internal static int Run(string work, bool preview = false)
        {
            checks = 0;
            if (Application.Current == null) Theme.Apply(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
            var r = Sample(); var now = DateTime.UtcNow;
            var subtle = Sample();
            subtle.Spark.Periods[1].NewsScore = -20; subtle.Spark.Periods[2].NewsScore = 40;
            Check(DollarAnalysisWindow.ScenarioOutlook(subtle, 5, DateTime.UtcNow) == "보합" &&
                DollarAnalysisWindow.ScenarioOutlook(subtle, 20, DateTime.UtcNow) == "보합", "weekly/monthly flat bands replaced by daily band");
            var w = new DollarAnalysisWindow(new Config(System.IO.Path.Combine(work, "primary.json")), ct => Task.FromResult(r));
            try {
                var mode = Field<ToggleButton>(w, "_analysisMode");
                var primary = Field<StackPanel>(w, "_primaryHost"); var forecasts = Field<StackPanel>(w, "_forecastPanel");
                var comparison = Field<Expander>(w, "_comparison"); var comparisonBody = Field<StackPanel>(w, "_comparisonBody");
                // 2026-09-09: 사용자 요청으로 비교 기준을 기본 펼침으로 바꿨다.
                // 주전망 하나를 앞세우는 v1.011 의도는 배치(_primaryHost)가 지키고, 이 항은 열어 둔다.
                Check(mode.IsChecked != true && comparison.IsExpanded, "AI enabled automatically or comparison collapsed by default");
                w.Render(r.ForStyle(false));
                Check(primary.Children.Count == 0 && comparisonBody.Children.Contains(forecasts), "basic calculation promoted to main forecast");
                Check(Field<TextBlock>(w, "_primaryStatus").Text.Contains("주전망 대기"), "no AI presented as a completed forecast");
                if (preview) Save(w, work, "primary-waiting.png");
                mode.IsChecked = true; w.Render(r);
                Check(primary.Children.Contains(forecasts) && !comparisonBody.Children.Contains(forecasts),
                    "AI and basic exposed as two equal main forecasts");
                Check(Field<TextBlock>(w, "_primaryStatus").Text.Contains("AI 주전망 · Spark"), "main forecast model is unclear");
                Check(Field<BriefTextBlock>(w, "_comparisonSummary").Text.Contains("같은 입력 자료") && Field<BriefTextBlock>(w, "_comparisonSummary").Text.Contains("월간"),
                    "same-input basic comparison missing");
                var directions = Field<TextBlock[]>(w, "_periodDirections");
                Check(directions[0].Text == "보합" && directions[1].Text == "하락" && directions[2].Text == "상승", "forecast does not use period-specific flat thresholds");
                Check(Field<TextBlock[]>(w, "_forecastStates").Select(t => t.Text).SequenceEqual(directions.Select(t => t.Text)), "cards and headline disagree");
                Check(Field<TextBlock[]>(w, "_periodReasons")[0].Text.Contains("미세 상승 압력"), "small pressure concealed instead of distinguished from flat");
                Check(Field<TextBlock[]>(w, "_periodPoints")[0].Text == "+1.0점", "display-only classification changed AI score");
                var briefs = Field<BriefTextBlock[]>(w, "_periodChecks");
                Check(briefs.All(b => b.Visibility == Visibility.Visible && b.Text.Contains("반박:") && b.Text.Contains("전환:")), "counterargument or reversal hidden from main outlook");
                Check(briefs.All(b => b.ToolTip.ToString().Contains("반대 근거가 확인되면")), "full reversal lost by summary shortening");
                var chart = Field<Canvas>(w, "_chart");
                Check(chart.Children.OfType<Polyline>().Count() == 1 && chart.Children.OfType<Polyline>().Single().Points.Count == 4,
                    "basic history line shown as competing main forecast");
                var line = chart.Children.OfType<Polyline>().Single();
                var dots = chart.Children.OfType<Ellipse>().ToList();
                Check(dots.Count == line.Points.Count && dots.Select((d, i) => Math.Abs(Canvas.GetTop(d) + 2.5 - line.Points[i].Y) < .00001).All(v => v),
                    "primary chart dots follow past history instead of AI");
                Check(Field<TextBlock[]>(w, "_forecastValues").All(t => t.Text.StartsWith("약 ")), "scenario prices shown as precise target prices");
                if (preview) Save(w, work, "primary-ai.png");
                Check(!comparisonBody.Children.Contains(forecasts), "opening comparison duplicated AI forecast cards");

                // ── 글자 크기 (Alt+- / Alt++ / Alt+0) ───────────────────────────
                // 뿌리에 LayoutTransform 을 걸어 머리글·본문·꼬리가 함께 움직여야 한다.
                // 본문만 키우면 헤더와 어긋나고, RenderTransform 을 쓰면 글자가 흐려진다.
                // 키 조합이 ZoomText 까지 오는지부터 본다. 전에는 ZoomText 만 검사해서
                // 키가 실제로 배선됐는지는 아무도 안 봤고, 그래서 눌러도 안 먹었다.
                Check(DollarAnalysisWindow.ZoomStep(Key.OemMinus, ModifierKeys.Alt) < 0 &&
                    DollarAnalysisWindow.ZoomStep(Key.Subtract, ModifierKeys.Alt) < 0, "Alt+minus is not wired to zoom out");
                Check(DollarAnalysisWindow.ZoomStep(Key.OemPlus, ModifierKeys.Alt) > 0 &&
                    DollarAnalysisWindow.ZoomStep(Key.Add, ModifierKeys.Alt) > 0, "Alt+plus is not wired to zoom in");
                Check(DollarAnalysisWindow.ZoomStep(Key.D0, ModifierKeys.Alt) == 0 &&
                    DollarAnalysisWindow.ZoomStep(Key.NumPad0, ModifierKeys.Alt) == 0, "Alt+0 is not wired to reset");
                Check(double.IsNaN(DollarAnalysisWindow.ZoomStep(Key.OemMinus, ModifierKeys.None)) &&
                    double.IsNaN(DollarAnalysisWindow.ZoomStep(Key.A, ModifierKeys.Alt)), "plain keys move the text size");
                var zoom = Field<ScaleTransform>(w, "_textScale");
                Check(zoom != null, "prediction window has no text scale");
                double at1 = zoom.ScaleX;
                w.ZoomText(0.1);
                Check(zoom.ScaleX > at1 && Math.Abs(zoom.ScaleX - zoom.ScaleY) < 1e-9, "Alt+plus did not enlarge text");
                w.ZoomText(-0.1);
                Check(Math.Abs(zoom.ScaleX - at1) < 1e-9, "Alt+minus did not return to the previous size");
                // 범위를 벗어나지 않는다. 계속 눌러도 0.8~1.8 안에 머문다.
                for (int i = 0; i < 30; i++) w.ZoomText(0.1);
                Check(zoom.ScaleX <= Config.MaxScale + 1e-9, "text scale ran past the maximum");
                for (int i = 0; i < 60; i++) w.ZoomText(-0.1);
                Check(zoom.ScaleX >= Config.MinScale - 1e-9, "text scale ran past the minimum");
                // Alt+0 은 기본으로 되돌린다.
                w.ZoomText(0);
                Check(Math.Abs(zoom.ScaleX - 1.0) < 1e-9, "Alt+0 did not reset the text size");
                // 다음에 여는 창이 같은 크기로 뜨도록 설정에 남는다.
                w.ZoomText(0.1);
                Check(Math.Abs(Field<Config>(w, "_cfg").AnalysisScale - zoom.ScaleX) < 1e-9, "text size not kept for the next window");
                Check(Config.ScaleOrDefault(double.NaN) == 1.0 && Config.ScaleOrDefault(99) == Config.MaxScale &&
                    Config.ScaleOrDefault(0.1) == Config.MinScale, "damaged scale value not clamped");
                // ★ 한 칸 움직일 기준은 이 창의 지금 크기다 ★ (2026-09-10 감사 #35)
                //   품목마다 창이 따로 열리고 다른 창은 배율을 따라가지 않는다. 그런데 다음 값을
                //   설정 파일에서 구하면, 다른 창에서 키운 만큼 이 창이 갑자기 건너뛴다.
                w.ZoomText(0);
                var second = new DollarAnalysisWindow(Field<Config>(w, "_cfg"), ct => Task.FromResult(r));
                try
                {
                    var otherZoom = Field<ScaleTransform>(second, "_textScale");
                    for (int i = 0; i < 3; i++) second.ZoomText(0.1);      // 다른 창만 1.3 으로
                    Check(Math.Abs(zoom.ScaleX - 1.0) < 1e-9, "one window's zoom moved another window");
                    w.ZoomText(0.1);
                    Check(Math.Abs(zoom.ScaleX - 1.1) < 1e-9,
                        "zoom stepped from the config file instead of this window: " + zoom.ScaleX);
                    Check(otherZoom.ScaleX > zoom.ScaleX, "the other window followed this one back down");
                }
                finally { second.Close(); }
                w.ZoomText(0);

                if (preview) Save(w, work, "primary-comparison.png");
                var failed = r.ForStyle(true); failed.SparkStatus = "Spark 응답 오류 · 검증 실패";
                w.Render(failed);
                Check(primary.Children.Count == 0 && comparisonBody.Children.Contains(forecasts) && Field<TextBlock>(w, "_primaryStatus").Text.Contains("생성 실패"),
                    "AI failure silently replaced by basic main forecast");
                Check(briefs.All(b => b.Visibility == Visibility.Collapsed && b.Text == ""), "failed AI retains stale counterarguments");
                if (preview) { comparison.IsExpanded = false; Save(w, work, "primary-failed.png"); }
                w.Render(r);
                var wrong = Sample(); wrong.Spark.TargetKey = "coin:KRW-DOGE";
                w.Render(wrong);
                Check(primary.Children.Count == 0 && Field<TextBlock>(w, "_primaryStatus").Text.Contains("생성 실패") && Field<bool>(w, "_sparkAutoPaused"),
                    "wrong-instrument AI is accepted as primary or failure concealed");
                w.Render(r); mode.IsChecked = false;
                Check(primary.Children.Count == 0 && Field<Border>(w, "_sparkCard").Visibility == Visibility.Collapsed,
                    "reference-only mode keeps an AI main outlook or controls");
                Check(!w.IsLoaded, "test launched a user window");
                return checks;
            } finally { w.Close(); }
        }
        private static void Save(DollarAnalysisWindow window, string work, string name)
        {
            var root = (FrameworkElement)window.Content; window.Width = 440;
            root.Measure(new Size(440, 900)); root.Arrange(new Rect(0, 0, 440, 900)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(660, 1350, 144, 144, PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = System.IO.Path.Combine(work, name);
            using (var stream = File.Create(path)) encoder.Save(stream);
            Console.WriteLine("PREVIEW: " + path);
        }
    }
}
