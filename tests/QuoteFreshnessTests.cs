using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace DeskWidget
{
    internal static class QuoteFreshnessTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Field(object obj, string name)
        { return obj.GetType().GetField(name, Flags | BindingFlags.Public).GetValue(obj); }
        private static object Call(object obj, string name, params object[] args)
        { return obj.GetType().GetMethod(name, Flags).Invoke(obj, args); }
        private static Quote Q(string price, DateTime at)
        { return new Quote { Ok = true, Value = 100, Price = price, Source = "synthetic", Time = "12:00", ReceivedUtc = at, Ratio = "1", RatioSuffix = "%", Dir = 1 }; }
        internal static int Run(string work)
        {
            int count = 0;
            Action<bool, string> check = (ok, why) => { if (!ok) throw new Exception("Quote display: " + why); count++; };
            var fx = new SymbolDef(SourceKind.Fx, "FX_USDKRW", "환율 검사");
            var coin = new SymbolDef(SourceKind.Coin, "KRW-DOGE", "도지 검사");
            var cfg = new Config(Path.Combine(work, "quote-display.json")) { Expanded = false, QuoteIntervalSec = 30 };
            cfg.Symbols.Add(fx); cfg.Symbols.Add(coin); cfg.Symbol = fx.Key;
            var window = new WidgetWindow(cfg, d => { });
            try
            {
                DateTime now = DateTime.UtcNow;
                var quotes = (IDictionary)Field(window, "_quotes");
                var oldFx = Q("1400.00", now.AddHours(-4));
                var freshCoin = Q("250.00", now);
                quotes[fx.Key] = oldFx; quotes[coin.Key] = freshCoin;
                Call(window, "RefreshHeader"); Call(window, "RefreshQuote");
                var price = (TextBlock)Field(window, "_price");
                var time = (TextBlock)Field(window, "_timeLabel");
                check(price.Text == "- - - -" && time.Text.Contains("지연"), "fresh coin masked expired collapsed FX");
                check((bool)window.GetType().GetProperty("IsStale", Flags).GetValue(window, null), "aggregate health masked outage");
                cfg.Symbol = coin.Key;
                Call(window, "RefreshHeader"); Call(window, "RefreshQuote");
                check(price.Text == "250.00" && !time.Text.Contains("지연"), "expired FX hid fresh selected coin");
                cfg.Expanded = true;
                foreach (bool grid in new[] { false, true })
                {
                    cfg.GridView = grid; Call(window, "RefreshSymbolViews");
                    int rows = 0;
                    foreach (object view in (IEnumerable)Field(window, grid ? "_tiles" : "_rows"))
                    {
                        var def = (SymbolDef)Field(view, "Def");
                        var cell = (TextBlock)Field(view, "Price"); rows++;
                        check(cell.Text == (def.Key == fx.Key ? "- -" : "250.00"), "list/tile used shared age");
                        check((cell.ToolTip ?? "").ToString().Contains("지연") == (def.Key == fx.Key), "list/tile delay hint missing");
                    }
                    check(rows == 2, "missing quote rows");
                }
                var dockViews = (IList)Field(window, "_dockViews");
                dockViews.Clear();
                foreach (bool vertical in new[] { false, true })
                {
                    object result = Call(window, "BuildDockQuote", fx, oldFx, vertical);
                    object view = result.GetType().GetProperty("Item1").GetValue(result, null);
                    var cell = (BriefTextBlock)Field(view, "Price");
                    check(cell.Text == "- -" && ((Border)Field(view, "Box")).ToolTip.ToString().Contains("지연"), "initial dock exposed expired value");
                    dockViews.Add(view);
                }
                quotes[fx.Key] = Q("1401.00", now);
                Call(window, "UpdateDockValues");
                foreach (object view in dockViews)
                {
                    check(!((BriefTextBlock)Field(view, "Price")).Text.Contains("-") && !((Border)Field(view, "Box")).ToolTip.ToString().Contains("지연"), "dock recovery retained delay");
                    check(Field(view, "Ratio") != null && ((BriefTextBlock)Field(view, "Ratio")).Text == "1%", "dock recovery lost ratio slot");
                }
                quotes[fx.Key] = oldFx; Call(window, "UpdateDockValues");
                foreach (object view in dockViews)
                    check(((BriefTextBlock)Field(view, "Price")).Text == "- -", "existing dock retained expired price");
                cfg.Expanded = false; cfg.Symbol = coin.Key;
                freshCoin.ProviderTimeRequired = true; freshCoin.ProviderUtc = now; freshCoin.TradedUtc = now.AddHours(-4);
                Call(window, "RefreshQuote");
                check(price.Text == "- - - -", "new receipt disguised old trade");
                freshCoin.TradedUtc = now.AddMinutes(-10);
                Call(window, "RefreshQuote");
                check(price.Text == "250.00" && price.ToolTip.ToString().Contains("지연"), "recent stale quote not labelled");
                freshCoin.TradedUtc = now; freshCoin.ProviderUtc = now.AddMinutes(1);
                Call(window, "RefreshQuote");
                check(price.Text == "- - - -", "future provider quote displayed");
                freshCoin.ProviderUtc = now;
                Call(window, "RefreshQuote");
                check(price.Text == "250.00" && !price.ToolTip.ToString().Contains("지연"), "fresh recovery did not restore value");
                cfg.Symbol = fx.Key;
                var oldFixing = Q("1402.00", now);
                oldFixing.TradedDate = DollarAnalysis.KoreaDate(now).AddDays(-1);
                quotes[fx.Key] = oldFixing;
                Call(window, "RefreshHeader"); Call(window, "RefreshQuote");
                check(time.Text.Contains("지연") && price.ToolTip.ToString().Contains("이전 거래일"), "new receipt presented yesterday fixing as current");
                cfg.ShowClock = false; cfg.QuoteIntervalSec = Config.MaxInterval;
                Call(window, "UpdateClockTimer");
                check(((System.Windows.Threading.DispatcherTimer)Field(window, "_clockTimer")).IsEnabled,
                    "collapsed quote with clock off has no freshness timer");
                quotes[fx.Key] = Q("1403.00", now);
                Call(window, "RefreshQuote");
                check(price.Text == "1403.00", "fresh fixture not displayed");
                quotes[fx.Key] = oldFx;
                Call(window, "RefreshQuoteAges");
                check(price.Text == "- - - -", "waiting for a long fetch interval retained expired value");
            }
            finally { window.Close(); }
            return count;
        }
    }
}
