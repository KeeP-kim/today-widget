using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace DeskWidget
{
    internal sealed class PredictionPoint
    {
        internal int Horizon;
        internal double Value, Low, High;
        internal DateTime Date;
    }

    internal static class PredictionData
    {
        internal static async Task<DollarAnalysisResult> FetchAsync(PredictionTarget target, string bank, CancellationToken ct)
        {
            if (target.Dollar)
            {
                var dollar = await DollarAnalysis.FetchAsync(bank, ct);
                dollar.Target = target;
                dollar.HistorySource = "Frankfurter / ECB";
                return dollar;
            }
            var now = DateTime.UtcNow;
            var result = new DollarAnalysisResult { Target = target, CheckedUtc = now, HistorySource = target.Economic ? "한국은행 ECOS 공표값" : target.Weather ? "Open-Meteo 일별 예보" : target.Def.Kind == SourceKind.Coin ? "업비트 확정 일봉" : "네이버 일별 시세" };
            var quote = Sources.FetchPredictionQuoteAsync(target.Def, bank, ct);
            var history = HistoryAsync(target, bank, now, ct);
            var news = target.Weather ? Task.FromResult(0) : NewsAsync(result, now, ct);
            // 달러 강세는 코스피·코인·미국 주식에도 걸리는 맥락이다. 날씨에는 뜻이 없으니 뺀다.
            DateTime koreaToday = DollarAnalysis.KoreaDate(now);
            var strength = target.Weather ? Task.FromResult(JNode.Empty)
                : Net.GetJsonAsync(DollarAnalysis.StrengthUrl(koreaToday), ct);
            // 미 국채금리도 코스피·코인·미국 주식에 똑같이 걸리는 맥락이다.
            var yields = (target.Weather ? new DateTime[0] : new[] { koreaToday, koreaToday.AddMonths(-1), koreaToday.AddMonths(-2) })
                .Select(m => Net.GetSlowTextAsync(DollarAnalysis.YieldUrl(m), ct)).ToArray();
            await Task.WhenAll(new Task[] { quote, history, news, strength }.Concat(yields));
            result.Quote = quote.Result; result.Rates = history.Result;
            result.Context.DollarIndex = DollarAnalysis.ParseStrength(strength.Result, koreaToday);
            var yieldDocs = yields.Select(t => t.Result).ToList();
            result.Context.Yield10Y = DollarAnalysis.JoinYields(yieldDocs, koreaToday, "BC_10YEAR");
            result.Context.Yield2Y = DollarAnalysis.JoinYields(yieldDocs, koreaToday, "BC_2YEAR");
            if (target.Weather)
            {
                foreach (int h in new[] { 1, 5, 20 })
                {
                    var day = result.Rates.FirstOrDefault(p => p.Date == DollarAnalysis.KoreaDate(now).AddDays(target.Steps(h)));
                    if (day != null) result.DirectForecasts.Add(new PredictionPoint { Horizon = h, Date = day.Date, Value = day.Value, Low = day.Value, High = day.Value });
                }
                result.Rates.Clear();
            }
            else if (!target.Economic)
                foreach (int h in new[] { 1, 5, 20 })
                {
                    var pattern = DollarAnalysis.Analyze(result.Rates, target.Steps(h));
                    if (pattern != null) { pattern.Horizon = h; result.Patterns.Add(pattern); }
                }
            result.Pattern = result.Patterns.FirstOrDefault(p => p.Horizon == 1);
            ct.ThrowIfCancellationRequested();
            return result;
        }

        private static async Task<int> NewsAsync(DollarAnalysisResult result, DateTime now, CancellationToken ct)
        {
            var t = result.Target;
            string[] queries = { "(" + t.SearchName + ")", "(" + t.SearchName + ") (전망 OR 실적 OR 금리 OR outlook OR earnings)",
                "(" + t.SearchName + ") (하락 OR 위험 OR 부진 OR risk OR downgrade)",
                "(" + t.SearchName + ") (상승 OR 성장 OR 호재 OR growth OR upgrade)",
                t.Economic ? "(물가 OR 고용 OR inflation OR employment) (" + t.SearchName + ")" :
                t.Def.Kind == SourceKind.Fx ? "(연준 OR 한은 OR 일본은행 OR Fed OR BOJ) (금리 OR rates)" : "(연준 OR Fed OR 관세 OR tariff OR 중동) (증시 OR stocks OR crypto OR market)" };
            var requests = queries.Select((q, i) => Net.GetTextAsync("https://news.google.com/rss/search?q=" + Uri.EscapeDataString(q + " when:1d") +
                (i % 2 == 0 ? "&hl=ko&gl=KR&ceid=KR:ko" : "&hl=en-US&gl=US&ceid=US:en"), ct)).ToArray();
            var direct = DollarNewsSources.Feeds.Select(url => Net.GetTextAsync(url, ct)).ToArray();
            await Task.WhenAll(requests.Concat(direct));
            result.TopicFeedsExpected = requests.Length + direct.Length;
            for (int i = 0; i < requests.Length; i++)
            {
                var parsed = DollarAnalysis.ParseNews(requests[i].Result, i % 2 == 0, now, null, t);
                if (parsed == null) continue;
                result.TopicFeedsAvailable++;
                if (i % 2 == 0) result.DomesticAvailable = true; else result.GlobalAvailable = true;
                result.News.AddRange(parsed);
            }
            for (int i = 0; i < direct.Length; i++)
            {
                var parsed = DollarAnalysis.ParseNews(direct[i].Result, i < 4, now, DollarNewsSources.Publisher(i), t);
                if (parsed == null) continue;
                result.TopicFeedsAvailable++;
                result.News.AddRange(parsed);
            }
            result.News = result.News.OrderByDescending(n => DollarNewsSources.IsArticle(n.Url))
                .GroupBy(n => System.Text.RegularExpressions.Regex.Replace(n.Title.Split(new[] { " - " }, StringSplitOptions.None)[0], @"[^\p{L}\p{N}]", "").ToLowerInvariant()).Select(g => g.First()).ToList();
            await DollarNewsSources.EnrichAsync(result.News, ct);
            return result.News.Count;
        }

        internal static async Task<List<DollarRate>> HistoryAsync(PredictionTarget target, string bank, DateTime now, CancellationToken ct, Func<string, CancellationToken, Task<JNode>> jsonFetch = null)
        {
            var fetchJson = jsonFetch ?? Net.GetJsonAsync;
            var def = target.Def; DateTime today = DollarAnalysis.KoreaDate(now);
            if (target.Weather)
            {
                if (double.IsNaN(def.Lat) || double.IsNaN(def.Lon)) return new List<DollarRate>();
                string url = string.Format(CultureInfo.InvariantCulture, "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&daily=temperature_2m_mean&timezone=Asia%2FSeoul&forecast_days=16", def.Lat, def.Lon);
                var daily = (await fetchJson(url, ct))["daily"];
                var values = new List<DollarRate>();
                for (int i = 0; i < daily["time"].Count; i++)
                {
                    DateTime date; double value = daily["temperature_2m_mean"][i].D;
                    if (DateTime.TryParseExact(daily["time"][i].S, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && !double.IsNaN(value) && value >= -100 && value <= 65 && date >= today && date <= today.AddDays(15))
                        values.Add(new DollarRate { Date = date, Value = value });
                }
                return values;
            }
            if (target.Economic)
            {
                if (string.IsNullOrEmpty(Sources.EcosKey) || !def.Code.StartsWith("INTL:") || !Config.IsSafeCode(def.Code.Substring(5))) return new List<DollarRate>();
                string url = "https://ecos.bok.or.kr/api/StatisticSearch/" + Sources.EcosKey + "/json/kr/1/100/902Y006/M/" + today.AddYears(-8).ToString("yyyyMM") + "/" + today.ToString("yyyyMM") + "/" + def.Code.Substring(5);
                var rows = (await fetchJson(url, ct))["StatisticSearch"]["row"];
                var values = new List<DollarRate>();
                for (int i = 0; i < rows.Count; i++)
                {
                    DateTime date; double value = rows[i]["DATA_VALUE"].D;
                    if (DateTime.TryParseExact(rows[i]["TIME"].S, "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && !double.IsNaN(value) && Math.Abs(value) <= 100 && date <= today)
                        values.Add(new DollarRate { Date = date, Value = value });
                }
                return values.OrderBy(p => p.Date).ToList();
            }
            if (def.Kind == SourceKind.DomesticStock || def.Kind == SourceKind.Index)
            {
                string xml = await Net.GetTextAsync("https://fchart.stock.naver.com/sise.nhn?symbol=" + Uri.EscapeDataString(def.Code) + "&timeframe=day&count=760&requestType=0", ct);
                return ParseChart(xml, def.Code, today);
            }
            var raw = new List<DollarRate>();
            if (def.Kind == SourceKind.Coin)
            {
                DateTime before = now.Date;
                for (int page = 0; page < 4; page++)
                {
                    var json = await fetchJson("https://api.upbit.com/v1/candles/days?market=" + Uri.EscapeDataString(def.Code) + "&count=200&to=" + Uri.EscapeDataString(before.ToString("yyyy-MM-ddTHH:mm:ss") + "Z"), ct);
                    var values = ParsePrices(json, today, def.Code);
                    if (values.Count == 0) break;
                    raw.AddRange(values); before = values.Min(v => v.Date);
                    await Task.Delay(120, ct);
                }
            }
            else
            {
                for (int page = 1; page <= 10; page++)
                {
                    string url = def.Kind == SourceKind.Fx ? "https://m.stock.naver.com/front-api/marketIndex/prices?category=exchange&reutersCode=" + Uri.EscapeDataString(def.Code + (bank == "SHB" ? "_SHB" : "")) + "&page=" + page + "&pageSize=60" :
                        "https://api.stock.naver.com/stock/" + Uri.EscapeDataString(def.Code) + "/price?page=" + page + "&pageSize=60";
                    var json = await fetchJson(url, ct);
                    if (def.Kind == SourceKind.Fx) json = json["result"];
                    var values = ParsePrices(json, today, null);
                    if (values.Count == 0) break;
                    raw.AddRange(values);
                    if (json.Count < 60) break;
                }
            }
            return Validate(raw, today);
        }

        internal static List<DollarRate> ParsePrices(JNode rows, DateTime today, string coin)
        {
            var values = new List<DollarRate>();
            for (int i = 0; i < rows.Count && i < 1000; i++)
            {
                if (coin != null && rows[i]["market"].S != coin) return new List<DollarRate>();
                string stamp = rows[i][coin == null ? "localTradedAt" : "candle_date_time_utc"].S ?? "";
                DateTime date; double value = rows[i][coin == null ? "closePrice" : "trade_price"].D;
                if (stamp.Length < 10 || !DateTime.TryParseExact(stamp.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return new List<DollarRate>();
                values.Add(new DollarRate { Date = date, Value = value });
            }
            return Validate(values, today);
        }

        internal static List<DollarRate> ParseChart(string xml, string code, DateTime today)
        {
            if (string.IsNullOrEmpty(xml)) return new List<DollarRate>();
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2000000 })) document.Load(reader);
                var chart = document.SelectSingleNode("/protocol/chartdata");
                if (chart == null || chart.Attributes["symbol"].Value != code) return new List<DollarRate>();
                var values = new List<DollarRate>();
                foreach (XmlNode item in chart.SelectNodes("item"))
                {
                    string[] parts = item.Attributes["data"].Value.Split('|'); DateTime date; double value;
                    if (parts.Length < 5 || !DateTime.TryParseExact(parts[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) || !double.TryParse(parts[4], NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return new List<DollarRate>();
                    values.Add(new DollarRate { Date = date, Value = value });
                }
                return Validate(values, today);
            }
            catch { return new List<DollarRate>(); }
        }

        internal static List<DollarRate> Validate(List<DollarRate> values, DateTime today)
        {
            if (values.Count > 1200 || values.Any(p => double.IsNaN(p.Value) || double.IsInfinity(p.Value) || p.Value <= 0)) return new List<DollarRate>();
            if (values.GroupBy(p => p.Date).Any(g => g.Select(p => p.Value).Distinct().Count() > 1)) return new List<DollarRate>();
            // 오늘 미확정 값은 과거 학습에 넣지 않는다. 현재 값은 별도 공유 시세만 사용한다.
            var ordered = values.Where(p => p.Date < today && p.Date >= today.AddYears(-4)).GroupBy(p => p.Date).Select(g => g.First()).OrderBy(p => p.Date).ToList();
            // 미조정 분할/합병 또는 깨진 시계열을 큰 방향 신호로 오독하지 않는다.
            for (int i = 1; i < ordered.Count; i++) if (ordered[i].Value / ordered[i - 1].Value > 1.6 || ordered[i].Value / ordered[i - 1].Value < 0.625) return new List<DollarRate>();
            return ordered;
        }
    }
}
