using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Threading.Tasks;
namespace DeskWidget
{
    internal static class PredictionJournalTests
    {
        internal static int Run(string work)
        {
            int checks = 0;
            Action<bool, string> check = (ok, why) => { if (!ok) throw new Exception(why); checks++; };
            check(new Config(Path.Combine(work, "journal-config.json")).SparkRefreshIntervalSec == 0, "new config not manual");
            check(DollarSpark.Arguments(work, "gpt-6-astra").Contains("--model gpt-6-astra "), "Astra not routed");
            check(!DollarSpark.Arguments(work, "bad --flag").Contains("bad --flag"), "model injection");
            var window = new DollarAnalysisWindow(new Config(Path.Combine(work, "journal-config.json")), ct => Task.FromResult<DollarAnalysisResult>(null));
            window.ChangeModel("gpt-6-astra"); window.Close();
            var saved = new Config(Path.Combine(work, "journal-config.json")); saved.Load(); check(saved.AnalysisModel == "gpt-6-astra", "model choice not saved");
            DateTime now = DateTime.UtcNow;
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
