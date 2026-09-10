using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Xml;
using System.Collections.Generic;

namespace DeskWidget
{
    // Append-only snapshots. Observations are separate: forecasts are never rewritten with future data.
    internal static class PredictionJournal
    {
        private static readonly object Gate = new object();
        internal static string Folder { get { return Path.Combine(Program.BaseDir, "prediction-history"); } }
        internal static DateTime Due(DateTime now, PredictionTarget target, int horizon)
        {
            if (target.Def.Kind == SourceKind.Coin) return now.AddDays(target.Steps(horizon));
            // ★ 요일은 거래소가 있는 곳의 요일이다 ★
            //   미국 정규장(13:30~20:00Z)의 뒷부분은 한국 시각으로 이미 다음날이라, 한국 요일로
            //   세면 목·금 오후장 예측의 만기가 일요일에 떨어져 영영 채점되지 않았다.
            //   미국 장은 UTC 자정을 넘지 않으므로 UTC 요일이면 된다.
            int shiftHours = target.Def.Kind == SourceKind.WorldStock ? 0 : 9;
            int remaining = horizon;
            while (remaining > 0) {
                now = now.AddDays(1); var day = now.AddHours(shiftHours).DayOfWeek;
                if (day != DayOfWeek.Saturday && day != DayOfWeek.Sunday) remaining--;
            }
            return now;
        }
        private static string S(double x) { return x.ToString("R", CultureInfo.InvariantCulture); }
        private static double N(XmlElement e, string key) { return double.Parse(e.GetAttribute(key), CultureInfo.InvariantCulture); }
        /// <summary>없거나 망가진 값이면 NaN. 없는 속성 하나로 채점이 통째로 멈추면 안 된다.</summary>
        private static double Maybe(XmlElement e, string key)
        {
            double v;
            return e != null && double.TryParse(e.GetAttribute(key), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                ? v : double.NaN;
        }
        private static DateTime T(XmlElement e, string key) { return DateTime.Parse(e.GetAttribute(key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }
        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        /// <summary>그 기록이 만들어질 때 쓰던 문턱. 없으면(스키마 2 이전) 지금 값으로 떨어진다.</summary>
        private static double StoredThreshold(XmlElement forecast, int horizon)
        {
            // ★ 스키마 2 기록에는 threshold 가 아예 없다 ★
            //   N 은 없는 값을 만나면 예외를 던진다. 그 예외가 Observe 를 통째로 끊어서,
            //   옛 기록이 하나만 섞여 있어도 그날 채점이 전부 멈췄다. 실측: 예측 69건 중
            //   채점 0건. 없으면 없는 대로 그 기간의 기본 문턱을 쓴다.
            double t = Maybe(forecast, "threshold");
            return Finite(t) && t > 0 && t < 1 ? t : DollarAnalysis.Threshold(horizon);
        }
        /// <summary>
        /// 채점에 쓸 수 있는 시세인가.
        /// ★ 환율은 휴일에도 받아진다 ★ - 네이버가 전날 고시환율을 돌려주고 ReceivedUtc 만 새것이라,
        /// 거래일을 보지 않으면 멈춘 값으로 채점한다. 거래일을 아는 시세는 오늘 것일 때만 쓴다.
        /// </summary>
        internal static bool Fresh(Quote q, DateTime now)
        {
            if (q == null || !q.Ok || q.MarketClosed || q.Time == "장마감" || string.IsNullOrEmpty(q.IdentityKey)) return false;
            if (!Finite(PredictionTarget.Number(q)) || PredictionTarget.Number(q) <= 0) return false;
            if (q.ReceivedUtc > now || (now - q.ReceivedUtc).TotalMinutes > 5) return false;
            if (q.TradedDate != DateTime.MinValue && q.TradedDate != DollarAnalysis.KoreaDate(now)) return false;
            return true;
        }
        /// <summary>
        /// 이 기록이 그 품목·그 피드의 것인가. 문서를 통째로 올리기 전에 뿌리 속성만 본다.
        ///
        /// ★ 남의 기록을 열어 보고 나서 남의 것인 줄 알면 늦다 ★
        ///   기록 한 건은 기사 본문 전건과 3년치 환율이 들어가 수백 KB 다. 종목이 다섯이면
        ///   같은 파일을 1분에 다섯 번 통째로 파싱했고, 그중 넷은 열자마자 버릴 것이었다.
        ///   XmlReader 는 앞부분만 흘려 읽으므로 파일 크기와 무관하게 싸다.
        /// </summary>
        private static bool HeaderMatches(string path, string identity, string source)
        { bool readable; return HeaderMatches(path, identity, source, out readable); }
        private static bool HeaderMatches(string path, string identity, string source, out bool readable)
        {
            readable = true;
            try {
                using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) {
                    if (!reader.ReadToFollowing("prediction")) { readable = false; return false; }
                    // ★ XmlReader 는 없는 속성에 null 을 준다 ★ XmlElement 는 "" 를 줬다.
                    //   그대로 비교하면 source 속성이 아예 없는 기록이 조용히 빠진다.
                    return (reader.GetAttribute("identity") ?? "") == (identity ?? "")
                        && (reader.GetAttribute("source") ?? "") == (source ?? "");
                }
            } catch (XmlException) { readable = false; return false; }
              catch (IOException) { readable = false; return false; }
              catch (UnauthorizedAccessException) { readable = false; return false; }
        }
        private static XmlDocument Read(string path)
        {
            try {
                if (new FileInfo(path).Length > 4194304) return null;
                var doc = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4194304 })) doc.Load(reader);
                return doc;
            } catch (XmlException) { return null; } catch (IOException) { return null; }
        }
        private static void Save(XmlDocument doc, string path)
        {
            string temp = path + ".tmp";
            // ★ 넘어진 자리를 치우고 넘어진다 ★
            //   디스크가 차거나 도중에 앱이 죽으면 .tmp 가 남는데, 이름이 Guid 라 아무도 다시
            //   쓰지 않고 *.xml 검색에도 안 잡혀 보이지 않게 쌓인다.
            try {
                using (var writer = XmlWriter.Create(temp, new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), Indent = true })) doc.Save(writer);
                File.Move(temp, path);
            } catch { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } throw; }
        }
        private static bool _swept;
        /// <summary>지난 실행에서 남은 .tmp 를 실행당 한 번 치운다. 한 시간 넘은 것만 - 지금 쓰는 중일 수 있다.</summary>
        private static void SweepTemps(DateTime now)
        {
            if (_swept) return;
            _swept = true;
            try {
                foreach (string temp in Directory.GetFiles(Folder, "*.tmp"))
                    try { if (File.GetLastWriteTimeUtc(temp) < now.AddHours(-1)) File.Delete(temp); }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        internal static void Record(DollarAnalysisResult r, DateTime now)
        {
            if (r == null || r.Target.Weather || r.Target.Economic || !Fresh(r.Quote, now) ||
                !(r.Quote.IdentityKey == r.Target.Key || r.Target.Def.Kind == SourceKind.Fx && (r.Quote.IdentityKey == r.Target.Key + "|HANA" || r.Quote.IdentityKey == r.Target.Key + "|SHB"))) return;
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                SweepTemps(now);
                var doc = new XmlDocument(); var root = doc.CreateElement("prediction"); doc.AppendChild(root);
                // ★ 스키마 3 - 기록만으로 점수와 근거를 되살릴 수 있게 넓혔다 ★
                //   2 로 남긴 옛 기록도 계속 읽는다(ValidRecord). 스키마를 올리면서
                //   옛 기록을 버리면 애써 쌓은 표본이 통째로 사라진다.
                root.SetAttribute("schema", "3"); root.SetAttribute("version", Config.AppVersion); root.SetAttribute("created", now.ToString("o"));
                root.SetAttribute("identity", r.Quote.IdentityKey); root.SetAttribute("source", r.Quote.Source ?? "");
                root.SetAttribute("unit", r.Target.Unit(r.Quote)); root.SetAttribute("anchor", S(PredictionTarget.Number(r.Quote)));
                root.SetAttribute("quoteReceived", r.Quote.ReceivedUtc.ToString("o"));
                root.SetAttribute("evaluation", "coin: calendar 1/7/30; others: weekdays 1/5/20, holidays not adjusted; first same-feed receipt within 6h; receipt time is not exchange time");
                // 신뢰도를 되살릴 입력값. 이게 없어서 저장 점수를 XML 만으로 재현할 수 없었다
                // (감사 실측: 피드 9/11 을 가정해야 소수점까지 맞았다).
                root.SetAttribute("domestic", r.DomesticAvailable ? "1" : "0");
                root.SetAttribute("global", r.GlobalAvailable ? "1" : "0");
                root.SetAttribute("topicFeeds", r.TopicFeedsAvailable.ToString(CultureInfo.InvariantCulture) + "/" + r.TopicFeedsExpected.ToString(CultureInfo.InvariantCulture));
                foreach (var news in r.News.Where(n => n.PublishedUtc <= now)) {
                    var e = doc.CreateElement("article"); e.SetAttribute("url", news.Url ?? ""); e.SetAttribute("published", news.PublishedUtc.ToString("o"));
                    e.SetAttribute("source", news.Source ?? "");
                    // 제목만 읽은 기사인지 본문까지 읽었는지. 근거의 두께가 여기서 갈린다
                    // (감사 실측: 576기사 중 본문을 읽은 것은 7%).
                    e.SetAttribute("bodyRead", news.BodyRead ? "1" : "0");
                    e.SetAttribute("weight", S(DollarAnalysis.SourceWeight(news.Source)));
                    e.InnerText = DollarSpark.SourceText(news); root.AppendChild(e);
                }
                // ★ 숫자 맥락을 예측 시점 값으로 박아 둔다 ★
                //   나중에 "달러 강세가 이 품목의 방향과 상관이 있었나" 를 IC 로 재려면,
                //   그때 그 시점의 값이 있어야 한다. 지금 와서 과거 값을 다시 받아 채우면
                //   그건 미래를 아는 상태로 만든 자료다(v1.018 에서 없다고 확인한 그 문제).
                if (r.Context != null && r.Context.Ok) {
                    var e = doc.CreateElement("strength");
                    e.SetAttribute("basis", "USD vs " + string.Join("/", DollarAnalysis.StrengthQuotes) + " (ECB, geometric mean)");
                    e.SetAttribute("observations", r.Context.DollarIndex.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (int d in new[] { 1, 5, 20 }) {
                        double c = r.Context.ChangePercent(d);
                        if (Finite(c)) e.SetAttribute("d" + d.ToString(CultureInfo.InvariantCulture), S(c));
                    }
                    root.AppendChild(e);
                }
                // 국채금리도 그 시점 값으로 박아 둔다. 나중에 이 신호가 쓸모 있었는지
                // 되짚을 때, 지나서 다시 받아 채운 값으로 재면 미래를 아는 상태가 된다.
                if (r.Context != null && r.Context.YieldOk) {
                    var e = doc.CreateElement("yield");
                    e.SetAttribute("source", "US Treasury daily par yield curve");
                    var last = r.Context.Yield10Y[r.Context.Yield10Y.Count - 1];
                    e.SetAttribute("asOf", last.Date.ToString("yyyy-MM-dd"));
                    e.SetAttribute("y10", S(last.Value));
                    foreach (int d in new[] { 1, 5, 20 }) {
                        double c = r.Context.YieldChange(d);
                        if (Finite(c)) e.SetAttribute("d" + d.ToString(CultureInfo.InvariantCulture), S(c));
                    }
                    double spread = r.Context.CurveSpread();
                    if (Finite(spread)) e.SetAttribute("spread", S(spread));
                    root.AppendChild(e);
                }
                foreach (var rate in r.Rates.Where(p => p.Date <= now.Date)) {
                    var e = doc.CreateElement("rate"); e.SetAttribute("date", rate.Date.ToString("yyyy-MM-dd")); e.SetAttribute("value", S(rate.Value)); root.AppendChild(e);
                }
                var basic = r.ForStyle(false);
                foreach (var current in r.Spark == null ? (r.Extreme ? new[] { basic, r } : new[] { basic }) : new[] { basic, r })
                foreach (int h in new[] { 1, 5, 20 }) {
                    var p = current.Patterns.FirstOrDefault(x => x.Horizon == h) ?? (current.Pattern != null && current.Pattern.Horizon == h ? current.Pattern : null);
                    if (p == null || !DollarAnalysis.Fresh(p, now)) continue;
                    var score = DollarAnalysis.Score(current, h, now);
                    double value = PredictionTarget.Number(r.Quote) * (1 + DollarAnalysis.ForecastReturn(p, score));
                    if (!Finite(value) || value <= 0 || (current.Spark != null && !score.IsAi)) continue;
                    var e = doc.CreateElement("forecast"); e.SetAttribute("model", current.Spark == null ? (current.Extreme ? "extreme-rule" : "basic") : DollarSpark.ModelId(current.Spark.ModelId));
                    e.SetAttribute("horizon", h.ToString()); e.SetAttribute("value", S(value)); e.SetAttribute("score", S(score.Value));
                    e.SetAttribute("due", Due(now, r.Target, h).ToString("o"));
                    e.SetAttribute("style", current.Extreme ? "extreme" : "basic");
                    e.SetAttribute("threshold", S(DollarAnalysis.Threshold(h, r.RoundTripPercent)));
                    // 점수를 이루는 성분. 합계만 남기면 나중에 어디가 틀렸는지 되짚을 수 없다.
                    e.SetAttribute("up", S(score.UpEvidence)); e.SetAttribute("down", S(score.DownEvidence));
                    e.SetAttribute("history", S(score.History)); e.SetAttribute("reliability", S(score.Reliability));
                    e.SetAttribute("directional", score.DirectionalCount.ToString(CultureInfo.InvariantCulture));
                    e.SetAttribute("articles", score.ArticleCount.ToString(CultureInfo.InvariantCulture));
                    e.SetAttribute("reviewed", score.ReviewedCount.ToString(CultureInfo.InvariantCulture));
                    if (current.Spark != null) {
                        var reason = current.Spark.Periods.First(x => x.Horizon == h);
                        e.InnerText = reason.Reason + "\n반박: " + reason.Counter + "\n전환: " + reason.Change;
                        e.SetAttribute("confidence", reason.Confidence ?? "");
                        e.SetAttribute("submitted", current.Spark.SubmittedCount.ToString(CultureInfo.InvariantCulture));
                        // ★ 인용을 기사 주소로 박아 둔다 ★
                        //   근거문의 (n) 은 프롬프트에 넣은 순서라, 저장 순서와 달라
                        //   기록만으로는 어느 기사인지 되짚을 수 없었다(감사 실측).
                        //   번호 대신 주소와 검증된 원문 발췌를 그대로 남긴다.
                        foreach (var c in reason.Citations) {
                            var cite = doc.CreateElement("cite");
                            cite.SetAttribute("model", DollarSpark.ModelId(current.Spark.ModelId));
                            cite.SetAttribute("horizon", h.ToString(CultureInfo.InvariantCulture));
                            cite.SetAttribute("url", c.News == null ? "" : (c.News.Url ?? ""));
                            cite.SetAttribute("role", c.Role ?? "");
                            cite.InnerText = c.Quote ?? "";
                            root.AppendChild(cite);
                        }
                    }
                    root.AppendChild(e);
                }
                if (root.SelectNodes("forecast").Count > 0) Save(doc, Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".xml"));
            }
        }
        internal static void Observe(Quote q, DateTime now)
        {
            if (!Fresh(q, now) || !Directory.Exists(Folder)) return;
            lock (Gate) foreach (string path in Directory.GetFiles(Folder, "*.xml")) {
                if (path.EndsWith(".score.xml", StringComparison.Ordinal)) continue;
                // 채점 창은 아무리 길어도 만기(최대 45일) + 6시간이다. 그보다 오래된 파일은
                // 열어 볼 이유가 없다. 기록은 계속 쌓이므로 이 걸음이 없으면 시세를 받을
                // 때마다 훑는 양이 끝없이 늘어난다.
                try { if (File.GetLastWriteTimeUtc(path) < now.AddDays(-60)) continue; }
                catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
                if (!HeaderMatches(path, q.IdentityKey, q.Source)) continue;
                var document = Read(path); if (document == null) continue; var root = document.DocumentElement;
                if (!ValidRecord(root)) continue;
                foreach (XmlElement f in root.SelectNodes("forecast")) {
                    // ★ 한 건이 넘어져도 나머지는 채점해야 한다 ★
                    //   전에는 기록 하나의 속성이 망가지면 예외가 Observe 밖으로 나가
                    //   그날의 나머지 기록이 전부 채점되지 않았다. 그리고 예측 기록은
                    //   한 번 채점 창(만기+6시간)을 놓치면 영영 채점할 수 없다.
                    try {
                    DateTime due = T(f, "due");
                    string output = path + "." + f.GetAttribute("model") + "." + f.GetAttribute("horizon") + ".score.xml";
                    if (File.Exists(output) || q.ReceivedUtc < due || q.ReceivedUtc > due.AddHours(6)) continue;
                    double anchor = N(root, "anchor"), predicted = N(f, "value"), actual = PredictionTarget.Number(q);
                    var doc = new XmlDocument(); var score = doc.CreateElement("score"); doc.AppendChild(score);
                    score.SetAttribute("schema", "2"); score.SetAttribute("source", q.Source ?? ""); score.SetAttribute("version", root.GetAttribute("version"));
                    score.SetAttribute("created", root.GetAttribute("created")); score.SetAttribute("due", f.GetAttribute("due"));
                    score.SetAttribute("anchor", S(anchor)); score.SetAttribute("forecast", S(predicted));
                    score.SetAttribute("identity", q.IdentityKey); score.SetAttribute("model", f.GetAttribute("model"));
                    score.SetAttribute("horizon", f.GetAttribute("horizon")); score.SetAttribute("actual", S(actual));
                    score.SetAttribute("received", q.ReceivedUtc.ToString("o"));
                    score.SetAttribute("error", S(Math.Abs(predicted - actual) / anchor * 100));
                    score.SetAttribute("baselineError", S(Math.Abs(anchor - actual) / anchor * 100));
                    double band = StoredThreshold(f, int.Parse(f.GetAttribute("horizon"), CultureInfo.InvariantCulture));
                    score.SetAttribute("threshold", S(band));
                    score.SetAttribute("hit", DollarAnalysis.DirectionAt(predicted / anchor - 1, band) == DollarAnalysis.DirectionAt(actual / anchor - 1, band) ? "1" : "0");
                    Save(doc, output);
                    } catch (FormatException) { } catch (OverflowException) { } catch (IOException) { }
                }
            }
        }
        /// <summary>
        /// 채점 창을 놓쳐 영영 채점할 수 없게 된 예측이 몇 건인가.
        ///
        /// ★ 보이지 않는 손실을 숫자로 보이게 한다 ★
        ///   채점은 신선한 시세가 있어야 하고, 시세는 위젯이 접혀 있으면 받지 않는다.
        ///   그래서 시세를 접어 두거나 최소화해 둔 시간만큼 표본을 잃는다. 예측은 채점
        ///   창(만기+6시간)을 한 번 놓치면 되살릴 수 없다 - 며칠 뒤 가격으로 매기면
        ///   그건 다른 것을 잰 것이다.
        ///   조회를 끈 것은 사용자의 선택이므로 몰래 켜지 않는다. 대신 그 대가를 적는다.
        /// </summary>
        internal static int Missed(string identity, string source, DateTime now)
        {
            if (string.IsNullOrEmpty(identity) || !Directory.Exists(Folder)) return 0;
            int missed = 0;
            lock (Gate) foreach (string path in Directory.GetFiles(Folder, "*.xml")) {
                if (path.EndsWith(".score.xml", StringComparison.Ordinal)) continue;
                // 여기에는 60일 컷을 두지 않는다 - 놓친 예측은 오래됐다고 없던 일이 되지 않는다.
                if (!HeaderMatches(path, identity, source)) continue;
                var document = Read(path); if (document == null) continue;
                var root = document.DocumentElement;
                if (!ValidRecord(root)) continue;
                foreach (XmlElement f in root.SelectNodes("forecast")) {
                    string output = path + "." + f.GetAttribute("model") + "." + f.GetAttribute("horizon") + ".score.xml";
                    if (File.Exists(output)) continue;
                    DateTime due;
                    try { due = T(f, "due"); } catch (FormatException) { continue; }
                    if (due.AddHours(6) < now) missed++;
                }
            }
            return missed;
        }

        private static bool ValidRecord(XmlElement root)
        {
            try {
                // 스키마 2 는 v1.018 이전 기록이다. 계속 읽는다 - 버리면 표본이 사라진다.
                string schema = root.GetAttribute("schema");
                if (root == null || root.Name != "prediction" || (schema != "2" && schema != "3")) return false;
                double anchor = N(root, "anchor"); if (!Finite(anchor) || anchor <= 0) return false;
                DateTime created = T(root, "created");
                var seen = new HashSet<string>();
                foreach (XmlElement f in root.SelectNodes("forecast")) {
                    string model = f.GetAttribute("model"), h = f.GetAttribute("horizon");
                    if (!new[] { "basic", "extreme-rule", DollarSpark.Model, "gpt-6-astra" }.Contains(model) || !new[] { "1", "5", "20" }.Contains(h) || !seen.Add(model + h)) return false;
                    if (!Finite(N(f, "value")) || N(f, "value") <= 0 || T(f, "due") <= created || T(f, "due") > created.AddDays(45)) return false;
                }
                return seen.Count > 0;
            } catch (FormatException) { return false; } catch (OverflowException) { return false; }
        }
        /// <summary>
        /// IC(정보계수) - 예측한 변동과 실제 변동의 상관.
        ///
        /// ★ 방향 적중률보다 훨씬 빨리 결론이 난다 ★
        ///   적중률은 '얼마나 크게 맞혔나' 를 버리고 맞다/틀리다로만 세므로 정보를 잃는다.
        ///   50% 대 55% 를 가르려면 600건이 넘게 필요한데, 같은 신호를 IC 로 재면
        ///   수백 건 안쪽에서 갈린다. 하루 몇 건씩 쌓는 개인에게는 1년과 두 달의 차이다.
        ///   값 자체는 작다 - 이 바닥에서 0.02~0.05 면 쓸 만한 축이다.
        /// </summary>
        internal static double Correlation(List<double> x, List<double> y)
        {
            int n = Math.Min(x.Count, y.Count);
            if (n < 3) return double.NaN;
            double mx = 0, my = 0;
            for (int i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
            mx /= n; my /= n;
            double sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < n; i++) {
                double dx = x[i] - mx, dy = y[i] - my;
                sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
            }
            // 한쪽이 늘 같은 값이면 상관을 정의할 수 없다. 0 이 아니라 '모름' 이다.
            if (sxx <= 0 || syy <= 0) return double.NaN;
            return sxy / Math.Sqrt(sxx * syy);
        }

        /// <summary>이만한 IC 를 우연이 아니라고 말하려면 표본이 몇 건 필요한가(t=2 기준).</summary>
        internal static int NeededSamples(double ic)
        {
            if (double.IsNaN(ic) || Math.Abs(ic) < 0.0001) return int.MaxValue;
            double n = 4 / (ic * ic);
            return n >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(n);
        }

        /// <summary>예측한 변동과 실제 변동. 기준값이 다르므로 비율로 맞춰 비교한다.</summary>
        private static void Returns(List<Tuple<DateTime, DateTime, XmlElement, XmlElement>> rows,
            Func<Tuple<DateTime, DateTime, XmlElement, XmlElement>, XmlElement> pick,
            List<double> predicted, List<double> realized)
        {
            foreach (var row in rows) {
                var e = pick(row);
                double anchor = N(e, "anchor");
                if (!Finite(anchor) || anchor <= 0) continue;
                double f = N(e, "forecast"), a = N(e, "actual");
                if (!Finite(f) || !Finite(a)) continue;
                predicted.Add(f / anchor - 1);
                realized.Add(a / anchor - 1);
            }
        }

        internal static string Summary(Quote q)
        {
            if (q == null || !Directory.Exists(Folder)) return "예측 성적 · 기록 대기";
            lock (Gate) {
                // Only matched basic/AI forecasts from one snapshot, source and version are compared.
                var groups = new Dictionary<string, List<Tuple<DateTime, DateTime, XmlElement, XmlElement>>>();
                int pending = 0, invalid = 0;
                foreach (string path in Directory.GetFiles(Folder, "*.xml").Where(p => !p.EndsWith(".score.xml"))) {
                    // 성적은 쌓인 전부를 본다 - 60일 컷을 두면 오래된 표본이 조용히 사라진다.
                    // 대신 남의 품목이면 문서를 올리지 않고 헤더에서 끝낸다.
                    bool readable;
                    if (!HeaderMatches(path, q.IdentityKey, q.Source, out readable)) { if (!readable) invalid++; continue; }
                    var doc = Read(path); if (doc == null || !ValidRecord(doc.DocumentElement)) { invalid++; continue; }
                    var root = doc.DocumentElement;
                    foreach (XmlElement f in root.SelectNodes("forecast")) {
                        string model = f.GetAttribute("model"), h = f.GetAttribute("horizon");
                        if (model == "basic") continue;
                        var own = ReadScore(path + "." + model + "." + h + ".score.xml", root, f);
                        var basic = root.SelectSingleNode("forecast[@model='basic'][@horizon='" + h + "']") as XmlElement;
                        var other = basic == null ? null : ReadScore(path + ".basic." + h + ".score.xml", root, basic);
                        if (own == null || other == null || own.GetAttribute("received") != other.GetAttribute("received")) { pending++; continue; }
                        // ★ 판이 아니라 잣대 판으로 묶는다 ★
                        //   UI 를 고쳐 올린 판까지 칸을 가르면 표본이 영영 안 쌓인다(Config.ScoringEra).
                        string key = model + "|" + h + "|" + Config.ScoringEra(root.GetAttribute("version"));
                        if (!groups.ContainsKey(key)) groups[key] = new List<Tuple<DateTime, DateTime, XmlElement, XmlElement>>();
                        groups[key].Add(Tuple.Create(T(root, "created"), T(own, "received"), own, other));
                    }
                }
                var lines = new List<string>();
                foreach (var group in groups) {
                    DateTime end = DateTime.MinValue; var selected = new List<Tuple<DateTime, DateTime, XmlElement, XmlElement>>();
                    foreach (var row in group.Value.OrderBy(r => r.Item1)) { if (row.Item1 < end) continue; selected.Add(row); end = row.Item2; }
                    // ★ 화면에 없는 이름을 성적표에 찍지 않는다 ★
                    //   토글은 '참고 / AI 전망' 둘인데 여기만 '극단 규칙' 이라고 했다. 옛 이름이다.
                    //   extreme-rule 은 AI 전망을 켰는데 AI 응답을 못 받아 규칙으로 대신한 건이다.
                    //   기록 안의 값은 그대로 두고(옛 기록이 살아 있어야 한다) 부르는 이름만 맞춘다.
                    var key = group.Key.Split('|'); string name = key[0] == "extreme-rule" ? "AI 전망(규칙 대체)" : DollarSpark.ModelName(key[0]);
                    var mineP = new List<double>(); var mineR = new List<double>();
                    var baseP = new List<double>(); var baseR = new List<double>();
                    Returns(selected, r => r.Item3, mineP, mineR);
                    Returns(selected, r => r.Item4, baseP, baseR);
                    double ic = Correlation(mineP, mineR), icBase = Correlation(baseP, baseR);
                    // ★ 두 예측의 오차가 같이 움직이면 합쳐도 이득이 없다 ★
                    //   규칙과 AI 가 같은 기사 묶음을 먹으므로 오차가 강하게 상관될 소지가 크다.
                    //   상관이 높으면 결합은 초과 성능이 아니라 기저율 쪽 축소일 뿐이고,
                    //   그때 정직한 결론은 '예측을 줄이고 불확실성을 크게 표시하라' 다.
                    var errMine = new List<double>(); var errBase = new List<double>();
                    for (int i = 0; i < mineP.Count && i < baseP.Count && i < mineR.Count; i++) {
                        errMine.Add(mineP[i] - mineR[i]); errBase.Add(baseP[i] - mineR[i]);
                    }
                    double errorPair = Correlation(errMine, errBase);
                    string icText;
                    if (double.IsNaN(ic)) icText = "IC 계산 불가 · 표본 부족";
                    else {
                        int need = NeededSamples(ic);
                        icText = "IC " + ic.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) +
                            " / 기본 " + (double.IsNaN(icBase) ? "-" : icBase.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture)) +
                            " · " + (need == int.MaxValue ? "우연과 구분 불가"
                                : selected.Count >= need ? "우연이라 보기 어려움"
                                : "이 크기면 " + need + "건 필요 (지금 " + selected.Count + "건)");
                    }
                    if (!double.IsNaN(errorPair))
                        icText += "\n오차 상관 " + errorPair.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) +
                            (errorPair >= 0.8 ? " · 기본과 거의 같이 틀린다. 합쳐도 이득이 없다"
                                : errorPair >= 0.5 ? " · 기본과 상당 부분 같이 틀린다"
                                : " · 기본과 다른 방식으로 틀린다");
                    // 판이 아니라 '잣대 판' 이다. 1.044 를 쓰는데 '잣대 v1.040' 이 뜨는 것이 맞다.
                    lines.Add(name + " · " + (key[1] == "20" ? "월" : key[1] == "5" ? "주" : "일") + " · 잣대 v" + key[2] + " · " + selected.Count + "건\n방향 " +
                        (100 * selected.Average(r => N(r.Item3, "hit"))).ToString("0") + "% / 기본 " + (100 * selected.Average(r => N(r.Item4, "hit"))).ToString("0") + "% · 오차 " +
                        selected.Average(r => N(r.Item3, "error")).ToString("0.00") + "% / 기본 " + selected.Average(r => N(r.Item4, "error")).ToString("0.00") + "% / 유지 " + selected.Average(r => N(r.Item3, "baselineError")).ToString("0.00") + "%\n" + icText);
                }
                return "겹침 제외·동일 자료 비교 · 관측 성적, 확률 아님\n" + (lines.Count == 0 ? "비교 결과 대기" : string.Join("\n", lines)) +
                    (pending > 0 ? " · 미채점 " + pending + "건" : "") + (invalid > 0 ? " · 구형/손상 기록 제외 " + invalid + "건" : "");
            }
        }
        private static XmlElement ReadScore(string path, XmlElement root, XmlElement forecast)
        {
            if (!File.Exists(path)) return null; var doc = Read(path); if (doc == null) return null; var e = doc.DocumentElement;
            try {
                if (e == null || e.Name != "score" || e.GetAttribute("schema") != "2" || e.GetAttribute("identity") != root.GetAttribute("identity") || e.GetAttribute("source") != root.GetAttribute("source") ||
                    e.GetAttribute("version") != root.GetAttribute("version") || e.GetAttribute("model") != forecast.GetAttribute("model") || e.GetAttribute("horizon") != forecast.GetAttribute("horizon") || e.GetAttribute("created") != root.GetAttribute("created")) return null;
                double actual = N(e, "actual"), anchor = N(root, "anchor"), prediction = N(forecast, "value"); int h = int.Parse(forecast.GetAttribute("horizon"));
                if (!Finite(actual) || actual <= 0 || T(e, "received") < T(forecast, "due") || T(e, "received") > T(forecast, "due").AddHours(6)) return null;
                // Derive metrics from immutable forecast and outcome, never trust stored score percentages.
                e.SetAttribute("error", S(Math.Abs(prediction - actual) / anchor * 100)); e.SetAttribute("baselineError", S(Math.Abs(anchor - actual) / anchor * 100));
                double band = StoredThreshold(forecast, h);
                e.SetAttribute("hit", DollarAnalysis.DirectionAt(prediction / anchor - 1, band) == DollarAnalysis.DirectionAt(actual / anchor - 1, band) ? "1" : "0");
                return e;
            } catch (FormatException) { return null; } catch (OverflowException) { return null; }
        }
    }
}
