using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DeskWidget
{
    internal sealed class PolicyReference
    {
        internal string Country, Institution, Kind, PublishedDate, Summary, Url;
    }
    internal sealed class PolicyEvent
    {
        internal string Name, Url, LocalDate, Zone;
        internal DateTime StartUtc, EndUtc;
        internal bool ExactTime;
        internal string Display { get { return ExactTime ? StartUtc.AddHours(9).ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) + " 한국시간 · " + Name : LocalDate + " " + Zone + " 날짜 · " + Name + " (시각 미확인)"; } }
    }

    // Public source review supplied with this daily update. It is not a live policy feed.
    // The original source dates never move when the user refreshes an analysis.
    internal static class PolicyContext
    {
        internal const string Policy = "dated-public-policy-v1";
        internal static readonly DateTime ReviewedUtc = new DateTime(2026, 10, 4, 5, 0, 0, DateTimeKind.Utc);
        internal static DateTime ExpiresUtc { get { return ReviewedUtc.AddDays(7); } }
        internal const string TreasuryJoint = "https://home.treasury.gov/news/press-releases/sb0269";
        internal const string TreasuryCurrent = "https://home.treasury.gov/news/press-releases/sb0539";
        internal const string KoreaMarket = "https://english.mofe.go.kr/pc/selectTbPressCenterDtl.do?boardCd=N0001&seq=6439";
        internal const string FedCalendar = "https://www.federalreserve.gov/newsevents/2026-october.htm";
        internal const string KoreaCalendar = "https://www.bok.or.kr/portal/stats/statsPublictSchdul/listCldr.do?date=2026-10&menuNo=200775";
        private static readonly PolicyReference[] Sources = {
            new PolicyReference { Country="US", Institution="미 재무부", Kind="stated_objective", PublishedDate="2026-06-23", Url=TreasuryCurrent,
                Summary="달러의 국제적 지위·시장 깊이·제도 신뢰 및 달러를 강화하는 금융혁신을 강조했다. 특정 USD/KRW 수준이나 단기 환율 상승 목표는 확인되지 않았다." },
            new PolicyReference { Country="US", Institution="백악관 CEA 당시 의장 발언", Kind="policy_argument", PublishedDate="2025-04-07", Url="https://www.whitehouse.gov/briefings-statements/2025/04/cea-chairman-steve-miran-hudson-institute-event-remarks/",
                Summary="기축통화 수요가 달러를 과도하게 강하게 유지해 제조업 경쟁력에 부담을 준다는 주장이다. 당시의 분석이며 현행 약달러 목표나 실제 개입 결정으로 확인된 자료가 아니다." },
            new PolicyReference { Country="US/KR", Institution="한미 외환당국 공동성명", Kind="stated_objective", PublishedDate="2025-09-30", Url=TreasuryJoint,
                Summary="경쟁 목적의 환율 조작을 배제하고, 개입은 과도한 변동·무질서한 절하와 절상을 양방향으로 완화하는 데 한정한다는 원칙을 합의했다." },
            new PolicyReference { Country="US/KR", Institution="미 재무부·한국 경제부총리 회담", Kind="stated_objective", PublishedDate="2026-04-19", Url="https://home.treasury.gov/news/press-releases/sb0459",
                Summary="원화의 과도한 변동은 바람직하지 않다고 재확인했다. 실제 외환 개입 금액과 목표 환율은 이 자료에서 확인되지 않았다." },
            new PolicyReference { Country="KR", Institution="한국 재정경제부", Kind="implemented_action", PublishedDate="2026-07-06", Url=KoreaMarket,
                Summary="24시간 외환시장 출범과 상시 감시를 발표했고 시장 안정·새 거래체계 정착을 최우선으로 설명했다. 시장 개방 자체가 원화 강세 또는 약세를 보장하지 않는다." },
            new PolicyReference { Country="US", Institution="연준 FOMC", Kind="implemented_action", PublishedDate="2026-09-16", Url="https://www.federalreserve.gov/newsevents/pressreleases/monetary20260916a.htm",
                Summary="물가 안정과 이중 책무를 위해 기준금리 목표범위를 0.25%p 올려 3.75~4.00%로 결정했다. 발표 당시 결정이며 미국 정부의 환율 목표가 아니다." },
            new PolicyReference { Country="KR", Institution="한국은행 금통위", Kind="implemented_action", PublishedDate="2026-08-27", Url="https://www.bok.or.kr/portal/bbs/B0000169/view.do?menuNo=200059&nttId=11064215",
                Summary="물가 압력·금융안정 위험을 고려해 기준금리를 0.25%p 올려 3.00%로 결정했다. 발표 당시 결정이며 한국 정부의 환율 목표가 아니다." },
            new PolicyReference { Country="KR", Institution="FTSE Russell", Kind="scheduled_program", PublishedDate="2026-03-16", Url="https://research.ftserussell.com/products/index-notices/home/getnotice/?id=2619061",
                Summary="한국 국채의 WGBI 편입은 2026년 4~11월 8개월 균등 단계로 공지됐다. 실제 자금 유입·환헤지·현물 원화 매수량은 별도 확인해야 한다." },
            new PolicyReference { Country="KR", Institution="산업통상부 9월 수출입 발표", Kind="observed_release", PublishedDate="2026-10-01", Url="https://english.motir.go.kr/eng/article/EATCLdfa319ada/2743/view",
                Summary="반도체·AI 수출 확대와 무역흑자, 원유 수입비용 및 중동 운송 위험이 함께 보고됐다. 수출 대금의 달러 공급과 에너지 수입의 달러 수요는 반대 경로이며 월 수출액은 당일 환전량이 아니다." }
        };
        private static readonly PolicyEvent[] Calendar = {
            DayEvent("미국 ISM 서비스업 PMI", "2026-10-05", "미국 동부", 4, "https://www.ismworld.org/supply-management-news-and-reports/reports/rob-report-calendar/"),
            ExactEvent("한국 2분기 자금순환(잠정)", "2026-10-07T03:00:00Z", KoreaCalendar),
            ExactEvent("연준 9월 FOMC 의사록", "2026-10-07T18:00:00Z", FedCalendar),
            ExactEvent("한국 8월 국제수지(잠정)", "2026-10-07T23:00:00Z", KoreaCalendar),
            ExactEvent("미국 9월 CPI", "2026-10-14T12:30:00Z", "https://www.bls.gov/schedule/2026/10_sched.htm"),
            ExactEvent("미국 9월 PPI", "2026-10-15T12:30:00Z", "https://www.bls.gov/schedule/2026/10_sched.htm"),
            DayEvent("한국은행 통화정책방향 회의", "2026-10-22", "한국", -9, "https://www.bok.or.kr/portal/singl/mainEvent/listCldr.do?date=2026-10&menuNo=200035"),
            ExactEvent("연준 10월 FOMC 결정 예정(회의 27~28일)", "2026-10-28T18:00:00Z", FedCalendar)
        };
        private static PolicyEvent ExactEvent(string name, string utc, string url)
        {
            var time = DateTime.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            return new PolicyEvent { Name=name, Url=url, StartUtc=time, EndUtc=time, ExactTime=true, Zone="UTC", LocalDate=time.ToString("yyyy-MM-dd") };
        }
        // These two dated October 2026 entries have verified local dates, not verified clock times.
        private static PolicyEvent DayEvent(string name, string date, string zone, int utcHours, string url)
        {
            var start = DateTime.SpecifyKind(DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddHours(utcHours), DateTimeKind.Utc);
            return new PolicyEvent { Name=name, Url=url, StartUtc=start, EndUtc=start.AddDays(1), LocalDate=date, Zone=zone };
        }
        internal static bool Available(DateTime now) { return now >= ReviewedUtc && now < ExpiresUtc; }
        internal static IEnumerable<PolicyReference> References(DateTime now) { return Available(now) ? Sources : Enumerable.Empty<PolicyReference>(); }
        internal static IEnumerable<PolicyEvent> Upcoming(DateTime now)
        { return Available(now) ? Calendar.Where(e => e.ExactTime ? e.StartUtc > now : e.EndUtc > now) : Enumerable.Empty<PolicyEvent>(); }
        internal static bool IsSourceLink(string url)
        { return Sources.Any(s => s.Url == url) || Calendar.Any(e => e.Url == url); }
        private static string Q(string value) { return "\"" + Json.Escape(value ?? "") + "\""; }
        internal static string Input(PredictionTarget target, DateTime now)
        {
            if (target.Economic || target.Weather) return "null";
            bool active = Available(now);
            var b = new StringBuilder("{\"policy\":" + Q(Policy) + ",\"reviewed_utc\":" + Q(ReviewedUtc.ToString("o")) + ",\"expires_utc\":" + Q(ExpiresUtc.ToString("o")) +
                ",\"status\":" + Q(active ? "reviewed_snapshot" : now < ReviewedUtc ? "not_yet_available" : "needs_refresh") +
                ",\"live_refreshed\":false,\"coverage_complete\":false,\"directional_scoring_allowed\":false,\"references\":[");
            bool first = true;
            foreach (var source in References(now)) {
                if (!first) b.Append(','); first=false;
                b.Append("{\"country\":").Append(Q(source.Country)).Append(",\"institution\":").Append(Q(source.Institution)).Append(",\"kind\":").Append(Q(source.Kind))
                    .Append(",\"source_date\":").Append(Q(source.PublishedDate)).Append(",\"url\":").Append(Q(source.Url)).Append(",\"summary\":").Append(Q(source.Summary))
                    .Append(",\"temporal_role\":\"background\",\"numeric_fx_target\":null}");
            }
            b.Append("],\"upcoming_events\":["); first=true;
            foreach (var entry in Upcoming(now)) {
                if (!first) b.Append(','); first=false;
                b.Append("{\"name\":").Append(Q(entry.Name)).Append(",\"url\":").Append(Q(entry.Url)).Append(",\"result\":null,\"consensus\":null,\"status\":\"scheduled_unconfirmed_result\"")
                    .Append(",\"time_precision\":").Append(Q(entry.ExactTime ? "minute" : "local_date_only"))
                    .Append(",\"scheduled_utc\":").Append(entry.ExactTime ? Q(entry.StartUtc.ToString("o")) : "null")
                    .Append(",\"local_date\":").Append(Q(entry.LocalDate)).Append(",\"local_zone\":").Append(Q(entry.Zone))
                    .Append(",\"window_start_utc\":").Append(Q(entry.StartUtc.ToString("o"))).Append(",\"window_end_utc\":").Append(Q(entry.EndUtc.ToString("o")))
                    .Append(",\"may_fall_in_horizons\":[").Append(string.Join(",", new[] { 1, 5, 20 }.Where(h => entry.StartUtc <= PredictionJournal.Due(now, target, h)))).Append("]}");
            }
            return b.Append("]}").ToString();
        }
        internal static string Summary(DateTime now)
        {
            if (!Available(now)) return "공식 정책·일정 자료 재확인 필요 · 자동 갱신 자료가 아닙니다";
            return "10-04 공식 원문 확인 · 7일 유효 · 일부 정책·일정만 포함\n미국: 달러 신뢰·국제 지위 / 제조업 부담 논거도 존재\n한국: 외환시장 안정·급변 완화 / 고정 목표 환율 미확인\n정부의 희망과 실제 가격 방향을 구분합니다. 금리는 중앙은행 결정입니다.\n원문 발표일을 유지하며 새 방향 점수에는 최신 기사 근거가 필요합니다.";
        }
    }
}
