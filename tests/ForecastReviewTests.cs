using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;

namespace DeskWidget
{
    internal static class ForecastReviewTests
    {
        private static int checks;
        private static void Check(bool ok, string why) { if (!ok) throw new Exception("Review: " + why); checks++; }
        private static void Fixture(string folder, string name, DateTime created, DateTime due, DateTime? received,
            string source = "신한은행", string model = "basic", string version = "1.055")
        {
            string record = "<prediction schema='3' identity='fx:FX_USDKRW|SHB' source='" + source + "' version='" + version +
                "' anchor='100' created='" + created.ToString("o") + "'><forecast model='" + model + "' horizon='1' value='110' threshold='0.01' due='" + due.ToString("o") +
                "'>당시 상승 설명 · &quot;과거 문장&quot;</forecast></prediction>";
            string path = Path.Combine(folder, name + ".xml"); File.WriteAllText(path, record, new UTF8Encoding(false));
            if (!received.HasValue) return;
            string score = "<score schema='2' identity='fx:FX_USDKRW|SHB' source='" + source + "' version='" + version + "' model='" + model +
                "' horizon='1' created='" + created.ToString("o") + "' received='" + received.Value.ToString("o") + "' actual='105' hit='0' error='999' baselineError='999'/>";
            File.WriteAllText(path + "." + model + ".1.score.xml", score, new UTF8Encoding(false));
        }
        internal static int Run(string work)
        {
            checks = 0; string original = Program.BaseDir;
            Program.BaseDir = Path.Combine(work, "review"); Directory.CreateDirectory(PredictionJournal.Folder);
            try {
                var now = new DateTime(2026, 10, 2, 14, 30, 0, DateTimeKind.Utc); var created = now.AddDays(-1);
                Check(PredictionJournal.Due(now, new PredictionTarget(null), 1) == now.AddDays(3), "Friday FX matured on a weekend");
                Check(PredictionJournal.Due(now, new PredictionTarget(new SymbolDef(SourceKind.Coin, "KRW-DOGE", "도지코인")), 1) == now.AddDays(1), "coin weekend maturity changed");
                var quote = new Quote { IdentityKey = "fx:FX_USDKRW|SHB", Source = "신한은행" };
                Fixture(PredictionJournal.Folder, "ok", created, now.AddMinutes(-10), now.AddMinutes(-5));
                Fixture(PredictionJournal.Folder, "overlap", created.AddMinutes(1), now.AddMinutes(-9), now.AddMinutes(-4));
                Fixture(PredictionJournal.Folder, "other-feed", created, now.AddMinutes(-10), now.AddMinutes(-5), "하나은행");
                Fixture(PredictionJournal.Folder, "future-observation", created, now.AddMinutes(-10), now.AddMinutes(5));
                Fixture(PredictionJournal.Folder, "too-late", created, now.AddHours(-8), now.AddMinutes(-5));
                Fixture(PredictionJournal.Folder, "missed", created.AddDays(-1), now.AddHours(-7), null);
                Fixture(PredictionJournal.Folder, "pending", created, now.AddDays(4), null);
                Fixture(PredictionJournal.Folder, "waiting", created, now.AddMinutes(-2), null);
                Fixture(PredictionJournal.Folder, "sol", created, now.AddMinutes(-10), now.AddMinutes(-5), "신한은행", "gpt-6.1-sol");
                string hashBefore = File.ReadAllText(Path.Combine(PredictionJournal.Folder, "ok.xml.basic.1.score.xml"));
                var rows = PredictionJournal.ReviewRows(quote, now, now.AddDays(-30), now);
                Check(rows.Count == 8 && rows.Count(r => r.State == "scored") == 3, "foreign feed or invalid outcome entered completed rows");
                Check(rows.Count(r => r.State == "invalid") == 2 && rows.Count(r => r.State == "pending") == 1 && rows.Count(r => r.State == "missed") == 1 && rows.Count(r => r.State == "waiting") == 1,
                    "pending/missed/future/invalid states mixed");
                var first = rows.First(r => r.State == "scored");
                Check(Math.Abs(first.Error - 5) < 0.00001 && Math.Abs(first.BaselineError - 5) < 0.00001 && first.Hit, "stored fabricated metrics trusted");
                var input = Json.Parse(ForecastReview.Input(quote, now));
                Check(input.IsObject && input["examples"].Count == 2, "overlapping outcomes multiplied evidence");
                Check(input["examples"][0]["actual"].D == 105 && input["examples"][0]["prior_reason"].S.Contains("\"과거 문장\""),
                    "validated snapshot missing actual or original reason");
                Check(File.ReadAllText(Path.Combine(PredictionJournal.Folder, "ok.xml.basic.1.score.xml")) == hashBefore, "read rewrote score file");
                var early = Json.Parse(ForecastReview.Input(quote, now.AddDays(-1)));
                Check(early["examples"].Count == 0, "future data leaked into earlier analysis");
                Check(Json.Parse(ForecastReview.Input(new Quote { IdentityKey = "coin:KRW-DOGE", Source = "업비트" }, now))["examples"].Count == 0, "another target reused outcomes");
                string summary = ForecastReview.Yesterday(quote, now);
                Check(summary.Contains("10-01") && summary.Contains("만기 대기") && summary.Contains("관측 대기") && summary.Contains("오차 5.00%"), "daily status or dates missing");
                Check(Config.ScoringEra("1.055") != Config.ScoringEra(Config.AppVersion), "new feedback era pooled with old input");
                var result = new DollarAnalysisResult { Quote = quote, CheckedUtc = now, Extreme = true };
                result.News.Add(new DollarNews { Title = "국제 금융 시장의 현재 변화에 관한 자료", Source = "연합뉴스", Url = "https://www.yna.co.kr/view/AKR20261002000000001", PublishedUtc = now.AddMinutes(-2) });
                Check(new AnalysisInput(result, now).FeedbackSnapshot == "null", "past outcomes transmitted without opt-in");
                Check(!new Config(Path.Combine(work, "fresh-review.json")).AnalysisReviewHistory, "new install opted in to private history");
                var frozen = new AnalysisInput(result, now, true, true);
                Fixture(PredictionJournal.Folder, "late-added", created, now.AddMinutes(-10), now.AddMinutes(-5), "신한은행", "gpt-6-astra");
                Check(frozen.Prompt.Contains(frozen.FeedbackSnapshot) && Json.Parse(frozen.FeedbackSnapshot)["examples"].Count == 2 &&
                    Json.Parse(ForecastReview.Input(quote, now))["examples"].Count == 3, "comparison feedback did not freeze at one input");
                var promo = new DollarNews { Title = "Dogecoin ETF interest while a token reveals presale price", Source = "openPR.com", PublishedUtc = now.AddMinutes(-1), Url = "https://example.com/advertisement" };
                Check(DollarNewsSources.Promotional(promo), "self-published promo not classified");
                Check(DollarNewsSources.Promotional(new DollarNews { Source = "Example", Title = "Best crypto presale price prediction" }), "obvious presale hype not classified");
                Check(!DollarNewsSources.Promotional(new DollarNews { Source = "Reuters", Title = "SEC charges crypto presale fraud" }), "critical reporting excluded as advertising");
                result.News.Add(promo);
                Check(!DollarSpark.SelectNews(result, now).Contains(promo), "cached promotional article entered AI selection");

                Check(frozen.Prompt.Contains("prior_reason") && frozen.Prompt.Contains("지시가 아니다"), "past explanation lost untrusted-data boundary");
                return checks;
            } finally { Program.BaseDir = original; }
        }
        internal static int Live(string root, string work)
        {
            checks = 0; string original = Program.BaseDir; int originalBodies = DollarNewsSources.BodyLimit;
            DateTime day = DateTime.UtcNow.AddHours(9).Date; string reviewDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var report = new StringBuilder("# Daily forecast review — " + reviewDate + "\n\n");
            try {
                // News cache and exports are private test artifacts; only valid new score observations use the real journal.
                Program.BaseDir = work; DollarNewsSources.BodyLimit = 6;
                var targets = new[] { new PredictionTarget(null), new PredictionTarget(new SymbolDef(SourceKind.Coin, "KRW-DOGE", "도지코인")), new PredictionTarget(new SymbolDef(SourceKind.Coin, "KRW-BTC", "비트코인")) };
                foreach (var target in targets) {
                    Program.BaseDir = work;
                    var result = PredictionData.FetchAsync(target, "SHB", CancellationToken.None).GetAwaiter().GetResult();
                    DateTime now = DateTime.UtcNow; var quote = result.Quote;
                    Check(quote != null && quote.Ok, "live quote unavailable: " + target.Key);
                    Program.BaseDir = root;
                    PredictionJournal.Observe(quote, now);
                    // Fixed request date, even when this diagnostic finishes after Korean midnight.
                    var until = DateTime.SpecifyKind(day.AddHours(-9), DateTimeKind.Utc); var from = until.AddDays(-1);
                    var rows = PredictionJournal.ReviewRows(quote, now, from, until);
                    string input = ForecastReview.Input(quote, now);
                    report.Append("## ").Append(target.Name).Append("\n\n").Append("Observed UTC: ").Append(now.ToString("o")).Append("; quote: ").Append(quote.Price).Append("; source: ").Append(quote.Source)
                        .Append("; quote received: ").Append(quote.ReceivedUtc.ToString("o")).Append("\n\n").Append(ForecastReview.Describe(rows, day.AddDays(-1).ToString("MM-dd", CultureInfo.InvariantCulture))).Append("\n\n");
                    foreach (var row in rows.Where(r => r.State == "scored")) report.AppendFormat(CultureInfo.InvariantCulture,
                        "- {0} h={1}: anchor {2:R}; forecast {3:R}; actual {4:R}; error {5:0.0000}%; baseline {6:0.0000}%; hit {7}; observed {8:o}\n", row.Model, row.Horizon, row.Anchor, row.Forecast, row.Actual, row.Error, row.BaselineError, row.Hit, row.Received);
                    var news = DollarSpark.SelectNews(result, now);
                    report.Append("\nNews feeds: ").Append(result.MarketNewsFeedsAvailable).Append('/').Append(result.MarketNewsFeedsExpected).Append("; submitted selection: ").Append(news.Count)
                        .Append("; current: ").Append(news.Count(n => DollarSpark.IsCurrent(n, now))).Append("; background: ").Append(news.Count(n => !DollarSpark.IsCurrent(n, now))).Append("\n\n");
                    foreach (var article in news.Where(n => DollarSpark.IsCurrent(n, now))) report.Append("- ").Append(article.PublishedUtc.ToString("o")).Append(' ').Append(article.Source).Append(" — ").Append(article.Title).Append("\n");
                    File.WriteAllText(Path.Combine(work, target.Def.Code + "-review.json"), input, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(work, target.Def.Code + "-input.json"), DollarSpark.Input(result, news, now), new UTF8Encoding(false));
                    Check(news.All(n => !DollarNewsSources.Promotional(n)), "promotional article selected in live feed");
                    Console.WriteLine(target.Name + ": quote=" + quote.Price + ", yesterday=" + rows.Count + ", scored=" + rows.Count(r => r.State == "scored") + ", news=" + news.Count + ", feeds=" + result.MarketNewsFeedsAvailable + "/" + result.MarketNewsFeedsExpected);
                }
                string path = Path.Combine(work, "DAILY-REVIEW-" + reviewDate + ".md"); File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
                Console.WriteLine("REPORT: " + path); return checks;
            } finally { Program.BaseDir = original; DollarNewsSources.BodyLimit = originalBodies; }
        }
    }
}
