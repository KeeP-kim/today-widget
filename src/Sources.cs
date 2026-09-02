// 시세/날씨 데이터 소스. 전부 무료·무인증 공개 엔드포인트이며 HTTPS만 사용한다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace DeskWidget
{
    internal enum SourceKind
    {
        Fx,             // 네이버 환율 (하나/신한 고시)
        Index,          // 네이버 국내지수 (코스피, 코스닥)
        DomesticStock,  // 네이버 국내주식 (005930 등)
        WorldStock,     // 네이버 해외주식 (DLTR.O 등)
        Coin,           // 업비트 원화마켓 (KRW-DOGE 등)
        Weather,        // 지역 날씨 (Open-Meteo, 좌표 기준)
        Ecos,           // 한국은행 ECOS 통계 (금리·물가 등, 인증키 필요)
    }

    internal sealed class SymbolDef
    {
        public SourceKind Kind;
        public string Code;      // 엔드포인트에 들어가는 코드 (날씨는 네이버 날씨 지역코드)
        public string Label;     // 화면에 쓰는 이름

        // 날씨 전용. 날씨는 좌표로 조회하고 코드(지역코드)는 링크에만 쓴다.
        public double Lat = double.NaN;
        public double Lon = double.NaN;

        public string Key { get { return KindName(Kind) + ":" + Code; } }

        /// <summary>환율만 하나/신한 전환이 된다.</summary>
        public bool BankSwitchable { get { return Kind == SourceKind.Fx; } }

        public SymbolDef() { }

        public SymbolDef(SourceKind kind, string code, string label)
        {
            Kind = kind; Code = code; Label = label;
        }

        public static string KindName(SourceKind k)
        {
            switch (k)
            {
                case SourceKind.Fx: return "fx";
                case SourceKind.Index: return "index";
                case SourceKind.DomesticStock: return "dstock";
                case SourceKind.WorldStock: return "wstock";
                case SourceKind.Coin: return "coin";
                case SourceKind.Weather: return "weather";
                case SourceKind.Ecos: return "ecos";
            }
            return "fx";
        }

        public static bool TryParseKind(string s, out SourceKind k)
        {
            switch (s)
            {
                case "fx": k = SourceKind.Fx; return true;
                case "index": k = SourceKind.Index; return true;
                case "dstock": k = SourceKind.DomesticStock; return true;
                case "wstock": k = SourceKind.WorldStock; return true;
                case "coin": k = SourceKind.Coin; return true;
                case "weather": k = SourceKind.Weather; return true;
                case "ecos": k = SourceKind.Ecos; return true;
            }
            k = SourceKind.Fx;
            return false;
        }

        /// <summary>헤더에 쓰는 조금 더 긴 이름.</summary>
        public string Header
        {
            get
            {
                switch (Kind)
                {
                    case SourceKind.Fx:
                        return Code == "FX_USDKRW" ? "USD / KRW"
                             : Code == "FX_JPYKRW" ? "JPY 100 / KRW"
                             : Label;
                    case SourceKind.Coin: return Label + " / KRW";
                    default: return Label;
                }
            }
        }
    }

    internal sealed class Quote
    {
        public bool Ok;
        public string Price = "- - - -";  // 표시용 (콤마 포함)
        public string Diff;               // 전일대비 절대값 표시용
        public string Ratio;              // 등락률 (부호 포함, % 제외)
        public int Dir;                   // 1 상승 / -1 하락 / 0 보합
        public string Time;               // "22:31"
        public string Source;             // "하나은행", "업비트", "나스닥" 등
        public string Link;               // 더블클릭 시 열 URL
        public string RatioSuffix = "%";  // 날씨처럼 % 가 아닌 경우를 위해
    }

    internal sealed class WeatherInfo
    {
        public bool Ok;
        public double Temp, Feels, Max, Min;
        public int Hum, Code;
        public bool IsDay;
    }

    internal sealed class GeoInfo
    {
        public double Lat, Lon;
        public string City;
    }

    /// <summary>종목 검색 결과 한 건.</summary>
    internal sealed class SearchHit
    {
        public SymbolDef Def;
        public string TypeName;   // "코스피", "나스닥 증권거래소", "업비트" 등
    }

    internal static class Sources
    {
        /// <summary>첫 실행 때 채워 넣는 기본 종목.</summary>
        public static List<SymbolDef> Defaults()
        {
            return new List<SymbolDef>
            {
                new SymbolDef(SourceKind.Fx,         "FX_USDKRW", "달러"),
                new SymbolDef(SourceKind.Fx,         "FX_JPYKRW", "엔화 100"),
                new SymbolDef(SourceKind.Index,      "KOSPI",     "코스피"),
                new SymbolDef(SourceKind.Coin,       "KRW-DOGE",  "도지코인"),
                new SymbolDef(SourceKind.WorldStock, "DLTR.O",    "달러트리"),
            };
        }

        // ---------- 시세 ----------

        public static async Task<Quote> FetchAsync(SymbolDef def, string bank, CancellationToken ct)
        {
            switch (def.Kind)
            {
                case SourceKind.Fx:            return await FetchFx(def, bank, ct).ConfigureAwait(false);
                case SourceKind.Index:         return await FetchIndex(def, ct).ConfigureAwait(false);
                case SourceKind.DomesticStock: return await FetchDomestic(def, ct).ConfigureAwait(false);
                case SourceKind.WorldStock:    return await FetchWorld(def, ct).ConfigureAwait(false);
                case SourceKind.Coin:          return await FetchUpbit(def, ct).ConfigureAwait(false);
                case SourceKind.Weather:       return await FetchWeatherQuote(def, ct).ConfigureAwait(false);
                case SourceKind.Ecos:          return await FetchEcos(def, ct).ConfigureAwait(false);
            }
            return new Quote();
        }

        // ---------- 한국은행 ECOS ----------

        /// <summary>ECOS 오픈API 인증키. 설정에서 읽어 시작할 때 넣어준다.</summary>
        public static string EcosKey;

        // 100대 통계지표는 한 번 부르면 100개가 통째로 오므로 캐시해서 여러 항목이 나눠 쓴다.
        private static List<string[]> _keyStats;      // [이름, 값, 단위, 시점, 분류]
        private static DateTime _keyStatsAt = DateTime.MinValue;
        private static readonly SemaphoreSlim _keyStatsLock = new SemaphoreSlim(1, 1);

        private static async Task<List<string[]>> GetKeyStatsAsync(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(EcosKey)) return null;
            if (_keyStats != null && (DateTime.UtcNow - _keyStatsAt).TotalMinutes < 30) return _keyStats;

            await _keyStatsLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_keyStats != null && (DateTime.UtcNow - _keyStatsAt).TotalMinutes < 30) return _keyStats;

                string url = "https://ecos.bok.or.kr/api/KeyStatisticList/" + EcosKey + "/json/kr/1/100";
                var j = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
                var rows = j["KeyStatisticList"]["row"];

                var list = new List<string[]>();
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    string name = r["KEYSTAT_NAME"].S;
                    if (string.IsNullOrEmpty(name)) continue;
                    list.Add(new string[]
                    {
                        name, r["DATA_VALUE"].S ?? "", r["UNIT_NAME"].S ?? "",
                        r["CYCLE"].S ?? "", r["CLASS_NAME"].S ?? "",
                    });
                }
                if (list.Count > 0) { _keyStats = list; _keyStatsAt = DateTime.UtcNow; }
                return _keyStats;
            }
            catch (OperationCanceledException) { throw; }
            catch { return _keyStats; }
            finally { _keyStatsLock.Release(); }
        }

        private static async Task<Quote> FetchEcos(SymbolDef def, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(EcosKey)) return new Quote();

            if (def.Code.StartsWith("INTL:", StringComparison.Ordinal))
            {
                // ECOS 코드는 통계 항목 이름이라 한글이 들어갈 수 있어 설정을 읽을 때는 검사를 건너뛴다.
                // 그중 이 갈래만 URL 경로로 들어가므로 여기서 따로 거른다.
                string cc = def.Code.Substring(5);
                if (!Config.IsSafeCode(cc)) return new Quote();
                return await FetchEcosIntl(cc, ct).ConfigureAwait(false);
            }

            string name = def.Code.StartsWith("KEY:", StringComparison.Ordinal)
                        ? def.Code.Substring(4) : def.Code;

            var stats = await GetKeyStatsAsync(ct).ConfigureAwait(false);
            if (stats == null) return new Quote();

            foreach (var row in stats)
            {
                if (!string.Equals(row[0], name, StringComparison.Ordinal)) continue;
                if (string.IsNullOrEmpty(row[1])) return new Quote();

                return new Quote
                {
                    Ok = true,
                    Price = row[1] + (string.IsNullOrEmpty(row[2]) ? "" : " " + row[2]),
                    Ratio = FormatEcosTime(row[3]),
                    RatioSuffix = "",
                    Dir = 0,
                    Time = FormatEcosTime(row[3]),
                    Source = "한국은행",
                    Link = "https://ecos.bok.or.kr/",
                };
            }
            return new Quote();
        }

        /// <summary>국제 주요국 중앙은행 정책금리 (902Y006, 월간).</summary>
        private static async Task<Quote> FetchEcosIntl(string country, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(country)) return new Quote();

            string to = DateTime.Now.ToString("yyyyMM", CultureInfo.InvariantCulture);
            string from = DateTime.Now.AddMonths(-10).ToString("yyyyMM", CultureInfo.InvariantCulture);
            string url = "https://ecos.bok.or.kr/api/StatisticSearch/" + EcosKey
                       + "/json/kr/1/12/902Y006/M/" + from + "/" + to + "/" + country;

            var j = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
            var rows = j["StatisticSearch"]["row"];
            if (rows.Count == 0) return new Quote();

            var last = rows[rows.Count - 1];
            string val = last["DATA_VALUE"].S;
            if (string.IsNullOrEmpty(val)) return new Quote();

            return new Quote
            {
                Ok = true,
                Price = val + " %",
                Ratio = FormatEcosTime(last["TIME"].S),
                RatioSuffix = "",
                Dir = 0,
                Time = FormatEcosTime(last["TIME"].S),
                Source = "한국은행",
                Link = "https://ecos.bok.or.kr/",
            };
        }

        /// <summary>"20260821" → "08.21", "202607" → "26.07"</summary>
        private static string FormatEcosTime(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            if (t.Length == 8) return t.Substring(4, 2) + "." + t.Substring(6, 2);
            if (t.Length == 6) return t.Substring(2, 2) + "." + t.Substring(4, 2);
            return t;
        }

        /// <summary>목록에 넣은 지역 날씨. 시세 자리에 기온을, 등락률 자리에 날씨 상태를 보여준다.</summary>
        private static async Task<Quote> FetchWeatherQuote(SymbolDef def, CancellationToken ct)
        {
            if (double.IsNaN(def.Lat) || double.IsNaN(def.Lon)) return new Quote();

            var w = await FetchWeatherAsync(def.Lat, def.Lon, ct).ConfigureAwait(false);
            if (!w.Ok) return new Quote();

            string link = "https://weather.naver.com/";
            if (!string.IsNullOrEmpty(def.Code)) link = "https://weather.naver.com/today/" + def.Code;

            return new Quote
            {
                Ok = true,
                Price = w.Temp.ToString("0.#", CultureInfo.InvariantCulture) + "°",
                Diff = null,
                Ratio = WeatherText(w.Code),
                RatioSuffix = "",
                Dir = 0,
                Time = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
                Source = "날씨",
                Link = link,
            };
        }

        /// <summary>WMO 코드를 짧은 우리말로. (아이콘 쪽 설명과 같은 표를 쓴다)</summary>
        private static string WeatherText(int code)
        {
            string s = WeatherIcon.Describe(code);
            return string.IsNullOrEmpty(s) ? "-" : s;
        }

        /// <summary>네이버 환율. reutersCode 에 _SHB 를 붙이면 신한은행 고시가 된다.</summary>
        private static async Task<Quote> FetchFx(SymbolDef def, string bank, CancellationToken ct)
        {
            string code = def.Code + (bank == "SHB" ? "_SHB" : "");
            string url = "https://m.stock.naver.com/front-api/marketIndex/productDetail" +
                         "?category=exchange&reutersCode=" + code;

            var j = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
            var r = j["result"];
            if (!j["isSuccess"].B || !r.Exists) return new Quote();

            string price = r["closePrice"].S;
            if (string.IsNullOrEmpty(price)) return new Quote();

            return new Quote
            {
                Ok = true,
                Price = price,
                Dir = DirFromName(r["fluctuationsType"]["name"].S, r["fluctuations"].D),
                Diff = FormatAbs(r["fluctuations"].D, 2),
                Ratio = FormatSigned(r["fluctuationsRatio"].D, 2),
                Time = TimeOf(r["localTradedAt"].S),
                Source = r["stockExchangeType"]["nameKor"].S ?? (bank == "SHB" ? "신한은행" : "하나은행"),
                Link = SafeLink(r["endUrl"].S, "https://m.stock.naver.com/marketindex/exchange/" + code),
            };
        }

        /// <summary>국내지수 (코스피/코스닥).</summary>
        private static async Task<Quote> FetchIndex(SymbolDef def, CancellationToken ct)
        {
            return await FetchPolling(
                "https://polling.finance.naver.com/api/realtime/domestic/index/" + def.Code,
                def.Label,
                "https://m.stock.naver.com/domestic/index/" + def.Code + "/total",
                ct).ConfigureAwait(false);
        }

        /// <summary>국내주식. 지수와 응답 스키마가 같다.</summary>
        private static async Task<Quote> FetchDomestic(SymbolDef def, CancellationToken ct)
        {
            return await FetchPolling(
                "https://polling.finance.naver.com/api/realtime/domestic/stock/" + def.Code,
                null,
                "https://m.stock.naver.com/domestic/stock/" + def.Code + "/total",
                ct).ConfigureAwait(false);
        }

        private static async Task<Quote> FetchPolling(string url, string sourceName, string link, CancellationToken ct)
        {
            var j = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
            var d = j["datas"][0];
            string price = d["closePrice"].S;
            if (string.IsNullOrEmpty(price)) return new Quote();

            double diff = d["compareToPreviousClosePrice"].D;
            bool closed = d["marketStatus"].S == "CLOSE";

            return new Quote
            {
                Ok = true,
                Price = price,
                Dir = DirFromNaverCode(d["compareToPreviousPrice"]["code"].S, diff),
                Diff = FormatAbs(diff, 2),
                Ratio = FormatSigned(d["fluctuationsRatio"].D, 2),
                Time = closed ? "장마감" : DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
                Source = sourceName ?? (d["stockExchangeType"]["nameKor"].S ?? "네이버"),
                Link = link,
            };
        }

        /// <summary>해외주식 (달러트리, 엔비디아 등).</summary>
        private static async Task<Quote> FetchWorld(SymbolDef def, CancellationToken ct)
        {
            string url = "https://api.stock.naver.com/stock/" + def.Code + "/basic";
            var j = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
            string price = j["closePrice"].S;
            if (string.IsNullOrEmpty(price)) return new Quote();

            double diff = j["compareToPreviousClosePrice"].D;
            bool closed = j["marketStatus"].S == "CLOSE";

            return new Quote
            {
                Ok = true,
                Price = "$" + price,
                Dir = DirFromNaverCode(j["compareToPreviousPrice"]["code"].S, diff),
                Diff = FormatAbs(diff, 2),
                Ratio = FormatSigned(j["fluctuationsRatio"].D, 2),
                Time = closed ? "장마감" : (j["delayTimeName"].S ?? ""),
                Source = j["stockExchangeType"]["nameKor"].S ?? "해외주식",
                Link = "https://m.stock.naver.com/worldstock/stock/" + def.Code + "/total",
            };
        }

        /// <summary>업비트 원화 마켓 시세.</summary>
        private static async Task<Quote> FetchUpbit(SymbolDef def, CancellationToken ct)
        {
            string url = "https://api.upbit.com/v1/ticker?markets=" + def.Code;
            var j = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
            var t = j[0];
            double price = t["trade_price"].D;
            if (double.IsNaN(price)) return new Quote();

            double diff = t["signed_change_price"].D;
            double rate = t["signed_change_rate"].D * 100.0;   // 비율 → %
            string chg = t["change"].S;

            string ticker = def.Code.StartsWith("KRW-", StringComparison.Ordinal)
                          ? def.Code.Substring(4) : def.Code;

            return new Quote
            {
                Ok = true,
                Price = price.ToString(price >= 1000 ? "N0" : "N1", CultureInfo.InvariantCulture),
                Dir = chg == "RISE" ? 1 : (chg == "FALL" ? -1 : 0),
                Diff = FormatAbs(diff, price >= 1000 ? 0 : 1),
                Ratio = FormatSigned(rate, 2),
                Time = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
                Source = "업비트",
                Link = "https://stock.naver.com/crypto/UPBIT/" + ticker + "/price",
            };
        }

        // ---------- 종목 검색 ----------

        // 네이버 자동완성은 주식·지수·코인만 다루고 환율은 결과에 넣어주지 않는다.
        // 그래서 주요 통화는 목록으로 들고 있다가 검색어와 맞춰본다.
        // (코드는 전부 실제 호출로 확인한 것들이다)
        private static readonly string[,] Currencies =
        {
            { "FX_USDKRW", "달러",       "달러 미국 usd dollar 미국달러" },
            { "FX_JPYKRW", "엔화 100",   "엔 엔화 일본 jpy yen 100엔" },
            { "FX_EURKRW", "유로",       "유로 유럽 eur euro 유럽연합" },
            { "FX_CNYKRW", "위안",       "위안 위안화 중국 cny yuan" },
            { "FX_GBPKRW", "파운드",     "파운드 영국 gbp pound" },
            { "FX_AUDKRW", "호주달러",   "호주 호주달러 aud" },
            { "FX_CADKRW", "캐나다달러", "캐나다 캐나다달러 cad" },
            { "FX_HKDKRW", "홍콩달러",   "홍콩 홍콩달러 hkd" },
            { "FX_CHFKRW", "스위스프랑", "스위스 프랑 chf franc" },
            { "FX_SGDKRW", "싱가포르달러", "싱가포르 sgd" },
            { "FX_TWDKRW", "대만달러",   "대만 twd" },
            { "FX_THBKRW", "태국바트",   "태국 바트 thb baht" },
            { "FX_VNDKRW", "베트남동 100", "베트남 동 vnd" },
        };

        /// <summary>내장 통화 목록에서 검색어와 맞는 것을 찾는다.</summary>
        private static void AddCurrencyMatches(string query, List<SearchHit> into)
        {
            string q = query.Trim().ToLowerInvariant();
            if (q.Length == 0) return;

            for (int i = 0; i < Currencies.GetLength(0); i++)
            {
                string code = Currencies[i, 0];
                string label = Currencies[i, 1];
                string alias = Currencies[i, 2];

                bool hit = alias.IndexOf(q, StringComparison.Ordinal) >= 0
                        || label.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0
                        || code.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0;
                if (!hit) continue;

                into.Add(new SearchHit
                {
                    Def = new SymbolDef(SourceKind.Fx, code, label),
                    TypeName = "환율",
                });
            }
        }

        /// <summary>
        /// 네이버 종목 자동완성. 한글로 검색된다.
        /// 예: "엔비디아" → NVDA.O(나스닥), "삼성전자" → 005930(코스피), "비트코인" → 업비트
        /// </summary>
        public static async Task<List<SearchHit>> SearchAsync(string query, CancellationToken ct)
        {
            var list = new List<SearchHit>();
            if (string.IsNullOrEmpty(query)) return list;

            // 환율은 자동완성에 없으므로 내장 목록에서 먼저 채운다
            AddCurrencyMatches(query, list);

            string url = "https://ac.stock.naver.com/ac?target=stock,index,marketindex,coin&q="
                       + Uri.EscapeDataString(query);

            var j = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
            var items = j["items"];

            for (int i = 0; i < items.Count && list.Count < 12; i++)
            {
                var it = items[i];
                string category = it["category"].S;
                string code = it["code"].S;
                string reuters = it["reutersCode"].S;
                string name = it["name"].S;
                string nation = it["nationCode"].S;
                string typeName = it["typeName"].S;
                if (string.IsNullOrEmpty(name)) continue;

                SymbolDef def = null;

                if (category == "coin")
                {
                    // reutersCode 예: BTC_KRW_UPBIT / BTC_KRW_BITHUMB → 업비트만 받는다
                    if (reuters == null || reuters.IndexOf("UPBIT", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (string.IsNullOrEmpty(code)) continue;
                    def = new SymbolDef(SourceKind.Coin, "KRW-" + code, name);
                    typeName = "업비트";
                }
                else if (category == "index")
                {
                    if (string.IsNullOrEmpty(code)) continue;
                    def = new SymbolDef(SourceKind.Index, code, name);
                }
                else if (category == "marketindex")
                {
                    if (string.IsNullOrEmpty(reuters)) continue;
                    def = new SymbolDef(SourceKind.Fx, reuters, name);
                }
                else if (category == "stock")
                {
                    if (nation == "KOR")
                    {
                        if (string.IsNullOrEmpty(code)) continue;
                        def = new SymbolDef(SourceKind.DomesticStock, code, name);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(reuters)) continue;
                        def = new SymbolDef(SourceKind.WorldStock, reuters, name);
                    }
                }

                if (def != null) list.Add(new SearchHit { Def = def, TypeName = typeName ?? "" });
            }

            // 한국은행 통계(금리 등)도 함께 제안한다
            try { await AddEcosMatches(query, list, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { }

            // 날씨 지역은 여기 섞지 않는다. 날씨는 날씨 영역의 + 버튼에서 따로 추가한다.
            return list;
        }

        // 국제 정책금리로 바로 고를 수 있게 해두는 주요국
        private static readonly string[,] IntlRates =
        {
            { "US", "미국 정책금리",  "미국 연준 fed us 정책금리 금리" },
            { "KR", "한국 기준금리",  "한국 한은 기준금리 금리 kr" },
            { "JP", "일본 정책금리",  "일본 jp 정책금리 금리" },
            { "XM", "유로 정책금리",  "유로 유럽 ecb xm 정책금리 금리" },
            { "CN", "중국 정책금리",  "중국 cn 정책금리 금리" },
        };

        private static async Task AddEcosMatches(string query, List<SearchHit> into, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(EcosKey)) return;

            string q = query.Trim().ToLowerInvariant();
            if (q.Length == 0) return;

            for (int i = 0; i < IntlRates.GetLength(0); i++)
            {
                if (IntlRates[i, 2].IndexOf(q, StringComparison.Ordinal) < 0 &&
                    IntlRates[i, 1].ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0) continue;

                into.Add(new SearchHit
                {
                    Def = new SymbolDef(SourceKind.Ecos, "INTL:" + IntlRates[i, 0], IntlRates[i, 1]),
                    TypeName = "한국은행 · 정책금리",
                });
            }

            var stats = await GetKeyStatsAsync(ct).ConfigureAwait(false);
            if (stats == null) return;

            foreach (var row in stats)
            {
                if (into.Count >= 24) break;
                if (row[0].ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0) continue;

                string label = row[0].Length > 18 ? row[0].Substring(0, 18) : row[0];
                into.Add(new SearchHit
                {
                    Def = new SymbolDef(SourceKind.Ecos, "KEY:" + row[0], label),
                    TypeName = "한국은행 · " + row[4],
                });
            }
        }

        // ---------- 날씨 ----------

        public static async Task<WeatherInfo> FetchWeatherAsync(double lat, double lon, CancellationToken ct)
        {
            string url = "https://api.open-meteo.com/v1/forecast"
                       + "?latitude=" + lat.ToString("0.####", CultureInfo.InvariantCulture)
                       + "&longitude=" + lon.ToString("0.####", CultureInfo.InvariantCulture)
                       + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code"
                       + "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1";

            var j = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
            var c = j["current"];
            if (!c.Exists) return new WeatherInfo();

            double temp = c["temperature_2m"].D;
            if (double.IsNaN(temp)) return new WeatherInfo();

            return new WeatherInfo
            {
                Ok = true,
                Temp = temp,
                Feels = c["apparent_temperature"].D,
                Hum = (int)Round0(c["relative_humidity_2m"].D),
                IsDay = c["is_day"].D >= 0.5,
                Code = (int)Round0(c["weather_code"].D),
                Max = j["daily"]["temperature_2m_max"][0].D,
                Min = j["daily"]["temperature_2m_min"][0].D,
            };
        }

        /// <summary>IP 기반 대략적 위치 감지 (HTTPS 소스만 사용).</summary>
        public static async Task<GeoInfo> DetectLocationAsync(CancellationToken ct)
        {
            var j = await Net.GetJsonAsync("https://ipapi.co/json/", ct).ConfigureAwait(false);
            double lat = j["latitude"].D, lon = j["longitude"].D;
            if (!double.IsNaN(lat) && !double.IsNaN(lon))
                return new GeoInfo { Lat = lat, Lon = lon, City = j["city"].S };

            j = await Net.GetJsonAsync("https://ipwho.is/", ct).ConfigureAwait(false);
            lat = j["latitude"].D; lon = j["longitude"].D;
            if (!double.IsNaN(lat) && !double.IsNaN(lon))
                return new GeoInfo { Lat = lat, Lon = lon, City = j["city"].S };

            return null;
        }

        /// <summary>
        /// 네이버 날씨 지역코드 조회. 예: "여의도" → "01140640" (서울특별시 영등포구 여의동)
        /// </summary>
        public static async Task<string> FindWeatherAreaCodeAsync(string query, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(query)) return null;
            var hits = await SearchWeatherAreasAsync(query, ct).ConfigureAwait(false);
            if (hits.Count == 0) return null;
            return hits[0].Def.Code;
        }

        /// <summary>
        /// 날씨용 지역 검색. 네이버 날씨 자동완성이 정확한 행정동명과 지역코드를 준다.
        /// 좌표는 주지 않으므로, 실제로 추가할 때 ResolveCoordsAsync 로 따로 구한다.
        /// </summary>
        public static async Task<List<SearchHit>> SearchWeatherAreasAsync(string query, CancellationToken ct)
        {
            var list = new List<SearchHit>();
            if (string.IsNullOrEmpty(query)) return list;

            string url = "https://ac.weather.naver.com/ac?q_enc=utf-8&r_format=json&r_enc=utf-8&r_lt=1&st=1&q="
                       + Uri.EscapeDataString(query);

            var j = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
            // items : [ [ [ ["서울특별시 영등포구 여의동"], ["01140640"] ], ... ] ]
            var group = j["items"][0];

            for (int i = 0; i < group.Count && list.Count < 6; i++)
            {
                string full = group[i][0][0].S;
                string code = group[i][1][0].S;
                if (string.IsNullOrEmpty(full) || string.IsNullOrEmpty(code)) continue;

                bool digits = true;
                foreach (char ch in code) if (ch < '0' || ch > '9') { digits = false; break; }
                if (!digits) continue;

                // "서울특별시 영등포구 여의동" → 표시용은 마지막 조각만
                string shortName = full;
                int sp = full.LastIndexOf(' ');
                if (sp >= 0 && sp < full.Length - 1) shortName = full.Substring(sp + 1);

                var def = new SymbolDef(SourceKind.Weather, code, shortName);
                list.Add(new SearchHit { Def = def, TypeName = full });
            }
            return list;
        }

        /// <summary>
        /// 지역 이름으로 위경도를 구한다. Open-Meteo 자체 검색은 한국 행정구역 정확도가 낮아
        /// OpenStreetMap Nominatim 을 쓴다. 종목을 추가할 때 한 번만 호출한다.
        /// </summary>
        public static async Task<bool> ResolveCoordsAsync(SymbolDef def, string fullName, CancellationToken ct)
        {
            if (def == null) return false;
            if (!double.IsNaN(def.Lat) && !double.IsNaN(def.Lon)) return true;

            string q = string.IsNullOrEmpty(fullName) ? def.Label : fullName;
            string url = "https://nominatim.openstreetmap.org/search?format=json&accept-language=ko"
                       + "&limit=1&countrycodes=kr&q=" + Uri.EscapeDataString(q);

            var j = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
            double lat = j[0]["lat"].D;
            double lon = j[0]["lon"].D;
            if (double.IsNaN(lat) || double.IsNaN(lon)) return false;

            def.Lat = lat;
            def.Lon = lon;
            return true;
        }

        // ---------- 헬퍼 ----------

        private static double Round0(double v) { return double.IsNaN(v) ? 0 : Math.Round(v); }

        private static int DirFromName(string name, double diff)
        {
            // 한글 텍스트("상승"/"하락") 대신 영문 name 을 쓴다 - 인코딩에 영향받지 않는다
            if (name == "RISING") return 1;
            if (name == "FALLING") return -1;
            if (name == "EVEN") return 0;
            return double.IsNaN(diff) ? 0 : Math.Sign(diff);
        }

        private static int DirFromNaverCode(string code, double diff)
        {
            // 네이버 코드: 1 상한 / 2 상승 / 3 보합 / 4 하한 / 5 하락
            if (code == "1" || code == "2") return 1;
            if (code == "4" || code == "5") return -1;
            if (code == "3") return 0;
            return double.IsNaN(diff) ? 0 : Math.Sign(diff);
        }

        private static string FormatAbs(double v, int digits)
        {
            if (double.IsNaN(v)) return null;
            return Math.Abs(v).ToString("N" + digits.ToString(CultureInfo.InvariantCulture),
                                        CultureInfo.InvariantCulture);
        }

        private static string FormatSigned(double v, int digits)
        {
            if (double.IsNaN(v)) return null;
            string f = "N" + digits.ToString(CultureInfo.InvariantCulture);
            string s = Math.Abs(v).ToString(f, CultureInfo.InvariantCulture);
            if (v > 0) return "+" + s;
            if (v < 0) return "-" + s;
            return s;
        }

        private static string TimeOf(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            DateTimeOffset dt;
            if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
                                        DateTimeStyles.AssumeLocal, out dt))
                return dt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            return null;
        }


        // ---------- 새 버전 알림 ----------

        /// <summary>
        /// updateUrl 에서 최신 버전 문자열을 읽어온다. 기대하는 응답은 { "version": "0.50" } 하나다.
        /// 실패하면 null 을 돌려준다 - 버전 확인이 안 됐다고 사용자를 귀찮게 할 이유가 없다.
        /// </summary>
        public static async Task<string> LatestVersionAsync(string url, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(url)) return null;

            var j = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
            if (!j.Exists) return null;

            // 우리 형식은 { "version": "0.88" } 이다.
            // GitHub Releases 는 같은 것을 tag_name 에 "v0.88" 로 담아 준다. 그 한 가지만
            // 받아 주면 중계 서버 없이 GitHub 을 그대로 쓸 수 있다 - 전에는 형식이 안 맞아
            // 주소를 넣어도 영영 아무 것도 안 떴다.
            string v = j["version"].S;
            if (string.IsNullOrEmpty(v)) v = j["tag_name"].S;
            if (string.IsNullOrEmpty(v)) return null;
            if (v[0] == 'v' || v[0] == 'V') v = v.Substring(1);
            if (v.Length == 0 || v.Length > 8) return null;

            // 숫자와 점만 허용한다. 서버 응답이 오염돼도 화면에 엉뚱한 문자열이 뜨지 않게.
            foreach (char c in v)
                if ((c < '0' || c > '9') && c != '.') return null;

            return v;
        }

        /// <summary>응답이 준 링크가 허용 도메인일 때만 사용하고, 아니면 안전한 기본 링크를 쓴다.</summary>
        private static string SafeLink(string fromResponse, string fallback)
        {
            if (!string.IsNullOrEmpty(fromResponse) && Net.IsAllowedLink(fromResponse)) return fromResponse;
            return fallback;
        }
    }
}
