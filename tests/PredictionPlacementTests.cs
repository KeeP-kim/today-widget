using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace DeskWidget
{
    internal static class PredictionPlacementTests
    {
        private static int checks;
        private static void Check(bool ok, string reason) { if (!ok) throw new Exception("Prediction placement: " + reason); checks++; }
        internal static int Run(string work)
        {
            checks = 0;
            var area = new Rect(0, 0, 1920, 1040);
            var size = new Size(440, 820);
            var leftCard = new Rect(100, 100, 320, 500);
            var box = DollarAnalysisWindow.NearbyBounds(leftCard, area, size, null, DockEdge.None);
            Check(box.Left == 430 && box.Top == 100 && area.Contains(box), "left card popup is distant or overlaps widget");
            var rightCard = new Rect(1570, 100, 320, 500);
            box = DollarAnalysisWindow.NearbyBounds(rightCard, area, size, null, DockEdge.None);
            Check(box.Right == 1560 && area.Contains(box), "right card popup does not flip inward");
            var leftMonitor = new Rect(-1920, 0, 1920, 1040);
            box = DollarAnalysisWindow.NearbyBounds(new Rect(-1870, 100, 90, 800), leftMonitor, size, null, DockEdge.Left);
            Check(leftMonitor.Contains(box) && box.Left == -1770, "negative monitor coordinate escaped to primary");
            var aboveMonitor = new Rect(0, -1080, 1920, 1040);
            box = DollarAnalysisWindow.NearbyBounds(new Rect(1700, -1000, 90, 800), aboveMonitor, size, null, DockEdge.Right);
            Check(aboveMonitor.Contains(box) && box.Right == 1690, "upper monitor placement crosses work area");
            box = DollarAnalysisWindow.NearbyBounds(new Rect(0, 0, 1920, 40), area, size, new Point(1600, 20), DockEdge.Top);
            Check(box.Top == 50 && box.Left > 1400 && area.Contains(box), "top bar click opens at far end");
            box = DollarAnalysisWindow.NearbyBounds(new Rect(0, 1000, 1920, 40), area, size, new Point(60, 1020), DockEdge.Bottom);
            Check(box.Bottom == 990 && box.Left == 20 && area.Contains(box), "bottom bar popup does not follow click above bar");
            box = DollarAnalysisWindow.NearbyBounds(new Rect(0, 0, 80, 1040), area, size, new Point(40, 600), DockEdge.Left);
            Check(box.Left == 90 && box.Bottom <= 1032, "left bar overlaps or goes below taskbar");
            box = DollarAnalysisWindow.NearbyBounds(new Rect(1840, 0, 80, 1040), area, size, new Point(1880, 300), DockEdge.Right);
            Check(box.Right == 1830 && area.Contains(box), "right bar popup opens on adjacent monitor");
            var small = new Rect(-300, -200, 300, 260);
            box = DollarAnalysisWindow.NearbyBounds(new Rect(-290, -190, 60, 80), small, size, null, DockEdge.None);
            Check(small.Contains(box) && box.Width == 284 && box.Height == 244, "small work area fails to fit popup");

            var cfg = new Config(Path.Combine(work, "placement.json")) { DollarX = 8000, DollarY = 8000 };
            var popup = new DollarAnalysisWindow(cfg, ct => Task.FromResult<DollarAnalysisResult>(null));
            var owner = new Window { Left = -1800, Top = 100, Width = 300, Height = 500 };
            try {
                double sx, sy; Dock.GetDpiScale(popup, out sx, out sy);
                var screens = new List<ScreenInfo> {
                    new ScreenInfo { Bounds = new Rect(0, 0, 1920 * sx, 1080 * sy), Work = new Rect(0, 0, 1920 * sx, 1040 * sy), Primary = true },
                    new ScreenInfo { Bounds = new Rect(-1920 * sx, 0, 1920 * sx, 1080 * sy), Work = new Rect(-1920 * sx, 0, 1920 * sx, 1040 * sy) }
                };
                popup.PositionNearOwner(owner, screens, new Point(100 * sx, 100 * sy));
                Check(popup.Left == -1490 && popup.Top == 100 && popup.WindowStartupLocation == WindowStartupLocation.Manual,
                    "DPI conversion or off-owner cursor selects wrong monitor");
                double x = popup.Left, y = popup.Top;
                var realWork = Dock.AllScreens()[0].Work;
                cfg.DollarX = realWork.Left / sx + 100; cfg.DollarY = realWork.Top / sy + 20;
                typeof(DollarAnalysisWindow).GetMethod("RestorePosition", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
                Check(popup.Left == x && popup.Top == y, "saved location overrides nearby placement");
                owner.Left = 1500;
                popup.PositionNearOwner(owner, screens, new Point(1550 * sx, 200 * sy));
                Check(popup.Left == 1050 && popup.Top == 176, "reopening does not follow moved widget and click");
                Check(!popup.IsLoaded && !owner.IsLoaded, "placement test displayed a user window");
            } finally { popup.Close(); owner.Close(); }
            return checks;
        }
    }
}
