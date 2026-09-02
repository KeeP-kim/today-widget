// 모니터 가장자리 도킹 - 모니터 목록, 붙일 자리 판정, 작업표시줄식 공간 확보(AppBar)
//
// 좌표 단위가 두 가지라 헷갈리기 쉽다. 여기서 확실히 나눠 둔다.
//   - Win32 (모니터 정보, SHAppBarMessage) 는 전부 "물리 픽셀"
//   - WPF (Window.Left/Top/Width/Height) 는 전부 "DIP"
// 그래서 이 파일은 픽셀로만 계산하고, 창에 넣기 직전에 DipFromPx 로 바꾼다.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeskWidget
{
    internal enum DockEdge
    {
        None = 0,
        Left,
        Top,
        Right,
        Bottom,
    }

    /// <summary>
    /// 모니터 하나.
    ///
    /// Bounds/Work 는 '이 프로세스가 보는' 좌표다. 이 앱은 System DPI 인식이라
    /// 배율이 다른 모니터는 실제 크기와 다르게 보인다.
    /// Phys 는 EnumDisplaySettings 로 얻은 진짜 물리 좌표이며, DPI 인식과 무관하다.
    /// 셸(AppBar)에 넘길 때는 Phys 를 써야 한다.
    /// </summary>
    internal sealed class ScreenInfo
    {
        public Rect Bounds;     // 모니터 전체 (이 프로세스가 보는 좌표)
        public Rect Work;       // 작업 영역 (작업표시줄 등을 뺀 자리)
        public Rect Phys;       // 진짜 물리 좌표. 비어 있으면 못 구한 것
        public bool Primary;
        public string Device;   // \\.\DISPLAY1 형태
    }

    internal static class Dock
    {
        // ---------- Win32 ----------

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left, Top, Right, Bottom;
            /// <summary>
            /// Rect 는 음수 크기를 받으면 예외를 던진다.
            /// 셸이 돌려주는 사각형이 뒤집혀 있는 경우가 있어 여기서 막는다.
            /// </summary>
            public Rect ToRect()
            {
                double w = Right - Left, h = Bottom - Top;
                if (w < 0) w = 0;
                if (h < 0) h = 0;
                return new Rect(Left, Top, w, h);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        // 화면 모드. dmPosition 과 dmPelsWidth/Height 는 DPI 인식과 무관한 진짜 물리 값이다.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY;          // POINTL
            public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
            public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int mode, ref DEVMODE dm);

        private const int ENUM_CURRENT_SETTINGS = -1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc fn, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX mi);

        [DllImport("shell32.dll")]
        internal static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        internal static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        internal const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_NOZORDER = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        // 우리 좌표를 진짜 물리 픽셀로 바꾼다 (Windows 8.1+)
        [DllImport("user32.dll")]
        private static extern bool LogicalToPhysicalPointForPerMonitorDPI(IntPtr hwnd, ref POINT pt);

        internal const uint ABM_NEW = 0x00;
        internal const uint ABM_REMOVE = 0x01;
        internal const uint ABM_QUERYPOS = 0x02;
        internal const uint ABM_SETPOS = 0x03;
        internal const uint ABM_WINDOWPOSCHANGED = 0x09;

        private const uint MONITORINFOF_PRIMARY = 0x01;

        // ---------- 모니터 ----------

        /// <summary>붙어 있는 모니터 전부. 실패하면 주 화면 하나만 돌려준다.</summary>
        public static List<ScreenInfo> AllScreens()
        {
            var list = new List<ScreenInfo>();
            try
            {
                MonitorEnumProc cb = delegate(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data)
                {
                    var mi = new MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                    if (GetMonitorInfo(hMon, ref mi))
                    {
                        list.Add(new ScreenInfo
                        {
                            Bounds = mi.rcMonitor.ToRect(),
                            Work = mi.rcWork.ToRect(),
                            Phys = PhysicalOf(mi.szDevice),
                            Primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                            Device = mi.szDevice,
                        });
                    }
                    return true;
                };
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            }
            catch { }

            if (list.Count == 0)
            {
                // 열거에 실패해도 최소한 주 화면 하나는 있어야 한다
                list.Add(new ScreenInfo
                {
                    Bounds = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight),
                    Work = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight),
                    Primary = true,
                });
            }
            return list;
        }

        /// <summary>
        /// 그 화면 장치의 진짜 물리 사각형.
        /// EnumDisplaySettings 는 프로세스의 DPI 인식과 무관하게 실제 픽셀을 돌려준다.
        /// GetMonitorInfo 와 달리 배율에 따라 값이 달라지지 않는다.
        /// </summary>
        private static Rect PhysicalOf(string device)
        {
            if (string.IsNullOrEmpty(device)) return Rect.Empty;
            try
            {
                var dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm)) return Rect.Empty;
                if (dm.dmPelsWidth <= 0 || dm.dmPelsHeight <= 0) return Rect.Empty;
                return new Rect(dm.dmPositionX, dm.dmPositionY, dm.dmPelsWidth, dm.dmPelsHeight);
            }
            catch { return Rect.Empty; }
        }

        /// <summary>주어진 점(픽셀)을 품는 모니터. 없으면 가장 가까운 것.</summary>
        /// <summary>
        /// 장치 이름으로 모니터를 찾는다.
        ///
        /// 바가 붙은 뒤에는 창 위치로 모니터를 고르면 안 된다. 바가 얇아 한쪽으로 쏠려 있으면
        /// 옆 모니터로 판정이 튀고, 거기 다시 자리를 확보하면서 폭주한다(실측으로 확인했다).
        /// 붙일 때 정한 모니터를 끝까지 쓴다.
        /// </summary>
        public static ScreenInfo ScreenByDevice(List<ScreenInfo> all, string device)
        {
            if (all == null || string.IsNullOrEmpty(device)) return null;
            foreach (var s in all) if (s.Device == device) return s;
            return null;
        }

        // WithoutOwnBar 는 지웠다. "작업영역에서 내 두께를 도로 더한다" 는 뺄셈이었는데,
        // scr.Phys 가 비어 배율이 1 로 뭉개지면 셸이 실제로 뺀 양과 어긋나 폭주했다.
        // 이제 기준값은 '우리 것이 하나도 없을 때 직접 잰 값' 뿐이다. DockStack.MeasureAnchor 참고.

        public static ScreenInfo ScreenAt(List<ScreenInfo> all, Point px)
        {
            foreach (var s in all)
                if (px.X >= s.Bounds.Left && px.X < s.Bounds.Right &&
                    px.Y >= s.Bounds.Top && px.Y < s.Bounds.Bottom) return s;

            ScreenInfo best = all[0];
            double bestD = double.MaxValue;
            foreach (var s in all)
            {
                double dx = Math.Max(0, Math.Max(s.Bounds.Left - px.X, px.X - s.Bounds.Right));
                double dy = Math.Max(0, Math.Max(s.Bounds.Top - px.Y, px.Y - s.Bounds.Bottom));
                double d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>
        /// 그 방향에 다른 모니터가 맞닿아 있는가.
        /// 듀얼 모니터의 연결지점은 화면 끝이 아니라 지나가는 통로이므로 거기에는 붙이지 않는다.
        /// </summary>
        public static bool EdgeShared(List<ScreenInfo> all, ScreenInfo me, DockEdge edge)
        {
            const double Tol = 2;   // 배치가 1px 어긋나 있는 경우가 있다

            foreach (var o in all)
            {
                if (ReferenceEquals(o, me)) continue;

                switch (edge)
                {
                    case DockEdge.Left:
                        if (Math.Abs(o.Bounds.Right - me.Bounds.Left) <= Tol && OverlapsY(me, o)) return true;
                        break;
                    case DockEdge.Right:
                        if (Math.Abs(o.Bounds.Left - me.Bounds.Right) <= Tol && OverlapsY(me, o)) return true;
                        break;
                    case DockEdge.Top:
                        if (Math.Abs(o.Bounds.Bottom - me.Bounds.Top) <= Tol && OverlapsX(me, o)) return true;
                        break;
                    case DockEdge.Bottom:
                        if (Math.Abs(o.Bounds.Top - me.Bounds.Bottom) <= Tol && OverlapsX(me, o)) return true;
                        break;
                }
            }
            return false;
        }

        private static bool OverlapsX(ScreenInfo a, ScreenInfo b)
        {
            return Math.Min(a.Bounds.Right, b.Bounds.Right) - Math.Max(a.Bounds.Left, b.Bounds.Left) > 1;
        }

        private static bool OverlapsY(ScreenInfo a, ScreenInfo b)
        {
            return Math.Min(a.Bounds.Bottom, b.Bounds.Bottom) - Math.Max(a.Bounds.Top, b.Bounds.Top) > 1;
        }

        // ---------- DIP <-> 픽셀 ----------

        /// <summary>
        /// 창이 올라간 화면의 배율 (DIP 1 이 픽셀 몇 개인가).
        ///
        /// 이 앱은 System DPI 인식이라 이 값은 창이 어느 모니터에 있든 같다.
        /// 그래서 '못 구했을 때 1' 은 배율 없음이 아니라 **틀린 값**이다.
        /// 200% 화면에서 1 이 나오면 좌표가 통째로 절반이 된다.
        /// 창이 아직 안 붙었으면 화면 DC 에서 시스템 DPI 를 직접 읽어 쓴다.
        /// </summary>
        public static void GetDpiScale(Visual v, out double sx, out double sy)
        {
            sx = 0; sy = 0;
            try
            {
                var src = PresentationSource.FromVisual(v);
                if (src != null && src.CompositionTarget != null)
                {
                    Matrix m = src.CompositionTarget.TransformToDevice;
                    if (m.M11 > 0) sx = m.M11;
                    if (m.M22 > 0) sy = m.M22;
                }
            }
            catch { }

            if (sx <= 0 || sy <= 0)
            {
                double s = SystemDpiScale();
                if (sx <= 0) sx = s;
                if (sy <= 0) sy = s;
            }
        }

        private const int LOGPIXELSX = 88;

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int index);

        private static double _sysDpi;

        /// <summary>시스템 DPI 배율. 프로세스가 사는 동안 바뀌지 않으므로 한 번만 읽는다.</summary>
        private static double SystemDpiScale()
        {
            if (_sysDpi > 0) return _sysDpi;
            _sysDpi = 1;
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                if (dc != IntPtr.Zero)
                {
                    int dpi = GetDeviceCaps(dc, LOGPIXELSX);
                    ReleaseDC(IntPtr.Zero, dc);
                    if (dpi > 0) _sysDpi = dpi / 96.0;
                }
            }
            catch { }
            return _sysDpi;
        }

        /// <summary>붙일 자리를 계산만 한다. 셸에는 알리지 않는다.</summary>
        public static Rect EdgeRect(Rect area, DockEdge edge, int thicknessPx)
        {
            try { return MakeRect(area, edge, thicknessPx).ToRect(); }
            catch { return Rect.Empty; }
        }

        /// <summary>
        /// 커서가 어느 화면 가장자리에 닿았는지 본다. 붙일 곳이 없으면 None.
        ///
        /// 창이 아니라 커서를 기준으로 삼는다. 네 방향이 똑같이 걸리고,
        /// 무엇보다 '화면 끝까지 밀기' 라는 기대와 일치한다.
        /// 본 창과 조각 창이 같은 손맛을 갖도록 판정을 여기 한 군데로 모았다.
        /// </summary>
        public static DockEdge DetectEdge(Visual v, Rect winDip, out ScreenInfo screen)
        {
            screen = null;
            try
            {
                double sx, sy;
                GetDpiScale(v, out sx, out sy);

                var all = AllScreens();
                if (all == null || all.Count == 0) return DockEdge.None;

                var cur = CursorPos();
                var scr = ScreenAt(all, cur);
                screen = scr;

                // 후하게 잡는다. 14px 은 4K 200% 화면에서 눈으로 7px 밖에 안 돼 너무 빡빡했다.
                double snapCur = 56 * Math.Max(sx, sy);
                double snapWin = 28 * Math.Max(sx, sy);

                double wl = winDip.Left * sx, wt = winDip.Top * sy;
                double wr = wl + winDip.Width * sx, wb = wt + winDip.Height * sy;

                var edge = DockEdge.None;
                double best = double.MaxValue;
                double d;

                d = cur.X - scr.Bounds.Left;   if (d < snapCur && d < best) { best = d; edge = DockEdge.Left; }
                d = cur.Y - scr.Bounds.Top;    if (d < snapCur && d < best) { best = d; edge = DockEdge.Top; }
                d = scr.Bounds.Right - cur.X;  if (d < snapCur && d < best) { best = d; edge = DockEdge.Right; }
                d = scr.Bounds.Bottom - cur.Y; if (d < snapCur && d < best) { best = d; edge = DockEdge.Bottom; }

                if (edge == DockEdge.None)
                {
                    // 커서가 못 닿았어도 창을 가장자리에 붙여 놓았으면 받아준다
                    double e;
                    e = Math.Abs(wl - scr.Bounds.Left);   if (e < snapWin && e < best) { best = e; edge = DockEdge.Left; }
                    e = Math.Abs(wt - scr.Bounds.Top);    if (e < snapWin && e < best) { best = e; edge = DockEdge.Top; }
                    e = Math.Abs(scr.Bounds.Right - wr);  if (e < snapWin && e < best) { best = e; edge = DockEdge.Right; }
                    e = Math.Abs(scr.Bounds.Bottom - wb); if (e < snapWin && e < best) { best = e; edge = DockEdge.Bottom; }
                }

                if (edge == DockEdge.None) return DockEdge.None;

                // 듀얼 모니터의 연결지점에는 붙이지 않는다.
                // 거기는 화면 끝이 아니라 옆 모니터로 넘어가는 통로다.
                if (EdgeShared(all, scr, edge)) return DockEdge.None;
                return edge;
            }
            catch { return DockEdge.None; }
        }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);

        [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr hdc, int x, int y);

        /// <summary>
        /// 그 자리의 바탕이 밝은가.
        ///
        /// 투명한 바는 글자가 바탕화면 위에 그대로 놓인다. 하늘 사진 위에 회색 글씨를 얹으면
        /// 아무것도 안 보인다. 그래서 뒤를 몇 점 찍어 보고 글자색을 정한다.
        /// 화면 DC 에서 바로 읽으므로 비트맵을 뜨지 않는다 - 아홉 점이면 충분하다.
        ///
        /// 우리 창도 화면에 있지만 배경이 투명하라 바탕이 그대로 찍힌다.
        /// 글자에 걸리는 점이 있어도 평균을 내므로 크게 흔들리지 않는다.
        /// </summary>
        public static bool IsBrightBehind(Rect procPx)
        {
            if (procPx.Width < 2 || procPx.Height < 2) return false;

            IntPtr dc = IntPtr.Zero;
            try
            {
                dc = GetDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return false;

                double sum = 0;
                int n = 0;
                for (int i = 1; i <= 3; i++)
                {
                    for (int k = 1; k <= 3; k++)
                    {
                        int x = (int)(procPx.Left + procPx.Width * i / 4.0);
                        int y = (int)(procPx.Top + procPx.Height * k / 4.0);

                        uint c = GetPixel(dc, x, y);
                        if (c == 0xFFFFFFFF) continue;   // CLR_INVALID

                        double r = c & 0xFF, g = (c >> 8) & 0xFF, b = (c >> 16) & 0xFF;
                        sum += 0.299 * r + 0.587 * g + 0.114 * b;
                        n++;
                    }
                }
                if (n == 0) return false;
                return (sum / n) > 140;   // 0~255. 어중간하면 흰 글씨가 무난하다
            }
            catch { return false; }
            finally
            {
                try { if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc); }
                catch { }
            }
        }

        /// <summary>
        /// 사각형을 정수 픽셀에 맞춘다. 크기가 아니라 **가장자리**를 반올림한다.
        ///
        /// ★ 자리와 크기를 따로 반올림하면 이웃 사이가 벌어진다 ★
        ///   위 바가 top=100.4, h=250.2 면 반올림해서 100..350 을 차지한다.
        ///   아래 바는 top=350.6 이라 351 에서 시작한다. 그 1px 로 바탕화면이 실처럼 비친다.
        ///   가장자리를 반올림하면 두 바가 경계에서 **같은 값**을 보므로 틈이 생기지 않는다.
        /// </summary>
        public static void SnapEdges(Rect r, out int x, out int y, out int w, out int h)
        {
            x = (int)Math.Round(r.Left);
            y = (int)Math.Round(r.Top);
            w = (int)Math.Round(r.Right) - x;
            h = (int)Math.Round(r.Bottom) - y;
            if (w < 1) w = 1;
            if (h < 1) h = 1;
        }

        /// <summary>커서 자리 (이 프로세스가 보는 픽셀).</summary>
        public static Point CursorPos()
        {
            POINT p;
            if (GetCursorPos(out p)) return new Point(p.X, p.Y);
            return new Point();
        }

        internal static uint EdgeCode(DockEdge e)
        {
            switch (e)
            {
                case DockEdge.Left: return 0;
                case DockEdge.Top: return 1;
                case DockEdge.Right: return 2;
                default: return 3;   // Bottom
            }
        }

        internal static RECT MakeRect(Rect full, DockEdge edge, int t)
        {
            var r = new RECT
            {
                Left = (int)Math.Round(full.Left),
                Top = (int)Math.Round(full.Top),
                Right = (int)Math.Round(full.Right),
                Bottom = (int)Math.Round(full.Bottom),
            };
            switch (edge)
            {
                case DockEdge.Left: r.Right = r.Left + t; break;
                case DockEdge.Right: r.Left = r.Right - t; break;
                case DockEdge.Top: r.Bottom = r.Top + t; break;
                case DockEdge.Bottom: r.Top = r.Bottom - t; break;
            }
            return r;
        }

        // ---------- 띠 계산 (DockStack 전용) ----------
        //
        // 여기 있는 것들은 작업영역(rcWork)을 '자리의 근거' 로 쓰지 않는다.
        // 어떤 변의 rcWork 경계는 그 변에 등록된 바들의 '띠 안쪽 경계 중 최솟값' 일 뿐이고
        // 합이 아니다(실측). 그래서 바깥 바가 빠지면 아무도 앉아 있지 않은 띠까지 포함한
        // 값이 나오고, 그 값을 믿고 다시 앉으면 정확히 빠진 두께만큼 화면 끝에 구멍이 남는다.

        /// <summary>그 변에서 '화면 안쪽' 으로 가는 방향. Left/Top 은 +, Right/Bottom 은 -.</summary>
        internal static int Inward(DockEdge e)
        {
            return (e == DockEdge.Left || e == DockEdge.Top) ? 1 : -1;
        }

        /// <summary>그 변의 바깥쪽(화면 끝에 가까운) 경계값.</summary>
        internal static double OuterOf(Rect area, DockEdge e)
        {
            switch (e)
            {
                case DockEdge.Left: return area.Left;
                case DockEdge.Top: return area.Top;
                case DockEdge.Right: return area.Right;
                default: return area.Bottom;   // Bottom
            }
        }

        /// <summary>그 변에서 작업영역이 모니터보다 얼마나 안쪽인가 (이 프로세스가 보는 픽셀).</summary>
        internal static double InsetOf(ScreenInfo scr, DockEdge e)
        {
            if (scr == null) return 0;
            switch (e)
            {
                case DockEdge.Left: return scr.Work.Left - scr.Bounds.Left;
                case DockEdge.Top: return scr.Work.Top - scr.Bounds.Top;
                case DockEdge.Right: return scr.Bounds.Right - scr.Work.Right;
                default: return scr.Bounds.Bottom - scr.Work.Bottom;
            }
        }

        /// <summary>그 변의 두께 방향 길이 (모니터 전체 기준).</summary>
        internal static double SpanOf(ScreenInfo scr, DockEdge e)
        {
            if (scr == null) return 0;
            return (e == DockEdge.Left || e == DockEdge.Right) ? scr.Bounds.Width : scr.Bounds.Height;
        }

        /// <summary>
        /// outer 에서 안쪽으로 thick 만큼 들어간 띠.
        ///
        /// 이웃한 두 띠가 경계값 하나를 함께 쓰도록 부르는 쪽에서 outer 를 이어 붙인다.
        /// 사이에 틈을 두면 그 틈은 통째로 죽은 공간이 된다 —
        /// 실측: 두 바 사이에 18px 을 띄웠더니 확보량이 50 이 아니라 68 이 나왔다.
        /// </summary>
        internal static Rect BandAt(Rect area, DockEdge e, double outer, double thick)
        {
            if (thick < 1) thick = 1;
            double a = outer, b = outer + Inward(e) * thick;
            double lo = Math.Min(a, b), hi = Math.Max(a, b);
            if (e == DockEdge.Left || e == DockEdge.Right)
                return new Rect(lo, area.Top, hi - lo, Math.Max(1, area.Height));
            return new Rect(area.Left, lo, Math.Max(1, area.Width), hi - lo);
        }

        internal static RECT ToRECT(Rect r)
        {
            var q = new RECT();
            q.Left = (int)Math.Round(r.Left);
            q.Top = (int)Math.Round(r.Top);
            q.Right = (int)Math.Round(r.Right);
            q.Bottom = (int)Math.Round(r.Bottom);
            return q;
        }

        /// <summary>모니터 하나 안에서 '우리 좌표 -> 물리 좌표' 배율. 못 구하면 false.</summary>
        internal static bool Ratios(ScreenInfo scr, out double rx, out double ry)
        {
            rx = 1; ry = 1;
            if (scr == null || scr.Phys.IsEmpty) return false;
            if (scr.Bounds.Width <= 0 || scr.Bounds.Height <= 0) return false;
            rx = scr.Phys.Width / scr.Bounds.Width;
            ry = scr.Phys.Height / scr.Bounds.Height;
            if (rx < 0.2 || rx > 5 || ry < 0.2 || ry > 5) { rx = 1; ry = 1; return false; }
            return true;
        }

        /// <summary>
        /// 이 프로세스가 보는 픽셀 -> 진짜 물리 픽셀.
        ///
        /// 예전 Reserve 는 '작업영역 여백을 배율로 옮겨 심는' 방식이었는데, 그 작업영역이
        /// 붙을 때 캐시해 둔 낡은 값이라 다시 앉힐 때마다 어긋났다.
        /// 한 모니터 안에서는 단순 비례이므로 그 모니터 자신의 원점과 배율로 한 번에 옮긴다.
        /// (모서리를 점 단위로 각각 변환하면 얇은 띠에서 두께가 통째로 틀어진다)
        /// </summary>
        internal static Rect ToPhys(ScreenInfo scr, Rect proc)
        {
            double rx, ry;
            if (proc.Width <= 0 || proc.Height <= 0) return proc;
            if (!Ratios(scr, out rx, out ry)) return proc;
            return new Rect(scr.Phys.Left + (proc.Left - scr.Bounds.Left) * rx,
                            scr.Phys.Top + (proc.Top - scr.Bounds.Top) * ry,
                            Math.Max(1, proc.Width * rx), Math.Max(1, proc.Height * ry));
        }

        // ---------- 이름 <-> 값 ----------

        public static string Name(DockEdge e)
        {
            switch (e)
            {
                case DockEdge.Left: return "left";
                case DockEdge.Top: return "top";
                case DockEdge.Right: return "right";
                case DockEdge.Bottom: return "bottom";
                default: return "none";
            }
        }

        public static DockEdge Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return DockEdge.None;
            switch (s.ToLowerInvariant())
            {
                case "left": return DockEdge.Left;
                case "top": return DockEdge.Top;
                case "right": return DockEdge.Right;
                case "bottom": return DockEdge.Bottom;
                default: return DockEdge.None;
            }
        }
    }

    /// <summary>
    /// 창 하나를 작업표시줄처럼 화면 가장자리에 등록한다.
    ///
    /// 예전에는 이 상태가 Dock 안의 정적 변수 하나였다. 본 창만 붙을 수 있었기 때문이다.
    /// 이제는 조각 창(날씨·즐겨찾기·시계)도 각자 붙을 수 있어야 해서 창마다 하나씩 갖는다.
    ///
    /// ★ ABM_REMOVE 를 빠뜨리면 확보한 공간이 로그오프할 때까지 남는다 ★
    ///   그래서 살아 있는 것들을 _live 에 모아 두고, 프로세스가 끝날 때 통째로 지운다.
    /// </summary>
    internal sealed class AppBar
    {
        private static readonly List<AppBar> _live = new List<AppBar>();

        private IntPtr _hwnd = IntPtr.Zero;
        private uint _callbackMsg;
        private DockEdge _edge = DockEdge.None;

        /// <summary>
        /// 우리가 확보한 변과 두께 (이 프로세스가 보는 픽셀).
        ///
        /// ★ 이 값을 자리 계산에 쓰지 마라 ★ 진단용이다.
        ///   예전에는 "작업영역에는 우리 몫도 빠져 있으니 이 값으로 되돌린다" 는 용도였는데,
        ///   scr.Phys 가 비어 배율이 1 로 뭉개지면 셸이 실제로 뺀 양과 어긋나 폭주했다.
        ///   덧붙여, 예전 주석의 "ABM_REMOVE 뒤 갱신 시점은 셸 마음" 은 사실이 아니다.
        ///   SHAppBarMessage 는 동기다 - 호출이 돌아온 순간 GetMonitorInfo 는 이미 새 값이다
        ///   (실측: 호출 자체가 24ms 블록, 0ms 샘플이 이미 갱신됨).
        /// </summary>
        public DockEdge ReservedEdge { get; private set; }
        public int ReservedPx { get; private set; }

        public bool Registered { get { return _hwnd != IntPtr.Zero; } }

        public void SetEdge(DockEdge e) { _edge = e; }

        /// <summary>AppBar 로 등록한다. 이미 등록돼 있으면 아무 것도 하지 않는다.</summary>
        public bool Register(Window w, uint callbackMessage)
        {
            if (Registered) return true;
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return false;

                var d = new Dock.APPBARDATA();
                d.cbSize = Marshal.SizeOf(typeof(Dock.APPBARDATA));
                d.hWnd = h;
                d.uCallbackMessage = callbackMessage;

                if (Dock.SHAppBarMessage(Dock.ABM_NEW, ref d) == IntPtr.Zero) return false;

                _hwnd = h;
                _callbackMsg = callbackMessage;
                lock (_live) _live.Add(this);
                return true;
            }
            catch { return false; }
        }

        /// <summary>등록을 해제하고 확보한 공간을 돌려준다. 몇 번 불러도 안전하다.</summary>
        public void Unregister()
        {
            if (!Registered) return;
            try
            {
                var d = new Dock.APPBARDATA();
                d.cbSize = Marshal.SizeOf(typeof(Dock.APPBARDATA));
                d.hWnd = _hwnd;
                d.uCallbackMessage = _callbackMsg;
                Dock.SHAppBarMessage(Dock.ABM_REMOVE, ref d);
            }
            catch { }
            _hwnd = IntPtr.Zero;
            ReservedEdge = DockEdge.None;
            ReservedPx = 0;
            lock (_live) _live.Remove(this);
        }

        /// <summary>남아 있는 등록을 전부 지운다. 프로세스가 끝날 때 부른다.</summary>
        public static void UnregisterAll()
        {
            AppBar[] all;
            lock (_live) all = _live.ToArray();
            foreach (var b in all) b.Unregister();
        }

        /// <summary>
        /// 스택 전체를 한 띠로 확보한다. 창을 다 앉힌 뒤에 부를 것.
        ///
        /// 셸은 AppBar 가 어느 모니터에 붙는지를 넘겨준 사각형이 아니라
        /// 그 창이 지금 놓인 위치로 판단한다. 옮기기 전에 부르면 보조 모니터에 붙여도
        /// 공간이 주 모니터 기준으로 잡히거나 아예 안 잡힌다.
        ///
        /// ★ 한 변에서 이것을 부르는 AppBar 는 반드시 하나여야 한다 ★
        ///   셸은 같은 변의 남의 띠에 '닿기만 해도' 요청을 잘라낸다. 잘린 사각형은
        ///   높이 0 이거나 위아래가 뒤집혀 돌아온다(실측).
        ///   특히 바깥 바가 자기 자리를 다시 청하면 안쪽 형제 때문에 반드시 거부된다.
        ///   대표 하나만 등록하면 부딪힐 '남의 띠' 가 우리 안에 없어 그 왕복이 사라진다.
        ///
        /// ★ 돌려주는 rc 를 반드시 읽는다 ★
        ///   예전 Reserve 는 이 반환값을 버려서, 셸이 우리 띠를 높이 0 이나 역전 사각형으로
        ///   뭉갠 순간을 전혀 눈치채지 못한 채 창만 그 자리에 앉혔다.
        /// </summary>
        /// <param name="bandProc">이 프로세스가 보는 픽셀 단위의 띠. 물리 환산은 여기서 한다.</param>
        /// <returns>청한 두께 그대로 받았으면 true.</returns>
        public bool ReserveSpan(ScreenInfo scr, DockEdge edge, Rect bandProc)
        {
            if (!Registered || scr == null) return false;
            if (bandProc.Width < 1 || bandProc.Height < 1) return false;
            try
            {
                Dock.RECT want = Dock.ToRECT(Dock.ToPhys(scr, bandProc));

                var d = new Dock.APPBARDATA();
                d.cbSize = Marshal.SizeOf(typeof(Dock.APPBARDATA));
                d.hWnd = _hwnd;
                d.uCallbackMessage = _callbackMsg;
                d.uEdge = Dock.EdgeCode(edge);
                d.rc = want;

                Dock.SHAppBarMessage(Dock.ABM_SETPOS, ref d);

                bool vert = (edge == DockEdge.Left || edge == DockEdge.Right);
                int wantT = vert ? (want.Right - want.Left) : (want.Bottom - want.Top);
                int gotT = vert ? (d.rc.Right - d.rc.Left) : (d.rc.Bottom - d.rc.Top);


                ReservedEdge = edge;
                if (gotT < wantT - 1)
                {
                    ReservedPx = 0;      // 잘렸거나 뒤집혔다 = 확보 못 했다
                    return false;
                }
                ReservedPx = (int)Math.Round(vert ? bandProc.Width : bandProc.Height);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 바를 맨 위로 올리거나 맨 아래로 내린다.
        /// 전체화면 앱이 뜨면 셸이 ABN_FULLSCREENAPP 으로 알려주는데,
        /// 그때 비켜주지 않으면 게임 위에 계속 떠 있게 된다.
        /// </summary>
        public void SetZOrder(bool onTop)
        {
            if (!Registered) return;
            try
            {
                Dock.SetWindowPos(_hwnd, onTop ? Dock.HWND_TOPMOST : Dock.HWND_BOTTOM,
                                  0, 0, 0, 0, Dock.SWP_NOSIZE | Dock.SWP_NOMOVE | Dock.SWP_NOACTIVATE);
            }
            catch { }
        }

        /// <summary>창을 옮긴 뒤 셸에 알린다.</summary>
        public void NotifyPosChanged()
        {
            if (!Registered) return;
            try
            {
                var d = new Dock.APPBARDATA();
                d.cbSize = Marshal.SizeOf(typeof(Dock.APPBARDATA));
                d.hWnd = _hwnd;
                d.uCallbackMessage = _callbackMsg;
                Dock.SHAppBarMessage(Dock.ABM_WINDOWPOSCHANGED, ref d);
            }
            catch { }
        }
    }


    /// <summary>한 변에 나란히 서는 바 하나. 본 창과 조각 창이 함께 구현한다.</summary>
    internal interface IDockBar
    {
        Window BarWindow { get; }
        AppBar BarAppBar { get; }
        uint BarCallbackMsg { get; }
        DockEdge BarEdge { get; }          // None 이면 지금 안 붙어 있다
        string BarDevice { get; }          // 붙은 모니터. 안 붙었으면 null
        int BarOrder { get; }              // 같은 변에서의 차례. 작을수록 화면 끝에 가깝다
        int BarThicknessPx { get; }        // 이 프로세스가 보는 픽셀
        bool BarActive { get; }            // 거짓이면 줄에서 빠진다 (섹션을 껐다)
        bool BarCentered { get; }          // 참이면 줄을 나눠 써도 화면 한가운데에 놓는다
        int BarOverhangPx { get; }         // 두께 중 '화면에서 안 뺏을' 몫 (호버로 커질 자리)
        bool BarOwnRow { get; }            // 참이면 한 줄을 통째로 쓴다 (시세 바)
        int BarLengthPx { get; }           // 한 줄을 나눠 쓸 때 원하는 길이. 0 이면 알아서
        void PlaceBar(Rect procPx);        // 계산된 자리에 창을 앉힌다 (DIP 변환은 창이 한다)
        void SetBarFullScreen(bool full);  // 전체화면 앱이 떴다/사라졌다
    }

    /// <summary>
    /// 같은 변에 붙은 바들을 우리가 직접 쌓는다.
    ///
    /// ───────────────────────────────────────────────────────────────
    /// 왜 작업영역(rcWork)을 자리의 근거로 쓰지 않는가
    /// ───────────────────────────────────────────────────────────────
    /// 어떤 변의 rcWork 경계는 '그 변에 등록된 바들의 띠 안쪽 경계 중 최솟값' 이다.
    /// 합이 아니다(실측, 4회 독립 확인). 확보는 언제나 '띠 안쪽 경계 -> 화면 끝' 이라,
    /// 바깥 바가 빠져도 남은 바가 자기가 앉지도 않은 바깥 띠를 계속 예약한 채로 있다.
    /// 그때 잰 rcWork 는 정확히 빠진 바의 두께만큼 작고, 그 값을 믿고 다시 앉으면
    /// 화면 끝에 그 두께만큼 구멍이 남는다. ← 64px 틈의 정체다.
    ///
    /// 그래서 자리는 이렇게 정한다.
    ///   (1) '남(작업표시줄·타사 바)이 먹은 두께' 를 우리 확보가 하나도 없는 순간에 직접 잰다
    ///   (2) 그 바깥 끝에서 우리 두께 목록을 이어 붙여 띠를 만든다
    ///   (3) 확보는 대표 하나가 스택 전체를 한 띠로 청한다
    ///
    /// ───────────────────────────────────────────────────────────────
    /// 이미 실패한 네 가지를 여기서 어떻게 피하는가
    /// ───────────────────────────────────────────────────────────────
    /// 1) "자리를 매번 다시 재기" -> 폭주.
    ///    여기서는 rcWork 를 자리 계산에 안 쓴다. AnchorInset 은 우리 것이 하나도 없을 때
    ///    잰 값이고, 그 뒤로도 다시 잴 때는 반드시 먼저 내려놓는다(MeasureAnchor).
    /// 2) "잴 때만 ABM_REMOVE 하고 바로 재기" -> 옛 값이 나온다고 판단했으나 오진이었다.
    ///    SHAppBarMessage 는 동기다(실측: REMOVE 호출 24ms 블록, 반환 시점에 이미 갱신).
    ///    그때 실패한 진짜 이유는 지연이 아니라 형제 바가 그대로 등록돼 있어 값이 여전히
    ///    min 규칙에 오염돼 있었기 때문이다. 여기서는 그 변에 우리 등록이 대표 하나뿐이라
    ///    하나만 내리면 오염이 완전히 사라진다.
    /// 3) "확보량을 기억했다가 되돌리기" -> 배율이 1 로 뭉개지면 어긋나 폭주.
    ///    여기에는 뺄셈으로 만든 기준값이 하나도 없다. 전부 직접 잰 값이다.
    /// 4) "붙을 때 한 번만 재기" -> 같은 변에 둘이면 바깥 64px 틈.
    ///    (2)가 이것을 구조적으로 없앤다. 바깥 바가 빠져도 남은 바의 자리는
    ///    '화면 끝 + 남의 몫' 에서 다시 세므로 화면 끝에 아무것도 안 남는다.
    ///
    /// ★ UI 스레드에서만 부를 것 ★ 창 속성을 만진다.
    /// </summary>
    internal static class DockStack
    {
        private sealed class Slot
        {
            public string Device;
            public DockEdge Edge;
            public bool HasAnchor;
            public double AnchorInset;    // 남이 먹은 두께. 우리 것이 없을 때 직접 잰 값
            public double SettledInset;   // 확보를 마친 직후 실제로 측정한 작업영역 여백
            public IDockBar Holder;       // 이 변에서 유일하게 셸에 등록된 바(대표)
            public int LastMeasureTick;
            public int FixTick;           // 마지막으로 셸에 밀린 것을 되돌린 때
            public int FixCount;          // 연달아 몇 번 되돌렸나. 계속 싸우면 접는다
        }

        private static readonly List<IDockBar> _bars = new List<IDockBar>();
        private static readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>();

        /// <summary>
        /// 바마다 '우리가 앉힌 자리'. 셸이 창을 밀어냈는지 보려고 들고 있다.
        ///
        /// ★ 셸은 우리 창을 옮긴다 ★
        ///   그 자리가 이미 예약돼 있으면(다른 AppBar, 또는 죽었지만 아직 안 걷힌 유령)
        ///   셸이 우리 창에 WM_MOVE 를 보내 안쪽으로 밀어낸다. 확보 요청은 통과시켜 놓고
        ///   창만 밀어내므로, 창과 확보가 어긋난 채로 남는다 - 화면 끝에 그만큼 구멍이 뜬다.
        ///   실측으로 스택까지 확인했다(Window.WmMoveChanged, 우리 코드가 아니다).
        /// </summary>
        private static readonly Dictionary<IDockBar, Rect> _placed = new Dictionary<IDockBar, Rect>();
        private static bool _busy;

        /// <summary>지금 우리가 자리를 다시 잡는 중인가. 셸 알림을 되받지 않으려고 본다.</summary>
        public static bool Busy { get { return _busy; } }

        // ---------- 바깥에서 부르는 것 ----------

        public static void Add(IDockBar b)
        {
            if (b != null && !_bars.Contains(b)) _bars.Add(b);
        }

        /// <summary>
        /// 바가 떨어졌거나 창이 닫힌다.
        /// ★ 그 창이 자기 Edge 를 None 으로 바꾸기 '전' 에 불러야 한다 ★
        ///   Edge 가 이미 None 이면 어느 변을 다시 쌓아야 하는지 알 수 없다.
        /// </summary>
        public static void Leave(IDockBar b)
        {
            if (b == null) return;
            string dev = b.BarDevice;
            DockEdge edge = b.BarEdge;

            _bars.Remove(b);
            _placed.Remove(b);

            // 떠나는 바의 등록을 먼저 내린다. 남겨 두면 남은 대표의 요청을 셸이 잘라낸다(실측).
            try { b.BarAppBar.Unregister(); } catch { }

            if (string.IsNullOrEmpty(dev) || edge == DockEdge.None) return;
            Slot slot = SlotOf(dev, edge);
            if (ReferenceEquals(slot.Holder, b)) slot.Holder = null;

            Apply(dev, edge, true);   // 남은 바들이 화면 끝으로 한 칸씩 당겨진다
        }

        /// <summary>그 모니터·그 변을 다시 쌓는다.</summary>
        public static void Apply(string device, DockEdge edge, bool remeasure)
        {
            if (_busy || string.IsNullOrEmpty(device) || edge == DockEdge.None) return;
            _busy = true;
            try { Layout(device, edge, remeasure); }
            catch { }
            finally { _busy = false; }

            CascadeToPerpendicular(device, edge);
        }

        /// <summary>
        /// 직각인 변도 같이 다시 쌓는다.
        ///
        /// ★ 한 변의 확보가 바뀌면 다른 변의 자리도 바뀐다 ★
        ///   아래 바의 좌우 끝은 작업영역에서 뽑는다. 즉 세로 바가 먹은 만큼 들어와 있다.
        ///   그런데 바뀐 변 하나만 다시 쌓으면, 세로 바를 떼어 낸 뒤에도 아래 바는
        ///   옛 작업영역 기준의 자리에 그대로 남는다.
        ///   (실측: 주 모니터 왼쪽에서 날씨를 치웠는데 즐겨찾기 바가 계속 x=118 에서 시작했다.
        ///    그 모니터 작업영역은 이미 X=0 이었다.)
        ///
        /// ★ 한 겹만 내려간다 ★
        ///   직각인 변을 고치면 그쪽 확보도 바뀌어 다시 이쪽에 영향을 준다. 끝까지 쫓아가면
        ///   두 변이 서로를 밀며 깜빡인다. 한 번만 고치고 만다 - 남는 오차는 셸이 보내는
        ///   ABN_POSCHANGED 가 다음 기회에 정리한다.
        /// </summary>
        private static void CascadeToPerpendicular(string device, DockEdge changed)
        {
            if (_cascading) return;
            _cascading = true;
            try
            {
                bool vert = (changed == DockEdge.Left || changed == DockEdge.Right);
                DockEdge a = vert ? DockEdge.Top : DockEdge.Left;
                DockEdge b = vert ? DockEdge.Bottom : DockEdge.Right;

                if (Members(device, a).Count > 0) Apply(device, a, true);
                if (Members(device, b).Count > 0) Apply(device, b, true);
            }
            catch { }
            finally { _cascading = false; }
        }

        private static bool _cascading;

        public static void ApplyFor(IDockBar b, bool remeasure)
        {
            if (b == null) return;
            Apply(b.BarDevice, b.BarEdge, remeasure);
        }

        /// <summary>
        /// 셸이 ABN_POSCHANGED / ABN_WINDOWARRANGE 를 보냈다.
        ///
        /// ★ 여기서 무조건 다시 쌓으면 무한 되돌이가 된다 ★
        ///   다시 쌓기는 ABM_REMOVE 로 작업영역을 넓혔다가 ABM_SETPOS 로 도로 줄인다.
        ///   그 두 번의 '진짜 변화' 가 셸을 통해 우리에게 알림으로 되돌아온다.
        ///   그래서 두 겹으로 끊는다.
        ///     (1) 지금 여백이 '확보를 마치고 실측해 둔 값' 그대로면 아무것도 하지 않는다.
        ///         이론값이 아니라 실측값과 비교하는 것이 핵심이다 - 이론값과 비교하면
        ///         혼합 DPI 반올림 때문에 보조 모니터에서 영원히 불일치가 난다.
        ///     (2) 그래도 달라졌으면, 마지막 측정 뒤 750ms 안에는 다시 재지 않는다.
        /// </summary>
        public static void OnShellChanged(IDockBar b)
        {
            if (_busy || b == null) return;
            string dev = b.BarDevice;
            DockEdge edge = b.BarEdge;
            if (string.IsNullOrEmpty(dev) || edge == DockEdge.None) return;

            Slot slot = SlotOf(dev, edge);
            ScreenInfo scr = Dock.ScreenByDevice(Dock.AllScreens(), dev);
            if (scr == null) { Apply(dev, edge, true); return; }   // 모니터가 사라졌다

            // ★ 밀림은 시간 제한보다 먼저 본다 ★
            //   셸은 등록 직후에 창을 한 번 밀어낸다. 그 WM_MOVE 는 우리가 자리를 잡은
            //   바로 다음에 오므로, 다시 재기 제한(750ms)에 걸리면 영영 못 고친다.
            //   되돌리기는 셸을 부르지 않고 창만 옮기므로 값싸다.
            List<IDockBar> here = Members(dev, edge);
            if (Drifted(here)) { Reassert(slot, here); return; }

            // 여기부터는 다시 재는 길이다. 너무 자주 재지 않는다.
            if (unchecked(Environment.TickCount - slot.LastMeasureTick) < 750) return;

            const double Tol = 4;   // 혼합 DPI 반올림 여유 (2배 가상화에서 셸 최소 단위가 2)
            if (slot.HasAnchor && Math.Abs(Dock.InsetOf(scr, edge) - slot.SettledInset) <= Tol)
                return;

            Apply(dev, edge, true);
        }

        /// <summary>
        /// 전체화면 앱이 떴다/사라졌다.
        /// ABN_FULLSCREENAPP 은 셸에 등록된 바 - 그 변의 대표 하나 - 에게만 온다.
        /// 나머지는 등록돼 있지 않으므로 여기서 대신 전해 준다. 같은 프로세스라 그냥 부르면 된다.
        /// </summary>
        public static void SetFullScreen(bool full)
        {
            for (int i = 0; i < _bars.Count; i++)
            {
                IDockBar b = _bars[i];
                if (b == null || b.BarEdge == DockEdge.None) continue;
                try { b.SetBarFullScreen(full); } catch { }
            }
        }

        /// <summary>
        /// 화면 구성이 바뀌었다(해상도·모니터 연결). 재어 둔 값을 통째로 버린다.
        ///
        /// ★ 장부만 지우면 안 된다 ★
        ///   셸에는 확보가 그대로 살아 있으므로, 바로 다음에 잰 작업영역은 우리 몫이 빠진
        ///   값이다. 그 값을 '남이 먹은 몫' 으로 믿는 순간 실패한 시도 1번(잴 때마다
        ///   안쪽으로 파고드는 폭주)이 글자 그대로 되살아난다. 반드시 ABM_REMOVE 가 먼저다.
        /// </summary>
        public static void Invalidate()
        {
            foreach (var kv in _slots)
            {
                Slot s = kv.Value;
                ReleaseHolder(s);            // <- 먼저 내린다. 이 순서를 뒤집지 마라.
                s.HasAnchor = false;
                s.SettledInset = 0;
                s.LastMeasureTick = 0;
            }
        }

        // ---------- 배치 ----------

        private static void Layout(string device, DockEdge edge, bool remeasure)
        {
            Slot slot = SlotOf(device, edge);

            // ★ 실패할 수 있는 것은 전부 '내려놓기 전' 에 끝낸다 ★
            //   등록을 내려놓은 뒤에 return 하면 되돌릴 사람이 없다. 바는 확보 없이
            //   떠 있고, 대표가 아니게 된 창은 ABN_POSCHANGED 도 못 받아 회복 경로가 끊긴다.
            ScreenInfo scr = Dock.ScreenByDevice(Dock.AllScreens(), device);
            if (scr == null)
            {
                // 모니터가 사라졌다(뽑기·해상도 변경). 확보만 놓아주고 창은 건드리지 않는다.
                // 여기서 창까지 옮기면 없는 좌표로 날아간다. WM_DISPLAYCHANGE 로 돌아온다.
                ReleaseHolder(slot);
                slot.HasAnchor = false;
                return;
            }

            List<IDockBar> mine = Members(device, edge);
            if (mine.Count == 0)
            {
                ReleaseHolder(slot);
                slot.HasAnchor = false;
                slot.SettledInset = 0;
                return;
            }

            // ★ 줄로 묶는다 ★
            //   시세 바는 흐르는 값이 길어 한 줄을 통째로 써야 한다.
            //   나머지(날씨·즐겨찾기·시계)는 짧아서 한 줄에 나란히 세우는 편이 자리를 덜 먹는다.
            List<List<IDockBar>> rows = Rows(mine);

            double total = 0;
            for (int i = 0; i < rows.Count; i++) total += RowThick(rows[i]);

            double cap = Dock.SpanOf(scr, edge) * 0.45;   // 무슨 일이 있어도 절반은 남긴다
            if (cap < 8) return;
            if (total > cap) total = cap;

            // 여기서부터는 도중에 빠져나가지 않는다.
            if (remeasure || !slot.HasAnchor)
            {
                ScreenInfo fresh = MeasureAnchor(slot, scr, edge);
                if (fresh != null) scr = fresh;
            }

            Rect free = FreeArea(scr, edge, slot.AnchorInset);
            double outer = Dock.OuterOf(free, edge);

            // (1) 창을 먼저 앉힌다. 셸 호출이 없으므로 실패할 것이 없다.
            //     셸은 AppBar 가 어느 모니터에 붙는지를 rc 가 아니라 그 창이 놓인 자리로 판단한다.
            double at = outer, used = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                double t = RowThick(rows[i]);
                if (used + t > total) t = total - used;
                if (t < 4) break;
                PlaceRow(rows[i], Dock.BandAt(free, edge, at, t), edge);
                at += Dock.Inward(edge) * t;
                used += t;
            }

            // (2) 확보는 대표 하나가 스택 전체를 한 띠로 한다.
            //     나머지는 반드시 등록을 내린다 - 남겨 두면 그 띠가 대표의 요청을 잘라낸다.

            IDockBar lead = PickHolder(slot, mine);
            for (int i = 0; i < mine.Count; i++)
            {
                if (ReferenceEquals(mine[i], lead)) continue;
                try { mine[i].BarAppBar.Unregister(); } catch { }
            }

            slot.Holder = null;
            if (lead != null)
            {
                lead.BarAppBar.SetEdge(edge);
                if (lead.BarAppBar.Register(lead.BarWindow, lead.BarCallbackMsg))
                {
                    slot.Holder = lead;
                    // 호버로 커질 자리까지 뺏으면 평소에 빈 띠가 남는다. 그 몫은 빼고 청한다.
                    double keep = used - OverhangOf(rows);
                    if (keep < 8) keep = used;
                    lead.BarAppBar.ReserveSpan(scr, edge, Dock.BandAt(free, edge, outer, keep));
                    lead.BarAppBar.NotifyPosChanged();
                }
            }

            // ★ 등록·확보 과정에서 셸이 창을 밀어낸다 ★
            //   확보 요청은 우리가 청한 그대로 통과시켜 놓고(실측: want == got),
            //   창에는 WM_MOVE 를 보내 작업영역 안쪽으로 밀어 넣는다.
            //   그대로 두면 창은 확보한 띠 바로 위에 앉아 화면 끝이 그만큼 빈다.
            //   확보가 끝난 뒤 한 번 더 앉히면 셸이 더는 건드리지 않는다.
            for (int i = 0; i < mine.Count; i++)
            {
                Rect keep;
                if (!_placed.TryGetValue(mine[i], out keep)) continue;
                try { mine[i].PlaceBar(keep); }
                catch { }
            }

            // (3) 셸이 실제로 얼마나 내줬는지 적어 둔다.
            //     OnShellChanged 는 이 값하고만 비교한다. 이론값(AnchorInset + used)과 비교하면
            //     보조 모니터의 물리 반올림 때문에 영원히 불일치가 나 깜빡임 루프에 갇힌다.
            ScreenInfo after = Dock.ScreenByDevice(Dock.AllScreens(), device);
            slot.SettledInset = (after != null) ? Dock.InsetOf(after, edge) : slot.AnchorInset + used;
        }

        /// <summary>
        /// '남이 먹은 두께' 를 정직하게 잰다.
        ///
        /// ★ 우리 확보를 내려놓은 순간에만 정직한 값이 나온다 ★
        ///   rcWork 의 그 변은 등록된 바들의 '띠 안쪽 경계 중 최솟값' 이라,
        ///   우리 확보가 살아 있는 동안 잰 값에는 우리 몫이 섞여 있다.
        ///   내리자마자 재도 된다 - SHAppBarMessage 는 동기다(실측).
        /// </summary>
        private static ScreenInfo MeasureAnchor(Slot slot, ScreenInfo scr, DockEdge edge)
        {
            ReleaseHolder(slot);
            slot.LastMeasureTick = Environment.TickCount;
            slot.FixCount = 0;   // 새로 재는 것이니 지난 싸움은 잊는다

            ScreenInfo fresh = Dock.ScreenByDevice(Dock.AllScreens(), scr.Device);
            if (fresh == null) fresh = scr;
            slot.AnchorInset = Math.Max(0, Dock.InsetOf(fresh, edge));
            slot.HasAnchor = true;
            return fresh;
        }

        /// <summary>
        /// 그 변에서 우리가 쓸 수 있는 자리.
        ///
        /// 두께 방향은 '모니터 끝 + 남이 먹은 몫' 에서 시작한다. 작업영역이 아니다.
        /// 변을 따라가는 길이는 지금 작업영역에서 가져온다 - 거기에는 직각으로 붙은
        /// 바(작업표시줄·우리 세로 바)의 몫이 이미 빠져 있어 모서리에서 겹치지 않는다.
        /// </summary>
        /// <summary>
        /// 그 변에 우리가 세워 둔 줄들의 두께 합.
        ///
        /// 슬롯을 뒤지지 않고 지금 붙어 있는 바에서 바로 센다 - 슬롯을 조회하면 없을 때
        /// 새로 만들어져, 아무도 안 붙은 변에 빈 슬롯이 쌓인다.
        /// </summary>
        private static double OwnThickness(string device, DockEdge edge)
        {
            List<IDockBar> mine = Members(device, edge);
            if (mine.Count == 0) return 0;

            List<List<IDockBar>> rows = Rows(mine);
            double t = 0;
            for (int i = 0; i < rows.Count; i++) t += RowThick(rows[i]);
            return t;
        }

        private static Rect FreeArea(ScreenInfo scr, DockEdge edge, double anchorInset)
        {
            Rect b = scr.Bounds, w = scr.Work;
            double wl = w.Left, wt = w.Top, wr = w.Right, wb = w.Bottom;
            if (wr - wl < 8) { wl = b.Left; wr = b.Right; }
            if (wb - wt < 8) { wt = b.Top; wb = b.Bottom; }

            // ★ 모서리는 세로 바가 갖는다 ★
            //   작업영역은 우리 가로 바가 먹은 자리까지 빼고 준다. 그대로 쓰면 세로 바가
            //   가로 바 앞에서 멈춰, 두 바가 만나는 모서리가 어느 쪽에도 안 덮인다.
            //   거기로 바탕화면이 네모나게 비친다.
            //
            //   **우리 몫만 되돌린다.** 작업표시줄이 먹은 자리까지 덮으면 셸이 도로 밀어낸다.
            //   가로 바 쪽(Top/Bottom)은 이미 작업영역의 좌우를 쓰므로 모서리를 내어 준다 -
            //   양쪽이 서로 양보하면 아무도 안 덮으니, 한쪽이 갖게 하는 것이 맞다.
            if (edge == DockEdge.Left || edge == DockEdge.Right)
            {
                double t2 = wt - OwnThickness(scr.Device, DockEdge.Top);
                double b2 = wb + OwnThickness(scr.Device, DockEdge.Bottom);
                if (t2 < b.Top) t2 = b.Top;
                if (b2 > b.Bottom) b2 = b.Bottom;
                wt = t2;
                wb = b2;
            }

            switch (edge)
            {
                case DockEdge.Left:
                    return new Rect(b.Left + anchorInset, wt,
                                    Math.Max(1, b.Width - anchorInset), Math.Max(1, wb - wt));
                case DockEdge.Right:
                    return new Rect(b.Left, wt,
                                    Math.Max(1, b.Width - anchorInset), Math.Max(1, wb - wt));
                case DockEdge.Top:
                    return new Rect(wl, b.Top + anchorInset,
                                    Math.Max(1, wr - wl), Math.Max(1, b.Height - anchorInset));
                default:   // Bottom
                    return new Rect(wl, b.Top,
                                    Math.Max(1, wr - wl), Math.Max(1, b.Height - anchorInset));
            }
        }

        // ---------- 잔손 ----------

        /// <summary>
        /// 셸이 밀어낸 창을 도로 앉힌다.
        ///
        /// ★ 끝없이 싸우지 않는다 ★
        ///   그 자리가 정말 남의 것이면 셸은 계속 밀어낼 것이다. 몇 번 해보고 안 되면
        ///   셸 말을 따른다 - 깜빡이며 싸우는 것보다 한 칸 안쪽에 얌전히 있는 편이 낫다.
        /// </summary>
        private static void Reassert(Slot slot, List<IDockBar> mine)
        {
            int now = Environment.TickCount;
            if (unchecked(now - slot.FixTick) > 2000) slot.FixCount = 0;   // 잠잠했으면 다시 센다
            if (slot.FixCount >= 3) return;

            slot.FixTick = now;
            slot.FixCount++;

            for (int i = 0; i < mine.Count; i++)
            {
                Rect keep;
                if (!_placed.TryGetValue(mine[i], out keep)) continue;
                try { mine[i].PlaceBar(keep); }
                catch { }
            }
        }

        /// <summary>창을 앉히고 그 자리를 적어 둔다.</summary>
        private static void Put(IDockBar b, Rect band)
        {
            _placed[b] = band;
            try { b.PlaceBar(band); }
            catch { }
        }

        /// <summary>
        /// 줄의 띠 안에서 제 두께만 차지하게 깎는다. 화면 바깥쪽 변에 붙인다.
        ///
        /// ★ 한 줄에 모였다고 다 같이 두꺼워지면 안 된다 ★
        ///   줄 두께는 그 줄에서 가장 두꺼운 것에 맞춰 잡는다. 그 값을 그대로
        ///   모두에게 주면, 얇은 시세 바가 즐겨찾기 아이콘만큼 부풀어 오른다.
        ///   저마다 제 두께로 앉되 화면 끝에 나란히 붙으면 한 줄로 보인다.
        /// </summary>
        /// <summary>
        /// 그 줄의 '속이 찬' 두께. 호버로 커질 몫은 뺀 값이다.
        ///
        /// 이 값이 줄의 바닥선이 된다. 얇은 바도 여기까지는 늘려야 한 줄로 이어져 보인다 -
        /// 안 그러면 두꺼운 바 옆에 턱이 생겨 그 틈으로 바탕화면이 비친다.
        /// 호버 몫은 빼고 잰다. 그 몫을 품은 바는 어차피 투명해서 턱이 안 보인다.
        /// </summary>
        private static double SolidThick(List<IDockBar> row)
        {
            double t = 0;
            for (int i = 0; i < row.Count; i++)
            {
                double x = Thick(row[i]) - row[i].BarOverhangPx;
                if (x > t) t = x;
            }
            return t;
        }

        private static Rect Fit(IDockBar b, Rect band, DockEdge edge, double floor)
        {
            double own = Thick(b);
            if (own < floor) own = floor;
            switch (edge)
            {
                case DockEdge.Bottom:
                    if (own >= band.Height) return band;
                    return new Rect(band.Left, band.Bottom - own, band.Width, own);
                case DockEdge.Top:
                    if (own >= band.Height) return band;
                    return new Rect(band.Left, band.Top, band.Width, own);
                case DockEdge.Left:
                    if (own >= band.Width) return band;
                    return new Rect(band.Left, band.Top, own, band.Height);
                case DockEdge.Right:
                    if (own >= band.Width) return band;
                    return new Rect(band.Right - own, band.Top, own, band.Height);
            }
            return band;
        }

        /// <summary>
        /// 셸이 우리 창을 앉힌 자리에서 밀어냈는가.
        ///
        /// 밀려났다는 것은 그 자리가 이미 남의 것이라는 뜻이므로, 다시 재서 그 몫을
        /// 반영해 앉아야 한다. 반대로 남이 물러났는데 우리가 안쪽에 남아 있는 경우도
        /// 이 검사가 잡아낸다 - 그때는 다시 재면 화면 끝으로 당겨진다.
        /// </summary>
        private static bool Drifted(List<IDockBar> mine)
        {
            for (int i = 0; i < mine.Count; i++)
            {
                IDockBar b = mine[i];
                Rect want;
                if (!_placed.TryGetValue(b, out want)) continue;
                if (b.BarWindow == null) continue;
                try
                {
                    double sx, sy;
                    Dock.GetDpiScale(b.BarWindow, out sx, out sy);
                    if (Math.Abs(b.BarWindow.Left * sx - want.Left) > 4) return true;
                    if (Math.Abs(b.BarWindow.Top * sy - want.Top) > 4) return true;
                }
                catch { }
            }
            return false;
        }

        /// <summary>대표는 되도록 바꾸지 않는다. 바뀔 때마다 REMOVE/NEW 왕복이 생긴다.</summary>
        private static IDockBar PickHolder(Slot slot, List<IDockBar> mine)
        {
            if (slot.Holder != null && mine.Contains(slot.Holder)) return slot.Holder;
            return mine.Count > 0 ? mine[0] : null;
        }

        private static void ReleaseHolder(Slot slot)
        {
            if (slot.Holder == null) return;
            try { slot.Holder.BarAppBar.Unregister(); } catch { }
            slot.Holder = null;
        }

        /// <summary>그 모니터 그 변의 우리 바들. 바깥(화면 끝)부터 차례로.</summary>
        /// <summary>
        /// 같은 변·같은 모니터에 다른 바가 또 있나.
        ///
        /// 배경을 정할 때 쓴다. 혼자 떠 있는 바는 바탕화면 위에 글자만 얹는 편이 낫지만,
        /// 옆에 다른 바가 붙어 있으면 그 배경을 이어받아야 한 줄로 보인다.
        /// </summary>
        public static bool HasCompanion(IDockBar b)
        {
            if (b == null || b.BarEdge == DockEdge.None) return false;
            return Members(b.BarDevice, b.BarEdge).Count > 1;
        }

        private static List<IDockBar> Members(string device, DockEdge edge)
        {
            var list = new List<IDockBar>();
            if (string.IsNullOrEmpty(device) || edge == DockEdge.None) return list;
            for (int i = 0; i < _bars.Count; i++)
            {
                IDockBar b = _bars[i];
                if (b == null || b.BarWindow == null) continue;
                if (b.BarEdge != edge || b.BarDevice != device) continue;
                // 꺼진 섹션은 줄에서 뺀다. 확보한 자리도 같이 돌려줘야 하므로
                // 여기서 빠지는 것만으로 충분하다 - 남은 바들이 그만큼 바깥으로 당겨진다.
                if (!b.BarActive) continue;
                list.Add(b);
            }
            // 차례는 창 종류로 고정한다(본 창 0, 날씨 1, 즐겨찾기 2, 시계 3).
            // 붙은 순서로 매기면 다시 켤 때마다 앞뒤가 달라진다.
            list.Sort(delegate(IDockBar x, IDockBar y) { return x.BarOrder.CompareTo(y.BarOrder); });
            return list;
        }

        private static double Thick(IDockBar b)
        {
            double t = b.BarThicknessPx;
            return t < 4 ? 4 : t;
        }

        /// <summary>
        /// 바들을 줄로 묶는다. 제 줄을 쓰는 바(시세)는 혼자, 나머지는 전부 한 줄에.
        /// 차례(BarOrder)는 이미 정렬돼 있으므로 그 순서가 그대로 화면 끝부터의 순서가 된다.
        /// </summary>
        private static List<List<IDockBar>> Rows(List<IDockBar> mine)
        {
            var rows = new List<List<IDockBar>>();
            List<IDockBar> shared = null;
            for (int i = 0; i < mine.Count; i++)
            {
                IDockBar b = mine[i];
                if (b.BarOwnRow)
                {
                    var one = new List<IDockBar>();
                    one.Add(b);
                    rows.Add(one);
                    continue;
                }
                if (shared == null) { shared = new List<IDockBar>(); rows.Add(shared); }
                shared.Add(b);
            }
            return rows;
        }

        /// <summary>줄들이 품고 있는 '안 뺏을 몫' 의 합.</summary>
        private static double OverhangOf(List<List<IDockBar>> rows)
        {
            double t = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                double m = 0;
                for (int k = 0; k < rows[i].Count; k++)
                {
                    double o = rows[i][k].BarOverhangPx;
                    if (o > m) m = o;
                }
                t += m;
            }
            return t;
        }

        /// <summary>한 줄의 두께. 그 줄에서 가장 두꺼운 것에 맞춘다.</summary>
        private static double RowThick(List<IDockBar> row)
        {
            double t = 0;
            for (int i = 0; i < row.Count; i++)
            {
                double x = Thick(row[i]);
                if (x > t) t = x;
            }
            return t < 4 ? 4 : t;
        }

        /// <summary>
        /// 한 줄에 든 바들을 변을 따라 나란히 앉힌다.
        ///
        /// 저마다 원하는 길이(BarLengthPx)의 비율대로 나누되 **띠를 정확히 채운다.**
        /// 남기면 그 자리에 바탕화면이 비쳐 줄이 끊겨 보이고, 확보한 자리도 놀게 된다.
        /// </summary>
        private static void PlaceRow(List<IDockBar> row, Rect band, DockEdge edge)
        {
            if (row.Count == 0) return;
            if (row.Count == 1) { Put(row[0], Fit(row[0], band, edge, 0)); return; }

            bool vert = (edge == DockEdge.Left || edge == DockEdge.Right);
            double span = vert ? band.Height : band.Width;
            if (span < row.Count) return;

            double floor = SolidThick(row);   // 얇은 바도 여기까지는 늘려 턱을 없앤다

            // ★ 0 은 '작다' 가 아니라 '남는 자리를 내가 갖는다' 는 뜻이다 ★
            //   시세 바는 길이를 안 밝힌다(0). 그걸 최소값 40 으로 치면 줄에서 가장
            //   작은 몫을 받고, 알맹이가 몇 줄뿐인 날씨가 나머지를 다 차지해
            //   화면 절반이 빈 어둠이 된다. 길이를 밝힌 쪽이 제 만큼 갖고,
            //   안 밝힌 쪽이 남는 자리를 나눠 갖는 것이 옳다.
            var want = new double[row.Count];
            double sum = 0;
            int fillers = 0;
            for (int i = 0; i < row.Count; i++)
            {
                double L = row[i].BarLengthPx;
                if (L <= 0) { want[i] = 0; fillers++; continue; }
                if (L < 40) L = 40;
                want[i] = L;
                sum += L;
            }
            if (fillers > 0)
            {
                double left = span - sum;
                double each = left / fillers;
                if (each < 40) each = 40;          // 남는 게 없으면 최소한만 주고 같이 줄인다
                for (int i = 0; i < row.Count; i++)
                    if (want[i] <= 0) { want[i] = each; sum += each; }
            }
            if (sum <= 0) return;

            double scale = span / sum;
            double at = vert ? band.Top : band.Left;
            double end = vert ? band.Bottom : band.Right;

            // ★ 가운데에 두어야 하는 바가 있으면 양옆을 같은 폭으로 비운다 ★
            //   옆 바가 한쪽에만 있으면 남은 자리의 한가운데는 화면 한가운데가 아니다.
            //   반대쪽도 같은 만큼 비워야 눈에 가운데로 보인다. 그 빈 자리는
            //   투명한 바가 덮고 있을 뿐이라 아무것도 잃지 않는다.
            int mid = -1;
            for (int i = 0; i < row.Count; i++) if (row[i].BarCentered) { mid = i; break; }

            if (mid >= 0)
            {
                double before = 0, after = 0;
                for (int i = 0; i < row.Count; i++)
                {
                    if (i == mid) continue;
                    if (i < mid) before += want[i] * scale;
                    else after += want[i] * scale;
                }
                double side = before > after ? before : after;
                if (side * 2 < span - 20)
                {
                    // 옆 바들을 제 길이대로 놓고, 가운데 바는 남는 한가운데를 차지한다
                    double p = vert ? band.Top : band.Left;
                    for (int i = 0; i < row.Count; i++)
                    {
                        double L = (i == mid) ? (span - side * 2) : want[i] * scale;
                        if (i == mid) p = (vert ? band.Top : band.Left) + side;
                        if (L < 1) L = 1;

                        Rect rc = vert ? new Rect(band.Left, p, Math.Max(1, band.Width), L)
                                       : new Rect(p, band.Top, L, Math.Max(1, band.Height));
                        Put(row[i], Fit(row[i], rc, edge, floor));
                        if (i == mid) p = (vert ? band.Top : band.Left) + span - side;
                        else p += L;
                    }
                    return;
                }
            }

            for (int i = 0; i < row.Count; i++)
            {
                double L = want[i] * scale;
                if (i == row.Count - 1) L = end - at;   // 반올림 나머지는 마지막이 흡수한다
                if (L < 1) L = 1;

                Rect r = vert ? new Rect(band.Left, at, Math.Max(1, band.Width), L)
                              : new Rect(at, band.Top, L, Math.Max(1, band.Height));
                Put(row[i], Fit(row[i], r, edge, floor));
                at += L;
            }
        }

        private static Slot SlotOf(string device, DockEdge edge)
        {
            string k = device + "|" + Dock.Name(edge);
            Slot s;
            if (!_slots.TryGetValue(k, out s))
            {
                s = new Slot();
                s.Device = device;
                s.Edge = edge;
                _slots[k] = s;
            }
            return s;
        }
    }
}
