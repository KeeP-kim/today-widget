using TextBlock = DeskWidget.KoreanTextBlock;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace DeskWidget
{
    internal static class AnalysisLoginTests
    {
        private static int count;
        private static void Check(bool ok, string reason)
        { if (!ok) throw new Exception("Analysis login: " + reason); count++; }
        private static T Field<T>(DollarAnalysisWindow window, string name)
        { return (T)typeof(DollarAnalysisWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); }
        private static string Primary(DollarAnalysisWindow window)
        { return Field<TextBlock>(window, "_primaryStatus").Text; }
        private static void AiMode(DollarAnalysisWindow window, bool enabled)
        { Field<ToggleButton>(window, "_analysisMode").IsChecked = enabled; }

        private static DollarAnalysisResult Sample(DateTime now)
        {
            var result = new DollarAnalysisResult { CheckedUtc = now, DomesticAvailable = true, GlobalAvailable = true,
                Quote = new Quote { Ok = true, Value = 1350, Price = "1,350.00", Source = "Synthetic login fixture", ReceivedUtc = now,
                    IdentityKey = "fx:FX_USDKRW" } };
            result.News.Add(new DollarNews { Title = "Synthetic settlement restoration confirmed", Source = "Synthetic publisher",
                PublishedUtc = now.AddMinutes(-5), Url = "https://news.google.com/articles/login-state-fixture" });
            foreach (int h in new[] { 1, 5, 20 }) {
                var pattern = new DollarPattern { Horizon = h, LatestDate = DollarAnalysis.KoreaDate(now), Up = 15, Down = 15 };
                for (int i = 0; i < 30; i++) pattern.Returns.Add((i - 14) * 0.001);
                result.Patterns.Add(pattern);
            }
            result.Pattern = result.Patterns[0];
            result.Rates.Add(new DollarRate { Date = DollarAnalysis.KoreaDate(now).AddDays(-1), Value = 1340 });
            result.Rates.Add(new DollarRate { Date = DollarAnalysis.KoreaDate(now), Value = 1350 });
            return result;
        }

        private static DollarSparkResult Spark(DollarAnalysisResult source, DateTime now, string model)
        {
            var spark = new DollarSparkResult { TargetKey = source.Target.Key, Extreme = source.Extreme, CheckedUtc = now,
                ModelId = model, SubmittedCount = source.News.Count, SubmittedNews = source.News };
            foreach (int h in new[] { 1, 5, 20 }) {
                var period = new DollarSparkPeriod { Horizon = h, NewsScore = 10, Confidence = "low",
                    Reason = "Synthetic reason", Counter = "Synthetic counter", Change = "Synthetic change condition" };
                period.Citations.Add(new DollarSparkCitation { News = source.News[0], Quote = source.News[0].Title, Role = "mixed" });
                spark.Periods.Add(period);
            }
            return spark;
        }

        internal static int Run(string work)
        {
            count = 0;
            if (Application.Current == null) Theme.Apply(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
            SavedLoginAndDisconnect(work);
            MissingAndFailedLogin(work);
            PendingLoginAndAnalysis(work);
            CancelPendingLogin(work, false);
            CancelPendingLogin(work, true);
            foreach (string action in new[] { "complete", "cancel", "basic", "close" }) SolProgressAndCancellation(work, action);
            return count;
        }

        private static void UpdateProgress(DollarAnalysisWindow window)
        {
            var update = typeof(DollarAnalysisWindow).GetMethod("UpdateAnalysisProgress", BindingFlags.Instance | BindingFlags.NonPublic);
            if (update == null) throw new Exception("Analysis login: analysis progress callback is missing");
            update.Invoke(window, null);
        }

        private static void SolProgressAndCancellation(string work, string action)
        {
            DateTime now = DateTime.UtcNow;
            var analysis = new TaskCompletionSource<DollarSparkResult>();
            CancellationToken observed = CancellationToken.None; DollarAnalysisResult activeSource = null;
            var cfg = new Config(Path.Combine(work, "analysis-sol-progress-" + action + ".json"));
            cfg.AnalysisModel = "gpt-6-astra";
            var window = new DollarAnalysisWindow(cfg, ct => Task.FromResult(Sample(now)),
                ct => Task.FromResult(true), ct => Task.FromResult(0), null,
                (source, ct) => { observed = ct; activeSource = source; return analysis.Task; }, () => now);
            bool closed = false; Task active = null;
            try {
                AiMode(window, true); active = window.RefreshAsync();
                Check(!active.IsCompleted && activeSource != null && Field<DispatcherTimer>(window, "_analysisProgressTimer").IsEnabled,
                    "Sol pending analysis did not start the progress timer: " + action);
                now = now.AddSeconds(65); UpdateProgress(window);
                string state = Field<TextBlock>(window, "_sparkState").Text;
                Check(state.Contains("Sol 6.1") && state.Contains("65초") && state.Contains("최대 5분"),
                    "Sol progress omitted elapsed time or the five-minute budget: " + action);
                Check(Primary(window).Contains("Sol 6.1") && Primary(window).Contains("65초") && Primary(window).Contains("최대 5분"),
                    "primary Sol status disagrees with elapsed progress or budget: " + action);

                if (action == "complete") {
                    analysis.SetResult(Spark(activeSource, now, DollarSpark.Model)); active.GetAwaiter().GetResult();
                    Check(!Field<DispatcherTimer>(window, "_analysisProgressTimer").IsEnabled && Primary(window).Contains("성능 검증 중"),
                        "finished Sol analysis retained its progress timer or pending banner");
                    string completePrimary = Primary(window), completeState = Field<TextBlock>(window, "_sparkState").Text;
                    now = now.AddSeconds(10); UpdateProgress(window);
                    Check(Primary(window) == completePrimary && Field<TextBlock>(window, "_sparkState").Text == completeState,
                        "late progress callback overwrote completed Sol analysis");
                    return;
                }

                if (action == "cancel") window.CancelRefresh();
                else if (action == "basic") AiMode(window, false);
                else { window.Close(); closed = true; }
                Check(observed.IsCancellationRequested && !Field<DispatcherTimer>(window, "_analysisProgressTimer").IsEnabled,
                    "model, basic-mode or close action did not immediately cancel Astra and stop its timer: " + action);
                string cancelledPrimary = Primary(window), cancelledState = Field<TextBlock>(window, "_sparkState").Text;
                now = now.AddSeconds(10); UpdateProgress(window);
                Check(Primary(window) == cancelledPrimary && Field<TextBlock>(window, "_sparkState").Text == cancelledState,
                    "late progress callback overwrote the cancellation state: " + action);
                analysis.SetResult(Spark(activeSource, now, DollarSpark.Model)); active.GetAwaiter().GetResult();
                Check(!Field<DispatcherTimer>(window, "_analysisProgressTimer").IsEnabled &&
                    Field<TextBlock>(window, "_sparkState").Text == cancelledState,
                    "late Sol result restarted progress or overwrote the cancellation state: " + action);
                if (closed) Check(Primary(window) == cancelledPrimary, "late Sol result changed the closed window");
                else Check(!Primary(window).Contains("분석 중") && Field<Button>(window, "_refresh").IsEnabled,
                    "late Sol result replaced the selected model/basic state or left refresh disabled: " + action);
            }
            finally {
                if (!closed) window.Close();
                if (activeSource != null) analysis.TrySetResult(Spark(activeSource, now, DollarSpark.Model));
                if (active != null) active.GetAwaiter().GetResult();
            }
        }

        private static void SavedLoginAndDisconnect(string work)
        {
            DateTime now = DateTime.UtcNow; int probes = 0, browser = 0, analyses = 0, fetches = 0;
            var cfg = new Config(Path.Combine(work, "analysis-login-existing.json"));
            var window = new DollarAnalysisWindow(cfg, ct => { fetches++; return Task.FromResult(Sample(now)); },
                ct => { probes++; return Task.FromResult(true); }, ct => { browser++; return Task.FromResult(0); }, null,
                (source, ct) => { analyses++; return Task.FromResult(Spark(source, now, cfg.AnalysisModel)); }, () => now);
            try {
                window.RefreshAsync().GetAwaiter().GetResult();
                Check(fetches == 1 && probes == 0 && analyses == 0 && browser == 0, "basic refresh checked login or consumed an AI request");
                AiMode(window, true);
                window.RefreshAsync().GetAwaiter().GetResult();
                Check(fetches == 2 && probes > 0 && analyses == 1 && browser == 0, "first AI refresh did not discover existing login after recent basic refresh");
                Check(Field<bool>(window, "_sparkConnected") && Field<Ellipse>(window, "_sparkDot").Fill == Palette.Online,
                    "discovered login did not turn connection indicator green");
                Check(Primary(window).Contains("AI 주전망") && Primary(window).Contains("Sol 6.1") && !Primary(window).Contains("로그인 후"),
                    "completed analysis retained the login-required placeholder");
                int beforeProbes = probes;
                window.RefreshAsync().GetAwaiter().GetResult();
                AiMode(window, false); AiMode(window, true);
                window.RefreshAsync().GetAwaiter().GetResult();
                Check(fetches == 2 && analyses == 1 && probes == beforeProbes, "repeated AI refresh or mode toggling bypassed the 30-second guard");

                now = now.AddSeconds(31); window.RefreshAsync().GetAwaiter().GetResult();
                Check(analyses == 2 && browser == 0 && Primary(window).Contains("Sol 6.1"), "manual refresh did not retain Sol and saved login");

                Field<Button>(window, "_sparkLogin").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                beforeProbes = probes; int beforeAnalyses = analyses;
                now = now.AddSeconds(31); window.RefreshAsync().GetAwaiter().GetResult();
                Check(probes == beforeProbes && analyses == beforeAnalyses && browser == 0 && !Field<bool>(window, "_sparkConnected"),
                    "manual refresh silently reversed explicit window disconnect");
                Check(Field<Ellipse>(window, "_sparkDot").Fill != Palette.Online && Field<Button>(window, "_refresh").IsEnabled,
                    "disconnected refresh retained a green indicator or disabled controls");
                Check(!window.IsLoaded, "login regression test opened a native window");
            }
            finally { window.Close(); }
        }

        private static void MissingAndFailedLogin(string work)
        {
            foreach (bool failure in new[] { false, true }) {
                DateTime now = DateTime.UtcNow; int probes = 0, browser = 0, analyses = 0;
                var window = new DollarAnalysisWindow(new Config(Path.Combine(work, "analysis-login-missing-" + failure + ".json")),
                    ct => Task.FromResult(Sample(now)), ct => { probes++; if (failure) throw new IOException("Synthetic probe failure"); return Task.FromResult(false); },
                    ct => { browser++; return Task.FromResult(0); }, null,
                    (source, ct) => { analyses++; return Task.FromResult(Spark(source, now, DollarSpark.Model)); }, () => now);
                try {
                    AiMode(window, true); window.RefreshAsync().GetAwaiter().GetResult();
                    Check(probes == 1 && browser == 0 && analyses == 0 && !Field<bool>(window, "_sparkConnected"),
                        "missing or failed saved login opened interactive auth, consumed AI or stayed connected");
                    Check(Field<Button>(window, "_refresh").IsEnabled && Field<Button>(window, "_sparkLogin").IsEnabled &&
                        !Field<bool>(window, "_busy") && !Field<bool>(window, "_loginBusy"), "login probe failure left UI busy or disabled");
                    Check(failure ? Primary(window).Contains("실패") : Primary(window).Contains("로그인"),
                        "primary forecast did not explain missing or failed authentication");
                    Check(Field<Ellipse>(window, "_sparkDot").Fill != Palette.Online, "failed login falsely showed green");
                }
                finally { window.Close(); }
            }
        }

        private static void PendingLoginAndAnalysis(string work)
        {
            DateTime now = DateTime.UtcNow; int probes = 0, browser = 0, analyses = 0;
            var probe = new TaskCompletionSource<bool>(); var analysis = new TaskCompletionSource<DollarSparkResult>();
            DollarAnalysisResult activeSource = null;
            var window = new DollarAnalysisWindow(new Config(Path.Combine(work, "analysis-login-pending.json")), ct => Task.FromResult(Sample(now)),
                ct => { probes++; return probe.Task; }, ct => { browser++; return Task.FromResult(0); }, null,
                (source, ct) => { analyses++; activeSource = source; return analysis.Task; }, () => now);
            Task active = null;
            try {
                AiMode(window, true); active = window.RefreshAsync();
                Check(!active.IsCompleted && probes == 1 && analyses == 0 && Primary(window).Contains("로그인 확인 중"),
                    "pending saved-login probe showed a login-required or analysis-ready placeholder");
                Check(Field<Button>(window, "_refresh").IsEnabled && Field<Button>(window, "_refresh").Content.ToString() == "취소" && Field<Ellipse>(window, "_sparkDot").Fill != Palette.Online,
                    "pending auth enabled another request or claimed verified login");
                window.RefreshAsync().GetAwaiter().GetResult();
                Check(probes == 1, "overlapping refresh started another login probe");
                probe.SetResult(true);
                Check(!active.IsCompleted && analyses == 1 && browser == 0 && Primary(window).Contains("분석 중") && !Primary(window).Contains("로그인 후"),
                    "pending analysis still instructed the connected user to log in");
                analysis.SetResult(Spark(activeSource, now, DollarSpark.Model)); active.GetAwaiter().GetResult();
                Check(Primary(window).Contains("AI 주전망") && Primary(window).Contains("Sol 6.1") && Field<Button>(window, "_refresh").IsEnabled,
                    "finished analysis failed to replace progress status or restore refresh");
            }
            finally {
                window.Close();
                probe.TrySetResult(false);
                if (activeSource != null) analysis.TrySetResult(Spark(activeSource, now, DollarSpark.Model));
                if (active != null) active.GetAwaiter().GetResult();
            }
        }

        private static void CancelPendingLogin(string work, bool close)
        {
            DateTime now = DateTime.UtcNow; int analyses = 0, browser = 0;
            var probe = new TaskCompletionSource<bool>(); CancellationToken observed = CancellationToken.None;
            var window = new DollarAnalysisWindow(new Config(Path.Combine(work, "analysis-login-cancel-" + close + ".json")),
                ct => Task.FromResult(Sample(now)), ct => { observed = ct; return probe.Task; },
                ct => { browser++; return Task.FromResult(0); }, null,
                (source, ct) => { analyses++; return Task.FromResult(Spark(source, now, DollarSpark.Model)); }, () => now);
            bool closed = false; Task active = null;
            try {
                AiMode(window, true); active = window.RefreshAsync();
                Check(!active.IsCompleted && Primary(window).Contains("로그인 확인 중"), "cancellation fixture did not reach pending login");
                if (close) { window.Close(); closed = true; } else AiMode(window, false);
                Check(observed.IsCancellationRequested, "closing or entering basic mode did not cancel login probe");
                probe.SetResult(true); active.GetAwaiter().GetResult();
                Check(analyses == 0 && browser == 0 && !Field<bool>(window, "_sparkConnected"),
                    "late successful probe authenticated or analyzed after cancellation");
                if (!close) Check(Field<Button>(window, "_refresh").IsEnabled && !Primary(window).Contains("분석 중"),
                    "cancelled login left the basic-mode UI analyzing");
            }
            finally {
                if (!closed) window.Close();
                probe.TrySetResult(false);
                if (active != null) active.GetAwaiter().GetResult();
            }
        }
    }
}
