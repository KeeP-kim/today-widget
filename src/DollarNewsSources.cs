using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DeskWidget
{
    internal static class DollarNewsSources
    {
        public static readonly string[] Feeds = {
            "https://www.yna.co.kr/rss/economy.xml", "https://www.yna.co.kr/rss/international.xml",
            "https://www.hankyung.com/feed/economy", "https://www.hankyung.com/feed/international",
            "https://feeds.bbci.co.uk/news/business/rss.xml", "https://feeds.bbci.co.uk/news/world/rss.xml"
        };
        public static string Publisher(int i) { return i < 2 ? "연합뉴스" : i < 4 ? "한국경제" : "BBC"; }

        /// <summary>본문까지 읽을 기사 수. Program 이 설정에서 넣어 준다(Sources.EcosKey 와 같은 방식).</summary>
        public static int BodyLimit = Config.DefaultBodyLimit;
        public static bool IsArticle(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https" || !u.IsDefaultPort || !string.IsNullOrEmpty(u.UserInfo)) return false;
            return (u.Host == "www.yna.co.kr" && u.AbsolutePath.StartsWith("/view/AKR", StringComparison.Ordinal)) ||
                (u.Host == "www.hankyung.com" && Regex.IsMatch(u.AbsolutePath, @"^/article/\d+$")) ||
                ((u.Host == "www.bbc.com" || u.Host == "www.bbc.co.uk") && u.AbsolutePath.StartsWith("/news/articles/", StringComparison.Ordinal));
        }
        public static string ExtractBody(string html)
        {
            if (string.IsNullOrEmpty(html) || html.Length > 2 * 1024 * 1024) return "";
            var body = Regex.Match(html, "\"articleBody\"\\s*:\\s*(\"(?:\\\\.|[^\"\\\\])*\")");
            if (body.Success)
            {
                // ★ 정규식이 받아들인 문자열을 JSON 파서가 거부할 수 있다 ★
                //   \x, \', 잘못된 \uXXXX 처럼. 그러면 Parse 가 Empty 를 주고 .S 는 null 이라
                //   다음 줄이 NRE 로 죽고, 그 예외가 WhenAll 을 타고 올라가 기사 한 건 때문에
                //   조회 전체가 끝났다. null 이면 <p> 경로로 내려간다.
                string decoded = Json.Parse(body.Groups[1].Value).S;
                if (decoded != null && decoded.Length > 150) return Plain(decoded);
            }
            var articles = Regex.Matches(html, @"<article\b[^>]*>([\s\S]*?)</article>", RegexOptions.IgnoreCase)
                .Cast<Match>().OrderByDescending(m => m.Length).ToList();
            if (articles.Count == 0) return "";
            string section = Regex.Replace(articles[0].Groups[1].Value, @"<(script|style|nav|aside|figure|footer)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
            // ★ 기사 안에 기사가 아닌 것이 섞인다 ★
            //   사진 설명, 저작권·제보 꼬리말, 재생 안 되는 영상 안내가 <p> 로 들어 있다.
            //   이 문장들은 발췌로 잘려 인용 번호가 붙으므로 모델이 인용해도 검증을 통과한다.
            var paragraphs = Regex.Matches(section, @"<p\b[^>]*>([\s\S]*?)</p>", RegexOptions.IgnoreCase)
                .Cast<Match>().Select(m => StripCredits(Plain(m.Groups[1].Value)))
                .Where(t => t.Length >= 25 && !Boilerplate(t)).ToArray();
            string text = string.Join("\n", paragraphs);
            return text.Length >= 150 ? DollarAnalysis.Clean(text, 6000) : "";
        }
        /// <summary>
        /// 사진 출처 표기를 지운다. 대괄호 안에 들어가고 그 뒤에 진짜 취재 문장이 이어진다.
        ///
        /// ★ 문단째 버리면 기사를 함께 버린다 ★
        ///   저장된 기록을 실제로 훑어 보니 '[EPA=OO뉴스 자료사진 재판매 및 DB 금지] (서울=OO뉴스)
        ///   OOO 기자 = ...' 처럼 사진 출처가 먼저 오고 그 뒤에 취재 문장이 이어지는 문단이 있었다.
        ///   '재판매 및 DB 금지' 를 꼬리말로 보고 문단을 버리면 저 뒤의 취재 내용이 통째로 사라진다.
        /// </summary>
        internal static string StripCredits(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string s = Regex.Replace(text, @"\[[^\[\]]{0,160}?(?:재판매\s?및\s?DB\s?금지|무단\s?전재|자료\s?사진|제공)[^\[\]]{0,160}?\]", " ");
            return Regex.Replace(s, @"\s{2,}", " ").Trim();
        }

        /// <summary>
        /// 문단 전체가 기사가 아닌가. 기사 끝의 제보·저작권 안내와, 재생할 수 없는 영상 자리의 안내문이다.
        ///
        /// ★ 실물로 확인하고 정했다 ★
        ///   기사를 실제로 받아 보니 기사 끝의 제보·저작권 안내 문단은 언제나 자기만의
        ///   &lt;p&gt; 에 들어 있고 취재 문장 안에는 나타나지 않는다. 그래서 이것만 문단째 버린다.
        ///   반대로 'AI 학습'·'저작권자' 같은 낱말만 보고 버리면, 저작권이나 AI 학습을 다루는
        ///   진짜 기사 문장을 버리게 된다 - 넓게 잡으면 안 되는 이유다.
        /// </summary>
        internal static bool Boilerplate(string text)
        {
            // 문단을 여는 것들은 앞에 못을 박는다 - 진짜 문장 앞에 붙어 오는 경우까지 버리지 않으려고.
            // 저작권 표기는 꼬리말 가운데에 오므로 못을 박지 않는다(취재 문장에는 이 형태로 안 나온다).
            return Regex.IsMatch(text ?? "", @"^\s*제보는 카카오톡|^\s*This video can ?not be played|카카오톡 okjebo|저작권자\s*[(ⓒ©]|Google 검색에서");
        }
        private static string Plain(string html)
        {
            return DollarAnalysis.Clean(System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " ")), 6000);
        }
        /// <summary>
        /// 본문까지 읽을 기사를 고른다. 망을 타지 않으므로 검사가 그대로 확인할 수 있다.
        ///
        /// ★ 요인이 많은 기사부터 읽으면 규칙이 이미 아는 것만 더 읽게 된다 ★
        ///   새 정보는 규칙이 아직 모르는 기사에 있다. 최신 것을 먼저 읽고,
        ///   요인 수는 같은 시각일 때 동점을 가르는 데만 쓴다.
        ///   출처별로도 나눈다 - 출처가 셋뿐이라 한 곳이 다 가져가면
        ///   같은 논조만 깊게 읽게 된다.
        /// </summary>
        internal static List<DollarNews> ChooseBodies(List<DollarNews> news, int limit)
        {
            if (news == null) return new List<DollarNews>();
            limit = limit < 0 ? 0 : limit > Config.MaxBodyLimit ? Config.MaxBodyLimit : limit;
            if (limit == 0) return new List<DollarNews>();
            int perSource = Math.Max(3, (int)Math.Ceiling(limit / 3.0));
            return news.Where(n => IsArticle(n.Url)).GroupBy(n => n.Source ?? "")
                .SelectMany(g => g.OrderByDescending(n => n.PublishedUtc).ThenByDescending(n => DollarFactors.Analyze(n).Count).Take(perSource))
                .OrderByDescending(n => n.PublishedUtc).Take(limit).ToList();
        }

        public static async Task EnrichAsync(List<DollarNews> news, CancellationToken ct)
        {
            // Bounded public article reads. Failures retain the original headline/RSS summary.
            var chosen = ChooseBodies(news, BodyLimit);
            if (chosen.Count == 0) { foreach (var n in news) DollarAnalysis.Classify(n); return; }
            await Task.WhenAll(chosen.Select(async n => {
                // 기사 한 건이 넘어져도 나머지는 읽어야 한다. 실패한 기사는 제목·요약 그대로 둔다.
                try
                {
                    string html = await Net.GetTextAsync(n.Url, ct).ConfigureAwait(false);
                    string body = ExtractBody(html);
                    if (body.Length > 0) { n.Context = body; n.BodyRead = true; }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { }
                DollarAnalysis.Classify(n);
            }));
        }
    }
}
