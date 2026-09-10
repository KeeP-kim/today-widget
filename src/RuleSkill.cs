// 규칙 하나하나가 제 밥값을 하는지 재는 장부.
//
// ★ 왜 필요한가 ★
//   지금 남은 감사 지적은 전부 '규칙이 없어서 침묵' 이다. 규칙을 더 넣으면 그 침묵은
//   사라진다. 그런데 규칙을 넣는 것은 곧 차원을 늘리는 것이고, 차원을 늘리면 자료가
//   특징별로 더 쪼개져 표본이 검증 문턱 아래로 떨어진다. 지금 측정된 실력이 기후값보다
//   못한(−0.032) 상태에서 검증 없이 규칙을 늘리면, 성적이 좋아지는 게 아니라
//   좋아 보이기만 한다.
//
//   그래서 넣기 전에 잴 자리를 먼저 만든다. 이 장부는 이미 채점이 끝난 기록만 보고,
//   "이 규칙이 방향을 말한 날, 그 방향이 실제와 맞았나" 를 규칙별로 센다.
//   그리고 지금 표본으로 결론을 낼 수 있는지, 없다면 몇 건이 더 필요한지를 같이 적는다.
//
// ★ 이 장부가 하지 않는 것 ★
//   - 규칙을 자동으로 켜고 끄지 않는다. 사람이 보고 정한다.
//   - AI 예측은 세지 않는다. AI 는 규칙으로 쪼갤 수 없다.
//   - 과거 기사를 지금 와서 다시 받아 채우지 않는다. 그건 미래를 아는 상태로 재는 것이다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace DeskWidget
{
    internal sealed class RuleRow
    {
        public string Rule;
        /// <summary>이 규칙이 방향을 말한 채점 건수.</summary>
        public int Fired;
        /// <summary>그 방향이 실제와 맞은 횟수.</summary>
        public int Hits;
        /// <summary>같은 건들에서 '늘 같은 쪽' 이라고만 답했을 때 맞았을 횟수(기후값).</summary>
        public int BaselineHits;
        public double HitRate { get { return Fired == 0 ? double.NaN : (double)Hits / Fired; } }
        public double BaselineRate { get { return Fired == 0 ? double.NaN : (double)BaselineHits / Fired; } }
        /// <summary>기후값 대비 우위. 음수면 이 규칙은 없느니만 못했다.</summary>
        public double Edge { get { return Fired == 0 ? double.NaN : HitRate - BaselineRate; } }
        /// <summary>결론을 낼 만큼 쌓였는가.</summary>
        public bool Enough { get { return Fired >= RuleSkill.MinimumFires; } }
        /// <summary>지금 보이는 크기의 우위를 우연과 가르려면 모두 몇 건이 필요한가.</summary>
        public int Needed { get { return RuleSkill.NeededForEdge(HitRate, BaselineRate); } }
        /// <summary>
        /// 이 숫자를 만든 기록들의 판. ★ 채점에 손댄 판이 섞여 있으면 같은 잣대가 아니다 ★
        /// 문턱·가중치·규칙이 바뀐 판을 한 통에 섞어 재면, 그 성적은 어느 것의 성적도 아니다.
        /// </summary>
        public HashSet<string> Versions = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>장부가 세는 한 건. 한 기록·한 기간이며 모델은 구분하지 않는다.</summary>
    internal sealed class LedgerCase
    {
        public string Horizon;
        public DateTime Created, Due;
        public int Outcome;
        public string Version;
        public Dictionary<string, int> Said;
    }

    internal static class RuleSkill
    {
        /// <summary>
        /// 규칙 하나를 판단하는 데 필요한 최소 건수.
        /// 확률 보정(ProbabilityCalibration)이 쓰는 30과 같은 값을 쓴다 - 잣대가
        /// 자리마다 다르면 어느 쪽 말을 믿어야 할지 알 수 없다.
        /// </summary>
        internal const int MinimumFires = 30;

        /// <summary>
        /// 적중률 p 가 기후값 p0 과 다르다고 말하려면 몇 건이 필요한가.
        ///
        /// ★ 이 숫자가 큰 것이 이 일의 본질이다 ★
        ///   55%가 50%와 다르다고 2시그마로 말하려면 **396건**이 필요하다(여기 셈).
        ///   기후값을 이미 아는 값으로 놓고 재는 한쪽 표본 셈이다. 두 예측을 서로
        ///   견주는 셈은 그 두 배쯤 든다(약 620건). 어느 쪽이든 하루 몇 건씩 쌓는
        ///   개인에게는 몇 달에서 몇 년이다. '느낌상 좋아졌다' 로 규칙을 넣으면 안 된다.
        ///   차이가 없으면(또는 값이 없으면) int.MaxValue - '이걸로는 영영 못 가른다'.
        /// </summary>
        internal static int NeededForEdge(double rate, double baseline)
        {
            if (double.IsNaN(rate) || double.IsNaN(baseline)) return int.MaxValue;
            double gap = Math.Abs(rate - baseline);
            if (gap < 1e-9) return int.MaxValue;
            double spread = rate * (1 - rate);
            if (spread <= 0) spread = 0.25;      // 전승·전패는 분산이 0 이라 그대로 쓰면 1건이 된다.
            double n = 4 * spread / (gap * gap); // t=2 기준
            return n >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(n);
        }

        private static PredictionTarget TargetOf(string identity)
        {
            string id = (identity ?? "").Split('|')[0];
            int colon = id.IndexOf(':');
            if (colon <= 0) return new PredictionTarget(null);
            string kind = id.Substring(0, colon), code = id.Substring(colon + 1);
            SourceKind k = kind == "coin" ? SourceKind.Coin : kind == "wstock" ? SourceKind.WorldStock
                : kind == "dstock" ? SourceKind.DomesticStock : kind == "index" ? SourceKind.Index
                : kind == "weather" ? SourceKind.Weather : SourceKind.Fx;
            return new PredictionTarget(new SymbolDef(k, code, code));
        }

        private static double Num(XmlElement e, string name)
        {
            double v;
            return e != null && double.TryParse(e.GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                ? v : double.NaN;
        }

        /// <summary>문서를 올리기 전에 뿌리 속성만 흘려 읽어 이 품목의 기록인지 본다.</summary>
        private static bool HeaderMatches(string path, string identity, string source)
        {
            try {
                using (var reader = XmlReader.Create(path, new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit })) {
                    if (!reader.ReadToFollowing("prediction")) return false;
                    // XmlReader 는 없는 속성에 null 을 준다 - XmlElement 의 "" 와 다르다.
                    return (reader.GetAttribute("identity") ?? "") == (identity ?? "")
                        && (reader.GetAttribute("source") ?? "") == (source ?? "");
                }
            } catch (XmlException) { return false; } catch (IOException) { return false; }
              catch (UnauthorizedAccessException) { return false; }
        }
        private static XmlDocument Load(string path)
        {
            try {
                // 기록 한 건은 기사 전건과 3년치 환율이 들어간다. 상한 없이 올리면
                // 손상되거나 부풀려진 파일 하나가 화면 스레드를 통째로 붙든다(PredictionJournal.Read 와 같은 잣대).
                if (new FileInfo(path).Length > 4194304) return null;
                var doc = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(path, new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 4194304 }))
                    doc.Load(reader);
                return doc;
            } catch (XmlException) { return null; } catch (IOException) { return null; }
              catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>한 기록의 기사 전부에 규칙을 걸어, 규칙마다 말한 방향(부호)을 모은다.</summary>
        private static Dictionary<string, int> SaidOf(XmlElement root, PredictionTarget target)
        {
            var net = new Dictionary<string, double>();
            foreach (XmlElement a in root.SelectNodes("article"))
            {
                string text = a.InnerText ?? "";
                int nl = text.IndexOf('\n');
                var news = new DollarNews {
                    Target = target.Dollar ? null : target,
                    Title = nl < 0 ? text : text.Substring(0, nl),
                    Context = nl < 0 ? "" : text.Substring(nl + 1),
                    Source = a.GetAttribute("source"), Url = a.GetAttribute("url")
                };
                foreach (var factor in DollarFactors.Analyze(news))
                {
                    if (factor.Direction == 0 || factor.Weight <= 0) continue;
                    if (!net.ContainsKey(factor.Rule)) net[factor.Rule] = 0;
                    net[factor.Rule] += factor.Direction * factor.Weight;
                }
            }
            var said = new Dictionary<string, int>();
            foreach (var pair in net)
                if (Math.Abs(pair.Value) > 1e-12) said[pair.Key] = Math.Sign(pair.Value);
            return said;
        }

        /// <summary>
        /// 채점이 끝난 기록만 보고 규칙별 장부를 만든다.
        ///
        /// 한 기록·한 기간에서 같은 규칙은 한 번만 센다. 같은 사건을 세 매체가 보도했다고
        /// 그 규칙이 세 번 맞은 것은 아니다(감사 2-5 가 지적한 바로 그 부풀리기다).
        /// </summary>
        internal static List<RuleRow> Ledger(string folder, string identity, string source)
        {
            var rows = new Dictionary<string, RuleRow>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return new List<RuleRow>();
            var target = TargetOf(identity);
            // 기후값: 채점된 건들의 실제 방향 중 가장 흔한 쪽. 그것만 늘 답했을 때의 성적이
            // 규칙이 넘어야 할 선이다. 균등 무작위(1/3)가 아니다(v1.017 과 같은 잣대).
            var cases = new List<LedgerCase>();

            foreach (string path in Directory.GetFiles(folder, "*.xml"))
            {
                if (path.EndsWith(".score.xml", StringComparison.Ordinal)) continue;
                // 남의 품목이면 문서를 올리기 전에 헤더에서 끝낸다 - 장부는 화면 스레드에서 돈다.
                if (!HeaderMatches(path, identity, source)) continue;
                var doc = Load(path); if (doc == null) continue;
                var root = doc.DocumentElement; if (root == null || root.Name != "prediction") continue;
                double anchor = Num(root, "anchor");
                if (double.IsNaN(anchor) || anchor <= 0) continue;

                DateTime created;
                if (!DateTime.TryParse(root.GetAttribute("created"), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out created)) continue;
                string version = root.GetAttribute("version");

                // 그 시점 기사에 지금 규칙을 걸어 본다. 기사 원문은 기록 안에 있으므로
                // 지나서 다시 받아 오지 않는다 - 그랬다면 미래를 아는 상태로 재는 것이다.
                // ★ 기록마다 한 번, 그리고 쓸 때만 ★
                //   전에는 예보마다(basic·extreme × 1/5/20) 같은 기사를 여섯 번 다시 훑었다.
                //   그렇다고 무조건 위에서 한 번 돌리면, 아직 채점된 예보가 하나도 없는 기록까지
                //   규칙을 돌리게 된다 - 쌓이는 기록은 대부분 그 상태다. 첫 쓸모가 생길 때 센다.
                Dictionary<string, int> said = null;

                foreach (XmlElement f in root.SelectNodes("forecast"))
                {
                    // AI 는 규칙으로 쪼갤 수 없다. 규칙 예측만 센다.
                    string model = f.GetAttribute("model");
                    if (model != "basic" && model != "extreme-rule") continue;
                    string horizon = f.GetAttribute("horizon");
                    var score = Load(path + "." + model + "." + horizon + ".score.xml");
                    if (score == null || score.DocumentElement == null) continue;
                    double actual = Num(score.DocumentElement, "actual"), band = Num(score.DocumentElement, "threshold");
                    if (double.IsNaN(actual) || actual <= 0 || double.IsNaN(band)) continue;
                    DateTime due;
                    if (!DateTime.TryParse(f.GetAttribute("due"), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out due)) continue;
                    int outcome = DollarAnalysis.DirectionAt(actual / anchor - 1, band);
                    if (said == null) said = SaidOf(root, target);
                    cases.Add(new LedgerCase { Horizon = horizon, Created = created, Due = due,
                        Outcome = outcome, Version = version, Said = said });
                }
            }
            if (cases.Count == 0) return new List<RuleRow>();

            // ★ 겹치는 기간은 독립된 표본이 아니다 ★
            //   20일 예측을 스무 날 이어서 내면 결과 구간이 거의 같은 스무 건이 된다.
            //   그것을 스무 건으로 세면 '결론을 낼 만큼 쌓였다' 는 판단이 크게 낙관적이 된다.
            //   Summary() 는 이미 이 걸음을 밟는다. 장부에도 같은 잣대를 쓴다.
            //
            //   이 걸음은 basic 과 extreme-rule 이 같은 하루를 두 번 세는 것도 함께 막는다.
            //   둘은 화면에 보여 주는 방식만 다르고 같은 기사에서 같은 요인을 내므로,
            //   결과 구간이 완전히 같아 뒤엣것이 여기서 걸러진다.
            // ★ 기후값은 기간마다 따로다 ★
            //   1일과 20일은 문턱도 다르고 실제 방향의 분포도 다르다. 하나로 뭉치면 1일 건이
            //   압도적으로 많아 그 다수 방향이 20일 건의 기후값으로 쓰이고, 우위가 부풀거나 눌린다.
            var kept = new List<LedgerCase>();
            var commonByHorizon = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var group in cases.GroupBy(c => c.Horizon))
            {
                DateTime end = DateTime.MinValue;
                var mine = new List<LedgerCase>();
                foreach (var c in group.OrderBy(c => c.Created))
                {
                    if (c.Created < end) continue;
                    mine.Add(c); end = c.Due;
                }
                if (mine.Count == 0) continue;
                kept.AddRange(mine);
                commonByHorizon[group.Key] = mine.GroupBy(c => c.Outcome).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            }
            if (kept.Count == 0) return new List<RuleRow>();

            foreach (var row in kept)
            {
                int common = commonByHorizon[row.Horizon];
                foreach (var pair in row.Said)
                {
                    RuleRow r;
                    if (!rows.TryGetValue(pair.Key, out r)) { r = new RuleRow { Rule = pair.Key }; rows[pair.Key] = r; }
                    r.Fired++;
                    if (pair.Value == row.Outcome) r.Hits++;
                    if (common == row.Outcome) r.BaselineHits++;
                    // 어떤 '잣대' 아래에서 나온 숫자들인지가 알고 싶은 것이다. 판올림 목록이 아니다.
                    if (!string.IsNullOrEmpty(row.Version)) r.Versions.Add(Config.ScoringEra(row.Version));
                }
            }
            return rows.Values.OrderByDescending(r => r.Fired).ThenBy(r => r.Rule, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// 장부를 사람이 읽을 글로. ★ 결론을 낼 수 없으면 그렇다고 말한다 ★
        /// 표본이 모자란 규칙의 적중률을 그대로 적으면, 보는 사람은 그것을 성적으로 읽는다.
        /// </summary>
        internal static string Report(List<RuleRow> rows)
        {
            if (rows == null || rows.Count == 0) return "규칙별 장부: 채점된 규칙 예측이 아직 없습니다.";
            var ready = rows.Where(r => r.Enough).ToList();
            string text = "규칙별 장부: 채점에 참여한 규칙 " + rows.Count + "종 · 결론을 낼 만큼 쌓인 것 " +
                ready.Count + "종(" + MinimumFires + "건 이상).\n";
            foreach (var r in rows.Take(8))
            {
                text += "  " + r.Rule + " " + r.Fired + "건";
                if (!r.Enough)
                {
                    text += " · 표본 부족(" + MinimumFires + "건 필요)\n";
                    continue;
                }
                text += string.Format(CultureInfo.InvariantCulture, " · 적중 {0:0.0}% / 기후값 {1:0.0}% · 우위 {2:+0.0;-0.0;0.0}%p",
                    r.HitRate * 100, r.BaselineRate * 100, r.Edge * 100);
                text += r.Needed == int.MaxValue ? " · 우위가 없어 표본을 늘려도 갈리지 않습니다\n"
                    : r.Needed > r.Fired ? " · 우연과 가르려면 " + r.Needed + "건 필요(지금 " + r.Fired + ")\n"
                    : " · 표본이 충분해 우연으로 보기 어렵습니다\n";
            }
            if (rows.Count > 8) text += "  … 그 밖 " + (rows.Count - 8) + "종\n";
            text += "이 장부는 규칙을 자동으로 켜거나 끄지 않습니다. 새 규칙을 넣기 전에 여기서 자리를 확인하세요.\n";
            return text;
        }
    }
}
