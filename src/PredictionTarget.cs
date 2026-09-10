// 품목 식별과 표시 단위. 화면 이름을 코드나 통화 대신 식별자로 쓰지 않는다.
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DeskWidget
{
    internal sealed class PredictionTarget
    {
        internal readonly SymbolDef Def;
        internal PredictionTarget(SymbolDef def)
        {
            def = def ?? new SymbolDef(SourceKind.Fx, "FX_USDKRW", "달러");
            Def = new SymbolDef(def.Kind, def.Code, def.Label) { Lat = def.Lat, Lon = def.Lon };
        }
        internal string Key { get { return Def.Key; } }
        internal bool Dollar { get { return Def.Kind == SourceKind.Fx && Def.Code == "FX_USDKRW"; } }
        internal bool Economic { get { return Def.Kind == SourceKind.Ecos; } }
        internal bool Weather { get { return Def.Kind == SourceKind.Weather; } }
        internal bool Policy { get { return Economic && (Def.Code.StartsWith("INTL:") || Def.Label.Contains("금리")); } }
        internal string Name { get { return Def.Label; } }
        internal string PeriodBasis { get { return Economic || Weather ? "1일·1주·1개월 (달력 기준)" : Def.Kind == SourceKind.Coin ? "1·7·30일 (UTC 일봉)" : "1·5·20거래일"; } }
        internal int Steps(int horizon) { return Def.Kind == SourceKind.Coin || Economic || Weather ? horizon == 5 ? 7 : horizon == 20 ? 30 : 1 : horizon; }
        internal string Unit(Quote q)
        {
            if (q != null && !string.IsNullOrEmpty(q.Unit)) return q.Unit;
            if (Def.Kind == SourceKind.Fx || Def.Kind == SourceKind.Coin || Def.Kind == SourceKind.DomesticStock) return "원";
            if (Def.Kind == SourceKind.Index) return "pt";
            if (Weather) return "°C";
            if (Policy) return "%";
            if (Economic) return "";
            return "현지통화";
        }
        internal string Format(double value, Quote q, bool compact = false)
        {
            string unit = Unit(q);
            string format = compact && Math.Abs(value) >= 1000 && unit == "원" ? "N0" : Math.Abs(value) < 1 ? "0.####" : "N2";
            string number = value.ToString(format, CultureInfo.InvariantCulture);
            return unit == "USD" ? "$" + number : number + (unit == "원" || unit == "%" ? "" : " ") + unit;
        }
        internal string Change(double anchor, double value, Quote q)
        {
            double delta = value - anchor;
            string unit = Unit(q), suffix = unit == "%" ? "%p" : unit;
            string format = Economic || Math.Abs(anchor) < 10 ? "+0.####;-0.####;0" : "+0.0;-0.0;0.0";
            double precision = Economic || Math.Abs(anchor) < 10 ? 0.00005 : 0.05;
            string change = (Math.Abs(delta) < precision ? 0 : delta).ToString(format, CultureInfo.InvariantCulture) + suffix;
            double percent = anchor == 0 ? 0 : (value / anchor - 1) * 100;
            return change + "\n" + (Economic || Weather || anchor == 0 ? "기준 대비" : (Math.Abs(percent) < 0.005 ? 0 : percent).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%");
        }
        internal static double Number(Quote q)
        {
            if (q == null || !q.Ok) return double.NaN;
            if (!double.IsNaN(q.Value)) return q.Value;
            var match = Regex.Match(q.Price ?? "", @"[-+]?\d[\d,]*(?:\.\d+)?");
            double value;
            return match.Success && double.TryParse(match.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ? value : double.NaN;
        }
        internal string SiteLink(Quote quote, string bank)
        {
            if (quote != null && Net.IsAllowedLink(quote.Link)) return quote.Link;
            string code = Uri.EscapeDataString(Def.Code ?? "");
            switch (Def.Kind) {
                case SourceKind.Fx: return "https://m.stock.naver.com/marketindex/exchange/" + code + (bank == "SHB" ? "_SHB" : "");
                case SourceKind.Index: return "https://m.stock.naver.com/domestic/index/" + code + "/total";
                case SourceKind.DomesticStock: return "https://m.stock.naver.com/domestic/stock/" + code + "/total";
                case SourceKind.WorldStock: return "https://m.stock.naver.com/worldstock/stock/" + code + "/total";
                case SourceKind.Coin: return "https://stock.naver.com/crypto/UPBIT/" + Uri.EscapeDataString(Def.Code.StartsWith("KRW-") ? Def.Code.Substring(4) : Def.Code) + "/price";
                case SourceKind.Ecos: return "https://ecos.bok.or.kr/";
                case SourceKind.Weather: return "https://weather.naver.com/" + (string.IsNullOrEmpty(code) ? "" : "today/" + code);
                default: return null;
            }
        }
        internal static string SiteName(string link)
        {
            Uri uri; if (!Uri.TryCreate(link, UriKind.Absolute, out uri)) return "사이트 ↗";
            string host = uri.Host.ToLowerInvariant();
            if (host == "naver.com" || host.EndsWith(".naver.com")) return "네이버 ↗";
            if (host == "bok.or.kr" || host.EndsWith(".bok.or.kr")) return "한은 ↗";
            if (host == "upbit.com" || host.EndsWith(".upbit.com")) return "업비트 ↗";
            return "사이트 ↗";
        }

        /// <summary>
        /// 해외주식 코드에서 티커만 뽑는다. "DLTR.O" -> "DLTR".
        ///
        /// ★ 숫자뿐인 티커는 돌려주지 않는다 ★
        ///   일본 종목은 "4849.T" 처럼 숫자다. 그대로 낱말 검사에 쓰면 기사 안의
        ///   아무 숫자 4849 에나 걸린다. 글자가 두 자 이상 없으면 빈 문자열을 준다.
        /// </summary>
        internal static string Ticker(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            string root = code.Split('.')[0];
            return Regex.IsMatch(root, "[A-Za-z]{2,}") ? root : "";
        }

        internal string SearchName
        {
            get
            {
                // 해외주식은 한글 이름과 코드에 더해 티커도 검색어에 넣는다.
                // 전에는 한 종목에만 영문 회사명을 손으로 박아 두었다 - 그 종목만
                // 검색이 잘 되고 나머지는 안 되는, 규칙이 아닌 예외였다.
                if (Def.Kind == SourceKind.WorldStock)
                {
                    string ticker = Ticker(Def.Code);
                    return "\"" + Name.Replace("\"", "") + "\" OR \"" + Def.Code + "\"" +
                           (ticker.Length > 0 ? " OR \"" + ticker + "\"" : "");
                }
                if (Economic && Def.Code == "INTL:US") return "연준 OR FOMC OR Federal Reserve";
                if (Economic && Def.Code == "INTL:KR") return "한국은행 OR 한은 OR Bank of Korea";
                if (Def.Kind == SourceKind.Coin) return "\"" + Name + "\" OR \"" + Def.Code.Replace("KRW-", "") + "\"";
                return "\"" + Name.Replace("\"", "") + "\"";
            }
        }
    }
}
