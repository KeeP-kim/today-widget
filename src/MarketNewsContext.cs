using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace DeskWidget
{
    // A bounded rolling evidence inbox, not a rule score or a claim that an old event is still active.
    internal static class MarketNewsContext
    {
        internal const int MaxAgeDays = 7;
        internal const int MaxItems = 480;
        internal const int MaxBytes = 8 * 1024 * 1024;
        private static readonly object Gate = new object();
        internal static string CachePath { get { return Path.Combine(Program.BaseDir, "market-news-context.xml"); } }

        internal static Task<string>[] StartSearches(CancellationToken ct)
        {
            // General discovery remains open; paired Korean/English desks also watch policy and calendars.
            string[] queries = { "(세계 경제 OR 국제 금융 OR 금융 시장) when:7d", "(global economy OR financial markets) when:7d", "(통화 정책 OR 경제 지표 OR 외환 수급) when:1d", "(cryptocurrency markets OR digital asset funds) when:1d",
                "(외환당국 OR 환율 안정 OR 외환시장 OR 통화정책 회의 OR 경제지표 발표) when:1d",
                "(Treasury dollar policy OR foreign exchange policy OR central bank decision OR economic calendar) when:1d" };
            return queries.Select((q, i) => Net.GetTextAsync("https://news.google.com/rss/search?q=" + Uri.EscapeDataString(q) +
                (i % 2 == 0 ? "&hl=ko&gl=KR&ceid=KR:ko" : "&hl=en-US&gl=US&ceid=US:en"), ct)).ToArray();
        }

        internal static async Task CompleteAsync(DollarAnalysisResult result, string[] direct, string[] searches, DateTime now, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var incoming = new List<DollarNews>();
            result.MarketNewsFeedsExpected = direct.Length + searches.Length;
            result.MarketNewsFeedsAvailable = 0;
            for (int i = 0; i < direct.Length + searches.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                bool publisherFeed = i < direct.Length;
                int searchIndex = i - direct.Length;
                var parsed = DollarAnalysis.ParseNews(publisherFeed ? direct[i] : searches[searchIndex],
                    publisherFeed ? i < 4 : searchIndex % 2 == 0, now, publisherFeed ? DollarNewsSources.Publisher(i) : null, null, true);
                if (parsed == null) continue;
                result.MarketNewsFeedsAvailable++;
                incoming.AddRange(parsed);
            }
            List<DollarNews> context;
            lock (Gate) { context = Merge(Read(CachePath, now, ct), incoming, now); }
            // Reuse bodies already fetched for the normal feed, without sharing mutable rule objects.
            foreach (var article in context)
            {
                var known = result.News.FirstOrDefault(n => n.BodyRead && (n.Url == article.Url || Key(n) == Key(article)));
                if (known != null) { article.Context = known.Context; article.BodyRead = true; }
            }
            int extraReads = Math.Min(6, Math.Max(0, DollarNewsSources.BodyLimit));
            var chosen = Select(context.Where(n => !n.BodyRead && DollarNewsSources.IsArticle(n.Url) &&
                !result.News.Any(known => known.Url == n.Url || Key(known) == Key(n))), now, extraReads);
            await Task.WhenAll(chosen.Select(async n => {
                ct.ThrowIfCancellationRequested();
                try {
                    string body = DollarNewsSources.ExtractBody(await Net.GetTextAsync(n.Url, ct).ConfigureAwait(false));
                    if (body.Length > 0) { n.Context = body; n.BodyRead = true; }
                } catch (OperationCanceledException) { throw; }
                  catch (Exception) { }
            }));
            ct.ThrowIfCancellationRequested();
            lock (Gate)
            {
                // A different analysis window may have added articles while bodies were loading.
                context = Merge(Read(CachePath, now, ct), context, now);
                Save(CachePath, context, now, ct);
            }
            result.MarketNews = SelectRolling(context, now, 160);
        }

        private static string Key(DollarNews n)
        {
            return Regex.Replace((n.Title ?? "").Split(new[] { " - " }, StringSplitOptions.None)[0], @"[^\p{L}\p{N}]", "").ToLowerInvariant();
        }
        private static bool Valid(DollarNews n, DateTime now)
        {
            return n != null && !string.IsNullOrWhiteSpace(n.Title) && n.Title.Length >= 8 && n.Title.Length <= 240 &&
                !string.IsNullOrWhiteSpace(n.Source) && n.Source.Length <= 60 && n.Url != null && n.Url.Length <= 4096 &&
                DollarAnalysis.IsNewsLink(n.Url) && n.PublishedUtc != DateTime.MinValue && n.PublishedUtc <= now &&
                n.PublishedUtc >= now.AddDays(-MaxAgeDays) && (n.Context == null || n.Context.Length <= 6000) && Key(n).Length > 0;
        }
        private static DollarNews Copy(DollarNews n)
        {
            return new DollarNews { Title = n.Title, Source = n.Source, Url = n.Url, PublishedUtc = n.PublishedUtc,
                Context = n.Context ?? "", Domestic = n.Domestic, BodyRead = n.BodyRead };
        }

        internal static List<DollarNews> Merge(IEnumerable<DollarNews> existing, IEnumerable<DollarNews> incoming, DateTime now)
        {
            var merged = new List<DollarNews>();
            foreach (var n in (existing ?? Enumerable.Empty<DollarNews>()).Concat(incoming ?? Enumerable.Empty<DollarNews>()).Where(n => Valid(n, now)))
            {
                string key = Key(n);
                var previous = merged.FirstOrDefault(p => p.Url == n.Url || Key(p) == key);
                if (previous == null) { merged.Add(Copy(n)); continue; }
                bool changedHeadline = n.Url == previous.Url && key != Key(previous);
                // A newer provider timestamp and changed headline identify a revised report. An
                // out-of-order copy must not restore the superseded headline, even with a fuller body.
                if (changedHeadline)
                {
                    if (n.PublishedUtc > previous.PublishedUtc) { merged.Remove(previous); merged.Add(Copy(n)); }
                    continue;
                }
                // Merely receiving the same article again does not make its publication time current.
                DateTime published = previous.PublishedUtc < n.PublishedUtc ? previous.PublishedUtc : n.PublishedUtc;
                bool richer = n.BodyRead && !previous.BodyRead || n.BodyRead == previous.BodyRead &&
                    (DollarNewsSources.IsArticle(n.Url) && !DollarNewsSources.IsArticle(previous.Url) ||
                     DollarNewsSources.IsArticle(n.Url) == DollarNewsSources.IsArticle(previous.Url) && (n.Context ?? "").Length > (previous.Context ?? "").Length);
                if (richer) { merged.Remove(previous); previous = Copy(n); merged.Add(previous); }
                previous.PublishedUtc = published;
            }
            return SelectRolling(merged, now, MaxItems);
        }

        internal static List<DollarNews> SelectRolling(IEnumerable<DollarNews> news, DateTime now, int limit)
        {
            limit = Math.Max(0, Math.Min(MaxItems, limit));
            if (news == null || limit == 0) return new List<DollarNews>();
            var candidates = news.Where(n => Valid(n, now)).ToList();
            int backgroundSlots = limit / 5;
            var selected = Select(candidates.Where(n => n.PublishedUtc >= now.AddHours(-24)), now, limit - backgroundSlots);
            var titles = new HashSet<string>(selected.Select(Key), StringComparer.Ordinal);
            var urls = new HashSet<string>(selected.Select(n => n.Url), StringComparer.Ordinal);
            selected.AddRange(Select(candidates.Where(n => n.PublishedUtc < now.AddHours(-24) && !titles.Contains(Key(n)) && !urls.Contains(n.Url)), now, backgroundSlots));
            titles.UnionWith(selected.Select(Key)); urls.UnionWith(selected.Select(n => n.Url));
            selected.AddRange(Select(candidates.Where(n => !titles.Contains(Key(n)) && !urls.Contains(n.Url)), now, limit - selected.Count));
            return selected.OrderByDescending(n => n.PublishedUtc).ToList();
        }

        internal static List<DollarNews> Select(IEnumerable<DollarNews> news, DateTime now, int limit)
        {
            limit = Math.Max(0, Math.Min(MaxItems, limit));
            var result = new List<DollarNews>();
            if (limit == 0 || news == null) return result;
            var groups = news.Where(n => Valid(n, now)).OrderByDescending(n => n.PublishedUtc)
                .GroupBy(n => n.Source.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new Queue<DollarNews>(g)).ToList();
            var titles = new HashSet<string>(StringComparer.Ordinal);
            var urls = new HashSet<string>(StringComparer.Ordinal);
            // Round-robin prevents one busy publisher from hiding a less familiar new issue.
            while (result.Count < limit && groups.Any(g => g.Count > 0))
                foreach (var group in groups)
                {
                    if (result.Count >= limit) break;
                    while (group.Count > 0)
                    {
                        var n = group.Dequeue(); string key = Key(n);
                        if (titles.Contains(key) || urls.Contains(n.Url)) continue;
                        titles.Add(key); urls.Add(n.Url); result.Add(n); break;
                    }
                }
            return result;
        }

        internal static List<DollarNews> Read(string path, DateTime now, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) return new List<DollarNews>();
                var doc = new XmlDocument { XmlResolver = null };
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null, MaxCharactersInDocument = MaxBytes })) doc.Load(reader);
                var root = doc.DocumentElement;
                if (root == null || root.Name != "marketNews" || root.GetAttribute("schema") != "1") return new List<DollarNews>();
                var items = root.SelectNodes("article");
                if (items.Count > MaxItems) return new List<DollarNews>();
                var news = new List<DollarNews>();
                foreach (XmlElement item in items)
                {
                    ct.ThrowIfCancellationRequested();
                    DateTimeOffset time;
                    if (!DateTimeOffset.TryParse(item.GetAttribute("published"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out time)) continue;
                    news.Add(new DollarNews { Title = item.GetAttribute("title"), Url = item.GetAttribute("url"),
                        Source = item.GetAttribute("source"), PublishedUtc = time.UtcDateTime, Context = item.InnerText,
                        Domestic = item.GetAttribute("domestic") == "true", BodyRead = item.GetAttribute("body") == "true" });
                }
                return Merge(null, news, now);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return new List<DollarNews>(); }
        }

        internal static void Save(string path, IEnumerable<DollarNews> news, DateTime now, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var retained = Merge(null, news, now);
                byte[] bytes;
                while (true)
                {
                    var doc = new XmlDocument { XmlResolver = null };
                    var root = doc.CreateElement("marketNews"); root.SetAttribute("schema", "1"); doc.AppendChild(root);
                    foreach (var n in retained)
                    {
                        ct.ThrowIfCancellationRequested();
                        var item = doc.CreateElement("article");
                        item.SetAttribute("title", n.Title); item.SetAttribute("source", n.Source); item.SetAttribute("url", n.Url);
                        item.SetAttribute("published", n.PublishedUtc.ToString("o", CultureInfo.InvariantCulture));
                        item.SetAttribute("domestic", n.Domestic ? "true" : "false"); item.SetAttribute("body", n.BodyRead ? "true" : "false");
                        item.InnerText = n.Context ?? ""; root.AppendChild(item);
                    }
                    using (var buffer = new MemoryStream())
                    {
                        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false })) doc.Save(writer);
                        if (buffer.Length <= MaxBytes) { bytes = buffer.ToArray(); break; }
                    }
                    // Keep exact evidence text. Under byte pressure retain fewer complete articles,
                    // with the same current/background reservations and publisher diversity.
                    retained = SelectRolling(retained, now, retained.Count * 3 / 4);
                }
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(temporary, bytes);
                ct.ThrowIfCancellationRequested();
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
