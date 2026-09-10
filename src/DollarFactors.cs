// Economic transmission rules. Observations and conditional inferences stay separate.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeskWidget
{
    internal sealed class DollarFactor
    {
        public string Category, Observation, Mechanism, Counter, Rule;
        public int Direction;
        public double Weight;
        public DollarNews News;
    }

    internal static class DollarFactors
    {
        public static readonly string[] Categories = { "연준·미국 경제", "한은·한국 경제", "한미 관계", "세계 정세", "기업·자금 흐름", "일본·엔캐리" };
        private const string Fed = @"연준|연방준비|워시|Warsh|파월|Powell|미국.{0,8}금리|\bFed\b|Federal Reserve|FOMC";
        // ★ 연준이 정하는 금리만 연준의 결정이다 ★
        //   Fed 조건에 '미국 + 금리' 가 들어 있어서 '미국 모기지 금리 인하',
        //   '미국 국채금리 내려' 가 연준의 인하 결정(가중치 0.8, 결정급)으로 잡혔다.
        //   게다가 '국채 금리 급등' 은 아무 규칙에도 안 걸려서, 내릴 때만 근거가 되는
        //   한쪽으로 기운 자료가 됐다(실측 v1.029).
        private const string FedActor = @"연준|연방준비|워시|Warsh|파월|Powell|\bFed\b|Federal Reserve|FOMC";
        private const string NonPolicyRate = @"(?:모기지|주택담보|주담대|국채|회사채|장기|시장|대출|예금|카드|전세|채권)\s*금리|" +
            @"금리\s*(?:스프레드|격차)|\b(?:mortgage|treasury|bond|corporate|deposit|lending|yields?)\b";
        /// <summary>연준 이야기인가. '미국 금리' 만으로는 부족하다 - 어떤 금리인지 봐야 한다.</summary>
        private static bool IsFed(string s)
        {
            return Has(s, FedActor) || (Has(s, @"미국.{0,8}금리") && !Has(s, NonPolicyRate));
        }

        // ★ 수식어가 명사를 뒤집는다 ★
        //   '물가 상승률 둔화' 는 '상승' 과 '둔화' 를 둘 다 가지고 있어서, 물가 가열과
        //   물가 둔화 규칙이 반대 방향으로 동시에 붙었다. 점수에서는 상쇄되지만
        //   근거 목록에는 정반대 두 줄이 같이 뜨고, 기사 한 건이 방향 기사로 두 번 세어졌다.
        private const string SlowingWord = @"(?:상승률|상승세|상승폭|증가율|증가폭|증가세|오름세|성장률|growth|pace)" +
            @"\s*(?:이|가|은|는)?\s*.{0,6}(?:둔화|축소|약화|하락|감소|낮아|주춤|꺾|\bslow\w*|\beas(?:e|es|ed|ing)\b|\bmoderat\w*|\bcool\w*)";
        /// <summary>'늘긴 늘되 느려진다' 는 문장인가.</summary>
        private static bool Slowing(string s) { return Has(s, SlowingWord); }

        private const string Bok = @"한국은행|한은|Bank of Korea|\bBOK\b";
        private const string Cut = @"(?:금리|정책금리|기준금리).{0,18}(?:인하|내[리린릴렸려])|(?:인하|내[리린릴렸려]).{0,10}금리|(?:\bcut(?:s|ting|back)?\b|\blower\w*|\breduc\w*)\s+(?:(?:the|interest|policy|benchmark)\s+){0,3}rates?|rate\s+cuts?|(?:monetary|policy|rate|rates|credit)\s+easing|eas(?:e|es|ed|ing)\s+(?:its\s+|the\s+)?(?:policy|rates?|monetary)|easing\s+(?:cycle|campaign|path|bias)";
        private const string Hike = @"(?:금리|정책금리|기준금리).{0,18}(?:인상|올[리린릴렸])|(?:인상|올[리린릴렸]).{0,10}금리|(?:\bhik(?:e|es|ed|ing)\b|\brais(?:e|es|ed|ing)\b)\s+(?:(?:the|interest|policy|benchmark)\s+){0,3}rates?|rate\s+hikes?|(?:monetary|policy|rate|rates|credit)\s+tightening|tighten(?:s|ed|ing)?\s+(?:its\s+|the\s+)?(?:policy|rates?|monetary)|tightening\s+(?:cycle|campaign|path|bias)";
        // ★ 기대가 물러나는 말들 ★
        //   '인하 기대 후퇴' 는 인하 기사가 아니라 그 반대다. 그런데 되돌림으로 인정하던
        //   말이 '철회·무산·포기' 셋뿐이라, 강한 고용·물가 뒤에 흔한 '기대 후퇴/약화/축소',
        //   'bets fade', 'pare bets' 가 전부 인하(달러 하락) 근거로 들어갔다(실측 v1.012).
        //   한국어는 '인하'와 되돌림 말이 붙어 나오므로 좁게 보고, 영어는 어순이 반대라
        //   같은 절 안에 있기만 하면 인정한다 - 절은 이미 Clauses() 가 나눠 준다.
        private const string RecedeKo = @"철회|무산|포기|후퇴|약화|축소|중단|제동|지연|연기|멀어|물\s?건너|사그라|사라[지진]|잦아|불투명|신중론|속도\s?조절";
        private const string RecedeEn = @"\b(?:\bfad\w+|par(?:e|es|ing)|\btrim(?:s|med|ming)?\b|scal(?:e|es|ed|ing)\s+back|dial(?:s|ed|ing)?\s+back|push(?:es|ed|ing)?\s+back|\bdelay\w*|\bpostpon\w*|temper(?:s|ed|ing)?|\bdampen\w*|\breced\w+|wan(?:e|es|ing)|\bretreat\w*|\bdoubt\w*|\bwithdraw\w*|\babandon\w*)\b";
        /// <summary>
        /// 무엇이 물러나는지를 가리키는 말. ★ 물러나는 것은 '기대' 여야 한다 ★
        ///   'recession fears fade' 의 fade 는 침체 걱정에 붙은 말이지 금리에 붙은 말이 아니다.
        ///   그런데 처음에는 되돌림 낱말이 절 어디에 있기만 하면 인정했다. 그래서
        ///   'Fed cuts rates as recession fears fade' 가 실제 인하인데도 인하 기대 후퇴(+1)로,
        ///   'Fed raises rates despite fading growth' 가 실제 인상인데도 -1 로 뒤집혔다(실측).
        /// </summary>
        private const string ExpectWordEn = @"\b(?:bets?|odds|expectations?|hopes?|wagers?|forecasts?|outlook|projections?|pricing)\b";
        private const string ExpectWordKo = @"전망|기대|관측|가능성|베팅|우려|기대감";

        /// <summary>
        /// 이미 내려진 결정을 말하는 문장인가.
        ///
        /// ★ 결정은 기대를 이긴다 ★
        ///   한 문장에 결정과 기대 되돌림이 같이 있으면 결정이 사실이고 되돌림은 배경이다.
        ///   'Fed cuts rates by 25bp after a delayed decision' 이 그런 문장인데, 전에는
        ///   'delayed' 만 보고 인하를 정반대로 읽었다(실측).
        /// </summary>
        private static bool Decided(string s)
        {
            return Has(s, @"(?:인하|인상)\s*(?:를|은|는)?\s*(?:단행|결정|확정|발표|의결)|기준금리를?\s*[\d.]+\s*%")
                || Has(s, @"\b(?:cut|cuts|cutting|lower|lowers|lowered|raise|raises|raised|hike|hikes|hiked|trim|trims|trimmed)\b.{0,28}\b(?:by|to)\b.{0,14}(?:\d|a quarter|a half|basis point)");
        }

        /// <summary>인하·인상 기대가 물러나는 문장인가. 사건 자체가 '일어나지 않음' 이다.</summary>
        private static bool Receding(string s)
        {
            if (Decided(s)) return false;
            // 한국어: 되돌림 말이 인하·인상에 바로 붙거나, 기대를 가리키는 말에 붙어야 한다.
            bool ko = Has(s, @"(?:인하|인상|긴축)\s*(?:폭|기조|속도)?\s*(?:" + RecedeKo + @")")
                || Has(s, @"(?:" + ExpectWordKo + @").{0,6}(?:" + RecedeKo + @")")
                || Has(s, @"(?:" + ExpectWordKo + @").{0,4}완화")
                || Has(s, @"(?:" + RecedeKo + @").{0,8}(?:인하|인상)");
            // 영어: 어순이 반대라 위치로는 못 가리므로, 같은 절 안에 '기대' 를 가리키는 말이
            // 함께 있어야 인정한다. 그것이 없으면 되돌리는 대상이 금리 기대가 아니다.
            bool en = Has(s, RecedeEn) && Has(s, ExpectWordEn);
            return ko || en;
        }
        private const string Hold = @"동결|금리.{0,10}유지|\bhold(?:s|ing)?\b.{0,15}rates?|rates?.{0,15}(?:steady|unchanged)";
        /// <summary>
        /// ★ .NET 은 최근 15개 패턴만 기억한다 ★
        ///   이 규칙들은 패턴 문자열로 Regex.IsMatch 를 부르는데, 서로 다른 패턴이 80개가
        ///   넘는다(실측). 기본 캐시(15)를 넘으면 매 호출마다 다시 컴파일한다 - 기사 77건에
        ///   규칙 80개면 한 번 분석에 수천 번이다. 대기 8MB 를 목표로 하는 위젯이 그럴 이유가 없다.
        ///   캐시를 넉넉히 늘린다. 정적 생성자라 위젯·분석 실행파일·검사 어디서든 한 번 걸린다.
        /// </summary>
        static DollarFactors()
        {
            if (Regex.CacheSize < 256) Regex.CacheSize = 256;
        }

        private static bool Has(string s, string pattern) { return Regex.IsMatch(s, pattern, RegexOptions.IgnoreCase); }

        // ★ 숫자는 이 규칙들에게 뜻이 없고 거리만 벌린다 ★
        //   Cut/Hike 는 금리와 인하 사이를 열여덟 자까지만 본다. 그런데 결정 기사는
        //   금리 값을 둘 담는 일이 흔해서('기준금리 4.25~4.50%로 0.25%p 인하')
        //   그 창을 넘어 통째로 빠졌다 - 가장 확정적인 기사가 근거에서 사라졌다(실측).
        //   창을 그냥 넓히면 먼 곳의 '인하' 와 잘못 짝지어진다. 숫자 덩어리만 한 글자로
        //   줄여서 재고, 사용자에게 보이는 문장(Observation)은 원문 그대로 둔다.
        //   과거 연도 판정(20\d\d)은 숫자가 있어야 하므로 여기에 태우지 않는다.
        private static readonly Regex NumberRun = new Regex(@"\d[\d.,~\-–～\s]*%?\s*(?:%p|%포인트|포인트|bp|p)?");
        private static string Compact(string text) { return NumberRun.Replace(text, "#"); }
        private static bool HasRate(string s, string pattern) { return Has(Compact(s), pattern); }

        private static void Add(List<DollarFactor> found, DollarNews n, string text, string category,
            string rule, int direction, string mechanism, string counter, double weight, bool eventIsAbsence = false)
        {
            if (found.Any(f => f.Rule == rule && f.Direction == direction)) return;
            // ★ 누가 한 말인지는 절이 아니라 문장의 성질이다 ★
            //   '도이체방크 "금융시장, 연준 금리 인상폭 과소평가"' 는 쉼표에서 갈리면서
            //   '도이체' 가 앞 절에 남고, 뒷 절만 보면 그냥 연준 인상 기사가 된다.
            //   그래서 은행 의견이 결정과 같은 만점(0.8) 근거로 들어갔다(실측).
            //   조건부 여부만은 제목 전체를 보고 판단한다 - 인용 표시는 문장 앞에 붙는다.
            string whole = (n.Title ?? "") + " " + text;
            bool conditional = Has(whole, @"전망|예상|가능|우려|기대|시사|확률|관측|베팅|촉구|요구|계획|언급|발언|강조|위협|주장|해야|시사|조건|인상설|인하설|긴장|견뎌|경고|초읽기|되풀이|도이체|씨티|블랙록|골드만|전문가|\?|\b(?:may|might|could|\bexpect\w*|\bforecast\w*|UBS|analysts?|outlook|odds|bets|calls? for|plans?|if|will|says?|said|urges?|threatens?|signals?|warns?|warned|warning|sees?|projects?|likely|risks?)\b");
            // Denied/hypothetical facts must never become asserted directional events.
            // '안' 뒤 활용형을 모두 받는다. 전에는 '안 하' 만 잡아 '인하 안 한다' 가
            // 실제 인하 결정과 같은 만점(0.8) 근거로 들어갔다(실측).
            // 앞에 한글이 붙은 '제안 했다' 같은 말은 부정이 아니므로 뒤돌아보기로 걸러낸다.
            bool negated = !eventIsAbsence && Has(text, @"(?<![가-힣])안\s*(?:올|내|하|한|했|할|해)|부인|않|아니|무산|없[다어을]|불가(?!피)|" +
                @"\b(?:not|no|never|won't|isn't|aren't|doesn't|didn't|unlikely|denies|denied|rejects|rejected)\b|rules? out|ruled out");
            bool historical = Has(text, @"지난해|작년|last year") || Regex.Matches(text, @"\b(20\d{2})년?").Cast<Match>()
                .Any(m => n.PublishedUtc.Year > 2000 && int.Parse(m.Groups[1].Value) < n.PublishedUtc.Year);
            historical = historical && !Has(text, @"되풀이|재연|repeat");
            bool distant = Has(text, @"내년|내후년|next year");
            found.Add(new DollarFactor { News = n, Category = category, Rule = rule,
                Observation = DollarAnalysis.Clean(text, 160), Direction = negated || historical ? 0 : direction,
                Weight = negated || historical ? 0 : weight * (distant ? 0.25 : 1) * (conditional ? 0.5 : 1) * (Has(text, @"미확인|소문|루머|rumou?r|unconfirmed|reportedly") ? 0.4 : 1),
                Mechanism = (historical ? "과거 사건 설명입니다. 현재 결정으로 합산하지 않습니다. " : negated ? "부정된 내용이므로 발생한 사건으로 합산하지 않습니다. " : conditional ? "전망이 실현된다면 " : "이 요인만 보면 ") + mechanism,
                Counter = counter });
        }

        // ★ 절 나누기 — 두 가지를 지킨다.
        //   ① 소수점(3.50%)과 천단위 쉼표(1,340원)는 절 경계가 아니다.
        //      전에는 '연준 기준금리 3.50%로 인하' 가 '연준 기준금리 3' / '50%로 인하' 로
        //      갈려 근거 0건이었다. 결정 보도는 거의 항상 소수점을 담으므로,
        //      가장 확정적인 기사만 골라서 빠지고 전망 기사만 점수에 남았다(실측).
        //   ② '연준,' 처럼 5자 미만 주어 절은 버리지 말고 다음 절에 붙인다.
        //      버리면 한국 통신사 헤드라인 표준형('연준, 기준금리 0.25%p 인하')에서
        //      행위자가 사라져 Policy() 가 아예 불리지 않는다.
        //   두 배우를 가르는 원래 의도는 그대로다 - '연준 금리 인하, 한국은행 금리 인상' 은
        //   두 절 다 5자 이상이라 종전처럼 둘로 나뉜다.
        private static List<string> Clauses(string input)
        {
            var parts = new List<string>();
            string carry = "";
            foreach (string raw in Regex.Split(input,
                @"(?<!\d)\.(?!\d)|[!;。\r\n]|(?<!\d),(?!\d)|，|(?:반면|그러나|하지만|\bwhile\b|\bwhereas\b|\bbut\b)"))
            {
                string text = (carry + " " + raw).Trim();
                carry = "";
                if (text.Length < 5) { carry = text; continue; }
                parts.Add(text);
            }
            return parts;
        }

        /// <summary>문장 하나에서 규칙을 훑는다. 절 단위로도, 통째로도 부를 수 있다.</summary>
        private static void Scan(List<DollarFactor> result, DollarNews n, string text)
        {
            bool fed = IsFed(text), bok = Has(text, Bok);
            if (fed && bok)
            {
                int f = Regex.Match(text, Fed, RegexOptions.IgnoreCase).Index;
                int b = Regex.Match(text, Bok, RegexOptions.IgnoreCase).Index;
                // A clause with two explicit actors is processed as two actor-local spans.
                Policy(result, n, text.Substring(Math.Min(f, b), Math.Abs(f - b)), f < b);
                Policy(result, n, text.Substring(Math.Max(f, b)), f > b);
            }
            else if (fed || bok) Policy(result, n, text, fed);

            if (Has(text, @"미국|\bUS\b|U\.S|American") && Has(text, @"물가|인플레|소비자물가|inflation|\bCPI\b|consumer prices?|price growth"))
            {
                // '오름세 주춤' 처럼 오르는 말과 느려지는 말이 붙으면 그 문장은 둔화 쪽이다.
                // 이 판정을 여기 두지 않으면 가열 규칙만 막히고 아무 근거도 남지 않는다.
                if (Slowing(text) || Has(text, @"둔화|하락|낮아|냉각|\bcool\w*|\beas(?:e|es|ed|ing)\b|\bslow\w*|below|\bfall(?:s|en|ing)?\b"))
                    Add(result, n, text, Categories[0], "us-inflation-soft", -1, "물가 둔화 → 연준 완화 여지 → 달러 금리 매력 약화", "이미 예상됐거나 위험 회피가 커지면 달러 하락 효과가 약해질 수 있습니다.", 0.65);
                // '상승률 둔화' 는 오르는 이야기가 아니라 느려지는 이야기다.
                // ★ 'risks' 는 'rises' 가 아니다 ★
                //   ris\w* 가 'inflation risks easing'(물가 위험 완화)의 'risks' 를 잡아,
                //   같은 문장이 물가 가열과 물가 둔화로 동시에 채점됐다. 점수에서는 상쇄되지만
                //   근거 목록에는 정반대 두 줄이 같이 뜨고 기사 한 건이 방향 기사로 두 번 세어진다.
                if (!Slowing(text) && Has(text, @"급등|상승|가속|웃돌|높아|\bhot(?:ter|test)?\b|\bincreas(?:e|es|ed|ing)\b|\bclimb(?:s|ed|ing)?\b|\baccelerat\w*|above|\bsurge\w*|\bris(?:e|es|en|ing)\b"))
                    Add(result, n, text, Categories[0], "us-inflation-hot", 1, "물가 압력 → 연준 완화 지연 가능성 → 달러 지지", "물가 상승이 미국 경기 불안으로 번지거나 이미 반영됐다면 다른 반응이 가능합니다.", 0.65);
            }
            if (Has(text, @"미국|\bUS\b|U\.S|American") && Has(text, @"고용|일자리|실업|payroll|jobs|employment"))
            {
                bool weak = Has(text, @"고용.{0,10}(?:둔화|냉각|감소)|일자리.{0,10}감소|실업률.{0,10}상승|\bweak\w*|\bslow\w*|below|\bmiss(?:es|ed|ing)?\b");
                bool strong = Has(text, @"고용.{0,10}(?:호조|증가)|실업률.{0,10}하락|\bstrong\w*|above|beat\w*");
                if (weak != strong) Add(result, n, text, Categories[0], "us-labor", weak ? -1 : 1,
                    weak ? "고용 약화 → 연준 인하 기대 → 달러 하락 압력" : "고용 호조 → 금리 인하 필요성 감소 → 달러 지지",
                    "침체 우려가 위험 회피를 자극하면 약한 고용에도 달러가 강해질 수 있습니다.", 0.6);
            }
            if (Has(text, @"트럼프|Trump") && Has(text, @"관세|tariff") && !Has(text, @"한국|한미|Korea"))
            {
                // impos 는 impossible 안에, rais 는 praise 안에 걸린다. 낱말로 못 박는다.
                // '발효·시행' 은 관세가 실제로 걸리는 순간인데 목록에 없어 방향이 없었다.
                bool increase = Has(text, @"부과|인상|위협|발효|시행|확대|\bimpos(?:e|es|ed|ing|ition)\b|\brais(?:e|es|ed|ing)\b|\bhik(?:e|es|ed|ing)\b|\bthreat(?:s|en|ens|ened|ening)?\b|\btakes? effect\b");
                bool decrease = Has(text, @"철회|철폐|인하|유예|완화|면제|\bremov(?:e|es|ed|ing|al)\b|\blower(?:s|ed|ing)?\b|\bsuspend(?:s|ed|ing)?\b|\bpause[sd]?\b|\bexempt(?:s|ed|ion|ions)?\b");
                if (increase != decrease) Add(result, n, text, Categories[3], "trump-tariffs", increase ? 1 : -1,
                    increase ? "트럼프의 관세 강화 → 무역 불확실성·물가 압력 → 원화 부담·달러 지지 가능성" : "관세 완화 → 무역 불확실성 감소 → 위험통화·원화 지지 가능성",
                    "미국 성장·정책 신뢰가 훼손되면 달러 자체가 약해질 수 있습니다. 발표·협상과 실제 시행은 구분해야 합니다.", 0.5);
            }
            if (Has(text, @"트럼프|Trump") && HasRate(text, Cut) && !fed)
                Add(result, n, text, Categories[0], "trump-rate-pressure", -1,
                    "금리 인하를 향한 정치적 압력 → 완화 기대를 자극할 수 있음",
                    "대통령의 요구는 연준 결정이 아닙니다. 독립성 논란·물가 위험이 반대 방향으로 작용할 수 있습니다.", 0.25);
            bool korea = Has(text, @"한국|한미|원화|한은|하이닉스|삼성|Korea|Korean|Hynix|Samsung|\bBOK\b");
            if (korea && Has(text, @"수출|경상수지|무역수지|exports?|current account|trade surplus"))
            {
                bool good = Has(text, @"증가|개선|흑자|호조|surplus|\bris(?:e|es|en|ing)\b|\bgrow\w*|\bimprov\w*|\bsurge\w*");
                bool bad = Has(text, @"감소|악화|적자|부진|deficit|\bfall(?:s|en|ing)?\b|\bdrop(?:s|ped|ping)?\b|\bdeclin\w*");
                // '수출 증가율 둔화' 는 늘긴 늘었다. 좋다고도 나쁘다고도 할 수 없으니
                // 아무 말도 하지 않는다 - 틀린 방향을 말하는 것보다 낫다.
                if (Slowing(text)) good = bad = false;
                if (good != bad) Add(result, n, text, Categories[1], "korea-flows", good ? -1 : 1,
                    good ? "수출·외화 유입 개선 → 원화 수요 지지 → 원/달러 하락 압력" : "외화 수급 악화 → 원화 부담 → 원/달러 상승 압력",
                    "해외 투자·수입 결제의 달러 수요가 수출 유입을 상쇄할 수 있습니다.", 0.65);
            }
            bool bilateral = Has(text, @"한미|한·미|한-미|Korea.{0,25}(?:US|U\.S|United States)|(?:US|U\.S|United States).{0,25}Korea") ||
                (korea && Has(text, @"미국|미 대통령|트럼프|Trump|Washington"));
            if (bilateral && Has(text, @"관세|무역|협상|투자|동맹|tariff|trade|deal|investment|alliance"))
            {
                // ★ 'disagreement' 안에 'agreement' 가 들어 있다 ★
                //   경계를 안 두어서 '무역 갈등 심화' 기사가 '합의' 로 읽혔다.
                bool good = Has(text, @"관세.{0,12}(?:인하|철폐|완화)|합의|타결|갈등.{0,8}완화|deal reached|\bagreements?\b|(?:cut|lower|remov)\w*.{0,12}tariff");
                bool bad = Has(text, @"관세.{0,12}(?:인상|부과)|결렬|갈등.{0,8}격화|위협|(?:rais|hik|impos)\w*.{0,12}tariff|talks.{0,8}fail|threat|" +
                    @"\b(?:disagreement|dispute|frictions?|standoff|\bretaliat\w*)\b");
                if (good != bad) Add(result, n, text, Categories[2], "bilateral-trade", good ? -1 : 1,
                    good ? "한미 통상 마찰 완화 → 한국 수출 불확실성 감소 → 원화 지지" : "한국 대상 통상 압박 → 수출·위험 선호 부담 → 원/달러 상승 압력",
                    "합의에 대규모 대미 투자·달러 지급이 포함되면 원화 지지 효과와 상쇄될 수 있습니다.", 0.65);
                if (Has(text, @"대미.{0,12}투자|미국.{0,12}투자|\binvest\w*.{0,20}(?:US|United States)"))
                    Add(result, n, text, Categories[2], "outward-investment", 1, "대미 투자 집행 시 달러 수요 증가 → 단기 원화 부담",
                        "투자 발표와 실제 집행 시점은 다르며 환헤지·외화 조달로 현물환 영향이 줄 수 있습니다.", 0.35);
            }
            if (korea && Has(text, @"미국|\bUS\b|United States") && Has(text, @"진출|공장|생산시설|설비|증설|plant|factory|facility") &&
                Has(text, @"투자|건설|신설|증설|진출|invest|build|expand|open"))
                Add(result, n, text, Categories[4], "company-us-expansion", 1,
                    "한국 기업의 미국 설비 투자 → 집행 시 달러 결제 수요 → 단기 원화 부담",
                    "해외 차입·현지 수익으로 조달하면 국내 달러 수요는 줄고 장기 매출·수출 효과는 원화에 도움이 될 수 있습니다.", 0.4);
            if (Has(text, @"엔\s*캐리|엔케리|yen carry|carry trade") && Has(text, @"청산|축소|되감|unwind|liquidat|revers|reduc"))
                Add(result, n, text, Categories[5], "yen-carry-unwind", 1,
                    "엔캐리 청산 → 차입 엔화 상환·위험자산 매도 → 원화 등 위험통화 약세 압력",
                    "엔화 강세는 달러/엔을 낮출 수 있어 달러 전반의 강세와는 다릅니다. 청산 규모·한국 자산 노출에 따라 원/달러 영향이 달라집니다.", 0.75);
            if (Has(text, @"일본은행|일은|\bBOJ\b|Bank of Japan") && Has(text, Hike))
                Add(result, n, text, Categories[5], "boj-hike", 1,
                    "일본 금리 인상 → 엔화 조달 비용 상승 → 엔캐리 축소 시 위험자산·원화 부담",
                    "금리 인상만으로 실제 청산이 발생했다고 볼 수 없습니다. 엔화 강세와 달러 약세가 반대 방향으로 작용할 수도 있습니다.", 0.3);
            if (korea && Has(text, @"정부|정책|재정|국회|정국|government|fiscal|policy|politic"))
            {
                if (Has(text, @"불확실|혼선|번복|탄핵|정국.{0,10}불안|신용등급.{0,10}강등|uncertain|reversal|instability|downgrade"))
                    Add(result, n, text, Categories[1], "korea-policy-risk", 1,
                        "국내 정책 불확실성·신용 위험 → 자금 유입 둔화·위험 프리미엄 상승 → 원화 부담",
                        "실제 외국인 자금 유출이 없거나 정책이 빠르게 안정되면 환율 영향은 제한될 수 있습니다.", 0.6);
                if (Has(text, @"재정.{0,10}(?:지출|확대)|추경|재정적자|fiscal stimulus|budget deficit"))
                    Add(result, n, text, Categories[1], "korea-fiscal", 0,
                        "재정 확대는 성장·수입 수요를 함께 자극합니다. 성장 개선의 원화 지지와 수입·부채 부담을 함께 비교합니다.",
                        "지출 규모·재원·실제 집행과 국채금리 반응이 없으면 일방적인 환율 방향을 정하기 어렵습니다.", 0);
            }
            // 무역/환율/반도체 '전쟁' 은 무력 분쟁이 아니다. 미국이 당사자인 통상 마찰은
            // 안전자산 수요 사건이 아니며(관세 국면에서 달러는 오히려 약했다) 관세는
            // trump-tariffs 가 따로 본다. 'war' 를 부분 문자열로 두면 Warsh·warns·toward·
            // software 가, '이란' 을 그냥 두면 '~것이란' 이 지정학으로 잡힌다(실측).
            if (Has(text, @"중동|(?<!(?:무역|환율|반도체|가격|관세|정보|문화|법정|자존심)\s?)전쟁|(?<![가-힣])이란|우크라이나|지정학|" +
                @"(?<!(?:trade|price|currency|tariff|chip|bidding|culture|talent) )\bwars?\b|\bIran\b|Ukraine|Middle East|geopolit"))
            {
                bool easing = Has(text, @"휴전|종전|공격.{0,8}(?:금지|중단|자제)|평화.{0,8}합의|긴장.{0,8}완화|ceasefire|peace deal|de-escalat");
                // '공격적'(aggressive)은 무력 공격이 아니다.
                bool risk = !Has(text, @"공격.{0,8}(?:금지|중단|자제)") && Has(text, @"확전|공격(?!적)|격화|침공|긴장.{0,8}고조|escalat|attack|invasion|strike");
                if (easing != risk) Add(result, n, text, Categories[3], "geopolitics", risk ? 1 : -1,
                    risk ? "지정학 위험 확대 → 안전자산 달러 수요 → 원/달러 상승 압력" : "긴장 완화 → 위험 회피 감소 → 원화 회복 여지",
                    "미국이 위험의 중심이거나 시장에 이미 반영됐다면 달러 반응이 달라질 수 있습니다.", 0.65);
            }
            if (Has(text, @"국제유가|유가|원유|oil prices?|crude|Brent"))
            {
                bool rise = Has(text, @"급등|상승|치솟|surge|\bris(?:e|es|en|ing)\b|jump|climb");
                bool fall = Has(text, @"급락|하락|떨어|fall|drop|slid|plung");
                if (rise != fall) Add(result, n, text, Categories[3], "oil", rise ? 1 : -1,
                    rise ? "유가 상승 → 한국 에너지 수입 대금·물가 부담 → 원화 약세 압력" : "유가 하락 → 한국 수입 비용 완화 → 원화 지지",
                    "유가 하락이 세계 경기 침체 때문이면 위험 회피가 원화에 불리하게 작용할 수 있습니다.", 0.55);
            }
        }

        public static List<DollarFactor> Analyze(DollarNews n)
        {
            if (n.Target != null && !n.Target.Dollar) return PredictionFactors.Analyze(n);
            // 열쇠를 만들지 않고 그때 쓴 문자열과 같은 개체인지만 본다 - 본문이 바뀌면 새 개체가 온다.
            if (n.FactorTargetKey == null && (object)n.FactorTitle == (object)n.Title && (object)n.FactorContext == (object)n.Context) return n.Factors;
            var result = new List<DollarFactor>();
            string input = Regex.Replace(n.Title + ". " + (n.Context ?? ""), @"U\.S\.?", "US", RegexOptions.IgnoreCase);
            // Splitting at clauses prevents a Korean policy action being assigned to the Fed, and vice versa.
            // ★ 절로 나누다 행위자를 잃으면 아무 근거도 남지 않는다 ★
            //   '한미 정상, 3500억달러 대미 투자 합의' 는 '한미 정상' 이 앞에서 떨어져
            //   나가고, 'UBS "연준, 두 차례 금리 인상 전망"' 은 따옴표 안의 쉼표 때문에
            //   행위자와 서술이 갈린다. 둘 다 근거 0건이 됐다(실측).
            //   나눠서 하나도 못 찾았을 때만 문장 전체를 다시 한 번 본다. 근거가
            //   하나라도 나온 문장은 건드리지 않으므로, 두 배우를 가르는 원래 의도
            //   ('연준 금리 인하, 한국은행 금리 인상')는 그대로다.
            foreach (string text in Clauses(input)) Scan(result, n, text);
            if (result.Count == 0) Scan(result, n, input);
            n.FactorTitle = n.Title; n.FactorContext = n.Context; n.FactorTargetKey = null; n.Factors = result;
            return result;
        }

        private static void Policy(List<DollarFactor> result, DollarNews n, string text, bool fed)
        {
            bool cut = HasRate(text, Cut), hike = HasRate(text, Hike);
            if (fed && Has(text, @"트럼프|Trump") && Has(text, @"요구|촉구|압박|해야|urge|call|demand|should"))
            {
                Add(result, n, text, Categories[0], "trump-rate-pressure", cut ? -1 : 0,
                    "트럼프의 인하 요구 → 완화 기대를 자극할 수 있으나 연준 결정은 아님",
                    "연준의 실제 표결과 물가 판단이 다르면 이 기대는 되돌려질 수 있습니다.", 0.25);
                return;
            }
            string category = fed ? Categories[0] : Categories[1];
            if (cut != hike && Receding(text))
            {
                int reversal = fed ? (cut ? 1 : -1) : (cut ? -1 : 1);
                // ★ 여기서는 부정어가 곧 사건이다 ★
                //   '인하 무산' 의 '무산' 은 negated 목록에도 있어서, 되돌림으로 잡아 놓고
                //   곧바로 방향 0 으로 지워 버렸다. 이 규칙만은 부정어를 세지 않는다.
                Add(result, n, text, category, "policy-expectation-withdrawn", reversal,
                    cut ? "인하 기대 후퇴 → 완화 기대 약화 → 기존 인하 기대와 반대 압력" : "인상 기대 후퇴 → 긴축 기대 약화 → 기존 인상 기대와 반대 압력",
                    "전망 변경은 실제 금리 변경이 아닙니다. 시장이 먼저 반영했는지 확인해야 합니다.", 0.4, true);
                return;
            }
            if (cut != hike)
            {
                int direction = fed ? (cut ? -1 : 1) : (cut ? 1 : -1);
                Add(result, n, text, category, fed ? "fed-policy" : "bok-policy", direction,
                    fed ? (cut ? "연준 인하 → 달러 금리 매력 감소 → 원/달러 하락 압력" : "연준 인상 → 달러 금리 매력 증가 → 원/달러 상승 압력") :
                          (cut ? "한은 인하 → 원화 금리 매력 감소 → 원/달러 상승 압력" : "한은 인상 → 원화 금리 매력 증가 → 원/달러 하락 압력"),
                    "상대국 정책·인하 이유·시장 선반영에 따라 효과가 달라집니다. 금리차만으로 환율을 확정할 수 없습니다.", 0.8);
            }
            else if (HasRate(text, Hold)) Add(result, n, text, category, fed ? "fed-hold" : "bok-hold", 0,
                "금리 동결은 기존 조건을 유지합니다. 상대국의 변화·정책 전망과 조합해야 방향을 알 수 있습니다.",
                "예상된 동결인지 예상 밖 동결인지에 따라 시장 반응이 달라집니다.", 0);
            else if (cut && hike) Add(result, n, text, category, "mixed-policy", 0,
                "인상·인하 시나리오가 함께 언급돼 상대적인 가능성 비교가 필요합니다.", "하나의 정책이 확정된 것으로 계산하지 않습니다.", 0);
        }

        public static List<DollarFactor> Collect(DollarAnalysisResult result, DateTime now)
        {
            return result.News.Where(n => DollarAnalysis.ForTarget(n, result.Target) && n.PublishedUtc <= now && n.PublishedUtc >= now.AddHours(-24) && DollarAnalysis.IsNewsLink(n.Url))
                .GroupBy(n => Regex.Replace(n.Title.Split(new[] { " - " }, StringSplitOptions.None)[0], @"[^\p{L}\p{N}]", "").ToLowerInvariant())
                .SelectMany(g => Analyze(g.First())).ToList();
        }

        public static string Brief(DollarAnalysisResult result, DateTime now)
        {
            var factors = Collect(result, now);
            var directional = factors.Where(f => f.Direction != 0).OrderByDescending(f => f.Weight * DollarAnalysis.SourceWeight(f.News.Source)).ToList();
            var score = DollarAnalysis.Score(result, 1, now);
            string outlook = DollarAnalysisWindow.ScenarioOutlook(result, 1, now);
            var pieces = new List<string> { "단기 비교: " + outlook + " · " +
                (outlook == "보합" && DollarAnalysisWindow.Pressure(score).Length > 0 ? DollarAnalysisWindow.Pressure(score) + " · " : "") + DollarAnalysisWindow.MainReason(score) };
            var strongest = directional.FirstOrDefault();
            if (strongest != null)
            {
                pieces.Add("개별 기사 요인: " + strongest.Mechanism.Replace("이 요인만 보면 ", ""));
                var next = directional.FirstOrDefault(f => f.Category != strongest.Category);
                if (next != null) pieces.Add((next.Direction == strongest.Direction ? "같은 방향: " : "상쇄 요인: ") + next.Mechanism.Replace("이 요인만 보면 ", ""));
                pieces.Add("반대 해석: " + strongest.Counter);
            }
            else if (factors.Count > 0) pieces.Add(factors[0].Mechanism.Replace("이 요인만 보면 ", ""));
            else pieces.Add("새 정책·자금 이동의 방향은 확인 중입니다. 과거 흐름을 참고하되 다음 발표가 전망을 바꿀 수 있습니다.");
            var p = result.Pattern;
            if (DollarAnalysis.Fresh(p, now)) pieces.Add("과거 참고: 유사 가격 흐름에서 상승 " + p.UpPercent.ToString("0") + "%·하락 " + p.DownPercent.ToString("0") + "%.");
            return string.Join("\n", pieces);
        }

        public static string Narrative(DollarAnalysisResult result, DateTime now)
        {
            var factors = Collect(result, now);
            if (!result.Target.Dollar) return string.Join("\n", factors.Select(f => f.Mechanism + "\n반대 해석: " + f.Counter).Distinct());
            var parts = new List<string>();
            foreach (string category in Categories)
            {
                var found = factors.Where(f => f.Category == category).OrderByDescending(f => f.Weight * DollarAnalysis.SourceWeight(f.News.Source)).ToList();
                if (found.Count == 0) continue;
                var primary = found[0];
                string effect = primary.Mechanism.Replace("이 요인만 보면 ", "");
                bool conflicting = found.Any(f => f.Direction > 0) && found.Any(f => f.Direction < 0);
                parts.Add(category.Split('·')[0] + ": " + (conflicting ? "서로 반대되는 요인이 함께 작용합니다. " : "") + effect);
            }
            var fed = factors.FirstOrDefault(f => f.Rule == "fed-policy" && f.Direction != 0);
            var bok = factors.FirstOrDefault(f => f.Rule == "bok-policy" && f.Direction != 0);
            if (fed != null && bok != null)
                parts.Add(fed.Direction == bok.Direction ? "조합: 두 나라의 금리 요인이 같은 환율 방향에 힘을 보탭니다." : "조합: 연준·한은의 금리 요인이 서로 상쇄합니다. 인하 폭·속도 차이가 관건입니다.");
            bool stress = factors.Any(f => f.Direction > 0 && (f.Rule == "geopolitics" || f.Rule == "yen-carry-unwind"));
            if (fed != null && fed.Direction < 0 && stress)
                parts.Add("조합: 연준 완화의 달러 약세 요인을 전쟁·청산에 따른 달러 수요가 상쇄할 수 있습니다.");
            var expansion = factors.FirstOrDefault(f => f.Rule == "company-us-expansion");
            if (expansion != null && factors.Any(f => f.Rule == "korea-flows" && f.Direction < 0))
                parts.Add("조합: 수출로 들어오는 달러와 해외 투자로 나가는 달러의 실제 집행 시점을 비교해야 합니다.");
            var p = result.Pattern;
            if (DollarAnalysis.Fresh(p, now))
                parts.Add("과거 참고: 비슷한 가격 흐름 " + p.Count + "회 중 상승 " + p.UpPercent.ToString("0") + "%·하락 " + p.DownPercent.ToString("0") + "%. 같은 정치·경제 사건을 비교한 통계는 아닙니다.");
            if (factors.Count > 0)
                parts.Add("반대 해석: " + factors.OrderByDescending(f => f.Weight * DollarAnalysis.SourceWeight(f.News.Source)).First().Counter);
            if (parts.Count == 0) parts.Add("현재 수집 자료에서 정책 결정·수급 변화가 확인되지 않았습니다. 연준·한은의 다음 결정과 한미 통상·국제 정세 변화를 확인해야 합니다.");
            return string.Join("\n", parts);
        }
    }
}
