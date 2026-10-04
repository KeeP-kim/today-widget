using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace DeskWidget
{
    // One immutable request body and one reference time for the Sol analysis.
    internal sealed class AnalysisInput
    {
        internal readonly DollarAnalysisResult Source;
        internal readonly DateTime AsOfUtc;
        internal readonly List<DollarNews> News;
        internal readonly string MarketSnapshot, FeedbackSnapshot, PolicySnapshot, Prompt, Schema, Id;
        internal AnalysisInput(DollarAnalysisResult source, DateTime now, bool freezeArticles = true, bool includeReview = false)
        {
            Source = source.Snapshot(); AsOfUtc = now;
            Source.Spark = null;
            if (freezeArticles) Source.News = source.News.Select(CloneArticle).ToList();
            if (freezeArticles) Source.MarketNews = source.MarketNews.Select(CloneArticle).ToList();
            Source.Rates = source.Rates.Select(CloneRate).ToList();
            Source.Patterns = source.Patterns.Select(p => p.Snapshot()).ToList();
            Source.Pattern = Source.Patterns.FirstOrDefault(p => source.Pattern != null && p.Horizon == source.Pattern.Horizon) ??
                (source.Pattern == null ? null : source.Pattern.Snapshot());
            if (source.Context != null) Source.Context = new MarketContext {
                DollarIndex = source.Context.DollarIndex.Select(CloneRate).ToList(),
                Yield10Y = source.Context.Yield10Y.Select(CloneRate).ToList(), Yield2Y = source.Context.Yield2Y.Select(CloneRate).ToList() };
            News = DollarSpark.SelectNews(Source, now);
            if (!News.Any(n => DollarSpark.IsCurrent(n, now))) throw new InvalidOperationException("AI에 전달할 최신 기사가 없습니다");
            MarketSnapshot = DollarSpark.MarketInput(Source, now);
            FeedbackSnapshot = includeReview ? ForecastReview.Input(Source.Quote, now) : "null";
            PolicySnapshot = PolicyContext.Input(Source.Target, now);
            Prompt = DollarSpark.BuildPrompt(Source, News, now, MarketSnapshot, FeedbackSnapshot, PolicySnapshot);
            Schema = DollarSpark.Schema(Source);
            using (var hash = SHA256.Create())
                Id = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(DollarSpark.ReasoningEffort + "\n" + Schema + "\n" + Prompt))).Replace("-", "").ToLowerInvariant();
        }
        private static DollarRate CloneRate(DollarRate r) { return new DollarRate { Date = r.Date, Value = r.Value }; }
        private static DollarNews CloneArticle(DollarNews n)
        {
            return new DollarNews { Target = n.Target, Title = n.Title, Source = n.Source, Url = n.Url, Topic = n.Topic,
                Context = n.Context, PublishedUtc = n.PublishedUtc, Domestic = n.Domestic, BodyRead = n.BodyRead,
                Direction = n.Direction, EvidenceDirection = n.EvidenceDirection, EvidenceWeight = n.EvidenceWeight, EvidenceReason = n.EvidenceReason };
        }
    }

    internal sealed class AnalysisRun
    {
        internal string Model, InputId, Outcome, Error;
        internal double Seconds;
        internal DollarSparkResult Result;
    }

    internal static class ModelComparison
    {
        internal static string Folder { get { return Path.Combine(Program.BaseDir, "analysis-runs"); } }
        internal static void SaveRuns(AnalysisInput input, IList<AnalysisRun> runs, DateTime finished)
        {
            if (input == null || runs.Count == 0) return;
            var doc = new XmlDocument(); var root = doc.CreateElement("analysisRuns"); doc.AppendChild(root);
            root.SetAttribute("schema", "1"); root.SetAttribute("version", Config.AppVersion);
            root.SetAttribute("identity", input.Source.Quote == null ? input.Source.Target.Key : input.Source.Quote.IdentityKey ?? input.Source.Target.Key);
            root.SetAttribute("source", input.Source.Quote == null ? "" : input.Source.Quote.Source ?? "");
            root.SetAttribute("inputId", input.Id); root.SetAttribute("asOf", input.AsOfUtc.ToString("o"));
            root.SetAttribute("finished", finished.ToString("o")); root.SetAttribute("effort", DollarSpark.ReasoningEffort);
            root.SetAttribute("articles", input.News.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var run in runs) {
                var e = doc.CreateElement("run"); e.SetAttribute("model", run.Model); e.SetAttribute("outcome", run.Outcome);
                e.SetAttribute("seconds", run.Seconds.ToString("R", CultureInfo.InvariantCulture));
                root.AppendChild(e);
            }
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".xml"), temp = path + ".tmp";
            try {
                using (var writer = XmlWriter.Create(temp, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) doc.Save(writer);
                File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal static string Current(AnalysisInput input, IList<AnalysisRun> runs)
        {
            if (input == null || runs == null || runs.Count == 0) return "분석 실행 기록 · 갱신 후 표시합니다";
            var lines = new List<string> { "분석 기사·시세 · " + input.News.Count + "건 · 기준 " + input.AsOfUtc.AddHours(9).ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) + " · High" };
            foreach (var run in runs) {
                string line = DollarSpark.ModelName(run.Model) + " · " + run.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + "초 · ";
                if (run.Result == null) line += OutcomeName(run.Outcome) + (string.IsNullOrEmpty(run.Error) ? "" : " · " + run.Error);
                else {
                    var view = input.Source.Snapshot(); view.Spark = run.Result;
                    line += string.Join(" / ", new[] { 1, 5, 20 }.Select(h => input.Source.Target.Steps(h) +
                        (input.Source.Target.Def.Kind == SourceKind.Coin || input.Source.Target.Economic ? "일 " : "거래일 ") +
                        DollarAnalysisWindow.ScenarioOutlook(view, h, input.AsOfUtc)));
                }
                lines.Add(line);
            }
            lines.Add("적중률은 예측 기간이 끝난 뒤 채점합니다.");
            return string.Join("\n", lines);
        }
        private static string OutcomeName(string outcome)
        { return outcome == null ? "진행 중" : outcome == "success" ? "완료" : outcome == "timeout" ? "시간 초과" : outcome == "cancelled" ? "취소" : "실패"; }

        internal static string Summary(Quote quote)
        {
            if (quote == null || !Directory.Exists(Folder)) return "분석 속도·실패율 · 기록 대기";
            var groups = new Dictionary<string, List<Tuple<string, double>>>(); int invalid = 0;
            foreach (string path in Directory.GetFiles(Folder, "*.xml")) {
                try {
                    if (new FileInfo(path).Length > 65536) { invalid++; continue; }
                    var doc = new XmlDocument { XmlResolver = null };
                    using (var reader = XmlReader.Create(path, new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 65536 })) doc.Load(reader);
                    var root = doc.DocumentElement;
                    if (root == null || root.Name != "analysisRuns" || root.GetAttribute("schema") != "1") { invalid++; continue; }
                    if (root.GetAttribute("identity") != quote.IdentityKey || root.GetAttribute("source") != (quote.Source ?? "")) continue;
                    if (root.GetAttribute("effort") != "high") { invalid++; continue; }
                    foreach (XmlElement run in root.SelectNodes("run")) {
                        string model = run.GetAttribute("model"), outcome = run.GetAttribute("outcome"); double seconds;
                        if (!DollarSpark.HistoricalModel(model) || !new[] { "success", "error", "timeout", "cancelled" }.Contains(outcome) ||
                            !double.TryParse(run.GetAttribute("seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) ||
                            double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 3600) { invalid++; continue; }
                        if (!groups.ContainsKey(model)) groups[model] = new List<Tuple<string, double>>();
                        groups[model].Add(Tuple.Create(outcome, seconds));
                    }
                } catch (IOException) { invalid++; } catch (UnauthorizedAccessException) { invalid++; } catch (XmlException) { invalid++; }
            }
            var lines = new List<string> { "전체 실행 기록 · High · 입력량이 다른 조회 포함 · 속도는 서버 상태에 따라 달라집니다" };
            foreach (var model in DollarSpark.ActiveModels.Where(groups.ContainsKey)) {
                var rows = groups[model]; int cancelled = rows.Count(r => r.Item1 == "cancelled"), completed = rows.Count - cancelled;
                int failed = rows.Count(r => r.Item1 == "error" || r.Item1 == "timeout");
                var success = rows.Where(r => r.Item1 == "success").ToList();
                lines.Add(DollarSpark.ModelName(model) + " · 성공 " + success.Count + "/" + completed +
                    " · 실패 " + (completed == 0 ? "—" : (100.0 * failed / completed).ToString("0", CultureInfo.InvariantCulture) + "%") +
                    "(시간 초과 " + rows.Count(r => r.Item1 == "timeout") + ") · 취소 " + cancelled +
                    " · 성공 평균 " + (success.Count == 0 ? "—" : success.Average(r => r.Item2).ToString("0.0", CultureInfo.InvariantCulture) + "초") +
                    " · 전체 대기 평균 " + rows.Average(r => r.Item2).ToString("0.0", CultureInfo.InvariantCulture) + "초");
            }
            return groups.Count == 0 ? "분석 속도·실패율 · 기록 대기" : string.Join("\n", lines) + (invalid == 0 ? "" : "\n읽을 수 없는 실행 기록 " + invalid + "건 제외");
        }
    }
}
