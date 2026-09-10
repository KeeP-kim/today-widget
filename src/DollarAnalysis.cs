// USD/KRW 전용. 통계는 과거 관측 빈도이며 뉴스 제목으로 확률을 가감하지 않는다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace DeskWidget
{
    internal sealed class DollarRate
    {
        public DateTime Date;
        public double Value;
    }

    /// <summary>
    /// 뉴스 말고 숫자로 된 맥락.
    ///
    /// ★ 지금까지 예측 근거는 기사 제목이 거의 전부였다 ★
    ///   바깥에서 쓰는 예측은 모멘텀·변동성 같은 숫자를 먼저 본다. 여기서는
    ///   이미 쓰고 있는 Frankfurter(ECB) 공표 환율만으로 달러 강세 지수를 만든다.
    ///   새 인증키도, 새 출처도, 새 대기 시간도 늘리지 않는다(같은 병렬 묶음).
    ///
    ///   ※ 이 값은 아직 점수에 넣지 않는다. 화면과 AI 프롬프트와 기록에만 실어
    ///     "달러가 어느 쪽으로 움직이는 중인가" 를 사람과 모델이 같이 보게 한다.
    ///     점수에 넣으려면 기후값 대비 실력이 실제로 오르는지 먼저 재야 한다(v1.017).
    ///     자료 800일을 특징별로 더 쪼개면 표본이 검증 문턱(30) 아래로 떨어진다.
    /// </summary>
    internal sealed class MarketContext
    {
        /// <summary>주요 통화 대비 달러의 상대 강도. 첫 관측일을 100 으로 맞춘 지수.</summary>
        public List<DollarRate> DollarIndex = new List<DollarRate>();
        /// <summary>미 국채 10년 금리(%). 미 재무부 공표값 그대로다 - 지수로 만들지 않는다.</summary>
        public List<DollarRate> Yield10Y = new List<DollarRate>();
        /// <summary>미 국채 2년 금리(%). 10년과의 차이가 경기 신호로 자주 인용된다.</summary>
        public List<DollarRate> Yield2Y = new List<DollarRate>();
        internal bool YieldOk { get { return Yield10Y.Count >= 25; } }
        /// <summary>최근 n 관측일 금리 변화(%p). 자료가 모자라면 NaN.</summary>
        public double YieldChange(int days)
        {
            if (days < 1 || Yield10Y.Count <= days) return double.NaN;
            double now = Yield10Y[Yield10Y.Count - 1].Value, then = Yield10Y[Yield10Y.Count - 1 - days].Value;
            return double.IsNaN(now) || double.IsNaN(then) ? double.NaN : now - then;
        }
        /// <summary>10년과 2년의 차이(%p). 음수면 역전이다.</summary>
        public double CurveSpread()
        {
            if (Yield10Y.Count == 0 || Yield2Y.Count == 0) return double.NaN;
            var last = Yield10Y[Yield10Y.Count - 1];
            for (int i = Yield2Y.Count - 1; i >= 0; i--)
                if (Yield2Y[i].Date == last.Date) return last.Value - Yield2Y[i].Value;
            return double.NaN;   // 같은 날짜가 없으면 억지로 짝짓지 않는다
        }
        public string YieldSummary()
        {
            if (!YieldOk) return "미 국채금리: 자료 부족";
            var parts = new List<string>();
            foreach (int d in new[] { 1, 5, 20 })
            {
                double c = YieldChange(d);
                if (double.IsNaN(c)) continue;
                parts.Add(d + "일 " + c.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%p");
            }
            string text = "미 국채 10년 " + Yield10Y[Yield10Y.Count - 1].Value.ToString("0.00", CultureInfo.InvariantCulture) + "%";
            if (parts.Count > 0) text += " (" + string.Join(" · ", parts) + ")";
            double spread = CurveSpread();
            if (!double.IsNaN(spread))
                text += " · 10년-2년 " + spread.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%p" +
                    (spread < 0 ? "(역전)" : "");
            return text + " · 미 재무부 공표";
        }
        /// <summary>말할 만큼 쌓였는가. 모자라면 아무 말도 하지 않는다.</summary>
        public bool Ok { get { return DollarIndex.Count >= 25; } }
        /// <summary>최근 n 관측일 변화율(%). 자료가 모자라면 NaN 이다 - 0 이 아니다.</summary>
        public double ChangePercent(int days)
        {
            if (days < 1 || DollarIndex.Count <= days) return double.NaN;
            double now = DollarIndex[DollarIndex.Count - 1].Value;
            double then = DollarIndex[DollarIndex.Count - 1 - days].Value;
            if (then <= 0 || double.IsNaN(now) || double.IsNaN(then)) return double.NaN;
            return (now / then - 1) * 100;
        }
        public string Summary()
        {
            if (!Ok) return "달러 강세 지수: 자료 부족";
            var parts = new List<string>();
            foreach (int d in new[] { 1, 5, 20 })
            {
                double c = ChangePercent(d);
                if (double.IsNaN(c)) continue;
                parts.Add(d + "일 " + c.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%");
            }
            if (parts.Count == 0) return "달러 강세 지수: 자료 부족";
            return "달러 강세 지수(EUR·JPY·GBP·CHF 대비, ECB 공표): " + string.Join(" · ", parts);
        }
    }

    internal sealed class DollarPattern
    {
        public int Horizon = 1;
        public int Up, Flat, Down;
        public int Count { get { return Up + Flat + Down; } }
        public double UpPercent { get { return Count == 0 ? 0 : 100.0 * Up / Count; } }
        public double FlatPercent { get { return Count == 0 ? 0 : 100.0 * Flat / Count; } }
        public double DownPercent { get { return Count == 0 ? 0 : 100.0 * Down / Count; } }
        public double FiveDayPercent;
        public DateTime LatestDate;
        public int ValidationCount, ValidationHits, BaselineHits;
        public List<double> Returns = new List<double>();
        public double ReferenceRate;
        public double MedianReturn { get { return Quantile(0.5); } }
        public double LowerReturn { get { return Quantile(0.1); } }
        public double UpperReturn { get { return Quantile(0.9); } }
        public double PriceErrorSum, PersistenceErrorSum;
        public int IntervalHits;
        internal ProbabilityAdjustment Calibration;
        /// <summary>기후값 대비 성적과 Murphy 분해. 확률 보정과 별개로 늘 잰다.</summary>
        internal ProbabilityScore Score;
        internal double EffectiveUp { get { return Calibration != null && Calibration.Ready ? Calibration.Probabilities[2] * 100 : UpPercent; } }
        internal double EffectiveDown { get { return Calibration != null && Calibration.Ready ? Calibration.Probabilities[0] * 100 : DownPercent; } }
        public double Quantile(double fraction)
        {
            if (Returns.Count == 0) return double.NaN;
            var sorted = Returns.OrderBy(x => x).ToArray();
            double position = (sorted.Length - 1) * fraction;
            int low = (int)position, high = Math.Min(low + 1, sorted.Length - 1);
            return sorted[low] + (sorted[high] - sorted[low]) * (position - low);
        }
        public bool Enough { get { return Count >= 30; } }
    }

    internal sealed class DollarNews
    {
        public PredictionTarget Target;
        public string Title, Source, Url, Topic, Context;
        /// <summary>
        /// 요인을 뽑을 때 쓴 제목·본문 그 자체(참조). 캐시가 유효한지 이것으로 본다.
        /// ★ 캐시를 확인하는 값이 캐시보다 비싸면 안 된다 ★
        ///   전에는 확인할 때마다 제목+본문을 이어 붙여 열쇠를 만들었다. 본문이 6000자라
        ///   호출 한 번이 4~12KB 를 새로 할당했고, Score 는 기사 수의 제곱만큼 이것을 불렀다.
        ///   문자열은 바뀌면 새 개체가 되므로 참조만 비교해도 같은 판정이 나온다.
        /// </summary>
        public string FactorTitle, FactorContext, FactorTargetKey;
        public List<DollarFactor> Factors = new List<DollarFactor>();
        public DateTime PublishedUtc;
        public bool Domestic, BodyRead;
        // 제목에 명시된 방향만 표시한다. 향후 수익률의 예측값이 아니다.
        public int Direction;
        public int EvidenceDirection;
        public double EvidenceWeight;
        public string EvidenceReason;
    }

    internal sealed class DollarScoreEvidence
    {
        public DollarNews News;
        public double Up, Down;
        public bool CrossCheckOnly;
        public string Quote, Role;
    }

    internal sealed class DollarScore
    {
        public double UpEvidence, DownEvidence, History, Reliability;
        public int ArticleCount, DirectionalCount;
        public int ReviewedCount;
        public List<DollarScoreEvidence> Evidence = new List<DollarScoreEvidence>();
        public bool IsAi;
        public double Value { get { return (UpEvidence - DownEvidence + History) * Reliability; } }
        public bool Available { get { return DirectionalCount > 0; } }
    }

    internal sealed class DollarAnalysisResult
    {
        public PredictionTarget Target = new PredictionTarget(null);
        public string HistorySource = "Frankfurter / ECB";
        public List<PredictionPoint> DirectForecasts = new List<PredictionPoint>();
        public DateTime CheckedUtc;
        public Quote Quote;
        public List<DollarRate> Rates = new List<DollarRate>();
        public DollarPattern Pattern;
        public List<DollarPattern> Patterns = new List<DollarPattern>();
        public List<DollarNews> News = new List<DollarNews>();
        public bool DomesticAvailable, GlobalAvailable;
        public int TopicFeedsAvailable, TopicFeedsExpected;
        public DollarSparkResult Spark;
        public string SparkStatus;
        public bool Extreme;
        /// <summary>이 품목의 왕복 거래비용(%). 설정에서 넣어 준다. 0 이면 문턱은 종전대로다.</summary>
        public double RoundTripPercent;
        /// <summary>뉴스가 아닌 숫자 맥락. 아직 점수에는 넣지 않는다.</summary>
        public MarketContext Context = new MarketContext();
        internal DollarAnalysisResult Snapshot()
        {
            var copy = (DollarAnalysisResult)MemberwiseClone();
            copy.Quote = Quote == null ? null : Quote.Snapshot();
            copy.News = News.ToList(); copy.Rates = Rates.ToList(); copy.Patterns = Patterns.ToList();
            return copy;
        }
        internal DollarAnalysisResult ForStyle(bool extreme)
        {
            var copy = (DollarAnalysisResult)MemberwiseClone();
            copy.Extreme = extreme; copy.Spark = null; copy.SparkStatus = null;
            return copy;
        }
    }

    internal static class DollarAnalysis
    {
        public const double FlatThreshold = 0.001; // 일별 ±0.1%를 보합으로 고정한다.
        public static DateTime KoreaDate(DateTime utc) { return utc.ToUniversalTime().AddHours(9).Date; }

        public static async Task<DollarAnalysisResult> FetchAsync(string bank, CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;
            DateTime today = KoreaDate(now);
            string historyUrl = "https://api.frankfurter.dev/v2/rates?base=USD&quotes=KRW&providers=ECB&from=" +
                today.AddYears(-3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "&to=" +
                today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string domesticUrl = "https://news.google.com/rss/search?q=" +
                Uri.EscapeDataString("(달러 환율 OR 원달러 OR 한국은행 금리) when:1d") + "&hl=ko&gl=KR&ceid=KR:ko";
            string globalUrl = "https://news.google.com/rss/search?q=" +
                Uri.EscapeDataString("(dollar OR USD OR Federal Reserve) (currency OR inflation OR rates OR won) when:1d") +
                "&hl=en-US&gl=US&ceid=US:en";
            var quoteTask = Sources.FetchPredictionQuoteAsync(new SymbolDef(SourceKind.Fx, "FX_USDKRW", "달러"), bank, ct);
            var historyTask = Net.GetJsonAsync(historyUrl, ct);
            // 같은 출처에 한 번 더. 이미 병렬로 기다리는 묶음에 얹으므로 체감 시간은 그대로다.
            var strengthTask = Net.GetJsonAsync(StrengthUrl(today), ct);
            // 미 국채금리. 달마다 8KB 남짓이라 석 달치를 나란히 받아도 가볍다.
            var yieldTasks = new[] { today, today.AddMonths(-1), today.AddMonths(-2) }
                .Select(m => Net.GetSlowTextAsync(YieldUrl(m), ct)).ToArray();
            var domesticTask = Net.GetTextAsync(domesticUrl, ct);
            var globalTask = Net.GetTextAsync(globalUrl, ct);
            string[] topicQueries = {
                "(연준 OR Federal Reserve) (금리 OR 고용 OR 물가 OR rates)",
                "(한국은행 OR 한은 OR 한국 정부) (금리 OR 재정 OR 정책 OR 수출)",
                "(한미 OR 한국 미국) (관세 OR 무역 OR 협상 OR 투자)",
                "(이란 OR 중동 OR 우크라이나 OR 국제유가) (전쟁 OR 공격 OR 휴전 OR 상승 OR 하락)",
                "(하이닉스 OR 삼성 OR 한국 기업) (미국 투자 OR 미국 공장 OR 미국 진출)",
                "(엔캐리 OR 엔케리 OR 일본은행 OR BOJ) (청산 OR 금리 OR unwind)",
                "(케빈 워시 OR 캐빈 워시 OR Warsh OR Trump OR 트럼프) (금리 OR 관세 OR 이란 OR rates OR tariff)",
                "site:cnn.com (Fed OR Warsh OR Trump OR Iran OR tariffs OR oil)",
                "site:bbc.com (Fed OR Warsh OR Trump OR Iran OR tariffs OR oil)"

            };
            var topicTasks = topicQueries.Select(q => Net.GetTextAsync("https://news.google.com/rss/search?q=" +
                Uri.EscapeDataString(q + " when:1d") + "&hl=ko&gl=KR&ceid=KR:ko", ct)).ToArray();
            var directTasks = DollarNewsSources.Feeds.Select(url => Net.GetTextAsync(url, ct)).ToArray();
            await Task.WhenAll(new Task[] { quoteTask, historyTask, strengthTask, domesticTask, globalTask }.Concat(yieldTasks).Concat(topicTasks).Concat(directTasks));
            ct.ThrowIfCancellationRequested();
            var result = new DollarAnalysisResult { CheckedUtc = DateTime.UtcNow, Quote = quoteTask.Result };
            result.Rates = ParseRates(historyTask.Result, today);
            result.Context.DollarIndex = ParseStrength(strengthTask.Result, today);
            var yieldDocs = yieldTasks.Select(t => t.Result).ToList();
            result.Context.Yield10Y = JoinYields(yieldDocs, today, "BC_10YEAR");
            result.Context.Yield2Y = JoinYields(yieldDocs, today, "BC_2YEAR");
            foreach (int horizon in new[] { 1, 5, 20 })
            {
                var pattern = Analyze(result.Rates, horizon);
                if (pattern != null) result.Patterns.Add(pattern);
            }
            result.Pattern = result.Patterns.FirstOrDefault(p => p.Horizon == 1);
            var domestic = ParseNews(domesticTask.Result, true, now);
            var global = ParseNews(globalTask.Result, false, now);
            result.DomesticAvailable = domestic != null;
            result.GlobalAvailable = global != null;
            if (domestic != null) result.News.AddRange(domestic);
            if (global != null) result.News.AddRange(global);
            result.TopicFeedsExpected = topicTasks.Length;
            foreach (var task in topicTasks)
            {
                var articles = ParseNews(task.Result, true, now);
                if (articles == null) continue;
                result.TopicFeedsAvailable++;
                result.News.AddRange(articles);
            }
            result.TopicFeedsExpected += directTasks.Length;
            for (int i = 0; i < directTasks.Length; i++)
            {
                var articles = ParseNews(directTasks[i].Result, i < 4, now, DollarNewsSources.Publisher(i));
                if (articles == null) continue;
                result.TopicFeedsAvailable++;
                result.News.AddRange(articles);
            }
            // Prefer directly readable publisher URLs over aggregator copies of the same title.
            result.News = result.News.OrderByDescending(n => DollarNewsSources.IsArticle(n.Url)).ThenByDescending(n => n.PublishedUtc)
                .GroupBy(n => Regex.Replace(n.Title.Split(new[] { " - " }, StringSplitOptions.None)[0], "[^\\p{L}\\p{N}]", "").ToLowerInvariant())
                .Select(g => g.First()).ToList();
            await DollarNewsSources.EnrichAsync(result.News, ct);
            ct.ThrowIfCancellationRequested();
            return result;
        }

        public static List<DollarRate> ParseRates(JNode json, DateTime today)
        {
            var byDate = new SortedDictionary<DateTime, double>();
            if (json.Count > 1200) return new List<DollarRate>();
            for (int i = 0; i < json.Count; i++)
            {
                var row = json[i];
                DateTime date;
                double value = row["rate"].D;
                if (row["base"].S != "USD" || row["quote"].S != "KRW" ||
                    !DateTime.TryParseExact(row["date"].S, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out date) || double.IsNaN(value) || double.IsInfinity(value) ||
                    value < 100 || value > 10000) return new List<DollarRate>();
                if (date > today || date < today.AddYears(-3)) continue;
                if (byDate.ContainsKey(date) && byDate[date] != value) return new List<DollarRate>();
                byDate[date] = value;
            }
            return byDate.Select(pair => new DollarRate { Date = pair.Key, Value = pair.Value }).ToList();
        }

        internal static readonly string[] StrengthQuotes = { "EUR", "JPY", "GBP", "CHF" };

        /// <summary>달러 강세 지수용 요청 주소. 기존 환율과 같은 출처, 같은 형식이다.</summary>
        public static string StrengthUrl(DateTime today)
        {
            return "https://api.frankfurter.dev/v2/rates?base=USD&quotes=" + string.Join(",", StrengthQuotes) +
                "&providers=ECB&from=" + today.AddDays(-200).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                "&to=" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 여러 통화 대비 달러 환율을 지수 하나로 접는다.
        ///
        /// ★ 네 통화가 다 있는 날만 쓴다 ★
        ///   하나 빠진 날을 직전 값으로 채우면, 있지도 않은 움직임을 만들어 낸다.
        ///   그렇게 만든 0.3% 하락을 근거라고 화면에 띄우면 그건 거짓말이다.
        ///
        /// ★ 기하평균인 이유 ★
        ///   환율은 비율이다. 산술평균을 쓰면 숫자가 큰 통화(엔 150 대)가 지수를
        ///   혼자 좌우하고, 유로(0.9 대)의 움직임은 사실상 사라진다.
        ///
        /// 실패하면 빈 목록이다. 반쯤 채운 지수를 돌려주지 않는다.
        /// </summary>
        public static List<DollarRate> ParseStrength(JNode json, DateTime today)
        {
            var empty = new List<DollarRate>();
            if (json.Count > 8000) return empty;
            var byDate = new SortedDictionary<DateTime, Dictionary<string, double>>();
            for (int i = 0; i < json.Count; i++)
            {
                var row = json[i];
                if (row["base"].S != "USD") continue;
                string quote = row["quote"].S;
                if (Array.IndexOf(StrengthQuotes, quote) < 0) continue;
                DateTime date;
                double value = row["rate"].D;
                if (!DateTime.TryParseExact(row["date"].S, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out date)) return empty;
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > 100000) return empty;
                if (date > today) continue;                  // 아직 오지 않은 날
                if (date < today.AddYears(-3)) continue;         // 너무 오래된 날
                if (!byDate.ContainsKey(date)) byDate[date] = new Dictionary<string, double>();
                // 같은 날 같은 통화가 서로 다른 값으로 두 번 오면 무엇이 맞는지 알 수 없다.
                if (byDate[date].ContainsKey(quote) && byDate[date][quote] != value) return empty;
                byDate[date][quote] = value;
            }
            var raw = new List<DollarRate>();
            foreach (var pair in byDate)
            {
                if (pair.Value.Count != StrengthQuotes.Length) continue;
                double sum = 0;
                foreach (double v in pair.Value.Values) sum += Math.Log(v);
                raw.Add(new DollarRate { Date = pair.Key, Value = Math.Exp(sum / pair.Value.Count) });
            }
            if (raw.Count == 0) return empty;
            double baseline = raw[0].Value;
            if (baseline <= 0 || double.IsNaN(baseline) || double.IsInfinity(baseline)) return empty;
            foreach (var r in raw) r.Value = r.Value / baseline * 100;
            return raw;
        }

        /// <summary>미 재무부 일별 수익률곡선. 달마다 따로 받는다 - 1년치는 260KB 라 매번 받기엔 무겁다.</summary>
        public static string YieldUrl(DateTime month)
        {
            return "https://home.treasury.gov/resource-center/data-chart-center/interest-rates/pages/xml" +
                "?data=daily_treasury_yield_curve&field_tdr_date_value_month=" +
                month.ToString("yyyyMM", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 재무부 Atom 피드에서 만기별 금리를 뽑는다.
        ///
        /// ★ 금리는 지수로 만들지 않는다 ★
        ///   환율은 비율이라 첫날을 100 으로 맞춰야 뜻이 통하지만, 금리는 그 자체가
        ///   %이고 차이(%p)가 곧 뜻이다. 4.80% 를 100 으로 바꾸면 '0.03%p 올랐다' 를
        ///   말할 수 없게 된다.
        ///
        /// 이름공간이 붙은 문서라 local-name 으로 고른다. 못 믿을 값은 그 날만 버린다 -
        /// 만기 하나가 비는 날이 실제로 있고, 그렇다고 그 날의 10년물까지 버릴 이유는 없다.
        /// </summary>
        public static List<DollarRate> ParseYields(string xml, DateTime today, string field)
        {
            var rows = new List<DollarRate>();
            if (string.IsNullOrEmpty(xml) || xml.Length > 4000000) return rows;
            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(new StringReader(xml),
                    new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit }))
                    doc.Load(reader);
                foreach (XmlNode props in doc.SelectNodes("//*[local-name()='properties']"))
                {
                    DateTime date = DateTime.MinValue; double value = double.NaN;
                    foreach (XmlNode child in props.ChildNodes)
                    {
                        if (child.LocalName == "NEW_DATE")
                            DateTime.TryParse(child.InnerText, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
                        else if (child.LocalName == field)
                            double.TryParse(child.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                    }
                    if (date == DateTime.MinValue || date.Date > today || date.Date < today.AddYears(-3)) continue;
                    // 국채 금리가 음수이거나 스무 자리로 오면 그건 자료가 아니다.
                    if (double.IsNaN(value) || value <= 0 || value > 25) continue;
                    rows.Add(new DollarRate { Date = date.Date, Value = value });
                }
            }
            catch (XmlException) { return new List<DollarRate>(); }
            // 달을 나눠 받으므로 겹치는 날이 생길 수 있다. 날짜 하나에 하나만 남긴다.
            return rows.GroupBy(r => r.Date).Select(g => g.First()).OrderBy(r => r.Date).ToList();
        }

        /// <summary>
        /// 달별 응답 여럿을 하나의 금리 열로 잇는다. 20관측일 변화를 말하려면 한 달로는 모자란다.
        /// 한 달이 실패해도 나머지로 잇는다 - 빈 자리는 채우지 않고 그냥 없는 날로 둔다.
        /// </summary>
        public static List<DollarRate> JoinYields(IEnumerable<string> documents, DateTime today, string field)
        {
            var all = new List<DollarRate>();
            foreach (string xml in documents) all.AddRange(ParseYields(xml, today, field));
            return all.GroupBy(r => r.Date).Select(g => g.First()).OrderBy(r => r.Date).ToList();
        }

        public static int Direction(double change)
        {
            return Direction(change, 1);
        }

        public static double[] MovingAverage(List<DollarRate> rates, int window)
        {
            if (window < 1) throw new ArgumentOutOfRangeException("window");
            var values = new double[rates.Count];
            double sum = 0;
            for (int i = 0; i < rates.Count; i++)
            {
                sum += rates[i].Value;
                if (i >= window) sum -= rates[i - window].Value;
                values[i] = i + 1 < window ? double.NaN : sum / window;
            }
            return values;
        }

        public static double Threshold(int horizon) { return horizon >= 20 ? 0.005 : horizon >= 5 ? 0.003 : FlatThreshold; }
        /// <summary>
        /// 왕복 거래비용(%)을 넘도록 올린 보합 문턱.
        ///
        /// ★ 비용 아래 구간은 맞혀도 쓸모가 없다 ★
        ///   일간 ±0.1% 는 거의 모든 자산의 왕복 비용 아래다. 그 안을 두고
        ///   상승·하락을 말하는 것은 실제로 아무 뜻이 없는 예측이다.
        ///   비용을 아직 안 넣었으면(0) 종전 문턱 그대로다 - 없는 값을 지어내지 않는다.
        /// </summary>
        public static double Threshold(int horizon, double roundTripPercent)
        {
            double band = Threshold(horizon);
            double cost = roundTripPercent / 100.0;
            return double.IsNaN(cost) || cost <= 0 ? band : Math.Max(band, cost);
        }
        /// <summary>
        /// 주어진 문턱으로 방향을 낸다.
        ///
        /// ★ 옛 기록을 채점할 때는 반드시 그때의 문턱을 써야 한다 ★
        ///   문턱을 바꾸고 코드의 현재 값으로 다시 재면 지난 성적이 통째로 다시 매겨진다.
        ///   그러면 '고친 뒤 좋아졌다' 가 진짜인지 잣대가 바뀐 것인지 알 수 없다.
        ///   기록에 threshold 를 남겨 온 이유가 이것인데, 정작 읽지 않고 있었다.
        /// </summary>
        public static int DirectionAt(double change, double threshold)
        {
            if (double.IsNaN(change) || double.IsInfinity(change)) return 0;
            return change > threshold ? 1 : change < -threshold ? -1 : 0;
        }
        public static int Direction(double change, int horizon)
        {
            double threshold = Threshold(horizon);
            return change > threshold ? 1 : change < -threshold ? -1 : 0;
        }

        /// <summary>
        /// 유사 사례를 고르는 조건. ★ v1.039 에서 바꿨다 ★
        ///
        ///   전에는 '5관측일 방향(3) × 20관측일 변동성(2)' 여섯 칸이었다. 800일 시세로 재 보니
        ///   그 조건은 기후값보다 **유의하게 나빴다**(사례별 Brier 차이 t=+3.02, USD/KRW +2.23,
        ///   JPY/KRW +2.47). 정보가 없는 게 아니라 틀린 정보였다.
        ///
        ///   미리 정해 둔 후보 여섯 개를 같은 잣대로 재서(저장소에 넣지 않은 감사 도구로 측정),
        ///   미리 정한 채택 규칙(h=1 에서 5품목 중 4개 실력>0, 200회 이상)을 통과한 것은
        ///   '전일 등락 부호' 하나였다. 다만 유의성(t<-1.96)에는 못 미쳤다(t=-1.52) - 기후값과
        ///   구별되지 않는 중립이다. 유의하게 해로운 것을 중립으로 바꾸는 것이므로 채택했다.
        ///   시도 여섯 번은 ScoringRevisions 에 올렸다.
        ///
        ///   칸이 셋뿐이라 유사 사례가 종전보다 두 배쯤 많다 - 백분위 추정이 그만큼 덜 흔들린다.
        /// </summary>
        internal static int Feature(List<DollarRate> rates, int index)
        {
            if (index < 20 || (rates[index].Date - rates[index - 1].Date).TotalDays > 5 ||
                (rates[index].Date - rates[index - 20].Date).TotalDays > 40) return -1;
            double change = rates[index].Value / rates[index - 1].Value - 1;
            return change > 0.001 ? 2 : change < -0.001 ? 0 : 1;
        }

        private static void Add(DollarPattern sample, int outcome)
        {
            if (outcome > 0) sample.Up++;
            else if (outcome < 0) sample.Down++;
            else sample.Flat++;
        }

        private static int Best(DollarPattern sample)
        {
            // 동률은 보합, 그 밖의 동률은 하락으로 고정하여 재조회마다 결과가 흔들리지 않게 한다.
            if (sample.Flat >= sample.Up && sample.Flat >= sample.Down) return 0;
            return sample.Up > sample.Down ? 1 : -1;
        }

        private static DollarPattern Sample(List<DollarRate> rates, int[] features, int target, bool similar, int horizon)
        {
            var sample = new DollarPattern { Horizon = horizon };
            // 목표일 이후 값은 읽지 않는다. 현재 5일 패턴과 겹치는 사례도 빼 둔다.
            for (int i = 20; i + horizon < target - 4; i++)
            {
                if (features[i] < 0 || (similar && features[i] != features[target]) ||
                    (rates[i + horizon].Date - rates[i].Date).TotalDays > horizon * 3 + 4) continue;
                Add(sample, Direction(rates[i + horizon].Value / rates[i].Value - 1, horizon));
                sample.Returns.Add(rates[i + horizon].Value / rates[i].Value - 1);
            }
            return sample;
        }

        public static DollarPattern Analyze(List<DollarRate> rates, int horizon = 1)
        {
            if (horizon != 1 && horizon != 5 && horizon != 7 && horizon != 20 && horizon != 30) throw new ArgumentOutOfRangeException("horizon");
            if (rates == null || rates.Count < 120) return null;
            var features = new int[rates.Count];
            for (int i = 0; i < rates.Count; i++) features[i] = Feature(rates, i);
            int latest = rates.Count - 1;
            if (features[latest] < 0) return null;
            var result = Sample(rates, features, latest, true, horizon);
            result.LatestDate = rates[latest].Date;
            result.ReferenceRate = rates[latest].Value;
            result.FiveDayPercent = (rates[latest].Value / rates[latest - 5].Value - 1) * 100;
            // 시간 순서대로 그날까지의 자료만 사용한다. 최신 1년 내 최대 250회 검증.
            // 주·월 검증은 결과 기간이 겹치지 않도록 각각 5·20공시일 간격으로 평가한다.
            var calibrationCases = new List<ProbabilityCase>();
            for (int target = Math.Max(120, latest - 250); target + horizon <= latest; target += horizon)
            {
                if (features[target] < 0 || (rates[target + horizon].Date - rates[target].Date).TotalDays > horizon * 3 + 4) continue;
                var sample = Sample(rates, features, target, true, horizon);
                if (!sample.Enough) continue;
                var baseline = Sample(rates, features, target, false, horizon);
                int actual = Direction(rates[target + horizon].Value / rates[target].Value - 1, horizon);
                calibrationCases.Add(new ProbabilityCase { At = target, Resolved = target + horizon, Outcome = actual, Similar = ProbabilityCalibration.Frequencies(sample), Baseline = ProbabilityCalibration.Frequencies(baseline) });
                result.ValidationCount++;
                double realized = rates[target + horizon].Value / rates[target].Value - 1;
                result.PriceErrorSum += Math.Abs(rates[target].Value * (1 + sample.MedianReturn) - rates[target + horizon].Value);
                result.PersistenceErrorSum += Math.Abs(rates[target].Value - rates[target + horizon].Value);
                if (realized >= sample.LowerReturn && realized <= sample.UpperReturn) result.IntervalHits++;
                if (Best(sample) == actual) result.ValidationHits++;
                if (Best(baseline) == actual) result.BaselineHits++;
            }
            if (result.Enough)
            {
                // 성적표를 먼저 낸다. 보정이 적용되든 말든 '기후값보다 나은가' 는 늘 재야 한다.
                result.Score = ProbabilityCalibration.Score(calibrationCases, latest);
                result.Calibration = ProbabilityCalibration.Evaluate(calibrationCases, latest, ProbabilityCalibration.Frequencies(result), ProbabilityCalibration.Frequencies(Sample(rates, features, latest, false, horizon)));
            }
            return result;
        }

        public static bool Fresh(DollarAnalysisResult result, DateTime nowUtc)
        {
            return result != null && Fresh(result.Pattern, nowUtc);
        }

        // ★ 여기에 결과를 기억해 두지 않는다 - 해 보고 되돌렸다 ★
        //   한 번 그리는 데 이 함수가 70번 불린다. 그래서 (자료·시각) 이 같으면 지난 답을 그대로
        //   주도록 만들어 봤는데, '자료가 같다' 를 값싸게 판정할 방법이 없었다. 뉴스 목록은 개체가
        //   그대로인 채 내용만 바뀔 수 있어서(목록에 기사를 더하는 것만으로도) 지난 답이 조용히
        //   틀린 답이 된다 - 검사가 바로 그것을 잡았다. 몇 밀리초를 벌자고 '조용히 틀린' 것을
        //   들이는 거래는 하지 않는다.
        //   대신 진짜 비용이던 것을 없앴다: 요인 캐시가 확인할 때마다 제목+본문(최대 12KB)을
        //   이어 붙여 열쇠를 만들던 것을 참조 비교로 바꿨다(DollarNews.FactorTitle).
        //   더 줄여야 하면 창이 Render 첫머리에서 기간 3개를 한 번 구해 인자로 넘기는 쪽이 맞다.
        public static DollarScore Score(DollarAnalysisResult result, int horizon, DateTime nowUtc)
        {
            var score = new DollarScore();
            if (result == null) return score;
            var ai = result.Spark == null || result.Spark.TargetKey != result.Target.Key || result.Spark.Extreme != result.Extreme || result.Spark.CheckedUtc > nowUtc ||
                (nowUtc - result.Spark.CheckedUtc).TotalHours > 24 || result.Spark.Periods.Any(p => p.Citations.Any(c =>
                    !result.News.Contains(c.News) || !ForTarget(c.News, result.Target) || c.News.PublishedUtc > nowUtc || c.News.PublishedUtc < nowUtc.AddHours(-24)))
                ? null : result.Spark.Periods.FirstOrDefault(p => p.Horizon == horizon);
            if (ai != null)
            {
                score.IsAi = true; score.Reliability = 1;
                score.UpEvidence = Math.Max(0, ai.NewsScore); score.DownEvidence = Math.Max(0, -ai.NewsScore);
                score.History = ai.HistoryScore; score.DirectionalCount = ai.Citations.Count;
                score.ArticleCount = ai.Citations.Count; score.ReviewedCount = result.Spark.SubmittedCount;
                score.Evidence = ai.Citations.Select(c => new DollarScoreEvidence { News = c.News, Quote = c.Quote, Role = c.Role }).ToList();
                return score;
            }
            // Explicit editorial weights, not learned probabilities. Never renormalize missing news to history.
            var news = result.News.Where(n => ForTarget(n, result.Target) && n.PublishedUtc <= nowUtc && n.PublishedUtc >= nowUtc.AddHours(-24) && IsNewsLink(n.Url))
                .GroupBy(n => Regex.Replace(n.Title.Split(new[] { " - " }, StringSplitOptions.None)[0], "[^\\p{L}\\p{N}]", "").ToLowerInvariant())
                .Select(g => g.First()).ToList();
            score.ReviewedCount = news.Count;
            double up = 0, down = 0, total = 0;
            var crossChecked = new HashSet<DollarNews>();
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in news)
            {
                Classify(n);
                // One publisher cannot multiply the same topic/direction by issuing many headlines.
                if (!groups.Add((n.Source ?? "unknown") + "|" + n.Topic + "|" + n.EvidenceDirection)) continue;
                var entry = new DollarScoreEvidence { News = n };
                score.Evidence.Add(entry);
                double weight = Math.Exp(-(nowUtc - n.PublishedUtc).TotalHours / 24);
                double sourceWeight = SourceWeight(n.Source);
                var corroborators = news.Where(other => other.Source != n.Source &&
                    DollarFactors.Analyze(other).Any(f => n.Factors.Any(own => own.Rule == f.Rule && own.Direction == f.Direction)))
                    .GroupBy(other => other.Source).Select(g => g.First()).Take(2).ToList();
                foreach (var other in corroborators) crossChecked.Add(other);
                double confirmation = n.Factors.Count == 0 ? 1 : Math.Min(1, 0.75 + 0.125 * corroborators.Select(other => other.Source).Distinct().Count());
                double strength = sourceWeight * confirmation;
                score.ArticleCount++;
                if (n.Factors.Count > 0)
                {
                    // ★ 방향을 못 낸 요인(동결·재정·이미 난 가격 변동)은 가중치가 0 이다.
                    //   그것을 분모에 넣으면 말이 있는 요인이 말없는 요인 수만큼 묽어진다.
                    int speaking = n.Factors.Count(f => f.Weight > 0);
                    double positive = speaking == 0 ? 0 : n.Factors.Where(f => f.Direction > 0).Sum(f => f.Weight) / speaking;
                    double negative = speaking == 0 ? 0 : n.Factors.Where(f => f.Direction < 0).Sum(f => f.Weight) / speaking;
                    up += weight * positive * strength;
                    down += weight * negative * strength;
                    entry.Up = weight * positive * strength;
                    entry.Down = weight * negative * strength;
                    if (positive > 0 || negative > 0) { score.DirectionalCount++; total += weight; }
                }
                else
                {
                    if (n.EvidenceDirection > 0) { entry.Up = weight * n.EvidenceWeight * strength; up += entry.Up; score.DirectionalCount++; total += weight; }
                    if (n.EvidenceDirection < 0) { entry.Down = weight * n.EvidenceWeight * strength; down += entry.Down; score.DirectionalCount++; total += weight; }
                }
            }
            double horizonWeight = HorizonWeight(horizon);
            // ★ 분모에서 침묵한 기사를 뺐으므로(위) '근거가 얇다' 는 판단은 이제 이 항이 혼자 진다.
            //   전에는 같은 보수성이 분모와 여기 두 곳에 겹쳐 걸려 점수가 문턱 근처에도 못 갔다.
            //   기준을 4건에서 8건으로 올려 전체 보수성은 그대로 두되 이중 계산만 없앤다.
            //   (실측: 방향 근거 4건짜리 날에 '충분한 근거' 라고 하던 것이 절반으로 내려간다)
            double coverage = Math.Min(1, score.DirectionalCount / 8.0);
            if (total > 0)
            {
                score.UpEvidence = 80 * up / total * coverage * horizonWeight;
                score.DownEvidence = 80 * down / total * coverage * horizonWeight;
            }
            var pattern = result.Patterns.FirstOrDefault(p => p.Horizon == horizon) ??
                (result.Pattern != null && result.Pattern.Horizon == horizon ? result.Pattern : null);
            if (Fresh(pattern, nowUtc))
            {
                // Failed or undersampled validation reduces the historical contribution by 75%.
                double validation = pattern.ValidationCount >= 60 && pattern.ValidationHits > pattern.BaselineHits ? 1 : 0.25;
                score.History = 20 * (pattern.EffectiveUp - pattern.EffectiveDown) / 100 * validation;
            }
            // Title-only evidence is provisional; missing feeds reduce it further.
            // ★ 전에는 0.5 고정이라 본문을 다 읽어도 신뢰가 오르지 않았다 - 자료를 더 읽을 이유가
            //   숫자에 없었다. 실제로 본문을 읽은 비율만큼만 올린다(제목만이면 종전과 같은 0.5).
            double bodyRead = score.ArticleCount == 0 ? 0 : (double)news.Count(a => a.BodyRead) / score.ArticleCount;
            score.Reliability = (0.5 + 0.25 * bodyRead) * (result.DomesticAvailable && result.GlobalAvailable ? 1 : 0.5);
            if (result.TopicFeedsExpected > 0)
                score.Reliability *= 0.5 + 0.5 * result.TopicFeedsAvailable / result.TopicFeedsExpected;
            double scale = total > 0 ? 80 / total * coverage * horizonWeight * score.Reliability : 0;
            foreach (var entry in score.Evidence) { entry.Up *= scale; entry.Down *= scale; }
            foreach (var other in crossChecked.Where(n => !score.Evidence.Any(e => e.News == n)))
                score.Evidence.Add(new DollarScoreEvidence { News = other, CrossCheckOnly = true });
            return score;
        }

        public static bool Fresh(DollarPattern pattern, DateTime nowUtc)
        {
            return pattern != null && pattern.Enough && (KoreaDate(nowUtc) - pattern.LatestDate).TotalDays <= 7 &&
                pattern.LatestDate <= KoreaDate(nowUtc);
        }

        internal static bool ForTarget(DollarNews news, PredictionTarget target)
        { return news.Target == null ? target.Dollar : news.Target.Key == target.Key; }

        /// <summary>24시간 뉴스가 먼 기간에 대해 갖는 무게. 주 0.5·월 0.25.</summary>
        internal static double HorizonWeight(int horizon) { return horizon == 20 ? 0.25 : horizon == 5 ? 0.5 : 1; }

        /// <summary>
        /// 이 기간에서 점수가 낼 수 있는 최대치.
        /// 뉴스 항(80 × 근거계수 × 기간가중치)과 과거 항(20 × 검증계수)을 합해 신뢰도를 곱한다.
        /// 근거계수는 1(방향 기사 8건 이상), 검증계수는 0.25(표본 부족 시 기본값)로 본다.
        /// </summary>
        internal static double ScoreCeiling(int horizon, double reliability)
        {
            return (80 * HorizonWeight(horizon) + 20 * 0.25) * reliability;
        }

        /// <summary>
        /// 보합을 벗어나려면 점수가 얼마여야 하는가.
        /// ForecastReturn 이 점수/100 × 과거 변동폭이므로, 문턱을 변동폭으로 나누면 나온다.
        /// </summary>
        internal static double DirectionalNeed(DollarPattern pattern, double band)
        {
            if (pattern == null) return double.NaN;
            double span = Math.Max(Math.Abs(pattern.LowerReturn), Math.Abs(pattern.UpperReturn));
            return span > 0 && !double.IsNaN(span) ? band / span * 100 : double.NaN;
        }

        internal static double ForecastReturn(DollarPattern pattern, DollarScore score)
        {
            // An uncalibrated scenario scale, not a fitted expected return. Preserve displayed pressure direction.
            if (score.Available) return (Math.Abs(score.Value) < 0.05 ? 0 : score.Value) / 100 * Math.Max(Math.Abs(pattern.LowerReturn), Math.Abs(pattern.UpperReturn));
            return pattern.MedianReturn;
        }

        public static List<string> CounterEvidence(DollarAnalysisResult result, DateTime nowUtc)
        {
            var reasons = new List<string>();
            if (result == null) { reasons.Add("자료를 받지 못해 결론을 낼 수 없습니다."); return reasons; }
            var periods = result.Patterns.Count > 0 ? result.Patterns :
                result.Pattern == null ? new List<DollarPattern>() : new List<DollarPattern> { result.Pattern };
            if (periods.Count == 0) reasons.Add(result.Target.Economic ? "월간 공표값을 일별 사례로 늘리지 않습니다. 정책·발표 근거를 별도로 확인합니다." : "시세 이력이 부족해 과거 사례 비율도 계산할 수 없습니다.");
            foreach (var p in periods)
            {
                string name = p.Horizon == 20 ? "월간" : p.Horizon == 5 ? "주간" : "일간";
                if (!Fresh(p, nowUtc)) reasons.Add(name + ": 표본 부족 또는 오래된 기준일로 판단을 보류합니다.");
                else if (p.ValidationCount < 60) reasons.Add(name + ": 시간 순서 검증이 " + p.ValidationCount + "회뿐이라 예측 성능을 확인하기 어렵습니다.");
                else if (p.ValidationHits <= p.BaselineHits) reasons.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}: 예측 적중 {1:0.0}%로 단순 기준 {2:0.0}%보다 높지 않았습니다.", name,
                    100.0 * p.ValidationHits / p.ValidationCount, 100.0 * p.BaselineHits / p.ValidationCount));
            }
            var daily = result.Pattern;
            if (daily != null && daily.Enough)
            {
                int lead = Best(daily);
                if ((lead > 0 && daily.FiveDayPercent < -0.3) || (lead < 0 && daily.FiveDayPercent > 0.3))
                    reasons.Add("최근 5공시일의 실제 추세는 일간 유사 사례에서 더 많았던 방향과 반대입니다.");
                if (lead != 0 && result.News.Any(n => n.Direction == -lead))
                    reasons.Add("일간 유사 사례의 우세 방향과 반대되는 뉴스 표현도 있습니다. 기사 제목만으로 어느 쪽이 맞는지 확정할 수 없습니다.");
                if (Math.Max(daily.UpPercent, Math.Max(daily.FlatPercent, daily.DownPercent)) < 55)
                    reasons.Add("일간 사례의 가장 많은 방향도 55% 미만으로, 한 방향으로 뚜렷하게 쏠리지 않았습니다.");
            }
            if (!result.DomesticAvailable || !result.GlobalAvailable) reasons.Add("국내외 뉴스 중 일부를 받지 못했습니다. 뉴스 근거가 완전하지 않습니다.");
            reasons.Add("유사 사례가 서로 겹칠 수 있고 뉴스 본문·시장 선반영은 검증하지 않았습니다. 이 비율을 미래 확률로 해석할 수 없습니다.");
            return reasons;
        }

        public static string Clean(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = Regex.Replace(value, "[\\x00-\\x1f\\x7f\\u202a-\\u202e\\u2066-\\u2069]", " ");
            value = Regex.Replace(value, "\\s+", " ").Trim();
            return value.Length <= max ? value : value.Substring(0, max);
        }

        public static bool IsNewsLink(string url)
        {
            if (DollarNewsSources.IsArticle(url)) return true;
            Uri parsed;
            return Uri.TryCreate(url, UriKind.Absolute, out parsed) && parsed.Scheme == "https" &&
                parsed.Host == "news.google.com" && parsed.IsDefaultPort && string.IsNullOrEmpty(parsed.UserInfo) &&
                (parsed.AbsolutePath.StartsWith("/rss/articles/", StringComparison.Ordinal) ||
                 parsed.AbsolutePath.StartsWith("/articles/", StringComparison.Ordinal));
        }

        public static List<DollarNews> ParseNews(string text, bool domestic, DateTime nowUtc, string publisher = null, PredictionTarget target = null)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 2 * 1024 * 1024) return null;
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 };
                var doc = new XmlDocument { XmlResolver = null };
                using (var sr = new StringReader(text))
                using (var reader = XmlReader.Create(sr, settings)) doc.Load(reader);
                if (doc.SelectSingleNode("/rss/channel") == null) return null;
                var news = new List<DollarNews>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var links = new HashSet<string>(StringComparer.Ordinal);
                foreach (XmlNode item in doc.SelectNodes("/rss/channel/item"))
                {
                    if (news.Count >= 60) break;
                    string title = Clean(NodeText(item, "title"), 240);
                    string url = NodeText(item, "link");
                    DateTimeOffset time;
                    if (title.Length < 8 || !IsNewsLink(url) ||
                        !DateTimeOffset.TryParse(NodeText(item, "pubDate"), CultureInfo.InvariantCulture,
                            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out time)) continue;
                    if (time.UtcDateTime > nowUtc || time.UtcDateTime < nowUtc.AddHours(-24)) continue;
                    if (target == null ? !Relevant(title, domestic) : !PredictionFactors.Relevant(title, target)) continue;
                    string key = Regex.Replace(title.Split(new[] { " - " }, StringSplitOptions.None)[0], "[^\\p{L}\\p{N}]", "");
                    if (!seen.Add(key) || !links.Add(url)) continue;
                    var n = new DollarNews { Target = target, Title = title, Url = url, Domestic = domestic,
                        Source = publisher ?? Clean(NodeText(item, "source"), 60), PublishedUtc = time.UtcDateTime,
                        Context = publisher == null ? ContextText(NodeText(item, "description"), title) : Clean(System.Net.WebUtility.HtmlDecode(Regex.Replace(NodeText(item, "description"), "<[^>]*>", " ")), 1800) };
                    Classify(n);
                    news.Add(n);
                }
                return news.OrderBy(n => NewsPriority(n.Source)).ThenByDescending(n => n.PublishedUtc).Take(publisher == null ? 6 : 12).ToList();
            }
            catch { return null; }
        }

        private static string NodeText(XmlNode item, string name)
        {
            var node = item.SelectSingleNode(name);
            return node == null ? "" : node.InnerText;
        }

        internal static double SourceWeight(string source)
        {
            if (Regex.IsMatch(source ?? "", "Federal Reserve|한국은행", RegexOptions.IgnoreCase)) return 1;
            return NewsPriority(source ?? "") <= 1 ? 0.9 : 0.6;
        }

        private static int NewsPriority(string source)
        {
            if (Regex.IsMatch(source, "Reuters|Bloomberg|CNBC|Financial Times|Wall Street Journal|BBC|CNN|한국경제|연합뉴스|한국은행|Federal Reserve", RegexOptions.IgnoreCase)) return 0;
            if (Regex.IsMatch(source, "YTN|KBS|MBC|SBS|서울신문|한국경제|매일경제|머니투데이|이데일리|한겨레|경향신문|조선|중앙|동아|연합인포맥스|FXStreet|MarketWatch", RegexOptions.IgnoreCase)) return 1;
            return 2;
        }

        // ★ 낱말이 다른 낱말 안에 숨어 있다 ★
        //   '한은' 은 '신한은행' 안에 있고 '관세' 는 '관세청' 안에 있다. 그래서
        //   '[게시판] 신한은행 ... 발행', '관세청 ... 적발' 같은 제목이 환율 기사
        //   자리를 차지했다(저장 기록 974b 실측, 피드당 6건 상한).
        //   요인 점수는 0 이지만 기사 수를 늘려 근거계수(coverage)를 부풀린다.
        //
        // ★ 반대로 진짜 환율 기사가 버려졌다 ★
        //   금통위·이창용·파월·FOMC·국고채·외환시장 마감 같은 제목에는 '환율' 도
        //   '달러' 도 없어서 목록에 걸리지 않았다.
        private const string DomesticTopic =
            "달러|환율|원화|한국은행|(?<![가-힣])한은|금통위|이창용|연준|파월|Powell|\\bFOMC\\b|\\bFed\\b|Federal Reserve|Warsh|워시|" +
            "Trump|트럼프|Iran|(?<![가-힣])이란|중동|우크라이나|국제유가|\\boil\\b|tariff|관세(?!청)|한미|한·미|" +
            "한국.{0,12}(?:정부|수출)|엔캐리|엔케리|일본은행|BOJ|국고채|외환시장|외환당국|무역수지|경상수지|" +
            "외국인.{0,14}(?:순매수|순매도|매수세|매도세)|(?:미국|美).{0,10}(?:고용|물가|금리|국채|CPI|일자리)|" +
            "(?:하이닉스|삼성).{0,20}(?:미국|관세|수출|공장|투자)";

        internal static bool Relevant(string title, bool domestic)
        {
            if (!domestic) return Regex.IsMatch(title, "Trump|Warsh|Iran|\\boil\\b|tariff|carry trade|Bank of Japan|Korea|\\bdollar\\b|\\bFed\\b|Federal Reserve|\\bFOMC\\b|Powell|USD/KRW|USDKRW|Korean won", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(title, "엔[/·]달러|달러[/·]엔|유로[/·]달러|달러[/·]유로") &&
                !Regex.IsMatch(title, "원[·/ㆍ-]?달러|원화|한국은행")) return false;
            return Regex.IsMatch(title, DomesticTopic, RegexOptions.IgnoreCase);
        }

        internal static string ContextText(string html, string title)
        {
            // Google descriptions often only repeat linked headlines. Do not pretend that these are article bodies.
            string plain = System.Net.WebUtility.HtmlDecode(Regex.Replace(html ?? "", "<[^>]*>", " "));
            plain = Clean(plain, 1800);
            string headline = title.Split(new[] { " - " }, StringSplitOptions.None)[0];
            if ((html ?? "").IndexOf("<ol", StringComparison.OrdinalIgnoreCase) >= 0 ||
                plain.Length <= title.Length + 100 || plain.Replace(headline, "").Trim().Length < 100) return "";
            return plain;
        }

        public static void Classify(DollarNews news)
        {
            if (news.Target != null && !news.Target.Dollar) { PredictionFactors.Classify(news); return; }
            string title = news.Title;
            // 통화와 방향 사이의 임의 문자를 허용하면 '달러 매도·엔화 강세'를 달러 강세로 오독한다.
            // 명시적으로 붙어 있는 표현만 분류하고 생략된 문맥은 추측하지 않는다.
            bool up = Regex.IsMatch(title, "(?:달러(?:화)?|환율)\\s*(?:는|은|가|이)?\\s*(?:강세|상승|급등)|원화\\s*(?:는|가)?\\s*약세|\\bdollar\\s+(?:rises|rallies|gains|strengthens|surges)\\b|\\bstronger dollar\\b", RegexOptions.IgnoreCase);
            bool down = Regex.IsMatch(title, "(?:달러(?:화)?|환율)\\s*(?:는|은|가|이)?\\s*(?:약세|하락|급락)|원화\\s*(?:는|가)?\\s*강세|\\bdollar\\s+(?:falls|drops|slides|weakens|slips)\\b|\\bweaker dollar\\b", RegexOptions.IgnoreCase);
            bool uncertain = Regex.IsMatch(title, "전망|예상|가능|우려|둔화|부인|않|아니|반전|멈|제한|\\?|\\b(?:may|might|could|not|no|denies|denied|forecast|expected|outlook|halt|halts)\\b", RegexOptions.IgnoreCase);
            news.Direction = uncertain || up == down ? 0 : up ? 1 : -1;
            bool negated = Regex.IsMatch(title, "부인|않|아니|반전|멈|제한|\\b(?:not|no|denies|denied|halt|halts)\\b", RegexOptions.IgnoreCase);
            news.EvidenceDirection = up == down || negated ? 0 : up ? 1 : -1;
            news.EvidenceWeight = news.EvidenceDirection == 0 ? 0 : uncertain ? 0.5 : 1;
            news.EvidenceReason = negated ? "부정·반전 문맥: 방향 미확정, 0점" : up && down ? "상반된 방향 혼재: 0점" :
                news.EvidenceDirection == 0 ? "USD/KRW 방향 연결 근거 부족: 0점" : uncertain ? "전망·조건부 표현: 50% 가중" : "명시된 방향: 기본 가중";
            news.Topic = Regex.IsMatch(title, "금리|연준|한국은행|\\bFed\\b|Federal Reserve|\\brates?\\b", RegexOptions.IgnoreCase) ? "금리·통화정책" :
                Regex.IsMatch(title, "물가|고용|inflation|payroll|employment|\\bCPI\\b", RegexOptions.IgnoreCase) ? "물가·고용" :
                Regex.IsMatch(title, "관세|수출|무역|tariff|trade|export", RegexOptions.IgnoreCase) ? "무역·수출입" :
                Regex.IsMatch(title, "전쟁|중동|유가|war|oil|geopolit", RegexOptions.IgnoreCase) ? "국제 정세·유가" : "환율 동향";
            news.Factors = DollarFactors.Analyze(news);
            if (news.Factors.Count > 0)
            {
                double impact = news.Factors.Sum(f => f.Direction * f.Weight) / news.Factors.Count;
                news.EvidenceDirection = Math.Sign(impact);
                news.EvidenceWeight = Math.Abs(impact);
                news.Topic = string.Join("·", news.Factors.Select(f => f.Category).Distinct());
                news.EvidenceReason = string.Join("\n", news.Factors.Select(f => f.Mechanism + "\n반대: " + f.Counter));
            }
        }
    }
}
