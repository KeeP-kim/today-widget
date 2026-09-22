using System;
using System.IO;
using System.Linq;

namespace DeskWidget
{
    internal static class PredictionAuditTests
    {
        private static int count;
        private static void Check(bool condition, string message)
        { if (!condition) throw new Exception("Prediction audit: " + message); count++; }

        private static PredictionTarget Coin(string code, string label)
        { return new PredictionTarget(new SymbolDef(SourceKind.Coin, code, label)); }

        private static DollarNews News(PredictionTarget target, string title, DateTime now, bool bodyRead = true)
        {
            return new DollarNews { Target = target, Title = title, Context = "", Source = "Reuters",
                Url = "https://news.google.com/articles/prediction-audit", PublishedUtc = now, BodyRead = bodyRead };
        }

        private static DollarAnalysisResult Input(PredictionTarget target, DateTime now)
        {
            var r = new DollarAnalysisResult { Target = target, CheckedUtc = now, DomesticAvailable = true, GlobalAvailable = true,
                Quote = new Quote { Ok = true, Value = 100, IdentityKey = target.Key, Source = "audit-fixture", ReceivedUtc = now, ProviderUtc = now, TradedUtc = now } };
            var p = new DollarPattern { Horizon = 1, Up = 40, LatestDate = now.Date.AddDays(-1), ReferenceRate = 100 };
            p.Returns.Add(0.01); r.Patterns.Add(p); r.Pattern = p;
            return r;
        }

        private static void BodyCoverage(DateTime now)
        {
            foreach (var target in new[] { new PredictionTarget(null), Coin("KRW-DOGE", "도지코인") }) {
                string title = target.Dollar ? "연준 기준금리 인상 결정 " : "도지코인 ETF 자금 순유입 보도 ";
                var r = Input(target, now); r.News.Add(News(target, title + "0", now));
                var one = DollarAnalysis.Score(r, 1, now);
                for (int i = 1; i < 10; i++) {
                    var repeat = News(target, title + i, now); repeat.Url += "-" + i; r.News.Add(repeat);
                }
                var repeated = DollarAnalysis.Score(r, 1, now);
                Check(one.ArticleCount == 1 && repeated.ArticleCount == 1 && repeated.DirectionalCount == 1,
                    "same-publisher repeats escaped evidence deduplication");
                Check(one.Reliability == 0.75 && repeated.Reliability == one.Reliability && Math.Abs(repeated.Value - one.Value) < 1e-12,
                    "discarded full-body repeats multiplied confidence or forecast score");
                Check(DollarAnalysis.ForecastReturn(r.Pattern, one) == DollarAnalysis.ForecastReturn(r.Pattern, repeated),
                    "publisher repeats changed the displayed forecast");
                r.News[0].BodyRead = false;
                Check(DollarAnalysis.Score(r, 1, now).Reliability == 0.5,
                    "discarded full-body repeats changed the admitted headline-only evidence");
                var independent = News(target, title + "separate report", now); independent.Source = "BBC"; independent.Url += "-independent";
                r.News.Add(independent);
                var mixed = DollarAnalysis.Score(r, 1, now);
                Check(mixed.ArticleCount == 2 && mixed.Reliability == 0.625,
                    "body coverage is not the fraction of admitted evidence articles");
            }
        }

        private static void EventNegation(DateTime now)
        {
            var target = Coin("KRW-DOGE", "도지코인");
            foreach (string text in new[] { "도지코인 규제 철회", "도지코인 규제안을 철회", "도지코인 규제 취소", "도지코인 소송 취하" })
                Check(PredictionFactors.Analyze(News(target, text, now)).Any(f => f.Rule.EndsWith(":crypto-regulation") && f.Direction == 1 && f.Weight > 0),
                    "restriction withdrawal was treated as a denial: " + text);
            foreach (string text in new[] { "SEC denied Dogecoin ETF application", "Dogecoin ETF application was denied", "SEC rejects Dogecoin ETF approval",
                "도지코인 ETF 승인 철회", "도지코인 ETF 승인 취소", "Dogecoin ETF approval was withdrawn", "SEC canceled Dogecoin ETF approval" })
                Check(PredictionFactors.Analyze(News(target, text, now)).Any(f => f.Rule.EndsWith(":etf-approval") && f.Direction == -1 && f.Weight > 0),
                    "ETF refusal or approval withdrawal lost its negative direction: " + text);
            Check(PredictionFactors.Analyze(News(target, "도지코인 규제 완화 철회", now)).Any(f => f.Rule.EndsWith(":crypto-regulation") && f.Direction == -1),
                "withdrawn easing was read as withdrawn restriction");
            Check(PredictionFactors.Analyze(News(target, "SEC approved Dogecoin ETF", now)).Any(f => f.Rule.EndsWith(":etf-approval") && f.Direction == 1),
                "actual ETF approval was lost");
            foreach (string text in new[] { "도지코인 규제 철회 부인", "도지코인 규제 철회하지 않는다", "도지코인 규제 철회 계획 없다",
                "도지코인 규제 철회 취소", "도지코인 규제 완화 철회 부인", "도지코인 ETF 승인 철회 부인", "도지코인 ETF 승인 취소는 사실 아니다",
                "SEC denied reports of Dogecoin ETF approval", "SEC denied that Dogecoin ETF application was rejected",
                "Dogecoin ETF application was not denied", "Dogecoin ETF approval was not withdrawn", "Dogecoin ETF approval was not canceled",
                "SEC denied reports that Dogecoin ETF approval was withdrawn", "Fed will not cut interest rates" })
                Check(PredictionFactors.Analyze(News(target, text, now)).All(f => f.Direction == 0 && f.Weight == 0),
                    "a factual denial became an established event: " + text);
        }

        private static void IdentityRoundTrip(string work, DateTime now)
        {
            string previous = Program.BaseDir;
            try {
                foreach (string code in new[] { "KRW-BTC", "KRW-ETH", "KRW-DOGE" }) {
                    string name = PredictionTarget.CoinKoreanName(code);
                    string title = name + " ETF 자금 순유입";
                    var live = Coin(code, name); var fromIdentity = Coin(code, code);
                    Check(PredictionFactors.Subject(title, fromIdentity) && PredictionFactors.Subject(title, Coin(code, "사용자 이름")),
                        "known Korean coin alias depends on display label: " + code);
                    Check(PredictionFactors.Analyze(News(live, title, now)).Select(f => f.Rule).SequenceEqual(
                        PredictionFactors.Analyze(News(fromIdentity, title, now)).Select(f => f.Rule)), "identity-only replay changed coin rules");
                    Program.BaseDir = Path.Combine(work, "identity-ledger-" + Guid.NewGuid().ToString("N"));
                    var input = Input(live, now); input.News.Add(News(live, title, now));
                    PredictionJournal.Record(input, now);
                    string forecast = Directory.GetFiles(PredictionJournal.Folder, "*.xml").Single();
                    string original = File.ReadAllText(forecast);
                    var outcome = new Quote { Ok = true, Value = 110, IdentityKey = live.Key, Source = input.Quote.Source,
                        ReceivedUtc = now.AddDays(1), ProviderUtc = now.AddDays(1), TradedUtc = now.AddDays(1) };
                    PredictionJournal.Observe(outcome, outcome.ReceivedUtc);
                    Check(Directory.GetFiles(PredictionJournal.Folder, "*.score.xml").Length == 1, "identity fixture was not actually scored");
                    var row = RuleSkill.Ledger(PredictionJournal.Folder, live.Key, outcome.Source).Single(r => r.Rule == live.Key + ":etf-flow");
                    Check(row.Fired == 1 && row.Hits == 1, "Korean live factor disappeared from the scored rule ledger: " + code);
                    Check(File.ReadAllText(forecast) == original, "rule replay rewrote the immutable forecast");
                }
                var doge = Coin("KRW-DOGE", "KRW-DOGE");
                foreach (string text in new[] { "DogecoinCash", "Dogecoins", "undogecoin", "Dogecoin2", "KRW-DOGE2", "비트코인 ETF 자금 순유입" })
                    Check(!PredictionFactors.Subject(text, doge), "coin identity alias crossed a target or English boundary: " + text);
            }
            finally { Program.BaseDir = previous; }
        }

        internal static int Run(string work)
        {
            count = 0;
            var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
            BodyCoverage(now); EventNegation(now); IdentityRoundTrip(work, now);
            return count;
        }
    }
}
