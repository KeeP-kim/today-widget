using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeskWidget
{
    internal static class PredictionFactors
    {
        private static bool Has(string text, string pattern) { return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase); }
        private const string Fed = @"연준|FOMC|Federal Reserve|\bFed\b";
        private const string Bok = @"한국은행|한은|Bank of Korea";
        private const string Hike = @"금리.{0,12}(?:인상|올[리린렸])|(?:hike|raise)[a-z]*.{0,15}rates?|rate hike";
        private const string Cut = @"금리.{0,12}(?:인하|내[리린렸])|(?:cut|lower)[a-z]*.{0,15}rates?|rate cut";
        // DollarFactors 의 negated 와 같은 뜻이어야 한다. '안 하' 만 잡던 것을 활용형까지 넓혔다.
        private const string Negation = @"(?<![가-힣])안\s*(?:올|내|하|한|했|할|해)|않|아니|부인|철회|취소|무산|없[다어을]|불가(?!피)|" +
            @"\b(?:not|no|never|won't|isn't|aren't|doesn't|didn't|unlikely)\b|denied|denies|cancel|rules? out|ruled out";
        internal static bool Relevant(string title, PredictionTarget target)
        {
            return Subject(title, target) || Has(title, Fed + "|" + Bok + @"|금리|관세|중동|전쟁|inflation|tariff|interest rate|stock market|crypto");
        }
        internal static bool Subject(string text, PredictionTarget target)
        {
            string name = target.Name;
            if (target.Def.Kind == SourceKind.Fx) name = name.Replace(" 100", "");
            bool named = name.Length > 1 ? text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 : Has(text, Regex.Escape(name) + @"\s+(?:주가|주식|실적)");
            return named || text.IndexOf(target.Def.Code, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (target.Def.Kind == SourceKind.WorldStock && PredictionTarget.Ticker(target.Def.Code).Length > 0 &&
                    Has(text, @"\b" + Regex.Escape(PredictionTarget.Ticker(target.Def.Code)) + @"\b")) ||
                (target.Def.Kind == SourceKind.Coin && Has(text, @"\b" + Regex.Escape(target.Def.Code.Replace("KRW-", "")) + @"\b"));
        }
        private static void Add(List<DollarFactor> factors, DollarNews n, string text, string rule, int direction, string mechanism, string counter, double weight)
        {
            // 저장하는 Rule 에는 품목 키가 앞에 붙는다. 그 접두사 없이 비교하면 이 검사가
            // 영원히 거짓이라 같은 규칙이 절마다 거듭 쌓였다 - 한 기사가 같은 근거를
            // 여러 번 낸 만큼 점수가 부풀었다.
            string key = n.Target.Key + ":" + rule;
            if (factors.Any(f => f.Rule == key && f.Direction == direction)) return;
            bool negative = Has(text, Negation), past = Has(text, @"지난해|작년|과거|last year|in 20(?:0|1)\d");
            // 확정 사건과 전망을 가른다. '우려·확률·관측·경고' 가 빠져 있어 '인상 우려' 가
            // 실제 인상과 같은 만점으로 들어갔다(실측). DollarFactors 의 목록에 맞춘다.
            bool conditional = Has(text, @"전망|예상|시사|요구|촉구|가능|우려|기대|확률|관측|베팅|경고|주장|위협|언급|발언|계획|\?|" +
                @"\b(?:may|might|could|\bexpect\w*|\bforecast\w*|likely|odds|bets|outlook|plans?|signals?|warns?|says?|said|urges?|threatens?|call for)\b");
            factors.Add(new DollarFactor { News = n, Category = n.Target.Name + " 영향", Observation = DollarAnalysis.Clean(text, 160), Rule = key,
                Direction = negative || past ? 0 : direction, Weight = negative || past ? 0 : weight * (conditional ? 0.5 : 1),
                Mechanism = (negative || past ? "부정·과거 맥락: 현재 발생으로 합산하지 않음. " : conditional ? "해당 전망이 실현된다면 " : "") + mechanism,
                Counter = counter });
        }
        internal static List<DollarFactor> Analyze(DollarNews n)
        {
            var target = n.Target;
            if (target == null || target.Dollar) return new List<DollarFactor>();
            // 같은 기사라도 품목이 바뀌면 요인이 달라진다. 품목 열쇠는 짧으므로 그대로 비교한다.
            if (n.FactorTargetKey == target.Key && (object)n.FactorTitle == (object)n.Title && (object)n.FactorContext == (object)n.Context) return n.Factors;
            var factors = new List<DollarFactor>();
            bool riskAsset = target.Def.Kind == SourceKind.DomesticStock || target.Def.Kind == SourceKind.WorldStock || target.Def.Kind == SourceKind.Index || target.Def.Kind == SourceKind.Coin;
            bool coin = target.Def.Kind == SourceKind.Coin;
            // 소수점은 절 경계가 아니다(DollarFactors.Clauses 와 같은 이유).
            foreach (string text in Regex.Split(n.Title + ". " + n.Context, @"(?<!\d)\.(?!\d)|[!;。\r\n]|(?:반면|그러나|하지만|\bwhile\b|\bbut\b)"))
            {
                bool subject = Subject(text, target), hike = Has(text, Hike), cut = Has(text, Cut);
                if (target.Policy)
                {
                    bool own = target.Def.Code == "INTL:US" ? Has(text, Fed) : target.Def.Code == "INTL:KR" ? Has(text, Bok) : subject;
                    // 두 중앙은행이 한 절에 있으면 누구의 결정인지 규칙으로 단정하지 않는다.
                    if (own && !(Has(text, Fed) && Has(text, Bok)) && hike != cut)
                        Add(factors, n, text, "policy-rate", hike ? 1 : -1, "해당 중앙은행의 " + (hike ? "긴축" : "완화") + " → " + target.Name + (hike ? " 상승" : " 하락") + " 압력", "발언과 실제 의결은 다르며 물가·고용 또는 회의 결과에 따라 동결될 수 있습니다.", 0.9);
                    continue;
                }
                if (riskAsset && Has(text, Fed) && hike != cut)
                    Add(factors, n, text, "discount-rate", hike ? -1 : 1, "미국 금리 " + (hike ? "상승 → 할인율·자금 비용 증가 → " + target.Name + " 가격 압박" : "하락 → 유동성·할인율 완화 → " + target.Name + " 가격 지지"), "금리 인하가 경기 침체 신호이거나 이미 반영됐다면 반대 반응이 가능합니다. 기업·코인 고유 수급도 확인해야 합니다.", 0.55);
                // 'war' 를 부분 문자열로 두면 warns·toward·software·hardware 가 무력 분쟁이 되고,
                // 무역전쟁·trade war 까지 위험 회피로 잡힌다(실측: 저장된 DOGE·BTC 기록의
                // 하락 근거가 실제로 이것이었다).
                if (riskAsset && Has(text, @"(?<!(?:무역|환율|반도체|가격|관세|정보|문화|법정|자존심)\s?)전쟁|공습|무력 공격|" +
                    @"(?<!(?:trade|price|currency|tariff|chip|bidding|culture|talent) )\bwars?\b|\bairstrikes?\b") && !Has(text, @"휴전|종전|ceasefire"))
                    Add(factors, n, text, "risk-off", -1, "분쟁 확대 → 위험 회피·비용 불안 → " + target.Name + " 매도 압력", "방산·에너지 수혜 업종이거나 충격이 이미 반영된 경우 반대 방향일 수 있습니다.", 0.35);
                // ── 코인 고유 요인 ────────────────────────────────────────────
                //   전에는 코인 규칙이 '연준 할인율' 과 '전쟁' 둘뿐이었다. 그래서 ETF
                //   자금유입·규제·해킹처럼 코인 값을 실제로 움직이는 기사가 전부 0점이었고,
                //   저장된 BTC·DOGE 예측의 방향이 코인과 무관한 근거에서 나왔다(실측).
                //   아래는 전달 경로가 분명한 것만 담는다. 확실치 않은 것은 가중치를 낮추고
                //   반대 해석을 함께 적는다 - 코인에서 '선반영' 은 특히 심하다.
                if (coin)
                {
                    bool spot = Has(text, @"현물\s?ETF|\bspot ETF\b|비트코인\s?ETF|이더리움\s?ETF");
                    if (spot || Has(text, @"\bETF\b"))
                    {
                        bool inflow = Has(text, @"순유입|자금\s?유입|매수세\s?유입|유입.{0,6}(?:규모|기록|확대)|\binflows?\b|net buying");
                        bool outflow = Has(text, @"순유출|자금\s?유출|환매|유출.{0,6}(?:규모|기록|확대)|\boutflows?\b|net selling|redemption");
                        if (inflow != outflow)
                            Add(factors, n, text, "etf-flow", inflow ? 1 : -1,
                                "ETF 를 통한 자금 " + (inflow ? "유입 → 현물 매수 수요" : "유출 → 현물 매도") + " → " + target.Name + (inflow ? " 상승" : " 하락") + " 압력",
                                "하루 흐름은 되돌려지기 쉽고 이미 가격에 반영됐을 수 있습니다. 다른 코인의 ETF 자금은 이 품목과 다를 수 있습니다.", 0.6);
                        bool approve = Has(text, @"승인|인가|상장\s?허가|\bapproves?\b|\bapproval\b");
                        bool reject = Has(text, @"반려|불허|거부|연기|\brejects?\b|\bdenied\b|\bdelays?\b");
                        if (approve != reject)
                            Add(factors, n, text, "etf-approval", approve ? 1 : -1,
                                "ETF " + (approve ? "승인 → 제도권 접근성 확대 → 신규 수요 기대" : "반려·연기 → 기대했던 신규 수요가 미뤄짐"),
                                "승인 자체가 자금 유입을 뜻하지는 않습니다. 발표 전 기대가 이미 반영돼 승인일에 오히려 빠지는 일이 잦았습니다.", 0.5);
                    }
                    if (Has(text, @"규제|제재|과세|세금|금지|단속|소송|기소|\bregulat\w*|\bban\b|\bsanction\w*|\blawsuit\b|\bsues?\b|\bSEC\b|\bCFTC\b|\bcrypto (?:rules?|regulations?|laws?)\b|\blegaliz\w*|\bderegulat\w*"))
                    {
                        // 결말이 원인을 이긴다. '소송 기각' 은 소송이 아니라 해소로 읽어야 한다.
                        // '해소' 가 어디에도 없어서 '규제 불확실성 해소' 가 방향 없는 기사가 됐다.
                        // 규제가 걷히는 것은 이 시장에서 가장 자주 인용되는 상승 재료다.
                        bool resolved = Has(text, @"기각|무혐의|승소|취하|철회|불확실성.{0,6}(?:해소|완화)|규제.{0,8}해소|" +
                            @"\bdismiss\w*|\bdrops? (?:the )?(?:case|charges?)\b|\bacquit\w*|regulatory clarity|\bclarity\b");
                        bool loosen = resolved || Has(text, @"완화|허용|합법화|\bapproves?\b|\beases?\b|\ballow\w*");
                        bool tighten = !resolved && Has(text, @"강화|도입|금지|제재|단속|과세|소송|기소|\bcrack\w*|\bban\b|\bsues?\b|\bcharges?\b|tighten\w*");
                        if (tighten != loosen)
                            Add(factors, n, text, "crypto-regulation", tighten ? -1 : 1,
                                "규제 " + (tighten ? "강화·법적 다툼 → 접근성 축소·불확실성 → 매도 압력" : "완화·해소 → 불확실성 감소 → 매수 여력 회복"),
                                "규제는 나라마다 다르고 시행까지 시간이 걸립니다. 발표만으로 실제 자금 흐름이 바뀌지 않을 수 있습니다.", 0.55);
                    }
                    if (Has(text, @"해킹|탈취|유출\s?사고|거래소.{0,10}(?:파산|정지|중단)|출금\s?중단|\bhack\w*|\bexploit\w*|\bbreach\b|\binsolvenc\w*") &&
                        !Has(text, @"복구|보상|환급|되찾|\brecover\w*|\brefund\w*"))
                        Add(factors, n, text, "exchange-incident", -1,
                            "거래소·프로토콜 사고 → 신뢰 훼손과 강제 매도 → " + target.Name + " 하락 압력",
                            "규모가 작거나 다른 코인·거래소의 일이면 영향이 제한됩니다. 사고 직후 저가 매수가 들어오기도 합니다.", 0.6);
                    if (Has(text, @"고래|대량\s?(?:이체|이동|매집|매도)|\bwhale\w*"))
                    {
                        bool toExchange = Has(text, @"거래소.{0,8}(?:입금|이체|전송)|매도\s?준비|\bto exchanges?\b|\bdeposit\w*");
                        bool fromExchange = Has(text, @"거래소.{0,8}(?:출금|인출)|장기\s?보관|콜드월렛|\bwithdraw\w*|\bto cold\b");
                        if (toExchange != fromExchange)
                            Add(factors, n, text, "whale-flow", toExchange ? -1 : 1,
                                "대량 보유분이 거래소로 " + (toExchange ? "들어옴 → 매도 준비로 읽히는 흐름" : "빠져나감 → 단기 매도 물량 감소"),
                                "지갑 이동이 곧 매매는 아닙니다. 이 신호는 널리 인용되지만 근거가 약하니 참고로만 봅니다.", 0.2);
                    }
                    if (Has(text, @"반감기|\bhalving\b"))
                        Add(factors, n, text, "halving", 1,
                            "반감기 → 신규 공급 감소 → 장기 수급 개선 기대",
                            "일정이 미리 알려져 있어 선반영 정도가 큽니다. 짧은 기간의 방향 근거로 삼기 어렵습니다.", 0.15);
                    if (Has(text, @"스테이블코인|\bUSDT\b|\bUSDC\b|테더|\bstablecoin\b"))
                    {
                        // '급증·급감' 은 '증가·감소' 와 글자가 달라 걸리지 않았다.
                        bool minted = Has(text, @"발행|추가\s?발행|증가|급증|확대|늘[어었]|\bmint\w*|\bissu\w*|\bsurg\w*|\bexpand\w*");
                        bool burned = Has(text, @"소각|회수|감소|급감|축소|줄[어었]|\bburn\w*|\bredeem\w*|\bshrink\w*|\bcontract\w*");
                        if (minted != burned)
                            Add(factors, n, text, "stablecoin-supply", minted ? 1 : -1,
                                "스테이블코인 " + (minted ? "발행 증가 → 시장에 들어올 유동성 확대" : "소각·감소 → 유동성 축소"),
                                "발행이 곧 매수는 아니며 다른 시장으로 갈 수도 있습니다.", 0.3);
                    }
                    // '매수' 가 목록에 없어 '기관 투자자 매수 확대' 가 통째로 빠졌다.
                    if (Has(text, @"(?:기업|국가|연기금|기관).{0,14}(?:매입|매수|채택|편입|보유|축적)|법정\s?통화\s?채택|" +
                        @"\badopts?\b.{0,20}\b(?:bitcoin|crypto)\b|\bcorporate treasury\b|\binstitution\w*\b.{0,20}\b(?:buy\w*|accumulat\w*|inflow\w*)\b"))
                        Add(factors, n, text, "institutional-adoption", 1,
                            "기관·국가의 매입·채택 → 장기 보유 수요 → " + target.Name + " 지지",
                            "발표와 실제 집행은 다르고 규모가 작으면 영향이 미미합니다.", 0.4);
                }

                if (target.Def.Kind == SourceKind.Fx)
                {
                    bool local = target.Def.Code == "FX_JPYKRW" ? Has(text, @"일본은행|\bBOJ\b|Bank of Japan") : subject;
                    if (local && hike != cut) Add(factors, n, text, "local-rate", hike ? 1 : -1, "대상 통화 금리 " + (hike ? "인상 → 금리 매력 증가 → 원화 대비 가격 지지" : "인하 → 금리 매력 약화 → 원화 대비 가격 압박"), "한국 금리·글로벌 달러·위험 회피의 동시 변동으로 교차환율 반응이 달라질 수 있습니다.", 0.6);
                }
                if (!subject) continue;
                // ★ 한국어는 되는데 영어가 통째로 안 됐다 ★
                //   'earnings beat' 만 있고 'beats earnings estimates' 가 없었다. 영어 헤드라인은
                //   동사가 앞에 오는데 어순을 하나만 적어 둔 탓이다. 'cuts full-year guidance',
                //   'tops expectations', 'downgraded' 도 전부 0점이었다(실측 v1.032).
                //   미국 주식 예측에서 실적은 가장 큰 동인인데 그 자리가 비어 있었다.
                bool up = Has(text, @"주가\s*(?:는|가)?\s*(?:상승|강세|급등)|영업이익.{0,12}(?:증가|개선)|실적.{0,8}(?:호조|상회)|매출.{0,8}증가|가격.{0,8}상승|" +
                    @"자사주.{0,8}(?:매입|취득|소각)|배당.{0,8}(?:확대|증액|인상|상향)|수주.{0,8}(?:확대|증가|잭팟)|어닝\s?서프라이즈|" +
                    @"\bearnings beat\b|\b(?:beat|beats|tops|topped|exceeds|exceeded)\b.{0,24}\b(?:estimates?|expectations?|forecasts?|views?)\b|" +
                    @"\b(?:raises?|raised|lifts?|lifted|boosts?|boosted)\b.{0,16}\b(?:outlook|guidance|forecast|target)\b|" +
                    @"\bshares?\b.{0,12}\b(?:rise|rises|rose|gain|gains|surge|surges|jump|jumps|climb|climbs)\b|\brall(?:y|ies|ied)\b|" +
                    @"\bupgrade[sd]?\b|\bbuyback\b|\brepurchase\b|\bdividend\b.{0,12}\b(?:hike|increase|raise|boost)\b");
                bool down = Has(text, @"주가\s*(?:는|가)?\s*(?:하락|약세|급락)|영업이익.{0,12}(?:감소|악화)|실적.{0,8}(?:부진|하회)|매출.{0,8}감소|가격.{0,8}하락|" +
                    @"리콜|압수\s?수색|(?:검찰|공정위|금감원|국세청).{0,10}(?:조사|제재|고발|추징)|배당.{0,8}(?:축소|삭감|중단)|어닝\s?쇼크|" +
                    @"\bearnings miss\b|\b(?:miss|misses|missed|trails?|trailed)\b.{0,24}\b(?:estimates?|expectations?|forecasts?|views?)\b|" +
                    @"\b(?:cuts?|cut|lowers?|lowered|slashes?|slashed|trims?|trimmed|scraps?|withdraws?)\b.{0,16}\b(?:outlook|guidance|forecast|target|dividend)\b|" +
                    @"\bshares?\b.{0,12}\b(?:fall|falls|fell|drop|drops|slide|slides|tumble|tumbles|sink|sinks)\b|\bplunge[sd]?\b|" +
                    @"\bdowngrade[sd]?\b|\bprofit warning\b|\b(?:SEC|DOJ|FTC)\b.{0,16}\b(?:probe|investigation|inquiry|lawsuit)\b|\brecalls?\b");
                // 실적만 회사 사건이 아니다. 자사주·배당·조사도 앞을 내다보는 사건이다.
                bool forwardEvent = Has(text, @"영업이익|실적|매출|가이던스|자사주|배당|리콜|조사|소송|어닝|수주|압수|" +
                    @"\b(?:earnings|outlook|revenue|guidance|buyback|repurchase|dividend|probe|investigation|inquiry|lawsuit|recall|upgrade[sd]?|downgrade[sd]?|estimates?|expectations?|results?|forecasts?|profit warning|share buyback)\b");
                if (up != down && !forwardEvent) Add(factors, n, text, "observed-price", 0, target.Name + "의 이미 발생한 가격 변동 · 미래 방향의 독립 근거로 중복 가산하지 않음", "가격 움직임의 원인이 되는 새로운 정책·실적·수급 사건을 따로 확인해야 합니다.", 0);
                if (up != down && forwardEvent) Add(factors, n, text, "own-performance", up ? 1 : -1, target.Name + " 자체 가격·실적 " + (up ? "개선" : "악화") + " → 기대와 수급의 " + (up ? "상승" : "하락") + " 압력", "과거 등락·실적은 미래 수익률과 다르며 선반영, 비용, 가이던스와 반대 보도를 함께 확인해야 합니다.", 0.7);
            }
            n.FactorTitle = n.Title; n.FactorContext = n.Context; n.FactorTargetKey = target.Key; n.Factors = factors;
            return factors;
        }
        internal static void Classify(DollarNews news)
        {
            var factors = Analyze(news);
            double impact = factors.Count == 0 ? 0 : factors.Sum(f => f.Direction * f.Weight) / factors.Count;
            news.Direction = news.EvidenceDirection = Math.Sign(impact); news.EvidenceWeight = Math.Abs(impact);
            news.Topic = news.Target.Name + " 영향";
            news.EvidenceReason = factors.Count == 0 ? "대상 품목의 정책·실적·수급 맥락 자료 · 본문 심화 해석은 Spark 사용" : string.Join("\n", factors.Select(f => f.Mechanism + "\n반대: " + f.Counter));
        }
    }
}
