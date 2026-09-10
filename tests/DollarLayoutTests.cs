using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;

namespace DeskWidget
{
    internal static class DollarLayoutTests
    {
        private static int count;
        private static void Check(bool ok, string reason) { if (!ok) throw new Exception("Dollar analysis: " + reason); count++; }
        private static T Field<T>(DollarAnalysisWindow window, string name)
        { return (T)typeof(DollarAnalysisWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); }

        internal static int Run(string work, bool preview = false)
        {
            count = 0;
            if (Application.Current == null) Theme.Apply(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
            var tiny = new DollarScore { UpEvidence = 2.3, Reliability = 1, DirectionalCount = 1 };
            Check(DollarAnalysisWindow.Outlook(tiny) == "상승 쪽" && DollarAnalysisWindow.DisplayPoints(tiny.Value) == "+2.3점", "small positive pressure concealed as mixed");
            tiny.UpEvidence = 0; tiny.DownEvidence = 1.2;
            Check(DollarAnalysisWindow.Outlook(tiny) == "하락 쪽" && DollarAnalysisWindow.DisplayPoints(tiny.Value) == "-1.2점", "small negative pressure concealed as mixed");
            tiny.DownEvidence = 0;
            Check(DollarAnalysisWindow.Outlook(tiny) == "혼조" && DollarAnalysisWindow.DisplayPoints(-0.001) == "0.0점", "zero pressure given a direction");
            Check(DollarAnalysisWindow.PriceChange(1000, 1002) == "+2.0원\n+0.20%" &&
                DollarAnalysisWindow.PriceChange(1000, 998) == "-2.0원\n-0.20%", "forecast delta baseline or sign wrong");
            var upwardHistory = new DollarPattern { Returns = new System.Collections.Generic.List<double> { 0.01, 0.02, 0.03 } };
            tiny.DownEvidence = 1.2;
            Check(DollarAnalysis.ForecastReturn(upwardHistory, tiny) < 0, "rule outlook and future graph disagree");
            tiny.DownEvidence = 0.001;
            Check(DollarAnalysis.ForecastReturn(upwardHistory, tiny) == 0 && DollarAnalysisWindow.PriceChange(1000, 999.999) == "0.0원\n0.00%", "zero pressure or rounding invents a drift");

            var result = Sample();
            var window = new DollarAnalysisWindow(new Config(Path.Combine(work, "layout.json")), ct => Task.FromResult(result), ct => Task.FromResult(true), ct => Task.FromResult(0));
            result.Extreme = true; result.Spark.Extreme = true;
            Field<ToggleButton>(window, "_analysisMode").IsChecked = true;
            window.Render(result);
            var root = (FrameworkElement)window.Content;
            var directions = Field<TextBlock[]>(window, "_periodDirections");
            var headings = Field<TextBlock[]>(window, "_periodHeadings");
            var counts = Field<TextBlock[]>(window, "_evidenceCounts");
            var points = Field<TextBlock[]>(window, "_periodPoints");
            var reasons = Field<TextBlock[]>(window, "_periodReasons");
            var expanders = Field<Expander[]>(window, "_periodEvidence");
            var mode = Field<ToggleButton>(window, "_analysisMode");
            var refresh = Field<Button>(window, "_refresh");
            foreach (double width in new[] { 340.0, 440.0, 720.0 })
            {
                window.Width = width; root.Measure(new Size(width, 820)); root.Arrange(new Rect(0, 0, width, 820)); root.UpdateLayout();
                Check(!window.IsLoaded, "layout test showed a native window");
                foreach (var column in new[] { headings, directions, counts, points })
                {
                    double first = column[0].TransformToAncestor(root).Transform(new Point()).X;
                    Check(column.All(t => Math.Abs(t.TransformToAncestor(root).Transform(new Point()).X - first) < 0.6), "forecast columns misaligned");
                    Check(column.All(t => Fits(t)), "forecast header clipped at width " + width);
                }
                Check(expanders.Select(e => ((FrameworkElement)e.Header).ActualHeight).Distinct().Count() == 1, "forecast rows have unequal heights");
                Check(mode.TransformToAncestor(root).Transform(new Point()).X + mode.ActualWidth <= refresh.TransformToAncestor(root).Transform(new Point()).X,
                    "style toggle is not beside refresh");
                Check(reasons.All(r => !string.IsNullOrEmpty(r.Text)) && points[0].Text.StartsWith("+"), "visible reason or signed points missing");
                var chart = Field<Canvas>(window, "_chart");
                Check(chart.Children.OfType<System.Windows.Shapes.Polyline>().SelectMany(line => line.Points).All(p => p.X <= chart.ActualWidth), "future graph clipped after resize");
                if (preview && width != 720) Save(root, work, "layout-" + (int)width + ".png", width, 820);
            }
            var header = (ToggleButton)expanders[0].Template.FindName("HeaderSite", expanders[0]);
            header.IsChecked = true; root.UpdateLayout();
            Check(expanders[0].IsExpanded && ((FrameworkElement)expanders[0].Template.FindName("EvidenceBody", expanders[0])).Visibility == Visibility.Visible,
                "custom disclosure does not open evidence");
            header.IsChecked = false; root.UpdateLayout();
            Check(!expanders[0].IsExpanded, "custom disclosure does not close evidence");
            mode.IsChecked = false;
            Check(counts.All(c => c.Text.Contains("0건")) && Field<TextBlock>(window, "_status").Text.Contains("참고 선택"), "uncomputed basic style reused extreme display");
            window.Render(Sample().ForStyle(false)); string basicPoints = points[0].Text;
            mode.IsChecked = true;
            Check(points[0].Text == "+2.3점", "extreme cache lost when entering basic");
            var extreme = Sample(); extreme.Extreme = true; extreme.Spark.Extreme = true; extreme.Spark.Periods[0].NewsScore = 22;
            window.Render(extreme);
            mode.IsChecked = false;
            Check(points[0].Text == basicPoints, "basic cached result overwritten by extreme");
            mode.IsChecked = true;
            Check(points[0].Text == "+22.0점", "extreme cached result overwritten by basic");
            var copy = extreme.ForStyle(false);
            Check(!copy.Extreme && copy.Spark == null && extreme.Extreme && extreme.Spark != null, "style snapshot mutated original analysis");
            window.Render(null);
            Check(Field<TextBlock[]>(window, "_forecastChanges").All(c => c.Text == "—\n—") && points.All(p => p.Text == "—점"), "failed refresh retained delta or score");
            window.Close();
            LoginChecks(work, preview);
            SparkScheduleChecks(work);
            SparkModeChecks(work, preview);
            return count;
        }

        private static bool Fits(TextBlock text)
        {
            var measure = new FormattedText(text.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, text.Foreground, 1.0);
            return measure.WidthIncludingTrailingWhitespace <= text.ActualWidth + 1;
        }

        private static void LoginChecks(string work, bool preview)
        {
            int starts = 0, probes = 0, fetches = 0, analyses = 0; bool loggedIn = false;
            var login = new TaskCompletionSource<int>(); CancellationToken observed = CancellationToken.None;
            var window = new DollarAnalysisWindow(new Config(System.IO.Path.Combine(work, "login.json")), ct => { fetches++; return Task.FromResult(Sample()); },
                ct => { probes++; return Task.FromResult(loggedIn); }, ct => { starts++; observed = ct; return login.Task; }, null,
                (result, ct) => { analyses++; return Task.FromResult(SparkFor(result)); });
            window.RefreshAsync().GetAwaiter().GetResult();
            Check(fetches == 1 && analyses == 0, "Spark started before user login");
            window.EnsureSparkLoginAsync().GetAwaiter().GetResult();
            Check(probes == 0 && Field<Border>(window, "_sparkCard").Visibility == Visibility.Collapsed, "basic mode shows or authenticates Spark");
            Field<ToggleButton>(window, "_analysisMode").IsChecked = true;
            Field<Button>(window, "_sparkLogin").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.EnsureSparkLoginAsync().GetAwaiter().GetResult();
            Check(starts == 1 && !Field<Button>(window, "_refresh").IsEnabled, "login button did not start login or duplicated login");
            Check(!Field<bool>(window, "_sparkConnected") && Field<Ellipse>(window, "_sparkDot").Fill != Palette.Online, "pending login falsely shows green");
            loggedIn = true; login.SetResult(0);
            Check(Field<TextBlock>(window, "_sparkState").Text.Contains("로그인됨") && Field<Button>(window, "_refresh").IsEnabled, "login completion not reflected");
            Check(fetches == 2 && analyses == 1, "first login did not immediately analyze after recent quote refresh");
            Check(Field<TextBlock[]>(window, "_periodPoints")[0].Text == "+2.3점", "automatic analysis result was not rendered");
            Check(Field<Ellipse>(window, "_sparkDot").Fill == Palette.Online && Field<bool>(window, "_sparkConnected"), "successful login lacks green indicator");
            window.EnsureSparkLoginAsync().GetAwaiter().GetResult();
            Check(starts == 1 && probes == 3 && analyses == 1, "already connected login duplicated authentication or analysis");
            var body = (FrameworkElement)window.Content;
            foreach (double width in new[] { 340.0, 440.0 })
            {
                window.Width = width; body.Measure(new Size(width, 820)); body.Arrange(new Rect(0, 0, width, 820)); body.UpdateLayout();
                var help = Field<Button>(window, "_sparkHelp"); var timer = Field<Button>(window, "_sparkCountdown"); var button = Field<Button>(window, "_sparkLogin");
                Check(help.TransformToAncestor(body).Transform(new Point()).X + help.ActualWidth <= timer.TransformToAncestor(body).Transform(new Point()).X &&
                    timer.TransformToAncestor(body).Transform(new Point()).X + timer.ActualWidth <= button.TransformToAncestor(body).Transform(new Point()).X,
                    "Spark help, countdown and login overlap at " + width);
                Check(!window.IsLoaded, "login test showed a native window");
                if (preview) Save(body, work, "spark-connected-" + (int)width + ".png", width, 820);
            }
            window.ChangeModel("gpt-6-astra");
            window.Width = 340; body.Measure(new Size(340,820)); body.Arrange(new Rect(0,0,340,820)); body.UpdateLayout();
            var modelLabel = Field<TextBlock>(window, "_sparkLabel");
            Check(modelLabel.Text == "Astra · High ▾", "Astra effort not displayed after swap");
            var astraHelp = Field<Button>(window, "_sparkHelp"); var astraTimer = Field<Button>(window, "_sparkCountdown");
            Check(astraHelp.TranslatePoint(new Point(astraHelp.ActualWidth,0), body).X <= astraTimer.TranslatePoint(new Point(),body).X,
                "Astra effort label overlaps timer");
            if (preview) Save(body, work, "astra-high-340.png", 340, 820);
            window.ChangeModel(DollarSpark.Model);
            Check(modelLabel.Text == "Spark · High ▾", "Spark effort lost after swap");
            Field<Button>(window, "_sparkLogin").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!Field<bool>(window, "_sparkConnected") && !Field<DispatcherTimer>(window, "_sparkTimer").IsEnabled && loggedIn, "disconnect kept automatic analysis or logged out global CLI");
            window.Close();

            var late = new TaskCompletionSource<int>(); starts = 0; probes = 0;
            var closing = new DollarAnalysisWindow(new Config(Path.Combine(work, "login-close.json")), ct => Task.FromResult<DollarAnalysisResult>(null),
                ct => { probes++; return Task.FromResult(false); }, ct => { starts++; observed = ct; return late.Task; });
            Field<ToggleButton>(closing, "_analysisMode").IsChecked = true;
            var active = closing.EnsureSparkLoginAsync();
            closing.Close(); late.SetResult(0); active.GetAwaiter().GetResult();
            Check(observed.IsCancellationRequested && probes == 1 && !Field<TextBlock>(closing, "_sparkState").Text.Contains("로그인됨"), "closed window completed login UI or kept probing");

            var failed = new DollarAnalysisWindow(new Config(Path.Combine(work, "login-fail.json")), ct => Task.FromResult<DollarAnalysisResult>(null),
                ct => Task.FromResult(false), ct => { throw new InvalidOperationException("로그인 실패"); });
            Field<ToggleButton>(failed, "_analysisMode").IsChecked = true;
            failed.EnsureSparkLoginAsync().GetAwaiter().GetResult();
            Check(!Field<bool>(failed, "_sparkConnected") && Field<TextBlock>(failed, "_sparkState").Text.Contains("실패") &&
                Field<Ellipse>(failed, "_sparkDot").Fill != Palette.Online && !Field<DispatcherTimer>(failed, "_sparkTimer").IsEnabled, "failed login left Spark enabled");
            failed.Close();
        }

        private static void SparkScheduleChecks(string work)
        {
            DateTime now = DateTime.UtcNow; int fetches = 0, analyses = 0, browser = 0; bool authorized = true;
            DollarAnalysisResult activeSource = null;
            TaskCompletionSource<DollarSparkResult> pending = null; CancellationToken observed = CancellationToken.None;
            var cfg = new Config(Path.Combine(work, "spark-period.json"));
            var window = new DollarAnalysisWindow(cfg, ct => { fetches++; return Task.FromResult(Sample()); },
                ct => Task.FromResult(authorized), ct => { browser++; return Task.FromResult(0); }, null,
                (result, ct) => { analyses++; observed = ct; activeSource = result; return pending == null ? Task.FromResult(SparkFor(result)) : pending.Task; }, () => now);
            Check(cfg.SparkRefreshIntervalSec == 0, "Spark interval default changed");
            window.SetSparkInterval(60); window.SparkTimerTickAsync().GetAwaiter().GetResult();
            Check(fetches == 0 && !Field<DispatcherTimer>(window, "_sparkTimer").IsEnabled, "timer ran without login");
            var loaded = new Config(cfg.Path); loaded.Load();
            Check(loaded.SparkRefreshIntervalSec == 60, "Spark interval not saved");
            Field<ToggleButton>(window, "_analysisMode").IsChecked = true;
            window.EnsureSparkLoginAsync().GetAwaiter().GetResult();
            Check(browser == 0 && analyses == 1 && fetches == 1, "existing CLI login failed to start first analysis automatically");
            Check(Field<Button>(window, "_sparkCountdown").Content.ToString() == "60초", "countdown not scheduled from completion");
            // ★ 화면은 주입한 시계를 쓴다 ★ (2026-09-10 감사 #36)
            //   전에는 문구를 만드는 자리마다 실제 시계를 읽어서, 검사가 시계를 옮겨도 절반은
            //   진짜 시계를 봤다. 시계를 25시간 앞으로 옮기면 하루가 지난 AI 응답은 반려돼야
            //   한다 - 실제 시계를 보고 있으면 그대로 통과한다.
            //   위 창의 갱신 예약을 건드리지 않으려고 별도의 창으로 본다.
            DateTime aheadOfTheNews = DateTime.UtcNow.AddHours(25);
            var clockWindow = new DollarAnalysisWindow(new Config(Path.Combine(work, "clock.json")),
                ct => Task.FromResult(Sample()), ct => Task.FromResult(true), ct => Task.FromResult(0), null,
                (result, ct) => Task.FromResult(SparkFor(result)), () => aheadOfTheNews);
            try
            {
                Field<ToggleButton>(clockWindow, "_analysisMode").IsChecked = true;
                var stale = Sample(); stale.Extreme = true;
                stale.Spark.Extreme = true; stale.Spark.TargetKey = stale.Target.Key;
                clockWindow.Render(stale);
                Check(Field<TextBlock>(clockWindow, "_status").Text.Contains("규칙 참고"),
                    "a day-old AI answer was accepted - the window is reading the real clock, not the injected one");
            }
            finally { clockWindow.Close(); }
            now = now.AddSeconds(59); window.SparkTimerTickAsync().GetAwaiter().GetResult();
            Check(analyses == 1 && Field<Button>(window, "_sparkCountdown").Content.ToString() == "1초", "timer fired early or wrong remaining seconds");
            now = now.AddSeconds(1); pending = new TaskCompletionSource<DollarSparkResult>(); var active = window.SparkTimerTickAsync();
            Check(analyses == 2 && !active.IsCompleted, "due timer did not start analysis");
            now = now.AddSeconds(100); window.SparkTimerTickAsync().GetAwaiter().GetResult(); window.RefreshAsync().GetAwaiter().GetResult();
            Check(analyses == 2 && fetches == 2, "timer or manual refresh overlapped running analysis");
            pending.SetResult(SparkFor(activeSource)); active.GetAwaiter().GetResult(); pending = null;
            Check(Field<Button>(window, "_sparkCountdown").Content.ToString() == "60초", "long analysis caused catch-up loop");
            now = now.AddSeconds(31); window.RefreshAsync().GetAwaiter().GetResult();
            Check(analyses == 3 && Field<Button>(window, "_sparkCountdown").Content.ToString() == "60초", "manual refresh did not reset countdown");
            window.SetSparkInterval(0); now = now.AddHours(7); window.SparkTimerTickAsync().GetAwaiter().GetResult();
            Check(analyses == 3 && !Field<DispatcherTimer>(window, "_sparkTimer").IsEnabled && Field<Button>(window, "_sparkCountdown").Content.ToString() == "수동", "manual setting kept automatic refresh");
            loaded.Load(); Check(loaded.SparkRefreshIntervalSec == 0, "manual interval not persisted");
            window.SetSparkInterval(60); authorized = false; now = now.AddSeconds(60); window.SparkTimerTickAsync().GetAwaiter().GetResult();
            Check(analyses == 3 && !Field<bool>(window, "_sparkConnected") && Field<Ellipse>(window, "_sparkDot").Fill != Palette.Online &&
                !Field<DispatcherTimer>(window, "_sparkTimer").IsEnabled, "expired login kept green or started AI");
            authorized = true; window.EnsureSparkLoginAsync().GetAwaiter().GetResult();
            Check(analyses == 4, "reconnect did not immediately analyze");
            pending = new TaskCompletionSource<DollarSparkResult>(); now = now.AddSeconds(60); active = window.SparkTimerTickAsync();
            string beforeClose = Field<TextBlock>(window, "_sparkState").Text;
            window.Close(); pending.SetResult(Sample().Spark); active.GetAwaiter().GetResult();
            now = now.AddHours(7); window.SparkTimerTickAsync().GetAwaiter().GetResult();
            Check(observed.IsCancellationRequested && analyses == 5 && !Field<DispatcherTimer>(window, "_sparkTimer").IsEnabled &&
                Field<TextBlock>(window, "_sparkState").Text == beforeClose, "closed window continued automatic analysis or late UI update");

            foreach (string setting in new[] { "", ",\"sparkRefreshIntervalSec\":-1", ",\"sparkRefreshIntervalSec\":1", ",\"sparkRefreshIntervalSec\":21601", ",\"sparkRefreshIntervalSec\":null" })
            {
                File.WriteAllText(cfg.Path, "{\"version\":\"" + Config.AppVersion + "\"" + setting + "}", new System.Text.UTF8Encoding(false));
                loaded.Load(); Check(loaded.SparkRefreshIntervalSec == 300, "invalid interval can flood AI calls: " + setting);
            }
        }

        private static void SparkModeChecks(string work, bool preview)
        {
            DateTime now = DateTime.UtcNow; int analyses = 0, probes = 0; bool fail = false;
            CancellationToken observed = CancellationToken.None; DollarAnalysisResult activeSource = null;
            TaskCompletionSource<DollarSparkResult> pending = null;
            var window = new DollarAnalysisWindow(new Config(Path.Combine(work, "spark-mode.json")), ct => Task.FromResult(Sample()),
                ct => { probes++; return Task.FromResult(true); }, ct => Task.FromResult(0), null,
                (source, ct) => { analyses++; observed = ct; activeSource = source;
                    if (fail) throw new InvalidDataException("원문과 다른 인용");
                    return pending == null ? Task.FromResult(SparkFor(source)) : pending.Task; }, () => now);
            var mode = Field<ToggleButton>(window, "_analysisMode");
            window.RefreshAsync().GetAwaiter().GetResult(); window.EnsureSparkLoginAsync().GetAwaiter().GetResult();
            Check(analyses == 0 && probes == 0 && Field<Border>(window, "_sparkCard").Visibility == Visibility.Collapsed, "basic mode called or displayed Spark");
            mode.IsChecked = true; window.EnsureSparkLoginAsync().GetAwaiter().GetResult();
            Check(analyses == 1 && Field<Border>(window, "_sparkCard").Visibility == Visibility.Visible, "extreme mode failed to expose and call Spark");
            mode.IsChecked = false; int before = probes; now = now.AddMinutes(10);
            window.SparkTimerTickAsync().GetAwaiter().GetResult(); window.RefreshAsync().GetAwaiter().GetResult();
            Check(analyses == 1 && probes == before && !Field<DispatcherTimer>(window, "_sparkTimer").IsEnabled &&
                Field<Border>(window, "_sparkCard").Visibility == Visibility.Collapsed && !Field<TextBlock>(window, "_status").Text.Contains("Spark"), "basic mode kept Spark timer or manual AI call");
            var basic = Sample(); window.Render(basic);
            Check(Field<DollarAnalysisResult>(window, "_lastRendered").Spark == null && basic.Spark != null, "basic displayed AI cache or mutated source");
            if (preview)
            {
                var body = (FrameworkElement)window.Content; window.Width = 440;
                body.Measure(new Size(440, 820)); body.Arrange(new Rect(0, 0, 440, 820)); body.UpdateLayout();
                Save(body, work, "spark-hidden-basic.png", 440, 820);
            }
            mode.IsChecked = true; now = now.AddSeconds(31); pending = new TaskCompletionSource<DollarSparkResult>();
            var active = window.RefreshAsync();
            Check(analyses == 2 && mode.IsEnabled, "style switch blocked during Spark request");
            mode.IsChecked = false; string selected = Field<TextBlock[]>(window, "_periodPoints")[0].Text;
            Check(observed.IsCancellationRequested && !Field<DispatcherTimer>(window, "_sparkTimer").IsEnabled, "entering basic did not cancel active Spark");
            pending.SetResult(SparkFor(activeSource)); active.GetAwaiter().GetResult(); pending = null;
            Check(Field<TextBlock[]>(window, "_periodPoints")[0].Text == selected && !Field<bool>(window, "_sparkAutoPaused"), "cancelled extreme result overwrote basic or counted as failure");
            mode.IsChecked = true; now = now.AddSeconds(31); fail = true; window.RefreshAsync().GetAwaiter().GetResult();
            Check(analyses == 3 && Field<TextBlock>(window, "_sparkState").Text.Contains("원문과 다른 인용") &&
                Field<Button>(window, "_sparkCountdown").Content.ToString() == "중지", "validation reason missing or failure timer not paused");
            window.SetSparkInterval(60); now = now.AddHours(2); window.SparkTimerTickAsync().GetAwaiter().GetResult();
            Check(analyses == 3 && !Field<DispatcherTimer>(window, "_sparkTimer").IsEnabled, "failed analysis kept consuming Spark automatically");
            fail = false; window.RefreshAsync().GetAwaiter().GetResult();
            Check(analyses == 4 && !Field<bool>(window, "_sparkAutoPaused") && Field<DispatcherTimer>(window, "_sparkTimer").IsEnabled, "manual successful retry did not resume timer");
            window.Close();

            var login = new TaskCompletionSource<int>(); CancellationToken loginToken = CancellationToken.None; int loginProbes = 0;
            var logging = new DollarAnalysisWindow(new Config(Path.Combine(work, "spark-mode-login.json")), ct => Task.FromResult(Sample()),
                ct => { loginProbes++; return Task.FromResult(false); }, ct => { loginToken = ct; return login.Task; });
            Field<ToggleButton>(logging, "_analysisMode").IsChecked = true; active = logging.EnsureSparkLoginAsync();
            Field<ToggleButton>(logging, "_analysisMode").IsChecked = false; login.SetResult(0); active.GetAwaiter().GetResult();
            Check(loginToken.IsCancellationRequested && loginProbes == 1 && !Field<bool>(logging, "_sparkConnected"), "basic switch failed to cancel pending login");
            logging.Close();
        }

        private static DollarSparkResult SparkFor(DollarAnalysisResult result)
        {
            var spark = Sample().Spark; spark.Extreme = result.Extreme; spark.TargetKey = result.Target.Key;
            foreach (var period in spark.Periods)
            {
                period.Citations.Clear();
                foreach (var news in result.News.Take(3)) period.Citations.Add(new DollarSparkCitation { News = news, Quote = news.Title, Role = "mixed" });
            }
            return spark;
        }

        private static DollarAnalysisResult Sample()
        {
            // ★ 픽스처는 검사 시계보다 앞서면 안 된다 ★
            //   창은 주입한 시계를 쓰는데(v1.040 부터 화면 전체가 그렇다) 이 자료는 실제 시계로
            //   만들어진다. 그러면 기사가 '아직 오지 않은 것' 이 되어 AI 응답이 통째로 반려된다.
            //   실제로 그렇게 어긋나 자동 갱신 예약이 다른 길로 샜다.
            //   1분만 물리면 '검사가 1분 안에 여기까지 온다' 는 벽시계 예산이 된다. 느린 기계에서
            //   그 예산을 넘기면 기사가 다시 미래가 되어 엉뚱한 곳에서 깨진다. AI 응답 유효 기간이
            //   24시간이므로 1시간은 넉넉하면서도 안전하다.
            DateTime now = DateTime.UtcNow.AddHours(-1);
            var result = new DollarAnalysisResult { CheckedUtc = now, DomesticAvailable = true, GlobalAvailable = true,
                Quote = new Quote { Ok = true, Price = "1,345.90", Ratio = "-0.81", Dir = -1, Source = "레이아웃 검사용 예시", Time = "12:00" },
                Spark = new DollarSparkResult { CheckedUtc = now, SubmittedCount = 48 } };
            for (int i = 0; i < 48; i++) result.News.Add(new DollarNews { Title = "레이아웃 확인용 기사 " + i, Source = "예시 출처", PublishedUtc = now,
                Url = "https://news.google.com/articles/preview" + i });
            foreach (int h in new[] { 1, 5, 20 })
            {
                var p = new DollarPattern { Horizon = h, LatestDate = DollarAnalysis.KoreaDate(now), Up = 18, Down = 12 };
                for (int i = 0; i < 30; i++) p.Returns.Add((i - 14) * 0.0006 * Math.Sqrt(h));
                result.Patterns.Add(p);
                var ai = new DollarSparkPeriod { Horizon = h, NewsScore = h == 1 ? 2.3 : h == 5 ? -1.2 : 8.4,
                    Reason = h == 5 ? "수출 달러 유입이 금리차의 상승 압력을 일부 상쇄" : "연준 금리차와 안전자산 수요가 달러 상승 압력",
                    Counter = "위험 선호 회복과 달러 매도가 반대 요인", Change = "금리 전망과 외국인 자금 흐름 변화 시 재평가", Confidence = "low" };
                foreach (var n in result.News.Take(h == 1 ? 48 : h == 5 ? 8 : 24)) ai.Citations.Add(new DollarSparkCitation { News = n, Quote = n.Title, Role = "mixed" });
                result.Spark.Periods.Add(ai);
            }
            result.Pattern = result.Patterns[0];
            result.Rates.Add(new DollarRate { Date = DollarAnalysis.KoreaDate(now).AddDays(-1), Value = 1347.5 });
            result.Rates.Add(new DollarRate { Date = DollarAnalysis.KoreaDate(now), Value = 1347.93 });
            return result;
        }

        private static void Save(FrameworkElement root, string work, string name, double width, double height)
        {
            var bitmap = new RenderTargetBitmap((int)(width * 1.5), (int)(height * 1.5), 144, 144, PixelFormats.Pbgra32);
            bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(work, name); using (var output = File.Create(path)) encoder.Save(output);
            Console.WriteLine("PREVIEW: " + path);
        }
    }
}
