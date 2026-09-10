using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DeskWidget
{
    internal static class DollarAnalysisTests
    {
        private static int count;
        private static void Check(bool ok, string message)
        {
            if (!ok) throw new Exception("Dollar analysis: " + message);
            count++;
        }

        internal static int Run(string work)
        {
            count = 0;
            count += DollarSparkTests.Run(work);
            count += DollarLayoutTests.Run(work);
            FactorChecks();
            DateTime today = new DateTime(2026, 9, 8);
            string row = "{\"date\":\"2026-09-07\",\"base\":\"USD\",\"quote\":\"KRW\",\"rate\":1350}";
            Check(DollarAnalysis.ParseRates(Json.Parse("[" + row + "," + row + "]"), today).Count == 1, "duplicate dates inflate history");
            Check(DollarAnalysis.ParseRates(Json.Parse("[" + row.Replace("2026-09-07", "2026-09-09") + "]"), today).Count == 0, "future history accepted");
            Check(DollarAnalysis.ParseRates(Json.Parse("[" + row.Replace("1350", "-1") + "]"), today).Count == 0, "invalid rate accepted");
            Check(DollarAnalysis.ParseRates(Json.Parse("[" + row.Replace("USD", "EUR") + "]"), today).Count == 0, "wrong currency accepted");
            Check(DollarAnalysis.ParseRates(Json.Parse("[" + row + "," + row.Replace("1350", "1351") + "]"), today).Count == 0, "conflicting same-day rates accepted");
            Check(DollarAnalysis.Analyze(new List<DollarRate>()) == null, "empty history given a pattern");

            // ── 어떤 기사를 들일 것인가 (감사 3-1·3-2, v1.032) ────────────
            // ★ 낱말이 다른 낱말 안에 숨어 있다 ★
            //   '한은' 이 '신한은행' 안에, '관세' 가 '관세청' 안에 걸려 환율과 무관한
            //   기사가 피드 자리(피드당 6건)를 차지했다. 요인 점수는 0 이지만 기사
            //   수를 늘려 근거계수를 부풀린다.
            foreach (string noise in new[] {
                "[게시판] 신한은행, 6억 유로 그린 커버드본드 발행",
                "신한은행, 퇴직연금형 개인 투자용 국채 판매",
                "관세청 1∼7월 탈세 등 1조760억원 적발…작년 연간 실적의 40%",
                "삼성전자 신형 갤럭시 국내 출시", "삼성 사장단 인사 단행" })
                Check(!DollarAnalysis.Relevant(noise, true), "an unrelated article took a feed slot: " + noise);
            // ★ 반대로 진짜 환율 기사가 버려졌다 ★
            //   이 제목들에는 '환율' 도 '달러' 도 없어서 목록에 걸리지 않았다.
            foreach (string real in new[] {
                "금통위, 기준금리 2.50% 동결", "이창용 총재 \"물가 경로 점검\"",
                "파월 의장 잭슨홀 연설", "9월 FOMC 앞두고 관망세",
                "국고채 3년물 금리 하락", "외환시장 마감…코스피 강세",
                "미국 고용지표 예상 웃돌아", "美 소비자물가 둔화",
                "외국인 코스피 3조원 순매도", "삼성전자, 미국 텍사스 공장 투자 확대",
                "무역수지 12개월 연속 흑자" })
                Check(DollarAnalysis.Relevant(real, true), "a real currency-relevant headline was dropped: " + real);
            // 종전에 걸리던 것은 그대로 걸려야 한다.
            foreach (string kept in new[] { "원/달러 환율 1350원 돌파", "한국은행 기준금리 인하", "트럼프 관세 부과", "국제유가 급등" })
                Check(DollarAnalysis.Relevant(kept, true), "a headline that used to pass stopped passing: " + kept);
            // 해외 피드도 같은 구멍이 있었다 - 'spoiled' 안의 'oil'.
            Check(!DollarAnalysis.Relevant("Holiday plans spoiled by rail strike", false), "the word oil was found inside another word");
            foreach (string world in new[] { "Powell signals caution", "FOMC minutes due Wednesday", "Oil prices jump on supply fears" })
                Check(DollarAnalysis.Relevant(world, false), "a relevant world headline was dropped: " + world);

            // ── 근거와 방향이 어긋날 때 (감사 6-1, v1.032) ────────────────
            // ★ 같은 화면에서 '상승 근거 우세' 와 '보합' 을 동시에 말했다 ★
            //   둘 다 틀린 게 아니라 서로 다른 것을 잰다. 점수 줄은 근거의 부호이고,
            //   방향 줄은 그 점수로 만든 예상 변동폭이 보합 범위를 넘는지다. 화면이
            //   그 말을 하지 않아서 앞뒤가 안 맞는 두 결론으로만 보였다.
            Check(DollarAnalysisWindow.Reconcile("상승 근거 우세", "보합").Contains("보합 범위 안"),
                "evidence pointing up while the direction says flat went unexplained");
            Check(DollarAnalysisWindow.Reconcile("하락 쪽", "보합").Contains("하락"),
                "evidence pointing down while the direction says flat went unexplained");
            Check(DollarAnalysisWindow.Reconcile("혼조", "상승").Contains("과거 통계"),
                "a direction with no evidence behind it went unexplained");
            Check(DollarAnalysisWindow.Reconcile("상승 근거 우세", "하락").Contains("뒤집"),
                "evidence and direction pointing opposite ways went unexplained");
            // 같은 말을 할 때는 아무 말도 덧붙이지 않는다. 화면이 잔소리로 차면 안 읽는다.
            foreach (string[] agree in new[] {
                new[] { "상승 근거 우세", "상승" }, new[] { "하락 쪽", "하락" }, new[] { "혼조", "보합" },
                new[] { "자료 부족", "보합" }, new[] { "상승 근거 우세", "판단 보류" }, new[] { "하락 쪽", "변동폭 미산정" } })
                Check(DollarAnalysisWindow.Reconcile(agree[0], agree[1]) == "",
                    "an explanation was added where the two lines already agreed: " + agree[0] + "/" + agree[1]);
            // 점수 줄의 말이 최종 방향처럼 읽히면 안 된다.
            var strong = new DollarScore { UpEvidence = 9, Reliability = 1, DirectionalCount = 1 };
            Check(DollarAnalysisWindow.Outlook(strong).Contains("근거"),
                "the score line still speaks as if it were the final direction");

            // ★ 설명이 실제로 화면 글에 들어가는지 본다 ★
            //   Reconcile 만 검사하면 그 결과가 화면까지 오는지는 아무도 안 본다.
            //   Alt 글자 크기 때 똑같이 당했다 - 함수는 멀쩡한데 배선이 끊겨 있었다.
            DateTime wiredNow = new DateTime(2026, 9, 8, 2, 0, 0, DateTimeKind.Utc);
            var wired = new DollarAnalysisResult { DomesticAvailable = true, GlobalAvailable = true,
                CheckedUtc = wiredNow, Rates = Series(420, 0.002) };
            wired.Rates[wired.Rates.Count - 1].Date = DollarAnalysis.KoreaDate(wiredNow);
            for (int i = wired.Rates.Count - 1; i >= 0; i--)
                wired.Rates[i].Date = DollarAnalysis.KoreaDate(wiredNow).AddDays(i - (wired.Rates.Count - 1));
            foreach (int h in new[] { 1, 5, 20 })
            {
                var pattern = DollarAnalysis.Analyze(wired.Rates, h);
                if (pattern != null) wired.Patterns.Add(pattern);
            }
            foreach (string headline in new[] { "연준 금리 인상 결정", "한국은행 기준금리 인하 결정", "국제유가 급등", "엔캐리 청산 본격화" })
            {
                var article = News(headline); article.PublishedUtc = wiredNow.AddMinutes(-30);
                wired.News.Add(article);
            }
            string block = DollarAnalysisWindow.ScoreText(wired, wiredNow);
            Check(block.Contains("일간:") && block.Contains("월간:"), "the score block lost a horizon");
            bool sawGap = false;
            foreach (int h in new[] { 1, 5, 20 })
            {
                string expected = DollarAnalysisWindow.Reconcile(
                    DollarAnalysisWindow.Outlook(DollarAnalysis.Score(wired, h, wiredNow)),
                    DollarAnalysisWindow.ScenarioOutlook(wired, h, wiredNow));
                if (expected.Length == 0) continue;
                sawGap = true;
                Check(block.Contains(expected), "the score block did not carry the explanation for horizon " + h);
            }
            Check(sawGap, "the fixture no longer produces a disagreement - this check would pass on a broken build");

            // ── 구조상 방향이 나오기 어려운 기간 (감사 §5-8 나머지, v1.036) ──
            // ★ 두 보수성이 겹쳐 있다 ★
            //   24시간 뉴스의 기간 가중치(주 0.5·월 0.25)가 점수를 줄이는데, 보합 문턱은
            //   기간이 길수록 넓어진다. 둘은 따로 정해졌고 아무도 그 곱을 본 적이 없다.
            //   실측(USD/KRW 764일): 보합을 벗어나려면 필요한 근거가 일간은 최대치의 21%,
            //   월간은 65% 다. 월간은 사실상 기사가 만장일치여야 방향이 난다.
            Check(DollarAnalysis.HorizonWeight(1) == 1 && DollarAnalysis.HorizonWeight(5) == 0.5 && DollarAnalysis.HorizonWeight(20) == 0.25,
                "the horizon weight changed without anyone saying so");
            Check(DollarAnalysis.ScoreCeiling(1, 1) > DollarAnalysis.ScoreCeiling(5, 1) &&
                  DollarAnalysis.ScoreCeiling(5, 1) > DollarAnalysis.ScoreCeiling(20, 1),
                "a longer horizon did not lower the reachable score");
            Check(Math.Abs(DollarAnalysis.ScoreCeiling(20, 1) - 25) < 1e-9, "the monthly ceiling is not news 20 plus history 5");
            Check(DollarAnalysis.ScoreCeiling(1, 0.5) < DollarAnalysis.ScoreCeiling(1, 1), "reliability did not scale the ceiling");
            // 문턱이 넓을수록, 과거 변동폭이 좁을수록 더 큰 근거가 필요하다.
            // 10~90 백분위는 Returns 에서 나온다. 절반씩 같은 크기로 채우면 그 값이 그대로 나온다.
            Func<double, DollarPattern> span = edge => {
                var made = new DollarPattern { Horizon = 1 };
                for (int i = 0; i < 10; i++) { made.Returns.Add(-edge); made.Returns.Add(edge); }
                return made;
            };
            var wide = span(0.02);
            var narrow = span(0.005);
            Check(DollarAnalysis.DirectionalNeed(narrow, 0.001) > DollarAnalysis.DirectionalNeed(wide, 0.001),
                "a narrower historical span did not demand a bigger score");
            Check(Math.Abs(DollarAnalysis.DirectionalNeed(wide, 0.002) - 10) < 1e-9, "the needed score is not band over span");
            Check(double.IsNaN(DollarAnalysis.DirectionalNeed(null, 0.001)), "a missing pattern produced a number");
            Check(double.IsNaN(DollarAnalysis.DirectionalNeed(new DollarPattern(), 0.001)),
                "a pattern with no returns produced a number instead of unknown");
            // ★ 화면이 그 사실을 말해야 한다 ★
            //   말하지 않으면 보는 사람은 '월간은 늘 보합' 을 시장이 조용해서라고 읽는다.
            // ★ 특정 자료의 결과가 아니라 불변식을 재야 한다 ★
            //   실제 USD/KRW 에서는 월간이 일간보다 훨씬 큰 근거를 요구하지만, 추세가 센
            //   합성 자료에서는 뒤집힌다. 그 방향을 검사에 박으면 자료가 바뀔 때 헛되이 깨진다.
            //   여기서 지킬 것은 '천장이 기간에 따라 낮아진다' 와 '화면이 계산한 값을 그대로
            //   말한다' 두 가지다.
            double monthly = DollarAnalysisWindow.Headroom(wired, 20, wiredNow);
            double daily = DollarAnalysisWindow.Headroom(wired, 1, wiredNow);
            Check(!double.IsNaN(monthly) && !double.IsNaN(daily), "the headroom could not be measured on the fixture");
            Check(monthly > 0 && daily > 0, "the headroom came back as a nonsense fraction");
            foreach (int h in new[] { 1, 5, 20 })
            {
                double room = DollarAnalysisWindow.Headroom(wired, h, wiredNow);
                bool warned = block.Contains("근거가 최대치의 " + room.ToString("0", CultureInfo.InvariantCulture) + "%");
                Check(room >= 50 ? warned : !warned,
                    "the screen did not say what it computed for horizon " + h + " (" + room.ToString("0.0", CultureInfo.InvariantCulture) + "%)");
            }
            Check(double.IsNaN(DollarAnalysisWindow.Headroom(null, 20, wiredNow)), "a missing result produced a headroom");

            // ── 낱말 안에 숨은 매칭 (v1.037) ──────────────────────────────
            // ★ \b 를 앞에 붙이는 것만으로는 부족하다 ★
            //   \bmiss\w* 는 'mission' 에 그대로 걸린다 - 'miss' 가 낱말 첫머리이기 때문이다.
            //   어미까지 못 박아야 한다. 실측으로 잡은 것들:
            //     eas\w*  -> "US inflation increases" 가 물가 둔화(-1) 로 채점됐다 (incr[eas]e)
            //     miss\w* -> "jobs mission statement" 가 고용 약화(-1) 로 채점됐다 ([miss]ion)
            //     hot\w*  -> hotel / photo
            //     fall\w* -> windfall, shortfall, fallout
            //     impos   -> impossible,  rais -> praise
            foreach (string[] pair in new[] {
                new[] { "US inflation increases faster than expected", "us-inflation-soft" },
                new[] { "US inflation increased in August", "us-inflation-soft" },
                new[] { "US jobs mission statement released", "us-labor" },
                new[] { "US inflation photo gallery released", "us-inflation-hot" },
                new[] { "US jobs data from a hotel chain", "us-inflation-hot" },
                new[] { "US consumer prices steady as windfall taxes are debated", "us-inflation-soft" } })
                Check(DollarFactors.Analyze(News(pair[0])).All(f => f.Rule != pair[1]),
                    "a word matched inside another word: " + pair[0] + " -> " + pair[1]);
            // 진짜 낱말은 그대로 걸려야 한다. 경계를 조이다 본체를 막으면 안 된다.
            foreach (string[] real in new[] {
                new[] { "US inflation eases to 2.4%", "us-inflation-soft" },
                new[] { "US inflation is hot again", "us-inflation-hot" },
                new[] { "US jobs report misses estimates", "us-labor" },
                new[] { "US consumer prices fall in August", "us-inflation-soft" } })
                Check(DollarFactors.Analyze(News(real[0])).Any(f => f.Rule == real[1]),
                    "pinning the suffix silenced a real headline: " + real[0]);
            // 'increases' 는 오르는 이야기다. 침묵이 아니라 가열이어야 한다.
            Check(DollarFactors.Analyze(News("US inflation increases faster than expected"))
                    .Any(f => f.Rule == "us-inflation-hot" && f.Direction == 1),
                "a plainly rising inflation headline said nothing");

            // ── 되돌림이 진짜 결정을 뒤집던 것 (v1.037) ───────────────────
            // ★ 물러나는 것은 '기대' 여야 한다 ★
            //   'recession fears fade' 의 fade 는 침체 걱정에 붙은 말이지 금리에 붙은 말이
            //   아니다. 그런데 되돌림 낱말이 절 어디에 있기만 하면 인정해서, 실제 인하가
            //   인하 기대 후퇴(+1)로 정반대로 뒤집혔다(실측 v1.036).
            foreach (string[] decided in new[] {
                new[] { "Fed cuts rates as recession fears fade", "-1" },
                new[] { "Fed cuts rates by 25bp after a delayed decision", "-1" },
                new[] { "Fed raises rates despite fading growth", "1" },
                new[] { "Fed cuts rates by 25bp as rate cut bets fade", "-1" },
                new[] { "Fed raises rates by 25bp even as hike bets fade", "1" },
                new[] { "연준 기준금리 인하 단행, 성장 둔화 우려 속", "-1" } })
            {
                var fs = DollarFactors.Analyze(News(decided[0]));
                Check(fs.All(f => f.Rule != "policy-expectation-withdrawn"),
                    "an actual decision was read as a receding expectation: " + decided[0]);
                Check(fs.Any(f => f.Rule == "fed-policy" && f.Direction == int.Parse(decided[1], CultureInfo.InvariantCulture)),
                    "an actual decision lost its direction: " + decided[0]);
            }
            // 진짜 되돌림은 그대로 잡혀야 한다 - '기대' 를 가리키는 말이 함께 있다.
            foreach (string receding in new[] {
                "Fed rate cut bets fade after strong jobs report", "Traders pare bets on Fed rate cuts",
                "Fed pushes back on rate cut expectations", "연준 금리 인하 기대 후퇴", "연준 금리 인하 중단" })
                Check(DollarFactors.Analyze(News(receding)).Any(f => f.Rule == "policy-expectation-withdrawn"),
                    "a real receding expectation stopped counting: " + receding);

            // ── 걸러내기가 지나치게 좁았다 (v1.037) ───────────────────────
            // 막으려던 것은 '관세청' 하나뿐인데 뒤돌아보기까지 걸어서, 상호관세·보복관세·
            // 자동차관세·철강관세가 통째로 버려졌다.
            foreach (string kept in new[] {
                "트럼프 상호관세 발효", "미국 자동차관세 인상 검토", "철강관세 25% 부과", "보복관세 카드 만지작" })
                Check(DollarAnalysis.Relevant(kept, true), "a compound tariff headline was dropped: " + kept);
            Check(!DollarAnalysis.Relevant("관세청 1∼7월 탈세 등 1조760억원 적발", true), "the customs office came back in");
            // '이란' 이 '것이란·삶이란' 에 걸린다 - DollarFactors 는 v1.013 에 고쳤는데 여기는 그대로였다.
            Check(!DollarAnalysis.Relevant("삶이란 무엇인가 묻는 전시회", true), "a suffix was read as Iran");
            Check(DollarAnalysis.Relevant("이란 호르무즈 해협 봉쇄 경고", true), "a real Iran headline was dropped");
            // 관세가 '발효·시행' 으로 와도 방향이 있어야 한다.
            Check(DollarFactors.Analyze(News("트럼프 상호관세 발효")).Any(f => f.Rule == "trump-tariffs" && f.Direction == 1),
                "a tariff taking effect had no direction");

            // ── 국내주식 장부가 통째로 비던 것 (v1.037) ───────────────────
            // identity 접두사를 만드는 쪽은 'dstock' 을 내는데 되돌리는 쪽은 'stock' 을 찾아,
            // 국내주식이 조용히 환율로 떨어져 규칙이 하나도 안 걸렸다.

            // ── Reconcile 이 모르는 방향을 보합으로 읽던 것 (v1.037) ──────
            // '자료 부족' 은 방향 쪽에도 올 수 있는데 막는 목록에 없어서, 변동폭이 산정된
            // 적도 없는데 '보합 범위 안이라 보합' 이라고 적었다.
            foreach (string unknown in new[] { "자료 부족", "판단 보류", "변동폭 미산정", "" })
                Check(DollarAnalysisWindow.Reconcile("상승 근거 우세", unknown) == "",
                    "an explanation was written for a direction we do not have: " + unknown);

            Check(DollarAnalysis.Direction(0.001) == 0 && DollarAnalysis.Direction(-0.001) == 0, "flat boundaries");

            var rates = Series(420, 0.002);

            // ── 유사 사례 조건 (v1.039) ──────────────────────────────────
            // ★ 종전 조건은 기후값보다 유의하게 나빴다 ★
            //   800일 시세로 잰 사례별 Brier 차이 t=+3.02(합쳐서). 후보 여섯 개 중 미리 정한
            //   채택 규칙을 통과한 것은 전일 등락 부호 하나였다. 이 검사는 그 조건이
            //   '전일 부호' 임을 못박는다 - 되돌리면 유의하게 해로운 조건으로 돌아간다.
            var flatDays = Series(60, 0);
            Check(DollarAnalysis.Feature(flatDays, 30) == 1, "an unchanged day was not the middle bucket");
            var upDay = Series(60, 0); upDay[30].Value = upDay[29].Value * 1.002;
            Check(DollarAnalysis.Feature(upDay, 30) == 2, "a rising day was not the upper bucket");
            var downDay = Series(60, 0); downDay[30].Value = downDay[29].Value * 0.998;
            Check(DollarAnalysis.Feature(downDay, 30) == 0, "a falling day was not the lower bucket");
            // 5일 추세나 변동성은 더 이상 조건이 아니다. 같은 전일 부호면 같은 칸이어야 한다.
            var trending = Series(60, 0.003); trending[30].Value = trending[29].Value * 1.002;
            Check(DollarAnalysis.Feature(trending, 30) == DollarAnalysis.Feature(upDay, 30),
                "the five-day trend still separates cases that share the same previous-day sign");
            Check(DollarAnalysis.Feature(flatDays, 10) == -1, "a day without twenty observations behind it was used");
            // 칸은 셋뿐이다. 여섯이던 시절로 돌아가면 유사 사례가 절반으로 줄어 백분위가 흔들린다.
            var seen = new HashSet<int>();
            foreach (var s in new[] { flatDays, upDay, downDay, trending }) seen.Add(DollarAnalysis.Feature(s, 30));
            Check(seen.All(f => f >= 0 && f <= 2), "the feature produced a bucket outside 0..2");

            var ma5 = DollarAnalysis.MovingAverage(rates, 5);
            var ma20 = DollarAnalysis.MovingAverage(rates, 20);
            Check(double.IsNaN(ma5[3]) && double.IsNaN(ma20[18]), "incomplete moving average invented");
            Check(Math.Abs(ma5[4] - rates.Take(5).Average(r => r.Value)) < 0.00001, "weekly moving average calculation");
            Check(Math.Abs(ma20[19] - rates.Take(20).Average(r => r.Value)) < 0.00001, "monthly moving average calculation");
            var changedTail = Series(420, 0.002);
            changedTail[419].Value *= 2;
            Check(Math.Abs(DollarAnalysis.MovingAverage(changedTail, 20)[418] - ma20[418]) < 0.00001,
                "future rate changed past graph");
            var p = DollarAnalysis.Analyze(rates);
            Check(p.Enough && p.Up == p.Count && p.Flat == 0 && p.Down == 0, "rising-series outcome counts");
            Check(p.Count == rates.Count - 26, "current overlapping pattern included");
            Check(p.ValidationCount > 0 && p.ValidationHits == p.ValidationCount, "chronological validation");
            Check(Math.Abs(p.UpPercent + p.FlatPercent + p.DownPercent - 100) < 0.000001, "ratios do not sum to 100");
            var flat = DollarAnalysis.Analyze(Series(420, 0));
            Check(flat.Flat == flat.Count && flat.Up == 0 && flat.Down == 0, "flat-series outcomes");
            var falling = DollarAnalysis.Analyze(Series(420, -0.002));
            Check(falling.Down == falling.Count, "falling-series outcomes");
            var slowRise = Series(420, 0.0005);
            var slowDay = DollarAnalysis.Analyze(slowRise, 1);
            var slowWeek = DollarAnalysis.Analyze(slowRise, 5);
            var slowMonth = DollarAnalysis.Analyze(slowRise, 20);
            Check(Math.Abs(slowMonth.MedianReturn - (Math.Pow(1.0005, 20) - 1)) < 1e-10,
                "forecast month is not a cumulative return");
            Check(Math.Abs(flat.MedianReturn) < 1e-10 && falling.MedianReturn < 0, "forecast direction or flat level wrong");
            Check(p.LowerReturn <= p.MedianReturn && p.MedianReturn <= p.UpperReturn, "forecast interval reversed");
            Check(p.PriceErrorSum < 0.00001 && p.PersistenceErrorSum > 0, "price backtest did not beat persistence on constant growth");
            var quantiles = new DollarPattern { Returns = new List<double> { -0.2, -0.1, 0, 0.1, 0.2 } };
            Check(Math.Abs(quantiles.LowerReturn + 0.16) < 1e-10 && Math.Abs(quantiles.UpperReturn - 0.16) < 1e-10,
                "empirical range interpolation wrong");
            var scoreResult = new DollarAnalysisResult { DomesticAvailable = true, GlobalAvailable = true };
            DateTime scoreNow = DateTime.UtcNow;
            scoreResult.News.Add(new DollarNews { Title = "Dollar rises", Source = "A", Url = "https://news.google.com/articles/a", PublishedUtc = scoreNow });
            double positive = DollarAnalysis.Score(scoreResult, 1, scoreNow).Value;
            Check(positive > 0 && positive <= 100, "positive evidence score missing");
            scoreResult.News.Add(new DollarNews { Title = "Dollar falls", Source = "B", Url = "https://news.google.com/articles/b", PublishedUtc = scoreNow });
            Check(Math.Abs(DollarAnalysis.Score(scoreResult, 1, scoreNow).Value) < 1e-10, "opposing evidence not subtracted");
            scoreResult.News.RemoveAt(1);
            scoreResult.News.Add(scoreResult.News[0]);
            Check(DollarAnalysis.Score(scoreResult, 1, scoreNow).Value == positive, "duplicate headline inflated score");
            scoreResult.News.RemoveAt(1);
            Check(DollarAnalysis.Score(scoreResult, 20, scoreNow).Value < positive, "24h news kept full monthly weight");
            scoreResult.GlobalAvailable = false;
            Check(DollarAnalysis.Score(scoreResult, 1, scoreNow).Value < positive, "missing feed failed to reduce score");
            scoreResult.GlobalAvailable = true;
            scoreResult.News[0].Title = "Dollar rises expected";
            Check(DollarAnalysis.Score(scoreResult, 1, scoreNow).Value > 0 && DollarAnalysis.Score(scoreResult, 1, scoreNow).Value < positive,
                "forecast headline hidden or given full fact weight");
            scoreResult.News[0].Title = "Dollar rises not expected";
            Check(!DollarAnalysis.Score(scoreResult, 1, scoreNow).Available, "negated headline scored as positive");
            scoreResult.News[0].Title = "Dollar rises";
            scoreResult.News[0].PublishedUtc = scoreNow.AddDays(-2);
            Check(!DollarAnalysis.Score(scoreResult, 1, scoreNow).Available, "old article scored");
            Check(slowDay.Flat == slowDay.Count && slowWeek.Flat == slowWeek.Count && slowMonth.Up == slowMonth.Count,
                "weekly/monthly outcomes inferred from daily direction instead of cumulative return");
            Check(slowWeek.Count == slowDay.Count - 4 && slowMonth.Count == slowDay.Count - 19,
                "not-yet-observed horizon outcomes entered samples");
            Check(slowWeek.ValidationCount <= 50 && slowMonth.ValidationCount <= 13,
                "overlapping weekly/monthly validation counted as separate intervals");
            Check(DollarAnalysis.Direction(0.003, 5) == 0 && DollarAnalysis.Direction(0.005, 20) == 0,
                "weekly/monthly flat thresholds");
            // 마지막 관측은 마지막 검증의 정답에만 영향을 줘야 한다. 앞선 예측을 바꾸면 누출이다.
            var futureShock = Series(420, 0.002);
            futureShock[futureShock.Count - 1].Value *= 0.8;
            var shock = DollarAnalysis.Analyze(futureShock);
            Check(shock.ValidationCount == p.ValidationCount && shock.ValidationHits == p.ValidationHits - 1,
                  "future observation changed earlier validation predictions");
            var result = new DollarAnalysisResult { Pattern = p, Rates = rates, CheckedUtc = DateTime.UtcNow };
            p.LatestDate = DollarAnalysis.KoreaDate(DateTime.UtcNow);
            Check(DollarAnalysis.Fresh(result, DateTime.UtcNow), "fresh result held");
            p.LatestDate = p.LatestDate.AddDays(-8);
            Check(!DollarAnalysis.Fresh(result, DateTime.UtcNow), "stale result accepted");
            Check(DollarAnalysis.CounterEvidence(result, DateTime.UtcNow).Any(x => x.Contains("오래된")),
                "counter evidence ignored stale basis");

            DateTime now = new DateTime(2026, 9, 8, 2, 0, 0, DateTimeKind.Utc);
            string a = Article("원달러 환율 상승 소식 - A", now.AddHours(-1), "one");
            string duplicate = Article("원달러 환율 상승 소식 - B", now.AddMinutes(-30), "two");
            string old = Article("달러 환율 하락 과거 소식", now.AddHours(-25), "old");
            string future = Article("달러 환율 하락 미래 소식", now.AddMinutes(1), "future");
            string safeRss = "<rss><channel>" + a + duplicate + old + future + "</channel></rss>";
            var news = DollarAnalysis.ParseNews(safeRss, true, now);
            Check(news != null && news.Count == 1 && news[0].Direction == 1, "old/future/duplicate news counted");
            Check(DollarAnalysis.ParseNews("<html/>", true, now) == null, "HTML treated as news");
            Check(DollarAnalysis.ParseNews("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///private'>]><rss><channel>&x;</channel></rss>", true, now) == null,
                  "XML external entity accepted");
            Check(DollarAnalysis.ParseNews("<rss><channel></channel></rss>", true, now).Count == 0, "valid empty feed marked failed");
            foreach (string bad in new[] { "http://news.google.com/rss/articles/x", "https://news.google.com.evil.test/rss/articles/x",
                "https://user@news.google.com/rss/articles/x", "file:///x", "https://news.google.com:444/rss/articles/x", "https://news.google.com/evil" })
                Check(!DollarAnalysis.IsNewsLink(bad), "unsafe article link accepted");
            Check(DollarAnalysis.IsNewsLink("https://news.google.com/rss/articles/x"), "valid article link rejected");
            Check(Net.IsAllowedLink("https://news.google.com/rss/articles/x"), "article blocked by shared link guard");
            Check(!Net.IsAllowedLink("https://news.google.com/url?url=https://evil.test"), "news redirect bypassed shared link guard");
            Check(DollarAnalysis.ParseNews(safeRss.Replace("https://news.google.com/rss/articles/", "https://evil.test/"), true, now).Count == 0,
                  "unsafe feed item retained");
            var ambiguous = new DollarNews { Title = "달러 상승 전망은 아니다" };
            DollarAnalysis.Classify(ambiguous);
            Check(ambiguous.Direction == 0, "negated forecast classified as rise");
            ambiguous.Title = "Dollar may fall after Federal Reserve decision";
            DollarAnalysis.Classify(ambiguous);
            Check(ambiguous.Direction == 0 && ambiguous.Topic == "금리·통화정책", "uncertain Fed headline");
            ambiguous.Title = "Dollar rises while dollar drops against other currencies";
            DollarAnalysis.Classify(ambiguous);
            Check(ambiguous.Direction == 0, "conflicting headline given direction");
            ambiguous.Title = "Dollar falls as investors assess inflation";
            DollarAnalysis.Classify(ambiguous);
            Check(ambiguous.Direction == -1 && ambiguous.Topic == "물가·고용", "explicit falling headline");
            ambiguous.Title = "기업 달러 매도·엔화 강세에… 어느새 1300원 바라보는 환율";
            DollarAnalysis.Classify(ambiguous);
            Check(ambiguous.Direction == 0, "yen strength misread as dollar strength");
            string yen = "<rss><channel>" + Article("엔/달러 환율 하락 소식", now.AddHours(-1), "yen") + "</channel></rss>";
            Check(DollarAnalysis.ParseNews(yen, true, now).Count == 0, "USD/JPY article treated as USD/KRW news");

            var cfg = new Config(System.IO.Path.Combine(work, "dollar-config.json"));
            cfg.Load();
            cfg.ShowDollarAnalysis = true; cfg.DollarX = -300; cfg.DollarY = 75;
            cfg.Save();
            var saved = new Config(cfg.Path); saved.Load();
            Check(saved.ShowDollarAnalysis && saved.DollarX == -300 && saved.DollarY == 75, "settings round trip");
            int requests = 0;
            var window = new DollarAnalysisWindow(cfg, ct => { requests++; return Task.FromResult(result); });
            window.RefreshAsync().GetAwaiter().GetResult();
            window.RefreshAsync().GetAwaiter().GetResult();
            Check(requests == 1, "refresh spam starts duplicate requests");
            Check(Text(window, "_up") == "—", "stale probabilities shown");
            p.LatestDate = DollarAnalysis.KoreaDate(DateTime.UtcNow);
            window.Render(result);
            Check(Text(window, "_up") == "100.0%", "valid historical frequency missing");
            var before = Text(window, "_up");
            result.News.Add(new DollarNews { Title = "Dollar falls", Direction = -1, Topic = "환율 동향", Url = "https://news.google.com/rss/articles/x" });
            window.Render(result);
            Check(Text(window, "_up") == before, "headline arbitrarily changed historical ratios");
            Check(Text(window, "_counterEvidence").Contains("반대되는 뉴스"), "opposing news hidden from counter evidence");
            p.ValidationCount = 100; p.ValidationHits = 40; p.BaselineHits = 50;
            p.FiveDayPercent = -1;
            window.Render(result);
            Check(Text(window, "_counterEvidence").Contains("높지 않았습니다") &&
                Text(window, "_counterEvidence").Contains("실제 추세"), "bad validation or opposite trend omitted");
            result.Patterns = new List<DollarPattern> { p, slowWeek, slowMonth };
            result.Quote = new Quote { Ok = true, Price = "1,340.60", Ratio = "-0.44", Dir = -1, Source = "검사용 공유 시세" };
            slowWeek.LatestDate = slowMonth.LatestDate = DollarAnalysis.KoreaDate(DateTime.UtcNow);
            window.Render(result);
            var cells = (TextBlock[,])typeof(DollarAnalysisWindow).GetField("_periodCells", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
            Check(cells[1, 1].Text == "100.0%" && cells[2, 0].Text == "100.0%", "weekly/monthly table not independently rendered");
            var chart = (Canvas)typeof(DollarAnalysisWindow).GetField("_chart", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
            Check(chart.Children.OfType<System.Windows.Shapes.Polyline>().Single().Points.Count == 4 &&
                chart.Children.OfType<System.Windows.Shapes.Polygon>().Count() == 1, "future horizons or interval missing");
            Check(Text(window, "_summary") == "과거 참고", "no evidence presented as a zero-score outlook");
            var details = (Expander)typeof(DollarAnalysisWindow).GetField("_details", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
            var scoreText = (TextBlock)typeof(DollarAnalysisWindow).GetField("_score", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
            Check(!details.IsExpanded && ((StackPanel)details.Content).Children.Contains(scoreText), "calculation details exposed on main screen");
            var forecastValues = (TextBlock[])typeof(DollarAnalysisWindow).GetField("_forecastValues", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
            var forecastStates = (TextBlock[])typeof(DollarAnalysisWindow).GetField("_forecastStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
            Check(forecastValues.All(v => v.Text.EndsWith("원")) && forecastStates.All(v => v.Text == "과거 참고"),
                "statistical-only forecasts missing or presented as news forecasts");
            result.News.Add(new DollarNews { Title = "Dollar rises", Source = "Live", Url = "https://news.google.com/articles/live", PublishedUtc = DateTime.UtcNow });
            window.Render(result);
            Check(chart.Children.OfType<System.Windows.Shapes.Polyline>().Count() == 2 && Text(window, "_score").Contains("점"),
                "integrated forecast or score missing");
            Check(Text(window, "_counterEvidence").Contains("월간: 시간 순서 검증"), "small monthly validation sample omitted");
            // Balanced evidence is different from no evidence.
            result.Patterns.Clear(); result.Pattern = null;
            result.News.Add(new DollarNews { Title = "Dollar falls", Source = "Opposite", Url = "https://news.google.com/articles/opp", PublishedUtc = result.News.Last().PublishedUtc });
            window.Render(result);
            Check(Text(window, "_summary") == "변동폭 미산정" && Text(window, "_brief").Contains("상쇄"), "balanced evidence without price history presented as quantified forecast");
            Check(Text(window, "_brief").StartsWith("단기 비교: " + Text(window, "_summary")), "brief disagrees with summary");
            window.Render(null);
            Check(Text(window, "_up") == "—" && Text(window, "_price") == "—", "failed refresh retained stale numbers");
            Check(forecastValues.All(v => v.Text == "—") && Text(window, "_summary") == "자료 부족",
                "failed refresh retained forecast cards or headline");
            // Restore a populated payload so the late-result guard is tested with visible data.
            result.Pattern = p;
            result.Patterns = new List<DollarPattern> { p, slowWeek, slowMonth };
            window.Close();
            Check(!cfg.ShowDollarAnalysis, "manual close did not save hidden state");
            window.RefreshAsync().GetAwaiter().GetResult();
            Check(requests == 1, "closed window fetched data");

            var pending = new TaskCompletionSource<DollarAnalysisResult>();
            CancellationToken token = CancellationToken.None;
            int concurrent = 0;
            var waiting = new DollarAnalysisWindow(cfg, ct => { concurrent++; token = ct; return pending.Task; });
            var first = waiting.RefreshAsync();
            waiting.RefreshAsync().GetAwaiter().GetResult();
            Check(concurrent == 1, "concurrent fetch duplicated");
            waiting.Close();
            Check(token.IsCancellationRequested, "close did not cancel request");
            pending.SetResult(result);
            first.GetAwaiter().GetResult();
            Check(Text(waiting, "_up") == "—", "late result changed closed window");
            count += PredictionTests.Run(work);
            return count;
        }

        private static string CoinRule(string title)
        {
            var n = new DollarNews { Target = new PredictionTarget(new SymbolDef(SourceKind.Coin, "KRW-BTC", "비트코인")),
                Title = title, Source = "연합뉴스", PublishedUtc = DateTime.UtcNow,
                Url = "https://news.google.com/articles/coin", Domestic = true };
            var f = PredictionFactors.Analyze(n).FirstOrDefault(x => x.Weight > 0);
            return f == null ? "" : f.Rule.Split(':').Last() + ":" + (f.Direction > 0 ? "+" : f.Direction < 0 ? "-" : "0");
        }

        private static DollarNews CoinItem(string title)
        {
            return new DollarNews { Target = new PredictionTarget(new SymbolDef(SourceKind.Coin, "KRW-BTC", "비트코인")),
                Title = title, Source = "연합뉴스", PublishedUtc = DateTime.UtcNow,
                Url = "https://news.google.com/articles/coin" };
        }

        /// <summary>
        /// ★ 한국어는 되는데 영어가 통째로 안 됐다 ★
        ///   'earnings beat' 만 있고 'beats earnings estimates' 가 없었다. 영어 헤드라인은
        ///   동사가 앞에 오는데 어순을 하나만 적어 둔 탓이다. 미국 주식 예측에서 실적은
        ///   가장 큰 동인인데 그 자리가 통째로 비어 있었다(실측 v1.032).
        /// </summary>
        private static void StockFactorChecks()
        {
            var target = new PredictionTarget(new SymbolDef(SourceKind.WorldStock, "DLTR.O", "Dollar Tree"));
            Func<string, List<DollarFactor>> read = title => DollarFactors.Analyze(new DollarNews {
                Target = target, Title = title, Source = "Reuters",
                Url = "https://news.google.com/articles/fixture", PublishedUtc = DateTime.UtcNow });
            foreach (string good in new[] {
                "Dollar Tree beats earnings estimates", "Dollar Tree tops expectations",
                "Dollar Tree raises full-year guidance", "Dollar Tree lifts its outlook",
                "Dollar Tree shares surge on results", "Dollar Tree announces share buyback",
                "Dollar Tree upgraded by analysts" })
                Check(read(good).Any(f => f.Rule.EndsWith("own-performance", StringComparison.Ordinal) && f.Direction == 1),
                    "a positive English company headline scored nothing: " + good);
            foreach (string bad in new[] {
                "Dollar Tree misses earnings estimates", "Dollar Tree cuts full-year guidance",
                "Dollar Tree slashes its outlook", "Dollar Tree shares tumble after results",
                "Dollar Tree faces SEC investigation", "Dollar Tree downgraded by analysts",
                "Dollar Tree issues a profit warning" })
                Check(read(bad).Any(f => f.Rule.EndsWith("own-performance", StringComparison.Ordinal) && f.Direction == -1),
                    "a negative English company headline scored nothing: " + bad);
            // 방향이 없는 기사는 방향을 만들지 않는다.
            foreach (string neutral in new[] { "Dollar Tree opens a store in Ohio", "Dollar Tree names a new CFO" })
                Check(read(neutral).All(f => !f.Rule.EndsWith("own-performance", StringComparison.Ordinal)),
                    "a headline with no company event was given a direction: " + neutral);
            // 한국어 회사 사건도 같은 대접을 받아야 한다. 회사 사건 목록에만 넣고
            // 방향 목록에는 안 넣으면 그 말들은 아무 일도 하지 않는 반쪽 규칙이 된다.
            var domestic = new PredictionTarget(new SymbolDef(SourceKind.DomesticStock, "005930", "삼성전자"));
            Func<string, List<DollarFactor>> korRead = title => DollarFactors.Analyze(new DollarNews {
                Target = domestic, Title = title, Source = "연합뉴스",
                Url = "https://news.google.com/articles/fixture", PublishedUtc = DateTime.UtcNow });
            foreach (string good in new[] { "삼성전자 자사주 매입 결정", "삼성전자 배당 확대 발표", "삼성전자 어닝 서프라이즈" })
                Check(korRead(good).Any(f => f.Rule.EndsWith("own-performance", StringComparison.Ordinal) && f.Direction == 1),
                    "a positive Korean company event scored nothing: " + good);
            foreach (string bad in new[] { "삼성전자 제품 리콜 결정", "삼성전자 공정위 조사 착수", "삼성전자 배당 축소" })
                Check(korRead(bad).Any(f => f.Rule.EndsWith("own-performance", StringComparison.Ordinal) && f.Direction == -1),
                    "a negative Korean company event scored nothing: " + bad);

            // 한국어가 망가지지 않았는지도 같이 본다.
            var korean = new PredictionTarget(new SymbolDef(SourceKind.Index, "KOSPI", "코스피"));
            var factors = DollarFactors.Analyze(new DollarNews { Target = korean, Title = "코스피 상장사 실적 부진",
                Source = "연합뉴스", Url = "https://news.google.com/articles/fixture", PublishedUtc = DateTime.UtcNow });
            Check(factors.Any(f => f.Rule.EndsWith("own-performance", StringComparison.Ordinal) && f.Direction == -1),
                "the Korean performance rule broke while widening the English one");
        }

        private static void CoinFactorChecks(string work)
        {
            StockFactorChecks();
            // 한국어와 영어가 같은 뜻으로 잡혀야 한다.
            var cases = new[] {
                new[] { "비트코인 현물 ETF 순유입 사상 최대", "etf-flow:+" },
                new[] { "Bitcoin spot ETF sees record inflows", "etf-flow:+" },
                new[] { "비트코인 현물 ETF 자금 유출 확대", "etf-flow:-" },
                new[] { "Bitcoin ETF posts heavy outflows", "etf-flow:-" },
                new[] { "비트코인 ETF 반려", "etf-approval:-" },
                // ── 감사 2번 영역 재확인 (v1.033) ─────────────────────────
                // 규칙은 이미 있는데 말 하나가 목록에 없어 통째로 침묵하던 것들.
                new[] { "비트코인 규제 불확실성 해소", "crypto-regulation:+" },
                new[] { "Regulatory clarity arrives for crypto", "crypto-regulation:+" },
                new[] { "스테이블코인 공급량 급증", "stablecoin-supply:+" },
                new[] { "스테이블코인 공급량 급감", "stablecoin-supply:-" },
                new[] { "기관 투자자 비트코인 매수 확대", "institutional-adoption:+" },
                new[] { "SEC delays decision on crypto ETF", "etf-approval:-" },
                new[] { "SEC, 코인 거래소 기소", "crypto-regulation:-" },
                new[] { "Regulators ban crypto trading for retail", "crypto-regulation:-" },
                new[] { "코인 규제 완화 발표", "crypto-regulation:+" },
                new[] { "Lawmakers ease crypto rules", "crypto-regulation:+" },
                new[] { "거래소 해킹으로 자금 탈취", "exchange-incident:-" },
                new[] { "Exchange hack drains customer funds", "exchange-incident:-" },
                new[] { "테더 추가 발행 증가", "stablecoin-supply:+" },
                new[] { "USDC 대량 소각", "stablecoin-supply:-" },
                new[] { "Tether mints new USDT", "stablecoin-supply:+" },
                new[] { "기업 비트코인 매입 발표", "institutional-adoption:+" },
                new[] { "Company adopts bitcoin for its corporate treasury", "institutional-adoption:+" },
            };
            foreach (var c in cases)
                Check(CoinRule(c[0]) == c[1], "coin rule wrong for: " + c[0] + " -> got " + CoinRule(c[0]) + ", want " + c[1]);

            // ★ 결말이 원인을 이긴다 ★ '소송 기각' 을 소송으로 읽으면 무죄가 악재가 된다.
            Check(CoinRule("SEC 소송 기각") == "crypto-regulation:+", "a dismissed lawsuit was read as a lawsuit");
            Check(CoinRule("Judge dismisses SEC lawsuit against exchange") == "crypto-regulation:+",
                "a dismissed lawsuit was read as a lawsuit (English)");
            Check(CoinRule("거래소 해킹 자금 복구 완료") == "", "a recovered hack still counted as damage");

            // 코인 규칙이 주식에 새면 안 된다.
            var stock = new DollarNews { Target = new PredictionTarget(new SymbolDef(SourceKind.WorldStock, "DLTR.O", "달러트리")),
                Title = "비트코인 현물 ETF 순유입 사상 최대", Source = "연합뉴스", PublishedUtc = DateTime.UtcNow,
                Url = "https://news.google.com/articles/coin" };
            Check(PredictionFactors.Analyze(stock).All(f => !f.Rule.EndsWith("etf-flow")), "a coin rule fired on a stock");

            // 미리 알려진 일정은 실제 자금 흐름보다 가볍게 세야 한다.
            var halving = PredictionFactors.Analyze(CoinItem("비트코인 반감기 도래")).First(f => f.Rule.EndsWith("halving"));
            var flow = PredictionFactors.Analyze(CoinItem("비트코인 현물 ETF 순유입 사상 최대")).First(f => f.Rule.EndsWith("etf-flow"));
            Check(halving.Weight < flow.Weight, "a pre-announced schedule weighs as much as an actual money flow");
            Check(halving.Counter.Contains("선반영"), "halving does not warn that it is priced in");

            // ★ 정규식 안의 역슬래시 b 가 살아 있어야 한다 ★
            //   패치 도구가 그것을 백스페이스 문자로 바꿔 넣은 적이 있다(실제로 75곳).
            //   그러면 영어 규칙이 통째로 죽는데 한국어만 보면 눈치채지 못한다.
            string root = Path.GetFullPath(Path.Combine(work, "..", ".."));
            foreach (string file in new[] { "PredictionFactors.cs", "DollarFactors.cs", "DollarAnalysis.cs", "DollarSpark.cs" })
            {
                string src = Path.Combine(root, "src", file);
                if (!File.Exists(src)) continue;
                Check(File.ReadAllText(src, System.Text.Encoding.UTF8).IndexOf('\b') < 0,
                    "a control character replaced a regex escape in " + file);
            }
        }

        private static DollarNews News(string title)
        {
            return News(title, "연합뉴스");
        }
        private static DollarNews News(string title, string source)
        {
            return new DollarNews { Title = title, Source = source, PublishedUtc = DateTime.UtcNow,
                Url = "https://news.google.com/articles/fixture", Domestic = true };
        }
        private static void FactorChecks()
        {
            Check(DollarFactors.Analyze(News("연준 금리 인상 결정")).Any(f => f.Rule == "fed-policy" && f.Direction == 1), "Fed hike has wrong KRW direction");
            Check(DollarFactors.Analyze(News("미국이 금리를 올린다")).Any(f => f.Direction == 1), "US hike without dollar keyword ignored");
            Check(DollarFactors.Analyze(News("한국은행 기준금리 인하 결정")).Any(f => f.Rule == "bok-policy" && f.Direction == 1), "BOK cut treated like Fed cut");
            var both = DollarFactors.Analyze(News("연준 금리 인하, 한국은행 금리 인상"));
            Check(both.Count(f => f.Direction == -1) == 2, "two central bank actions crossed actor boundaries");
            var denied = DollarFactors.Analyze(News("연준 금리 인하하지 않기로 결정"));
            Check(denied.Count > 0 && denied.All(f => f.Direction == 0), "negated policy treated as decision");
            Check(DollarFactors.Analyze(News("연준 금리 안 올리는 유일한 중앙은행")).All(f => f.Direction == 0), "Korean short negation inverted policy");
            Check(DollarFactors.Analyze(News("지난해 연준 금리 인하 결정")).All(f => f.Direction == 0), "past event reported as current decision");
            Check(DollarFactors.Analyze(News("한은 금리 동결")).Any(f => f.Direction == 0 && f.Mechanism.Contains("상대국")), "hold has no comparative explanation");
            Check(DollarFactors.Analyze(News("SK하이닉스 미국 공장 투자 발표")).Any(f => f.Rule == "company-us-expansion" && f.Direction == 1), "company overseas funding not modeled");
            Check(DollarFactors.Analyze(News("엔캐리 청산 본격화")).Any(f => f.Rule == "yen-carry-unwind" && f.Direction == 1), "yen carry unwind ignored");
            Check(DollarFactors.Analyze(News("일본은행 금리 인상 결정")).Any(f => f.Rule == "boj-hike" && f.Counter.Contains("실제 청산")), "BOJ hike claimed realized liquidation");
            Check(DollarFactors.Analyze(News("이란 공격으로 중동 확전")).Any(f => f.Rule == "geopolitics" && f.Direction == 1), "war risk missing");
            Check(DollarFactors.Analyze(News("이란 휴전 합의")).Any(f => f.Direction == -1), "ceasefire scored like escalation");
            Check(DollarFactors.Analyze(News("국제유가 급락")).Any(f => f.Direction == -1 && f.Counter.Contains("침체")), "oil counter mechanism missing");
            Check(DollarFactors.Analyze(News("한국 정부 정책 혼선으로 불확실성 확대")).Any(f => f.Rule == "korea-policy-risk"), "domestic policy uncertainty ignored");
            Check(DollarFactors.Analyze(News("한국 정부 재정 지출 확대")).Any(f => f.Direction == 0 && f.Mechanism.Contains("함께")), "fiscal spending given unconditional direction");
            Check(DollarFactors.Analyze(News("한미 관세 협상 타결")).Any(f => f.Direction == -1), "trade agreement effect reversed");
            var warsh = DollarFactors.Analyze(News("워시 금리 인하 시사")).Single(f => f.Rule == "fed-policy");
            var decision = DollarFactors.Analyze(News("연준 금리 인하 결정")).Single(f => f.Rule == "fed-policy");
            Check(warsh.Weight < decision.Weight, "Warsh signal given full policy-decision weight");
            Check(DollarFactors.Analyze(News("케빈 워시 회의 참석")).Count == 0, "person name alone moved score");
            var pressure = DollarFactors.Analyze(News("트럼프 연준 금리 인하 촉구"));
            Check(pressure.All(f => f.Rule != "fed-policy") && pressure.Any(f => f.Rule == "trump-rate-pressure"), "Trump request treated as Fed decision");
            Check(DollarFactors.Analyze(News("트럼프 관세 철회 결정")).Any(f => f.Rule == "trump-tariffs" && f.Direction == -1), "tariff rollback treated as threat");
            Check(DollarFactors.Analyze(News("연준 연내 금리인하 전망 철회")).Any(f => f.Rule == "policy-expectation-withdrawn" && f.Direction == 1), "withdrawn cut forecast still scored as a cut");

            // ── 기대가 물러나는 문장 (감사 1-3, v1.030) ──────────────────
            // ★ '인하 기대 후퇴' 는 인하 기사가 아니라 그 반대다 ★
            //   되돌림으로 인정하는 말이 '철회·무산·포기' 셋뿐이라, 강한 고용·물가 뒤에
            //   가장 흔한 문형이 전부 인하(달러 하락) 근거로 들어갔다. 인하 사이클
            //   국면에서는 규칙 점수의 부호 자체가 뒤집힌다.
            foreach (string hawkish in new[] {
                "연준 금리 인하 기대 후퇴", "연준 금리 인하 기대 약화", "연준 금리 인하 전망 축소",
                "연준 금리 인하 속도 조절 시사", "연준 금리 인하 중단", "연준 금리 인하 지연",
                "연준 금리 인하 물 건너가나", "연준 금리 인하 기대 불투명",
                "Fed rate cut bets fade after strong jobs report", "Traders pare bets on Fed rate cuts",
                "Fed pushes back on rate cut expectations" })
                Check(DollarFactors.Analyze(News(hawkish)).Any(f => f.Rule == "policy-expectation-withdrawn" && f.Direction == 1),
                    "a receding cut expectation was still scored as a cut: " + hawkish);
            // 인상 기대가 물러나면 반대쪽이다. 방향을 한쪽만 고치면 절반은 여전히 틀린다.
            foreach (string dovish in new[] { "연준 금리 인상 우려 완화", "연준 금리 인상 전망 후퇴", "Fed rate hike bets fade" })
                Check(DollarFactors.Analyze(News(dovish)).Any(f => f.Rule == "policy-expectation-withdrawn" && f.Direction == -1),
                    "a receding hike expectation was not scored as easing: " + dovish);
            // ★ 이 규칙에서는 부정어가 곧 사건이다 ★
            //   '무산' 은 부정어 목록에도 있어, 되돌림으로 잡아 놓고 곧바로 0 으로 지웠다.
            var undone = DollarFactors.Analyze(News("연준 금리 인하 무산")).Single(f => f.Rule == "policy-expectation-withdrawn");
            Check(undone.Direction == 1 && undone.Weight > 0, "the word that made the event happen also erased it");
            // 진짜 인하 기사는 그대로여야 한다. 되돌림을 넓히다 본체를 망가뜨리면 안 된다.
            foreach (string real in new[] { "연준 기준금리 0.25%p 인하 결정", "연준 금리 인하 단행", "Fed cuts rates by a quarter point" })
                Check(DollarFactors.Analyze(News(real)).Any(f => f.Rule == "fed-policy" && f.Direction == -1),
                    "a real cut stopped counting as a cut: " + real);

            // ── easing / tightening 은 홀로 서지 못한다 (감사 1-3 곁가지) ──
            // 'inflation easing'(물가 둔화)·'conditions are tightening'(금융여건 경색)이
            // 연준의 금리 결정으로 잡혔다. 뒤엣것은 결정급 0.8 로 달러 상승 근거가 됐다.
            foreach (string notPolicy in new[] {
                "Fed officials say inflation easing", "Fed warns financial conditions are tightening",
                "Trade tensions easing after talks", "Labour market tightening further" })
                Check(DollarFactors.Analyze(News(notPolicy)).All(f => f.Rule != "fed-policy"),
                    "a non-policy use of easing/tightening became a Fed decision: " + notPolicy);
            foreach (string policy in new[] { "Fed begins monetary easing cycle", "Fed signals policy tightening ahead" })
                Check(DollarFactors.Analyze(News(policy)).Any(f => f.Rule == "fed-policy"),
                    "a real policy easing/tightening headline was dropped: " + policy);
            // 경고는 결정이 아니다.
            var warn = DollarFactors.Analyze(News("Fed warns of rate hikes")).FirstOrDefault(f => f.Rule == "fed-policy");
            Check(warn != null && warn.Weight < 0.8, "a warning was weighted like a decision");

            // ── 연준이 정하지 않는 금리 (감사 E절, v1.030) ────────────────
            // ★ '미국 + 금리' 만으로 연준의 결정이라 볼 수 없다 ★
            //   '미국 모기지 금리 인하', '미국 국채금리 내려' 가 연준 인하 결정으로 잡혔고,
            //   가중치도 결정급 0.8 이었다. 게다가 '국채 금리 급등' 은 아무 규칙에도 안
            //   걸려서, 내릴 때만 근거가 생기는 한쪽으로 기운 자료가 됐다.
            foreach (string other in new[] {
                "미국 모기지 금리 인하", "미국 국채금리 내려", "미국 국채 10년물 금리 급등",
                "미국 회사채 금리 상승", "미국 주택담보대출 금리 인하", "US treasury yields fall" })
                Check(DollarFactors.Analyze(News(other)).All(f => f.Rule != "fed-policy"),
                    "a rate the Fed does not set became a Fed decision: " + other);
            // 진짜 연준 기사는 그대로 잡혀야 한다.
            foreach (string real in new[] { "미국 기준금리 인하", "연준 국채 매입 속 기준금리 인하 결정" })
                Check(DollarFactors.Analyze(News(real)).Any(f => f.Rule == "fed-policy"),
                    "an actual Fed headline stopped counting: " + real);

            // ── 수식어가 명사를 뒤집는 문장 (감사 D·H절) ──────────────────
            // '물가 상승률 둔화' 는 '상승' 과 '둔화' 를 둘 다 가져서 물가 가열과 물가
            // 둔화가 반대 방향으로 동시에 붙었다. 상쇄되어 점수는 0 이지만, 근거 목록에
            // 정반대 두 줄이 뜨고 기사 한 건이 방향 기사로 두 번 세어진다.
            foreach (string cooling in new[] { "미국 물가 상승률 둔화", "미국 물가 상승세 둔화", "미국 소비자물가 오름세 주춤" })
            {
                var fs = DollarFactors.Analyze(News(cooling));
                Check(fs.All(f => f.Rule != "us-inflation-hot"), "a cooling inflation headline also fired the hot rule: " + cooling);
                Check(fs.Any(f => f.Rule == "us-inflation-soft" && f.Direction == -1), "a cooling inflation headline lost its direction: " + cooling);
            }
            // ★ 'risks' 는 'rises' 가 아니다 ★
            var risky = DollarFactors.Analyze(News("US inflation risks easing, Fed says"));
            Check(risky.All(f => f.Rule != "us-inflation-hot"), "the word risks was read as rises");
            // '수출 증가율 둔화' 는 늘긴 늘었다. 좋다고도 나쁘다고도 못하니 침묵해야 한다.
            Check(DollarFactors.Analyze(News("한국 수출 증가율 둔화")).All(f => f.Rule != "korea-flows"),
                "slowing export growth was scored as improving exports");
            foreach (string plain in new[] { "한국 수출 증가", "한국 수출 감소" })
                Check(DollarFactors.Analyze(News(plain)).Any(f => f.Rule == "korea-flows"),
                    "a plain export headline stopped counting: " + plain);

            // ── disagreement 안의 agreement (감사 G절) ────────────────────
            Check(DollarFactors.Analyze(News("US-Korea trade disagreement deepens")).Any(f => f.Rule == "bilateral-trade" && f.Direction == 1),
                "a deepening trade dispute was read as an agreement");
            Check(DollarFactors.Analyze(News("한미 관세 협상 타결")).Any(f => f.Rule == "bilateral-trade" && f.Direction == -1),
                "a real trade deal stopped counting as one");
            Check(DollarFactors.Analyze(News("Korea and US reach a trade agreement")).Any(f => f.Rule == "bilateral-trade" && f.Direction == -1),
                "an English trade agreement stopped counting");

            // ── 인용 헤드라인 (감사 1-4 나머지, v1.031) ───────────────────
            // ★ 절로 나누다 행위자를 잃으면 아무 근거도 남지 않는다 ★
            //   따옴표 안의 쉼표와 '한미 정상,' 같은 다섯 자 주어가 행위자를 떼어 놓아
            //   근거 0건이 됐다. 하나도 못 찾았을 때만 문장 전체를 다시 본다.
            Check(DollarFactors.Analyze(News("한미 정상, 3500억달러 대미 투자 합의")).Any(f => f.Rule == "bilateral-trade"),
                "a summit headline lost its actor to the comma");
            Check(DollarFactors.Analyze(News("UBS \"연준, 두 차례 금리 인상 전망\"")).Any(f => f.Rule == "fed-policy" && f.Direction == 1),
                "a quoted forecast headline produced no factor at all");
            // ★ 누가 한 말인지는 절이 아니라 문장의 성질이다 ★
            //   은행 이름이 앞 절에 남으면 뒷 절만 보고 결정급 0.8 을 줬다.
            var quoted = DollarFactors.Analyze(News("도이체방크 \"금융시장, 연준 금리 인상폭 과소평가\"")).Single(f => f.Rule == "fed-policy");
            Check(quoted.Weight < 0.8, "an analyst opinion outside the clause was weighted like a Fed decision");
            var ubs = DollarFactors.Analyze(News("UBS \"연준, 두 차례 금리 인상 전망\"")).Single(f => f.Rule == "fed-policy");
            Check(ubs.Weight < 0.8, "a quoted bank forecast was weighted like a decision");
            // 두 배우를 가르는 원래 의도는 그대로여야 한다 - 근거가 나온 문장은 다시 보지 않는다.
            // 위 320행에서 이미 분석한 그 문장을 그대로 쓴다.
            Check(both.Any(f => f.Rule == "fed-policy" && f.Direction == -1) && both.Any(f => f.Rule == "bok-policy" && f.Direction == -1),
                "two actors in one headline were merged again");
            // ★ 근거가 하나라도 있으면 문장 전체를 다시 보지 않는다 ★
            //   다시 보면 절 경계를 넘어 낱말이 섞인다. 아래 문장은 유가가 급등했을 뿐
            //   물가가 급등한 게 아닌데, 통째로 읽으면 '미국 + 물가 + 급등' 이 되어
            //   있지도 않은 물가 가열 근거가 생긴다.
            var oilThenPrices = DollarFactors.Analyze(News("국제유가 급등, 미국 물가 안정"));
            Check(oilThenPrices.Any(f => f.Rule == "oil" && f.Direction == 1), "the oil clause stopped counting");
            Check(oilThenPrices.All(f => f.Rule != "us-inflation-hot"), "the whole-sentence fallback ran on a sentence that already had evidence");
            // 결정 기사는 여전히 결정급이다. 조건부를 넓히다 본체를 깎으면 안 된다.
            Check(DollarFactors.Analyze(News("연준, 기준금리 0.25%p 인하")).Single(f => f.Rule == "fed-policy").Weight == 0.8,
                "a plain decision headline lost its decision weight");
            Check(DollarFactors.Analyze(News("우크라이나 사흘간의 공격 금지 정책 합의")).Any(f => f.Direction == -1), "attack ban scored as escalation");
            Check(DollarFactors.Analyze(News("UBS forecasts two US Fed rate hikes")).First(f => f.Rule == "fed-policy").Weight < 0.8, "plural English forecast treated as a decision");
            Check(DollarFactors.Analyze(News("트럼프 금리 내려라 압박")).Any(f => f.Rule == "trump-rate-pressure" && f.Direction == -1), "imperative rate cut request ignored");
            var actual = DollarFactors.Analyze(News("트럼프 관세 부과 결정")).First(f => f.Rule == "trump-tariffs");
            var threat = DollarFactors.Analyze(News("트럼프 관세 부과 위협")).First(f => f.Rule == "trump-tariffs");
            Check(threat.Weight < actual.Weight, "tariff threat scored as implementation");
            var r = new DollarAnalysisResult { DomesticAvailable = true, GlobalAvailable = true };
            r.News.Add(News("연준 금리 인하 결정")); r.News.Add(News("한은 금리 인하 결정"));
            Check(DollarFactors.Narrative(r, DateTime.UtcNow).Contains("상쇄"), "opposite channels not combined");
            Check(DollarAnalysis.Score(r, 1, DateTime.UtcNow).DirectionalCount > 0, "macro factors not connected to score");
            var within = new DollarAnalysisResult { DomesticAvailable = true, GlobalAvailable = true };
            within.News.Add(News("연준 금리 인하, 한은 금리 인하"));
            var mixedScore = DollarAnalysis.Score(within, 1, DateTime.UtcNow);
            Check(mixedScore.Available && mixedScore.UpEvidence > 0 && mixedScore.DownEvidence > 0 && Math.Abs(mixedScore.Value) < 1e-8,
                "opposite factors inside one article erased before scoring");
            var context = News("세계 경제 브리핑"); context.Context = "연준 금리 인하 결정. 이란 공격으로 확전.";
            Check(DollarFactors.Analyze(context).Count >= 2, "body or RSS context ignored");
            context.Context = "한은 금리 인상 결정";
            Check(DollarFactors.Analyze(context).All(f => f.Category == "한은·한국 경제"), "factor cache retained changed context");
            // ★ 캐시를 확인하는 값이 캐시보다 비싸면 안 된다 ★ (#39)
            //   전에는 확인할 때마다 제목+본문을 이어 붙여 열쇠를 만들었다. 이제 참조만 본다.
            //   같은 글자라도 다른 개체면 다시 센다 - 그래도 답은 같아야 한다.
            var reused = News("연준 금리 인하 결정");
            var first = DollarFactors.Analyze(reused);
            Check(ReferenceEquals(DollarFactors.Analyze(reused), first), "the factor cache no longer holds between calls");
            reused.Context = new string("연준 금리 인상 결정".ToCharArray());
            Check(!ReferenceEquals(DollarFactors.Analyze(reused), first), "a changed body was served from the factor cache");
            // 같은 기사를 품목별로 물으면 답이 달라야 한다 - 품목 열쇠가 캐시에 들어가 있는지.
            var shared = News("삼성전자 영업이익 급증");
            shared.Target = new PredictionTarget(new SymbolDef(SourceKind.DomesticStock, "005930", "삼성전자"));
            var stockFactors = PredictionFactors.Analyze(shared);
            shared.Target = new PredictionTarget(new SymbolDef(SourceKind.Coin, "KRW-BTC", "비트코인"));
            Check(!ReferenceEquals(PredictionFactors.Analyze(shared), stockFactors), "one instrument's factors were served to another");
            Check(DollarNewsSources.IsArticle("https://www.yna.co.kr/view/AKR20260908001000071") &&
                !DollarNewsSources.IsArticle("https://www.yna.co.kr.evil.test/view/AKR1") &&
                DollarNewsSources.IsArticle("https://www.bbc.co.uk/news/articles/example"), "publisher URL validation");
            Check(!DollarNewsSources.IsArticle("https://user@www.hankyung.com/article/123") &&
                !DollarNewsSources.IsArticle("https://www.hankyung.com:444/article/123"), "publisher authority guard");
            string content = string.Concat(Enumerable.Repeat("한국은행은 금리 동결을 결정했다. ", 15));
            Check(DollarNewsSources.ExtractBody("<article><script>bad()</script><p>" + content + "</p></article>").Contains("금리 동결"), "public article text not extracted");
            Check(DollarNewsSources.ExtractBody("<div>not an article</div>") == "", "page chrome treated as article");
            // #23 사진 설명·저작권 꼬리말은 본문이 아니다.
            string tail = string.Concat(Enumerable.Repeat("한국은행은 금리 동결을 결정했다. ", 15));
            // 실물 연합뉴스 기사의 꼬리말 문단을 그대로 쓴다 (yna.co.kr 에서 받아 확인).
            string withTail = "<article><p>" + tail + "</p>" +
                "<p>제보는 카카오톡 okjebo &lt;저작권자(c) 연합뉴스, 무단 전재-재배포, AI 학습 및 활용 금지&gt; 2026/09/07 15:34 송고</p></article>";
            Check(!DollarNewsSources.ExtractBody(withTail).Contains("okjebo"), "the tip-line footer was kept as article text");
            Check(DollarNewsSources.ExtractBody(withTail).Contains("금리 동결"), "the real body was lost with the footer");
            // 사진 출처가 붙은 문단은 출처만 지우고 남긴다.
            string photo = "<article><p>[EPA=연합뉴스 자료사진 재판매 및 DB 금지] " + tail + "</p></article>";
            string extracted = DollarNewsSources.ExtractBody(photo);
            Check(extracted.Contains("금리 동결") && !extracted.Contains("EPA="),
                "a photo credit either survived or took the reporting with it");
            // ★ 정규식이 받아들인 문자열을 JSON 파서가 거부할 수 있다 ★ (2026-09-10 감사 #15·#22)
            //   \x 같은 이스케이프는 정규식은 통과하고 파서는 거부한다. 그러면 .S 가 null 이라
            //   NRE 가 WhenAll 을 타고 올라가, 기사 한 건 때문에 본문 읽기 전체가 끝났다.
            string escaped = "<script type=\"application/ld+json\">{\"articleBody\":\"" + string.Concat(Enumerable.Repeat("금리 동결 ", 40)) + "\\x41\"}</script>";
            string safe = null; bool threw = false;
            try { safe = DollarNewsSources.ExtractBody(escaped); } catch (Exception) { threw = true; }
            Check(!threw && safe == "", "a bad JSON escape in articleBody crashed the whole enrichment");
            Check(DollarNewsSources.ExtractBody("<script type=\"application/ld+json\">{\"articleBody\":\"" + content + "\"}</script>").Contains("금리 동결"), "a valid articleBody was not extracted");

            // ── 본문을 몇 건이나 읽는가 (2026-09-09) ─────────────────────────
            // 실측: 저장된 기사 687건 중 본문이 있는 것은 10% 뿐이었다.
            // 제목 한 줄로는 알 수 없는 것이 있으니 여기가 예측의 실제 병목이다.
            var pool = new List<DollarNews>();
            DateTime baseTime = DateTime.UtcNow.AddHours(-2);
            string[] hosts = { "https://www.yna.co.kr/view/AKR", "https://www.hankyung.com/article/", "https://www.bbc.com/news/articles/" };
            string[] sources = { "연합뉴스", "한국경제", "BBC" };
            for (int i = 0; i < 30; i++)
                for (int src = 0; src < 3; src++)
                    pool.Add(new DollarNews { Title = "기사 " + src + "-" + i, Source = sources[src],
                        Url = hosts[src] + (10000000 + i * 3 + src), PublishedUtc = baseTime.AddMinutes(i) });
            // 집계 링크는 본문을 읽을 수 없다. 후보에 들어가면 안 된다.
            pool.Add(new DollarNews { Title = "집계 링크", Source = "연합뉴스",
                Url = "https://news.google.com/rss/articles/xyz", PublishedUtc = baseTime.AddMinutes(99) });

            var picked = DollarNewsSources.ChooseBodies(pool, 24);
            Check(picked.Count == 24, "body-read limit not honoured");
            Check(picked.All(n => DollarNewsSources.IsArticle(n.Url)), "an unreadable aggregator link was queued for a body read");
            // 한 매체가 전부를 가져가면 같은 논조만 깊게 읽게 된다.
            foreach (string src in sources)
                Check(picked.Count(n => n.Source == src) <= 10 && picked.Count(n => n.Source == src) > 0,
                    "one publisher took over the body reads: " + src);
            // 최신 것을 먼저 읽어야 한다. 규칙이 이미 아는 기사부터 읽으면 새 정보가 안 들어온다.
            Check(picked.First().PublishedUtc >= picked.Last().PublishedUtc, "chosen articles are not newest-first");
            Check(picked.Any(n => n.PublishedUtc == baseTime.AddMinutes(29)), "the newest article was left unread");

            // 0 이면 아무것도 읽지 않는다. 제목만으로 돌아간다.
            Check(DollarNewsSources.ChooseBodies(pool, 0).Count == 0, "zero limit still queued reads");
            // 상한 위로는 못 올라간다. 남의 서버를 무한정 두드리지 않는다.
            Check(DollarNewsSources.ChooseBodies(pool, 9999).Count <= Config.MaxBodyLimit, "body reads exceeded the hard ceiling");
            // 예전 기본값(9)보다 확실히 많이 읽는다 - 이 작업의 요점이다.
            Check(Config.DefaultBodyLimit > 9 && DollarNewsSources.ChooseBodies(pool, Config.DefaultBodyLimit).Count > 9,
                "the default still reads as few bodies as before");
            Check(DollarNewsSources.ChooseBodies(null, 24).Count == 0, "null pool threw instead of returning nothing");

            // 코인 고유 요인. 전에는 규칙이 '연준 할인율' 과 '전쟁' 둘뿐이라
            // ETF·규제·해킹 기사가 전부 0점이었다.
            CoinFactorChecks(Program.BaseDir);

            // ── 2026-09-08 감사에서 나온 것들. 전부 실측으로 재현하고 고쳤다. ──
            // 아래 반례가 하나라도 무너지면 그 규칙은 되돌아간 것이다.

            // ① 부정 활용형. '안 하' 만 잡던 시절 '인하 안 한다' 가 실제 인하 결정과
            //    같은 만점(0.8) 근거로 들어갔다.
            foreach (string refused in new[] { "연준 금리 인하 안 한다", "연준 금리 인하 안 했다",
                "연준 금리 인하 안 할 것", "연준 금리 인하 안 해", "연준 금리 인하 안한다",
                "연준 금리 인하 없다", "연준 금리 인하 불가" })
                Check(DollarFactors.Analyze(News(refused)).All(f => f.Direction == 0),
                    "negated Korean policy still scored as a decision: " + refused);
            foreach (string refused in new[] { "Fed won't cut rates", "Fed isn't cutting rates this year",
                "Fed rules out rate cut", "Fed unlikely to cut rates", "Fed says no rate cut needed" })
                Check(DollarFactors.Analyze(News(refused)).All(f => f.Direction == 0),
                    "negated English policy still scored as a decision: " + refused);
            // '불가피' 는 부정이 아니라 강한 단언이다. '불가' 만 보고 0점 처리하면 안 된다.
            Check(DollarFactors.Analyze(News("연준 금리 인상 불가피")).Any(f => f.Rule == "fed-policy" && f.Direction == 1),
                "'불가피' read as a negation");
            // 부정어가 아닌 '제안 했다' 를 부정으로 읽으면 안 된다(뒤돌아보기 가드).
            Check(DollarFactors.Analyze(News("연준 금리 인하를 제안 했다")).Any(f => f.Rule == "fed-policy" && f.Direction == -1),
                "lookbehind guard swallowed a real decision");

            // ★ 금리 값이 둘 들어간 결정 기사 ★
            //   Cut/Hike 는 금리와 인하 사이를 열여덟 자까지만 본다. 결정 기사는
            //   '기준금리 4.25~4.50%로 0.25%p 인하' 처럼 값을 둘 담는 일이 흔해
            //   그 창을 넘어 통째로 빠졌다. 가장 확정적인 기사가 사라진 것이다.
            //   숫자 덩어리를 한 글자로 줄여서 재되, 창 자체는 넓히지 않는다.
            Check(DollarFactors.Analyze(News("연준 기준금리 4.25~4.50%로 0.25%p 인하")).Any(f => f.Rule == "fed-policy" && f.Direction == -1),
                "a decision headline carrying two rate figures was dropped");
            Check(DollarFactors.Analyze(News("연준, 기준금리 4.25~4.50%로 0.25%p 인하")).Any(f => f.Rule == "fed-policy" && f.Direction == -1),
                "the same headline with a leading actor comma was dropped");
            Check(DollarFactors.Analyze(News("한국은행 기준금리 연 2.50%에서 2.25%로 0.25%포인트 인하")).Any(f => f.Rule == "bok-policy" && f.Direction == 1),
                "a BOK decision with from/to figures was dropped");
            Check(DollarFactors.Analyze(News("연준 기준금리 5.25~5.50%로 25bp 인상")).Any(f => f.Rule == "fed-policy" && f.Direction == 1),
                "a hike expressed in basis points was dropped");
            Check(DollarFactors.Analyze(News("연준 기준금리 4.25~4.50%로 동결")).Any(f => f.Rule == "fed-hold"),
                "a hold with two rate figures was dropped");
            // 숫자를 줄여도 부정·과거·전망 판정은 그대로여야 한다.
            Check(DollarFactors.Analyze(News("지난해 연준 기준금리 4.25~4.50%로 0.25%p 인하")).All(f => f.Direction == 0),
                "compacting numbers broke the past-event guard");
            var guess = DollarFactors.Analyze(News("연준 기준금리 4.25~4.50%로 0.25%p 인하 전망")).First(f => f.Rule == "fed-policy");
            var done = DollarFactors.Analyze(News("연준 기준금리 4.25~4.50%로 0.25%p 인하")).First(f => f.Rule == "fed-policy");
            Check(guess.Weight < done.Weight, "compacting numbers made a forecast weigh as much as a decision");
            // 숫자만 있고 정책이 아닌 문장을 끌어오면 안 된다.
            Check(DollarFactors.Analyze(News("코스피 2,450선 회복, 금리 우려 완화")).All(f => f.Rule != "fed-policy" && f.Rule != "bok-policy"),
                "number compaction pulled an unrelated sentence into a policy decision");
            // 사용자에게 보이는 문장은 원문 그대로여야 한다 - 숫자를 지운 채 보여주면 안 된다.
            var shown = DollarFactors.Analyze(News("연준 기준금리 4.25~4.50%로 0.25%p 인하")).First(f => f.Rule == "fed-policy");
            Check(shown.Observation.Contains("4.25") && shown.Observation.Contains("0.25"),
                "the compacted text leaked into what the user reads");

            // ② 소수점은 절 경계가 아니다. 결정 보도는 거의 항상 소수점을 담는다.
            Check(DollarFactors.Analyze(News("연준 기준금리 3.50%로 인하")).Any(f => f.Rule == "fed-policy" && f.Direction == -1),
                "decimal point split the clause and dropped the decision");
            Check(DollarFactors.Analyze(News("연준 기준금리 4.5%로 동결")).Any(f => f.Rule == "fed-hold"),
                "decimal point split the clause and dropped the hold");

            // ③ '연준,' 같은 5자 미만 주어 절은 버리지 말고 다음 절에 붙인다.
            //    한국 통신사 헤드라인의 표준형이다.
            Check(DollarFactors.Analyze(News("연준, 기준금리 0.25%p 인하")).Any(f => f.Rule == "fed-policy" && f.Direction == -1),
                "leading actor clause dropped, decision lost");
            Check(DollarFactors.Analyze(News("한은, 기준금리 인하")).Any(f => f.Rule == "bok-policy" && f.Direction == 1),
                "leading actor clause dropped for BOK");
            Check(DollarFactors.Analyze(News("트럼프, 관세 25% 부과 결정")).Any(f => f.Rule == "trump-tariffs" && f.Direction == 1),
                "leading actor clause dropped for tariffs");
            // 천단위 쉼표도 경계가 아니다.
            Check(DollarFactors.Analyze(News("연준 금리 인하, 한국은행 금리 인상")).Count(f => f.Direction == -1) == 2,
                "two central bank actions crossed actor boundaries after the split change");

            // ④ 무역/환율/반도체 '전쟁' 과 'Warsh·warns' 는 무력 분쟁이 아니다.
            foreach (string notWar in new[] { "미중 무역전쟁 격화 우려", "환율전쟁 격화", "반도체 전쟁 격화",
                "Warsh strikes hawkish tone, warns on inflation", "Trump escalates trade war with Canada" })
                Check(DollarFactors.Analyze(News(notWar)).All(f => f.Rule != "geopolitics"),
                    "non-military conflict scored as safe-haven demand: " + notWar);
            Check(DollarFactors.Analyze(News("연준이 공격적 금리 인하에 나설 것이란 전망")).All(f => f.Rule != "geopolitics"),
                "aggressive stance and '것이란' read as geopolitics");
            // 진짜 지정학은 그대로 잡혀야 한다.
            Check(DollarFactors.Analyze(News("이란 공격으로 중동 확전")).Any(f => f.Rule == "geopolitics" && f.Direction == 1),
                "real war risk lost by the geopolitics guard");
            Check(DollarFactors.Analyze(News("이란 휴전 합의")).Any(f => f.Direction == -1),
                "ceasefire lost by the geopolitics guard");

            // ⑥ 점수가 '보합' 밖으로 나올 수 있어야 한다.
            //   전에는 방향을 못 낸 기사까지 분모에 들어가(실측 53건 중 33건) 신호를
            //   2.65배 묽혔고, 같은 보수성이 coverage 에 한 번 더 걸렸다. 그래서 저장된
            //   규칙 예측 45건이 전부 보합이었다 - 규칙은 어떤 날에도 방향을 말할 수 없었다.
            //   12.5점은 2026-09-08 실측 기준선이다(일간 보합 문턱 0.1% ÷ 과거 변동폭 0.8%).
            ScoreScaleChecks();

            // ⑦ 품목별 규칙(PredictionFactors)도 같은 잣대를 쓴다.
            PredictionFactorChecks();
        }

        // 한쪽으로 쏠린 강한 결정 기사 여덟 건(서로 다른 매체) + 침묵한 기사 스무 건.
        // 실제 하루의 모양이다 - 오늘 재 보니 기사 중 62%가 아무 방향도 내지 않는다.
        private static DollarAnalysisResult OneSidedDay(int strong, int silent, DateTime now)
        {
            var r = new DollarAnalysisResult { DomesticAvailable = true, GlobalAvailable = true };
            // 기준 시각보다 미래면 Score 가 통째로 걸러낸다. 시각을 넘겨받아 고정한다.
            DateTime when = now.AddMinutes(-5);
            string[] titles = { "연준 기준금리 인하 결정", "연준 금리 인하 단행", "미국 연준 금리를 내렸다",
                "연방준비제도 기준금리 인하 확정", "Fed cuts rates", "연준 정책금리 인하 결정했다",
                "연준 기준금리 내린다", "Federal Reserve lowers benchmark rates" };
            string[] sources = { "Reuters", "Bloomberg", "CNBC", "BBC", "CNN", "한국경제", "연합뉴스", "Financial Times" };
            for (int i = 0; i < strong && i < titles.Length; i++)
            { var a = News(titles[i], sources[i]); a.PublishedUtc = when; r.News.Add(a); }
            for (int i = 0; i < silent; i++)
            { var a = News("무관한 소식 " + i + "번 기사", "기타매체" + i); a.PublishedUtc = when; r.News.Add(a); }
            return r;
        }

        private static void ScoreScaleChecks()
        {
            DateTime now = DateTime.UtcNow;
            var strong = DollarAnalysis.Score(OneSidedDay(8, 20, now), 1, now);
            Check(strong.DirectionalCount == 8, "one-sided day lost directional articles");
            Check(strong.Value < -12.5, "one-sided strong news cannot leave 보합 - the score is diluted flat again");
            // 침묵한 기사가 늘어도 방향 근거의 크기는 그대로여야 한다. 침묵은 반대 증거가 아니다.
            var noisy = DollarAnalysis.Score(OneSidedDay(8, 60, now), 1, now);
            Check(Math.Abs(noisy.Value - strong.Value) < 1e-9, "silent articles counted as counter-evidence");
            // 그렇다고 얇은 근거가 방향을 말하면 안 된다. 두 건으로는 문턱을 못 넘는다.
            var thin = DollarAnalysis.Score(OneSidedDay(2, 20, now), 1, now);
            Check(Math.Abs(thin.Value) < 12.5, "two articles alone reached a directional call");
            Check(Math.Abs(thin.Value) < Math.Abs(strong.Value), "thin evidence scored as high as a full day");
            // 방향을 못 낸 요인(동결·재정)은 같은 기사의 방향 요인을 묽히면 안 된다.
            var mixed = News("연준 금리 인하 결정. 한국 정부 재정 지출 확대", "Reuters");
            mixed.Context = "한국 정부 재정 지출 확대";
            var one = News("연준 금리 인하 결정", "Reuters");
            Check(DollarAnalysis.Score(Wrap(mixed, now), 1, now).DownEvidence >=
                  DollarAnalysis.Score(Wrap(one, now), 1, now).DownEvidence - 1e-9,
                "a zero-weight factor diluted the directional factor in the same article");
        }

        private static DollarAnalysisResult Wrap(DollarNews n, DateTime now)
        {
            var r = new DollarAnalysisResult { DomesticAvailable = true, GlobalAvailable = true };
            n.PublishedUtc = now.AddMinutes(-5);
            r.News.Add(n);
            return r;
        }

        private static DollarNews CoinNews(string title)
        {
            return new DollarNews { Target = new PredictionTarget(new SymbolDef(SourceKind.Coin, "KRW-BTC", "비트코인")),
                Title = title, Source = "연합뉴스", PublishedUtc = DateTime.UtcNow,
                Url = "https://news.google.com/articles/fixture", Domestic = true };
        }

        private static void PredictionFactorChecks()
        {
            // 'war' 를 부분 문자열로 두면 warns·toward·software·무역전쟁이 무력 분쟁이 된다.
            // 저장된 DOGE·BTC 기록의 하락 근거가 실제로 이것이었다.
            foreach (string notWar in new[] { "호주 연금, 규제 당국의 조치를 thwart 하려 시도",
                "Trump escalates trade war with Canada", "미중 무역전쟁 격화",
                "소프트웨어 기업 software 실적 발표", "Fed official warns on inflation" })
                Check(PredictionFactors.Analyze(CoinNews(notWar)).All(f => f.Rule.Split(':').Last() != "risk-off"),
                    "substring 'war' scored as armed conflict: " + notWar);
            Check(PredictionFactors.Analyze(CoinNews("이란 공습으로 중동 확전")).Any(f => f.Rule.EndsWith("risk-off")),
                "real conflict lost by the risk-off guard");

            // 부정 활용형 - DollarFactors 와 뜻이 같아야 한다.
            foreach (string denied in new[] { "연준 금리 인하 안 한다", "연준 금리 인하 없다",
                "Fed won't cut rates", "Fed unlikely to cut rates" })
                Check(PredictionFactors.Analyze(CoinNews(denied)).All(f => f.Direction == 0),
                    "negated policy scored for a coin: " + denied);

            // 전망·우려·확률은 확정 사건의 절반 가중이어야 한다.
            var expected = PredictionFactors.Analyze(CoinNews("연준 금리 인상 결정")).First(f => f.Rule.EndsWith("discount-rate"));
            foreach (string guess in new[] { "연준 금리 인상 우려 확산", "연준 금리 인상 확률 60%로 상승", "연준 금리 인상 관측" })
                Check(PredictionFactors.Analyze(CoinNews(guess)).First(f => f.Rule.EndsWith("discount-rate")).Weight < expected.Weight,
                    "forecast scored as a decision: " + guess);

            // 소수점이 절을 끊으면 결정 기사가 통째로 빠진다.
            Check(PredictionFactors.Analyze(CoinNews("연준 기준금리 3.50%로 인하")).Any(f => f.Rule.EndsWith("discount-rate")),
                "decimal point split the clause for a coin");

            // 저장하는 Rule 에 품목 키가 붙는다. 접두사 없이 비교하면 중복 제거가 영원히 거짓이라
            // 같은 규칙이 절마다 쌓여 점수가 부푼다.
            var repeated = CoinNews("연준 금리 인상 결정");
            repeated.Context = "연준 금리 인상 결정. 연준 금리 인상 결정. 연준 금리 인상 결정";
            Check(PredictionFactors.Analyze(repeated).Count(f => f.Rule.EndsWith("discount-rate")) == 1,
                "duplicate rule stacked because the dedup key ignored the target prefix");
        }

        internal static int Live(string work)
        {
            var r = DollarAnalysis.FetchAsync("HANA", CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine("LIVE quote=" + (r.Quote != null && r.Quote.Ok) + " rates=" + r.Rates.Count +
                " pattern=" + (r.Pattern == null ? "none" : r.Pattern.Count.ToString()) +
                " domestic=" + r.DomesticAvailable + " international=" + r.GlobalAvailable + " articles=" + r.News.Count);
            foreach (var p in r.Patterns)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "Horizon={0} samples={1} up={2:0.0}% flat={3:0.0}% down={4:0.0}% validation={5} hits={6} baselineHits={7}",
                    p.Horizon,p.Count,p.UpPercent,p.FlatPercent,p.DownPercent,p.ValidationCount,p.ValidationHits,p.BaselineHits));
            Console.WriteLine("BODY=" + r.News.Count(n => n.BodyRead) + " FACTORS=" + DollarFactors.Collect(r, DateTime.UtcNow).Count + " feeds=" + r.TopicFeedsAvailable + "/" + r.TopicFeedsExpected);
            Console.WriteLine(DollarFactors.Narrative(r, DateTime.UtcNow));
            Console.WriteLine("SCORE=" + DollarAnalysis.Score(r, 1, DateTime.UtcNow).Value);
            foreach (var f in DollarFactors.Collect(r, DateTime.UtcNow))
                Console.WriteLine("FACTOR " + f.Rule + " " + f.Direction + " w=" + f.Weight + " " + f.News.Source + " | " + f.Observation);
            if (r.Rates.Count < 120 || !r.DomesticAvailable || !r.GlobalAvailable) throw new Exception("Live data unavailable");
            return 1;
        }

        private static string Text(DollarAnalysisWindow w, string name)
        {
            var item = typeof(DollarAnalysisWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w);
            return item is BriefTextBlock ? ((BriefTextBlock)item).Text : ((TextBlock)item).Text;
        }

        private static List<DollarRate> Series(int n, double change)
        {
            var result = new List<DollarRate>();
            DateTime date = new DateTime(2025, 1, 1);
            double price = 1300;
            for (int i = 0; i < n; i++)
            {
                while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) date = date.AddDays(1);
                result.Add(new DollarRate { Date = date, Value = price });
                date = date.AddDays(1); price *= 1 + change;
            }
            return result;
        }

        private static string Article(string title, DateTime time, string id)
        {
            return "<item><title>" + title + "</title><link>https://news.google.com/rss/articles/" + id +
                "</link><pubDate>" + time.ToString("r", CultureInfo.InvariantCulture) + "</pubDate><source>Source</source></item>";
        }
    }
}
