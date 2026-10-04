using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Threading.Tasks;
namespace DeskWidget
{
    internal static class PredictionJournalTests
    {
        private static void RecordSizeBounds(string work, Action<bool, string> check)
        {
            string folder = Path.Combine(work, "record-size-bounds"); Directory.CreateDirectory(folder);
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var save = typeof(PredictionJournal).GetMethod("Save", flags);
            var read = typeof(PredictionJournal).GetMethod("Read", flags);
            var load = typeof(RuleSkill).GetMethod("Load", flags);
            var doc = new XmlDocument(); var root = doc.CreateElement("prediction"); doc.AppendChild(root);
            // Maximum target-feed counts plus a full submitted AI batch. Escaping expands each
            // allowed 6,000-character body, so article-count bounds alone do not bound file bytes.
            for (int i = 0; i < 138; i++) {
                var article = doc.CreateElement("article");
                article.InnerText = "Synthetic target article " + i + "\n" + new string('&', i < 60 ? 6000 : 1800);
                root.AppendChild(article);
            }
            var input = doc.CreateElement("aiNewsInput"); root.AppendChild(input);
            for (int i = 0; i < 100; i++) {
                var article = doc.CreateElement("marketArticle");
                article.InnerText = "Synthetic submitted article " + i + "\n" + new string('&', 6000);
                input.AppendChild(article);
            }
            string path = Path.Combine(folder, "escaped-evidence.xml");
            save.Invoke(null, new object[] { doc, path });
            long length = new FileInfo(path).Length;
            check(length > 4 * 1024 * 1024 && length <= PredictionJournal.MaxRecordBytes,
                "escaped evidence fixture does not exercise the expanded shared record bound");
            var journal = (XmlDocument)read.Invoke(null, new object[] { path });
            var ledger = (XmlDocument)load.Invoke(null, new object[] { path });
            check(journal != null && journal.DocumentElement.InnerText.Replace("\r\n", "\n") == root.InnerText,
                "journal cannot read its saved bounded full-evidence record exactly");
            check(ledger != null && ledger.DocumentElement.InnerText.Replace("\r\n", "\n") == root.InnerText,
                "rule ledger and journal disagree on the saved record size bound");
            check(!File.Exists(path + ".tmp"), "successful bounded record save left a temporary file");

            var oversized = new XmlDocument(); var tooLarge = oversized.CreateElement("prediction"); oversized.AppendChild(tooLarge);
            tooLarge.InnerText = new string('&', PredictionJournal.MaxRecordBytes / 5 + 1);
            string rejectedPath = Path.Combine(folder, "oversized-evidence.xml");
            bool rejected = false;
            try { save.Invoke(null, new object[] { oversized, rejectedPath }); }
            catch (System.Reflection.TargetInvocationException ex) { rejected = ex.InnerException is InvalidDataException; }
            check(rejected, "oversized serialized record was not rejected explicitly");
            check(!File.Exists(rejectedPath) && !File.Exists(rejectedPath + ".tmp"),
                "rejected oversized record left an unreadable final or temporary file");
        }

        internal static int Run(string work)
        {
            int checks = 0;
            Action<bool, string> check = (ok, why) => { if (!ok) throw new Exception(why); checks++; };
            check(new Config(Path.Combine(work, "journal-config.json")).SparkRefreshIntervalSec == 0, "new config not manual");
            check(DollarSpark.Arguments(work, "gpt-6-astra").Contains("--model gpt-6.1-sol "), "retired Astra setting did not route to Sol");
            check(!DollarSpark.Arguments(work, "bad --flag").Contains("bad --flag"), "model injection");
            check(DollarSpark.ModelId(DollarSpark.LegacyModel) == DollarSpark.Model &&
                DollarSpark.ModelName(DollarSpark.LegacyModel) == "Spark" && DollarSpark.ModelName(DollarSpark.Model) == "Sol 6.1",
                "old Spark setting or history relabeled incorrectly");
            string oldConfigPath = Path.Combine(work, "old-spark-config.json");
            File.WriteAllText(oldConfigPath, "{\"version\":\"1.047\",\"analysisModel\":\"" + DollarSpark.LegacyModel + "\"}");
            var oldConfig = new Config(oldConfigPath); oldConfig.Load();
            check(oldConfig.AnalysisModel == DollarSpark.Model, "old Spark selection did not migrate to Sol");
            foreach (string retired in new[] { DollarSpark.AstraModel, DollarSpark.LunaModel }) {
                string retiredPath = Path.Combine(work, "retired-" + retired + ".json");
                File.WriteAllText(retiredPath, "{\"analysisModel\":\"" + retired + "\",\"analysisReviewHistory\":true}");
                var migrated = new Config(retiredPath); migrated.Load();
                check(migrated.AnalysisModel == DollarSpark.Model && migrated.AnalysisReviewHistory, "retired selection migration lost Sol or history opt-in");
                migrated.Save(); var saved = new Config(retiredPath); saved.Load();
                check(saved.AnalysisModel == DollarSpark.Model && !File.ReadAllText(retiredPath).Contains(retired), "retired model selection survived saving");
                check(DollarSpark.HistoricalModel(retired), "retired historical model rejected");
            }
            // Monday in Korea: the observation fixture must not assume a Friday forecast matures on Saturday.
            DateTime now = new DateTime(2026, 9, 28, 4, 0, 0, DateTimeKind.Utc);
            var quote = new Quote { Ok = true, Value = 100, IdentityKey = "fx:FX_USDKRW", Source = "same-feed", ReceivedUtc = now };
            check(!PredictionJournal.Fresh(quote, now.AddMinutes(6)), "stale quote admitted");
            check(!PredictionJournal.Fresh(quote, now.AddSeconds(-1)), "future quote admitted");
            var result = new DollarAnalysisResult { Quote = quote, CheckedUtc = now };
            var p = new DollarPattern { Horizon = 1, Up = 40, LatestDate = now.Date, ReferenceRate = 100 };
            p.Returns.Add(0.01); result.Patterns.Add(p);
            PredictionJournal.Record(result, now);
            var paths = Directory.GetFiles(PredictionJournal.Folder, "*.xml");
            check(paths.Length == 1, "forecast not recorded");
            string original = File.ReadAllText(paths[0]);
            var legacyRecord = new XmlDocument();
            legacyRecord.LoadXml(original.Replace("model=\"basic\"", "model=\"" + DollarSpark.LegacyModel + "\""));
            var validRecord = typeof(PredictionJournal).GetMethod("ValidRecord", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            check((bool)validRecord.Invoke(null, new object[] { legacyRecord.DocumentElement }), "old Spark forecast rejected after model migration");
            quote.ReceivedUtc = now.AddDays(1).AddSeconds(-1); quote.Value = 102;
            PredictionJournal.Observe(quote, quote.ReceivedUtc);
            check(Directory.GetFiles(PredictionJournal.Folder, "*.score.xml").Length == 0, "premature score");
            quote.ReceivedUtc = now.AddDays(1); quote.Source = "other-feed";
            PredictionJournal.Observe(quote, quote.ReceivedUtc);
            check(Directory.GetFiles(PredictionJournal.Folder, "*.score.xml").Length == 0, "different feed scored");
            quote.Source = "same-feed"; quote.ReceivedUtc = now.AddDays(1).AddHours(7);
            PredictionJournal.Observe(quote, quote.ReceivedUtc);
            check(Directory.GetFiles(PredictionJournal.Folder, "*.score.xml").Length == 0, "late quote scored");
            quote.ReceivedUtc = now.AddDays(1).AddMinutes(1);
            PredictionJournal.Observe(quote, quote.ReceivedUtc);
            var scores = Directory.GetFiles(PredictionJournal.Folder, "*.score.xml");
            check(scores.Length == 1, "mature forecast missing score");
            var doc = new XmlDocument(); doc.Load(scores[0]);
            check(doc.DocumentElement.GetAttribute("error") == "1" && doc.DocumentElement.GetAttribute("baselineError") == "2", "wrong errors");
            quote.Value = 80; PredictionJournal.Observe(quote, quote.ReceivedUtc);
            var again = new XmlDocument(); again.Load(scores[0]);
            check(again.DocumentElement.GetAttribute("actual") == "102", "score overwritten");
            check(File.ReadAllText(paths[0]) == original, "forecast rewritten with future data");
            quote.Value = 100; quote.ReceivedUtc = now;
            const string submittedMarket = "{\"as_of_utc\":\"fixture\",\"history\":{\"last_completed_value\":99}}";
            result.Spark = new DollarSparkResult { CheckedUtc = now, MarketSnapshot = submittedMarket };
            var broad = new DollarNews { Title = "Unexpected settlement network interruption", Context = "Synthetic background evidence.",
                Source = "Fixture", Url = "https://news.google.com/articles/journal-market", PublishedUtc = now.AddDays(-3) };
            result.MarketNews.Add(broad);
            result.Spark.SubmittedNews.Add(broad);
            result.Rates.Add(new DollarRate { Date = now.Date.AddDays(-1), Value = 101 });
            PredictionJournal.Record(result, now);
            var capturedPath = Directory.GetFiles(PredictionJournal.Folder, "*.xml").First(x => x != paths[0] && !x.EndsWith(".score.xml"));
            var captured = new XmlDocument(); captured.Load(capturedPath);
            check(captured.SelectSingleNode("/prediction/aiMarketInput").InnerText == submittedMarket,
                "journal recomputed AI input from later prices instead of preserving submitted snapshot");
            var submitted = (XmlElement)captured.SelectSingleNode("/prediction/aiNewsInput/marketArticle");
            check(submitted != null && submitted.GetAttribute("id") == "1" && submitted.GetAttribute("recency") == "background" &&
                submitted.GetAttribute("origin") == "broad-market" && submitted.InnerText.Replace("\r\n", "\n") == DollarSpark.SourceText(broad),
                "submitted broad evidence lost its original text, recency or order");
            check(captured.SelectNodes("/prediction/article").Count == 0 &&
                ((XmlElement)captured.SelectSingleNode("/prediction/aiNewsInput")).GetAttribute("policy") == DollarSpark.InputPolicy,
                "broad AI evidence leaked into the basic rule ledger or lost its input policy");
            check(Config.ScoringEra("1.050") != Config.ScoringEra(Config.AppVersion), "broad market policy pooled with earlier forecasts");
            check(Config.ScoringEra("1.048") != Config.ScoringEra(Config.AppVersion), "new forecast inputs pooled with old scoring era");
            RecordSizeBounds(work, check);
            var brief = new BriefTextBlock { Text = "한국 기준금리 비트코인 삼성전자 브리프 요약입니다. 반대 근거도 함께 표시합니다.", FontSize = 16, Foreground = System.Windows.Media.Brushes.White };
            check(brief.Lines(brief.TextWidth("한국 기준금리") + 1)[0] == "한국 기준금리", "Korean word split");
            check(brief.Lines(brief.TextWidth("비트코인") - 1).Contains("비트코인"), "narrow token split");
            check(BriefTextBlock.Tracking == -0.05, "tracking changed");
            brief.Measure(new System.Windows.Size(340, double.PositiveInfinity));
            brief.Arrange(new System.Windows.Rect(0, 0, 340, brief.DesiredSize.Height)); brief.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(340, (int)Math.Ceiling(brief.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(brief); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(work, "brief-tracking.png"))) encoder.Save(stream);
            var stack = new System.Windows.Controls.StackPanel { Width = 80, Background = System.Windows.Media.Brushes.Black };
            var dock = typeof(WidgetWindow).GetMethod("DockLine", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic, null,
                new[] { typeof(string), typeof(double), typeof(System.Windows.Media.Brush), typeof(bool) }, null);
            foreach (string text in new[] { "한국 기준금리", "2.75 %", "26.07", "비트코인", "1.07억", "-0.42%", "삼성전자", "276,500", "+2.41%" })
                stack.Children.Add((System.Windows.UIElement)dock.Invoke(null, new object[] { text, 16.0, System.Windows.Media.Brushes.White, true }));
            stack.Measure(new System.Windows.Size(80, double.PositiveInfinity)); stack.Arrange(new System.Windows.Rect(0, 0, 80, stack.DesiredSize.Height)); stack.UpdateLayout();
            var dockBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(80, (int)Math.Ceiling(stack.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32); dockBitmap.Render(stack);
            var dockEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); dockEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(dockBitmap));
            using (var stream = File.Create(Path.Combine(work, "dock-tracking.png"))) dockEncoder.Save(stream);
            Console.WriteLine("PREVIEW: " + Path.Combine(work, "dock-tracking.png"));
            return checks;
        }
    }
}
