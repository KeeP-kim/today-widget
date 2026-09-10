using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskWidget
{
    internal static class SurgeTests
    {
        private static int checks;
        private static void Check(bool ok, string why) { if (!ok) throw new Exception("Surge: " + why); checks++; }
        private static object Field(object o, string name) { return o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(o); }
        private static double Note(WidgetWindow window, SymbolDef def, Quote q)
        { return (double)typeof(WidgetWindow).GetMethod("NoteSurge", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { def, q }); }
        private static Quote Q(double value, DateTime time, string source = "fixture")
        { return new Quote { Ok = true, Value = value, Price = "0.0", ReceivedUtc = time, Source = source }; }
        internal static int Run(string work)
        {
            checks = 0;
            var coin = new SymbolDef(SourceKind.Coin, "KRW-DOGE", "도지코인");
            var cfg = new Config(Path.Combine(work, "surge-config.json")) { Expanded = true, GridView = true, SurgeAlert = true, QuoteIntervalSec = 30 };
            cfg.Symbols.Clear(); cfg.Symbols.Add(coin); cfg.Symbol = coin.Key;
            var window = new WidgetWindow(cfg, d => { }); DateTime now = DateTime.UtcNow;
            try {
                Check(Note(window, coin, Q(.01, now.AddSeconds(-31))) == 0, "first sample triggers surge");
                Check(Math.Abs(Note(window, coin, Q(.0102, now)) - 2) < .0001, "unrounded price not used");
                Check(Note(window, coin, Q(.0105, now)) == 0, "duplicate receipt triggers surge");
                Check(Note(window, coin, Q(.02, now, "other-feed")) == 0, "feed change creates surge");
                var last = (IDictionary)Field(window, "_lastPrice"); last.Clear();
                cfg.QuoteIntervalSec = 300;
                Note(window, coin, Q(100, now.AddSeconds(-303)));
                Check(Note(window, coin, Q(102, now)) > 1.9, "five minute latency loses surge");
                last.Clear(); Note(window, coin, Q(100, now.AddSeconds(-700)));
                Check(Note(window, coin, Q(102, now)) == 0, "long disconnection creates surge");
                cfg.SurgeAlert = false; last.Clear(); Note(window, coin, Q(100, now.AddSeconds(-31)));
                Check(Note(window, coin, Q(102, now)) == 0, "disabled alert fires"); cfg.SurgeAlert = true;
                last.Clear(); Note(window, coin, Q(100, now.AddSeconds(-31)));
                var shared = Q(98, now); shared.IdentityKey = Sources.QuoteKey(coin, cfg.Bank);
                typeof(WidgetWindow).GetMethod("OnSharedQuote", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { shared.IdentityKey, shared });
                var tiles = ((IEnumerable)Field(window, "_tiles")).Cast<object>().ToList();
                var root = (Border)Field(tiles[0], "Root");
                Check(root.Background is SolidColorBrush && ((SolidColorBrush)root.Background).HasAnimatedProperties, "shared quote does not flash tile");
                cfg.Expanded = false; last.Clear(); Note(window, coin, Q(100, now.AddSeconds(-31)));
                typeof(WidgetWindow).GetMethod("OnSharedQuote", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { shared.IdentityKey, shared });
                var collapsed = (Border)Field(window, "_collapsedSurge");
                Check(collapsed.Background is SolidColorBrush && ((SolidColorBrush)collapsed.Background).HasAnimatedProperties, "collapsed quote has no alert surface");

                // ── 전수 감사 (2026-09-10) 위젯 쪽 네 건 ──────────────────────
                // #1 공유 시세는 방금 것일 때만 받고, '받았다' 는 실제 수신에만 준다.
                //    전에는 조회가 실패하면 30분 전 공유값이 '더 새롭다' 며 채택돼, 선을 뽑아도
                //    상태등이 초록이고 낡은 숫자가 몇 시간이고 그대로 떴다.
                var none = new Quote(); var stale = Q(100, now.AddMinutes(-30)); var recent = Q(101, now.AddSeconds(-5)); bool got;
                Check(!WidgetWindow.PickQuote(none, stale, null, now, out got).Ok && !got, "a half-hour-old shared quote replaced a failed fetch and counted as received");
                Check(WidgetWindow.PickQuote(none, recent, null, now, out got) == recent && got, "a fresh shared quote was refused after a failed fetch");
                var own = Q(99, now.AddSeconds(-10));   // 공유값(5초 전)보다 오래된 내 조회
                Check(WidgetWindow.PickQuote(own, recent, null, now, out got) == recent && got, "a newer shared quote lost to an older own fetch");
                Check(WidgetWindow.PickQuote(own, stale, null, now, out got) == own && got, "an older shared quote replaced the own fetch");
                Check(WidgetWindow.PickQuote(own, null, null, now, out got) == own && got, "no shared quote broke the own fetch");
                Check(!WidgetWindow.PickQuote(none, Q(102, now.AddSeconds(20)), null, now, out got).Ok && !got, "a shared quote from the future was accepted");
                // ★ 공유 창고에는 내가 방금 올린 내 시세도 들어 있다 ★
                //   조회 주기를 짧게 두면 선을 뽑은 뒤에도 '몇 초 전 내 시세' 가 돌아와 남의 새
                //   시세인 척한다. 이미 띄우고 있던 바로 그 개체면 새로 받은 것이 아니다.
                Check(!WidgetWindow.PickQuote(none, recent, recent, now, out got).Ok && !got,
                    "the widget's own last quote came back from the shared store and counted as a fresh reception");
                Check(WidgetWindow.PickQuote(none, recent, Q(100, now.AddSeconds(-5)), now, out got) == recent && got,
                    "a genuinely new shared quote was refused because something else was on screen");
                // #2 실패 뒤에는 15초 → 30초 → … 120초로 되돌아온다. 전에는 한 주기(최대 6시간)를 통째로 기다렸다.
                Check(WidgetWindow.RetryStamp(now, true, 3, 300) == now, "a success did not return to the regular interval");
                Check(Math.Abs((now - WidgetWindow.RetryStamp(now, false, 1, 300)).TotalSeconds - 285) < 1e-6, "the first failure did not come back in 15 seconds");
                Check(Math.Abs((now - WidgetWindow.RetryStamp(now, false, 2, 300)).TotalSeconds - 270) < 1e-6, "the second failure did not come back in 30 seconds");
                Check(Math.Abs((now - WidgetWindow.RetryStamp(now, false, 50, 300)).TotalSeconds - 180) < 1e-6, "the retry interval did not cap at 120 seconds");
                Check(WidgetWindow.RetryStamp(now, false, 1, 10) == now, "a ten-second interval was pushed past itself by the retry");
                // #4 내 조회가 쏜 알림은 무시한다 - Tick 이 끝에서 한 번 그린다.
                var quotes = (IDictionary)Field(window, "_quotes");
                var selfFlag = typeof(WidgetWindow).GetField("_selfFetching", BindingFlags.Instance | BindingFlags.NonPublic);
                var mine = Q(97, now.AddSeconds(1)); mine.IdentityKey = shared.IdentityKey;
                selfFlag.SetValue(window, true);
                typeof(WidgetWindow).GetMethod("OnSharedQuote", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { mine.IdentityKey, mine });
                Check(!quotes.Contains(coin.Key) || !ReferenceEquals(quotes[coin.Key], mine), "the widget's own fetch echoed back through the shared-quote path");
                selfFlag.SetValue(window, false);
                typeof(WidgetWindow).GetMethod("OnSharedQuote", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { mine.IdentityKey, mine });
                Check(quotes.Contains(coin.Key) && ReferenceEquals(quotes[coin.Key], mine), "a shared quote was ignored outside the widget's own fetch");
                // #3 마지막 날씨 지역을 지운 결정은 되살아나지 않는다.
                cfg.Weathers.Clear(); cfg.Lat = 37.5665; cfg.Lon = 126.978; cfg.City = "";
                typeof(WidgetWindow).GetField("_locationTried", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, true);
                ((Task)typeof(WidgetWindow).GetMethod("EnsureLocation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { CancellationToken.None })).GetAwaiter().GetResult();
                Check(cfg.Weathers.Count == 0, "a deleted last weather region was re-added on the next tick");
                return checks;
            } finally { window.Close(); }
        }
    }
}
