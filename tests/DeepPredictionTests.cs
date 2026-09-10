using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace DeskWidget
{
    internal static class DeepPredictionTests
    {
        private static int checks;
        private static void Check(bool ok, string message) { if (!ok) throw new Exception("Deep prediction: " + message); checks++; }
        internal static int Run(string work)
        {
            checks = 0; string original = Program.BaseDir;
            Program.BaseDir = Path.Combine(work, "deep"); Directory.CreateDirectory(Program.BaseDir);
            try { Calibration(); SkillChecks(); IcChecks(); HonestyChecks(); CostChecks(); StrengthChecks(); YieldChecks(); LedgerChecks(); GradingChecks(); HolidayChecks(); LeakageChecks(); Journal(); ThresholdChecks(); AbstainChecks(); News(); return checks; }
            finally { Program.BaseDir = original; }
        }
        internal static int Live(string work)
        {
            int count = 0;
            foreach (var def in new[] { new SymbolDef(SourceKind.Coin, "KRW-DOGE", "DOGE"), new SymbolDef(SourceKind.Coin, "KRW-BTC", "BTC"), new SymbolDef(SourceKind.Index, "KOSPI", "KOSPI") }) {
                var target = new PredictionTarget(def);
                var rates = PredictionData.HistoryAsync(target, "HANA", DateTime.UtcNow, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                if (rates.Count < 120) throw new Exception("Public history unavailable: " + def.Code);
                var report = new List<string>(); report.Add("date,value");
                report.AddRange(rates.Select(r => r.Date.ToString("yyyy-MM-dd") + "," + r.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                File.WriteAllLines(Path.Combine(work, def.Code + "-history.csv"), report, new System.Text.UTF8Encoding(false));
                foreach (int h in new[] { 1, 5, 20 }) {
                    var p = DollarAnalysis.Analyze(rates, target.Steps(h));
                    if (p == null || p.Calibration == null) { Console.WriteLine(def.Code + " h=" + h + " insufficient similar cases"); continue; }
                    var c = p.Calibration;
                    Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} h={1} candles={2} last={3:yyyy-MM-dd} folds={4} applied={5} Brier={6:0.0000} raw={7:0.0000} baseline={8:0.0000} hits={9}/{4} rawhits={10} basehits={11}", def.Code, h, rates.Count, rates.Last().Date, c.Count, c.Ready, c.Brier, c.RawBrier, c.BaselineBrier, c.Hits, c.RawHits, c.BaselineHits));
                    if (c.Ready && !(c.Brier < c.RawBrier && c.Brier < c.BaselineBrier)) throw new Exception("Unimproved calibration applied");
                    count++;
                }
            }
            return count;
        }
        private static void Calibration()
        {
            var cases = new List<ProbabilityCase>();
            for (int i = 0; i < 180; i++) {
                int actual = i % 2 == 0 ? 1 : -1;
                Func<bool, double[]> p = wrong => (actual > 0) != wrong ? new[] { .05, 0.0, .95 } : new[] { .95, 0.0, .05 };
                cases.Add(new ProbabilityCase { At = i * 2, Resolved = i * 2 + 1, Outcome = actual, Similar = p(i % 10 < 3), Baseline = p(i % 10 >= 3 && i % 10 < 6) });
            }
            var current = new[] { .95, 0.0, .05 }; var baseline = new[] { .05, 0.0, .95 };
            var result = ProbabilityCalibration.Evaluate(cases, 400, current, baseline);
            Check(result.Ready && result.Count == 150 && result.Brier < result.RawBrier && result.Brier < result.BaselineBrier, "beneficial mixture not selected out of sample");
            Check(result.Hits >= result.RawHits && result.Hits >= result.BaselineHits, "accuracy regressed under calibration");
            Check(result.Weight > 0 && result.Weight < 1 && Math.Abs(result.Probabilities.Sum() - 1) < 1e-9, "invalid adjusted probabilities");
            var small = Enumerable.Range(0, 58).Select(i => new ProbabilityCase { At = i * 2, Resolved = i * 2 + 1, Outcome = i % 2 == 0 ? 1 : -1, Similar = new[] { 0.0, 0.0, 1.0 }, Baseline = new[] { 1.0, 0.0, 0.0 } }).ToList();
            Check(!ProbabilityCalibration.Evaluate(small, 400, current, baseline).Ready, "small validation accepted");
            var equal = cases.Select(c => new ProbabilityCase { At = c.At, Resolved = c.Resolved, Outcome = c.Outcome, Similar = c.Similar, Baseline = c.Similar }).ToList();
            Check(!ProbabilityCalibration.Evaluate(equal, 400, current, current).Ready, "equal forecasts claimed improvement");
            var before = ProbabilityCalibration.Evaluate(cases, 200, current, baseline);
            foreach (var c in cases.Where(c => c.Resolved > 200)) c.Outcome *= -1;
            var after = ProbabilityCalibration.Evaluate(cases, 200, current, baseline);
            Check(before.Brier == after.Brier && before.Weight == after.Weight && before.Count == after.Count, "future outcomes leaked into fit");
            var overlaps = cases.SelectMany(c => new[] { c, c }).ToList();
            var unique = ProbabilityCalibration.Evaluate(cases, 200, current, baseline);
            var duplicate = ProbabilityCalibration.Evaluate(overlaps, 200, current, baseline);
            Check(unique.Count == duplicate.Count && unique.Brier == duplicate.Brier, "overlapping outcomes inflate calibration");
            Check(ProbabilityCalibration.Brier(new[] { 0.0, 0.0, 1.0 }, 1) == 0 && ProbabilityCalibration.Brier(new[] { 1.0, 0.0, 0.0 }, 1) == 2, "Brier scoring wrong");
        }
        private static DollarAnalysisResult Fixture(DateTime now)
        {
            var r = new DollarAnalysisResult { CheckedUtc = now, Extreme = true, DomesticAvailable = true, GlobalAvailable = true,
                Quote = new Quote { Ok = true, Value = 100, IdentityKey = "fx:FX_USDKRW", Source = "fixture", ReceivedUtc = now } };
            var p = new DollarPattern { Horizon = 1, Up = 40, LatestDate = now.Date, ReferenceRate = 100 }; p.Returns.Add(.001); r.Patterns.Add(p);
            var news = new DollarNews { Title = "Dollar rises after policy announcement", Source = "BBC", PublishedUtc = now, Url = "https://news.google.com/articles/test" }; r.News.Add(news);
            // Parse 가 low/medium/high 중 하나를 강제하므로 픽스처도 실제와 같게 채운다.
            var period = new DollarSparkPeriod { Horizon = 1, NewsScore = 10, Reason = "reason", Counter = "counter", Change = "change", Confidence = "medium" };
            period.Citations.Add(new DollarSparkCitation { News = news, Quote = news.Title, Role = "mixed" });
            r.Spark = new DollarSparkResult { Extreme = true, CheckedUtc = now, SubmittedCount = 1 }; r.Spark.Periods.Add(period);
            return r;
        }
        private static void Journal()
        {
            var now = new DateTime(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc);
            var r = Fixture(now); var snapshot = r.Snapshot(); r.Quote.Value = 200;
            Check(snapshot.Quote.Value == 100, "snapshot price mutates with live quote");
            r = Fixture(now); PredictionJournal.Record(r, now);
            DollarAnalysisResult r2 = Fixture(now.AddMinutes(1)); PredictionJournal.Record(r2, now.AddMinutes(1));
            var q = r.Quote.Snapshot(); q.ReceivedUtc = now.AddDays(1).AddMinutes(3); q.Value = 99.999;
            PredictionJournal.Observe(q, q.ReceivedUtc);
            var files = Directory.GetFiles(PredictionJournal.Folder, "*.score.xml"); Check(files.Length == 4, "paired outcomes not recorded");

            // ── 숫자 맥락이 기록에 실제로 박히는가 (v1.038) ────────────────
            // ★ 이 코드는 검사에서 한 번도 실행된 적이 없었다 ★
            //   v1.029·v1.035 가 예측 기록에 달러 강세 지수와 국채금리를 박게 만들었는데,
            //   픽스처가 맥락을 채우지 않아 그 가지가 한 번도 안 돌았다. 나중에 이 신호가
            //   쓸모 있었는지 되짚으려면 그때의 값이 기록에 있어야 하는데, 없으면 영영 못 잰다.
            foreach (string stale in Directory.GetFiles(PredictionJournal.Folder)) File.Delete(stale);
            var ctx = Fixture(now);
            for (int i = 0; i < 30; i++)
            {
                ctx.Context.DollarIndex.Add(new DollarRate { Date = now.Date.AddDays(i - 29), Value = 100 + i * 0.1 });
                ctx.Context.Yield10Y.Add(new DollarRate { Date = now.Date.AddDays(i - 29), Value = 4.5 + i * 0.01 });
                ctx.Context.Yield2Y.Add(new DollarRate { Date = now.Date.AddDays(i - 29), Value = 4.1 + i * 0.01 });
            }
            Check(ctx.Context.Ok && ctx.Context.YieldOk, "the context fixture is not full enough to be recorded");
            PredictionJournal.Record(ctx, now);
            var recorded = new XmlDocument();
            recorded.Load(Directory.GetFiles(PredictionJournal.Folder, "*.xml").First(p => !p.EndsWith(".score.xml", StringComparison.Ordinal)));
            var strength = (XmlElement)recorded.DocumentElement.SelectSingleNode("strength");
            Check(strength != null, "the dollar strength index was not written to the record");
            Check(strength.HasAttribute("d1") && strength.HasAttribute("d5") && strength.HasAttribute("d20"),
                "the strength record is missing its changes");
            Check(strength.GetAttribute("observations") == "30", "the strength record lost its observation count");
            var yield = (XmlElement)recorded.DocumentElement.SelectSingleNode("yield");
            Check(yield != null, "the treasury yield was not written to the record");
            Check(yield.HasAttribute("y10") && yield.HasAttribute("spread"), "the yield record is missing its level or curve");
            Check(yield.GetAttribute("asOf") == now.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "the yield record lost the date it was taken");
            // 맥락이 모자라면 아무것도 박지 않는다 - 반쯤 채운 값을 남기면 나중에 그것을 믿게 된다.
            foreach (string stale in Directory.GetFiles(PredictionJournal.Folder)) File.Delete(stale);
            PredictionJournal.Record(Fixture(now), now);
            var bare = new XmlDocument();
            bare.Load(Directory.GetFiles(PredictionJournal.Folder, "*.xml").First(p => !p.EndsWith(".score.xml", StringComparison.Ordinal)));
            Check(bare.DocumentElement.SelectSingleNode("strength") == null &&
                  bare.DocumentElement.SelectSingleNode("yield") == null,
                "a thin context was written to the record anyway");
            foreach (string stale in Directory.GetFiles(PredictionJournal.Folder)) File.Delete(stale);
            r = Fixture(now); PredictionJournal.Record(r, now);
            r2 = Fixture(now.AddMinutes(1)); PredictionJournal.Record(r2, now.AddMinutes(1));
            PredictionJournal.Observe(q, q.ReceivedUtc);
            files = Directory.GetFiles(PredictionJournal.Folder, "*.score.xml");
            Check(files.Length == 4, "the journal did not come back to its earlier state");
            var score = new XmlDocument(); score.Load(files.First(p => p.Contains(DollarSpark.Model)));
            Check(score.DocumentElement.GetAttribute("hit") == "1", "flat prediction vs micro move classified as miss");
            var summary = PredictionJournal.Summary(q); Check(summary.Contains("1건") && !summary.Contains("2건"), "overlapping pair counted twice");
            q.MarketClosed = true; Check(!PredictionJournal.Fresh(q, q.ReceivedUtc), "closed market accepted as fresh"); q.MarketClosed = false;
            var invalid = Fixture(now); invalid.Quote.IdentityKey = "coin:KRW-DOGE"; PredictionJournal.Record(invalid, now);
            Check(Directory.GetFiles(PredictionJournal.Folder, "*.xml").Length == 6, "mismatched target recorded");
            File.WriteAllText(Path.Combine(PredictionJournal.Folder, "broken.xml"), "<broken");
            var next = Fixture(now.AddDays(2)); PredictionJournal.Record(next, now.AddDays(2));
            q.ReceivedUtc = now.AddDays(3).AddMinutes(3); PredictionJournal.Observe(q, q.ReceivedUtc);
            Check(PredictionJournal.Summary(q).Contains("2건"), "broken record blocks later valid scoring");
            string traversal = Path.Combine(PredictionJournal.Folder, "traversal.xml");
            string valid = Directory.GetFiles(PredictionJournal.Folder, "*.xml").First(p => !p.EndsWith(".score.xml") && p != Path.Combine(PredictionJournal.Folder, "broken.xml"));
            File.WriteAllText(traversal, File.ReadAllText(valid).Replace("model=\"basic\"", "model=\"../../escape\""));
            PredictionJournal.Observe(q, q.ReceivedUtc);
            Check(PredictionJournal.Summary(q).Contains("2건"), "untrusted model path polluted statistics");
            var raw = new XmlDocument(); raw.Load(files[0]); raw.DocumentElement.SetAttribute("hit", "999"); raw.Save(files[0]);
            Check(!PredictionJournal.Summary(q).Contains("999"), "stored score tampering trusted");

            // ── 기록만으로 되짚을 수 있는가 (2026-09-09, 스키마 3) ────────────────
            // 감사에서 나온 것: 저장된 점수를 XML 만으로 재현할 수 없었고(신뢰도 입력 누락),
            // AI 근거문의 (n) 이 어느 기사인지 코드를 다시 돌려야 알 수 있었다.
            var kept = new XmlDocument();
            kept.Load(Directory.GetFiles(PredictionJournal.Folder, "*.xml")
                .First(p => !p.EndsWith(".score.xml") && !p.EndsWith("broken.xml") && !p.EndsWith("traversal.xml")));
            var head = kept.DocumentElement;
            Check(head.GetAttribute("schema") == "3", "new records are not written with the wider schema");
            Check(head.GetAttribute("domestic").Length > 0 && head.GetAttribute("global").Length > 0 &&
                head.GetAttribute("topicFeeds").Contains("/"),
                "reliability inputs missing - the stored score cannot be reproduced from the record");
            var firstArticle = head.SelectSingleNode("article") as XmlElement;
            Check(firstArticle != null && firstArticle.GetAttribute("bodyRead").Length > 0 && firstArticle.GetAttribute("weight").Length > 0,
                "article does not say whether the body was read or how much the source counted");
            var anyForecast = head.SelectSingleNode("forecast") as XmlElement;
            Check(anyForecast.GetAttribute("up").Length > 0 && anyForecast.GetAttribute("down").Length > 0 &&
                anyForecast.GetAttribute("history").Length > 0 && anyForecast.GetAttribute("reliability").Length > 0,
                "score components missing - only the total was kept");
            var aiForecast = head.SelectNodes("forecast").Cast<XmlElement>()
                .FirstOrDefault(f => f.GetAttribute("model") == DollarSpark.Model);
            if (aiForecast != null)
            {
                Check(aiForecast.GetAttribute("confidence").Length > 0, "AI confidence not kept");
                var cites = head.SelectNodes("cite").Cast<XmlElement>()
                    .Where(c => c.GetAttribute("model") == DollarSpark.Model).ToList();
                Check(cites.Count > 0, "AI citations were not kept - the reasoning cannot be checked later");
                // 주소로 박아야 한다. 번호로 남기면 프롬프트 순서를 다시 만들어야 알 수 있다.
                Check(cites.All(c => c.GetAttribute("url").Length > 0 && c.GetAttribute("role").Length > 0),
                    "citation without a source address or role");
                // 저장한 발췌가 같은 기록 안의 기사 원문에 실제로 있어야 한다.
                var bodies = head.SelectNodes("article").Cast<XmlElement>()
                    .ToDictionary(a => a.GetAttribute("url"), a => a.InnerText);
                foreach (var c in cites)
                {
                    Check(bodies.ContainsKey(c.GetAttribute("url")), "citation points at an article the record does not contain");
                    Check(c.InnerText.Length == 0 || bodies[c.GetAttribute("url")].Replace("\n", " ").Contains(c.InnerText.Replace("\n", " ")),
                        "stored citation text is not in the stored article - the record contradicts itself");
                }
            }
            // 옛 스키마 2 기록도 계속 읽어야 한다. 버리면 애써 쌓은 표본이 사라진다.
            string legacy = Path.Combine(PredictionJournal.Folder, "legacy.xml");
            var old = new XmlDocument(); old.LoadXml(kept.OuterXml);
            old.DocumentElement.SetAttribute("schema", "2");
            old.DocumentElement.SetAttribute("created", now.AddDays(5).ToString("o"));
            foreach (XmlElement f in old.DocumentElement.SelectNodes("forecast"))
                f.SetAttribute("due", now.AddDays(6).ToString("o"));
            old.Save(legacy);
            var oldQuote = q.Snapshot(); oldQuote.ReceivedUtc = now.AddDays(6).AddMinutes(3);
            PredictionJournal.Observe(oldQuote, oldQuote.ReceivedUtc);
            Check(Directory.GetFiles(PredictionJournal.Folder, "legacy.xml.*.score.xml").Length > 0,
                "records written before the schema widened are no longer scored");
        }
        // ── 기후값 기준선과 Murphy 분해 (2026-09-09) ──────────────────────────
        // 전에는 3분류 무작위 0.667 을 상대로 삼았다. 그건 틀린 잣대다.
        // 상대는 그때까지의 실현 빈도를 그대로 내놓는 기후값 예측기다.
        private static List<ProbabilityCase> Cases(int n, Func<int, int> outcome, Func<int, double[]> similar, double[] climate)
        {
            var list = new List<ProbabilityCase>();
            for (int i = 0; i < n; i++)
                list.Add(new ProbabilityCase { At = i * 2, Resolved = i * 2 + 1, Outcome = outcome(i),
                    Similar = similar(i), Baseline = (double[])climate.Clone() });
            return list;
        }

        // ── 미래를 읽는지 확인 (2026-09-09) ────────────────────────────────
        // 과거 검증이 '그 시점에 알 수 없던 값' 을 쓰면 성적이 부풀고 실전에서 무너진다.
        // 증명 방법: 마지막 한 봉만 망친다. 그 봉은 마지막 사례의 '결과' 일 뿐,
        // 그 앞 사례들에게는 미래다. 앞 사례들의 성적이 바뀌면 미래를 읽은 것이다.
        private static List<DollarRate> Wiggle(int n, int seed)
        {
            var list = new List<DollarRate>();
            var rnd = new Random(seed);
            DateTime d = new DateTime(2023, 1, 2);
            double v = 1300;
            for (int i = 0; i < n; i++)
            {
                while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
                list.Add(new DollarRate { Date = d, Value = v });
                d = d.AddDays(1);
                v *= 1 + (rnd.NextDouble() - 0.5) * 0.02;
            }
            return list;
        }

        private static List<DollarRate> Copy(List<DollarRate> src)
        {
            return src.Select(r => new DollarRate { Date = r.Date, Value = r.Value }).ToList();
        }

        // ── IC 채점 (2026-09-09) ──────────────────────────────────────────
        // 적중률은 '얼마나 크게 맞혔나' 를 버려서 결론까지 표본이 훨씬 많이 든다.
        // 같은 신호를 크기까지 살려 재면 훨씬 빨리 갈린다. 그 주장을 검사로 고정한다.
        // ── 문턱 보존과 기권 (2026-09-09) ──────────────────────────────────
        // ── 시도 횟수와 오차 상관 (2026-09-09) ────────────────────────────
        // ── 왕복 거래비용 (2026-09-09) ────────────────────────────────────
        // 일간 ±0.1% 는 거의 모든 자산의 왕복 비용 아래다. 그 안을 두고 상승·하락을
        // 말하는 것은 실제로 아무 뜻이 없다. 다만 비용 숫자는 조사해서 넣을 값이지
        // 코드가 지어낼 값이 아니므로, 안 넣었으면 종전 문턱 그대로 둔다.
        // ── 달러 강세 지수 (2026-09-09) ───────────────────────────────────
        // 예측 근거가 기사 제목뿐이라 숫자 맥락을 하나 들였다. 값이 틀리면
        // 화면과 AI 프롬프트와 기록 세 곳에 한꺼번에 거짓말이 퍼진다.

        /// <summary>어느 하루의 네 통화 시세를 JSON 행으로 만든다.</summary>
        private static string StrengthRows(string date, double eur, double jpy, double gbp, double chf)
        {
            var quotes = new[] { "EUR", "JPY", "GBP", "CHF" };
            var values = new[] { eur, jpy, gbp, chf };
            var b = new StringBuilder();
            for (int i = 0; i < quotes.Length; i++)
            {
                if (b.Length > 0) b.Append(',');
                b.Append("{\"date\":\"").Append(date).Append("\",\"base\":\"USD\",\"quote\":\"")
                 .Append(quotes[i]).Append("\",\"rate\":")
                 .Append(values[i].ToString("R", CultureInfo.InvariantCulture)).Append('}');
            }
            return b.ToString();
        }

        /// <summary>기준일부터 하루에 step 배씩 네 통화가 함께 오르는 자료.</summary>
        private static string StrengthSeries(DateTime start, int days, double step)
        {
            var b = new StringBuilder();
            double f = 1;
            for (int i = 0; i < days; i++, f *= step)
            {
                if (b.Length > 0) b.Append(',');
                b.Append(StrengthRows(start.AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    0.92 * f, 147.5 * f, 0.78 * f, 0.86 * f));
            }
            return "[" + b + "]";
        }

        // ── 규칙별 장부 (v1.034) ─────────────────────────────────────────
        // ★ 규칙을 더 넣기 전에 잴 자리 ★
        //   남은 감사 지적은 전부 '규칙이 없어서 침묵' 이다. 규칙을 넣으면 그 침묵은
        //   사라지지만, 차원이 늘면 자료가 더 쪼개져 표본이 문턱 아래로 떨어진다.
        //   느낌으로 규칙을 늘리지 않도록, 넣기 전에 자리를 확인하는 장부를 둔다.

        /// <summary>기록 한 건과 그 채점 파일을 만든다. 실제 Record/Score 와 같은 모양이어야 한다.</summary>
        private static void WriteCase(string folder, int index, string headline, double anchor, double actual, int horizon, int dayOffset = -1, string identity = "fx:FX_USDKRW", bool alsoExtreme = false)
        {
            // 같은 채점 창에 두 기록을 놓아야 하는 검사가 있어서 날짜를 따로 줄 수 있게 한다.
            DateTime created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(dayOffset < 0 ? index * 3 : dayOffset);
            string name = Path.Combine(folder, "case" + index.ToString("00", CultureInfo.InvariantCulture) + ".xml");
            var doc = new XmlDocument(); var root = doc.CreateElement("prediction"); doc.AppendChild(root);
            root.SetAttribute("schema", "3"); root.SetAttribute("version", Config.AppVersion);
            root.SetAttribute("created", created.ToString("o"));
            root.SetAttribute("identity", identity); root.SetAttribute("source", "HANA");
            root.SetAttribute("anchor", anchor.ToString("R", CultureInfo.InvariantCulture));
            var article = doc.CreateElement("article");
            article.SetAttribute("url", "https://www.yna.co.kr/view/AKR" + index);
            article.SetAttribute("source", "연합뉴스");
            article.SetAttribute("published", created.AddMinutes(-30).ToString("o"));
            article.InnerText = headline; root.AppendChild(article);
            foreach (string model in alsoExtreme ? new[] { "basic", "extreme-rule" } : new[] { "basic" })
            {
                var forecast = doc.CreateElement("forecast");
                forecast.SetAttribute("model", model); forecast.SetAttribute("horizon", horizon.ToString(CultureInfo.InvariantCulture));
                forecast.SetAttribute("value", anchor.ToString("R", CultureInfo.InvariantCulture));
                forecast.SetAttribute("due", created.AddDays(horizon).ToString("o"));
                root.AppendChild(forecast);
            }
            doc.Save(name);

            var sdoc = new XmlDocument(); var score = sdoc.CreateElement("score"); sdoc.AppendChild(score);
            score.SetAttribute("schema", "2"); score.SetAttribute("anchor", anchor.ToString("R", CultureInfo.InvariantCulture));
            score.SetAttribute("actual", actual.ToString("R", CultureInfo.InvariantCulture));
            score.SetAttribute("threshold", DollarAnalysis.Threshold(horizon).ToString("R", CultureInfo.InvariantCulture));
            score.SetAttribute("horizon", horizon.ToString(CultureInfo.InvariantCulture));
            foreach (string model in alsoExtreme ? new[] { "basic", "extreme-rule" } : new[] { "basic" })
                sdoc.Save(name + "." + model + "." + horizon.ToString(CultureInfo.InvariantCulture) + ".score.xml");
        }

        // ── 채점이 실제로 돌아가는가 (v1.034) ───────────────────────────
        // ★ 이 자리가 비어 있었다 ★
        //   채점기는 예측 창 안에서만 불렸다. 채점 창은 만기 뒤 6시간인데, 하필 그
        //   6시간에 그 품목의 창을 열어 둘 일이 거의 없다. 그래서 예측 69건이 쌓이는
        //   동안 채점된 것이 0건이었고, 성적·기권·규칙 장부가 전부 빈 채였다.
        //   함수만 검사하고 배선을 안 봐서 아무도 몰랐다 - Alt 글자 크기 때와 같다.
        // ── 미 국채금리 (2026-09-09) ─────────────────────────────────────
        // 실제 미 재무부 피드와 같은 모양으로 만든다. 이름공간이 세 개 붙어 있어서
        // 이름만 보고 고르지 않으면 아무것도 못 찾는다 - 실제 피드를 받아 확인했다.
        private static string YieldFeed(params string[] entries)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\" ?>" +
                "<feed xml:base=\"https://home.treasury.gov/x\" " +
                "xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" " +
                "xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\" " +
                "xmlns=\"http://www.w3.org/2005/Atom\">" + string.Join("", entries) + "</feed>";
        }

        private static string YieldEntry(string date, string two, string ten)
        {
            return "<entry><title type=\"text\"></title><content type=\"application/xml\"><m:properties>" +
                "<d:NEW_DATE m:type=\"Edm.DateTime\">" + date + "T00:00:00</d:NEW_DATE>" +
                "<d:BC_1MONTH m:type=\"Edm.Double\">3.81</d:BC_1MONTH>" +
                (two == null ? "" : "<d:BC_2YEAR m:type=\"Edm.Double\">" + two + "</d:BC_2YEAR>") +
                (ten == null ? "" : "<d:BC_10YEAR m:type=\"Edm.Double\">" + ten + "</d:BC_10YEAR>") +
                "</m:properties></content></entry>";
        }

        private static void YieldChecks()
        {
            // ── 느린 통로와 하루치 기억 (v1.038) ──────────────────────────
            // ★ 파일로만 확인하면 통로가 막힌 것을 못 본다 ★
            //   v1.035 는 curl 로 받아 둔 파일을 파싱해 보고 '된다' 고 했다. 실제 통로는
            //   10초 제한인데 미 재무부는 느릴 때 17~19초가 걸려서, 국채금리는 **한 번도
            //   들어온 적이 없었다.** 빈 값을 조용히 돌려주는 설계라 아무도 몰랐다.
            DateTime slowNow = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
            string slowUrl = "https://home.treasury.gov/x?month=202609";
            Check(Net.CachedSlow(slowUrl, slowNow) == null, "an unseen url came back from the cache");
            Net.RememberSlow(slowUrl, "<feed/>", slowNow);
            Check(Net.CachedSlow(slowUrl, slowNow) == "<feed/>", "a remembered document did not come back");
            Check(Net.CachedSlow(slowUrl, slowNow.Add(Net.SlowCacheLife).AddMinutes(1)) == null,
                "a stale document was still served");
            // 절전에서 깨면 시계가 뒤로 가기도 한다. 그때 영원히 붙잡으면 안 된다.
            Check(Net.CachedSlow(slowUrl, slowNow.AddHours(-1)) == null, "a document survived the clock going backwards");
            // 실패를 기억하면 그 창 동안 계속 실패한 채로 남는다.
            Net.RememberSlow(slowUrl + "&empty=1", "", slowNow);
            Check(Net.CachedSlow(slowUrl + "&empty=1", slowNow) == null, "an empty response was remembered");
            Net.RememberSlow(null, "x", slowNow);
            Check(Net.CachedSlow(null, slowNow) == null, "a null url was remembered");
            // 기억이 한없이 쌓이면 안 된다.
            for (int i = 0; i < 80; i++) Net.RememberSlow("https://x/" + i, "d", slowNow);
            Check(Net.CachedSlow("https://x/79", slowNow) == "d", "the newest document was dropped");

            DateTime today = new DateTime(2026, 9, 8);
            var feed = new List<string>();
            for (int i = 0; i < 40; i++)
                feed.Add(YieldEntry(today.AddDays(i - 39).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    (4.00 + i * 0.01).ToString("0.00", CultureInfo.InvariantCulture),
                    (4.50 + i * 0.01).ToString("0.00", CultureInfo.InvariantCulture)));
            string xml = YieldFeed(feed.ToArray());

            var ten = DollarAnalysis.ParseYields(xml, today, "BC_10YEAR");
            Check(ten.Count == 40, "the treasury feed lost rows - the namespaced fields were not found");
            Check(Math.Abs(ten[ten.Count - 1].Value - 4.89) < 1e-9, "the last ten-year yield is wrong");
            Check(ten[0].Date < ten[ten.Count - 1].Date, "the yield series came back out of order");

            var context = new MarketContext();
            context.Yield10Y = ten;
            context.Yield2Y = DollarAnalysis.ParseYields(xml, today, "BC_2YEAR");
            // ★ 금리는 지수로 만들지 않는다 ★
            //   환율은 비율이라 첫날을 100 으로 맞추지만, 금리는 그 자체가 %이고 차이가
            //   곧 뜻이다. 지수로 바꾸면 '0.03%p 올랐다' 를 말할 수 없게 된다.
            Check(Math.Abs(context.YieldChange(1) - 0.01) < 1e-9, "the one-day yield change is not in points");
            Check(Math.Abs(context.YieldChange(20) - 0.20) < 1e-9, "the twenty-day yield change is wrong");
            Check(Math.Abs(context.CurveSpread() - 0.50) < 1e-9, "the ten-minus-two spread is wrong");
            Check(context.YieldOk && context.YieldSummary().Contains("4.89%"), "the summary did not state the level");
            Check(context.YieldSummary().Contains("10년-2년 +0.50%p"), "the summary did not state the curve");

            // 자료가 모자라면 0 이 아니라 모른다고 한다.
            var thin = new MarketContext();
            thin.Yield10Y = DollarAnalysis.ParseYields(YieldFeed(
                YieldEntry("2026-09-07", "4.39", "4.80"), YieldEntry("2026-09-08", "4.40", "4.83")), today, "BC_10YEAR");
            Check(thin.Yield10Y.Count == 2 && double.IsNaN(thin.YieldChange(5)), "a change was reported without enough days");
            Check(!thin.YieldOk && thin.YieldSummary() == "미 국채금리: 자료 부족", "a two-day series claimed to be usable");
            Check(new MarketContext().YieldSummary() == "미 국채금리: 자료 부족", "an empty context still spoke");
            Check(double.IsNaN(new MarketContext().CurveSpread()), "an empty context invented a curve spread");

            // ★ 같은 날짜가 없으면 억지로 짝짓지 않는다 ★
            //   만기 하나가 비는 날이 실제로 있다. 다른 날의 2년물과 빼면 없는 역전이 생긴다.
            var mismatched = new MarketContext();
            mismatched.Yield10Y = DollarAnalysis.ParseYields(YieldFeed(YieldEntry("2026-09-08", null, "4.80")), today, "BC_10YEAR");
            mismatched.Yield2Y = DollarAnalysis.ParseYields(YieldFeed(YieldEntry("2026-09-07", "4.39", null)), today, "BC_2YEAR");
            Check(mismatched.Yield10Y.Count == 1 && mismatched.Yield2Y.Count == 1, "the partial-maturity fixture was not parsed");
            Check(double.IsNaN(mismatched.CurveSpread()), "two different days were subtracted to make a curve spread");
            // 만기 하나가 비어도 그 날의 다른 만기는 살린다.
            Check(DollarAnalysis.ParseYields(YieldFeed(YieldEntry("2026-09-08", null, "4.80")), today, "BC_2YEAR").Count == 0,
                "a missing maturity produced a row anyway");

            // 못 믿을 값은 그 날만 버린다.
            Check(DollarAnalysis.ParseYields(YieldFeed(YieldEntry("2026-09-08", "4.39", "-1")), today, "BC_10YEAR").Count == 0,
                "a negative yield was accepted");
            Check(DollarAnalysis.ParseYields(YieldFeed(YieldEntry("2026-09-08", "4.39", "99")), today, "BC_10YEAR").Count == 0,
                "an absurd yield was accepted");
            Check(DollarAnalysis.ParseYields(YieldFeed(YieldEntry("2026-09-20", "4.39", "4.80")), today, "BC_10YEAR").Count == 0,
                "a future observation entered the yield series");
            Check(DollarAnalysis.ParseYields("<html>not xml at all", today, "BC_10YEAR").Count == 0, "HTML was parsed as a feed");
            Check(DollarAnalysis.ParseYields("", today, "BC_10YEAR").Count == 0, "an empty response produced rows");

            // 달을 나눠 받으므로 겹치는 날이 생긴다. 하나만 남겨야 한다.
            string one = YieldFeed(YieldEntry("2026-09-07", "4.39", "4.80"), YieldEntry("2026-09-08", "4.40", "4.83"));
            string two = YieldFeed(YieldEntry("2026-09-08", "4.40", "4.83"), YieldEntry("2026-09-04", "4.38", "4.78"));
            var joined = DollarAnalysis.JoinYields(new[] { one, two }, today, "BC_10YEAR");
            Check(joined.Count == 3, "months that overlap produced duplicate days");
            Check(joined[0].Date == new DateTime(2026, 9, 4) && joined[2].Date == new DateTime(2026, 9, 8),
                "the joined series is not in date order");
            // 한 달이 실패해도 나머지로 잇는다.
            Check(DollarAnalysis.JoinYields(new[] { "", one }, today, "BC_10YEAR").Count == 2, "one failed month lost the others");

            // 주소는 그 달만 청한다. 1년치는 260KB 라 매번 받기엔 무겁다.
            string url = DollarAnalysis.YieldUrl(new DateTime(2026, 9, 8));
            Check(url.StartsWith("https://home.treasury.gov/", StringComparison.Ordinal), "the yield request left the treasury");
            Check(url.EndsWith("=202609", StringComparison.Ordinal), "the yield request did not ask for a single month");
            Check(DollarAnalysis.YieldUrl(new DateTime(2026, 1, 3)).EndsWith("=202601", StringComparison.Ordinal),
                "a single-digit month was not zero padded");

            // 모델에게도 보내고, 없으면 없다고 보낸다.
            var result = new DollarAnalysisResult { Target = new PredictionTarget(new SymbolDef(SourceKind.Fx, "FX_USDKRW", "달러")) };
            result.Context = context;
            DateTime asOf = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
            string prompt = DollarSpark.Input(result, new List<DollarNews>(), asOf);
            Check(prompt.Contains("\"treasury_yields\":{") && prompt.Contains("ten_minus_two_points"),
                "the model was not shown the treasury yields");
            var blank = new DollarAnalysisResult { Target = result.Target };
            Check(DollarSpark.Input(blank, new List<DollarNews>(), asOf).Contains("\"treasury_yields\":null"),
                "an empty yield series was sent as if it were real");
        }

        private static void GradingChecks()
        {
            Check(WidgetWindow.ShouldGrade(DateTime.MinValue, new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc)),
                "the first quote cycle did not grade anything");
            DateTime t = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);
            Check(!WidgetWindow.ShouldGrade(t, t.AddSeconds(WidgetWindow.GradeIntervalSec - 1)),
                "grading ran again before its interval");
            Check(WidgetWindow.ShouldGrade(t, t.AddSeconds(WidgetWindow.GradeIntervalSec)),
                "grading stopped running after its interval");

            string folder = Path.Combine(Program.BaseDir, "prediction-history");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            WriteCase(folder, 1, "연준 기준금리 인하 결정", 1300, 1320, 1);
            string scoreFile = Path.Combine(folder, "case01.xml.basic.1.score.xml");
            File.Delete(scoreFile);
            // WriteCase 가 정하는 시각과 같아야 한다. 여기서 따로 만들면 창을 빗나간다.
            DateTime due = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(3).AddDays(1);

            // 채점 창 안에서 받은 시세는 채점을 만들어야 한다.
            var quote = new Quote { Ok = true, Value = 1320, Price = "1320", IdentityKey = "fx:FX_USDKRW",
                Source = "HANA", ReceivedUtc = due.AddHours(1) };
            // ★ 옛 기록에는 threshold 속성이 없다 ★
            //   없는 값을 그대로 읽어서 예외가 나면 그 예외가 채점기를 통째로 끊는다.
            //   그리고 예측은 채점 창(만기+6시간)을 한 번 놓치면 영영 채점할 수 없다.
            //   실측: 예측 69건이 쌓이는 동안 채점된 것 0건.
            Check(!File.Exists(scoreFile), "the fixture already had a score");
            WidgetWindow.GradeAll(new[] { quote }, quote.ReceivedUtc);
            Check(File.Exists(scoreFile), "a due prediction was not graded when the quote arrived");

            // 실패한 시세와 null 은 조용히 지나가야 한다 - 하나가 넘어져도 나머지는 채점된다.
            File.Delete(scoreFile);
            var broken = new Quote { Ok = false, IdentityKey = "fx:FX_USDKRW", Source = "HANA", ReceivedUtc = quote.ReceivedUtc };
            WidgetWindow.GradeAll(new Quote[] { null, broken, quote }, quote.ReceivedUtc);
            Check(File.Exists(scoreFile), "one bad quote stopped the others from being graded");
            WidgetWindow.GradeAll(null, quote.ReceivedUtc);

            // 창이 닫힌 뒤에 받은 시세로는 채점하지 않는다. 며칠 뒤 가격으로 매기면
            // 그건 다른 것을 잰 것이다.
            File.Delete(scoreFile);
            var late = new Quote { Ok = true, Value = 1320, Price = "1320", IdentityKey = "fx:FX_USDKRW",
                Source = "HANA", ReceivedUtc = due.AddHours(7) };
            WidgetWindow.GradeAll(new[] { late }, late.ReceivedUtc);
            Check(!File.Exists(scoreFile), "a prediction was graded with a price from outside its window");

            // ★ 한 건이 넘어져도 나머지는 채점해야 한다 ★
            //   예측은 채점 창(만기+6시간)을 한 번 놓치면 영영 채점할 수 없다.
            //   앞의 기록 하나가 예외를 내면 그날의 나머지가 전부 사라진다.
            File.Delete(scoreFile);
            Directory.CreateDirectory(scoreFile);          // 여기에는 파일을 쓸 수 없다
            // 같은 채점 창 안에 둔다. 앞의 것이 넘어질 때 뒤의 것이 살아남는지 보는 검사다.
            WriteCase(folder, 2, "연준 기준금리 인하 결정", 1300, 1320, 1, 3);
            string second = Path.Combine(folder, "case02.xml.basic.1.score.xml");
            File.Delete(second);
            var again = new Quote { Ok = true, Value = 1320, Price = "1320", IdentityKey = "fx:FX_USDKRW",
                Source = "HANA", ReceivedUtc = due.AddHours(2) };
            WidgetWindow.GradeAll(new[] { again }, again.ReceivedUtc);
            Check(File.Exists(second), "one record that could not be written stopped the rest of the pass");
            Directory.Delete(scoreFile, true);
            File.Delete(Path.Combine(folder, "case02.xml"));
            File.Delete(second);

            // ── 놓친 채점 창을 세어 보인다 (v1.038) ──────────────────────
            // ★ 보이지 않는 손실은 없는 것처럼 여겨진다 ★
            //   채점은 신선한 시세가 있어야 돌고, 시세는 접어 두면 받지 않는다. 그래서
            //   위젯을 접어 둔 시간만큼 표본을 잃는데 그 손실이 아무 데도 안 보였다.
            //   조회를 끈 것은 사용자의 선택이므로 몰래 켜지 않는다. 대신 대가를 적는다.
            File.Delete(scoreFile);
            DateTime longAfter = due.AddDays(3);
            Check(PredictionJournal.Missed("fx:FX_USDKRW", "HANA", longAfter) == 1,
                "a prediction whose window closed unscored was not counted");
            // 아직 창이 열려 있으면 잃은 것이 아니다.
            Check(PredictionJournal.Missed("fx:FX_USDKRW", "HANA", due.AddHours(1)) == 0,
                "a prediction still inside its window was counted as lost");
            // 채점된 것은 세지 않는다.
            WidgetWindow.GradeAll(new[] { quote }, quote.ReceivedUtc);
            Check(File.Exists(scoreFile), "the fixture was not graded");
            Check(PredictionJournal.Missed("fx:FX_USDKRW", "HANA", longAfter) == 0,
                "a graded prediction was counted as lost");
            // 다른 품목의 손실이 섞이면 안 된다.
            Check(PredictionJournal.Missed("coin:KRW-BTC", "HANA", longAfter) == 0, "another instrument's loss leaked in");
            Check(PredictionJournal.Missed(null, "HANA", longAfter) == 0, "a null identity produced a count");
            File.Delete(scoreFile);   // 다음 검사는 채점 파일이 없는 상태에서 시작한다

            // 아주 오래된 기록은 열어 보지도 않는다. 그러지 않으면 기록이 쌓일수록
            // 시세를 받을 때마다 훑는 양이 끝없이 늘어난다.
            var ancient = new Quote { Ok = true, Value = 1320, Price = "1320", IdentityKey = "fx:FX_USDKRW",
                Source = "HANA", ReceivedUtc = due.AddHours(1) };
            File.SetLastWriteTimeUtc(Path.Combine(folder, "case01.xml"), due.AddDays(-100));
            WidgetWindow.GradeAll(new[] { ancient }, ancient.ReceivedUtc);
            Check(!File.Exists(scoreFile), "a record far outside any scoring window was still opened and graded");
            Directory.Delete(folder, true);
        }

        private static void LedgerChecks()
        {
            // 필요 표본 계산부터. 이 숫자가 큰 것이 이 일의 본질이다.
            Check(RuleSkill.NeededForEdge(0.55, 0.50) >= 380 && RuleSkill.NeededForEdge(0.55, 0.50) <= 420,
                "the sample needed to tell 55% from 50% is wrong");
            Check(RuleSkill.NeededForEdge(0.80, 0.50) < RuleSkill.NeededForEdge(0.55, 0.50),
                "a bigger edge did not need fewer cases");
            Check(RuleSkill.NeededForEdge(0.5, 0.5) == int.MaxValue, "a zero edge claimed to be decidable");
            Check(RuleSkill.NeededForEdge(double.NaN, 0.5) == int.MaxValue, "an unknown rate claimed to be decidable");
            // 전승은 분산이 0 이라 그냥 계산하면 '1건이면 충분' 이 된다.
            Check(RuleSkill.NeededForEdge(1.0, 0.5) > 1, "a perfect record was called decidable from one case");

            string folder = Path.Combine(Program.BaseDir, "ledger-history");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            Check(RuleSkill.Ledger(folder, "fx:FX_USDKRW", "HANA").Count == 0, "an empty folder produced a ledger");
            Check(RuleSkill.Report(new List<RuleRow>()).Contains("아직 없습니다"), "an empty ledger claimed to have results");

            // 연준 인하 기사 -> 규칙은 하락(-1). 실제도 하락인 건과 상승인 건을 섞는다.
            for (int i = 0; i < 12; i++)
                WriteCase(folder, i, "연준 기준금리 인하 결정", 1300, i < 9 ? 1280 : 1320, 1);
            var rows = RuleSkill.Ledger(folder, "fx:FX_USDKRW", "HANA");
            var fed = rows.FirstOrDefault(r => r.Rule == "fed-policy");
            Check(fed != null, "the rule that spoke on every case is missing from the ledger");
            Check(fed.Fired == 12 && fed.Hits == 9, "the ledger counted the wrong number of hits");
            // 기후값은 실제 방향의 최빈값이다. 여기서는 하락이 9건이라 하락이 기후값이고,
            // 규칙도 늘 하락이라 우위는 0 이어야 한다 - '늘 같은 쪽' 과 다를 게 없다.
            Check(Math.Abs(fed.Edge) < 1e-9, "a rule that only repeats the climatology showed an edge");
            Check(!fed.Enough && RuleSkill.Report(rows).Contains("표본 부족"),
                "twelve cases were treated as enough to judge a rule");
            // ★ 신원 접두사를 되돌리는 이름이 만드는 이름과 같아야 한다 ★
            //   identity 를 만드는 쪽은 'dstock' 을 내는데 되돌리는 쪽이 'stock' 을 찾으면,
            //   국내주식이 조용히 환율로 떨어져 riskAsset 규칙이 하나도 안 걸린다.
            //   장부는 '채점된 기록이 아직 없습니다' 만 적고, 겉으로는 기능이 도는 것처럼 보인다.
            Check(SymbolDef.KindName(SourceKind.DomesticStock) == "dstock", "the domestic stock prefix changed");
            string kospiFolder = Path.Combine(Program.BaseDir, "dstock-history");
            if (Directory.Exists(kospiFolder)) Directory.Delete(kospiFolder, true);
            Directory.CreateDirectory(kospiFolder);
            for (int i = 0; i < 3; i++)
                WriteCase(kospiFolder, i, "연준 기준금리 인상 결정", 70000, 69000, 1, -1, "dstock:005930");
            var kospiRows = RuleSkill.Ledger(kospiFolder, "dstock:005930", "HANA");
            Check(kospiRows.Any(r => r.Rule.EndsWith("discount-rate", StringComparison.Ordinal)),
                "a domestic stock record produced no rules - the kind prefix did not round-trip");
            Directory.Delete(kospiFolder, true);

            // ★ basic 과 extreme-rule 은 같은 기사에서 같은 요인을 낸다 ★
            //   둘은 보여 주는 방식만 다르다. 둘 다 세면 같은 하루가 표본 두 건이 되어,
            //   '결론을 낼 만큼 쌓였다' 는 판단이 두 배로 낙관적이 된다.
            string twinFolder = Path.Combine(Program.BaseDir, "twin-history");
            if (Directory.Exists(twinFolder)) Directory.Delete(twinFolder, true);
            Directory.CreateDirectory(twinFolder);
            for (int i = 0; i < 5; i++)
                WriteCase(twinFolder, i, "연준 기준금리 인하 결정", 1300, 1280, 1, -1, "fx:FX_USDKRW", true);
            var twin = RuleSkill.Ledger(twinFolder, "fx:FX_USDKRW", "HANA").First(r => r.Rule == "fed-policy");
            Check(twin.Fired == 5, "the same day was counted once per model: " + twin.Fired + " instead of 5");
            Directory.Delete(twinFolder, true);

            // ★ 겹치는 기간은 독립된 표본이 아니다 ★
            //   20일 예측을 이어서 내면 결과 구간이 거의 같은 건들이 쌓인다. 그것을 그대로
            //   세면 필요 표본 계산이 통째로 낙관적이 된다. Summary() 는 이미 겹침을 뺀다.
            string overlapFolder = Path.Combine(Program.BaseDir, "overlap-history");
            if (Directory.Exists(overlapFolder)) Directory.Delete(overlapFolder, true);
            Directory.CreateDirectory(overlapFolder);
            // 하루 간격으로 20일 예측을 열 번 낸다 - 결과 구간이 거의 다 겹친다.
            for (int i = 0; i < 10; i++)
                WriteCase(overlapFolder, i, "연준 기준금리 인하 결정", 1300, 1280, 20, i);
            var overlap = RuleSkill.Ledger(overlapFolder, "fx:FX_USDKRW", "HANA").First(r => r.Rule == "fed-policy");
            Check(overlap.Fired == 1, "overlapping windows were counted as independent samples: " + overlap.Fired);
            // 겹치지 않게 스무 날씩 띄우면 전부 센다.
            Directory.Delete(overlapFolder, true); Directory.CreateDirectory(overlapFolder);
            for (int i = 0; i < 4; i++)
                WriteCase(overlapFolder, i, "연준 기준금리 인하 결정", 1300, 1280, 20, i * 25);
            var spaced = RuleSkill.Ledger(overlapFolder, "fx:FX_USDKRW", "HANA").First(r => r.Rule == "fed-policy");
            Check(spaced.Fired == 4, "non-overlapping windows were dropped: " + spaced.Fired);
            // 어느 '잣대' 의 기록인지도 남겨야 한다 - 채점에 손댄 판이 섞이면 같은 잣대가 아니다.
            // ★ 판올림 목록이 아니라 잣대 목록이다 ★ UI 를 고쳐 올린 판까지 적으면 목록만 길어지고
            //   정작 알고 싶은 것(어떤 잣대들이 섞였나)이 안 보인다.
            Check(spaced.Versions.Count >= 1 && spaced.Versions.Contains(Config.ScoringEra(Config.AppVersion)),
                "the ledger did not record which yardstick produced the numbers");
            Check(!spaced.Versions.Contains(Config.AppVersion) || Config.ScoringEra(Config.AppVersion) == Config.AppVersion,
                "the ledger recorded the app version instead of the yardstick");
            Directory.Delete(overlapFolder, true);

            // ★ 기후값은 기간마다 따로다 ★ (2026-09-10 감사 #26)
            //   1일과 20일은 문턱도 다르고 실제 방향의 분포도 다르다. 하나로 뭉치면 1일 건이
            //   압도적으로 많아 그 다수 방향이 20일 건의 기후값으로 쓰인다.
            string mixFolder = Path.Combine(Program.BaseDir, "horizon-history");
            if (Directory.Exists(mixFolder)) Directory.Delete(mixFolder, true);
            Directory.CreateDirectory(mixFolder);
            for (int i = 0; i < 30; i++) WriteCase(mixFolder, i, "연준 기준금리 인하 결정", 1300, 1320, 1);                       // 1일: 실제 상승 30건
            for (int i = 30; i < 40; i++) WriteCase(mixFolder, i, "연준 기준금리 인하 결정", 1300, 1200, 20, (i - 30) * 25 + 200);   // 20일: 실제 하락 10건
            var mixed = RuleSkill.Ledger(mixFolder, "fx:FX_USDKRW", "HANA").First(r => r.Rule == "fed-policy");
            Check(mixed.Fired == 40 && mixed.Hits == 10, "the mixed-horizon ledger miscounted: " + mixed.Fired + "/" + mixed.Hits);
            // 한 값으로 뭉치면 상승(30:10)이 20일 건의 기후값까지 되어 30 이 된다. 기간마다 따로면 30 + 10 = 40.
            Check(mixed.BaselineHits == 40, "the climatology was pooled across horizons: " + mixed.BaselineHits);
            Directory.Delete(mixFolder, true);

            // 다른 신원·출처의 기록은 섞이면 안 된다.
            Check(RuleSkill.Ledger(folder, "coin:KRW-BTC", "HANA").Count == 0, "another instrument's records leaked in");
            Check(RuleSkill.Ledger(folder, "fx:FX_USDKRW", "SHB").Count == 0, "another feed's records leaked in");

            // 이제 규칙이 기후값과 다른 말을 하는 건을 넣는다. 실제는 상승인데 규칙은 하락.
            for (int i = 12; i < 45; i++)
                WriteCase(folder, i, "연준 기준금리 인하 결정", 1300, 1320, 1);
            rows = RuleSkill.Ledger(folder, "fx:FX_USDKRW", "HANA");
            fed = rows.First(r => r.Rule == "fed-policy");
            Check(fed.Fired == 45 && fed.Enough, "the ledger did not accumulate past the minimum");
            // 실제 상승 36건 / 하락 9건 -> 기후값은 상승. 규칙은 늘 하락이니 성적이 크게 나쁘다.
            Check(fed.Hits == 9 && fed.BaselineHits == 36, "the climatology was not the most common realised direction");
            Check(fed.Edge < -0.5, "a rule that is wrong nearly every time showed no penalty");
            string report = RuleSkill.Report(rows);
            Check(report.Contains("우위 -60.0%p"), "the report did not state how far below the climatology the rule was");
            Check(!report.Contains("표본 부족"), "a rule with 45 cases was still called short of samples");
            // 채점 파일이 없는 기록은 세지 않는다 - 아직 결과를 모르는 예측이다.
            WriteCase(folder, 99, "연준 기준금리 인하 결정", 1300, 1320, 1);
            File.Delete(Path.Combine(folder, "case99.xml.basic.1.score.xml"));
            Check(RuleSkill.Ledger(folder, "fx:FX_USDKRW", "HANA").First(r => r.Rule == "fed-policy").Fired == 45,
                "an unscored prediction was counted in the ledger");
            Directory.Delete(folder, true);
        }

        // ── 휴일 고시환율과 미국 주식 만기 (2026-09-10 감사 #24·#27) ─────────
        // ★ 환율은 휴일에도 받아진다 ★ 네이버는 토요일에도 금요일 고시환율을 돌려주고
        //   ReceivedUtc 만 새것이라, 거래일을 보지 않으면 멈춘 값으로 채점한다.
        // ★ 요일은 거래소가 있는 곳의 요일이다 ★ 미국 장의 뒷부분은 한국 시각으로 다음날이라,
        //   한국 요일로 세면 목·금 오후장 예측의 만기가 일요일에 떨어져 영영 채점되지 않았다.
        private static void HolidayChecks()
        {
            DateTime saturday = new DateTime(2026, 9, 12, 3, 0, 0, DateTimeKind.Utc);   // 토 12:00 KST
            var q = new Quote { Ok = true, Value = 1300, Price = "1,300.00", IdentityKey = "fx:FX_USDKRW", Source = "fixture", ReceivedUtc = saturday.AddSeconds(-10) };
            Check(PredictionJournal.Fresh(q, saturday), "a quote with no traded date was refused");
            q.TradedDate = DollarAnalysis.KoreaDate(saturday);
            Check(PredictionJournal.Fresh(q, saturday), "today's traded quote was refused");
            q.TradedDate = DollarAnalysis.KoreaDate(saturday).AddDays(-1);
            Check(!PredictionJournal.Fresh(q, saturday), "Friday's posted rate was accepted as Saturday's price");
            Check(Sources.DateOf("2026-09-11T15:30:00+09:00") == new DateTime(2026, 9, 11), "the traded date was not read from the feed timestamp");
            Check(Sources.DateOf("2026-09-10T23:30:00-05:00") == new DateTime(2026, 9, 11), "the traded date was not moved to the Korean calendar");
            Check(Sources.DateOf(null) == DateTime.MinValue && Sources.DateOf("not a time") == DateTime.MinValue, "an unreadable timestamp did not stay unknown");

            DateTime thursday = new DateTime(2026, 9, 10, 19, 0, 0, DateTimeKind.Utc);   // 목 19:00Z = 미국 장중, 한국은 금 04:00
            Check(thursday.DayOfWeek == DayOfWeek.Thursday, "fixture weekday");
            var tree = new PredictionTarget(new SymbolDef(SourceKind.WorldStock, "DLTR.O", "달러트리"));
            var won = new PredictionTarget(new SymbolDef(SourceKind.Fx, "FX_USDKRW", "달러"));
            Check(PredictionJournal.Due(thursday, tree, 1) == thursday.AddDays(1), "a Thursday US-session forecast fell due on " + PredictionJournal.Due(thursday, tree, 1).DayOfWeek);
            Check(PredictionJournal.Due(thursday.AddDays(1), tree, 1) == thursday.AddDays(4), "a Friday US-session forecast did not fall due on Monday");
            DateTime friday = new DateTime(2026, 9, 11, 6, 0, 0, DateTimeKind.Utc);   // 금 15:00 KST
            Check(PredictionJournal.Due(friday, won, 1) == friday.AddDays(3), "the won's due day no longer follows the Korean calendar");
            DateTime lateFriday = new DateTime(2026, 9, 11, 16, 0, 0, DateTimeKind.Utc);   // 토 01:00 KST - 한국은 이미 주말
            Check(PredictionJournal.Due(lateFriday, won, 1) == lateFriday.AddDays(2), "a Korean Saturday-morning forecast did not skip to Monday");
        }

        private static void StrengthChecks()
        {
            DateTime today = new DateTime(2026, 9, 8);
            DateTime start = today.AddDays(-40);

            // 첫 관측일은 100 이다. 그래야 통화별 단위 차이가 지수에서 사라진다.
            var flat = DollarAnalysis.ParseStrength(Json.Parse(StrengthSeries(start, 30, 1)), today);
            Check(flat.Count == 30, "a clean strength series lost days");
            Check(Math.Abs(flat[0].Value - 100) < 1e-9, "the strength index did not start at 100");
            Check(Math.Abs(flat[29].Value - 100) < 1e-9, "an unchanging market moved the strength index");

            // ★ 기하평균 확인 ★
            //   네 통화가 모두 정확히 1% 오르면 지수도 정확히 1% 다. 산술평균이면
            //   엔(147)이 지수를 삼켜 유로의 움직임이 사라진다.
            var up = DollarAnalysis.ParseStrength(Json.Parse(StrengthSeries(start, 30, 1.01)), today);
            var context = new MarketContext(); context.DollarIndex = up;
            Check(Math.Abs(context.ChangePercent(1) - 1.0) < 1e-9, "a uniform 1% move was not a 1% index move");
            Check(Math.Abs(context.ChangePercent(5) - (Math.Pow(1.01, 5) - 1) * 100) < 1e-9, "the 5-day change compounded wrong");
            // ★ 엔 하나만 10% 움직여도 지수는 그 4분의 1(기하)만 움직인다 ★
            //   산술평균이면 147 짜리 엔이 지수를 거의 혼자 끌고 간다.
            var lopsided = new MarketContext();
            lopsided.DollarIndex = DollarAnalysis.ParseStrength(Json.Parse("[" +
                StrengthRows("2026-09-01", 0.92, 147.5, 0.78, 0.86) + "," +
                StrengthRows("2026-09-02", 0.92, 162.25, 0.78, 0.86) + "]"), today);
            Check(lopsided.DollarIndex.Count == 2, "the lopsided fixture was not parsed");
            Check(Math.Abs(lopsided.ChangePercent(1) - (Math.Pow(1.1, 0.25) - 1) * 100) < 1e-9,
                "one currency dominated the index - this is an arithmetic mean, not a geometric one");
            Check(lopsided.ChangePercent(1) < 3, "a 10% move in one of four currencies moved the index more than a quarter");

            // ★ 한 통화가 빠진 날은 통째로 버린다 ★
            //   직전 값으로 채우면 있지도 않은 움직임을 만들어 낸다. 그 가짜 움직임이
            //   화면에 '근거' 로 뜨는 것이 이 검사가 막으려는 일이다.
            string holed = "[" + StrengthRows("2026-09-01", 0.92, 147.5, 0.78, 0.86) + "," +
                "{\"date\":\"2026-09-02\",\"base\":\"USD\",\"quote\":\"EUR\",\"rate\":0.95}," +
                StrengthRows("2026-09-03", 0.92, 147.5, 0.78, 0.86) + "]";
            var partial = DollarAnalysis.ParseStrength(Json.Parse(holed), today);
            Check(partial.Count == 2, "a day missing one currency was kept");
            Check(partial.All(p => Math.Abs(p.Value - 100) < 1e-9), "a partial day leaked into the index");

            // 자료가 모자라면 0 이 아니라 NaN 이다. 0 은 '움직임 없음' 이라는 거짓말이다.
            var thin = new MarketContext();
            thin.DollarIndex = DollarAnalysis.ParseStrength(Json.Parse(StrengthSeries(start, 3, 1.01)), today);
            Check(double.IsNaN(thin.ChangePercent(5)), "a change was reported without enough observations");
            Check(double.IsNaN(thin.ChangePercent(0)), "a zero-day change was accepted");
            Check(!thin.Ok && thin.Summary() == "달러 강세 지수: 자료 부족", "a three-day index claimed to be usable");
            Check(new MarketContext().Summary() == "달러 강세 지수: 자료 부족", "an empty context still spoke");
            Check(context.Ok && context.Summary().Contains("1일 +1.00%"), "a usable index did not report its move");

            // 못 믿을 자료는 통째로 버린다. 반쯤 채운 지수를 돌려주면 안 된다.
            // ★ 첫날이 아닌 날의 이상한 값도 잡아야 한다 ★
            //   첫날이 망가지면 기준값이 NaN 이라 저절로 걸린다. 진짜 위험한 것은
            //   중간의 한 줄이다 - 그 날만 NaN 인 지수가 화면까지 흘러간다.
            foreach (string bad in new[] { "-1", "0", "200000" })
            {
                string spoiled = "[" + StrengthRows("2026-09-01", 0.92, 147.5, 0.78, 0.86) + "," +
                    StrengthRows("2026-09-02", 0.92, 147.5, 0.78, 0.86).Replace("\"rate\":0.78", "\"rate\":" + bad) + "]";
                Check(DollarAnalysis.ParseStrength(Json.Parse(spoiled), today).Count == 0,
                    "an out-of-range rate (" + bad + ") entered the index");
            }
            var clean = DollarAnalysis.ParseStrength(Json.Parse(StrengthSeries(start, 30, 1.001)), today);
            Check(clean.All(p => !double.IsNaN(p.Value) && !double.IsInfinity(p.Value) && p.Value > 0),
                "the index carried a value that is not a number");
            Check(DollarAnalysis.ParseStrength(Json.Parse(StrengthSeries(start, 30, 1).Replace("2026-08-01", "8/1/2026")), today).Count == 0,
                "a malformed date was accepted");
            string clash = "[" + StrengthRows("2026-09-01", 0.92, 147.5, 0.78, 0.86) + "," +
                "{\"date\":\"2026-09-01\",\"base\":\"USD\",\"quote\":\"EUR\",\"rate\":0.95}]";
            Check(DollarAnalysis.ParseStrength(Json.Parse(clash), today).Count == 0, "conflicting same-day rates were accepted");
            // 아직 오지 않은 날은 버린다 - 미래를 읽고 예측하는 꼴이 된다.
            string ahead = "[" + StrengthRows("2026-09-01", 0.92, 147.5, 0.78, 0.86) + "," +
                StrengthRows("2026-09-20", 0.99, 158.0, 0.84, 0.92) + "]";
            Check(DollarAnalysis.ParseStrength(Json.Parse(ahead), today).Count == 1, "a future observation entered the index");
            // 다른 기준통화나 모르는 통화는 조용히 지나간다 - 섞이면 지수가 뜻을 잃는다.
            string noise = "[" + StrengthRows("2026-09-01", 0.92, 147.5, 0.78, 0.86) +
                ",{\"date\":\"2026-09-01\",\"base\":\"EUR\",\"quote\":\"JPY\",\"rate\":160}" +
                ",{\"date\":\"2026-09-01\",\"base\":\"USD\",\"quote\":\"KRW\",\"rate\":1350}]";
            Check(DollarAnalysis.ParseStrength(Json.Parse(noise), today).Count == 1, "foreign rows disturbed the index");
            Check(DollarAnalysis.ParseStrength(Json.Parse("[]"), today).Count == 0, "an empty response produced an index");

            // 요청 주소는 이미 쓰는 출처 그대로여야 한다. 새 출처를 늘리지 않겠다는 약속이다.
            string url = DollarAnalysis.StrengthUrl(today);
            Check(url.StartsWith("https://api.frankfurter.dev/", StringComparison.Ordinal), "the strength index left the source we already trust");
            // ★ 주소를 만든 목록으로 주소를 검사하면 아무것도 안 지킨다 ★
            //   StrengthQuotes 가 통째로 바뀌어도 이 검사는 늘 통과한다. 실제로 쓰기로 한
            //   네 통화를 글자로 적어 둔다 - 바꾸려면 검사도 같이 고치게 만든다.
            foreach (string q in new[] { "EUR", "JPY", "GBP", "CHF" })
                Check(url.Contains(q), "the strength request dropped " + q);
            Check(DollarAnalysis.StrengthQuotes.Length == 4, "the strength basket changed size without anyone saying so");
            Check(url.Contains("to=2026-09-08") && !url.Contains("to=2026-09-09"), "the strength request asked for tomorrow");

            // 기록에 남아야 나중에 이 신호가 쓸모 있었는지 잴 수 있다.
            var result = new DollarAnalysisResult { Target = new PredictionTarget(new SymbolDef(SourceKind.Fx, "FX_USDKRW", "달러")) };
            result.Context.DollarIndex = up;
            string xml = DollarSpark.Input(result, new List<DollarNews>(), new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc));
            Check(xml.Contains("\"dollar_strength\":{") && xml.Contains("change_1d_percent"), "the model was not shown the strength index");
            var blank = new DollarAnalysisResult { Target = result.Target };
            Check(DollarSpark.Input(blank, new List<DollarNews>(), new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc))
                    .Contains("\"dollar_strength\":null"), "an empty index was sent as if it were real");
        }

        private static void CostChecks()
        {
            const string setting = "fx:FX_USDKRW=1.5, coin:KRW-BTC=0.1; index:KOSPI = 0.25";
            Check(Config.CostOf(setting, "fx:FX_USDKRW") == 1.5, "cost lookup failed for a plain entry");
            Check(Config.CostOf(setting, "coin:KRW-BTC") == 0.1, "cost lookup failed after a comma");
            Check(Config.CostOf(setting, "index:KOSPI") == 0.25, "cost lookup failed with spaces around =");
            Check(Config.CostOf(setting, "wstock:DLTR.O") == 0, "an unlisted instrument invented a cost");
            Check(Config.CostOf("", "fx:FX_USDKRW") == 0 && Config.CostOf(null, "x") == 0, "empty setting invented a cost");
            // 오타 하나로 예측이 통째로 침묵하면 안 된다.
            Check(Config.CostOf("fx:FX_USDKRW=abc", "fx:FX_USDKRW") == 0, "a malformed cost was accepted");
            Check(Config.CostOf("fx:FX_USDKRW=-2", "fx:FX_USDKRW") == 0, "a negative cost was accepted");
            Check(Config.CostOf("fx:FX_USDKRW=99", "fx:FX_USDKRW") == 0, "an absurd cost was accepted");

            // 비용을 안 넣었으면 문턱은 종전 그대로다.
            foreach (int h in new[] { 1, 5, 20 })
                Check(DollarAnalysis.Threshold(h, 0) == DollarAnalysis.Threshold(h),
                    "an unset cost changed the flat band at horizon " + h);
            // 비용이 문턱보다 작으면 문턱이 이긴다. 문턱을 낮추는 데 쓰이면 안 된다.
            Check(DollarAnalysis.Threshold(20, 0.1) == DollarAnalysis.Threshold(20),
                "a small cost narrowed the band instead of leaving it alone");
            // 비용이 크면 문턱이 그만큼 올라간다.
            Check(Math.Abs(DollarAnalysis.Threshold(1, 1.5) - 0.015) < 1e-12, "cost was not converted from percent");
            Check(DollarAnalysis.Threshold(1, 1.5) > DollarAnalysis.Threshold(1),
                "a real round-trip cost did not widen the daily band");
            // 넓어진 문턱 안의 변동은 보합이어야 한다 - 맞혀도 쓸모없는 구간이다.
            Check(DollarAnalysis.DirectionAt(0.008, DollarAnalysis.Threshold(1, 1.5)) == 0,
                "a move below the round-trip cost was still called a direction");
            Check(DollarAnalysis.DirectionAt(0.02, DollarAnalysis.Threshold(1, 1.5)) == 1,
                "a move above the round-trip cost lost its direction");

            // ★ 화면에 적는 범위가 실제 판정 문턱과 같아야 한다 ★
            //   문턱만 넓히고 화면에는 옛 숫자를 적으면, 보는 사람은 왜 보합이
            //   나왔는지 알 수 없다. 설명이 틀린 예측은 근거 없는 예측과 같다.
            Check(DollarAnalysisWindow.BandText(0) == "보합 범위: 일간 ±0.1% · 주간 ±0.3% · 월간 ±0.5%",
                "the displayed flat band drifted from the default thresholds");
            Check(DollarAnalysisWindow.BandText(1.5) == "보합 범위: 일간 ±1.5% · 주간 ±1.5% · 월간 ±1.5%",
                "the displayed flat band ignored the round-trip cost");
            Check(DollarAnalysisWindow.BandText(0.2) == "보합 범위: 일간 ±0.2% · 주간 ±0.3% · 월간 ±0.5%",
                "a cost between the daily and weekly bands was applied to the wrong horizons");

            // 설정을 저장하고 다시 읽어도 살아 있어야 한다.
            string path = Path.Combine(Program.BaseDir, "cost.json");
            var cfg = new Config(path); cfg.Load();
            Check(cfg.RoundTripCost == "", "round-trip cost has a made-up default");
            cfg.RoundTripCost = setting; cfg.Save();
            var again = new Config(path); again.Load();
            Check(again.RoundTripCost == setting, "round-trip cost did not survive a save and load");
        }

        private static void HonestyChecks()
        {
            // ★ 채점에 손댄 판을 세어 둔다 ★
            //   여러 번 고쳐 보고 좋아 보일 때 고르면 그 성적은 우연일 수 있다.
            //   횟수를 모르면 다중검정 보정을 할 수 없다.
            var revisions = Config.ScoringRevisions;
            Check(revisions != null && revisions.Length >= 5, "scoring revisions are not being counted");
            Check(revisions.Distinct().Count() == revisions.Length, "a scoring revision is listed twice");
            // 목록은 현재 버전을 넘어설 수 없다. 있지도 않은 판을 세면 안 된다.
            foreach (string v in revisions)
                Check(!Config.IsNewer(v), "scoring revision list names a version that does not exist yet: " + v);
            // ★ 오늘 채점을 바꾼 판들이 빠져 있으면 안 된다 ★
            //   빠뜨리면 N 이 작아 보여 성적이 실제보다 믿음직해 보인다.
            foreach (string must in new[] { "1.013", "1.014", "1.017", "1.021", "1.022" })
                Check(revisions.Contains(must), "a version that changed scoring is missing from the count: v" + must);

            // ★ 두 예측이 같이 틀리면 합쳐도 이득이 없다 ★
            var a = new List<double>(); var b = new List<double>(); var c = new List<double>();
            for (int i = 0; i < 50; i++) { double e = (i % 7) - 3; a.Add(e); b.Add(e); c.Add(-e); }
            Check(Math.Abs(PredictionJournal.Correlation(a, b) - 1) < 1e-9, "identical errors did not correlate at 1");
            Check(Math.Abs(PredictionJournal.Correlation(a, c) + 1) < 1e-9, "opposite errors did not correlate at -1");
            // 서로 무관하게 틀리면 상관이 낮아야 한다 - 그때만 결합에 뜻이 있다.
            var x = new List<double>(); var y = new List<double>();
            var rnd = new Random(777);
            for (int i = 0; i < 4000; i++) { x.Add(rnd.NextDouble() - 0.5); y.Add(rnd.NextDouble() - 0.5); }
            Check(Math.Abs(PredictionJournal.Correlation(x, y)) < 0.1, "independent errors reported a strong correlation");
        }

        private static void ThresholdChecks()
        {
            // ① 문턱을 직접 받는 판정.
            Check(DollarAnalysis.DirectionAt(0.02, 0.01) == 1 && DollarAnalysis.DirectionAt(-0.02, 0.01) == -1 &&
                DollarAnalysis.DirectionAt(0.005, 0.01) == 0, "explicit threshold direction wrong");
            Check(DollarAnalysis.DirectionAt(double.NaN, 0.01) == 0, "NaN change produced a direction");
            // 넓은 문턱은 같은 변동을 보합으로 만든다. 그래서 문턱이 바뀌면 성적이 바뀐다.
            Check(DollarAnalysis.DirectionAt(0.002, 0.001) == 1 && DollarAnalysis.DirectionAt(0.002, 0.01) == 0,
                "a wider band did not swallow the same move");

            // ② ★ 기록에 남은 문턱으로 채점해야 한다 ★
            //    옛 기록의 threshold 를 넓게 바꾸면 그 기록의 성적만 달라져야 한다.
            //    현재 코드의 문턱으로 다시 재면 문턱을 고칠 때마다 지난 성적이 통째로
            //    다시 매겨져, '좋아졌다' 가 진짜인지 잣대가 바뀐 것인지 알 수 없다.
            string folder = PredictionJournal.Folder;
            var files = Directory.GetFiles(folder, "*.xml")
                .Where(p => !p.EndsWith(".score.xml") && !p.EndsWith("broken.xml")).ToList();
            Check(files.Count > 0, "no journal record to test threshold preservation");
            var doc = new XmlDocument(); doc.Load(files[0]);
            var fc = doc.DocumentElement.SelectSingleNode("forecast") as XmlElement;
            double stored = double.Parse(fc.GetAttribute("threshold"), CultureInfo.InvariantCulture);
            Check(stored > 0, "record does not carry the band it was graded with");
            Check(Math.Abs(stored - DollarAnalysis.Threshold(int.Parse(fc.GetAttribute("horizon")))) < 1e-12,
                "stored band does not match the band in force when it was written");
        }

        private static void AbstainChecks()
        {
            var now = new DateTime(2026, 9, 9, 3, 0, 0, DateTimeKind.Utc);
            var r = new DollarAnalysisResult { CheckedUtc = now, DomesticAvailable = true, GlobalAvailable = true,
                Quote = new Quote { Ok = true, Value = 100, IdentityKey = "fx:FX_USDKRW", Source = "fixture", ReceivedUtc = now } };
            var p = new DollarPattern { Horizon = 1, Up = 40, Down = 30, Flat = 40, LatestDate = now.Date, ReferenceRate = 100 };
            for (int i = 0; i < 40; i++) p.Returns.Add((i - 20) * 0.0005);
            r.Patterns.Add(p); r.Pattern = p;
            var news = new DollarNews { Title = "연준 금리 인하 결정", Source = "Reuters", PublishedUtc = now.AddMinutes(-5),
                Url = "https://news.google.com/articles/abstain" };
            r.News.Add(news);

            // 성적을 아직 못 쟀으면 기권하지 않는다. 모르는 것과 나쁜 것은 다르다.
            p.Score = null;
            Check(!DollarAnalysisWindow.Abstains(r, 1, now), "abstained without having measured anything");
            // 표본이 모자라도 기권하지 않는다.
            p.Score = new ProbabilityScore { Count = 5, Brier = 0.9, Climatology = 0.5 };
            Check(!DollarAnalysisWindow.Abstains(r, 1, now), "five cases were enough to give up");
            // ★ 기후값보다 못하다고 스스로 쟀으면 방향을 말하지 않는다 ★
            p.Score = new ProbabilityScore { Count = 200, Brier = 0.642, Climatology = 0.622 };
            Check(p.Score.Skill < 0, "probe score is not actually worse than climatology");
            Check(DollarAnalysisWindow.Abstains(r, 1, now), "kept giving a direction while scoring worse than climatology");
            Check(DollarAnalysisWindow.ScenarioOutlook(r, 1, now) == "판단 보류",
                "a proven-useless conditioning still printed a direction");
            // 기후값보다 나으면 그대로 말한다.
            p.Score = new ProbabilityScore { Count = 200, Brier = 0.500, Climatology = 0.622 };
            Check(!DollarAnalysisWindow.Abstains(r, 1, now), "abstained even though it beats climatology");
            Check(DollarAnalysisWindow.ScenarioOutlook(r, 1, now) != "판단 보류", "useful conditioning was silenced");
        }

        private static void IcChecks()
        {
            var up = new List<double>(); var down = new List<double>();
            for (int i = 0; i < 40; i++) { up.Add(i); down.Add(-i); }
            Check(Math.Abs(PredictionJournal.Correlation(up, up) - 1) < 1e-9, "identical series did not correlate at 1");
            Check(Math.Abs(PredictionJournal.Correlation(up, down) + 1) < 1e-9, "mirrored series did not correlate at -1");

            // 늘 같은 값을 예측하면 상관은 '0' 이 아니라 '모름' 이다.
            var flat = new List<double>(); for (int i = 0; i < 40; i++) flat.Add(7);
            Check(double.IsNaN(PredictionJournal.Correlation(flat, up)), "a constant forecast reported a correlation");
            Check(double.IsNaN(PredictionJournal.Correlation(new List<double> { 1, 2 }, new List<double> { 1, 2 })),
                "two points treated as enough for a correlation");

            // 필요한 표본은 IC 가 작을수록 급격히 는다. 이게 '왜 결론이 안 나는가' 의 답이다.
            Check(PredictionJournal.NeededSamples(0.2) == 100, "sample requirement wrong at IC 0.2");
            Check(PredictionJournal.NeededSamples(0.05) == 1600, "sample requirement wrong at IC 0.05");
            Check(PredictionJournal.NeededSamples(-0.2) == 100, "sign changed the sample requirement");
            Check(PredictionJournal.NeededSamples(0) == int.MaxValue && PredictionJournal.NeededSamples(double.NaN) == int.MaxValue,
                "a zero or unknown IC claimed a finite sample requirement");

            // ★ 핵심 주장 ★ 같은 신호라도 크기를 살려 재면 방향만 세는 것보다 빨리 갈린다.
            //   약한 신호(예측이 실제와 조금 같이 움직인다)를 만들어 둘을 견준다.
            //   신호는 매일 고르게 있지 않다. 열흘에 하루쯤 사건이 있고 나머지는 잡음이다.
            //   방향만 세면 잡음인 날까지 한 표씩 세어 이득이 묽어지지만,
            //   크기를 살리면 사건이 있던 날이 제 몫만큼 잡힌다. 표본을 크게 잡아
            //   추정 자체의 흔들림을 없앤 뒤 견준다.
            var predicted = new List<double>(); var realized = new List<double>();
            var rnd = new Random(4321);
            int hits = 0, n = 20000;
            for (int i = 0; i < n; i++) {
                bool eventDay = i % 10 == 0;
                double signal = eventDay ? (rnd.NextDouble() < 0.5 ? -2.0 : 2.0) : (rnd.NextDouble() - 0.5) * 0.1;
                double noise = (rnd.NextDouble() - 0.5) * 6;
                double actual = signal + noise;
                predicted.Add(signal); realized.Add(actual);
                if (Math.Sign(signal) == Math.Sign(actual)) hits++;
            }
            double ic = PredictionJournal.Correlation(predicted, realized);
            Check(ic > 0, "a genuinely informative signal scored a non-positive IC");
            int needIc = PredictionJournal.NeededSamples(ic);
            // 같은 신호를 방향으로만 세면 적중률이 50%에서 아주 조금 벗어난다.
            // 그 차이를 가르는 데 필요한 표본은 대략 1/(차이^2) 규모로 커진다.
            double edge = Math.Abs(hits / (double)n - 0.5);
            int needHit = edge < 0.0001 ? int.MaxValue : (int)Math.Ceiling(0.25 / (edge * edge));
            Check(needIc < needHit,
                "measuring by size needed more samples than measuring by direction - the reason for IC does not hold");
        }

        private static void LeakageChecks()
        {
            var clean = Wiggle(400, 20260909);
            var a = DollarAnalysis.Analyze(clean, 1);
            Check(a != null && a.ValidationCount > 30, "leakage probe needs a longer series");

            // 마지막 봉만 100배로 망친다.
            var tail = Copy(clean);
            tail[tail.Count - 1].Value *= 100;
            var b = DollarAnalysis.Analyze(tail, 1);
            Check(b != null, "poisoned series failed to analyse");
            Check(a.ValidationCount == b.ValidationCount,
                "altering only the last bar changed how many past cases were validated");
            Check(Math.Abs(a.ValidationHits - b.ValidationHits) <= 1,
                "altering only the last bar changed past validation results - the backtest reads the future");
            Check(a.Score != null && b.Score != null && a.Score.Count == b.Score.Count,
                "altering only the last bar changed the scored sample size");

            // ★ 대조군 ★ 가운데를 망치면 반드시 달라져야 한다.
            //   안 달라지면 위 검사는 아무것도 재지 않는 빈 검사다.
            var middle = Copy(clean);
            for (int i = 150; i < 200; i++) middle[i].Value *= 3;
            var c = DollarAnalysis.Analyze(middle, 1);
            Check(c != null && c.Score != null, "control series failed to analyse");
            Check(c.ValidationHits != a.ValidationHits || Math.Abs(c.Score.Brier - a.Score.Brier) > 1e-9,
                "poisoning the middle changed nothing - the leakage probe measures nothing");

            // 채점 표본은 결과가 이미 난 것만이어야 한다. now 를 앞당기면 사례가 줄어야 한다.
            var wide = ProbabilityCalibration.Score(Cases(60, i => i % 3 - 1,
                i => new[] { 0.3, 0.4, 0.3 }, new[] { 0.3, 0.4, 0.3 }), 100000);
            var narrow = ProbabilityCalibration.Score(Cases(60, i => i % 3 - 1,
                i => new[] { 0.3, 0.4, 0.3 }, new[] { 0.3, 0.4, 0.3 }), 40);
            Check(narrow.Count < wide.Count && narrow.Count > 0,
                "cases resolving after the evaluation point were still scored");
        }

        private static void SkillChecks()
        {
            // 보합이 흔한 자산. 기후값은 이 빈도를 그대로 내놓는다.
            var climate = new[] { 0.25, 0.50, 0.25 };
            Func<int, int> mixed = i => i % 4 == 0 ? 1 : i % 4 == 1 ? -1 : 0;   // 상승 25 · 하락 25 · 보합 50

            // ① 기후값을 그대로 따라 하면 실력은 0 이어야 한다.
            var same = ProbabilityCalibration.Score(Cases(120, mixed, i => (double[])climate.Clone(), climate), 100000);
            Check(same.Count == 120, "clean sample dropped valid cases");
            Check(Math.Abs(same.Skill) < 1e-9, "copying climatology did not score zero skill");
            Check(Math.Abs(same.Brier - same.Climatology) < 1e-9, "climatology brier differs from an identical forecast");

            // ② 결과를 아는 완벽한 예측은 실력이 1 에 가까워야 한다.
            Func<int, double[]> perfect = i => { var p = new double[3]; p[mixed(i) + 1] = 1; return p; };
            var best = ProbabilityCalibration.Score(Cases(120, mixed, perfect, climate), 100000);
            Check(best.Brier < 1e-9 && best.Skill > 0.99, "a perfect forecast did not beat climatology");
            Check(best.Resolution > 0.2, "a perfect forecast showed no resolution");

            // ③ 늘 틀리는 예측은 실력이 음수 - 기후값보다 나쁘다는 뜻이다.
            Func<int, double[]> wrong = i => { var p = new double[3]; p[mixed(i) == 1 ? 0 : 2] = 1; return p; };
            var bad = ProbabilityCalibration.Score(Cases(120, mixed, wrong, climate), 100000);
            Check(bad.Skill < 0, "an always-wrong forecast scored non-negative skill");

            // ④ Murphy 항등식: Brier = 불확실성 - 구별력 + 눈금오차.
            //    이게 깨지면 분해가 틀린 것이고 진단도 믿을 수 없다.
            foreach (var sc in new[] { same, best, bad })
            {
                Check(Math.Abs(sc.Brier - (sc.Uncertainty - sc.Resolution + sc.Reliability + sc.Residual)) < 1e-9,
                    "Murphy decomposition does not add back up to the Brier score");
                // 한 칸 안의 확률이 모두 같은 합성 자료에서는 남는 몫이 0 이어야 한다.
                Check(Math.Abs(sc.Residual) < 1e-9, "binning left a residual on constant-probability cases");
            }

            // ⑤ 진단 문구가 상황을 가려야 한다.
            Check(ProbabilityCalibration.Diagnosis(bad).Contains("나쁨"), "worse-than-climatology not reported as such");
            Check(ProbabilityCalibration.Diagnosis(same).Contains("구별력"), "zero-resolution forecast not diagnosed");
            Check(ProbabilityCalibration.Diagnosis(new ProbabilityScore()).Contains("부족"), "empty score claimed a verdict");
            var thin = ProbabilityCalibration.Score(Cases(10, mixed, perfect, climate), 100000);
            Check(!thin.Enough && ProbabilityCalibration.Diagnosis(thin).Contains("판단할 수 없"),
                "ten cases treated as enough to judge");

            // ⑥ 겹치는 구간은 빼야 한다. Evaluate 와 Score 가 같은 표본을 봐야 한다.
            var overlap = new List<ProbabilityCase>();
            for (int i = 0; i < 60; i++)
                overlap.Add(new ProbabilityCase { At = i, Resolved = i + 20, Outcome = mixed(i),
                    Similar = (double[])climate.Clone(), Baseline = (double[])climate.Clone() });
            var trimmed = ProbabilityCalibration.Score(overlap, 100000);
            Check(trimmed.Count < 60 && trimmed.Count > 0, "overlapping windows were counted as independent");
        }

        private static void News()
        {
            var target = new PredictionTarget(new SymbolDef(SourceKind.Coin, "KRW-DOGE", "도지코인"));
            var news = new DollarNews { Target = target, Title = "도지코인 가격 상승", PublishedUtc = DateTime.UtcNow };
            Check(PredictionFactors.Analyze(news).All(f => f.Direction == 0 || f.Weight == 0), "past price counted as new causal signal");
            Check(!DollarSpark.StyleInstructions(true).Contains("뒤집기 어려운 이유"), "extreme prompt forces defense of initial thesis");
        }
    }
}
