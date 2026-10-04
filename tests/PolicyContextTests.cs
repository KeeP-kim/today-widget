using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskWidget
{
    internal static class PolicyContextTests
    {
        private static int checks;
        private static void Check(bool value, string reason)
        { if (!value) throw new Exception("Policy context: " + reason); checks++; }
        private static IEnumerable<JNode> Items(JNode array)
        { return Enumerable.Range(0, array.Count).Select(i => array[i]); }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        private static JNode Event(JNode input, string name)
        { return Items(input["upcoming_events"]).SingleOrDefault(e => e["name"].S == name) ?? JNode.Empty; }
        private static DateTime Utc(string text)
        { return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal); }
        private static bool MappedToDue(JNode input, PredictionTarget target, DateTime now)
        {
            return Items(input["upcoming_events"]).All(e => {
                DateTime start = Utc(e["window_start_utc"].S);
                int[] expected = new[] { 1, 5, 20 }.Where(h => start <= PredictionJournal.Due(now, target, h)).ToArray();
                return Items(e["may_fall_in_horizons"]).Select(h => (int)h.D).SequenceEqual(expected);
            });
        }
        private static DollarAnalysisResult Current(PredictionTarget target, DateTime now)
        {
            var result = new DollarAnalysisResult { Target = target, CheckedUtc = now };
            result.News.Add(new DollarNews { Target = target, Title = "Synthetic current financial policy observation", Source = "Fixture source",
                Url = "https://news.google.com/articles/policy-context-fixture", PublishedUtc = now.AddMinutes(-1), Context = "This is a local test fixture, not an actual policy statement." });
            return result;
        }
        private static void Integration(PredictionTarget fx, PredictionTarget coin, DateTime reviewed, DateTime expiry)
        {
            var fxSource = Current(fx, reviewed); var coinSource = Current(coin, reviewed);
            var frozen = new AnalysisInput(fxSource, reviewed); var coinFrozen = new AnalysisInput(coinSource, reviewed);
            var fxJson = Json.Parse(DollarSpark.Input(frozen.Source, frozen.News, reviewed, frozen.MarketSnapshot, frozen.FeedbackSnapshot, frozen.PolicySnapshot));
            var coinJson = Json.Parse(DollarSpark.Input(coinFrozen.Source, coinFrozen.News, reviewed, coinFrozen.MarketSnapshot, coinFrozen.FeedbackSnapshot, coinFrozen.PolicySnapshot));
            Check(frozen.PolicySnapshot == PolicyContext.Input(fx, reviewed) && coinFrozen.PolicySnapshot == PolicyContext.Input(coin, reviewed) &&
                fxJson["policy_context"]["reviewed_utc"].S == reviewed.ToString("o") && coinJson["policy_context"]["references"].Count > 0 &&
                frozen.Prompt.Contains(frozen.PolicySnapshot) && coinFrozen.Prompt.Contains(coinFrozen.PolicySnapshot),
                "the frozen official policy snapshot was not delivered to the actual FX or DOGE input and prompt");
            Check(frozen.Prompt.Contains(DollarSpark.PolicyInstructions) && coinFrozen.Prompt.Contains(DollarSpark.PolicyInstructions) &&
                DollarSpark.BuildPrompt(frozen.Source, frozen.News, reviewed, frozen.MarketSnapshot, frozen.FeedbackSnapshot, frozen.PolicySnapshot) == frozen.Prompt,
                "the common policy safeguards or explicit frozen-context prompt overload were bypassed for a target");
            var laterSource = Current(fx, expiry);
            var carried = Json.Parse(DollarSpark.Input(laterSource, laterSource.News, expiry, frozen.MarketSnapshot, frozen.FeedbackSnapshot, frozen.PolicySnapshot));
            var refreshed = Json.Parse(DollarSpark.Input(laterSource, laterSource.News, expiry));
            Check(carried["policy_context"]["status"].S == "reviewed_snapshot" && carried["policy_context"]["reviewed_utc"].S == reviewed.ToString("o") &&
                carried["policy_context"]["references"].Count == fxJson["policy_context"]["references"].Count &&
                refreshed["policy_context"]["status"].S == "needs_refresh" && refreshed["policy_context"]["references"].Count == 0,
                "a captured comparison context was recalculated after expiry or a fresh request revived the expired edition");
            Check(frozen.FeedbackSnapshot == "null" && coinFrozen.FeedbackSnapshot == "null" &&
                !fxJson["prediction_review"].Exists && !coinJson["prediction_review"].Exists && !refreshed["prediction_review"].Exists,
                "adding public policy context enabled submission of private prediction history without opt-in");
            var fxMapping = Json.Parse(frozen.MarketSnapshot)["forecast_mapping"];
            var coinMapping = Json.Parse(coinFrozen.MarketSnapshot)["forecast_mapping"];
            Check(fxMapping.Count == 3 && coinMapping.Count == 3 &&
                Items(fxMapping).All(e => e["calendar_basis"].S == "weekdays_without_holidays" && e["holidays_adjusted"].S == "false") &&
                Items(coinMapping).All(e => e["calendar_basis"].S == "calendar_days" && e["holidays_adjusted"].S == "false") &&
                fxMapping[1]["steps"].D == 5 && coinMapping[1]["steps"].D == 7,
                "FX or coin input failed to disclose its actual weekday/calendar and holiday expiry basis");

            DateTime last = DollarAnalysis.KoreaDate(reviewed).AddDays(-2);
            for (int i = 0; i < 25; i++) fxSource.Context.DollarIndex.Add(new DollarRate { Date = last.AddDays(i - 24), Value = 100 + i });
            var strength = Json.Parse(DollarSpark.Input(fxSource, fxSource.News, reviewed))["dollar_strength"];
            Check(strength["last_completed_date"].S == last.ToString("yyyy-MM-dd") && strength["age_calendar_days"].D == 2 &&
                strength["stale"].S == "false" && strength["observations"].D == 25 &&
                strength["change_basis"].S == "provider observations, not calendar days" &&
                Math.Abs(strength["change_1d_percent"].D - Math.Round((124.0 / 123 - 1) * 100, 3)) < 1e-9,
                "dollar strength lost its completed observation date, age, observation basis or actual change");
            foreach (var point in fxSource.Context.DollarIndex) point.Date = point.Date.AddDays(-5);
            var sevenDays = Json.Parse(DollarSpark.Input(fxSource, fxSource.News, reviewed))["dollar_strength"];
            foreach (var point in fxSource.Context.DollarIndex) point.Date = point.Date.AddDays(-1);
            var eightDays = Json.Parse(DollarSpark.Input(fxSource, fxSource.News, reviewed))["dollar_strength"];
            Check(sevenDays["age_calendar_days"].D == 7 && sevenDays["stale"].S == "false" &&
                eightDays["age_calendar_days"].D == 8 && eightDays["stale"].S == "true" &&
                eightDays["last_completed_date"].S == last.AddDays(-6).ToString("yyyy-MM-dd"),
                "an old dollar-strength observation was refreshed by receipt time or its staleness boundary was changed");
            fxSource.Context.DollarIndex.RemoveAt(0);
            Check(!Json.Parse(DollarSpark.Input(fxSource, fxSource.News, reviewed))["dollar_strength"].Exists,
                "insufficient dollar-strength history was emitted as invented zero-valued current context");
        }

        internal static int Run(string work)
        {
            checks = 0;
            var fx = new PredictionTarget(null);
            var coin = new PredictionTarget(new SymbolDef(SourceKind.Coin, "KRW-DOGE", "도지코인"));
            DateTime reviewed = PolicyContext.ReviewedUtc, expiry = PolicyContext.ExpiresUtc;
            var before = Json.Parse(PolicyContext.Input(fx, reviewed.AddTicks(-1)));
            var input = Json.Parse(PolicyContext.Input(fx, reviewed));
            var expired = Json.Parse(PolicyContext.Input(fx, expiry));

            Check(!PolicyContext.Available(reviewed.AddTicks(-1)) && PolicyContext.Available(reviewed),
                "future reviewed sources became available before their verification time");
            Check(before.IsObject && before["status"].S == "not_yet_available" && before["references"].Count == 0 && before["upcoming_events"].Count == 0,
                "future policy facts or calendar entries leaked into an earlier as-of input");
            Check(PolicyContext.Available(expiry.AddTicks(-1)) && !PolicyContext.Available(expiry),
                "the seven-day snapshot expiry is not an exclusive boundary");
            Check(expired.IsObject && expired["status"].S == "needs_refresh" && expired["references"].Count == 0 && expired["upcoming_events"].Count == 0,
                "expired policy facts or future events survived without renewed verification");
            Check(input.IsObject && input["status"].S == "reviewed_snapshot" && input["reviewed_utc"].S == reviewed.ToString("o") &&
                input["expires_utc"].S == expiry.ToString("o") && expiry - reviewed == TimeSpan.FromDays(7),
                "snapshot verification and expiry metadata do not match the actual validity window");
            Check(new[] { before, input, expired }.All(n => n["live_refreshed"].S == "false" && n["coverage_complete"].S == "false" &&
                n["directional_scoring_allowed"].S == "false"), "a reviewed public snapshot was promoted to live complete directional evidence");

            var references = Items(input["references"]).ToList();
            Check(references.Count > 0 && references.All(r => r["temporal_role"].S == "background" && !r["numeric_fx_target"].Exists),
                "an official policy reference was assigned a current role or a fabricated FX target");
            Check(references.All(r => !string.IsNullOrEmpty(r["institution"].S) && !string.IsNullOrEmpty(r["country"].S) &&
                !string.IsNullOrEmpty(r["kind"].S) && !string.IsNullOrEmpty(r["summary"].S) && new Uri(r["url"].S).Scheme == "https"),
                "policy references lost their institution, stated status, explanation or original public source");
            Check(PolicyContext.References(reviewed).All(r => Net.IsAllowedLink(r.Url)) &&
                PolicyContext.Upcoming(reviewed).All(e => Net.IsAllowedLink(e.Url)),
                "a verified official reference or calendar link cannot be opened through the app's link policy");
            string treasury = PolicyContext.TreasuryCurrent;
            Check(!Net.IsAllowedLink(treasury.Replace("home.treasury.gov", "home.treasury.gov.invalid")) &&
                !Net.IsAllowedLink("https://home.treasury.gov/news/press-releases/not-reviewed") &&
                !Net.IsAllowedLink(treasury.Replace("https://", "http://")) &&
                !Net.IsAllowedLink(treasury.Replace("https://", "https://someone@")),
                "official source exceptions admitted an unreviewed path, lookalike host, HTTP or userinfo link");
            Check(references.All(r => Utc(r["source_date"].S) <= reviewed) && references.Any(r => Utc(r["source_date"].S) < reviewed.AddDays(-7)),
                "source publication dates were replaced with review time or historical policy context was silently omitted");
            var later = Items(Json.Parse(PolicyContext.Input(fx, expiry.AddTicks(-1)))["references"]).ToList();
            Check(later.Count == references.Count && references.Zip(later, (a, b) => a["url"].S == b["url"].S &&
                a["source_date"].S == b["source_date"].S && b["temporal_role"].S == "background").All(v => v),
                "refreshing the same edition changed publication dates or revived old policy as current news");

            var events = Items(input["upcoming_events"]).ToList();
            Check(events.Count > 0 && events.All(e => !e["result"].Exists && !e["consensus"].Exists &&
                e["status"].S == "scheduled_unconfirmed_result"), "an upcoming release acquired an invented result or market consensus");
            Check(events.All(e => new Uri(e["url"].S).Scheme == "https" && !string.IsNullOrEmpty(e["name"].S) &&
                Utc(e["window_start_utc"].S) <= Utc(e["window_end_utc"].S)), "calendar source or event time window was lost");
            var minutes = Event(input, "연준 9월 FOMC 의사록");
            DateTime minutesUtc = new DateTime(2026, 10, 7, 18, 0, 0, DateTimeKind.Utc);
            Check(minutes["time_precision"].S == "minute" && Utc(minutes["scheduled_utc"].S) == minutesUtc &&
                Utc(minutes["window_start_utc"].S) == minutesUtc && Utc(minutes["window_end_utc"].S) == minutesUtc,
                "a verified exact release time was changed into an uncertain date or a widened window");
            Check(PolicyContext.Upcoming(reviewed).Single(e => e.Name == "연준 9월 FOMC 의사록").Display.StartsWith("10-08 03:00 한국시간", StringComparison.Ordinal),
                "the October 7 US release was displayed on the wrong Korean date or hour");
            Check(PolicyContext.Upcoming(reviewed).Single(e => e.Name == "한국 8월 국제수지(잠정)").Display.StartsWith("10-08 08:00 한국시간", StringComparison.Ordinal),
                "the Korean release UTC-to-KST conversion lost its next-day boundary");
            Check(!Event(Json.Parse(PolicyContext.Input(fx, minutesUtc)), "연준 9월 FOMC 의사록").Exists &&
                !Event(Json.Parse(PolicyContext.Input(fx, minutesUtc.AddTicks(1))), "연준 9월 FOMC 의사록").Exists,
                "a release at or before the current as-of time was still presented as upcoming");

            var ism = Event(input, "미국 ISM 서비스업 PMI");
            var bok = Event(input, "한국은행 통화정책방향 회의");
            Check(ism["time_precision"].S == "local_date_only" && !ism["scheduled_utc"].Exists && ism["local_date"].S == "2026-10-05" &&
                ism["local_zone"].S == "미국 동부", "a date-only US calendar entry was assigned an unverified release clock");
            Check(bok["time_precision"].S == "local_date_only" && !bok["scheduled_utc"].Exists && bok["local_date"].S == "2026-10-22" &&
                bok["local_zone"].S == "한국", "a date-only BOK meeting was assigned an unverified decision clock");
            DateTime ismStart = new DateTime(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc), ismEnd = ismStart.AddDays(1);
            Check(Utc(ism["window_start_utc"].S) == ismStart && Utc(ism["window_end_utc"].S) == ismEnd &&
                Utc(bok["window_start_utc"].S) == new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc),
                "verified local dates were translated to the wrong US Eastern or Korean UTC day window");
            Check(Event(Json.Parse(PolicyContext.Input(fx, ismStart.AddHours(12))), "미국 ISM 서비스업 PMI").Exists &&
                !Event(Json.Parse(PolicyContext.Input(fx, ismEnd)), "미국 ISM 서비스업 PMI").Exists,
                "a date-only event was dropped before its possible release window ended or retained after it ended");

            Check(MappedToDue(input, fx, reviewed) && MappedToDue(Json.Parse(PolicyContext.Input(coin, reviewed)), coin, reviewed),
                "the FX or coin event horizon mapping does not use that target's actual expiry dates");
            var coinInput = Json.Parse(PolicyContext.Input(coin, reviewed));
            Check(!Items(ism["may_fall_in_horizons"]).Select(h => (int)h.D).Except(new[] { 1, 5, 20 }).Any() &&
                Items(ism["may_fall_in_horizons"]).Any(h => h.D == 1) &&
                Items(minutes["may_fall_in_horizons"]).Select(h => (int)h.D).SequenceEqual(new[] { 5, 20 }) &&
                Items(Event(coinInput, "한국은행 통화정책방향 회의")["may_fall_in_horizons"]).Select(h => (int)h.D).SequenceEqual(new[] { 20 }),
                "daily, weekly or monthly policy risk was assigned to the wrong forecast horizon");
            var friday = new DateTime(2026, 10, 9, 4, 0, 0, DateTimeKind.Utc);
            Check(PredictionJournal.Due(friday, fx, 1) == friday.AddDays(3) && PredictionJournal.Due(friday, coin, 1) == friday.AddDays(1) &&
                PredictionJournal.Due(reviewed, fx, 5) == reviewed.AddDays(5) && PredictionJournal.Due(reviewed, coin, 5) == reviewed.AddDays(7) &&
                PredictionJournal.Due(new DateTime(2026, 10, 8, 4, 0, 0, DateTimeKind.Utc), fx, 1) == friday,
                "policy calendar integration silently changed the existing weekend or unadjusted-holiday expiry basis");
            Check(PolicyContext.Input(new PredictionTarget(new SymbolDef(SourceKind.Ecos, "INTL:US", "미국 금리")), reviewed) == "null" &&
                PolicyContext.Input(new PredictionTarget(new SymbolDef(SourceKind.Weather, "weather", "날씨")), reviewed) == "null",
                "FX-oriented public policy context leaked into unsupported economic or weather target input");
            Integration(fx, coin, reviewed, expiry);
            return checks;
        }

        // Separate from Run/all: render only the actual policy disclosure body without showing a window.
        internal static int Layout(string work)
        {
            checks = 0;
            if (Application.Current == null) Theme.Apply(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
            DateTime now = PolicyContext.ReviewedUtc;
            var target = new PredictionTarget(null);
            var fixture = Current(target, now);
            var window = new DollarAnalysisWindow(new Config(Path.Combine(work, "policy-layout.json")), ct => Task.FromResult(fixture),
                ct => Task.FromResult(false), ct => Task.FromResult(0), target, null, () => now);
            try {
                var body = (StackPanel)typeof(DollarAnalysisWindow).GetField("_policyContextBody", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                ((Expander)body.Parent).Content = null;
                var previewRoot = new Border { Background = new SolidColorBrush(Color.FromRgb(20, 23, 30)), Padding = new Thickness(10), Child = body };
                previewRoot.SetValue(Control.FontFamilyProperty, new FontFamily("Segoe UI, Malgun Gothic"));
                const double width = 340;
                previewRoot.Measure(new Size(width, double.PositiveInfinity));
                double height = Math.Ceiling(previewRoot.DesiredSize.Height);
                previewRoot.Arrange(new Rect(0, 0, width, height)); previewRoot.UpdateLayout();
                var buttons = body.Children.OfType<Button>().ToList();
                var labels = Descendants(body).OfType<KoreanTextBlock>().ToList();
                var tracked = Descendants(body).OfType<BriefTextBlock>().ToList();
                Check(!window.IsLoaded && buttons.Count == PolicyContext.References(now).Count() + PolicyContext.Upcoming(now).Count(),
                    "policy layout opened a native window or omitted a reference/calendar button");
                Check(body.ActualWidth <= 320 && buttons.All(b => b.ActualWidth > 0 && b.ActualWidth <= 320) && labels.Count > 0 &&
                    labels.All(t => t.ActualWidth > 0 && t.ActualWidth <= 320 && t.TextWrapping == TextWrapping.Wrap &&
                        t.TransformToAncestor(previewRoot).Transform(new Point(t.ActualWidth, 0)).X <= width - 10 + 1),
                    "the 340px policy disclosure measured a label or button outside its available width");
                Check(tracked.Count > 0 && tracked.Any(t => t.Lines(t.ActualWidth).Count > 1) &&
                    tracked.All(t => t.Lines(t.ActualWidth).All(line => t.TextWidth(line) <= t.ActualWidth + 1)),
                    "long Korean policy text did not wrap or its rendered lines exceed the measured width");
                int pixelHeight = (int)Math.Ceiling(height * 1.5);
                var bitmap = new RenderTargetBitmap(510, pixelHeight, 144, 144, PixelFormats.Pbgra32); bitmap.Render(previewRoot);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(work);
                string path = Path.Combine(work, "policy-context-340.png");
                using (var stream = File.Create(path)) encoder.Save(stream);
                var last = (FrameworkElement)body.Children[body.Children.Count - 1];
                Check(last.TransformToAncestor(previewRoot).Transform(new Point(0, last.ActualHeight)).Y <= height + 1 && new FileInfo(path).Length > 1000,
                    "the full-height policy preview clipped its final note or failed to save the rendered image");
                Console.WriteLine("PREVIEW: " + path);
                return checks;
            }
            finally { window.Close(); }
        }
    }
}
