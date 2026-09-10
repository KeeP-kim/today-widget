using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskWidget
{
    internal static class DockBackdropTests
    {
        private static int checks;
        private static void Check(bool ok, string why) { if (!ok) throw new Exception("Dock backdrop: " + why); checks++; }
        private sealed class Bar : IDockBar
        {
            public int Order, Thickness, Length, Overhang;
            public Rect Placed;
            public Window BarWindow { get { return null; } }
            public AppBar BarAppBar { get { return null; } }
            public uint BarCallbackMsg { get { return 0; } }
            public DockEdge BarEdge { get { return DockEdge.Left; } }
            public string BarDevice { get { return "fixture"; } }
            public int BarOrder { get { return Order; } }
            public int BarThicknessPx { get { return Thickness; } }
            public bool BarActive { get { return true; } }
            public int BarOverhangPx { get { return Overhang; } }
            public bool BarOwnRow { get { return false; } }
            public int BarLengthPx { get { return Length; } }
            public void PlaceBar(Rect r) { int x,y,w,h; Dock.SnapEdges(r,out x,out y,out w,out h); Placed = new Rect(x,y,w,h); }
            public void SetBarFullScreen(bool full) { }
        }
        private static object Call(string name, params object[] args)
        { return typeof(DockStack).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args); }
        private static void Clean(List<IDockBar> row)
        {
            foreach (string field in new[] { "_placed", "_solid" }) {
                var entries = (System.Collections.IDictionary)typeof(DockStack).GetField(field,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                foreach (var bar in row) entries.Remove(bar);
            }
        }
        internal static int Run(string work)
        {
            checks = 0;
            foreach (var edge in new[] { DockEdge.Left,DockEdge.Right,DockEdge.Top,DockEdge.Bottom })
            foreach (int width in new[] { 60,88,126 }) {
                var quote = new Bar { Order=0, Thickness=width };
                var weather = new Bar { Order=1, Thickness=width+17, Length=90 };
                var clock = new Bar { Order=3, Thickness=width-9, Length=60 };
                var row = new List<IDockBar> { quote,weather,clock };
                try {
                    double solid = (double)Call("SolidThick",row), thickness = (double)Call("RowThick",row);
                    Check(solid==width && thickness==width,"followers enlarged quote base");
                    bool vertical = edge==DockEdge.Left || edge==DockEdge.Right;
                    var band = vertical ? new Rect(-1500.4,17.4,thickness,500.3) : new Rect(-1500.4,17.4,500.3,thickness);
                    Call("PlaceRow",row,band,edge,solid);
                    foreach (var child in new[] { weather,clock })
                        Check(vertical ? child.Placed.Left==quote.Placed.Left && child.Placed.Right==quote.Placed.Right :
                            child.Placed.Top==quote.Placed.Top && child.Placed.Bottom==quote.Placed.Bottom,"joined background edges differ at "+edge);
                    Check(vertical ? quote.Placed.Bottom==weather.Placed.Top && weather.Placed.Bottom==clock.Placed.Top :
                        quote.Placed.Right==weather.Placed.Left && weather.Placed.Right==clock.Placed.Left,"pixel gap between sections");
                } finally { Clean(row); }
            }
            var icons = new Bar { Order=2,Thickness=150,Overhang=50,Length=100 };
            var withIcons = new List<IDockBar> { new Bar { Order=0,Thickness=88 },new Bar { Order=1,Thickness=120,Length=90 },icons };
            Check((double)Call("SolidThick",withIcons)==88 && (double)Call("RowThick",withIcons)==150,"icon hover room changed painted base");
            try {
                Call("PlaceRow",withIcons,new Rect(0,0,150,500),DockEdge.Left,88.0);
                Check(((Bar)withIcons[0]).Placed.Right==88 && ((Bar)withIcons[1]).Placed.Right==88 && icons.Placed.Right==150,
                    "opaque follower painted into icon hover space");
            } finally { Clean(withIcons); }
            var alone = new List<IDockBar> { new Bar { Order=1,Thickness=95,Length=90 },new Bar { Order=3,Thickness=70,Length=60 } };
            Check((double)Call("SolidThick",alone)==95,"standalone followers lost natural thickness");

            if (Application.Current==null) Theme.Apply(new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown });
            var window = new WidgetWindow(new Config(Path.Combine(work,"backdrop.json")),d=>{});
            try {
                typeof(WidgetWindow).GetMethod("BuildDockBar",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);
                var bar = (Border)typeof(WidgetWindow).GetField("_dockBar",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                Check(ReferenceEquals(bar.Background,Palette.CardEnd),"quote backdrop differs from attached panels");
                var members = (List<IDockBar>)typeof(DockStack).GetField("_bars",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                var config = (Config)typeof(WidgetWindow).GetField("_cfg",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                config.DockedEdge=DockEdge.Left;
                typeof(WidgetWindow).GetField("_dockScreen",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,new ScreenInfo { Device="fixture" });
                members.Add(window);
                try {
                    foreach (string key in new[] { "날씨","시계" }) {
                        var follower = new PanelWindow(key,new Border(),1.5,null,null); follower.Edge=DockEdge.Left;
                        typeof(PanelWindow).GetField("_dockScreen",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(follower,new ScreenInfo { Device="fixture" });
                        typeof(PanelWindow).GetField("_active",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(follower,true);
                        members.Add(follower);
                        try {
                            var color=(Brush)typeof(PanelWindow).GetMethod("BarBackdrop",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(follower,new object[] { false });
                            Check(ReferenceEquals(color,bar.Background),key+" has a visible background seam");
                        } finally { members.Remove(follower); follower.Edge=DockEdge.None; follower.Close(); }
                    }
                } finally { members.Remove(window); config.DockedEdge=DockEdge.None; }
                var canvas = new Canvas { Width=140,Height=380,Background=Brushes.DarkOliveGreen };
                var sections = new[] { "시세\n\n1,569.0\nJPY\n+0.19%", "☀\n28.9°\n종로1가", "15:39\n9/8" };
                double at=0;
                foreach (var index in Enumerable.Range(0,3)) {
                    var panel = new Border { Width=88,Height=index==0?220:index==1?95:65,Background=bar.Background,
                        Child=new TextBlock { Text=sections[index],Foreground=Palette.Text,TextAlignment=TextAlignment.Center,FontSize=16,VerticalAlignment=VerticalAlignment.Center } };
                    Canvas.SetTop(panel,at); at+=panel.Height; canvas.Children.Add(panel);
                }
                canvas.Measure(new Size(140,380)); canvas.Arrange(new Rect(0,0,140,380)); canvas.UpdateLayout();
                var bitmap = new RenderTargetBitmap(140,380,96,96,PixelFormats.Pbgra32); bitmap.Render(canvas);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using(var stream=File.Create(Path.Combine(work,"joined-background.png"))) encoder.Save(stream);
                Console.WriteLine("PREVIEW: "+Path.Combine(work,"joined-background.png"));
            } finally { window.Close(); }
            return checks;
        }
    }
}
