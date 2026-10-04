using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Xml;

namespace DeskWidget
{
    internal static class ModelComparisonTests
    {
        private static int checks;
        private static void Check(bool value, string reason) { if (!value) throw new Exception("Model comparison: " + reason); checks++; }
        private static T Field<T>(DollarAnalysisWindow w, string name)
        { return (T)typeof(DollarAnalysisWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w); }
        private static DollarAnalysisResult Sample(DateTime now)
        {
            var r = new DollarAnalysisResult { Extreme = true, CheckedUtc = now, DomesticAvailable = true, GlobalAvailable = true,
                Quote = new Quote { Ok = true, Value = 1350, Price = "1,350.00", ReceivedUtc = now, IdentityKey = "fx:FX_USDKRW|HANA", Source = "Synthetic comparison" } };
            r.News.Add(new DollarNews { Title = "Synthetic settlement change confirmed", Source = "Synthetic publisher", Context = "Synthetic dated evidence.",
                PublishedUtc = now.AddMinutes(-5), Url = "https://news.google.com/articles/comparison-fixture" });
            foreach (int h in new[] { 1, 5, 20 }) {
                var p = new DollarPattern { Horizon = h, LatestDate = DollarAnalysis.KoreaDate(now), Up = 15, Down = 15 };
                for (int i = 0; i < 30; i++) p.Returns.Add((i - 14) * 0.001);
                r.Patterns.Add(p);
            }
            r.Pattern = r.Patterns[0];
            r.Rates.Add(new DollarRate { Date = DollarAnalysis.KoreaDate(now).AddDays(-1), Value = 1340 });
            return r;
        }
        private static DollarSparkResult Spark(DollarAnalysisResult r, DateTime now, string model)
        {
            var ai = new DollarSparkResult { ModelId = model, Extreme = true, TargetKey = r.Target.Key,
                CheckedUtc = now, SubmittedNews = r.News, SubmittedCount = r.News.Count };
            foreach (int h in new[] { 1, 5, 20 }) {
                var p = new DollarSparkPeriod { Horizon = h, NewsScore = 10, Confidence = "low", Reason = "Synthetic reason", Counter = "Synthetic counter", Change = "Synthetic condition" };
                p.Citations.Add(new DollarSparkCitation { News = r.News[0], Quote = r.News[0].Title, Role = "mixed" });
                ai.Periods.Add(p);
            }
            return ai;
        }
        internal static int Run(string work)
        {
            checks = 0; string previous = Program.BaseDir;
            Program.BaseDir = Path.Combine(work, "model-comparison"); Directory.CreateDirectory(Program.BaseDir);
            if (Application.Current == null) Theme.Apply(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
            try { FrozenInputs(); Ledger(); PairedJournal(); WindowFlow(); Cancellation(); return checks; }
            finally { Program.BaseDir = previous; }
        }
        private static void FrozenInputs()
        {
            DateTime now = DateTime.UtcNow; var source = Sample(now); var input = new AnalysisInput(source, now);
            var compatible = new AnalysisInput(source, now, false);
            Check(DollarSpark.EligibleNews(compatible.Source, source.News[0], now) && object.ReferenceEquals(compatible.News[0], source.News[0]),
                "single-model API no longer binds citations to the caller article list");
            Check(input.Id == new AnalysisInput(source, now).Id && input.Id.Length == 64, "same frozen request has a different fingerprint");
            source.Quote.Value = 2000; source.News[0].Title = "Revised later"; source.Rates[0].Value = 900;
            source.Patterns[0].Returns.Clear(); source.Patterns[0].Up = 90;
            Check(input.Source.Quote.Value == 1350 && input.News[0].Title != "Revised later" && input.Source.Rates[0].Value == 1340,
                "later quote, article or rate mutation changed the frozen input");
            Check(input.Source.Patterns[0].Returns.Count == 30 && input.Source.Patterns[0].Up == 15, "later pattern mutation changed comparison price conversion");
            Check(input.Id != new AnalysisInput(source, now).Id, "changed input was pooled under the old request fingerprint");
            Check(DollarSpark.ModelId(DollarSpark.LunaModel) == DollarSpark.Model && DollarSpark.ModelId(DollarSpark.AstraModel) == DollarSpark.Model && DollarSpark.ActiveModels.SequenceEqual(new[] { DollarSpark.Model }) && DollarSpark.HistoricalModel(DollarSpark.LegacyModel), "existing model choice or old Spark record was relabeled");
            Check(DollarSpark.AnalysisTimeoutSeconds(DollarSpark.Model) == 300 && DollarSpark.AnalysisTimeoutSeconds(DollarSpark.LunaModel) == 300,
                "model-specific timeout changed");
        }
        private static void Ledger()
        {
            DateTime now = DateTime.UtcNow; var input = new AnalysisInput(Sample(now), now);
            var runs = new List<AnalysisRun> {
                new AnalysisRun { Model = DollarSpark.Model, Outcome = "success", Seconds = 10 },
                new AnalysisRun { Model = DollarSpark.Model, Outcome = "timeout", Seconds = 300 },
                new AnalysisRun { Model = DollarSpark.Model, Outcome = "cancelled", Seconds = 20 },
                new AnalysisRun { Model = DollarSpark.LunaModel, Outcome = "error", Seconds = 3 } };
            ModelComparison.SaveRuns(input, runs, now);
            string summary = ModelComparison.Summary(input.Source.Quote);
            Check(summary.Contains("성공 1/2") && summary.Contains("실패 50%") && summary.Contains("취소 1"), "failed or cancelled attempts disappeared from the summary");
            Check(summary.Contains("성공 평균 10.0초") && summary.Contains("전체 대기 평균 110.0초"), "success-only duration hid failed waiting time");
            string file = Directory.GetFiles(ModelComparison.Folder, "*.xml").Single(); var doc = new XmlDocument(); doc.Load(file);
            ((XmlElement)doc.SelectSingleNode("/analysisRuns/run")).SetAttribute("seconds", "NaN"); doc.Save(file);
            Check(ModelComparison.Summary(input.Source.Quote).Contains("읽을 수 없는 실행 기록 1건 제외"), "non-finite timing was included");
        }
        private static void PairedJournal()
        {
            DateTime now = DateTime.UtcNow; var r = Sample(now); var input = new AnalysisInput(r, now);
            r.ComparisonInputId = input.Id;
            var ai = Spark(r, now, DollarSpark.Model); ai.InputId = input.Id; r.Spark = ai;
            PredictionJournal.Record(r, now);
            string path = Directory.GetFiles(PredictionJournal.Folder, "*.xml").Single(); var doc = new XmlDocument(); doc.Load(path);
            doc.DocumentElement.SetAttribute("comparisonInput", input.Id);
            foreach (string model in new[] { DollarSpark.LunaModel, DollarSpark.AstraModel })
                foreach (XmlElement sol in doc.SelectNodes("/prediction/forecast[@model='gpt-6.1-sol']")) {
                    var archived = (XmlElement)sol.CloneNode(true); archived.SetAttribute("model", model); doc.DocumentElement.AppendChild(archived);
                }
            doc.Save(path);
            Check(doc.SelectNodes("/prediction/forecast").Count == 12, "three model forecasts and basic reference were not recorded together");
            Check(doc.SelectSingleNode("/prediction/aiNewsInput") != null, "submitted evidence was lost");
            Check(PredictionJournal.ComparisonSummary(r.Quote).Contains("채점 대기 9쌍") && !PredictionJournal.ComparisonSummary(r.Quote).Contains("방향 "),
                "unmatured predictions were presented as accuracy");
            var old = new XmlDocument(); old.LoadXml(doc.OuterXml);
            foreach (XmlNode node in old.SelectNodes("/prediction/forecast[@model!='basic']")) node.ParentNode.RemoveChild(node);
            foreach (XmlElement node in old.SelectNodes("/prediction/forecast")) node.SetAttribute("model", DollarSpark.LunaModel);
            var valid = typeof(PredictionJournal).GetMethod("ValidRecord", BindingFlags.Static | BindingFlags.NonPublic);
            Check((bool)valid.Invoke(null, new object[] { old.DocumentElement }), "historical Luna forecasts rejected after Sol became default");
            string original = File.ReadAllText(path); var due = DateTime.Parse(((XmlElement)doc.SelectSingleNode("/prediction/forecast[@horizon='1']")).GetAttribute("due"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
            var outcome = r.Quote.Snapshot(); outcome.Value = 1400; outcome.ReceivedUtc = due.AddMinutes(1);
            PredictionJournal.Observe(outcome, outcome.ReceivedUtc);
            Check(File.ReadAllText(path) == original, "outcome observation rewrote the forecast input");
            string summary = PredictionJournal.ComparisonSummary(outcome);
            Check(summary.Contains("Sol 6.1 / Luna") && summary.Contains("방향 ") && summary.Contains("일간 · 1건"), "matching matured model pairs were not scored");
            string archiveCopy = Path.Combine(PredictionJournal.Folder, "historical-duplicate.xml"); File.WriteAllText(archiveCopy, original);
            string duplicate = Directory.GetFiles(PredictionJournal.Folder, "*.xml").First(p => p != path && !p.EndsWith(".score.xml"));
            PredictionJournal.Observe(outcome, outcome.ReceivedUtc);
            Check(PredictionJournal.ComparisonSummary(outcome).Contains("일간 · 1건") && !PredictionJournal.ComparisonSummary(outcome).Contains("일간 · 2건"),
                "overlapping duplicate forecasts inflated model accuracy samples");
            File.Delete(duplicate);
            foreach (string file in Directory.GetFiles(PredictionJournal.Folder, Path.GetFileName(duplicate) + ".*.score.xml")) File.Delete(file);
            var luna = (XmlElement)doc.SelectSingleNode("/prediction/forecast[@model='gpt-5.6-luna'][@horizon='1']");
            luna.SetAttribute("effort", "medium"); doc.Save(path);
            Check(!PredictionJournal.ComparisonSummary(outcome).Contains("Sol 6.1 / Luna"), "different reasoning effort was pooled");
            luna.SetAttribute("effort", "high"); luna.SetAttribute("inputId", new string('f', 64)); doc.Save(path);
            Check(!PredictionJournal.ComparisonSummary(outcome).Contains("Sol 6.1 / Luna"), "different article input was pooled");
            doc.LoadXml(original); doc.Save(path);
            string scorePath = path + "." + DollarSpark.LunaModel + ".1.score.xml"; var score = new XmlDocument(); score.Load(scorePath);
            score.DocumentElement.SetAttribute("received", due.AddMinutes(2).ToString("o")); score.Save(scorePath);
            Check(!PredictionJournal.ComparisonSummary(outcome).Contains("Sol 6.1 / Luna"), "different observation time was pooled");
            score.DocumentElement.SetAttribute("received", due.AddMinutes(1).ToString("o")); score.DocumentElement.SetAttribute("actual", "1401"); score.Save(scorePath);
            Check(!PredictionJournal.ComparisonSummary(outcome).Contains("Sol 6.1 / Luna"), "different observed price was pooled");
            score.DocumentElement.SetAttribute("actual", "1400"); score.Save(scorePath);
            doc.LoadXml(original); ((XmlElement)doc.SelectSingleNode("/prediction/forecast[@model='gpt-5.6-luna'][@horizon='1']")).SetAttribute("threshold", "0.01"); doc.Save(path);
            Check(!PredictionJournal.ComparisonSummary(outcome).Contains("Sol 6.1 / Luna"), "different scoring threshold was pooled");
        }
        private static void WindowFlow()
        {
            DateTime now = DateTime.UtcNow; var calls = new List<string>(); bool fail = false;
            var cfg = new Config(Path.Combine(Program.BaseDir, "window.json")); cfg.Bank = "HANA"; cfg.AnalysisModel = DollarSpark.AstraModel;
            var w = new DollarAnalysisWindow(cfg, ct => Task.FromResult(Sample(now)), ct => Task.FromResult(true), ct => Task.FromResult(0),
                null, null, () => now, (r, ct, model) => {
                    calls.Add(model);
                    if (fail) throw new InvalidOperationException("응답 시간 초과 · 가상 검사");
                    return Task.FromResult(Spark(r, now, model)); });
            try {
                w.RefreshAsync().GetAwaiter().GetResult(); Check(calls.Count == 0, "basic mode consumed AI calls");
                Field<ToggleButton>(w, "_analysisMode").IsChecked = true; w.RefreshAsync().GetAwaiter().GetResult();
                Check(calls.SequenceEqual(new[] { DollarSpark.Model }), "retired configured model or multiple models executed");
                Check(Field<DollarAnalysisResult>(w, "_lastRendered").Spark.ModelId == DollarSpark.Model, "primary forecast was not Sol");
                Check(Field<DollarAnalysisResult>(w, "_lastRendered").ComparisonResults.Count == 0, "single request retained comparison results");
                Check(typeof(DollarAnalysisWindow).GetMethod("ChangeModel", BindingFlags.Instance | BindingFlags.NonPublic) == null &&
                    typeof(DollarAnalysisWindow).GetMethod("CompareModelsAsync", BindingFlags.Instance | BindingFlags.NonPublic) == null &&
                    typeof(DollarAnalysisWindow).GetMethod("RetryModelAsync", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                    "removed model commands remain callable");
                Check(typeof(DollarAnalysisWindow).GetField("_compareModels", BindingFlags.Instance | BindingFlags.NonPublic) == null &&
                    typeof(DollarAnalysisWindow).GetField("_retryModel", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                    "removed model controls remain in the window");
                now = now.AddSeconds(31); fail = true; w.RefreshAsync().GetAwaiter().GetResult(); int before = calls.Count;
                Check(Field<bool>(w, "_sparkAutoPaused") && Field<List<AnalysisRun>>(w, "_analysisRuns").Single().Outcome == "timeout",
                    "Sol failure did not pause automatic calls or record timeout");
                fail = false; now = now.AddSeconds(31); w.RefreshAsync().GetAwaiter().GetResult();
                Check(calls.Count == before + 1 && calls.All(m => m == DollarSpark.Model) && Field<DollarAnalysisResult>(w, "_lastRendered").Spark.ModelId == DollarSpark.Model,
                    "manual retry invoked a retired model or multiple calls");
                Check(!w.IsLoaded, "Sol test opened a native window");
            } finally { w.Close(); }
        }
        private static void Cancellation()
        {
            DateTime now = DateTime.UtcNow; int calls = 0; var pending = new TaskCompletionSource<DollarSparkResult>();
            var cfg = new Config(Path.Combine(Program.BaseDir, "cancel.json")); cfg.Bank = "HANA";
            var w = new DollarAnalysisWindow(cfg, ct => Task.FromResult(Sample(now)), ct => Task.FromResult(true), ct => Task.FromResult(0),
                null, null, () => now, (r, ct, model) => { calls++; ct.Register(() => pending.TrySetCanceled()); return pending.Task; });
            try {
                Field<ToggleButton>(w, "_analysisMode").IsChecked = true; var task = w.RefreshAsync();
                Check(!task.IsCompleted && Field<Button>(w, "_refresh").Content.ToString() == "취소", "pending analysis did not expose cancellation");
                w.CancelRefresh(); task.GetAwaiter().GetResult();
                Check(calls == 1 && Field<List<AnalysisRun>>(w, "_analysisRuns").Single().Outcome == "cancelled", "cancellation continued to the remaining models or became a failure");
                Check(Field<Button>(w, "_refresh").IsEnabled && Field<bool>(w, "_sparkAutoPaused"), "cancellation left controls disabled or restarted automatic calls");
                Check(!Field<System.Windows.Threading.DispatcherTimer>(w, "_analysisProgressTimer").IsEnabled, "cancelled analysis retained progress updates");
            } finally { w.Close(); }
        }
    }
}