// 분리된 조각 창.
//
// 카드 하나에 몰려 있던 섹션(날씨·즐겨찾기·시계)을 따로 떼어 각자 창으로 띄운다.
// 섹션의 화면 요소를 '그대로 옮겨 담는' 방식이라 데이터 루프도 편집 모드도 손댈 것이 없다.
//
// 서로 가장자리가 닿으면 한 덩어리가 되어 같이 움직인다.
// 덩어리를 따로 기억해 두지는 않는다. 끌기 시작할 때 '지금 닿아 있는 것들' 을
// 그때그때 훑는다. 붙였다 떼는 것이 곧 묶었다 푸는 것이 되어 규칙이 단순해진다.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;

namespace DeskWidget
{
    internal sealed class PanelWindow : Window, IDockBar
    {
        /// <summary>지금 떠 있는 조각 창들. 붙이기 판정에 쓴다.</summary>
        private static readonly List<PanelWindow> _all = new List<PanelWindow>();

        /// <summary>본 창(시세). 조각들이 여기에도 붙을 수 있어야 한다.</summary>
        public static Window Main;

        /// <summary>본 창이 덩어리에 딸려 움직였을 때 알린다. 자리 저장은 본 창이 한다.</summary>
        public static Action MainMoved;

        /// <summary>본 창이 가장자리에 붙어 있는가. 본 창이 채운다.</summary>
        public static Func<bool> MainDocked;

        /// <summary>끌기를 마쳤다. 화면 밖으로 나간 창이 있으면 본 창이 되돌린다.</summary>
        public static Action Lost;

        /// <summary>
        /// 본 창을 덩어리에 끌어들여도 되는가.
        ///
        /// 붙어 있는 바는 자리가 셸과의 약속이라 우리가 옮기면 안 된다.
        /// 옮기면 바가 화면 끝에서 한 단씩 밀려 올라가고, 그 자리가 저장까지 된다.
        /// </summary>
        private static bool MainJoinable()
        {
            if (Main == null) return false;
            try { if (MainDocked != null && MainDocked()) return false; }
            catch { }
            return true;
        }

        public const double SnapGap = 14;    // 이 안에 들어오면 붙는다
        private const double Touch = 3;      // 이 정도면 '닿아 있다' 로 본다

        // ---------- 가장자리에 붙기 ----------
        //
        // 본 창과 같은 방식이다. 얇은 바로 앉고, 그만큼 화면 공간을 확보한다.
        // ★ 같은 변에 여럿이 붙어도 셸은 쌓아 주지 않는다 ★ 자동 배치는 없고, 겹치거나
        //   닿는 요청은 옮겨 주는 대신 잘라낸다(실측). 쌓는 일은 DockStack 이 한다.
        //   그래서 확보(ABM_SETPOS)는 그 변의 대표 하나만 하고, 나머지는 등록조차 안 한다.

        /// <summary>지금 붙어 있는 변. None 이면 떠 있다.</summary>
        public DockEdge Edge = DockEdge.None;

        /// <summary>다시 켰을 때 붙을 모니터(\\.\DISPLAYn). 비어 있으면 창이 놓인 자리로 고른다.</summary>
        public string PreferDevice;

        /// <summary>지금 붙어 있는 모니터. 설정에 남겨 두었다가 다음에 켤 때 쓴다.</summary>
        public string DockDevice
        {
            get { return _dockScreen != null ? _dockScreen.Device : null; }
        }

        /// <summary>바에 실을 내용을 만들어 준다 (세로 바면 true). 본 창이 채운다.</summary>
        public Func<bool, UIElement> MakeBarContent;

        /// <summary>붙거나 떨어졌을 때 알린다. 설정 저장은 본 창이 한다.</summary>
        public Action<DockEdge> EdgeChanged;

        /// <summary>붙기 전 자리. 뗄 때 여기로 돌아온다.</summary>
        public double UndockX = double.NaN, UndockY = double.NaN;

        private const uint PanelCallbackMsg = 0x0402;   // WM_USER + 2 (본 창은 +1)
        private const double BarThickness = 22;         // 위·아래에 붙었을 때 (DIP)
        private const double BarThicknessSide = 64;     // 좌·우는 가로쓰기가 들어가야 해서 넓다
        // 아이콘만 싣는 바. 호버로 커질 몫까지 미리 품고 있어야 한다 -
        // 창은 제 테두리 밖을 못 그리므로, 두께가 모자라면 커진 아이콘이 잘린다.
        private const double BarThicknessIcons = 58;

        /// <summary>
        /// 좌·우에 붙었을 때의 폭.
        /// 날씨·시계는 글자가 들어가 넓어야 하지만, 즐겨찾기는 아이콘뿐이라 절반이면 충분하다.
        /// </summary>
        private double SideThickness
        {
            get { return Key == "즐겨찾기" ? BarThicknessIcons : BarThicknessSide; }
        }

        /// <summary>
        /// 이 바의 기본 두께.
        /// 즐겨찾기는 가로로 붙든 세로로 붙든 아이콘만 실으므로 두께가 같아야 한다 -
        /// 얇은 쪽(22)에 맞추면 아이콘이 눌려 잘린다.
        /// </summary>
        private double ThicknessFor(bool vertical)
        {
            if (Key == "즐겨찾기") return BarThicknessIcons;
            // 가로 날씨 바는 글자가 작아 잘 안 보였다. 알맹이를 1.5배로 키우고 두께도 그만큼.
            // 상하 여백을 준 만큼 두께도 늘려야 글자가 눌리지 않는다
            if (Key == "날씨" && !vertical) return BarThickness * WeatherZoom * 1.25;
            return vertical ? BarThicknessSide : BarThickness;
        }

        /// <summary>가로 날씨 바를 이만큼 키운다. 두께와 알맹이가 같은 값을 본다.</summary>
        public const double WeatherZoom = 1.3;

        /// <summary>이 바의 두께 (DIP). 바에 실을 것의 크기를 여기에 맞춘다.</summary>
        public double BarThicknessDip
        {
            get
            {
                bool vertical = (Edge == DockEdge.Left || Edge == DockEdge.Right);
                return ThicknessFor(vertical) * _scale.ScaleX;
            }
        }

        private readonly AppBar _appBar = new AppBar();
        private ScreenInfo _dockScreen;
        // _dockArea 는 지웠다. 작업영역을 자리의 근거로 삼던 필드이고, 그것이 64px 틈의 원인이다.
        private bool _relayoutPending;
        private bool _moveWatch;
        private Border _barBox;
        private Grid _barHost;        // 알맹이 + 두께 손잡이를 함께 담는다
        private Border _barContent;   // RebuildBar 가 갈아끼우는 것은 이 안쪽뿐이다
        private Border _barGrip;      // 안쪽 가장자리 - 끌어서 두께 조절
        private Border _barGrab;      // 배경이 없는 바에서 '잡을 수 있는' 띠 (Palette.Grab)
        private bool _topmost;

        private readonly Border _card;
        private readonly ScaleTransform _scale;
        private readonly Action<double, double> _onMoved;
        private Action<double> _onScaled;

        public string Key { get; private set; }

        public PanelWindow(string key, UIElement content, double scale,
                           Action<double, double> onMoved, Action<double> onScaled)
        {
            Key = key;
            _onMoved = onMoved;
            _onScaled = onScaled;

            Title = "오늘은 - " + key;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            _scale = new ScaleTransform(scale, scale);

            // 오른쪽 아래 모서리를 잡아 크기를 바꾼다.
            // 손잡이를 카드 '안' 에 넣어야 배율이 커질 때 같이 커져 손끝을 따라온다.
            // 크기 조절 손잡이가 오른쪽 아래 모서리에 얹히므로, 알맹이가 그 밑으로 들어가지 않게
            // 오른쪽을 조금 비워 둔다. 시계처럼 한 줄짜리는 손잡이와 글자가 겹쳐 읽기 나빴다.
            _contentHost = new Border { Child = content, Padding = new Thickness(0, 0, 9, 0) };

            // ★ 머리 버튼은 알맹이 '위에 얹지' 않고 '위에 놓는다' ★
            //   얹으면 시계처럼 낮은 카드에서 글자를 가린다. 마우스 올렸을 때만 띄워도 봤지만
            //   그때그때 사라지는 것이 더 성가셨다. 한 줄을 내주면 아무것도 안 가리고 늘 보인다.
            var inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            UIElement tools = BuildHeaderTools();
            Grid.SetRow(tools, 0);
            Grid.SetRow(_contentHost, 1);
            inner.Children.Add(tools);
            inner.Children.Add(_contentHost);

            var body = new Grid();
            body.Children.Add(inner);
            body.Children.Add(BuildFoldStrip());
            body.Children.Add(BuildSizeGrip());

            _card = new Border
            {
                Child = body,
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = Palette.CardEdge,
                Background = Palette.Card,
                Padding = new Thickness(14, 10, 14, 11),
                Margin = new Thickness(4),
                LayoutTransform = _scale,
            };
            Content = _card;

            MouseLeftButtonDown += OnDragStart;
            MouseMove += OnDragMove;
            MouseLeftButtonUp += OnDragEnd;

            _all.Add(this);
            // ★ Leave 는 Edge 를 None 으로 바꾸기 전에 ★ 남은 바들이 바깥으로 당겨져 자리를 메운다.
            Closed += delegate { _all.Remove(this); try { DockStack.Leave(this); } catch { } _appBar.Unregister(); };
            Closing += delegate { try { DockStack.Leave(this); } catch { } _appBar.Unregister(); };

            SourceInitialized += delegate
            {
                var src = PresentationSource.FromVisual(this) as HwndSource;
                if (src != null) src.AddHook(AppBarHook);
            };
        }

        public void SetScale(double s)
        {
            _scale.ScaleX = s;
            _scale.ScaleY = s;
            // 붙어 있으면 두께도 배율을 따른다. 두께만 바뀐 것이라 남의 몫은 다시 재지 않는다.
            if (Edge != DockEdge.None)
            {
                RebuildBar();   // 아이콘 크기도 배율을 따라간다
                DockStack.ApplyFor(this, false);
            }
        }

        /// <summary>항상 위 설정. 붙어 있는 동안에는 늘 위에 있어야 의미가 있다.</summary>
        public void SetTopmost(bool on)
        {
            _topmost = on;
            Topmost = on || Edge != DockEdge.None;
        }

        // ---------- 붙이기 ----------

        /// <summary>가장자리에 붙는다. None 이면 떼어낸다.</summary>
        public void DockTo(DockEdge edge)
        {
            if (edge == DockEdge.None) { Undock(); return; }

            if (Edge == DockEdge.None)
            {
                UndockX = Left;
                UndockY = Top;
                _dockScreen = null;   // 지난번 모니터 기억을 지운다 (WidgetWindow.ApplyDock 주석 참고)
            }
            Edge = edge;

            if (_barBox == null)
            {
                _barBox = new Border
                {
                    ClipToBounds = true,
                    ToolTip = "안쪽으로 끌면 떨어집니다 · 더블클릭해도 됩니다",
                };
                AttachBarGestures(_barBox);

                // ★ 손잡이는 알맹이와 따로 만든다 ★
                //   두께를 끌면 SetScale -> RebuildBar 로 알맹이가 새로 만들어진다.
                //   손잡이가 거기 섞여 있으면 끄는 도중에 제가 사라져 마우스 캡처가
                //   풀리고 드래그가 한 번 움직이고 끊긴다. 그래서 한 번만 만들어 둔다.
                _barContent = new Border();
                _barGrip = BuildBarThicknessGrip();
                _barGrab = new Border { Background = Palette.Grab };

                _barHost = new Grid();
                _barHost.Children.Add(_barGrab);      // 맨 아래 - 알맹이가 없는 곳을 받아 준다
                _barHost.Children.Add(_barContent);
                _barHost.Children.Add(_barGrip);
                _barBox.Child = _barHost;
            }

            SizeToContent = SizeToContent.Manual;
            Content = _barBox;
            Topmost = true;

            // 붙는 방향이 바뀌면 배경도 다시 정한다
            _barBox.Background = BarBackdrop(edge == DockEdge.Top || edge == DockEdge.Bottom);
            RebuildBar();

            // ★ 자리는 작업영역에서 뽑지 않는다 ★ DockStack 주석 참고.
            //   등록도 여기서 하지 않는다 - 그 변의 대표가 누구인지는 DockStack 이 정한다.
            if (DockScreen() == null) return;

            // 셸이 밀어내는 것을 듣는다 (WidgetWindow 와 같은 이유)
            if (!_moveWatch)
            {
                _moveWatch = true;
                LocationChanged += delegate { if (Edge != DockEdge.None) RelayoutSoon(); };
            }

            DockStack.Add(this);
            DockStack.Apply(_dockScreen.Device, edge, true);   // 붙을 때는 남의 몫을 새로 잰다

            // 꺼두거나 닫아둔 섹션이면 붙어도 보이지 않는다. 줄에서도 빠져 자리를 차지하지 않는다.
            if (!_active) Visibility = Visibility.Hidden;

            if (EdgeChanged != null) EdgeChanged(edge);
        }

        /// <summary>가장자리에서 떼고 원래 조각 창으로 돌아간다.</summary>
        public void Undock()
        {
            if (Edge == DockEdge.None) return;

            // ★ Edge 를 None 으로 바꾸기 전에 부른다 ★
            //   Leave 가 내 등록을 내리고, 남은 바들을 화면 끝부터 다시 앉힌다.
            try { DockStack.Leave(this); } catch { }

            Edge = DockEdge.None;
            _appBar.Unregister();
            _dockScreen = null;

            Content = _card;
            SizeToContent = SizeToContent.WidthAndHeight;
            Width = double.NaN;
            Height = double.NaN;
            Topmost = _topmost;

            if (!double.IsNaN(UndockX) && !double.IsNaN(UndockY))
            {
                Left = UndockX;
                Top = UndockY;
            }

            if (EdgeChanged != null) EdgeChanged(DockEdge.None);

            // ★ 화면 안으로 들여놓는다 ★
            //   붙어 있던 자리는 화면 가장자리다. 거기서 카드로 돌아오면 카드가
            //   가장자리 밖으로 걸치고, 오른쪽·아래 변이었으면 통째로 나가 버린다.
            //   SizeToContent 라 지금은 크기가 아직 안 잡혔으므로 두 번 부른다 -
            //   지금 한 번(대충이라도 들여놓고), 크기가 잡힌 뒤 한 번 더(정확히).
            ClampIntoScreen();
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)delegate
                {
                    ClampIntoScreen();
                    Report();
                });
            }
            catch { }

            Report();
        }

        /// <summary>
        /// 창을 지금 놓인 모니터 안으로 들인다.
        ///
        /// ★ 가상 화면의 감싸는 사각형을 쓰면 안 된다 ★
        ///   ㄱ자 배치에는 어느 모니터에도 속하지 않는 빈 구역이 생긴다. 그 안으로 들여놓으면
        ///   계산상 '화면 안' 인데 눈에는 안 보인다. 반드시 **실제 모니터** 하나에 맞춘다.
        ///
        /// 창보다 모니터가 좁으면 왼쪽·위를 맞춘다 - 오른쪽·아래를 맞추면 제목 줄과
        /// 손잡이가 밖으로 나가 잡을 데가 없어진다.
        /// </summary>
        private void ClampIntoScreen()
        {
            try
            {
                double sx, sy;
                Dock.GetDpiScale(this, out sx, out sy);
                if (sx <= 0 || sy <= 0) return;

                double w = ActualWidth > 0 ? ActualWidth : 160;
                double h = ActualHeight > 0 ? ActualHeight : 80;

                var all = Dock.AllScreens();
                if (all == null || all.Count == 0) return;

                // 한가운데가 놓인 모니터. 어디에도 없으면 가장 가까운 것을 고른다.
                var scr = Dock.ScreenAt(all, new Point((Left + w / 2) * sx, (Top + h / 2) * sy));
                if (scr == null) scr = Nearest(all, sx, sy, w, h);
                if (scr == null) return;

                Rect b = scr.Work.Width > 0 ? scr.Work : scr.Bounds;
                if (b.Width <= 0 || b.Height <= 0) return;

                double L = b.Left / sx, T = b.Top / sy, R = b.Right / sx, B = b.Bottom / sy;

                double x = Left, y = Top;
                if (x + w > R) x = R - w;
                if (y + h > B) y = B - h;
                if (x < L) x = L;
                if (y < T) y = T;

                if (Math.Abs(x - Left) > 0.5) Left = x;
                if (Math.Abs(y - Top) > 0.5) Top = y;
            }
            catch { }
        }

        /// <summary>창 한가운데에서 가장 가까운 모니터.</summary>
        private ScreenInfo Nearest(List<ScreenInfo> all, double sx, double sy, double w, double h)
        {
            double cx = (Left + w / 2) * sx, cy = (Top + h / 2) * sy;
            ScreenInfo best = null;
            double bestD = double.MaxValue;

            for (int i = 0; i < all.Count; i++)
            {
                Rect b = all[i].Bounds;
                double dx = cx < b.Left ? b.Left - cx : (cx > b.Right ? cx - b.Right : 0);
                double dy = cy < b.Top ? b.Top - cy : (cy > b.Bottom ? cy - b.Bottom : 0);
                double d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = all[i]; }
            }
            return best;
        }

        /// <summary>
        /// 바 배경.
        ///
        /// 아이콘이나 짧은 글자만 싣는 바는 판을 깔지 않는다 - 바탕화면 위에 그것만 떠 있는 편이 낫다.
        ///
        /// ★ 알파 0 으로는 못 잡는다 (여기서 오래 틀렸다) ★
        ///   Palette.Clear(#00FFFFFF)를 '투명하지만 실재하는' 브러시로 여겨 왔는데, 그것은
        ///   WPF 안에서만 맞는 말이다. AllowsTransparency 창은 WS_EX_LAYERED 라 윈도우가
        ///   픽셀마다 알파를 보고 0 인 자리는 마우스를 아래 창으로 흘려보낸다 - 우리 창에는
        ///   메시지가 닿지도 않으므로 WPF 히트 테스트는 불릴 기회조차 없다.
        ///   실측: 즐겨찾기 바의 빈 곳에서 WindowFromPoint 가 바탕화면을 돌려줬고,
        ///   두께 손잡이는 40번 놓이는 동안 한 번도 눌리지 않았다(grip.log: PLACE 40, DOWN 0).
        ///
        ///   그래서 배경은 그대로 안 깔되, 확보한 띠만큼은 Palette.Grab(알파 1)을 덮는다.
        ///   BuildBarGrab 참고.
        /// </summary>
        private Brush BarBackdrop(bool horizontal)
        {
            if (ClearBackdrop) return Palette.Clear;                  // 사용자가 골랐다
            if (Key == "즐겨찾기") return Palette.Clear;              // 아이콘만

            // ★ 옆에 다른 바가 있으면 그 배경을 이어받는다 ★
            //   Card 는 그라데이션이라 요소마다 제 크기에 맞춰 다시 칠해진다. 긴 시세 바와
            //   짧은 날씨 바에 그대로 주면 경계에서 색이 되돌아가 줄이 보이고,
            //   투명하게 두면 그 자리로 바탕화면이 비쳐 한 줄이 끊겨 보인다.
            //   시세가 끝나는 색 하나로 칠하면 이어진다(Palette.CardEnd 주석).
            //   이 바들은 늘 시세 뒤에 서므로(BarOrder) 끝 색이 맞는 색이다.
            if (DockStack.HasCompanion(this)) return Palette.CardEnd;

            // 혼자 떠 있을 때 - 가로 날씨는 한 줄뿐이라 판을 깔지 않는 편이 낫다
            if (Key == "날씨" && horizontal) return Palette.Clear;

            return Palette.Card;
        }

        /// <summary>
        /// 바 배경을 지운다. 우클릭 메뉴에서 고른다.
        ///
        /// 배경을 지워도 확보한 띠에는 Palette.Grab(알파 1)이 깔려 있어 잡히기는 한다.
        /// 알파 0 으로 두면 OS 가 마우스를 아래 창으로 흘려보내 끌 수도 조절할 수도 없다.
        /// </summary>
        public bool ClearBackdrop
        {
            get { return _clearBackdrop; }
            set
            {
                if (_clearBackdrop == value) return;
                _clearBackdrop = value;
                if (Edge != DockEdge.None && _barBox != null)
                    _barBox.Background = BarBackdrop(Edge == DockEdge.Top || Edge == DockEdge.Bottom);
            }
        }
        private bool _clearBackdrop;

        /// <summary>바에 실은 내용을 다시 만든다. 값이 바뀌면 본 창이 불러 준다.</summary>
        public void RebuildBar()
        {
            if (Edge == DockEdge.None || _barContent == null || MakeBarContent == null) return;
            bool vertical = (Edge == DockEdge.Left || Edge == DockEdge.Right);
            try { _barContent.Child = MakeBarContent(vertical); }
            catch { }
            PlaceBarGrip(vertical);
        }

        /// <summary>손잡이를 '화면 안쪽' 가장자리에 둔다. 붙은 변이 바뀌면 자리도 바뀐다.</summary>
        private void PlaceBarGrip(bool vertical)
        {
            if (_barGrip == null) return;

            const double Grab = 6;
            _barGrip.Width = vertical ? Grab : double.NaN;
            _barGrip.Height = vertical ? double.NaN : Grab;

            _barGrip.HorizontalAlignment = vertical
                ? (Edge == DockEdge.Left ? HorizontalAlignment.Right : HorizontalAlignment.Left)
                : HorizontalAlignment.Stretch;
            _barGrip.VerticalAlignment = vertical
                ? VerticalAlignment.Stretch
                : (Edge == DockEdge.Top ? VerticalAlignment.Bottom : VerticalAlignment.Top);

            _barGrip.Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS;
            PlaceBarGrab(vertical);

            RefreshBackdrop();
        }

        /// <summary>
        /// 배경을 다시 고른다.
        ///
        /// ★ 이웃은 자리를 잡은 뒤에야 알 수 있다 ★
        ///   DockTo 는 RebuildBar 를 부른 **다음에** DockScreen() 으로 모니터를 정한다.
        ///   그 시점에는 _dockScreen 이 비어 있어 BarDevice 가 null 이고, 그러면
        ///   DockStack.HasCompanion 이 같은 변의 이웃을 하나도 못 찾는다.
        ///   그래서 PlaceBar(자리를 다 잡은 뒤)에서도 한 번 더 부른다.
        ///   이웃이 붙거나 떨어질 때마다 PlaceBar 가 도므로 그때그때 따라간다.
        /// </summary>
        private void RefreshBackdrop()
        {
            if (_barBox == null || Edge == DockEdge.None) return;
            try { _barBox.Background = BarBackdrop(Edge == DockEdge.Top || Edge == DockEdge.Bottom); }
            catch { }
        }

        /// <summary>
        /// 배경 없는 바에서 '잡을 수 있는' 띠를 깐다.
        ///
        /// ★ 창 전체를 덮지 않는다 ★
        ///   즐겨찾기 바는 아이콘이 커질 자리까지 품고 있어서, 창이 확보한 띠보다 두껍다.
        ///   그 넘치는 부분은 남의 자리(최대화한 창의 가장자리) 위에 떠 있을 뿐이다.
        ///   거기까지 알파를 주면 남의 창 가장자리 클릭을 우리가 삼킨다.
        ///   그래서 **확보한 만큼만** 덮고 나머지는 그대로 통과시킨다.
        ///
        /// 배경이 있는 바(Palette.Card)는 이미 불투명하므로 이 띠가 필요 없다.
        /// </summary>
        private void PlaceBarGrab(bool vertical)
        {
            if (_barGrab == null) return;

            bool clear = ReferenceEquals(BarBackdrop(!vertical), Palette.Clear)
                      || ReferenceEquals(BarBackdrop(!vertical), Palette.Grab);
            if (!clear || Edge == DockEdge.None)
            {
                _barGrab.Visibility = Visibility.Collapsed;
                return;
            }
            _barGrab.Visibility = Visibility.Visible;

            // 확보한 두께 = 창 두께 - 커질 몫 (BarOverhangPx 와 같은 셈)
            double keep = BarThicknessDip;
            if (Key == "즐겨찾기") keep = keep / HoverRoom;
            if (keep < 4) keep = 4;

            _barGrab.Width = vertical ? keep : double.NaN;
            _barGrab.Height = vertical ? double.NaN : keep;

            // 화면 바깥쪽 변에 붙인다 - 확보한 띠가 거기 있다.
            _barGrab.HorizontalAlignment = vertical
                ? (Edge == DockEdge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right)
                : HorizontalAlignment.Stretch;
            _barGrab.VerticalAlignment = vertical
                ? VerticalAlignment.Stretch
                : (Edge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom);
        }

        /// <summary>
        /// 바 두께 손잡이. 안쪽으로 끌면 두꺼워지고 바깥으로 끌면 얇아진다.
        ///
        /// 배율(_scale)을 움직이는 것이라 아이콘·글자도 같이 커지고 작아진다 -
        /// 두께만 늘리면 알맹이는 그대로라 허전해진다.
        /// </summary>
        private Border BuildBarThicknessGrip()
        {
            var grip = new Border
            {
                Background = Palette.Grab,   // ★ Clear(알파 0) 로 두면 OS 가 흘려보낸다 ★
                ToolTip = "끌어서 두께 조절",
            };

            bool dragging = false;
            double startPos = 0;
            double startScale = 1;

            grip.MouseEnter += delegate { grip.Background = Palette.Hover; };
            grip.MouseLeave += delegate { if (!dragging) grip.Background = Palette.Grab; };

            grip.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (Edge == DockEdge.None) return;
                e.Handled = true;   // 여기서 끊지 않으면 '안쪽으로 끌어 떼기' 로 넘어간다
                dragging = true;

                bool vertical = (Edge == DockEdge.Left || Edge == DockEdge.Right);
                Point c = CursorOnScreen();
                startPos = vertical ? c.X : c.Y;
                startScale = _scale.ScaleX;
                grip.CaptureMouse();
            };

            grip.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!dragging) return;
                if (e.LeftButton != MouseButtonState.Pressed) return;

                bool vertical = (Edge == DockEdge.Left || Edge == DockEdge.Right);

                double sx, sy;
                Dock.GetDpiScale(this, out sx, out sy);

                Point c = CursorOnScreen();
                double moved = (vertical ? c.X : c.Y) - startPos;
                // 화면 안쪽으로 끌면 두꺼워진다. 오른쪽·아래에 붙었으면 방향이 뒤집힌다.
                if (Edge == DockEdge.Right || Edge == DockEdge.Bottom) moved = -moved;

                // 커서는 프로세스 픽셀, 두께는 DIP 다. 단위를 맞추고 나서 나눈다.
                double axis = vertical ? sx : sy;
                double thick = ThicknessFor(vertical);
                if (thick < 1) thick = 1;

                double next = startScale + (moved / axis) / thick;
                if (next < MinScale) next = MinScale;
                if (next > MaxScale) next = MaxScale;
                if (Math.Abs(next - _scale.ScaleX) < 0.005) return;

                SetScale(next);
            };

            grip.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (!dragging) return;
                e.Handled = true;
                dragging = false;
                grip.Background = Palette.Grab;
                grip.ReleaseMouseCapture();
                if (_onScaled != null) _onScaled(_scale.ScaleX);   // 설정에 남긴다
            };

            return grip;
        }

        /// <summary>
        /// 바 내용을 다시 만들고, **자리도 다시 나눈다.**
        ///
        /// ★ 내용만 갈아끼우면 폭은 그대로다 ★
        ///   이 바가 한 줄에서 차지하는 길이는 내용을 재서 정한다(BarLengthPx). 그런데
        ///   RebuildBar 는 그리기만 할 뿐 그 나눔을 다시 하지 않는다. 그래서 '날씨' 라벨
        ///   폭으로 잡혀 있던 자리에 온도·지역이 들어오면 그 자리에서 잘린다.
        /// </summary>
        public void RebuildBarAndFit()
        {
            RebuildBar();
            if (Edge == DockEdge.None) return;
            try { DockStack.ApplyFor(this, false); }
            catch { }
        }

        /// <summary>붙을 모니터를 고른다. ★ 작업영역은 돌려주지 않는다 ★ (WidgetWindow 와 같은 이유)</summary>
        private ScreenInfo DockScreen()
        {
            double sx, sy;
            Dock.GetDpiScale(this, out sx, out sy);

            double w = ActualWidth > 0 ? ActualWidth : 120;
            double h = ActualHeight > 0 ? ActualHeight : 60;
            var all = Dock.AllScreens();
            if (all == null || all.Count == 0) return null;

            // 이미 붙어 있으면 그때 정한 모니터를 계속 쓴다 (Dock.ScreenByDevice 주석 참고)
            ScreenInfo scr = null;
            if (Edge != DockEdge.None && _dockScreen != null)
                scr = Dock.ScreenByDevice(all, _dockScreen.Device);

            // 다시 켰을 때는 지난번에 붙어 있던 모니터를 먼저 본다.
            // 창 위치로 되짚으면 '떠 있을 때의 자리' 를 보고 엉뚱한 화면에 붙는다.
            if (scr == null && !string.IsNullOrEmpty(PreferDevice))
                scr = Dock.ScreenByDevice(all, PreferDevice);

            if (scr == null)
                scr = Dock.ScreenAt(all, new Point((Left + w / 2) * sx, (Top + h / 2) * sy));
            _dockScreen = scr;
            return scr;
        }

        // ---------- IDockBar ----------

        Window IDockBar.BarWindow { get { return this; } }
        AppBar IDockBar.BarAppBar { get { return _appBar; } }
        uint IDockBar.BarCallbackMsg { get { return PanelCallbackMsg; } }
        DockEdge IDockBar.BarEdge { get { return Edge; } }

        string IDockBar.BarDevice
        {
            get { return (Edge != DockEdge.None && _dockScreen != null) ? _dockScreen.Device : null; }
        }

        /// <summary>
        /// 같은 변에서의 차례. 작을수록 화면 끝에 가깝다.
        /// 본 창이 0 이고 조각은 늘 같은 차례로 선다 -
        /// 붙은 순서로 하면 다시 켤 때마다 배치가 달라진다.
        /// </summary>
        int IDockBar.BarOrder
        {
            get
            {
                if (Key == "날씨") return 1;
                if (Key == "즐겨찾기") return 2;
                return 3;   // 시계
            }
        }

        int IDockBar.BarThicknessPx
        {
            get
            {
                double sx, sy;
                Dock.GetDpiScale(this, out sx, out sy);
                return ThicknessPx(sx, sy);
            }
        }

        bool IDockBar.BarActive { get { return _active; } }

        /// <summary>즐겨찾기 아이콘은 화면 한가운데 있어야 보기 좋다. 글자 바는 제자리에 둔다.</summary>
        bool IDockBar.BarCentered { get { return Key == "즐겨찾기"; } }

        /// <summary>
        /// 두께 중 화면에서 뺏지 않을 몫.
        ///
        /// ★ 창이 앉은 두께와 화면에서 뺏는 두께는 같을 필요가 없다 ★
        ///   즐겨찾기 바는 아이콘이 호버로 1.5배 커질 자리까지 품고 있다. 창은 그만큼
        ///   두꺼워야 커진 아이콘이 안 잘리지만, 그 자리까지 작업영역에서 빼면
        ///   평소에는 아무것도 없는 띠가 화면을 밀어낸다 (실측: 169px 중 아이콘은 72px).
        ///   커지는 것은 잠깐이고 바는 늘 맨 위에 있으니, 남는 몫은 바탕화면 위로 넘긴다.
        /// </summary>
        int IDockBar.BarOverhangPx
        {
            get
            {
                if (Key != "즐겨찾기" || Edge == DockEdge.None) return 0;
                double sx, sy;
                Dock.GetDpiScale(this, out sx, out sy);
                return (int)Math.Round(ThicknessPx(sx, sy) * (1 - 1 / HoverRoom));
            }
        }

        /// <summary>아이콘이 커지는 배수. WidgetWindow.MaxGrow 와 같아야 한다.</summary>
        public const double HoverRoom = 1.5;

        /// <summary>조각 바는 짧다. 시세 바와 달리 한 줄을 나눠 쓴다.</summary>
        bool IDockBar.BarOwnRow { get { return false; } }

        /// <summary>담은 내용이 바라는 길이. 줄을 나눌 때 이 비율로 자른다.</summary>
        int IDockBar.BarLengthPx
        {
            get
            {
                try
                {
                    // 손잡이가 아니라 알맹이를 잰다 - 손잡이는 길이를 바라지 않는다
                    if (_barContent == null || _barContent.Child == null) return 0;
                    _barContent.Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Size d = _barContent.Child.DesiredSize;

                    double sx, sy;
                    Dock.GetDpiScale(this, out sx, out sy);

                    bool vert = (Edge == DockEdge.Left || Edge == DockEdge.Right);
                    double dip = vert ? d.Height : d.Width;
                    if (dip < 1) return 0;

                    // ★ 세로 바에서는 여유를 적게 청한다 ★
                    //   같은 변의 시세 바는 길이를 안 밝혀서(0) '남는 자리' 를 갖는다.
                    //   여기서 넉넉히 부르면 그만큼 시세 몫이 줄어 맨 아래 종목이 잘린다.
                    //   가로 바는 글자가 좌우로 붙어 보이면 답답하니 그대로 둔다.
                    double slack = vert ? 3 : 20;
                    return (int)Math.Round((dip + slack) * (vert ? sy : sx));
                }
                catch { return 0; }
            }
        }

        void IDockBar.PlaceBar(Rect procPx)
        {
            if (procPx.Width <= 0 || procPx.Height <= 0) return;

            // ★ 한 번에 옮긴다 ★
            //   WPF 의 Left/Top/Width/Height 는 대입할 때마다 따로 SetWindowPos 를 부른다.
            //   그 사이의 어중간한 크기·자리를 셸이 보고 창을 작업영역 안으로 밀어 넣는다
            //   (실측: 확보 요청은 청한 그대로 통과시켜 놓고 창에만 WM_MOVE 를 보낸다).
            //   그러면 창은 확보한 띠 바로 위에 앉아 화면 끝이 그 두께만큼 빈다.
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                if (h != IntPtr.Zero)
                {
                    // 크기가 아니라 가장자리를 반올림한다 - 옆 바와 사이가 벌어지지 않게.
                    int px, py, pw, ph;
                    Dock.SnapEdges(procPx, out px, out py, out pw, out ph);

                    Dock.SetWindowPos(h, IntPtr.Zero, px, py, pw, ph,
                        Dock.SWP_NOZORDER | Dock.SWP_NOACTIVATE);
                    RefreshBackdrop();   // 이웃이 정해진 뒤라야 배경을 제대로 고른다
                    return;
                }
            }
            catch { }

            // 핸들이 아직 없으면 WPF 쪽으로 앉힌다
            double sx, sy;
            Dock.GetDpiScale(this, out sx, out sy);
            Left = procPx.Left / sx;
            Top = procPx.Top / sy;
            Width = procPx.Width / sx;
            Height = procPx.Height / sy;
        }

        void IDockBar.SetBarFullScreen(bool full)
        {
            try
            {
                Topmost = full ? false : (_topmost || Edge != DockEdge.None);
                if (_appBar.Registered) { _appBar.SetZOrder(!full); return; }
                IntPtr h = new WindowInteropHelper(this).Handle;
                if (h != IntPtr.Zero)
                    Dock.SetWindowPos(h, full ? Dock.HWND_BOTTOM : Dock.HWND_TOPMOST,
                                      0, 0, 0, 0,
                                      Dock.SWP_NOSIZE | Dock.SWP_NOMOVE | Dock.SWP_NOACTIVATE);
            }
            catch { }
        }


        /// <summary>바 두께 (이 프로세스가 보는 픽셀).</summary>
        private int ThicknessPx(double sx, double sy)
        {
            bool vertical = (Edge == DockEdge.Left || Edge == DockEdge.Right);
            double axis = vertical ? sx : sy;
            double thick = ThicknessFor(vertical);
            int t = (int)Math.Round(thick * _scale.ScaleX * axis);
            return t < 14 ? 14 : t;
        }

        /// <summary>자리를 다시 잡는다. 계산도 확보도 DockStack 이 한다.</summary>
        private void Position()
        {
            if (Edge == DockEdge.None) return;
            if (_dockScreen == null && DockScreen() == null) return;
            DockStack.ApplyFor(this, false);
        }

        // Redock() 은 지웠다. 등록을 내렸다 한 박자 뒤에 다시 붙는 것이었는데,
        // 그 한 박자는 정확성에 기여한 적이 없다 - SHAppBarMessage 는 동기다(실측).
        // 게다가 자기 하나만 내려서는 작업영역이 안 바뀐다. 형제가 min 규칙으로
        // 붙들고 있기 때문이다. 그래서 값이 여전히 오염돼 있었다.

        /// <summary>셸이 보내는 AppBar 알림. 자리가 밀리면 다시 잡는다.</summary>
        private IntPtr AppBarHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != (int)PanelCallbackMsg || Edge == DockEdge.None) return IntPtr.Zero;

            const int ABN_POSCHANGED = 0x1;
            const int ABN_FULLSCREENAPP = 0x2;
            const int ABN_WINDOWARRANGE = 0x3;

            int notify = wParam.ToInt32();
            if (notify == ABN_POSCHANGED || notify == ABN_WINDOWARRANGE)
            {
                RelayoutSoon();
            }
            else if (notify == ABN_FULLSCREENAPP)
            {
                // 이 알림은 셸에 등록된 바(그 변의 대표) 에게만 온다. 나머지에도 전한다.
                DockStack.SetFullScreen(lParam != IntPtr.Zero);
            }
            handled = true;
            return IntPtr.Zero;
        }

        /// <summary>셸 쪽이 바뀌었다. 한 박자 뒤에 한 번만. (WidgetWindow.RelayoutSoon 과 같다)</summary>
        private void RelayoutSoon()
        {
            if (DockStack.Busy || _relayoutPending) return;
            _relayoutPending = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, (Action)delegate
            {
                try { DockStack.OnShellChanged(this); }
                catch { }
                finally { _relayoutPending = false; }
            });
        }

        /// <summary>바를 안쪽으로 끌거나 더블클릭하면 떨어진다. 본 창과 같은 손짓이다.</summary>
        private void AttachBarGestures(Border bar)
        {
            bool watching = false;
            Point start = new Point();

            bar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (e.ClickCount == 2) { Undock(); return; }
                watching = true;
                start = CursorOnScreen();
                bar.CaptureMouse();
            };
            bar.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!watching) return;
                var now = CursorOnScreen();

                // 화면 안쪽으로 끌 때만 떼어낸다.
                // 방향을 안 보면 바 위에서 조금만 움직여도 떨어져 나가 성가시다.
                double away;
                switch (Edge)
                {
                    case DockEdge.Left: away = now.X - start.X; break;
                    case DockEdge.Right: away = start.X - now.X; break;
                    case DockEdge.Top: away = now.Y - start.Y; break;
                    default: away = start.Y - now.Y; break;   // Bottom
                }

                double sx, sy;
                Dock.GetDpiScale(this, out sx, out sy);
                if (away < 90 * Math.Max(sx, sy)) return;   // away 는 물리 픽셀

                watching = false;
                bar.ReleaseMouseCapture();
                Undock();
                try { DragMove(); } catch { }
                Report();
            };
            bar.MouseLeftButtonUp += delegate
            {
                if (!watching) return;
                watching = false;
                bar.ReleaseMouseCapture();
            };
        }

        public double Scale { get { return _scale.ScaleX; } }

        // ---------- 머리 버튼 ----------

        private Border _toolsHost;
        private Border _contentHost;

        /// <summary>
        /// 오른쪽 위에 얹는 작은 버튼들. 본 창이 만들어 넘긴다.
        ///
        /// 떼어낸 창은 본 창의 머리에 손이 닿지 않는다. 설정을 열 길도, 접을 길도 없어지므로
        /// 제 머리를 따로 갖는다. 날씨는 제 안에 이미 갖고 있어 본 창이 넘기지 않는다.
        /// </summary>
        public void SetHeaderTools(UIElement tools)
        {
            if (_toolsHost == null) return;
            _toolsHost.Child = tools;
            _toolsHost.Visibility = tools == null ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// 머리 버튼을 위로 더 끌어올린다.
        ///
        /// 즐겨찾기는 아이콘마다 사방 여백(IconPad)이 있어 버튼 아래가 더 벌어져 보인다.
        /// 위아래가 같아 보이게 그만큼 더 올린다. 카드 위 여백(10) 안에서만 움직인다.
        /// </summary>
        public void SetToolsLift(double up)
        {
            if (_toolsHost == null) return;
            if (up < 0) up = 0;
            if (up > 9) up = 9;
            _toolsHost.Margin = new Thickness(0, -up, 0, 3);
        }

        /// <summary>
        /// 머리 버튼 자리.
        ///
        /// ★ 평소에는 숨긴다 ★
        ///   시계처럼 한 줄짜리 카드는 높이가 낮아, 오른쪽 위에 얹힌 버튼이 글자 위로 겹친다.
        ///   자리를 따로 마련하자니 그만큼 카드가 커지고, 작게 만들려던 뜻이 사라진다.
        ///   그래서 마우스를 올렸을 때만 띄운다. 안 쓸 때는 알맹이만 보인다.
        /// </summary>
        private UIElement BuildHeaderTools()
        {
            _toolsHost = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                // 카드 위 여백(10) 안으로 4px 끌어올린다. 그래야 머리선과 눈높이가 맞는다.
                Margin = new Thickness(0, -4, 0, 3),
                Background = Palette.Clear,
                Visibility = Visibility.Collapsed,
            };
            return _toolsHost;
        }

        // ---------- 접었을 때 ----------

        private TextBlock _foldLabel;
        private Border _foldStrip;

        /// <summary>접힌 조각을 되살릴 때 부른다. 본 창이 채운다.</summary>
        public Action Restore;

        /// <summary>
        /// 섹션을 접으면 알맹이 대신 이 줄만 남는다.
        ///
        /// 창째로 숨기지 않는 이유: 따로 떠 있는 창을 숨겨 버리면 다시 펼 방법이 없다.
        /// 본 창의 접기 버튼은 본 창 것이지 이 창 것이 아니다.
        /// </summary>
        private UIElement BuildFoldStrip()
        {
            _foldLabel = new TextBlock
            {
                FontSize = 11,
                Foreground = Palette.TextGhost,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _foldStrip = new Border
            {
                Child = _foldLabel,
                MinWidth = 74,
                Padding = new Thickness(8, 3, 8, 3),
                Background = Palette.Clear,
                Cursor = Cursors.Hand,
                ToolTip = "눌러서 펴기",
                Visibility = Visibility.Collapsed,
            };

            _foldStrip.MouseEnter += delegate { _foldLabel.Foreground = Palette.Text; };
            _foldStrip.MouseLeave += delegate { _foldLabel.Foreground = Palette.TextGhost; };
            _foldStrip.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            _foldStrip.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (Restore != null) Restore();
            };
            return _foldStrip;
        }

        private bool _active = true;

        /// <summary>
        /// 이 섹션을 켰는가 껐는가.
        ///
        /// 떠 있을 때는 '펴기' 줄만 남기면 되지만, 가장자리에 붙어 있을 때는 그것으로 안 된다.
        /// 붙은 바는 알맹이가 _card 가 아니라 _barBox 라 '펴기' 줄이 보이지도 않고,
        /// 무엇보다 화면을 차지한 채로 남는다. 그래서 창을 감추고 줄에서 빠져
        /// 확보한 자리까지 돌려준다.
        /// </summary>
        /// <summary>
        /// 켬/접힘/닫힘을 한 번에 정한다.
        ///
        ///   show=true             편 상태
        ///   show=false, closed=false   접힘 - '펴기' 줄만 남는다
        ///   closed=true           닫힘 - 창이 통째로 사라지고 붙어 있었으면 자리도 내놓는다
        /// </summary>
        public void SetState(bool show, bool closed)
        {
            _closed = closed;
            SetActive(show && !closed);
            if (Edge == DockEdge.None) Visibility = closed ? Visibility.Hidden : Visibility.Visible;
        }

        private bool _closed;

        public void SetActive(bool on)
        {
            // ★ 언제 불려도 결과가 같아야 한다 ★
            //   붙이기(DockTo)는 한 박자 미뤄 실행되므로 이 함수가 붙기 전에도 뒤에도 불린다.
            //   '바뀐 것만' 처리하면 순서에 따라 창이 켜진 채로 남는다.
            bool changed = (_active != on);
            _active = on;

            if (Edge == DockEdge.None) { Visibility = _closed ? Visibility.Hidden : Visibility.Visible; return; }

            Visibility = on ? Visibility.Visible : Visibility.Hidden;

            // 꺼졌으면 등록부터 내린다. 남겨 두면 남은 대표의 요청을 셸이 잘라낸다.
            if (!on) { try { _appBar.Unregister(); } catch { } }

            if (changed) DockStack.ApplyFor(this, true);   // 남은 바들이 그만큼 바깥으로 당겨진다
        }

        /// <summary>접힘 표시. 접혔으면 알맹이 대신 '펴기' 줄이 보인다.</summary>
        public void SetFolded(bool folded)
        {
            if (_foldStrip == null) return;
            _foldStrip.Visibility = folded ? Visibility.Visible : Visibility.Collapsed;
            if (_foldLabel != null) _foldLabel.Text = Key + " 펴기";

            // 접었으면 '펴기' 줄만 남는다. 버튼까지 남으면 빈 카드에 아이콘만 떠 어수선하다.
            if (_toolsHost != null && _toolsHost.Child != null)
                _toolsHost.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
            if (_contentHost != null)
                _contentHost.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---------- 크기 조절 ----------

        private UIElement BuildSizeGrip()
        {
            var dot = new System.Windows.Shapes.Path
            {
                Fill = Palette.GripDot,
                Data = Geometry.Parse("M 11,0 L 11,11 L 0,11 Z"),   // 오른쪽 아래 삼각형
                Opacity = 0.55,
            };

            var grip = new Border
            {
                Child = dot,
                Width = 12,
                Height = 12,
                Background = Palette.Clear,   // 히트 테스트를 받으려면 필요하다
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -5, -4),
                Cursor = Cursors.SizeNWSE,
                ToolTip = "끌어서 크기 조절",
            };

            bool sizing = false;
            Point start = new Point();
            double startScale = 1, startW = 1, startH = 1;
            double dipX = 1, dipY = 1;

            grip.MouseEnter += delegate { dot.Opacity = 1; };
            grip.MouseLeave += delegate { if (!sizing) dot.Opacity = 0.55; };

            grip.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;   // 여기서 끊지 않으면 창 끌기로 넘어간다
                sizing = true;
                start = CursorOnScreen();
                startScale = _scale.ScaleX;
                startW = ActualWidth > 0 ? ActualWidth : 120;
                startH = ActualHeight > 0 ? ActualHeight : 60;
                Dock.GetDpiScale(this, out dipX, out dipY);
                grip.CaptureMouse();
            };

            grip.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!sizing) return;
                if (e.LeftButton != MouseButtonState.Pressed) return;

                var now = CursorOnScreen();
                // 커서는 물리 픽셀, 창 크기는 DIP 다. 먼저 단위를 맞춘다.
                double dx = (now.X - start.X) / dipX;
                double dy = (now.Y - start.Y) / dipY;

                // 대각선으로 끈 만큼 비례해서 키운다. 창 크기 기준이라 큰 창은 덜 예민하다.
                double grow = (dx / startW + dy / startH) / 2;
                double next = startScale * (1 + grow);
                if (next < MinScale) next = MinScale;
                if (next > MaxScale) next = MaxScale;

                SetScale(next);
            };

            grip.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (!sizing) return;
                e.Handled = true;
                sizing = false;
                dot.Opacity = 0.55;
                grip.ReleaseMouseCapture();
                if (_onScaled != null) _onScaled(_scale.ScaleX);
            };

            return grip;
        }

        // 지금 쓰는 크기(1.4~1.5배)의 절반까지 내려갈 수 있어야 바를 얇게 만들 수 있다
        public const double MinScale = 0.5;
        public const double MaxScale = 1.8;

        /// <summary>담고 있던 것을 돌려주고 창을 닫는다. 다시 카드로 합칠 때 쓴다.</summary>
        public UIElement Detach()
        {
            // 알맹이는 body 의 첫 자식(host) 안에 들어 있다. 껍데기가 아니라 알맹이를 돌려준다.
            UIElement c = null;
            if (_contentHost != null) { c = _contentHost.Child; _contentHost.Child = null; }
            _card.Child = null;
            Content = null;
            Close();
            return c;
        }

        // ---------- 끌기 ----------

        private bool _dragging;
        private Point _grab;                      // 커서 자리 (물리 픽셀)
        private double _dipX = 1, _dipY = 1;      // 물리 픽셀 -> DIP 환산 배율
        private List<Window> _group;              // 같이 움직일 창들
        private List<Point> _groupOrigin;

        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 1) return;
            if (Edge != DockEdge.None) return;   // 붙어 있으면 바 제스처가 받는다

            _dragging = true;
            _grab = CursorOnScreen();

            // ★ 커서는 물리 픽셀, Left/Top 은 DIP ★
            //   그냥 더하면 200% 화면에서 창이 커서보다 정확히 2배 멀리 달아난다.
            Dock.GetDpiScale(this, out _dipX, out _dipY);

            _group = ConnectedWith(this);
            _groupOrigin = new List<Point>();
            foreach (var w in _group) _groupOrigin.Add(new Point(w.Left, w.Top));

            CaptureMouse();
            e.Handled = true;
        }

        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }

            var now = CursorOnScreen();
            double dx = (now.X - _grab.X) / _dipX;
            double dy = (now.Y - _grab.Y) / _dipY;

            for (int i = 0; i < _group.Count; i++)
            {
                _group[i].Left = _groupOrigin[i].X + dx;
                _group[i].Top = _groupOrigin[i].Y + dy;
            }
        }

        private void OnDragEnd(object sender, MouseButtonEventArgs e) { EndDrag(); }

        private void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();

            // 화면 가장자리에 닿았으면 거기 붙는다. 붙었으면 이웃에 맞출 것도 없다.
            if (TryDock()) return;

            SnapToNeighbours();
            if (Lost != null) { try { Lost(); } catch { } }   // 빈 구역에 놓였으면 되돌린다

            // 덩어리 전부의 자리를 기억해 둔다
            bool movedMain = false;
            foreach (var w in _group)
            {
                var p = w as PanelWindow;
                if (p != null) p.Report();
                else if (ReferenceEquals(w, Main)) movedMain = true;
            }
            Report();

            // 본 창이 딸려 왔으면 그쪽도 자리를 저장해야 한다
            if (movedMain && MainMoved != null) { try { MainMoved(); } catch { } }
        }

        private void Report()
        {
            if (_onMoved != null) _onMoved(Left, Top);
        }

        /// <summary>끌기를 마친 자리가 화면 가장자리면 거기 붙는다.</summary>
        private bool TryDock()
        {
            if (Edge != DockEdge.None) return false;
            try
            {
                ScreenInfo scr;
                var edge = Dock.DetectEdge(this, new Rect(Left, Top, ActualWidth, ActualHeight), out scr);
                if (edge == DockEdge.None) return false;
                DockTo(edge);
                return true;
            }
            catch { return false; }
        }

        // ---------- 붙이기 ----------

        /// <summary>가까이 오면 가장자리를 맞춰 붙인다.</summary>
        private void SnapToNeighbours()
        {
            double bestDx = 0, bestDy = 0;
            double gapX = SnapGap, gapY = SnapGap;

            foreach (var other in Others())
            {
                double l = Left, t = Top, r = Left + ActualWidth, b = Top + ActualHeight;
                double ol = other.Left, ot = other.Top;
                double or_ = other.Left + other.ActualWidth, ob = other.Top + other.ActualHeight;

                // 세로로 겹칠 때만 좌우로 붙인다
                if (Overlap(t, b, ot, ob) > 0)
                {
                    Try(ref gapX, ref bestDx, ol - r);     // 내 오른쪽 <-> 상대 왼쪽
                    Try(ref gapX, ref bestDx, or_ - l);    // 내 왼쪽 <-> 상대 오른쪽
                    Try(ref gapX, ref bestDx, ol - l);     // 왼쪽 끼리 맞추기
                    Try(ref gapX, ref bestDx, or_ - r);    // 오른쪽 끼리 맞추기
                }
                // 가로로 겹칠 때만 위아래로 붙인다
                if (Overlap(l, r, ol, or_) > 0)
                {
                    Try(ref gapY, ref bestDy, ot - b);
                    Try(ref gapY, ref bestDy, ob - t);
                    Try(ref gapY, ref bestDy, ot - t);
                    Try(ref gapY, ref bestDy, ob - b);
                }
            }

            if (bestDx == 0 && bestDy == 0) return;

            // 덩어리째 옮겨야 붙어 있던 것들이 흩어지지 않는다
            foreach (var w in _group)
            {
                w.Left += bestDx;
                w.Top += bestDy;
            }
        }

        private static void Try(ref double best, ref double keep, double delta)
        {
            double d = Math.Abs(delta);
            if (d >= best) return;
            best = d;
            keep = delta;
        }

        private static double Overlap(double a1, double a2, double b1, double b2)
        {
            return Math.Min(a2, b2) - Math.Max(a1, b1);
        }

        private IEnumerable<Window> Others()
        {
            if (MainJoinable() && !ReferenceEquals(Main, this)) yield return Main;
            foreach (var w in _all)
            {
                if (ReferenceEquals(w, this)) continue;
                if (w.Edge != DockEdge.None) continue;   // 붙어 있는 창은 따라다니지 않는다
                yield return w;
            }
        }

        /// <summary>지금 이 창에 (건너건너라도) 닿아 있는 창들. 자기 자신을 포함한다.</summary>
        public static List<Window> ConnectedWith(Window seed)
        {
            var all = new List<Window>();
            if (MainJoinable()) all.Add(Main);
            foreach (var w in _all) if (w.Edge == DockEdge.None) all.Add(w);

            var group = new List<Window> { seed };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var w in all)
                {
                    if (group.Contains(w)) continue;
                    foreach (var g in group)
                    {
                        if (!Touching(w, g)) continue;
                        group.Add(w);
                        grew = true;
                        break;
                    }
                    if (grew) break;
                }
            }
            return group;
        }

        private static bool Touching(Window a, Window b)
        {
            if (a.ActualWidth <= 0 || b.ActualWidth <= 0) return false;

            double al = a.Left, at = a.Top, ar = a.Left + a.ActualWidth, ab = a.Top + a.ActualHeight;
            double bl = b.Left, bt = b.Top, br = b.Left + b.ActualWidth, bb = b.Top + b.ActualHeight;

            bool vOver = Overlap(at, ab, bt, bb) > 1;
            bool hOver = Overlap(al, ar, bl, br) > 1;

            if (vOver && (Math.Abs(ar - bl) <= Touch || Math.Abs(br - al) <= Touch)) return true;
            if (hOver && (Math.Abs(ab - bt) <= Touch || Math.Abs(bb - at) <= Touch)) return true;
            return false;
        }

        // ---------- 커서 ----------

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint pt);

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        /// <summary>
        /// 커서의 화면 좌표. **물리 픽셀** 단위다 (DIP 아님).
        /// 끌기 중에는 창 기준 좌표가 갱신되지 않는 경우가 있어 화면 절대 좌표를 쓴다
        /// (본 창에서 실측으로 확인한 것과 같은 이유다).
        /// </summary>
        internal static Point CursorOnScreen()
        {
            NativePoint p;
            if (GetCursorPos(out p)) return new Point(p.X, p.Y);
            return new Point();
        }
    }
}
