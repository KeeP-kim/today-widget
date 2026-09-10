using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeskWidget
{
    internal static class RegressionTests
    {
        private static int checks;
        private static void Check(bool value, string name)
        {
            if (!value) throw new Exception(name);
            checks++;
        }

        /// <summary>소스 글을 읽는 검사가 볼 곳. 사보타주 때는 바꿔친 사본을 가리킨다.</summary>
        private static string SourceRoot;

        [STAThread]
        public static int Main(string[] args)
        {
            string root = args[0];
            string suite = args.Length > 1 ? args[1] : "all";
            // ★ 소스 글을 읽는 검사는 '방금 컴파일한 src' 를 봐야 한다 ★
            //   사보타주 하네스는 src 사본만 바꿔 놓고 검사를 돌린다. 검사가 원본
            //   저장소를 읽으면 그 결함을 영영 못 본다 - 실제로 배선 검사가 그랬다.
            SourceRoot = args.Length > 2 && !string.IsNullOrEmpty(args[2]) ? args[2] : root;
            string work = Path.Combine(root, "_test", "cases-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            Program.BaseDir = work;
            try
            {
                if (suite == "cli-live") checks += CliDockTests.Live();
                if (suite == "all" || suite == "backdrop") checks += DockBackdropTests.Run(work);
                if (suite == "all" || suite == "cli-side") checks += CliDockTests.Run(work);
                if (suite == "all" || suite == "primary" || suite == "primary-layout") checks += PrimaryForecastTests.Run(work, suite == "primary-layout");
                if (suite == "all" || suite == "placement") checks += PredictionPlacementTests.Run(work);
                if (suite == "calibration-live") checks += DeepPredictionTests.Live(work);
                if (suite == "all" || suite == "surge") checks += SurgeTests.Run(work);
                if (suite == "all" || suite == "deep") checks += DeepPredictionTests.Run(work);
                if (suite == "journal") checks += PredictionJournalTests.Run(work);
                if (suite == "all" || suite == "json") { JsonAndConfig(root, work); AuditFixes(work); }
                if (suite == "all" || suite == "ui") CollapsedQuotes(work);
                if (suite == "all" || suite == "docs") { Readme(root); BuildSources(root); DocCounts(root); SlowLaneWiring(root); TradedDateWiring(root); WeatherSearchWiring(root); ModeNaming(root); }
                if (suite == "all" || suite == "dollar") checks += DollarAnalysisTests.Run(work);
                if (suite == "dollar-live") checks += DollarAnalysisTests.Live(work);
                if (suite == "astra-live") checks += DollarSparkTests.Live(work, "gpt-6-astra");
                if (suite == "spark-live") checks += DollarSparkTests.Live(work);
                if (suite == "spark-doge-live") checks += DollarSparkTests.LiveDoge(work);
                if (suite == "dollar-layout") checks += DollarLayoutTests.Run(work, true);
                if (suite == "prediction-layout") checks += PredictionTests.Run(work, true);
                if (suite == "prediction-live") checks += PredictionTests.Live(root);
                Console.WriteLine("PASS: " + checks + " checks (" + suite + ")");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex);
                return 1;
            }
        }

        private static void JsonAndConfig(string root, string work)
        {
            string[] invalid = {
                "", "  ", "hello", "{", "[", "{\"x\":", "{\"x\":1", "[1",
                "{\"x\" 1}", "{\"x\":}", "{\"x\":1,}", "[1,]", "[1 2]",
                "{\"x\":1 \"y\":2}", "{}garbage", "{}{}", "true false",
                "tru", "fals", "nul", "{\"x\":truth}", "{\"x\":undefined}",
                "\"unterminated", "\"slash\\", "\"bad\\q\"", "\"bad\\u12\"",
                "\"bad\\uZZZZ\"", "\"bad\\u 123\"", "\"raw\nline\"",
                "\"escaped\\nthen\tcontrol\"", "01", "+1", ".1", "1.", "1e", "1e+", "--1",
                "NaN", "Infinity", "1e9999", new string('[', 70) + "0" + new string(']', 70)
            };
            foreach (string value in invalid)
                Check(!Json.Parse(value).Exists, "Malformed JSON accepted: " + Array.IndexOf(invalid, value));

            string[] valid = {
                "{}", "[]", "true", "false", "0", "-0", "123", "-0.125", "1e+2", "1E-2",
                " \r\n {\"a\":null,\"b\":[true,false,{},[],1]} \t",
                "\"한글 😀\"", "\"\\\"\\\\\\/\\b\\f\\n\\r\\t\\uD55C\\uAE00\""
            };
            foreach (string value in valid)
                Check(Json.Parse(value).Exists, "Valid JSON rejected: " + Array.IndexOf(valid, value));
            Check(Json.Parse("[null,1]").Count == 2, "Null array item lost");
            Check(Json.Parse("{\"value\":\"1,386.00\"}")["value"].D == 1386, "API numeric string conversion");
            Check(Json.Parse("{\"tag_name\":\"v0.94\"}")["tag_name"].S == "v0.94", "Release response");
            Check(Json.Parse("{\"current\":{\"temperature_2m\":23.7},\"daily\":{\"time\":[\"2026-09-08\"]}}")
                      ["current"]["temperature_2m"].D == 23.7, "Weather response");
            string escaped = "한글\n\t\"\\\u0001";
            Check(Json.Parse("\"" + Json.Escape(escaped) + "\"").S == escaped, "Escape round trip");

            string path = Path.Combine(work, "config.json");
            foreach (string value in invalid)
                Preserve(path, value);
            foreach (string value in new[] { "[]", "[1]", "true", "123", "\"text\"", "null" })
                Preserve(path, value);

            var missing = new Config(Path.Combine(work, "missing.json"));
            missing.Load();
            Check(!missing.LoadFailed && missing.Symbols.Count > 0, "Fresh install defaults");
            missing.Save();
            Check(Json.Parse(File.ReadAllText(missing.Path)).IsObject, "Fresh install save");

            File.Copy(Path.Combine(root, "config.sample.json"), path, true);
            var cfg = new Config(path);
            cfg.Load();
            Check(!cfg.LoadFailed && cfg.Symbols.Count == 5, "Sample config load");
            Check(cfg.FileVersion == Config.AppVersion, "Sample version differs from app");
            decimal version = decimal.Parse(Config.AppVersion, System.Globalization.CultureInfo.InvariantCulture);
            Check(Assembly.GetExecutingAssembly().GetName().Version == new Version((int)version, 0, (int)((version % 1) * 1000), 0),
                  "Assembly version differs from app");
            cfg.ShowQuotes = false;
            cfg.Save();
            var reloaded = new Config(path);
            reloaded.Load();
            Check(!reloaded.LoadFailed && !reloaded.ShowQuotes && reloaded.Symbols.Count == 5,
                  "Normal config save and reload");
            Check(reloaded.FileVersion == Config.AppVersion, "Saved version");
        }

        private static void Preserve(string path, string value)
        {
            File.WriteAllText(path, value, new UTF8Encoding(true));
            string before = Convert.ToBase64String(File.ReadAllBytes(path));
            var cfg = new Config(path);
            cfg.Load();
            Check(cfg.LoadFailed, "Invalid config did not block saving");
            cfg.ShowQuotes = false;
            cfg.Save();
            Check(Convert.ToBase64String(File.ReadAllBytes(path)) == before, "Invalid config overwritten");
            Check(!File.Exists(path + ".tmp"), "Invalid config created a replacement");
        }

        /// <summary>
        /// README 가 실제 동작과 어긋나지 않는지.
        ///
        /// ★ 이 저장소는 여기서 두 번 미끄러졌다 ★
        ///   v0.88~0.89 의 새 버전 알림 안내가 실제와 달랐고,
        ///   v1.006 에서 기본 갱신을 수동으로 바꿨는데 README 는 300초라고 계속 말했다.
        ///   README 는 공개 브랜치로 나가는 파일이라 틀리면 남이 읽는다.
        ///   문서를 손으로 맞추는 대신 어긋나면 검사가 막게 한다.
        /// </summary>
        /// <summary>
        /// ★ 빌드는 파일 목록을 손으로 적고, 검사는 폴더를 훑는다 ★
        ///   그래서 src 에 새 파일을 넣으면 검사는 통과하는데 실제 빌드에서는 조용히
        ///   빠진다. 실제로 RuleSkill.cs 가 그렇게 빠져 빌드가 깨졌다(v1.034).
        ///   목록을 자동으로 만들면 빌드 순서를 잃으므로, 어긋나면 검사가 막게 한다.
        /// </summary>
        /// <summary>
        /// ★ 문서가 세어 둔 숫자는 저절로 낡는다 ★
        ///   README 가 "고의 결함 16종" 이라고 적어 둔 사이 실제로는 177종이 됐다. 열 배가
        ///   넘게 어긋났는데 아무도 몰랐다. 검사 규모는 이 프로젝트가 신뢰의 근거로 내세우는
        ///   숫자라, 틀리면 그대로 오해가 된다. 사람이 맞추는 대신 검사가 막는다.
        /// </summary>
        private static void DocCounts(string root)
        {
            string script = File.ReadAllText(Path.Combine(root, "tests", "sabotage.ps1"), Encoding.UTF8);
            int cases = System.Text.RegularExpressions.Regex.Matches(script, @"@\{\s*Name=").Count;
            Check(cases > 0, "sabotage.ps1 no longer lists any cases");
            string readme = File.ReadAllText(Path.Combine(root, "README.md"), Encoding.UTF8);
            var stated = System.Text.RegularExpressions.Regex.Match(readme, @"고의 결함 (\d+)종");
            Check(stated.Success, "README no longer states how many sabotage defects there are");
            Check(int.Parse(stated.Groups[1].Value, CultureInfo.InvariantCulture) == cases,
                "README says 고의 결함 " + stated.Groups[1].Value + "종 but sabotage.ps1 has " + cases);
            // 근거계수 문턱도 코드와 맞아야 한다. v1.014 에서 4->8 로 올렸는데 문서만 남아 있었다.
            string analysis = File.ReadAllText(Path.Combine(root, "src", "DollarAnalysis.cs"), Encoding.UTF8);
            var coverage = System.Text.RegularExpressions.Regex.Match(analysis, @"DirectionalCount / (\d+)\.0");
            Check(coverage.Success, "the coverage divisor moved out of Score()");
            Check(readme.Contains("방향 기사 " + coverage.Groups[1].Value + "개 미만"),
                "README does not state the coverage floor of " + coverage.Groups[1].Value);
            // 본문 건수 기본값도 마찬가지.
            string config = File.ReadAllText(Path.Combine(root, "src", "Config.cs"), Encoding.UTF8);
            var body = System.Text.RegularExpressions.Regex.Match(config, @"DefaultBodyLimit = (\d+)");
            Check(body.Success, "the default body limit moved");
            Check(readme.Contains("총 " + body.Groups[1].Value + "건까지"),
                "README does not state the body-excerpt limit of " + body.Groups[1].Value);
        }

        /// <summary>
        /// ★ 느린 자료가 느린 통로로 나가는지 ★
        ///   미 재무부는 느릴 때 17~19초가 걸리는데 기본 통로는 10초에서 끊는다.
        ///   빠른 통로로 되돌리면 국채금리가 조용히 빈 값이 되고, 화면은 '자료 부족'
        ///   이라고만 적어서 아무도 눈치채지 못한다. 실제로 v1.035 가 그런 상태였다.
        /// </summary>
        private static void SlowLaneWiring(string root)
        {
            foreach (string name in new[] { "DollarAnalysis.cs", "PredictionData.cs" })
            {
                string code = File.ReadAllText(Path.Combine(SourceRoot, "src", name), Encoding.UTF8);
                if (code.IndexOf("YieldUrl(", StringComparison.Ordinal) < 0) continue;
                foreach (string line in code.Split('\n'))
                {
                    if (line.IndexOf("YieldUrl(", StringComparison.Ordinal) < 0) continue;
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    if (line.IndexOf("Net.Get", StringComparison.Ordinal) < 0) continue;
                    Check(line.IndexOf("GetSlowTextAsync", StringComparison.Ordinal) >= 0,
                        name + " fetches the treasury feed on the fast lane: " + line.Trim());
                }
            }
        }

        /// <summary>
        /// ★ 거래일이 붙지 않으면 Fresh 는 아무것도 거르지 못한다 ★ (2026-09-10 감사 #24)
        ///   Fresh() 는 '거래일을 아는 시세' 만 오늘 것인지 본다. FetchFx 가 TradedDate 를 안 채우면
        ///   검사는 다 통과하는데 실제로는 휴일 고시환율로 채점한다. 망을 타지 않고 배선을 본다.
        /// </summary>
        private static void TradedDateWiring(string root)
        {
            string code = File.ReadAllText(Path.Combine(SourceRoot, "src", "Sources.cs"), Encoding.UTF8);
            int fx = code.IndexOf("Task<Quote> FetchFx(", StringComparison.Ordinal);
            Check(fx >= 0, "FetchFx is missing from Sources.cs");
            int end = code.IndexOf("Task<Quote> FetchIndex(", fx, StringComparison.Ordinal);
            string body = code.Substring(fx, end > fx ? end - fx : code.Length - fx);
            Check(body.IndexOf("TradedDate = DateOf(r[\"localTradedAt\"].S)", StringComparison.Ordinal) >= 0,
                "FetchFx no longer stamps the traded date - Fresh() cannot reject a holiday's posted rate");
        }

        /// <summary>
        /// 2026-09-10 전수 감사에서 나온 작은 것들. 하나하나는 작지만 전부 '조용히 틀린' 쪽이라
        /// 화면만 봐서는 알 수 없다 - 그래서 값으로 잡아 둔다.
        /// </summary>
        private static void AuditFixes(string work)
        {
            // #6 조각 창 배율: 손잡이가 내려 주는 만큼은 읽을 때도 살아야 한다.
            //    (PanelWindow.MinScale 과 Config.MinPanelScale 을 견주는 것은 뜻이 없다 - 지금은
            //     같은 상수의 별칭이라 언제나 참이다. 실제로 지키는지는 아래 왕복으로 본다.)
            var scaled = new Config(Path.Combine(work, "panel-scale.json"));
            scaled.WeatherScale = 0.5; scaled.Save();
            var reread = new Config(scaled.Path); reread.Load();
            Check(reread.WeatherScale == 0.5, "a half-size panel scale was dropped on reload: " + reread.WeatherScale);

            // #8 version 을 모르면 옛 설정으로 단정하지 않는다.
            string noVersion = Path.Combine(work, "no-version.json");
            File.WriteAllText(noVersion, "{\n  \"scale\": 1.2\n}\n", Encoding.UTF8);
            var bare = new Config(noVersion); bare.Load();
            Check(bare.Scale == 1.2, "a config without a version had its scale inflated to " + bare.Scale);
            string badVersion = Path.Combine(work, "bad-version.json");
            File.WriteAllText(badVersion, "{\n  \"version\": \"v1.039\",\n  \"scale\": 1.2\n}\n", Encoding.UTF8);
            var typo = new Config(badVersion); typo.Load();
            Check(typo.Scale == 1.2, "an unreadable version inflated the scale to " + typo.Scale);
            string old = Path.Combine(work, "old-version.json");
            File.WriteAllText(old, "{\n  \"version\": \"0.14\",\n  \"scale\": 1.0\n}\n", Encoding.UTF8);
            var migrated = new Config(old); migrated.Load();
            Check(Math.Abs(migrated.Scale - 1.2) < 1e-9, "a genuine pre-0.15 config was no longer converted: " + migrated.Scale);

            // ★ 성적표는 '판' 이 아니라 '잣대 판' 으로 묶는다 ★ (2026-09-11)
            //   판올림 한 번에 성적표 칸이 하나 더 생기면 표본이 영영 안 쌓인다.
            //   실측: 예보 72건이 판 11개에 흩어져 가장 큰 칸이 12건이었다. 필요한 것은 97건이다.
            //   갈라야 하는 것은 채점 잣대가 바뀐 판뿐이고, 그 목록은 ScoringRevisions 다.
            string firstEra = Config.ScoringRevisions[0];
            string lastEra = Config.ScoringRevisions[Config.ScoringRevisions.Length - 1];
            Check(Config.ScoringEra(lastEra) == lastEra, "a yardstick version is not its own era");
            Check(Config.ScoringEra(Config.AppVersion) == lastEra,
                "the current app version maps to " + Config.ScoringEra(Config.AppVersion) + " instead of " + lastEra);
            // 잣대를 안 건드린 판올림은 앞 잣대에 붙는다 - 그래야 표본이 안 쪼개진다.
            Check(Config.ScoringEra("1.041") == "1.040" && Config.ScoringEra("1.044") == "1.040",
                "a UI-only release started its own scoring era");
            Check(Config.ScoringEra("1.039") == "1.039" && Config.ScoringEra("1.035") == "1.034",
                "an era boundary moved");
            // 첫 잣대보다 이른 기록은 그 시절끼리 한 칸.
            Check(Config.ScoringEra("1.006") == "~" + firstEra, "pre-yardstick records were not pooled");
            // 모르는 판은 아는 척하지 않는다 - 남의 칸에 섞느니 제 칸에 혼자 둔다.
            Check(Config.ScoringEra("") == "?" && Config.ScoringEra("v1.039") == "v1.039",
                "an unreadable version was folded into someone else's era");

            // ★ 날씨는 예측하지 않는다 ★ (사용자 요청, 2026-09-10)
            //   창을 열면 Open-Meteo 예보를 보여 주면서 문구는 시세용 그대로였다 -
            //   '새 정책·수급 확인 중' 같은 말이 날씨 화면에 떴다.
            //   버튼을 숨기는 것으로는 부족하다. 창을 만드는 문에서 막혔는지를 본다.
            if (Application.Current == null) Theme.Apply(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
            var weatherCfg = new Config(Path.Combine(work, "no-weather.json"));
            var region = new SymbolDef(SourceKind.Weather, "1100", "서울"); region.Lat = 37.5665; region.Lon = 126.978;
            var openField = typeof(DollarAnalysisWindow).GetField("OpenWindows", BindingFlags.Static | BindingFlags.NonPublic);
            var openList = (System.Collections.ICollection)openField.GetValue(null);
            int openedBefore = openList.Count;
            DollarAnalysisWindow.ShowSingle(null, weatherCfg, region);
            Check(openList.Count == openedBefore, "a prediction window opened for a weather region");

            //   시세 목록에도 들어갈 수 없다. 들어가면 예측·급등 알림·채점이 날씨를 값으로 다룬다.
            var host = new WidgetWindow(weatherCfg, d => { });
            try
            {
                int before = weatherCfg.Symbols.Count;
                typeof(WidgetWindow).GetMethod("AddSymbol", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(host, new object[] { region });
                Check(weatherCfg.Symbols.Count == before, "a weather region was added to the quote list");
                var normal = new SymbolDef(SourceKind.Coin, "KRW-BTC", "비트코인");
                typeof(WidgetWindow).GetMethod("AddSymbol", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(host, new object[] { normal });
                Check(weatherCfg.Symbols.Count == before + 1, "a normal symbol was refused along with weather");
            }
            finally { host.Close(); }

            // #7 날씨 지역을 지운 결정은 다시 켜도 남아야 한다.
            //    목록이 비었다는 것만 보면 '아직 안 채웠다' 와 '사용자가 지웠다' 를 구별할 수 없다.
            string weatherPath = Path.Combine(work, "weather-seed.json");
            File.WriteAllText(weatherPath,
                "{\n  \"version\": \"" + Config.AppVersion + "\",\n  \"lat\": 37.5665,\n  \"lon\": 126.978,\n  \"city\": \"서울\"\n}\n",
                Encoding.UTF8);
            var seeded = new Config(weatherPath); seeded.Load();
            Check(seeded.Weathers.Count == 1 && seeded.WeatherSeeded, "the first run did not seed a weather region");
            seeded.Weathers.Clear(); seeded.Save();          // 사용자가 마지막 지역을 지웠다
            var afterRestart = new Config(weatherPath); afterRestart.Load();
            Check(afterRestart.Weathers.Count == 0, "a deleted last weather region came back after a restart");
            // ★ v1.040 이전 설정에는 weatherSeeded 키가 없다 ★
            //   그렇다고 '아직 안 채웠다' 로 보면, 옛 사용자는 지역을 다 지운 첫 번째에 하나가
            //   되살아난다. 지역을 갖고 있다는 것 자체가 이미 채웠다는 증거다.
            string legacy = Path.Combine(work, "legacy-weather.json");
            File.WriteAllText(legacy,
                "{\n  \"version\": \"1.038\",\n  \"weathers\": [ { \"code\": \"1100\", \"label\": \"서울\", \"lat\": 37.5665, \"lon\": 126.978 } ]\n}\n",
                Encoding.UTF8);
            var legacyCfg = new Config(legacy); legacyCfg.Load();
            Check(legacyCfg.Weathers.Count == 1 && legacyCfg.WeatherSeeded,
                "a pre-v1.040 config with regions was treated as never seeded");

            // #17 1원 미만 코인이 '0.0원' 으로 잘리던 것.
            Check(Sources.CoinDigits(1500) == 0 && Sources.CoinDigits(12.3) == 1 &&
                  Sources.CoinDigits(0.05) == 4 && Sources.CoinDigits(0.000123) == 6,
                "sub-won coin prices are still rounded away");

            // #18 공유 시세는 뒤로 가지 않는다.
            // 두 스레드가 같은 품목을 동시에 발행하면, 시각을 먼저 찍은 쪽이 나중에 저장될 수 있다.
            // 구독자는 시각으로 걸러 지켰지만 사전을 읽는 쪽(SharedQuote)은 옛 것을 받았다.
            var coin = new SymbolDef(SourceKind.Coin, "KRW-AUDIT", "감사용");
            DateTime stamp = new DateTime(2026, 9, 10, 3, 0, 0, DateTimeKind.Utc);
            Sources.PublishQuote(coin, "HANA", new Quote { Ok = true, Value = 2, Price = "2" }, stamp);
            Sources.PublishQuote(coin, "HANA", new Quote { Ok = true, Value = 1, Price = "1" }, stamp.AddSeconds(-5));
            Check(Sources.SharedQuote(coin, "HANA").Value == 2, "an older quote overwrote a newer shared one");
            Sources.PublishQuote(coin, "HANA", new Quote { Ok = true, Value = 3, Price = "3" }, stamp.AddSeconds(5));
            Check(Sources.SharedQuote(coin, "HANA").Value == 3, "a newer quote was rejected");

            // #23 기사 안의 기사가 아닌 것. 기사 끝에 붙는 제보·저작권 안내 문단의 모양이다.
            Check(DollarNewsSources.Boilerplate("제보는 카카오톡 okjebo <저작권자(c) OO뉴스, 무단 전재-재배포> 2026/09/07 15:34 송고") &&
                  DollarNewsSources.Boilerplate("This video can not be played"),
                "page furniture is still read as article text");
            Check(!DollarNewsSources.Boilerplate("한국은행은 기준금리를 동결하기로 결정했다고 밝혔다."),
                "a real sentence was thrown away as page furniture");
            // ★ 사진 출처는 진짜 문장 앞에 붙어 온다 ★ 문단째 버리면 취재 내용을 함께 버린다.
            string credited = "[EPA=OO뉴스 자료사진 재판매 및 DB 금지] (서울=OO뉴스) 홍길동 기자 = 원/달러 환율이 사흘째 올랐다.";
            Check(!DollarNewsSources.Boilerplate(credited), "a photo credit made the whole reporting paragraph disappear");
            Check(DollarNewsSources.StripCredits(credited) == "(서울=OO뉴스) 홍길동 기자 = 원/달러 환율이 사흘째 올랐다.",
                "the photo credit was not stripped from the reporting: " + DollarNewsSources.StripCredits(credited));
            Check(DollarNewsSources.StripCredits("금리를 동결했다.") == "금리를 동결했다.", "a credit-free sentence was altered");
            // 저작권·AI 학습을 다루는 진짜 기사를 버리면 안 된다.
            Check(!DollarNewsSources.Boilerplate("정부는 AI 학습 데이터의 저작권 처리 기준을 내놓겠다고 밝혔다."),
                "an article about AI training data was discarded as a footer");
        }

        /// <summary>
        /// ★ 화면에 없는 이름을 성적표가 찍지 않는가 ★ (2026-09-11, 사용자 지적)
        ///   토글은 '참고 / AI 전망' 둘인데 성적표만 '극단 규칙' 이라는 옛 이름을 찍고 있었다.
        ///   검사가 그 이름을 하나도 안 잡고 있어서 아무도 몰랐다. 코드 안의 값(extreme-rule)은
        ///   옛 기록을 읽어야 하므로 그대로 두고, 사람에게 보이는 글자만 잡는다.
        /// </summary>
        private static void ModeNaming(string root)
        {
            foreach (string name in new[] { "PredictionJournal.cs", "DollarAnalysisWindow.cs" })
            {
                string code = File.ReadAllText(Path.Combine(SourceRoot, "src", name), Encoding.UTF8);
                // 줄마다 Check 를 부르면 검사 수가 수천으로 부풀어 다른 검사가 묻힌다. 한 번만 센다.
                string offender = null;
                foreach (string line in code.Split('\n'))
                {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    if (line.IndexOf("\"극단", StringComparison.Ordinal) >= 0) { offender = line.Trim(); break; }
                }
                Check(offender == null, name + " still shows the retired mode name to the user: " + offender);
            }
            // 토글에 적힌 두 이름이 안내문과 같아야 한다.
            string window = File.ReadAllText(Path.Combine(SourceRoot, "src", "DollarAnalysisWindow.cs"), Encoding.UTF8);
            Check(window.IndexOf("참고: AI 호출 없음 / AI 전망", StringComparison.Ordinal) >= 0,
                "the mode toggle tooltip no longer names 참고 / AI 전망");
        }

        /// <summary>
        /// 종목 검색 결과에서 날씨를 거르는 줄이 살아 있는가.
        /// 지금은 출처가 갈려 있어 섞일 일이 없지만, 자동완성이 지역명을 내주기 시작하면
        /// 이 줄 하나가 유일한 방어다. 망을 타지 않고 배선만 본다.
        /// </summary>
        private static void WeatherSearchWiring(string root)
        {
            string code = File.ReadAllText(Path.Combine(SourceRoot, "src", "SearchWindow.cs"), Encoding.UTF8);
            Check(code.IndexOf("h.Def.Kind != SourceKind.Weather", StringComparison.Ordinal) >= 0,
                "the quote search no longer filters weather out of its results");
        }

        private static void BuildSources(string root)
        {
            string path = Path.Combine(root, "build.ps1");
            Check(File.Exists(path), "build.ps1 missing");
            string script = File.ReadAllText(path, System.Text.Encoding.UTF8);
            int open = script.IndexOf("$sources = @(", StringComparison.Ordinal);
            Check(open >= 0, "build.ps1 no longer lists its sources");
            int close = script.IndexOf(')', open);
            Check(close > open, "build.ps1 source list is not closed");
            var listed = new HashSet<string>(
                System.Text.RegularExpressions.Regex.Matches(script.Substring(open, close - open), @"'([^']+)'")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value));
            var onDisk = new HashSet<string>(
                Directory.GetFiles(Path.Combine(root, "src"), "*.cs").Select(Path.GetFileNameWithoutExtension));
            var missing = onDisk.Where(f => !listed.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            var extra = listed.Where(f => !onDisk.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            Check(missing.Count == 0, "build.ps1 does not compile: " + string.Join(", ", missing));
            Check(extra.Count == 0, "build.ps1 lists files that are gone: " + string.Join(", ", extra));
        }

        private static void Readme(string root)
        {
            string path = Path.Combine(root, "README.md");
            Check(File.Exists(path), "README.md missing");
            string text = File.ReadAllText(path, System.Text.Encoding.UTF8);

            // 화면에 보이는 버전과 문서가 같아야 한다.
            Check(text.Contains("UI 버전 " + Config.AppVersion),
                "README does not state the current app version " + Config.AppVersion);

            // 기본 갱신 주기. 코드가 진실이고 문서가 따라간다.
            var fresh = new Config(Path.Combine(root, "_test", "readme-probe.json"));
            if (fresh.SparkRefreshIntervalSec == 0)
                Check(text.Contains("기본은 수동") && !text.Contains("기본은 300초"),
                    "README still claims a 300s default while the code defaults to manual");
            else
                Check(!text.Contains("기본은 수동"), "README claims a manual default while the code sets an interval");

            // 화면에 없는 이름을 사용자 안내에 쓰지 않는다.
            // v1.011 에서 토글이 '참고 / AI 전망' 으로 바뀌었고 basic/extreme 은 내부 이름으로만 남았다.
            int extremeMentions = 0, at = 0;
            while ((at = text.IndexOf("극단적", at)) >= 0) { extremeMentions++; at += 3; }
            Check(extremeMentions <= 1,
                "README still calls the toggle 극단적 in " + extremeMentions + " places; the screen says AI 전망");
            Check(text.Contains("참고 / AI 전망") || text.Contains("**참고 / AI 전망**"),
                "README does not name the toggle the way the screen does");

            // 본문 읽는 건수는 설정으로 열려 있다. 숫자가 바뀌면 문서도 바뀌어야 한다.
            Check(text.Contains("articleBodyLimit"), "README does not mention the article body limit setting");
            Check(text.Contains(Config.DefaultBodyLimit + "건으로"),
                "README does not state the current default body-read count " + Config.DefaultBodyLimit);
        }

        private static void CollapsedQuotes(string work)
        {
            var cfg = new Config(Path.Combine(work, "ui-config.json"));
            cfg.Load();
            var window = new WidgetWindow(cfg);
            try
            {
                var type = typeof(WidgetWindow);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var bar = (Border)type.GetField("_quotesBar", flags).GetValue(window);
                Check(bar != null && bar.Parent is Grid, "Quotes restore strip missing from visual tree");
                Check(Grid.GetRow(bar) == 1, "Quotes restore strip in wrong row");
                var apply = type.GetMethod("ApplyMinimized", flags);
                var header = (UIElement)type.GetField("_headerRow", flags).GetValue(window);
                var body = (UIElement)type.GetField("_bodyHost", flags).GetValue(window);
                Check(bar.Visibility == Visibility.Collapsed && header.Visibility == Visibility.Visible,
                      "Expanded quotes show duplicate restore strip");
                cfg.ShowQuotes = false;
                apply.Invoke(window, null);
                Check(bar.Visibility == Visibility.Visible && header.Visibility == Visibility.Collapsed &&
                      body.Visibility == Visibility.Collapsed, "Collapsed quotes cannot be restored");
                cfg.Minimized = true;
                apply.Invoke(window, null);
                Check(bar.Visibility == Visibility.Collapsed && header.Visibility == Visibility.Visible,
                      "Minimized window lost restore header");
                cfg.Minimized = false;
                cfg.QuotesClosed = true;
                apply.Invoke(window, null);
                Check(bar.Visibility == Visibility.Collapsed, "Closed section exposes restore strip");
                // ── 카드가 통째로 비는 자리 (2026-09-09, 실제로 겪음) ──────────────
                //   조각 셋을 떼어내 둔 채로 시세를 '닫으면' 카드에 아무것도 남지 않는다.
                //   닫힌 섹션은 원래 '펴기' 줄을 안 남기므로(조회도 멈춘다) 되돌릴 길이
                //   우클릭 메뉴뿐이었다. 카드가 빌 때만 줄을 되살린다.
                cfg.Separated = true;
                cfg.ShowQuotes = false; cfg.QuotesClosed = true;
                apply.Invoke(window, null);
                Check(bar.Visibility == Visibility.Visible,
                      "Card left completely empty with no way back to quotes");
                // 그 줄을 누르면 닫힘까지 풀려야 한다. ShowQuotes 만 켜면 아무 일이 없다.
                var revive = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
                revive.RoutedEvent = UIElement.MouseLeftButtonDownEvent;
                bar.RaiseEvent(revive);
                Check(cfg.ShowQuotes && !cfg.QuotesClosed,
                      "Restore strip left the section closed, so pressing it did nothing");
                // 조각이 카드 안에 남아 있으면 닫힌 섹션은 줄을 남기지 않는다(원래 뜻 유지).
                cfg.Separated = false;
                cfg.ShowQuotes = false; cfg.QuotesClosed = true;
                apply.Invoke(window, null);
                Check(bar.Visibility == Visibility.Collapsed,
                      "Closed section exposed a restore strip while the card still had content");
                cfg.ShowQuotes = false; cfg.QuotesClosed = false;
                apply.Invoke(window, null);
                type.GetField("_forceQuote", flags).SetValue(window, false);
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
                click.RoutedEvent = UIElement.MouseLeftButtonDownEvent;
                bar.RaiseEvent(click);
                Check(click.Handled && cfg.ShowQuotes && bar.Visibility == Visibility.Collapsed &&
                      body.Visibility == Visibility.Visible, "Restore click failed or leaked to window drag");
                Check((bool)type.GetField("_forceQuote", flags).GetValue(window), "Restore did not request fresh quotes");
                var saved = new Config(cfg.Path);
                saved.Load();
                Check(saved.ShowQuotes && !saved.LoadFailed, "Restore state not saved");
            }
            finally { window.Close(); }
        }
    }
}
