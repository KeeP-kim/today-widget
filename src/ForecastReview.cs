using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DeskWidget
{
    internal sealed class ForecastReviewRow
    {
        internal string Model, Version, State, Reason;
        internal int Horizon;
        internal DateTime Created, Due, Received;
        internal double Anchor, Forecast, Actual, Error, BaselineError;
        internal bool Hit;
    }
    // Already observed, same-feed outcomes. No current-price backfill or fitted correction.
    internal static class ForecastReview
    {
        internal const string Policy = "validated-outcomes-v1";
        private static string Q(string text) { return "\"" + Json.Escape(text ?? "") + "\""; }
        private static string N(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        internal static string Input(Quote quote, DateTime now)
        {
            var rows = PredictionJournal.ReviewRows(quote, now, now.AddDays(-30), now);
            var selected = new List<ForecastReviewRow>();
            foreach (var group in rows.Where(r => r.State == "scored").GroupBy(r => r.Model + "|" + r.Horizon + "|" + Config.ScoringEra(r.Version))) {
                DateTime end = DateTime.MinValue;
                foreach (var row in group.OrderBy(r => r.Created)) {
                    if (row.Created < end) continue;
                    selected.Add(row); end = row.Due;
                }
            }
            selected = selected.OrderByDescending(r => r.Received).ThenBy(r => r.Model, StringComparer.Ordinal).ThenBy(r => r.Horizon).Take(12).ToList();
            var b = new StringBuilder("{\"policy\":" + Q(Policy) + ",\"as_of_utc\":" + Q(now.ToString("o")) +
                ",\"lookback_days\":30,\"overlaps_excluded_per_model_horizon_era\":true,\"pending\":" + rows.Count(r => r.State == "pending") +
                ",\"missed_window\":" + rows.Count(r => r.State == "missed") + ",\"invalid_scores\":" + rows.Count(r => r.State == "invalid") + ",\"examples\":[");
            bool first = true;
            foreach (var row in selected) {
                if (!first) b.Append(','); first = false;
                b.Append("{\"model\":").Append(Q(row.Model)).Append(",\"version\":").Append(Q(row.Version))
                    .Append(",\"scoring_era\":").Append(Q(Config.ScoringEra(row.Version))).Append(",\"horizon\":").Append(row.Horizon)
                    .Append(",\"created_utc\":").Append(Q(row.Created.ToString("o"))).Append(",\"due_utc\":").Append(Q(row.Due.ToString("o")))
                    .Append(",\"observed_utc\":").Append(Q(row.Received.ToString("o"))).Append(",\"anchor\":").Append(N(row.Anchor))
                    .Append(",\"forecast\":").Append(N(row.Forecast)).Append(",\"actual\":").Append(N(row.Actual))
                    .Append(",\"error_percent_of_anchor\":").Append(N(row.Error)).Append(",\"baseline_error_percent_of_anchor\":").Append(N(row.BaselineError))
                    .Append(",\"direction_hit\":").Append(row.Hit ? "true" : "false").Append(",\"prior_reason\":").Append(Q(row.Reason)).Append('}');
            }
            return b.Append("]}").ToString();
        }
        internal static string Yesterday(Quote quote, DateTime now)
        {
            DateTime until = now.AddHours(9).Date.AddHours(-9), from = until.AddDays(-1);
            return Describe(PredictionJournal.ReviewRows(quote, now, from, until), from.AddHours(9).ToString("MM-dd", CultureInfo.InvariantCulture));
        }
        internal static string Describe(IList<ForecastReviewRow> rows, string date)
        {
            var lines = new List<string> { "전일 점검 " + date + " · 같은 시세 출처 · 반복/겹침 포함한 개별 기록" };
            if (rows.Count == 0) { lines.Add("저장된 1일·주간 예측 없음"); return string.Join("\n", lines); }
            foreach (var group in rows.GroupBy(r => r.Model + "|" + r.Horizon).OrderBy(g => g.Key, StringComparer.Ordinal)) {
                var example = group.First(); var done = group.Where(r => r.State == "scored").ToList();
                string text = (example.Model == "basic" ? "참고" : example.Model == "extreme-rule" ? "규칙 AI 대체" : DollarSpark.ModelName(example.Model)) +
                    " · " + (example.Horizon == 1 ? "1일" : "주간") + " · 채점 " + done.Count + "/" + group.Count();
                if (done.Count > 0) text += " · 방향 " + done.Count(r => r.Hit) + "/" + done.Count + " · 오차 " +
                    done.Average(r => r.Error).ToString("0.00", CultureInfo.InvariantCulture) + "% · 유지 " + done.Average(r => r.BaselineError).ToString("0.00", CultureInfo.InvariantCulture) + "%";
                foreach (string state in new[] { "pending", "waiting", "missed", "invalid" }) {
                    int count = group.Count(r => r.State == state);
                    if (count > 0) text += " · " + (state == "pending" ? "만기 대기" : state == "waiting" ? "관측 대기" : state == "missed" ? "채점 창 놓침" : "기록 검증 실패") + " " + count;
                }
                lines.Add(text);
            }
            lines.Add("오차는 기준가 대비 절대 오차 · 소표본으로 성능 개선을 판단하지 않습니다");
            return string.Join("\n", lines);
        }
    }
}
