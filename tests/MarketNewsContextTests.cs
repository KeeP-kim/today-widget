using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DeskWidget
{
    internal static class MarketNewsContextTests
    {
        private static int checks;
        private static void Check(bool value, string why)
        { if (!value) throw new Exception("Market news context: " + why); checks++; }
        private static DollarNews Article(string title, string source, DateTime when, string id)
        {
            return new DollarNews { Title = title, Source = source, PublishedUtc = when,
                Url = "https://news.google.com/articles/" + id, Context = "" };
        }
        private static string Rss(params DollarNews[] news)
        {
            var doc = new System.Xml.XmlDocument(); var rss = doc.CreateElement("rss"); doc.AppendChild(rss);
            var channel = doc.CreateElement("channel"); rss.AppendChild(channel);
            foreach (var n in news)
            {
                var item = doc.CreateElement("item"); channel.AppendChild(item);
                string[] names = { "title", "link", "source", "pubDate" };
                string[] values = { n.Title, n.Url, n.Source, n.PublishedUtc.ToString("r", CultureInfo.InvariantCulture) };
                for (int i = 0; i < names.Length; i++) { var element = doc.CreateElement(names[i]); element.InnerText = values[i]; item.AppendChild(element); }
            }
            return doc.OuterXml;
        }
        internal static int Run(string work)
        {
            checks = 0;
            var now = new DateTime(2026, 9, 24, 4, 0, 0, DateTimeKind.Utc);
            var target = new PredictionTarget(new SymbolDef(SourceKind.Coin, "KRW-BTC", "Bitcoin"));
            var unknown = Article("Major shipping route closed after a regional incident", "BBC", now.AddHours(-1), "unknown");
            Check(DollarAnalysis.ParseNews(Rss(unknown), false, now, null, target).Count == 0, "fixture unexpectedly passes old title filter");
            var broad = DollarAnalysis.ParseNews(Rss(unknown), false, now, null, null, true);
            Check(broad.Count == 1 && broad[0].Title == unknown.Title, "unfamiliar current event discarded");
            Check(broad[0].Factors.Count == 0 && broad[0].EvidenceWeight == 0 && broad[0].Target == null, "broad context was assigned rule scores or an asset");
            var recent = Article("Unfamiliar financing disturbance continues", "Second", now.AddDays(-6), "ongoing");
            var boundary = Article("Unfamiliar regional events remain unresolved", "Third", now.AddDays(-7), "boundary");
            var tooOld = Article("Expired regional event has no current report", "Fourth", now.AddDays(-7).AddSeconds(-1), "old");
            var future = Article("Unpublished regional event headline", "Fifth", now.AddSeconds(1), "future");
            var dates = DollarAnalysis.ParseNews(Rss(recent, boundary, tooOld, future), false, now, null, null, true);
            Check(dates.Count == 2 && dates.Any(n => n.Url == recent.Url) && dates.Any(n => n.Url == boundary.Url), "seven-day boundary or future filter changed");
            Check(DollarAnalysis.ParseNews(Rss(unknown).Replace(unknown.PublishedUtc.ToString("r", CultureInfo.InvariantCulture), "not-a-date"), false, now, null, null, true).Count == 0, "malformed date accepted");
            var unsafeLink = Article("Unfamiliar local device report", "BBC", now, "bad"); unsafeLink.Url = "http://127.0.0.1/internal";
            Check(DollarAnalysis.ParseNews(Rss(unsafeLink), false, now, null, null, true).Count == 0, "broad path bypassed URL policy");
            Check(DollarAnalysis.ParseNews("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///C:/secret'>]><rss><channel>&x;</channel></rss>", false, now, null, null, true) == null, "broad parser permitted external entities");
            var shuffled = Enumerable.Range(0, 80).Select(i => Article("New regional development number " + i, "BBC", now.AddMinutes(-80 + i), "order-" + i)).ToArray();
            var latest = DollarAnalysis.ParseNews(Rss(shuffled), false, now, null, null, true);
            Check(latest.Count == 24 && latest[0].Url.EndsWith("order-79"), "RSS order hid later current articles");

            var repeated = Article(unknown.Title + " - BBC", "BBC", now.AddMinutes(-1), "richer");
            repeated.Url = "https://www.bbc.com/news/articles/richer"; repeated.Context = "A detailed source account of the newly discovered event."; repeated.BodyRead = true;
            var merged = MarketNewsContext.Merge(new[] { unknown, recent, tooOld }, new[] { repeated, future }, now);
            Check(merged.Count == 2, "merge did not deduplicate and prune");
            var kept = merged.Single(n => n.Title == repeated.Title);
            Check(kept.PublishedUtc == unknown.PublishedUtc && kept.BodyRead && kept.Url == repeated.Url, "repeat refreshed the timestamp or discarded the fuller publisher copy");
            Check(!object.ReferenceEquals(kept, repeated) && !object.ReferenceEquals(kept, unknown), "cache shares mutable rule objects");
            Check(unknown.Context == "" && unknown.PublishedUtc == now.AddHours(-1), "merge altered caller data");
            var changedTitle = Article("Updated unfamiliar regional incident account", "BBC", now, "richer"); changedTitle.Url = repeated.Url;
            changedTitle.Context = "The earlier incident account was withdrawn.";
            var corrected = MarketNewsContext.Merge(merged, new[] { changedTitle }, now);
            Check(corrected.Count == 2, "same URL with a new title inflated context");
            Check(corrected.Any(n => n.Title == changedTitle.Title && n.Context == changedTitle.Context && !n.BodyRead && n.PublishedUtc == changedTitle.PublishedUtc), "a changed headline lost its correction or inherited the old body");
            var halted = Article("Major shipping route halted after a regional incident", "BBC", now.AddDays(-3), "revised-report");
            halted.Context = "Traffic has stopped."; halted.BodyRead = true;
            var resumed = Article("Major shipping route resumed after regional talks", "BBC", now.AddMinutes(-20), "revised-report");
            resumed.Context = "The route reopened after an agreement.";
            var revised = MarketNewsContext.Merge(new[] { halted }, new[] { resumed }, now);
            Check(revised.Count == 1 && revised[0].Title == resumed.Title && revised[0].PublishedUtc == resumed.PublishedUtc && DollarSpark.IsCurrent(revised[0], now), "a fresh reversal inherited the superseded report's old date");
            var replay = Article("Major shipping route still halted during talks", "BBC", now.AddDays(-1), "revised-report");
            replay.Context = new string('x', 500); replay.BodyRead = true;
            revised = MarketNewsContext.Merge(revised, new[] { replay, halted }, now);
            Check(revised.Count == 1 && revised[0].Title == resumed.Title && revised[0].Context == resumed.Context && revised[0].PublishedUtc == resumed.PublishedUtc && !revised[0].BodyRead, "older fuller reports replaced the newer correction");
            Check(MarketNewsContext.Merge(merged, null, now.AddDays(8)).Count == 0, "cached context never expires");
            var sourceA = Enumerable.Range(0, 30).Select(i => Article("Busy publisher account number " + i, "A", now.AddSeconds(-i), "a-" + i));
            var diverse = sourceA.Concat(new[] { recent, unknown });
            Check(MarketNewsContext.Select(diverse, now, 3).Select(n => n.Source).Distinct().Count() == 3, "one publisher crowded out other sources");
            Check(MarketNewsContext.Select(diverse, now, 0).Count == 0, "zero selection limit ignored");
            Check(MarketNewsContext.Select(Enumerable.Range(0, 600).Select(i => Article("Bounded publication article " + i, "A", now, "cap-" + i)), now, 900).Count == MarketNewsContext.MaxItems, "context bound ignored");
            var busy = Enumerable.Range(0, 600).Select(i => Article("Latest new event number " + i, "A", now.AddMinutes(-i), "busy-" + i)).Concat(new[] { recent }).ToList();
            var retained = MarketNewsContext.Merge(null, busy, now);
            Check(retained.Count == MarketNewsContext.MaxItems && retained.Any(n => n.Url == recent.Url), "busy current coverage erased rolling background from cache");
            var submitted = MarketNewsContext.SelectRolling(retained, now, 160);
            Check(submitted.Count == 160 && submitted.Any(n => n.Url == recent.Url), "busy current coverage erased background before AI selection");

            string path = Path.Combine(work, "context-test.xml");
            MarketNewsContext.Save(path, merged, now, CancellationToken.None);
            var restored = MarketNewsContext.Read(path, now, CancellationToken.None);
            Check(restored.Count == 2 && restored.Any(n => n.PublishedUtc == unknown.PublishedUtc && n.BodyRead), "cache lost original dates or body state");
            Check(restored.All(n => n.Target == null && n.Factors.Count == 0), "cache persisted target-specific rules");
            Check(MarketNewsContext.Read(path, now.AddDays(8), CancellationToken.None).Count == 0, "cache reload revived stale articles");
            MarketNewsContext.Save(path, new[] { recent }, now, CancellationToken.None);
            Check(MarketNewsContext.Read(path, now, CancellationToken.None).Count == 1, "atomic replacement failed");
            Check(Directory.GetFiles(work, "context-test.xml.*.tmp").Length == 0, "cache left temporary writes");
            File.WriteAllText(path, "<!DOCTYPE marketNews [<!ENTITY x SYSTEM 'file:///C:/secret'>]><marketNews schema='1'>&x;</marketNews>", new UTF8Encoding(false));
            Check(MarketNewsContext.Read(path, now, CancellationToken.None).Count == 0, "cache permitted external entities");
            File.WriteAllText(path, "<marketNews schema='1'><article", new UTF8Encoding(false));
            Check(MarketNewsContext.Read(path, now, CancellationToken.None).Count == 0, "truncated cache disrupted analysis");
            using (var oversized = new FileStream(path, FileMode.Create)) oversized.SetLength(MarketNewsContext.MaxBytes + 1L);
            Check(MarketNewsContext.Read(path, now, CancellationToken.None).Count == 0, "oversized cache accepted");
            var large = Enumerable.Range(0, MarketNewsContext.MaxItems).Select(i => {
                var article = Article("Large UTF8 context article " + i, "A", now, "utf8-" + i);
                article.Context = new string('\uac00', 6000); article.BodyRead = true; return article;
            }).Concat(new[] { recent }).ToList();
            MarketNewsContext.Save(path, large, now, CancellationToken.None);
            var fitted = MarketNewsContext.Read(path, now, CancellationToken.None);
            Check(new FileInfo(path).Length <= MarketNewsContext.MaxBytes && fitted.Count > 0 && fitted.Count < MarketNewsContext.MaxItems, "byte pressure prevented all fresh cache updates");
            Check(fitted.Any(n => n.Url == recent.Url) && fitted.Where(n => n.BodyRead).All(n => n.Context.Length == 6000), "byte pressure erased background or rewrote evidence text");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); bool rejected = false;
                try { MarketNewsContext.Read(path, now, cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
                Check(rejected, "cache read swallowed cancellation");
                rejected = false;
                try { MarketNewsContext.Save(path, merged, now, cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
                Check(rejected, "cache write swallowed cancellation");
            }

            var result = new DollarAnalysisResult { Target = target, DomesticAvailable = true, GlobalAvailable = true };
            var regular = Article("Bitcoin rises after a new ETF inflow", "Reuters", now, "regular"); regular.Target = target;
            result.News.Add(regular);
            var baseline = DollarAnalysis.Score(result, 1, now);
            result.MarketNews.AddRange(merged);
            var after = DollarAnalysis.Score(result, 1, now);
            Check(after.Value == baseline.Value && after.ArticleCount == baseline.ArticleCount && after.ReviewedCount == baseline.ReviewedCount, "broad evidence changed basic rule scores");
            var snapshot = result.Snapshot(); snapshot.MarketNews.Clear();
            Check(result.MarketNews.Count == merged.Count, "snapshot shares mutable market list");
            return checks;
        }

        internal static int Live(string work)
        {
            checks = 0;
            var now = DateTime.UtcNow;
            var direct = DollarNewsSources.Feeds.Select(url => Net.GetTextAsync(url, CancellationToken.None)).ToArray();
            var general = MarketNewsContext.StartSearches(CancellationToken.None);
            System.Threading.Tasks.Task.WhenAll(direct.Concat(general)).GetAwaiter().GetResult();
            string previous = Program.BaseDir; int bodyLimit = DollarNewsSources.BodyLimit;
            try
            {
                Program.BaseDir = work; DollarNewsSources.BodyLimit = 0;
                var result = new DollarAnalysisResult();
                MarketNewsContext.CompleteAsync(result, direct.Select(t => t.Result).ToArray(), general.Select(t => t.Result).ToArray(), now, CancellationToken.None).GetAwaiter().GetResult();
                Check(result.MarketNewsFeedsAvailable > 0, "no live discovery feed was available");
                Check(result.MarketNews.Count > 0, "available live feeds yielded no valid dated context");
                Check(result.MarketNews.All(n => n.PublishedUtc <= now && n.PublishedUtc >= now.AddDays(-7) && DollarAnalysis.IsNewsLink(n.Url)), "live discovery bypassed boundaries");
                Check(result.News.Count == 0, "live context populated rule inputs");
                Console.WriteLine("Live context: feeds=" + result.MarketNewsFeedsAvailable + "/" + result.MarketNewsFeedsExpected + ", articles=" + result.MarketNews.Count + ", publishers=" + result.MarketNews.Select(n => n.Source).Distinct().Count());
            }
            finally { Program.BaseDir = previous; DollarNewsSources.BodyLimit = bodyLimit; }
            return checks;
        }
    }
}
