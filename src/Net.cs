// 네트워크 계층 - HttpClient 싱글톤(소켓 누수 방지) + 링크 열기 화이트리스트
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DeskWidget
{
    internal static class Net
    {
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private static readonly HttpClient _client = Create(10);

        /// <summary>
        /// 느린 공개 자료용 통로.
        ///
        /// ★ 시세와 참고 자료는 급한 정도가 다르다 ★
        ///   시세가 10초 안에 안 오면 그 값은 이미 낡았다. 반대로 하루에 한 번 갱신되는
        ///   참고 자료는 20초가 걸려도 받는 편이 낫다.
        ///   실측(2026-09-10): 미 재무부 수익률곡선이 매달 17~19초 걸린다. 10초 통로로
        ///   받고 있었기 때문에 v1.035 의 국채금리는 **한 번도 들어온 적이 없었다.**
        ///   빈 값을 조용히 돌려주는 설계라 아무도 몰랐다.
        /// </summary>
        private static readonly HttpClient _slow = Create(45);

        private static HttpClient Create(int timeoutSec)
        {
            // ServicePointManager.SecurityProtocol 은 일부러 건드리지 않는다 (SystemDefault 유지).
            //
            // .NET Framework 4.7 이상에서는 SystemDefault 가 권장 방식이고, OS 가 TLS 1.2/1.3 을
            // 알아서 협상한다. Windows 11 은 TLS 1.0/1.1 이 기본 비활성화라 보안상으로도 이쪽이 낫다.
            // 반대로 Tls12 로 고정하면 Cloudflare 뒤에 있는 서버(Open-Meteo)와의 핸드셰이크가
            // "SSL/TLS 보안 채널을 만들 수 없습니다" 로 실패하는 것을 실측으로 확인했다.
            // 이 값은 프로세스 전역이라 잘못 만지면 모든 요청이 함께 깨진다.

            // 인증서 검증은 .NET 기본 동작 그대로이며 절대 우회하지 않는다.

            ServicePointManager.DefaultConnectionLimit = 8;
            ServicePointManager.Expect100Continue = false;

            var handler = new HttpClientHandler
            {
                // Deflate 는 넣지 않는다. 서버가 zlib 헤더가 붙은 deflate 를 보내면
                // .NET Framework 의 DeflateStream 이 이를 풀지 못하고
                // "디코딩하는 동안 잘못된 데이터를 찾았습니다" 로 실패한다.
                AutomaticDecompression = DecompressionMethods.GZip,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                UseCookies = false,          // 쿠키를 저장하지 않는다
                UseProxy = true,
            };

            // 인증을 요구하는 프록시 뒤에서도 동작하게 한다.
            // 시스템(인터넷 옵션) 프록시 설정을 그대로 따르고, 프록시가 407 로 인증을
            // 요구하면 로그온한 Windows 계정으로 응답한다. 자격 증명은 프록시에만 가고
            // 대상 서버로는 전송되지 않는다. 프록시가 없는 환경에서는 아무 영향이 없다.
            // (이게 없으면 인증 프록시 뒤에서 모든 요청이 407 로 죽어 전부 오프라인이 된다)
            try
            {
                var proxy = WebRequest.GetSystemWebProxy();
                proxy.Credentials = CredentialCache.DefaultCredentials;
                handler.Proxy = proxy;
            }
            catch { }

            var c = new HttpClient(handler);
            c.Timeout = TimeSpan.FromSeconds(timeoutSec);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Accept-Encoding 은 직접 넣지 않는다.
            // AutomaticDecompression 이 알아서 붙이는데, 수동으로 중복 지정하면
            // 핸들러가 압축 해제를 건너뛰어 gzip 바이트가 그대로 넘어온다.
            // 응답 크기 상한 (비정상적으로 큰 응답으로 메모리를 소모하지 못하게)
            c.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            return c;
        }

        /// <summary>
        /// HTTPS GET 후 JSON 파싱. 실패하면 JNode.Empty를 반환한다(예외를 밖으로 던지지 않는다).
        /// </summary>
        public static async Task<JNode> GetJsonAsync(string url, CancellationToken ct)
        {
            try
            {
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return JNode.Empty;

                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                using (var res = await _client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct)
                                              .ConfigureAwait(false))
                {
                    if (!res.IsSuccessStatusCode) return JNode.Empty;
                    string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return Json.Parse(body);
                }
            }
            catch (OperationCanceledException)
            {
                // HttpClient 는 타임아웃도 OperationCanceledException 으로 던진다.
                // 위젯 종료(ct 취소)일 때만 위로 올리고, 단순 타임아웃은 '이번엔 실패' 로 처리한다.
                // 이걸 구분하지 않으면 네트워크가 잠깐 끊긴 것만으로 데이터 루프가 영영 끝나버린다.
                if (ct.IsCancellationRequested) throw;
                return JNode.Empty;
            }
            catch
            {
                return JNode.Empty;
            }
        }

        // ---------- 링크 열기 (화이트리스트) ----------

        /// <summary>뉴스 RSS용. JSON 조회와 같은 연결·용량·타임아웃 제한을 쓴다.</summary>
        public static async Task<string> GetTextAsync(string url, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.Accept.Clear();
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/rss+xml"));
                    using (var res = await _client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                    {
                        if (!res.IsSuccessStatusCode) return null;
                        return await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { if (ct.IsCancellationRequested) throw; return null; }
            catch { return null; }
        }

        /// <summary>
        /// 느린 참고 자료를 얼마나 오래 기억할지. 국채 수익률곡선은 하루 한 번 갱신된다.
        /// 창을 다시 열 때마다 20초를 다시 치를 이유가 없다.
        /// </summary>
        internal static TimeSpan SlowCacheLife = TimeSpan.FromHours(6);
        private static readonly object _slowGate = new object();
        private static readonly Dictionary<string, KeyValuePair<DateTime, string>> _slowCache =
            new Dictionary<string, KeyValuePair<DateTime, string>>(StringComparer.Ordinal);

        /// <summary>기억해 둔 값이 아직 쓸 만하면 그것을. 아니면 null.</summary>
        internal static string CachedSlow(string url, DateTime now)
        {
            if (string.IsNullOrEmpty(url)) return null;
            lock (_slowGate)
            {
                KeyValuePair<DateTime, string> hit;
                if (!_slowCache.TryGetValue(url, out hit)) return null;
                // 시계가 뒤로 간 경우(절전 복귀·시간대 변경)에도 영원히 붙잡지 않는다.
                if (now < hit.Key || now - hit.Key > SlowCacheLife) return null;
                return hit.Value;
            }
        }

        /// <summary>받아 온 값을 기억한다. 빈 값은 기억하지 않는다 - 실패를 캐시하면 안 된다.</summary>
        internal static void RememberSlow(string url, string body, DateTime now)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(body)) return;
            lock (_slowGate)
            {
                // 무한정 쌓이지 않게 한다. 주소 종류는 몇 개뿐이라 넉넉한 값이다.
                if (_slowCache.Count > 64) _slowCache.Clear();
                _slowCache[url] = new KeyValuePair<DateTime, string>(now, body);
            }
        }

        /// <summary>
        /// 느린 통로로 받는다. 기억해 둔 값이 있으면 그것을 쓴다.
        /// 실패하면 null - 없는 값을 지어내지 않는다.
        /// </summary>
        public static async Task<string> GetSlowTextAsync(string url, CancellationToken ct)
        {
            string cached = CachedSlow(url, DateTime.UtcNow);
            if (cached != null) return cached;
            try
            {
                if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.Accept.Clear();
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
                    using (var res = await _slow.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                    {
                        if (!res.IsSuccessStatusCode) return null;
                        string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        RememberSlow(url, body, DateTime.UtcNow);
                        return body;
                    }
                }
            }
            catch (OperationCanceledException) { if (ct.IsCancellationRequested) throw; return null; }
            catch { return null; }
        }

        // 설정 파일이 오염되더라도 임의의 URL이나 프로그램이 실행되지 않도록,
        // 열 수 있는 호스트를 명시적으로 제한한다.
        private static readonly string[] AllowedHosts =
        {
            "naver.com",
            "stock.naver.com",
            "m.stock.naver.com",
            "finance.naver.com",
            "weather.naver.com",
            "search.naver.com",
            "upbit.com",
            "ecos.bok.or.kr",
            "bok.or.kr",

            // 새 버전을 받으러 가는 곳. 이 한 줄로 api.github.com 도 함께 열린다 -
            // 아래 검사가 "." + allowed 로 끝나는 호스트도 통과시키기 때문이다.
            "github.com",
        };

        public static bool IsAllowedLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme != Uri.UriSchemeHttps) return false;   // https 전용
            if (!string.IsNullOrEmpty(u.UserInfo)) return false; // user@host 형태 차단

            string host = u.Host.ToLowerInvariant();
            // 뉴스 검색/리다이렉트 URL 전체를 허용하지 않고 기사 경로만 연다.
            if (host == "news.google.com" || DollarNewsSources.IsArticle(url)) return DollarAnalysis.IsNewsLink(url);
            foreach (string allowed in AllowedHosts)
            {
                if (host == allowed) return true;
                if (host.EndsWith("." + allowed, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>기본 브라우저로 링크를 연다. 화이트리스트에 없으면 아무 것도 하지 않는다.</summary>
        public static void OpenLink(string url)
        {
            if (!IsAllowedLink(url)) return;
            try
            {
                var psi = new ProcessStartInfo(url) { UseShellExecute = true };
                // 받은 것을 놓아준다. 브라우저가 이미 떠 있으면 null 이 오는데 using 은 그것도 받는다.
                using (Process.Start(psi)) { }
            }
            catch { }
        }
    }
}
