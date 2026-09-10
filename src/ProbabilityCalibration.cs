using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskWidget
{
    internal sealed class ProbabilityCase
    {
        internal int At, Resolved, Outcome;
        internal double[] Similar, Baseline;
    }
    internal sealed class ProbabilityAdjustment
    {
        internal bool Ready;
        internal int Count, Hits, RawHits, BaselineHits;
        internal double Weight = 1, Brier, RawBrier, BaselineBrier;
        internal double[] Probabilities;
    }
    /// <summary>
    /// 예측이 '아무것도 안 하는 것' 보다 나은지 재는 성적표.
    ///
    /// ★ 상대는 균등 무작위(3분류 0.667)가 아니라 기후값이다 ★
    ///   기후값(climatology)은 그때까지의 실현 빈도를 그대로 내놓는 예측기다.
    ///   보합이 흔한 자산에서는 이것만으로도 Brier 가 0.52 근처까지 내려간다.
    ///   0.667 을 상대로 삼으면 실력이 없는데 있는 것처럼 보인다.
    ///   (Goyal-Welch 2008 이후 표본외 성능을 '역사적 평균 대비' 로 재는 것이 표준이다.)
    ///
    /// Murphy(1973) 분해: Brier = 불확실성 - 구별력 + 눈금오차
    ///   불확실성 : 자산이 원래 얼마나 안 정해져 있나. 우리가 못 바꾼다.
    ///   구별력   : 상황마다 다른 확률을 내놓고 그게 실제로 갈렸나. 클수록 좋다.
    ///   눈금오차 : 70% 라 한 날 중 실제로 70% 가 맞았나. 작을수록 좋다.
    /// 이 셋을 갈라 보면 '정보가 없는 것' 과 '눈금만 어긋난 것' 이 구분된다.
    /// 앞이면 자료를 더 넣어야 하고, 뒤면 보정층만 고치면 된다.
    /// </summary>
    internal sealed class ProbabilityScore
    {
        internal int Count;
        internal double Brier;         // 유사 패턴 빈도로 예측했을 때
        internal double Climatology;   // 기후값으로 예측했을 때
        internal double Uncertainty, Resolution, Reliability;
        /// <summary>
        /// 칸을 나눠 재기 때문에 남는 몫. 한 칸 안에서도 확률이 조금씩 다르면
        /// 세 항의 합이 Brier 와 정확히 같아지지 않는다. 그 차이를 숨기지 않고 적는다.
        /// (합성 자료처럼 한 칸 안의 확률이 모두 같으면 0 이 된다)
        /// </summary>
        internal double Residual;
        /// <summary>기후값 대비 실력. 0 이면 기후값과 같고 음수면 기후값보다 나쁘다.</summary>
        internal double Skill { get { return Count == 0 || Climatology <= 0 ? 0 : 1 - Brier / Climatology; } }
        internal bool Enough { get { return Count >= 30; } }
    }

    internal static class ProbabilityCalibration
    {
        private const int Bins = 10;
        private static int Bin(double p) { int b = (int)(p * Bins); return b < 0 ? 0 : b >= Bins ? Bins - 1 : b; }

        /// <summary>
        /// 겹치지 않고 이미 결과가 난 사례만 시간 순으로 고른다.
        /// Evaluate 와 Score 가 반드시 같은 표본을 봐야 한다 - 다르면 성적표와
        /// 적용 판정이 서로 다른 이야기를 하게 된다.
        /// </summary>
        private static List<ProbabilityCase> Clean(List<ProbabilityCase> cases, int now)
        {
            var clean = new List<ProbabilityCase>(); int end = -1;
            foreach (var c in cases.OrderBy(x => x.At)) {
                if (c.At < end || c.Resolved <= c.At || c.Resolved > now || c.Outcome < -1 || c.Outcome > 1 || !Valid(c.Similar) || !Valid(c.Baseline)) continue;
                clean.Add(c); end = c.Resolved;
            }
            return clean;
        }

        /// <summary>기후값 대비 성적과 Murphy 분해. 확률 보정을 적용하기 전의 날것을 잰다.</summary>
        internal static ProbabilityScore Score(List<ProbabilityCase> cases, int now)
        {
            var result = new ProbabilityScore();
            var clean = Clean(cases, now);
            if (clean.Count == 0) return result;
            result.Count = clean.Count;
            foreach (var c in clean) {
                result.Brier += Brier(c.Similar, c.Outcome);
                result.Climatology += Brier(c.Baseline, c.Outcome);
            }
            result.Brier /= clean.Count;
            result.Climatology /= clean.Count;
            // 우리 Brier 는 결과별 제곱오차의 합이므로, 결과마다 따로 분해해 더하면 그대로 맞는다.
            for (int k = 0; k < 3; k++)
            {
                double baseRate = clean.Count(c => c.Outcome + 1 == k) / (double)clean.Count;
                result.Uncertainty += baseRate * (1 - baseRate);
                for (int b = 0; b < Bins; b++)
                {
                    var group = clean.Where(c => Bin(c.Similar[k]) == b).ToList();
                    if (group.Count == 0) continue;
                    double share = group.Count / (double)clean.Count;
                    double meanP = group.Average(c => c.Similar[k]);
                    double meanO = group.Count(c => c.Outcome + 1 == k) / (double)group.Count;
                    result.Reliability += share * (meanP - meanO) * (meanP - meanO);
                    result.Resolution += share * (meanO - baseRate) * (meanO - baseRate);
                }
            }
            // 세 항만으로는 Brier 가 정확히 복원되지 않는다. 남는 몫을 그대로 적는다.
            result.Residual = result.Brier - (result.Uncertainty - result.Resolution + result.Reliability);
            return result;
        }

        /// <summary>성적표를 사람이 읽을 한 문장으로. 어디를 고쳐야 하는지가 요점이다.</summary>
        internal static string Diagnosis(ProbabilityScore s)
        {
            if (s == null || s.Count == 0) return "평가 자료 부족";
            if (!s.Enough) return "표본 " + s.Count + "회로는 판단할 수 없음";
            // 순서가 뜻을 정한다. '기후값과 같음' 을 '나쁨' 으로 부르면 안 된다.
            if (s.Skill < -0.001) return "기후값보다 나쁨 · 이 조건 구분이 도움이 되지 않는다";
            if (s.Resolution < 0.005) return "구별력이 거의 없음 · 상황마다 다른 확률을 내놓지 못한다";
            if (s.Skill <= 0.001) return "기후값과 같음 · 조건을 나눈 이득이 없다";
            if (s.Reliability > s.Resolution) return "눈금이 어긋남 · 확률 보정으로 줄일 수 있다";
            return "기후값보다 나음 · 과거 구간에 한한 결과다";
        }

        internal static double[] Frequencies(DollarPattern p)
        { return new[] { p.Down / (double)p.Count, p.Flat / (double)p.Count, p.Up / (double)p.Count }; }
        internal static double[] Blend(double[] similar, double[] baseline, double weight)
        { return Enumerable.Range(0, 3).Select(i => weight * similar[i] + (1 - weight) * baseline[i]).ToArray(); }
        internal static double Brier(double[] probabilities, int outcome)
        { return Enumerable.Range(0, 3).Sum(i => Math.Pow(probabilities[i] - (i == outcome + 1 ? 1 : 0), 2)); }
        private static int Best(double[] p)
        { return p[1] >= p[0] && p[1] >= p[2] ? 0 : p[2] > p[0] ? 1 : -1; }
        private static double Fit(List<ProbabilityCase> past)
        {
            // Equal-loss ties retain the original model, reducing needless changes.
            return new[] { 1.0, 0.75, 0.5, 0.25, 0.0 }.OrderBy(w => past.Sum(c => Brier(Blend(c.Similar, c.Baseline, w), c.Outcome))).First();
        }
        private static bool Better(List<double> differences)
        {
            double mean = differences.Average();
            double variance = differences.Sum(x => (x - mean) * (x - mean)) / (differences.Count - 1);
            return mean + 1.96 * Math.Sqrt(variance / differences.Count) < -0.0001;
        }
        private static bool Valid(double[] p)
        { return p != null && p.Length == 3 && p.All(x => !double.IsNaN(x) && !double.IsInfinity(x) && x >= 0 && x <= 1) && Math.Abs(p.Sum() - 1) < 0.00001; }
        internal static ProbabilityAdjustment Evaluate(List<ProbabilityCase> cases, int now, double[] similar, double[] baseline)
        {
            var result = new ProbabilityAdjustment { Probabilities = similar };
            if (!Valid(similar) || !Valid(baseline)) return result;
            var clean = Clean(cases, now);
            var rawDifference = new List<double>(); var baseDifference = new List<double>();
            foreach (var current in clean) {
                // The outcome of the current or an overlapping case must not select its own weight.
                var past = clean.Where(c => c.Resolved <= current.At && c.At < current.At).ToList();
                if (past.Count < 30) continue;
                var p = Blend(current.Similar, current.Baseline, Fit(past));
                double loss = Brier(p, current.Outcome), raw = Brier(current.Similar, current.Outcome), naive = Brier(current.Baseline, current.Outcome);
                result.Count++; result.Brier += loss; result.RawBrier += raw; result.BaselineBrier += naive;
                if (Best(p) == current.Outcome) result.Hits++;
                if (Best(current.Similar) == current.Outcome) result.RawHits++;
                if (Best(current.Baseline) == current.Outcome) result.BaselineHits++;
                rawDifference.Add(loss - raw); baseDifference.Add(loss - naive);
            }
            if (result.Count > 0) { result.Brier /= result.Count; result.RawBrier /= result.Count; result.BaselineBrier /= result.Count; }
            result.Ready = result.Count >= 30 && result.Hits >= result.RawHits && result.Hits >= result.BaselineHits && Better(rawDifference) && Better(baseDifference);
            if (result.Ready) { result.Weight = Fit(clean); result.Probabilities = Blend(similar, baseline, result.Weight); }
            return result;
        }
    }
}
