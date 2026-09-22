using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml;

namespace DeskWidget
{
    internal static class DataFreshnessTests
    {
        private static int checks;
        private static void Check(bool ok, string why)
        { if (!ok) throw new Exception("Data freshness: " + why); checks++; }
        internal static int Run(string work)
        {
            checks = 0;
            QuoteTimes();
            SearchChanges();
            JournalTimes(work);
            return checks;
        }
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static string Ms(DateTime time)
        { return ((long)(time - Epoch).TotalMilliseconds).ToString(CultureInfo.InvariantCulture); }
        private static Quote Coin(DateTime received, DateTime provider, DateTime traded)
        {
            var def = new SymbolDef(SourceKind.Coin, "KRW-DOGE", "DOGE fixture");
            var quote = Sources.ParseUpbit(def, Json.Parse("[{\"market\":\"KRW-DOGE\",\"trade_price\":100,\"timestamp\":" + Ms(provider) +
                ",\"trade_timestamp\":" + Ms(traded) + ",\"signed_change_price\":0,\"signed_change_rate\":0,\"change\":\"EVEN\"}]"));
            quote.ReceivedUtc = received; quote.IdentityKey = def.Key; quote.Source = "fixture";
            return quote;
        }
        private static void QuoteTimes()
        {
            var now = new DateTime(2026, 9, 22, 3, 0, 0, DateTimeKind.Utc);
            var q = Coin(now, now.AddSeconds(-1), now.AddSeconds(-2));
            Check(q.Ok && q.ProviderTimeRequired && q.ProviderUtc == now.AddSeconds(-1) && q.TradedUtc == now.AddSeconds(-2), "provider clocks were lost");
            Check(q.Time == q.TradedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), "display time used the receipt clock");
            Check(Sources.IsQuoteFresh(q, now) && PredictionJournal.Fresh(q, now), "current trade refused");
            q.TradedUtc = now.AddDays(-1);
            Check(Sources.QuoteFreshnessOf(q, now) == QuoteFreshness.Stale && !PredictionJournal.Fresh(q, now), "old trade accepted after new receipt");
            q.TradedUtc = now.AddMinutes(-5); q.ProviderUtc = now;
            Check(Sources.IsQuoteFresh(q, now), "five-minute boundary refused");
            q.TradedUtc = now.AddMinutes(-5).AddMilliseconds(-1);
            Check(!Sources.IsQuoteFresh(q, now), "expired trade boundary accepted");
            q.TradedUtc = now; q.ProviderUtc = now.AddMinutes(-6);
            Check(Sources.QuoteFreshnessOf(q, now) != QuoteFreshness.Fresh, "old ticker accepted");
            q.ProviderUtc = q.TradedUtc = now.AddSeconds(30);
            Check(Sources.IsQuoteFresh(q, now), "documented clock skew refused");
            q.ProviderUtc = q.TradedUtc = now.AddSeconds(31);
            Check(Sources.QuoteFreshnessOf(q, now) == QuoteFreshness.Future, "future timestamp accepted");
            q.ProviderUtc = now.AddSeconds(-31); q.TradedUtc = now;
            Check(Sources.QuoteFreshnessOf(q, now) == QuoteFreshness.Invalid, "trade after ticker accepted");
            q.ProviderUtc = DateTime.MinValue;
            Check(Sources.QuoteFreshnessOf(q, now) == QuoteFreshness.Invalid, "missing timestamp accepted");
            var missing = Sources.ParseUpbit(new SymbolDef(SourceKind.Coin, "KRW-DOGE", "DOGE"), Json.Parse("[{\"market\":\"KRW-DOGE\",\"trade_price\":100}]"));
            missing.ReceivedUtc = now;
            Check(missing.Ok && missing.ProviderTimeRequired && Sources.QuoteFreshnessOf(missing, now) == QuoteFreshness.Invalid, "missing trade time became current time");
            var wrong = Sources.ParseUpbit(new SymbolDef(SourceKind.Coin, "KRW-DOGE", "DOGE"), Json.Parse("[{\"market\":\"KRW-BTC\",\"trade_price\":100}]"));
            Check(!wrong.Ok, "another market was labeled DOGE");
            var invalid = Sources.ParseUpbit(new SymbolDef(SourceKind.Coin, "KRW-DOGE", "DOGE"), Json.Parse("[{\"market\":\"KRW-DOGE\",\"trade_price\":100,\"timestamp\":1e100,\"trade_timestamp\":-1}]"));
            invalid.ReceivedUtc = now;
            Check(Sources.QuoteFreshnessOf(invalid, now) == QuoteFreshness.Invalid, "invalid epoch accepted");
            var fx = new Quote { Ok = true, Value = 1300, ReceivedUtc = now, IdentityKey = "fx:FX_USDKRW", TradedDate = DollarAnalysis.KoreaDate(now).AddDays(-1) };
            Check(!PredictionJournal.Fresh(fx, now), "holiday posted FX quote accepted");
            fx.TradedDate = DollarAnalysis.KoreaDate(now);
            Check(PredictionJournal.Fresh(fx, now), "current FX quote refused");
            fx.ReceivedUtc = now.AddSeconds(1);
            Check(!PredictionJournal.Fresh(fx, now), "future receipt accepted");
        }

        private static object Field(object target, string name)
        { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
        private static void StartSearch(SearchWindow window, string text)
        {
            ((TextBox)Field(window, "_input")).Text = text;
            var timer = (DispatcherTimer)Field(window, "_debounce"); if (timer != null) timer.Stop();
            typeof(SearchWindow).GetMethod("Run", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
        }
        private static void Pump()
        {
            for (int i = 0; i < 5; i++) {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; }));
                Dispatcher.PushFrame(frame);
            }
        }
        private static List<SearchHit> Hits(string label)
        { return new List<SearchHit> { new SearchHit { Def = new SymbolDef(SourceKind.DomesticStock, "005930", label), TypeName = "fixture" } }; }
        private static void SearchChanges()
        {
            var original = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var pending = new List<TaskCompletionSource<List<SearchHit>>>();
            var tokens = new List<CancellationToken>();
            var window = new SearchWindow(new SymbolDef[0], delegate { }, false, (q, ct) => {
                var request = new TaskCompletionSource<List<SearchHit>>(); pending.Add(request); tokens.Add(ct); return request.Task;
            });
            try {
                var input = (TextBox)Field(window, "_input"); var results = (StackPanel)Field(window, "_results");
                StartSearch(window, "old"); input.Text = "";
                Check(tokens[0].IsCancellationRequested, "clear did not immediately cancel old search");
                pending[0].SetResult(Hits("old")); Pump();
                Check(results.Children.Count == 0 && ((TextBlock)Field(window, "_status")).Text == "", "cleared query regained old results");
                StartSearch(window, "first"); input.Text = "second";
                Check(tokens[1].IsCancellationRequested, "edit waited for debounce before cancelling");
                pending[1].SetResult(Hits("first")); Pump();
                Check(results.Children.Count == 0, "edited query displayed old results");
                StartSearch(window, "second"); pending[2].SetResult(Hits("second")); Pump();
                Check(results.Children.Count == 1, "current query did not display results");
                StartSearch(window, "close"); window.Close();
                Check(tokens[3].IsCancellationRequested, "closed window did not cancel search");
                pending[3].SetResult(Hits("close")); Pump();
                Check(results.Children.Count == 0, "closed window accepted late results");
            } finally { window.Close(); SynchronizationContext.SetSynchronizationContext(original); }
        }

        private static void JournalTimes(string work)
        {
            string original = Program.BaseDir;
            Program.BaseDir = Path.Combine(work, "data-freshness-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(PredictionJournal.Folder);
            try {
                var now = new DateTime(2026, 9, 22, 3, 0, 0, DateTimeKind.Utc);
                var quote = Coin(now, now, now.AddDays(-1));
                var target = new PredictionTarget(new SymbolDef(SourceKind.Coin, "KRW-DOGE", "DOGE fixture"));
                var result = new DollarAnalysisResult { Target = target, Quote = quote, CheckedUtc = now };
                var pattern = new DollarPattern { Horizon = 1, Up = 40, LatestDate = now.Date, ReferenceRate = 100 };
                pattern.Returns.Add(0.01); result.Patterns.Add(pattern);
                PredictionJournal.Record(result, now);
                Check(Directory.GetFiles(PredictionJournal.Folder, "*.xml").Length == 0, "stale trade recorded as a new forecast");
                quote.TradedUtc = now;
                PredictionJournal.Record(result, now);
                string path = Directory.GetFiles(PredictionJournal.Folder, "*.xml").Single();
                var record = new XmlDocument(); record.Load(path); var root = record.DocumentElement;
                Check(root.GetAttribute("quoteTradedUtc") == now.ToString("o") && root.GetAttribute("timePolicy") == "upbit-trade-v1", "forecast omitted anchor clocks");
                string immutable = File.ReadAllText(path);
                var legacy = new XmlDocument(); legacy.LoadXml(immutable);
                legacy.DocumentElement.SetAttribute("version", "1.049");
                legacy.DocumentElement.RemoveAttribute("timePolicy"); legacy.DocumentElement.RemoveAttribute("quoteProviderUtc"); legacy.DocumentElement.RemoveAttribute("quoteTradedUtc");
                string legacyPath = Path.Combine(PredictionJournal.Folder, "legacy.xml"); legacy.Save(legacyPath);
                DateTime due = now.AddDays(1), observed = due.AddMinutes(1);
                var outcome = Coin(observed, observed, due.AddDays(-1));
                PredictionJournal.Observe(outcome, observed);
                string newScorePath = path + ".basic.1.score.xml", oldScorePath = legacyPath + ".basic.1.score.xml";
                Check(!File.Exists(newScorePath), "old exchange trade graded a new forecast");
                Check(File.Exists(oldScorePath), "legacy receipt-time scoring changed");
                string oldScoreBytes = File.ReadAllText(oldScorePath);
                outcome.TradedUtc = due.AddSeconds(-1);
                PredictionJournal.Observe(outcome, observed);
                Check(!File.Exists(newScorePath), "pre-maturity trade graded a new forecast");
                outcome.TradedUtc = due.AddSeconds(1); outcome.ProviderUtc = due.AddSeconds(2);
                PredictionJournal.Observe(outcome, observed);
                Check(File.Exists(newScorePath), "fresh post-maturity trade was not graded");
                Check(File.ReadAllText(path) == immutable && File.ReadAllText(oldScorePath) == oldScoreBytes, "old forecast or score was rewritten");
                var score = new XmlDocument(); score.Load(newScorePath);
                Check(score.DocumentElement.GetAttribute("tradedUtc") == outcome.TradedUtc.ToString("o") &&
                    score.DocumentElement.GetAttribute("providerUtc") == outcome.ProviderUtc.ToString("o"), "outcome clocks were not preserved");
                var readScore = typeof(PredictionJournal).GetMethod("ReadScore", BindingFlags.Static | BindingFlags.NonPublic);
                Check(readScore.Invoke(null, new object[] { newScorePath, root, root.SelectSingleNode("forecast") }) != null, "new valid score unreadable");
                Check(readScore.Invoke(null, new object[] { oldScorePath, legacy.DocumentElement, legacy.DocumentElement.SelectSingleNode("forecast") }) != null, "legacy score reinterpreted by new clock rule");
                score.DocumentElement.SetAttribute("tradedUtc", due.AddDays(-1).ToString("o")); score.Save(newScorePath);
                Check(readScore.Invoke(null, new object[] { newScorePath, root, root.SelectSingleNode("forecast") }) == null, "corrupt new score trade time accepted");
            } finally { Program.BaseDir = original; }
        }
    }
}
