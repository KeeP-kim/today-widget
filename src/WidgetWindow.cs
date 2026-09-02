// 위젯 본체 UI
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeskWidget
{
    internal sealed class WidgetWindow : Window, IDockBar
    {
        // 여기 값들은 배율 100% 기준이다. 기본 배율이 120% 이므로 화면에서는 1.2배로 보인다.
        private const double CardWidth = 286;
        // 창 가장자리 여백. 그림자를 쓰지 않으므로 안티앨리어싱용으로만 조금 남긴다.
        private const double ShadowMargin = 4;
        private const double IconSize = 55;
        private const double BigIconSize = 96;          // 하나 크게 볼 때의 날씨 아이콘
        private const double WeatherToolsGap = 52;      // 우측 도구(전환·새로고침·접기) 자리

        private readonly Config _cfg;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        // 상한을 1 로 둔다. 상한이 없으면 새로고침을 연타할 때 허가가 쌓여
        // Tick 이 그만큼 연속 실행된다.
        private readonly SemaphoreSlim _wake = new SemaphoreSlim(0, 1);
        private bool _areaCodeTried;

        // 데이터 (UI 스레드에서만 접근)
        private readonly Dictionary<string, Quote> _quotes = new Dictionary<string, Quote>(StringComparer.Ordinal);
        private WeatherInfo _weather = new WeatherInfo();
        private DateTime _lastQuoteAt = DateTime.MinValue;      // 마지막 '시도' 시각 (대기 시간 계산용)
        private DateTime _lastWeatherAt = DateTime.MinValue;
        private DateTime _lastQuoteOkAt = DateTime.MinValue;    // 마지막 '성공' 시각 (값이 낡았는지 판단용)
        private bool _forceQuote = true;
        private bool _forceWeather = true;

        // UI 요소
        private Border _card;
        private ScaleTransform _scale;
        private TextBlock _symbolLabel, _symbolCaret;
        private TextBlock _sourceLabel, _sourceCaret, _timeLabel;
        private StackPanel _collapsedBody;
        private StackPanel _expandedBody;
        private TextBlock _price, _diff;
        private Grid _weatherRow;               // 날씨 영역 전체 (새로고침 버튼을 겹쳐 둔다)
        private StackPanel _weatherPanel;       // 지역별 행 + 추가 버튼
        private readonly List<WeatherView> _weatherViews = new List<WeatherView>();
        private readonly Dictionary<string, WeatherInfo> _weatherData =
            new Dictionary<string, WeatherInfo>(StringComparer.Ordinal);

        private sealed class WeatherView
        {
            public SymbolDef Def;
            public Border Root;
            public Canvas Icon;
            public TextBlock Temp, Desc, Sub, City;   // City 는 큰 카드에서만 쓴다
            public UIElement DelBtn;
            public RotateTransform Wiggle;
            public ScaleTransform Scale;
            public TranslateTransform Translate;
        }
        private UIElement _bodyHost, _dividerEl, _symBtn, _srcBtn, _headerRow;
        private TextBlock _minBtn, _quoteRefresh;
        private UIElement _clockRow, _clockDivider;

        // 맨 아래 공지 줄 - 새 버전이 있을 때만 나타난다
        // 즐겨찾기 - 바로가기(.lnk) 를 끌어다 놓아 모아 둔다
        private sealed class AppView
        {
            public AppDef Def;
            public Border Root;
            public UIElement DelBtn;
            public UIElement IconBtn;   // 그림 갈아끼우기. 편집 모드에서만 보인다
            public RotateTransform Wiggle;
        }
        private readonly List<AppView> _appViews = new List<AppView>();
        private StackPanel _appsRow;

        // 창 나누기 - 떼어낸 조각 창들
        private Grid _rootGrid;
        private PanelWindow _panelWeather, _panelApps, _panelClock;
        private WrapPanel _appsPanel;
        private Border _appsDivider;
        private TextBlock _appsHint;
        private const double AppTile = 38;

        private StackPanel _noticeRow;
        private TextBlock _noticeText;
        private DateTime _lastUpdateCheckAt = DateTime.MinValue;
        private string _latestVersion;          // 서버가 알려준 최신 버전 (확인 전이면 null)
        private const double UpdateCheckHours = 24;
        private Border _clockDelBtn;
        private UIElement _addBtn, _addWeatherBtn, _weatherBar, _quotesBar;
        private TextBlock _wxViewToggle;        // 날씨 목록 <-> 하나 크게
        private TextBlock _clockTime, _clockDate;
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _saveTimer;     // 연속 조작에서 매번 파일을 쓰지 않게
        private readonly List<QuoteView> _rows = new List<QuoteView>();    // 리스트 보기
        private readonly List<QuoteView> _tiles = new List<QuoteView>();   // 그리드 보기
        private StackPanel _listPanel;
        private UniformGrid _gridPanel;
        private TextBlock _viewToggle;
        private Ellipse _statusDot;
        private TextBlock _countdown;
        private bool _lastFetchOk;
        private bool _editMode;
        private bool _longFired;
        private DispatcherTimer _pressTimer;

        // 그리드 타일 높이. 폭은 2열로 나뉘어 약 124가 되는데, 정사각형으로 두면
        // 위아래가 허전해서 내용에 맞춰 낮췄다 (여백이 기존의 1/3).
        private const double TileSize = 91;

        private sealed class QuoteView
        {
            public SymbolDef Def;
            public Border Root;
            public TextBlock Name, Price, Ratio;
            public UIElement DelBtn;
            public RotateTransform Wiggle;
            public ScaleTransform Scale;
            public TranslateTransform Translate;
            public bool IsTile;
        }

        /// <summary>편집 모드에서 지운 항목. 흔들림이 멈추기 전까지 Alt+Z 로 되돌릴 수 있다.</summary>
        private sealed class RemovedItem
        {
            public SymbolDef Def;
            public int Index;
            public bool IsWeather;
        }

        private readonly List<RemovedItem> _undoStack = new List<RemovedItem>();
        private TextBlock _undoHint, _limitHint;
        private Border _limitHintBox;           // "n개 더" 줄. 여기도 끌어서 개수를 조절한다

        // 모니터 가장자리 도킹
        private Border _dockBar;                // 붙었을 때의 얇은 바 (창 Content 를 이걸로 바꾼다)
        private StackPanel _dockContent;        // 좌우 도킹에서 통째로 90도 돌린다
        private StackPanel _dockItems;
        private Border _dockGrip;               // 바 안쪽 가장자리 - 끌어서 두께 조절
        private Border _dockClip;               // 흐르는 영역. 여기서 잘린다
        private Canvas _dockCanvas;             // 자식을 제 크기대로 배치해 주는 자리
        private TranslateTransform _dockScroll;
        private AnimationClock _dockScrollClock;
        private const double MarqueeSpeed = 46;   // DIP/초
        private const double MarqueeGap = 44;     // 한 바퀴 사이의 빈 자리
        private double _marqueeLoop, _marqueeSecs;
        private bool _scrubbing;                  // 마우스로 잡아 끄는 중
        private double _scrubStartCursor, _scrubStartX;
        private bool _clipPending;      // 아직 가로(마퀴)인지 세로(떼기)인지 안 정했다
        private Point _clipStart;       // 누른 자리 (물리 픽셀)
        private TextBlock _dockClock;
        private HwndSource _hwndSource;
        private const int AppBarCallbackMsg = 0x0400 + 0x5A5;   // WM_USER + 임의값
        // 급등·급락 알림
        private sealed class PricePoint { public double Value; public DateTime At; }

        /// <summary>가장자리 바의 항목 하나. 값만 갈아끼우려고 붙잡아 둔다.</summary>
        private sealed class DockView
        {
            public string Key;
            public Border Box;                  // 항목마다 따로 번쩍이려면 자기 배경이 있어야 한다
            public TextBlock Price, Ratio;
            public ScaleTransform Scale;
        }
        private readonly List<DockView> _dockViews = new List<DockView>();
        private string _dockSig = "";       // 지금 담긴 구성. 그대로면 다시 만들지 않는다

        private WrapPanel _dockApps;        // 바에 실은 즐겨찾기. 흐르지 않는 고정 자리다
        private string _dockAppsSig = "";

        // 가장자리에 붙기 직전, 조각 창이 카드에서 얼마나 떨어져 있었는지.
        // 떼어낼 때 이 값으로 되돌려야 붙이기 전 배치가 그대로 살아난다.
        private readonly Dictionary<string, Point> _panelOff = new Dictionary<string, Point>();

        /// <summary>급등·급락 한 건. 얼마나 움직였는지까지 들고 다닌다.</summary>
        private sealed class SurgeHit { public string Key; public double Pct; }

        private const double DockGrowMinPercent = 5;    // 바에서 커지기 시작하는 기본 변동폭
        private const double DockGrowFloor = 0.05;      // 커질 땐 최소 이만큼. 안 그러면 눈에 안 띈다
        private const double DockGrowMaxScale = 0.10;   // 커지는 한도 (10%)
        private readonly Dictionary<string, PricePoint> _lastPrice = new Dictionary<string, PricePoint>();
        private readonly AppBar _appBar = new AppBar();   // 화면에서 자리를 확보하는 등록
        // _dockArea 는 지웠다. 작업영역을 자리의 근거로 삼는 필드였고, 그것이 64px 틈의 원인이다.
        // 자리는 DockStack 이 '화면 끝 + 남의 몫' 에서 두께를 이어 붙여 계산한다.
        private bool _relayoutPending;          // 셸 알림을 한 박자에 하나로 모은다
        private bool _moveWatch;                // 창이 밀려나는지 듣고 있는가
        private ScreenInfo _dockScreen;         // 그 모니터. 물리 좌표를 들고 있다
        private const double SurgeScale = 1.10;
        private const int SurgeHalfMs = 260;
        private const int SurgeCycles = 3;
        // 기준값은 이 시간이 지나야 갱신한다. 갱신 주기를 1초로 두더라도
        // 늘 '약 30초 전 값' 과 비교하게 되어, 주기와 무관하게 같은 뜻을 갖는다.
        private const double SurgeWindowSec = 30;
        private const double SurgeMaxGapSec = 300;   // 이보다 오래 끊겼던 값은 '단기' 가 아니다

        private const double DockThickness = 20;                // 위·아래에 붙었을 때 (DIP)
        private const double DockThicknessSide = 60;            // 좌·우는 가로쓰기가 들어가야 해서 3배
        private const double DockFontBase = 11.5;               // 배율 1.0 일 때의 값 글자 크기

        /// <summary>바 글자 크기. 두께가 배율을 따라 커지므로 글자도 같이 커진다.</summary>
        private double DockFont
        {
            get
            {
                double f = DockFontBase * _cfg.DockScale;
                if (f < 8) f = 8;
                if (f > 20) f = 20;
                return f;
            }
        }
        private UIElement _bottomGrip, _leftGrip, _rightGrip;

        // 편집 모드에서 항목을 끌어 순서 바꾸기
        private QuoteView _pressedView;
        private QuoteView _dragView;
        private Point _dragOrigin;
        private bool _dragActive;

        /// <summary>접었을 때 보여줄 종목. 설정값이 유효하지 않으면 첫 번째를 쓴다.</summary>
        private SymbolDef CurrentDef
        {
            get
            {
                var list = _cfg.Symbols;
                if (list == null || list.Count == 0) return null;
                if (_cfg.Symbol != null)
                    foreach (var d in list) if (d.Key == _cfg.Symbol) return d;
                return list[0];
            }
        }

        public WidgetWindow(Config cfg)
        {
            _cfg = cfg;
            BuildWindow();
            BuildUi();

            var first = CurrentDef;                       // 설정의 종목 키를 실제 목록에 맞춰 보정
            if (first != null) _cfg.Symbol = first.Key;

            ApplyLayoutMode();
            ApplyMinimized();
            RestorePlacement();

            Loaded += (s, e) => StartLoop();
            Closed += (s, e) => Shutdown();
        }

        // ---------- 창 ----------

        private void BuildWindow()
        {
            Title = "오늘은";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Topmost = _cfg.Topmost;
            Opacity = _cfg.Opacity;
            FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            // 단일 클릭 드래그로 창 이동 (더블클릭은 자식이 가로챈다)
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount != 1) return;
                if (_editMode) { ExitEditMode(); return; }   // 빈 곳을 누르면 편집 종료
                // 붙어 있는 조각들을 함께 옮긴다.
                // DragMove 는 끝날 때까지 돌아오지 않으므로, 끌기 전 자리를 적어 두었다가
                // 돌아온 뒤 그만큼 옮긴다. 끄는 동안이 아니라 놓을 때 따라붙는다.
                var carry = CarryPanels();
                double bx = Left, by = Top;

                try { DragMove(); } catch { }

                MovePanels(carry, Left - bx, Top - by);
                if (!TryDockAfterDrag()) { RescueIfLost(); SavePlacement(); }
            };
            // 드래그는 창에서도 받는다.
            // 항목을 옮기는 동안 패널에서 잠시 떼었다 붙이는데, 그때 마우스 캡처가 풀려
            // 항목 자신의 MouseMove 가 더 이상 오지 않는 경우가 있다.
            MouseMove += (s, e) =>
            {
                if (_dragView == null) return;
                if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
                DragTo();
            };
            MouseLeftButtonUp += (s, e) =>
            {
                if (_dragView == null) return;
                CancelPress();
                EndDrag();
            };

            // 바탕화면이나 다른 창을 누르면 편집 모드를 빠져나온다
            Deactivated += (s, e) => ExitEditMode();

            SourceInitialized += (s, e) =>
            {
                _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
                if (_hwndSource != null) _hwndSource.AddHook(AppBarHook);
                // 붙이기가 실패해도 위젯 자체는 떠야 한다.
                // Show() 안에서 터지면 메시지 루프 전이라 예외 처리기가 잡지 못하고 프로세스가 죽는다.
                try { if (Docked) ApplyDock(_cfg.DockedEdge, false); }   // 지난번에 붙여둔 자리로 복원
                catch { _cfg.DockedEdge = DockEdge.None; }

                // 나눠 두었으면 조각 창들을 다시 띄운다.
                // 실패해도 카드는 떠야 하므로 실패 시 합쳐진 상태로 되돌린다.
                if (_cfg.Separated) { try { ApplySeparation(); } catch { _cfg.Separated = false; } }
            };

            // ★ AppBar 등록을 지우지 않으면 확보한 화면 공간이 로그오프할 때까지 남는다. ★
            //   창 종료와 프로세스 종료 양쪽에서 지운다.
            // ★ Leave 는 DockedEdge 를 지우기 전에 불러야 어느 변을 다시 쌓을지 알 수 있다 ★
            Closing += (s, e) => { try { DockStack.Leave(this); } catch { } _appBar.Unregister(); };
            AppDomain.CurrentDomain.ProcessExit += delegate { AppBar.UnregisterAll(); };

            // 휠은 창 어디서 굴려도 접어둔 목록을 넘긴다
            // (목록 패널까지 이벤트가 올라오지 않는 경우가 있어 창에서도 받는다)
            MouseWheel += (s, e) =>
            {
                if (e.Handled) return;
                if (_cfg.Minimized || !_cfg.Expanded || !_cfg.ShowQuotes) return;
                OnListWheel(s, e);
            };

            KeyDown += (s, e) =>
            {
                // Alt 조합은 Key 가 System 으로 오고 실제 키는 SystemKey 에 담긴다
                Key k = (e.Key == Key.System) ? e.SystemKey : e.Key;
                if (k == Key.Escape) { ExitEditMode(); return; }
                if (k == Key.Z && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
                {
                    e.Handled = true;
                    UndoRemove();
                }
            };
        }

        private void RestorePlacement()
        {
            var wa = SystemParameters.WorkArea;
            double cw0 = CurrentCardWidth;
            if (double.IsNaN(cw0)) cw0 = CardWidth;
            double w = (cw0 + ShadowMargin * 2) * _cfg.Scale;

            if (!double.IsNaN(_cfg.X) && !double.IsNaN(_cfg.Y))
            {
                Left = _cfg.X;
                Top = _cfg.Y;
            }
            else
            {
                Left = wa.Right - w;
                Top = wa.Top;
            }
            ClampToScreen();
        }

        /// <summary>
        /// 창이 화면 밖으로 사라지지 않게만 보정한다. 지금 놓인 모니터 안으로만 되돌린다.
        ///
        /// ★ 가상 화면(모든 모니터를 감싸는 사각형)을 쓰면 안 된다 ★
        ///   모니터 배치가 ㄱ자면 그 사각형 안에 어느 모니터에도 속하지 않는 빈 구역이 생긴다.
        ///   여기 실측 배치가 정확히 그렇다 — 주 (0,0)-(1920,1080), 보조 (-1280,444)-(0,1468),
        ///   감싸는 사각형은 (-1280,0)-(1920,1468). 그 빈 구역으로 밀어 넣으면
        ///   계산상 '화면 안' 인데 눈에는 안 보인다.
        /// </summary>
        private void ClampToScreen()
        {
            double cw = CurrentCardWidth;
            if (double.IsNaN(cw)) cw = CardWidth;
            double w = (cw + ShadowMargin * 2) * _cfg.Scale;
            double h = ActualHeight > 0 ? ActualHeight : 200 * _cfg.Scale;

            Rect box;
            if (!ScreenBoxOf(Left, Top, w, h, out box)) return;   // 모르면 건드리지 않는다

            if (Left + w > box.Right) Left = box.Right - w;
            if (Top + h > box.Bottom) Top = box.Bottom - h;
            if (Left < box.Left) Left = box.Left;
            if (Top < box.Top) Top = box.Top;
        }

        /// <summary>
        /// 그 자리가 놓인 모니터의 영역을 DIP 로 돌려준다.
        /// Dock 은 픽셀로 계산하므로 넘길 때 곱하고 받을 때 나눈다.
        /// </summary>
        private bool ScreenBoxOf(double x, double y, double w, double h, out Rect box)
        {
            box = new Rect();
            try
            {
                double sx, sy;
                Dock.GetDpiScale(this, out sx, out sy);
                if (sx <= 0 || sy <= 0) return false;

                var all = Dock.AllScreens();
                if (all == null || all.Count == 0) return false;

                var scr = Dock.ScreenAt(all, new Point((x + w / 2) * sx, (y + h / 2) * sy));
                if (scr == null) return false;

                Rect b = scr.Bounds;
                if (b.Width <= 0 || b.Height <= 0) return false;
                box = new Rect(b.Left / sx, b.Top / sy, b.Width / sx, b.Height / sy);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 어느 모니터에도 안 걸쳐 있으면 가까운 모니터 안으로 되돌린다.
        ///
        /// ★ 걸쳐 놓는 것은 막지 않는다 ★
        ///   모니터 두 개에 걸쳐 두고 쓰는 것은 정상이므로, 조금이라도 겹치면 그대로 둔다.
        ///   되돌리는 것은 '어디에도 없을 때' 뿐이다.
        ///   배치가 ㄱ자면 모든 모니터를 감싸는 사각형 안에 어느 화면도 아닌 빈 구역이 생기고,
        ///   카드 아래를 잡고 위로 끌면 그리로 넘어가 통째로 사라진다. 실제로 그랬다.
        /// </summary>
        private void RescueIfLost()
        {
            try
            {
                if (!OffScreen(this)) return;

                Rect box;
                if (!ScreenBoxOf(Left, Top, ActualWidth, ActualHeight, out box)) return;
                if (box.Width <= 0 || box.Height <= 0) return;

                double w = ActualWidth > 0 ? ActualWidth : CardWidth;
                double h = ActualHeight > 0 ? ActualHeight : 200;

                if (Left + w > box.Right) Left = box.Right - w;
                if (Top + h > box.Bottom) Top = box.Bottom - h;
                if (Left < box.Left) Left = box.Left;
                if (Top < box.Top) Top = box.Top;
            }
            catch { }
        }

        private void SavePlacement()
        {
            _cfg.X = Left;
            _cfg.Y = Top;
            _cfg.Save();
        }

        // ---------- UI 구성 ----------

        private void BuildUi()
        {
            _scale = new ScaleTransform(_cfg.Scale, _cfg.Scale);

            _card = new Border
            {
                Width = CardWidth,
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                BorderBrush = Palette.CardEdge,
                Background = Palette.Card,
                Padding = new Thickness(16, 13, 16, 14),
                Margin = new Thickness(ShadowMargin),
                LayoutTransform = _scale,
                // 그림자(DropShadowEffect)는 쓰지 않는다.
                // 밝은 바탕화면 위에서는 둥근 모서리 바깥에 회색 얼룩처럼 보인다.
                // 카드 경계는 BorderBrush 만으로 충분히 구분된다.
            };

            var root = new Grid();
            _rootGrid = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // body
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // divider
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // weather
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // clock divider
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 공지 줄
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // clock
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 즐겨찾기

            root.Children.Add(BuildHeader());
            root.Children.Add(BuildBody());
            root.Children.Add(BuildDivider());
            root.Children.Add(BuildWeather());
            root.Children.Add(BuildWeatherCollapsedBar());   // 접혔을 때 같은 자리에 대신 놓인다
            AddClock(root);
            AddApps(root);
            AddNotice(root);

            // 우하단 크기 조절 그립 (카드 위에 겹친다)
            var grip = BuildGrip();
            Grid.SetRow(grip, 7);
            root.Children.Add(grip);

            // 하단 가장자리 - 보이는 개수 조절
            _bottomGrip = BuildBottomGrip();
            Grid.SetRow((FrameworkElement)_bottomGrip, 7);
            root.Children.Add(_bottomGrip);

            // 좌우 가장자리 - 타일 가로 개수 조절
            _leftGrip = BuildSideGrip(true);
            Grid.SetRowSpan((FrameworkElement)_leftGrip, 8);
            root.Children.Add(_leftGrip);

            _rightGrip = BuildSideGrip(false);
            Grid.SetRowSpan((FrameworkElement)_rightGrip, 8);
            root.Children.Add(_rightGrip);

            _card.Child = root;
            Content = _card;

            ContextMenu = BuildContextMenu();
            PanelWindow.Main = this;
            PanelWindow.MainMoved = SavePlacement;
            PanelWindow.MainDocked = delegate { return Docked; };
            PanelWindow.Lost = RescuePanelsIfLost;
        }

        private UIElement BuildHeader()
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 좌측: 종목 이름 + 펼침/선택 화살표
            _symbolLabel = new TextBlock
            {
                FontSize = 10.5, FontWeight = FontWeights.SemiBold,
                Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center,
            };
            _symbolCaret = new TextBlock
            {
                Text = "▾", FontSize = 9, Foreground = Palette.TextGhost,
                Margin = new Thickness(4, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            var symBtn = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = Palette.Clear,          // 히트 테스트를 받기 위해 필요
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            symBtn.Children.Add(_symbolLabel);
            symBtn.Children.Add(_symbolCaret);
            symBtn.ToolTip = "종목 선택 / 전체 펼치기";
            symBtn.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                // 접혀 있으면 라벨을 눌러도 펴진다
                if (!_cfg.ShowQuotes && !_cfg.Minimized)
                {
                    _cfg.ShowQuotes = true;
                    ApplyMinimized();
                    _cfg.Save();
                    RequestQuoteRefresh();
                    return;
                }
                ShowSymbolMenu(symBtn);
            };

            // 목록 ↔ 타일 전환 (펼쳤을 때만 보인다)
            _viewToggle = MakeIconButton("▦", "타일로 보기", false, ToggleViewMode);
            _viewToggle.Margin = new Thickness(9, 0, 0, 0);
            _viewToggle.Visibility = Visibility.Collapsed;

            var leftBox = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // 수신 상태등 - 값을 받고 있으면 초록, 끊기면 빨강
            _statusDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = Palette.Offline,
                Margin = new Thickness(10, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };

            // 다음 갱신까지 남은 시간
            _countdown = new TextBlock
            {
                FontSize = 12.5,
                Foreground = Palette.IconIdle,
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "다음 갱신까지 남은 시간",
                Visibility = Visibility.Collapsed,
            };

            leftBox.Children.Add(symBtn);
            leftBox.Children.Add(_viewToggle);
            leftBox.Children.Add(_statusDot);
            leftBox.Children.Add(_countdown);
            Grid.SetColumn(leftBox, 0);
            g.Children.Add(leftBox);
            _symBtn = leftBox;

            // 우측: 출처(은행) + 시각
            _sourceLabel = new TextBlock
            {
                FontSize = 9.5, Foreground = Palette.TextGhost,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _sourceCaret = new TextBlock
            {
                Text = "▾", FontSize = 8.5, Foreground = Palette.TextGhost,
                Margin = new Thickness(3, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            _timeLabel = new TextBlock
            {
                FontSize = 9.5, Foreground = Palette.TextGhost,
                Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            var srcBtn = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = Palette.Clear,
                VerticalAlignment = VerticalAlignment.Center,
            };
            srcBtn.Children.Add(_sourceLabel);
            srcBtn.Children.Add(_sourceCaret);
            srcBtn.Children.Add(_timeLabel);
            srcBtn.MouseLeftButtonDown += (s, e) =>
            {
                if (_sourceCaret.Visibility != Visibility.Visible) return;
                e.Handled = true;
                ShowBankMenu(srcBtn);
            };

            _srcBtn = srcBtn;

            // 은행 라벨을 왼쪽으로 밀고 오른쪽 끝에 시세 새로고침을 둔다
            var quoteRefresh = MakeRefreshButton("시세 새로고침", RequestQuoteRefresh);
            quoteRefresh.Margin = new Thickness(9, 0, 0, 0);
            _quoteRefresh = quoteRefresh;

            var rightBox = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // 새로고침 오른쪽: 시세 접기(─) ↔ 펴기(+). 최소화 상태에서는 창 복원(▢).
            _minBtn = MakeIconButton("─", "시세 접기", false, delegate
            {
                if (_cfg.Minimized) { ToggleMinimized(); return; }
                _cfg.ShowQuotes = !_cfg.ShowQuotes;
                ApplyMinimized();
                _cfg.Save();
                if (_cfg.ShowQuotes) RequestQuoteRefresh();
            });
            _minBtn.Margin = new Thickness(7, 0, 0, 0);

            rightBox.Children.Add(srcBtn);
            rightBox.Children.Add(quoteRefresh);
            var qClose = MakeIconButton("×", "시세 닫기 (우클릭 메뉴로 다시 열기)", false, delegate
            {
                CloseSection("시세");
            });
            qClose.Margin = new Thickness(7, 0, 0, 0);

            rightBox.Children.Add(_minBtn);
            rightBox.Children.Add(qClose);
            Grid.SetColumn(rightBox, 1);
            g.Children.Add(rightBox);

            _headerRow = g;
            return g;
        }

        private UIElement BuildBody()
        {
            var host = new Grid { Margin = new Thickness(0, 4, 0, 0) };

            // 접힘: 큰 숫자 하나
            _price = new TextBlock { FontSize = 23, Foreground = Palette.Text };
            _diff = new TextBlock
            {
                FontSize = 11, Foreground = Palette.TextDim,
                Margin = new Thickness(9, 0, 0, 5), VerticalAlignment = VerticalAlignment.Bottom,
            };
            _collapsedBody = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = Palette.Clear,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            _collapsedBody.Children.Add(_price);
            _collapsedBody.Children.Add(_diff);
            _collapsedBody.ToolTip = "더블클릭하면 네이버에서 열립니다";
            _collapsedBody.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount != 2) return;
                e.Handled = true;
                OpenQuoteLink(_cfg.Symbol);
            };
            host.Children.Add(_collapsedBody);

            // 펼침: 전 종목 (리스트 또는 그리드) + 추가 버튼
            _listPanel = new StackPanel();
            _gridPanel = new UniformGrid { Columns = 2, Margin = new Thickness(-3, 0, -3, 0) };

            _expandedBody = new StackPanel { Margin = new Thickness(0, 2, 0, 1), Background = Palette.Clear };
            _expandedBody.MouseWheel += OnListWheel;
            _expandedBody.Children.Add(_listPanel);
            _expandedBody.Children.Add(_gridPanel);

            _limitHint = new TextBlock
            {
                FontSize = 9.5,
                Foreground = Palette.TextGhost,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            // 카드 아래 가장자리까지 내려가지 않아도 여기서 바로 끌 수 있게 한다.
            // 숨은 항목이 있을 때만 보이므로 늘 끌 거리가 남아 있는 자리다.
            _limitHintBox = new Border
            {
                Child = _limitHint,
                Background = Palette.Clear,      // 히트 테스트를 받으려면 필요하다
                Cursor = Cursors.SizeNS,
                Padding = new Thickness(0, 5, 0, 3),
                Visibility = Visibility.Collapsed,
                ToolTip = "위아래로 끌어서 보이는 개수 조절 · 휠로도 넘어간다",
            };
            AttachLimitDrag(_limitHintBox, delegate(bool on)
            {
                _limitHint.Foreground = on ? Palette.TextDim : Palette.TextGhost;
            });
            _expandedBody.Children.Add(_limitHintBox);

            _expandedBody.Children.Add(BuildAddButton());

            _undoHint = new TextBlock
            {
                Text = "Alt + Z  되돌리기",
                FontSize = 9.5,
                Foreground = Palette.TextGhost,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            _expandedBody.Children.Add(_undoHint);
            host.Children.Add(_expandedBody);

            RebuildSymbolViews();

            Grid.SetRow(host, 1);
            _bodyHost = host;
            return host;
        }

        private QuoteView BuildRow(SymbolDef def)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // 삭제 배지
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(77) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });

            var defForDelete = def;
            var del = BuildDeleteBadge(delegate { RemoveSymbol(defForDelete); });
            del.VerticalAlignment = VerticalAlignment.Center;
            del.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(del, 0);
            g.Children.Add(del);

            var name = new TextBlock
            {
                Text = def.Label, FontSize = 11, Foreground = Palette.TextDim,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var price = new TextBlock
            {
                FontSize = 12.5, Foreground = Palette.Text,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var ratio = new TextBlock
            {
                FontSize = 10.5, Foreground = Palette.Flat,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(price, 2);
            Grid.SetColumn(ratio, 3);
            g.Children.Add(name);
            g.Children.Add(price);
            g.Children.Add(ratio);

            var wiggle = new RotateTransform(0);
            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform(0, 0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(wiggle);
            group.Children.Add(translate);

            var root = new Border
            {
                Child = g,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(-4, 0, -4, 0),
                CornerRadius = new CornerRadius(6),
                Background = Palette.Clear,
                Cursor = Cursors.Hand,
                RenderTransform = group,
                RenderTransformOrigin = new Point(0.5, 0.5),
                ToolTip = "클릭: 대표 종목 / 더블클릭: 네이버에서 열기 / 꾹 누르기: 편집",
            };

            var v = new QuoteView
            {
                Def = def, Root = root, Name = name, Price = price, Ratio = ratio,
                DelBtn = del, Wiggle = wiggle, Scale = scale, Translate = translate,
            };
            AttachViewEvents(v);
            return v;
        }

        /// <summary>그리드 보기용 정사각형 타일. 접힘 화면처럼 숫자를 크게 보여준다.</summary>
        private QuoteView BuildTile(SymbolDef def)
        {
            var name = new TextBlock
            {
                Text = def.Label, FontSize = 10.5, Foreground = Palette.TextDim,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var price = new TextBlock
            {
                FontSize = 18, Foreground = Palette.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 7, 0, 0),
            };
            var ratio = new TextBlock
            {
                FontSize = 11, Foreground = Palette.Flat,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            };

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(name);
            sp.Children.Add(price);
            sp.Children.Add(ratio);

            var inner = new Grid();
            inner.Children.Add(sp);

            var defForDelete = def;
            var del = BuildDeleteBadge(delegate { RemoveSymbol(defForDelete); });
            del.HorizontalAlignment = HorizontalAlignment.Left;
            del.VerticalAlignment = VerticalAlignment.Top;
            del.Margin = new Thickness(-2, -2, 0, 0);
            inner.Children.Add(del);

            var wiggle = new RotateTransform(0);
            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform(0, 0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(wiggle);
            group.Children.Add(translate);

            var root = new Border
            {
                Child = inner,
                Height = TileSize,
                Margin = new Thickness(3),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(10),
                Background = Palette.Tile,
                BorderBrush = Palette.TileEdge,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                RenderTransform = group,
                RenderTransformOrigin = new Point(0.5, 0.5),
                ToolTip = "클릭: 대표 종목 / 더블클릭: 네이버에서 열기 / 꾹 누르기: 편집",
            };

            var v = new QuoteView
            {
                Def = def, Root = root, Name = name, Price = price, Ratio = ratio,
                DelBtn = del, Wiggle = wiggle, Scale = scale, Translate = translate,
            };
            AttachViewEvents(v);
            return v;
        }

        /// <summary>편집 모드에서 항목 앞에 뜨는 빨간 − 배지.</summary>
        private Border BuildDeleteBadge(Action onDelete)
        {
            var b = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = Palette.Delete,
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                ToolTip = "삭제",
                // 문자 '−' 는 글리프가 baseline 기준이라 원 안에서 위로 치우쳐 보인다.
                // 도형으로 직접 그려야 상하 중앙이 정확히 맞는다.
                Child = new Rectangle
                {
                    Width = 7.5,
                    Height = 1.7,
                    RadiusX = 0.85,
                    RadiusY = 0.85,
                    Fill = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; if (onDelete != null) onDelete(); };
            return b;
        }

        private const int BadgeMs = 160;

        /// <summary>
        /// 편집 배지를 스르르 보이고 스르르 감춘다.
        ///
        /// ★ 감추는 것은 애니메이션이 끝난 '뒤' 에 ★
        ///   Visibility 를 먼저 바꾸면 사라지는 모습이 아예 안 보인다. 그래서 다 옅어진
        ///   다음에 접는다. 대신 그 사이에 편집 모드가 다시 켜졌을 수 있으므로,
        ///   끝나는 시점에 한 번 더 확인하고 접는다 - 안 그러면 방금 켠 배지를 도로 감춘다.
        ///
        /// 크기도 같이 준다. 옅어지기만 하는 것보다 자리에 '자라나는' 편이 눈에 자연스럽다.
        /// </summary>
        private void ShowBadge(UIElement el, bool show)
        {
            if (el == null) return;

            var fe = el as FrameworkElement;
            ScaleTransform sc = (fe != null) ? fe.RenderTransform as ScaleTransform : null;
            if (fe != null && sc == null)
            {
                sc = new ScaleTransform(1, 1);
                fe.RenderTransform = sc;
                fe.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var dur = new Duration(TimeSpan.FromMilliseconds(BadgeMs));

            if (show)
            {
                el.Visibility = Visibility.Visible;

                var fade = new DoubleAnimation(0, 1, dur);
                fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                el.BeginAnimation(UIElement.OpacityProperty, fade);

                if (sc != null)
                {
                    var grow = new DoubleAnimation(0.55, 1, dur);
                    grow.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                    sc.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                    sc.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                }
                return;
            }

            if (el.Visibility != Visibility.Visible) return;   // 이미 접혀 있으면 할 일이 없다

            var out1 = new DoubleAnimation(1, 0, dur);
            out1.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            out1.Completed += delegate
            {
                if (_editMode) return;   // 그 사이 다시 켜졌다
                el.BeginAnimation(UIElement.OpacityProperty, null);
                el.Opacity = 1;          // 다음에 보일 때를 위해 되돌린다
                el.Visibility = Visibility.Collapsed;
            };
            el.BeginAnimation(UIElement.OpacityProperty, out1);

            if (sc != null)
            {
                var shrink = new DoubleAnimation(1, 0.55, dur);
                shrink.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            }
        }

        /// <summary>
        /// 아이콘 그림을 갈아끼우는 작은 동그라미. 아래 가운데에 붙는다.
        ///
        ///   왼쪽 클릭 → 256x256 PNG 고르기
        ///   오른쪽 클릭 → 원래 아이콘으로 되돌리기
        ///
        /// 편집 모드에서만 보인다(ApplyEditModeToApps). 붙은 바에는 안 단다 -
        /// 거기서는 아이콘이 작고 편집할 자리도 아니다. 갈아끼운 그림은 바에도 그대로 나온다.
        /// </summary>
        private UIElement BuildIconBadge(AppDef def)
        {
            var me = def;

            // 속이 빈 회색 고리. 테두리를 굵게 줘서 작아도 눈에 남는다.
            const double Dia = 6.3;        // 처음 9 에서 70%
            const double Ring = 3;         // 테두리. 바깥지름은 Dia + Ring 이 된다
            const double Hit = 16;         // 눈에 보이는 것보다 넉넉히 - 안 그러면 못 누른다

            var ring = new Ellipse
            {
                Width = Dia,
                Height = Dia,
                Stroke = Palette.IconHover,
                StrokeThickness = Ring,
                Fill = null,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var b = new Border
            {
                Width = Hit,
                Height = Hit,
                Child = ring,
                // ★ 잡는 칸은 알파 0 이면 안 된다 ★
                //   투명 창에서는 윈도우가 알파 0 인 자리를 '창이 없는 곳' 으로 보고
                //   마우스를 아래로 흘려보낸다 (Palette.Grab 주석 참고).
                Background = Palette.Grab,
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Center,
                // 타일 아래쪽. 살짝 걸치게 두는 편이 타일에 딸린 것으로 읽힌다.
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -(Hit - 2)),
                ToolTip = "그림 바꾸기 (256x256 PNG) · 우클릭하면 되돌린다",
            };

            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; PickIconPng(me); };
            b.MouseRightButtonDown += (s, e) =>
            {
                e.Handled = true;
                Apps.ClearIconOverride(me.Path);
                RefreshAppBars();
            };
            return b;
        }

        /// <summary>
        /// 갈아끼울 PNG 를 고른다.
        ///
        /// 256x256 만 받는 이유: 바 두께에 따라 아이콘이 커졌다 작아졌다 하는데, 작은 그림을
        /// 늘리면 뭉개진다. 한 크기로 못박아 두면 어디에 실려도 또렷하다.
        /// 아닌 파일은 조용히 넘기지 않고 왜 안 되는지 알려준다 - 크기 때문인 줄 모르면
        /// 몇 번이고 다시 고르게 된다.
        /// </summary>
        private void PickIconPng(AppDef def)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Title = "아이콘 그림 고르기 (256x256 PNG)";
                dlg.Filter = "PNG 그림 (*.png)|*.png";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != true) return;

                if (!Apps.SetIconOverride(def.Path, dlg.FileName))
                {
                    MessageBox.Show(this,
                        "256x256 크기의 PNG 만 쓸 수 있습니다.",
                        "오늘은", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                RefreshAppBars();
            }
            catch { }
        }

        /// <summary>목록 맨 아래 가운데 원형 + 버튼.</summary>
        private UIElement BuildAddButton()
        {
            var plus = new TextBlock
            {
                Text = "+",
                FontSize = 16,
                Foreground = Palette.TextDim,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0),
            };
            var circle = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = Palette.Hover,
                BorderBrush = Palette.CardEdge,
                BorderThickness = new Thickness(1),
                Child = plus,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 2),
                ToolTip = "종목 추가",
                Visibility = Visibility.Collapsed,   // 꾹 눌러 편집 모드로 들어갔을 때만 보인다
            };
            circle.MouseEnter += (s, e) => { circle.Background = Palette.TileHover; plus.Foreground = Palette.Text; };
            circle.MouseLeave += (s, e) => { circle.Background = Palette.Hover; plus.Foreground = Palette.TextDim; };
            circle.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                SearchWindow.Open(this, _cfg.Symbols, AddSymbol);
            };
            _addBtn = circle;
            return circle;
        }

        // ---------- 항목 상호작용 (클릭 / 더블클릭 / 꾹 누르기) ----------

        private void AttachViewEvents(QuoteView v)
        {
            v.Root.MouseEnter += (s, e) => { if (!_editMode) v.Root.Background = HoverBrush(v); };
            v.Root.MouseLeave += (s, e) =>
            {
                if (!_editMode) v.Root.Background = IdleBrush(v);
                CancelPress();
            };

            v.Root.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;   // 창 드래그로 넘어가지 않게
                if (e.ClickCount == 2)
                {
                    CancelPress();
                    if (!_editMode) OpenQuoteLink(v.Def.Key);
                    return;
                }
                _pressedView = v;
                if (_editMode) PrepareDrag(v);   // 편집 중엔 곧바로 끌 수 있다
                else BeginPress();
            };

            v.Root.MouseMove += (s, e) =>
            {
                if (_dragView == null) return;
                if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
                DragTo();
            };

            v.Root.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                bool wasLong = _longFired;
                bool wasDrag = _dragActive;
                CancelPress();
                EndDrag();
                if (wasLong || wasDrag) return;   // 편집 진입/순서 변경 직후엔 선택하지 않는다
                if (_editMode) { ExitEditMode(); return; }
                SelectSymbol(v.Def);
            };
        }

        // ---------- 끌어서 순서 바꾸기 ----------

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint pt);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        /// <summary>
        /// 커서의 화면 좌표. WPF 의 GetPosition(this) 는 드래그 중 값이 갱신되지 않는 경우가 있어
        /// (실측으로 확인) 드래그 판정에는 화면 절대 좌표를 쓴다.
        /// </summary>
        private static Point CursorOnScreen()
        {
            NativePoint p;
            if (GetCursorPos(out p)) return new Point(p.X, p.Y);
            return new Point();
        }

        private void PrepareDrag(QuoteView v)
        {
            _dragView = v;
            _dragOrigin = CursorOnScreen();
            _dragActive = false;
            try { v.Root.CaptureMouse(); } catch { }

            LiftView(v);   // 잡는 순간 바로 들린다
        }

        /// <summary>잡은 항목을 살짝 키우고 비스듬히 들어 올린다.</summary>
        private static void LiftView(QuoteView v)
        {
            // 애니메이션이 붙어 있는 동안에는 속성에 값을 직접 넣어도 무시된다.
            // 흔들림(각도)과 눌림(배율)을 먼저 떼어낸 뒤에 값을 준다.
            v.Wiggle.BeginAnimation(RotateTransform.AngleProperty, null);
            v.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            v.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            v.Wiggle.Angle = 8;
            v.Scale.ScaleX = 1.08;
            v.Scale.ScaleY = 1.08;
            v.Root.Opacity = 0.92;
            Panel.SetZIndex(v.Root, 10);
        }

        private void DragTo()
        {
            Point p = CursorOnScreen();
            double dx = p.X - _dragOrigin.X;
            double dy = p.Y - _dragOrigin.Y;

            if (!_dragActive)
            {
                if (Math.Abs(dx) + Math.Abs(dy) < 3) return;   // 손떨림은 무시
                _dragActive = true;
                LiftView(_dragView);   // 잡을 때 이미 들렸지만, 혹시 풀렸으면 다시 건다
            }

            // 커서는 물리 픽셀, Translate 는 시각 트리 안의 DIP 다.
            // 화면 배율과 카드 배율 둘 다로 나눠야 손끝과 같이 움직인다.
            double s = _cfg.Scale <= 0 ? 1 : _cfg.Scale;
            double sx, sy;
            Dock.GetDpiScale(this, out sx, out sy);
            _dragView.Translate.X = dx / (s * sx);
            _dragView.Translate.Y = dy / (s * sy);

            // 다른 항목 위로 들어가면 그 자리로 밀어 넣는다
            var views = _cfg.GridView ? _tiles : _rows;
            Panel panel = _cfg.GridView ? (Panel)_gridPanel : _listPanel;
            Point local;
            try { local = panel.PointFromScreen(p); }
            catch { return; }

            int from = views.IndexOf(_dragView);
            if (from < 0) return;

            for (int i = 0; i < views.Count; i++)
            {
                if (i == from) continue;
                var el = views[i].Root;
                if (el.Visibility != Visibility.Visible) continue;   // 접혀서 안 보이는 자리는 건너뛴다
                Point tl = el.TranslatePoint(new Point(0, 0), panel);
                var rect = new Rect(tl.X, tl.Y, el.ActualWidth, el.ActualHeight);
                if (rect.Contains(local)) { MoveItem(from, i); break; }
            }
        }

        /// <summary>목록·화면·설정을 같은 순서로 옮긴다. 보이지 않는 쪽 뷰도 함께 맞춘다.</summary>
        private void MoveItem(int from, int to)
        {
            if (from == to || from < 0 || to < 0) return;
            if (from >= _cfg.Symbols.Count || to >= _cfg.Symbols.Count) return;

            var def = _cfg.Symbols[from];
            _cfg.Symbols.RemoveAt(from);
            _cfg.Symbols.Insert(to, def);

            Reorder(_rows, _listPanel, from, to);
            Reorder(_tiles, _gridPanel, from, to);

            // 요소가 새 자리로 옮겨졌으니 기준점을 지금 위치로 다시 잡는다 (그래야 튀지 않는다)
            if (_dragView != null)
            {
                Panel panel = _cfg.GridView ? (Panel)_gridPanel : _listPanel;
                panel.UpdateLayout();
                _dragOrigin = CursorOnScreen();
                _dragView.Translate.X = 0;
                _dragView.Translate.Y = 0;
            }
        }

        private static void Reorder(List<QuoteView> views, Panel panel, int from, int to)
        {
            if (views.Count == 0 || panel == null) return;
            if (from >= views.Count || to >= views.Count) return;

            var v = views[from];
            views.RemoveAt(from);
            views.Insert(to, v);

            panel.Children.Remove(v.Root);
            panel.Children.Insert(to, v.Root);
        }

        private void EndDrag()
        {
            if (_dragView == null) return;

            var v = _dragView;
            _dragView = null;

            try { v.Root.ReleaseMouseCapture(); } catch { }

            // 남아 있을지 모를 애니메이션을 떼고 원래대로 되돌린다
            v.Wiggle.BeginAnimation(RotateTransform.AngleProperty, null);
            v.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            v.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            v.Wiggle.Angle = 0;
            v.Translate.X = 0;
            v.Translate.Y = 0;
            v.Scale.ScaleX = 1;
            v.Scale.ScaleY = 1;
            v.Root.Opacity = 1;
            Panel.SetZIndex(v.Root, 0);

            if (_dragActive)
            {
                _dragActive = false;
                _cfg.Save();
                if (_editMode) ApplyEditMode();   // 새 순서에 맞춰 흔들림 위상을 다시 잡는다
                RefreshSymbolViews();
            }
        }

        private static Brush IdleBrush(QuoteView v) { return v.IsTile ? Palette.Tile : Palette.Clear; }
        private static Brush HoverBrush(QuoteView v) { return v.IsTile ? Palette.TileHover : Palette.Hover; }

        private void BeginPress()
        {
            _longFired = false;
            if (_pressTimer == null)
            {
                _pressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
                _pressTimer.Tick += (s, e) =>
                {
                    _pressTimer.Stop();
                    _longFired = true;
                    EnterEditMode();
                    // 누른 손을 떼지 않았다면 그대로 끌어서 순서를 바꿀 수 있게 이어준다
                    if (_pressedView != null && Mouse.LeftButton == MouseButtonState.Pressed)
                        PrepareDrag(_pressedView);
                };
            }
            _pressTimer.Stop();
            _pressTimer.Start();
        }

        private void CancelPress()
        {
            if (_pressTimer != null) _pressTimer.Stop();
        }

        private void SelectSymbol(SymbolDef def)
        {
            _cfg.Symbol = def.Key;
            _cfg.Save();
            RefreshHeader();
            RefreshSymbolViews();
        }

        // ---------- 편집 모드 ----------

        private void EnterEditMode()
        {
            if (_editMode) return;
            _editMode = true;
            _undoStack.Clear();          // 편집 세션마다 되돌리기 기록을 새로 시작한다
            ApplyEditMode();
            UpdateUndoHint();
            try { Focus(); } catch { }   // Alt+Z 를 받으려면 창에 포커스가 있어야 한다
        }

        private void ExitEditMode()
        {
            if (!_editMode) return;
            EndDrag();
            _editMode = false;
            _undoStack.Clear();          // 흔들림이 멈추면 되돌리기도 끝난다
            ApplyEditMode();
            UpdateUndoHint();
        }

        private void ApplyEditMode()
        {
            ApplyEditModeTo(_rows);
            ApplyEditModeTo(_tiles);
            ApplyEditModeToWeather();
            ApplyEditModeToApps();
            ApplyNotice(null);           // 공지 줄도 편집 모드에서만 보인다

            // 추가 버튼과 시계 접기 배지는 편집 모드에서만 드러난다
            var v = _editMode ? Visibility.Visible : Visibility.Collapsed;
            if (_addBtn != null) _addBtn.Visibility = v;
            if (_addWeatherBtn != null) _addWeatherBtn.Visibility = v;
            if (_clockDelBtn != null) _clockDelBtn.Visibility = v;
        }

        private void ApplyEditModeToWeather()
        {
            for (int i = 0; i < _weatherViews.Count; i++)
            {
                var v = _weatherViews[i];
                if (v.DelBtn != null) v.DelBtn.Visibility = _editMode ? Visibility.Visible : Visibility.Collapsed;
                v.Root.Background = Palette.Clear;

                if (_editMode)
                {
                    var a = new DoubleAnimation(-0.55, 0.55, new Duration(TimeSpan.FromMilliseconds(155)))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = TimeSpan.FromMilliseconds(i * 43),
                    };
                    v.Wiggle.BeginAnimation(RotateTransform.AngleProperty, a);
                }
                else
                {
                    v.Wiggle.BeginAnimation(RotateTransform.AngleProperty, null);
                    v.Wiggle.Angle = 0;
                }
            }
        }

        private void ApplyEditModeTo(List<QuoteView> views)
        {
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                v.DelBtn.Visibility = _editMode ? Visibility.Visible : Visibility.Collapsed;
                v.Root.Background = IdleBrush(v);

                if (_editMode)
                {
                    // 항목마다 시작 시점을 살짝 어긋나게 해서 기계적으로 보이지 않게 한다
                    var a = new DoubleAnimation(-0.55, 0.55, new Duration(TimeSpan.FromMilliseconds(155)))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = TimeSpan.FromMilliseconds(i * 43),
                    };
                    v.Wiggle.BeginAnimation(RotateTransform.AngleProperty, a);
                }
                else
                {
                    v.Wiggle.BeginAnimation(RotateTransform.AngleProperty, null);
                    v.Wiggle.Angle = 0;
                }
            }
        }

        // ---------- 종목 추가 / 삭제 ----------

        private void AddSymbol(SymbolDef def)
        {
            if (def == null || _cfg.Symbols.Count >= Config.MaxSymbols) return;
            foreach (var d in _cfg.Symbols) if (d.Key == def.Key) return;

            _cfg.Symbols.Add(def);
            _cfg.Save();
            RebuildSymbolViews();
            RequestQuoteRefresh();
        }

        private void RemoveSymbol(SymbolDef def)
        {
            int idx = -1;
            for (int i = 0; i < _cfg.Symbols.Count; i++)
                if (_cfg.Symbols[i].Key == def.Key) { idx = i; break; }
            if (idx < 0) return;

            // 지금 보이는 쪽에서 해당 항목을 찾아 사라지는 동작을 보여준다
            QuoteView target = null;
            foreach (var v in (_cfg.GridView ? _tiles : _rows))
                if (v.Def.Key == def.Key) { target = v; break; }

            int removeAt = idx;
            Action commit = () =>
            {
                for (int i = 0; i < _cfg.Symbols.Count; i++)
                    if (_cfg.Symbols[i].Key == def.Key) { _cfg.Symbols.RemoveAt(i); break; }

                _undoStack.Add(new RemovedItem { Def = def, Index = removeAt });

                if (_cfg.Symbols.Count == 0) _cfg.Symbols = Sources.Defaults();   // 전부 지우면 기본값으로

                var cur = CurrentDef;
                if (cur != null) _cfg.Symbol = cur.Key;
                _cfg.Save();

                RebuildSymbolViews();
                RefreshHeader();
                RefreshQuote();
                UpdateUndoHint();
            };

            if (target != null) AnimateRemoveElement(target.Scale, target.Root, commit);
            else commit();
        }

        /// <summary>앞으로 살짝 튀어나왔다가 작아지며 사라진다.</summary>
        private static void AnimateRemoveElement(ScaleTransform sc, UIElement root, Action done)
        {
            root.IsHitTestVisible = false;   // 사라지는 동안 다시 눌리지 않게

            var pop = new DoubleAnimationUsingKeyFrames();
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            });

            var pop2 = pop.Clone();
            bool fired = false;
            pop.Completed += (s, e) =>
            {
                if (fired) return;
                fired = true;
                if (done != null) done();
            };

            var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(210)))
            {
                BeginTime = TimeSpan.FromMilliseconds(120),
            };

            sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop2);
            root.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>Alt+Z. 편집 모드가 유지되는 동안만 되돌릴 수 있다.</summary>
        private void UndoRemove()
        {
            if (!_editMode || _undoStack.Count == 0) return;

            var last = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);

            var target = last.IsWeather ? _cfg.Weathers : _cfg.Symbols;
            foreach (var d in target)
                if (d.Key == last.Def.Key) { UpdateUndoHint(); return; }   // 이미 돌아와 있으면 무시

            int at = last.Index;
            if (at < 0) at = 0;
            if (at > target.Count) at = target.Count;
            target.Insert(at, last.Def);
            _cfg.Save();

            if (last.IsWeather)
            {
                RebuildWeatherViews();
                RequestWeatherRefresh();
            }
            else
            {
                RebuildSymbolViews();
                RefreshHeader();
                RefreshQuote();
                RequestQuoteRefresh();
            }
            UpdateUndoHint();
        }

        private void UpdateUndoHint()
        {
            if (_undoHint == null) return;
            _undoHint.Visibility = (_editMode && _undoStack.Count > 0)
                                 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- 목록 재구성 ----------

        private void RebuildSymbolViews()
        {
            _rows.Clear();
            _tiles.Clear();
            _listPanel.Children.Clear();
            _gridPanel.Children.Clear();

            foreach (var def in _cfg.Symbols)
            {
                var r = BuildRow(def);
                _rows.Add(r);
                _listPanel.Children.Add(r.Root);

                var t = BuildTile(def);
                t.IsTile = true;
                t.Root.Background = Palette.Tile;
                _tiles.Add(t);
                _gridPanel.Children.Add(t.Root);
            }

            ApplyViewMode();
            ApplyVisibleLimit();
            RefreshSymbolViews();
            if (_editMode) ApplyEditMode();
        }

        private void ApplyViewMode()
        {
            ApplyCardWidth();
            bool grid = _cfg.GridView;
            if (_listPanel != null) _listPanel.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
            if (_gridPanel != null) _gridPanel.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
            if (_viewToggle != null)
            {
                _viewToggle.Text = grid ? "☰" : "▦";
                _viewToggle.ToolTip = grid ? "목록으로 보기" : "타일로 보기";
            }
        }

        private void ToggleViewMode()
        {
            _cfg.GridView = !_cfg.GridView;
            // 타일로 넘어갈 때 홀수로 접혀 있으면 줄 단위에 맞춰준다
            if (_cfg.GridView && _cfg.ListLimit > 0 && _cfg.ListLimit % 2 == 1) SetListLimit(_cfg.ListLimit + 1);
            _cfg.Save();
            ApplyViewMode();
            ApplyVisibleLimit();
            RefreshSymbolViews();
            if (_editMode) ApplyEditMode();
        }

        private UIElement BuildDivider()
        {
            var b = new Border
            {
                Height = 1, Background = Palette.Divider,
                Margin = new Thickness(0, 12, 0, 12),
            };
            Grid.SetRow(b, 2);
            _dividerEl = b;
            return b;
        }

        private UIElement BuildWeather()
        {
            _weatherRow = new Grid();

            _weatherPanel = new StackPanel();
            _weatherRow.Children.Add(_weatherPanel);

            // 날씨 영역 우측 상단: 새로고침 + 접기
            var wxRefresh = MakeRefreshButton("날씨 새로고침", RequestWeatherRefresh);

            var wxCollapse = MakeIconButton("−", "날씨 접기", false, delegate
            {
                _cfg.ShowWeather = false;
                ApplyMinimized();
                _cfg.Save();
            });
            wxCollapse.Margin = new Thickness(7, 0, 0, 0);

            var wxClose = MakeIconButton("×", "날씨 닫기 (우클릭 메뉴로 다시 열기)", false, delegate
            {
                CloseSection("날씨");
            });
            wxClose.Margin = new Thickness(7, 0, 0, 0);

            // 목록 <-> 하나 크게 전환
            _wxViewToggle = MakeIconButton("▣", "하나만 크게 보기", false, ToggleWeatherViewMode);
            _wxViewToggle.Margin = new Thickness(0, 0, 7, 0);

            var wxTools = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -1, 0, 0),
            };
            wxTools.Children.Add(_wxViewToggle);
            wxTools.Children.Add(wxRefresh);
            wxTools.Children.Add(wxCollapse);
            wxTools.Children.Add(wxClose);
            _weatherRow.Children.Add(wxTools);

            RebuildWeatherViews();

            Grid.SetRow(_weatherRow, 3);
            return _weatherRow;
        }

        /// <summary>시세를 접었을 때 남는 얇은 줄.</summary>
        private UIElement BuildQuotesCollapsedBar()
        {
            var bar = MakeCollapsedBar("시세", delegate
            {
                _cfg.ShowQuotes = true;
                ApplyMinimized();
                _cfg.Save();
                RequestQuoteRefresh();
            });
            Grid.SetRow(bar, 1);
            _quotesBar = bar;
            return bar;
        }

        /// <summary>접힌 섹션 자리에 놓이는 공통 얇은 줄.</summary>
        private Border MakeCollapsedBar(string text, Action onOpen)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                Foreground = Palette.TextGhost,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var caret = new TextBlock
            {
                Text = "▾",
                FontSize = 9,
                Foreground = Palette.TextGhost,
                Margin = new Thickness(5, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            sp.Children.Add(label);
            sp.Children.Add(caret);

            var bar = new Border
            {
                Child = sp,
                Background = Palette.Clear,
                Cursor = Cursors.Hand,
                Padding = new Thickness(0, 4, 0, 4),
                Visibility = Visibility.Collapsed,
                ToolTip = text + " 펴기",
            };
            bar.MouseEnter += (s, e) => { label.Foreground = Palette.TextDim; caret.Foreground = Palette.TextDim; };
            bar.MouseLeave += (s, e) => { label.Foreground = Palette.TextGhost; caret.Foreground = Palette.TextGhost; };
            bar.MouseLeftButtonDown += (s, e) => { e.Handled = true; if (onOpen != null) onOpen(); };
            return bar;
        }

        /// <summary>날씨를 접었을 때 남는 얇은 줄. 눌러서 다시 편다.</summary>
        private UIElement BuildWeatherCollapsedBar()
        {
            var bar = MakeCollapsedBar("날씨", delegate
            {
                _cfg.ShowWeather = true;
                ApplyMinimized();
                _cfg.Save();
                RequestWeatherRefresh();
            });
            Grid.SetRow(bar, 3);
            _weatherBar = bar;
            return bar;
        }

        private void RebuildWeatherViews()
        {
            _weatherViews.Clear();
            _weatherPanel.Children.Clear();

            // 하나만 크게 보기.
            // 지역이 하나도 없으면 목록으로 떨어뜨린다 - 그래야 + 버튼이 나온다.
            if (_cfg.WeatherBig && _cfg.Weathers.Count > 0)
            {
                _addWeatherBtn = null;
                var big = BuildWeatherBigCard(MainWeatherDef);
                _weatherViews.Add(big);
                _weatherPanel.Children.Add(big.Root);
            }
            else
            {
                foreach (var def in _cfg.Weathers)
                {
                    var v = BuildWeatherRow(def);
                    _weatherViews.Add(v);
                    _weatherPanel.Children.Add(v.Root);
                }

                _weatherPanel.Children.Add(BuildAddWeatherButton());
            }

            ApplyWeatherViewMode();
            RefreshWeather();
            if (_editMode) ApplyEditMode();
        }

        private WeatherView BuildWeatherRow(SymbolDef def)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 삭제 배지
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 아이콘
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var def2 = def;
            var del = BuildDeleteBadge(delegate { RemoveWeather(def2); });
            del.VerticalAlignment = VerticalAlignment.Center;
            del.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(del, 0);
            g.Children.Add(del);

            var icon = WeatherIcon.Create(IconSize);
            var iconHost = new Grid
            {
                Width = IconSize, Height = IconSize,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            iconHost.Children.Add(icon);
            Grid.SetColumn(iconHost, 1);
            g.Children.Add(iconHost);

            var temp = new TextBlock { FontSize = 25, Foreground = Palette.Text };
            var desc = new TextBlock
            {
                FontSize = 14, Foreground = Palette.TextDim,
                Margin = new Thickness(8, 0, 0, 3), VerticalAlignment = VerticalAlignment.Bottom,
            };
            var line1 = new StackPanel { Orientation = Orientation.Horizontal };
            line1.Children.Add(temp);
            line1.Children.Add(desc);

            var sub = new TextBlock
            {
                Text = "불러오는 중", FontSize = 9.5, Foreground = Palette.TextFaint,
                Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            col.Children.Add(line1);
            col.Children.Add(sub);
            Grid.SetColumn(col, 2);
            g.Children.Add(col);

            var wiggle = new RotateTransform(0);
            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform(0, 0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(wiggle);
            group.Children.Add(translate);

            var root = new Border
            {
                Child = g,
                Background = Palette.Clear,
                Cursor = Cursors.Hand,
                Padding = new Thickness(2, 2, 2, 2),
                // 우측은 새로고침·접기 버튼 자리라 배경이 그 아래까지 깔리지 않게 비워둔다
                Margin = new Thickness(-2, 1, WeatherToolsGap, 1),
                CornerRadius = new CornerRadius(8),
                RenderTransform = group,
                RenderTransformOrigin = new Point(0.5, 0.5),
                ToolTip = "더블클릭: 네이버 날씨 / 꾹 누르기: 편집",
            };

            var v = new WeatherView
            {
                Def = def, Root = root, Icon = icon,
                Temp = temp, Desc = desc, Sub = sub,
                DelBtn = del, Wiggle = wiggle, Scale = scale, Translate = translate,
            };

            root.MouseEnter += (s, e) => { if (!_editMode) root.Background = Palette.Hover; };
            root.MouseLeave += (s, e) => { if (!_editMode) root.Background = Palette.Clear; CancelPress(); };
            root.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (e.ClickCount == 2)
                {
                    CancelPress();
                    if (!_editMode) OpenWeatherLink(def2);
                    return;
                }
                if (!_editMode) BeginPress();
            };
            root.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                bool wasLong = _longFired;
                CancelPress();
                if (wasLong) return;
                if (_editMode) ExitEditMode();
            };

            return v;
        }

        /// <summary>날씨 목록 아래의 지역 추가 버튼.</summary>
        /// <summary>크게 볼 지역. 설정이 없거나 그 지역이 지워졌으면 목록 첫 번째를 쓴다.</summary>
        private SymbolDef MainWeatherDef
        {
            get
            {
                if (_cfg.Weathers.Count == 0) return null;
                if (!string.IsNullOrEmpty(_cfg.WeatherMain))
                    foreach (var d in _cfg.Weathers)
                        if (d.Key == _cfg.WeatherMain) return d;
                return _cfg.Weathers[0];
            }
        }

        private void ApplyWeatherViewMode()
        {
            if (_wxViewToggle == null) return;
            bool big = _cfg.WeatherBig && _cfg.Weathers.Count > 0;
            _wxViewToggle.Text = big ? "☰" : "▣";
            _wxViewToggle.ToolTip = big ? "목록으로 보기" : "하나만 크게 보기";
        }

        /// <summary>
        /// 저장을 잠깐 미룬다. 휠처럼 연달아 들어오는 조작에서
        /// 매 번 config.json 을 다시 쓰지 않도록 마지막 한 번만 저장한다.
        /// </summary>
        private void SaveSoon()
        {
            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _saveTimer.Tick += delegate { _saveTimer.Stop(); _cfg.Save(); };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>큰 카드에서 휠을 굴려 다른 지역으로 넘긴다.</summary>
        private void CycleMainWeather(int step)
        {
            int n = _cfg.Weathers.Count;
            if (n < 2) return;

            var cur = MainWeatherDef;
            int i = 0;
            for (int k = 0; k < n; k++)
                if (_cfg.Weathers[k].Key == cur.Key) { i = k; break; }

            i = ((i + step) % n + n) % n;
            _cfg.WeatherMain = _cfg.Weathers[i].Key;
            RebuildWeatherViews();
            SaveSoon();
        }

        private void ToggleWeatherViewMode()
        {
            _cfg.WeatherBig = !_cfg.WeatherBig;
            _cfg.Save();
            RebuildWeatherViews();
        }

        /// <summary>큰 카드에서 어느 지역을 볼지 고르는 메뉴.</summary>
        private void ShowWeatherMainMenu(UIElement target)
        {
            if (_cfg.Weathers.Count == 0) return;

            var m = NewMenu();
            var cur = MainWeatherDef;
            foreach (var def in _cfg.Weathers)
            {
                var def2 = def;
                var mi = NewItem(def2.Label);
                mi.IsCheckable = true;
                mi.IsChecked = (cur != null && cur.Key == def2.Key);
                mi.Click += (s, e) =>
                {
                    _cfg.WeatherMain = def2.Key;
                    _cfg.Save();
                    RebuildWeatherViews();
                };
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());

            var list = NewItem("목록으로 보기");
            list.Click += (s, e) => ToggleWeatherViewMode();
            m.Items.Add(list);

            Popup(m, target);
        }

        /// <summary>
        /// 지역 하나를 크게 보여주는 카드. 목록 세 줄쯤 되는 높이를 쓴다.
        /// 지역 바꾸기는 이름 옆 캐럿으로 하고, 추가·삭제·정렬은 목록 보기에서 한다.
        /// </summary>
        private WeatherView BuildWeatherBigCard(SymbolDef def)
        {
            var def2 = def;

            var icon = WeatherIcon.Create(BigIconSize);
            var iconHost = new Grid
            {
                Width = BigIconSize,
                Height = BigIconSize,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            iconHost.Children.Add(icon);

            var temp = new TextBlock
            {
                FontSize = 46,
                Foreground = Palette.Text,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var top = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            top.Children.Add(iconHost);
            top.Children.Add(temp);

            var desc = new TextBlock
            {
                FontSize = 16,
                Foreground = Palette.TextDim,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            };

            // 지역 이름 + 캐럿 : 눌러서 크게 볼 지역을 바꾼다
            var city = new TextBlock
            {
                FontSize = 12.5,
                Foreground = Palette.TextDim,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var caret = new TextBlock
            {
                Text = "▾",
                FontSize = 9,
                Foreground = Palette.TextGhost,
                Margin = new Thickness(4, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var cityBox = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 7, 0, 0),
                Background = Palette.Clear,      // 히트 테스트를 받으려면 필요하다
                Cursor = Cursors.Hand,
                ToolTip = "다른 지역 보기",
            };
            cityBox.Children.Add(city);
            cityBox.Children.Add(caret);
            cityBox.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;               // 카드의 꾹 누르기로 넘어가지 않게 막는다
                CancelPress();
                ShowWeatherMainMenu(cityBox);
            };
            cityBox.MouseEnter += (s, e) => city.Foreground = Palette.Text;
            cityBox.MouseLeave += (s, e) => city.Foreground = Palette.TextDim;

            var sub = new TextBlock
            {
                Text = "불러오는 중",
                FontSize = 10.5,
                Foreground = Palette.TextFaint,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var col = new StackPanel();
            col.Children.Add(top);
            col.Children.Add(desc);
            col.Children.Add(cityBox);
            col.Children.Add(sub);

            var wiggle = new RotateTransform(0);
            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform(0, 0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(wiggle);
            group.Children.Add(translate);

            var root = new Border
            {
                Child = col,
                Background = Palette.Clear,
                Cursor = Cursors.Hand,
                Padding = new Thickness(2, 6, 2, 6),
                // 위쪽은 도구 버튼 줄을 피해 내려놓는다. 좌우는 비워 가운데 정렬이 어긋나지 않게.
                Margin = new Thickness(-2, 15, -2, 1),
                CornerRadius = new CornerRadius(10),
                RenderTransform = group,
                RenderTransformOrigin = new Point(0.5, 0.5),
                ToolTip = "더블클릭: 네이버 날씨 / 꾹 누르기: 편집",
            };

            var v = new WeatherView
            {
                Def = def2, Root = root, Icon = icon,
                Temp = temp, Desc = desc, Sub = sub, City = city,
                DelBtn = null,                  // 삭제·정렬은 목록 보기에서 한다
                Wiggle = wiggle, Scale = scale, Translate = translate,
            };

            root.MouseEnter += (s, e) => { if (!_editMode) root.Background = Palette.Hover; };
            root.MouseLeave += (s, e) => { if (!_editMode) root.Background = Palette.Clear; CancelPress(); };
            root.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (e.ClickCount == 2)
                {
                    CancelPress();
                    if (!_editMode) OpenWeatherLink(def2);
                    return;
                }
                if (!_editMode) BeginPress();
            };
            root.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                bool wasLong = _longFired;
                CancelPress();
                if (wasLong) return;
                if (_editMode) ExitEditMode();
            };

            // 휠로 다른 지역을 넘겨본다.
            // Handled 로 막아야 창 전체 휠(시세 목록 넘기기)로 새어나가지 않는다.
            root.MouseWheel += (s, e) =>
            {
                if (_cfg.Weathers.Count < 2) return;
                e.Handled = true;
                CycleMainWeather(e.Delta > 0 ? -1 : 1);
            };

            return v;
        }

        private UIElement BuildAddWeatherButton()
        {
            var plus = new TextBlock
            {
                Text = "+",
                FontSize = 16,
                Foreground = Palette.TextDim,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0),
            };
            var circle = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = Palette.Hover,
                BorderBrush = Palette.CardEdge,
                BorderThickness = new Thickness(1),
                Child = plus,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 9, 0, 1),
                ToolTip = "날씨 지역 추가",
                Visibility = Visibility.Collapsed,   // 편집 모드에서만 보인다
            };
            circle.MouseEnter += (s, e) => { circle.Background = Palette.TileHover; plus.Foreground = Palette.Text; };
            circle.MouseLeave += (s, e) => { circle.Background = Palette.Hover; plus.Foreground = Palette.TextDim; };
            circle.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                SearchWindow.OpenWeather(this, _cfg.Weathers, AddWeather);
            };
            _addWeatherBtn = circle;
            return circle;
        }

        private void AddWeather(SymbolDef def)
        {
            if (def == null || _cfg.Weathers.Count >= Config.MaxSymbols) return;
            if (double.IsNaN(def.Lat) || double.IsNaN(def.Lon)) return;
            foreach (var d in _cfg.Weathers) if (d.Key == def.Key) return;

            _cfg.Weathers.Add(def);
            _cfg.Save();
            RebuildWeatherViews();
            RequestWeatherRefresh();
        }

        private void RemoveWeather(SymbolDef def)
        {
            int idx = -1;
            for (int i = 0; i < _cfg.Weathers.Count; i++)
                if (_cfg.Weathers[i].Key == def.Key) { idx = i; break; }
            if (idx < 0) return;

            WeatherView target = null;
            foreach (var v in _weatherViews) if (v.Def.Key == def.Key) { target = v; break; }

            int removeAt = idx;
            Action commit = () =>
            {
                for (int i = 0; i < _cfg.Weathers.Count; i++)
                    if (_cfg.Weathers[i].Key == def.Key) { _cfg.Weathers.RemoveAt(i); break; }

                _undoStack.Add(new RemovedItem { Def = def, Index = removeAt, IsWeather = true });
                _cfg.Save();
                RebuildWeatherViews();
                UpdateUndoHint();
            };

            if (target != null) AnimateRemoveElement(target.Scale, target.Root, commit);
            else commit();
        }

        private static readonly string[] DayNames = { "일", "월", "화", "수", "목", "금", "토" };

        private void AddClock(Grid root)
        {
            var divider = new Border
            {
                Height = 1,
                Background = Palette.Divider,
                Margin = new Thickness(0, 12, 0, 11),
            };
            Grid.SetRow(divider, 4);
            root.Children.Add(divider);
            _clockDivider = divider;

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 접기 배지
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 날짜
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _clockDelBtn = BuildDeleteBadge(delegate
            {
                _cfg.ShowClock = false;
                ApplyClockVisibility();
                _cfg.Save();
            });
            _clockDelBtn.VerticalAlignment = VerticalAlignment.Center;
            _clockDelBtn.Margin = new Thickness(0, 0, 6, 0);
            _clockDelBtn.ToolTip = "시계 숨기기";
            Grid.SetColumn(_clockDelBtn, 0);
            g.Children.Add(_clockDelBtn);

            // 왼쪽에 날짜·요일, 오른쪽에 시간. 크기는 같게 두고 색으로만 구분한다.
            _clockDate = new TextBlock
            {
                FontSize = 18.5,
                Foreground = Palette.TextDim,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(_clockDate, 1);
            g.Children.Add(_clockDate);

            _clockTime = new TextBlock
            {
                FontSize = 18.5,
                Foreground = Palette.Text,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(_clockTime, 2);
            g.Children.Add(_clockTime);

            // 시계에서도 꾹 눌러 편집 모드로 들어갈 수 있게
            var host = new Border { Child = g, Background = Palette.Clear };
            host.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount != 1) return;
                e.Handled = true;
                if (!_editMode) BeginPress();
            };
            host.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                bool wasLong = _longFired;
                CancelPress();
                if (wasLong) return;
                if (_editMode) ExitEditMode();
            };
            host.MouseLeave += (s, e) => CancelPress();

            Grid.SetRow(host, 5);
            root.Children.Add(host);
            _clockRow = host;

            UpdateClock();
        }

        private void UpdateClock()
        {
            if (Docked)
            {
                UpdateDockClock();
                // 시계를 떼어냈으면 그쪽 창은 계속 돌아야 한다
                if (!_cfg.Separated) return;
            }
            var now = DateTime.Now;
            SetText(_clockTime, now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            SetText(_clockDate, string.Format(CultureInfo.InvariantCulture, "{0}월 {1}일 ({2})",
                                              now.Month, now.Day, DayNames[(int)now.DayOfWeek]));
        }

        /// <summary>다음 시세 갱신까지 남은 시간.</summary>
        private void UpdateCountdown()
        {
            if (_countdown == null || _countdown.Visibility != Visibility.Visible) return;

            double left = 0;
            if (_lastQuoteAt != DateTime.MinValue)
                left = _cfg.QuoteIntervalSec - (DateTime.UtcNow - _lastQuoteAt).TotalSeconds;
            if (left < 0) left = 0;

            int s = (int)Math.Ceiling(left);
            SetText(_countdown, (s / 60).ToString(CultureInfo.InvariantCulture) + ":"
                              + (s % 60).ToString("00", CultureInfo.InvariantCulture));
        }

        /// <summary>수신 상태등. 값을 받고 있으면 초록, 끊겼으면 빨강.</summary>
        private void UpdateStatusDot()
        {
            if (_statusDot == null) return;
            bool ok = _lastFetchOk && !IsStale;
            _statusDot.Fill = ok ? Palette.Online : Palette.Offline;
            _statusDot.ToolTip = ok ? "실시간 수신 중" : "연결 끊김 — 값이 갱신되지 않고 있습니다";
        }

        /// <summary>
        /// 시계 타이머. 0.5초마다 깨어나지만 SetText 가 값이 바뀔 때만 대입하므로
        /// 실제 화면 갱신은 초당 1회다. 최소화하거나 시계를 끄면 타이머 자체를 멈춘다.
        /// </summary>
        private void UpdateClockTimer()
        {
            // 시계뿐 아니라 갱신 카운트다운도 이 타이머로 움직인다
            bool need = Docked
                     || (_cfg.Separated && _cfg.ShowClock)
                     || (!_cfg.Minimized && (_cfg.ShowClock || _cfg.Expanded));

            if (!need)
            {
                if (_clockTimer != null) _clockTimer.Stop();
                return;
            }

            if (_clockTimer == null)
            {
                _clockTimer = new DispatcherTimer(DispatcherPriority.Background);
                _clockTimer.Interval = TimeSpan.FromMilliseconds(500);
                _clockTimer.Tick += (s, e) => { UpdateClock(); UpdateCountdown(); };
            }
            UpdateClock();
            _clockTimer.Start();
        }

        /// <summary>
        /// 카드 맨 아래 공지 줄. + - 편집 모드에서 새 버전이 있을 때만 나타나고,
        /// 그 밖에는 자리조차 차지하지 않는다. 누르는 동작은 없다 - 알려주기만 한다.
        /// </summary>
        private void AddNotice(Grid root)
        {
            var divider = new Border
            {
                Height = 1,
                Background = Palette.Divider,
                Margin = new Thickness(0, 12, 0, 10),
            };

            _noticeText = new TextBlock
            {
                FontSize = 12.5,
                Foreground = Palette.Notice,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            _noticeRow = new StackPanel { Visibility = Visibility.Collapsed };
            _noticeRow.Children.Add(divider);
            _noticeRow.Children.Add(_noticeText);

            Grid.SetRow(_noticeRow, 7);
            root.Children.Add(_noticeRow);
        }

        /// <summary>
        /// 공지 줄을 다시 그린다.
        /// latest 가 null 이면 '이번엔 확인하지 못했다' 는 뜻이므로 이전 상태를 그대로 둔다.
        /// </summary>
        private void ApplyNotice(string latest)
        {
            if (_noticeRow == null) return;
            if (latest != null) _latestVersion = latest;

            bool show = _editMode                       // 평소엔 감춰 둔다 - 꾹 눌러야 보인다
                        && _cfg.NotifyUpdate
                        && !_cfg.Minimized
                        && Config.IsNewer(_latestVersion);

            if (show) SetText(_noticeText, "새 버전 v" + _latestVersion + " 이 있습니다");
            _noticeRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        /// <summary>
        /// 즐겨찾기 칸. 바탕화면이나 탐색기에서 바로가기(.lnk)를 끌어다 놓으면 담긴다.
        /// exe 를 직접 받지 않는 이유는 Apps.cs 머리말에 적어 두었다.
        /// </summary>
        private void AddApps(Grid root)
        {
            _appsDivider = new Border
            {
                Height = 1,
                Background = Palette.Divider,
                Margin = new Thickness(0, 11, 0, 9),
            };

            _appsPanel = new WrapPanel { Orientation = Orientation.Horizontal };

            _appsHint = new TextBlock
            {
                Text = "바로가기를 끌어다 놓으세요",
                FontSize = 10,
                Foreground = Palette.TextGhost,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 3),
            };

            _appsRow = new StackPanel { Background = Palette.Clear, AllowDrop = true };
            _appsRow.Children.Add(_appsDivider);
            _appsRow.Children.Add(_appsPanel);
            _appsRow.Children.Add(_appsHint);

            _appsRow.DragOver += OnAppsDragOver;
            _appsRow.DragEnter += OnAppsDragOver;
            _appsRow.Drop += OnAppsDrop;
            _appsRow.DragLeave += (s, e) => _appsRow.Background = Palette.Clear;

            Grid.SetRow(_appsRow, 6);
            root.Children.Add(_appsRow);

            RebuildAppViews();
        }

        /// <summary>끌고 온 것이 바로가기인지 본다. 아니면 받지 않는다.</summary>
        private void OnAppsDragOver(object sender, DragEventArgs e)
        {
            bool ok = false;
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null)
                        foreach (string f in files)
                            if (Apps.IsAllowed(f)) { ok = true; break; }
                }
            }
            catch { }

            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            _appsRow.Background = ok ? Palette.Hover : Palette.Clear;
            e.Handled = true;
        }

        private void OnAppsDrop(object sender, DragEventArgs e)
        {
            _appsRow.Background = Palette.Clear;
            e.Handled = true;
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                AddAppPaths(files);
            }
            catch { }
        }

        /// <summary>
        /// 바로가기 여러 개를 담는다. 끌어다 놓기와 '고르기' 가 같은 길을 쓴다.
        /// 검사는 Apps.IsAllowed 한 군데서만 한다 — .lnk 만 받고 명령줄은 우리가 만들지 않는다.
        /// </summary>
        private void AddAppPaths(string[] files)
        {
            if (files == null) return;

            bool added = false;
            foreach (string f in files)
            {
                if (_cfg.Apps.Count >= Apps.MaxApps) break;
                if (!Apps.IsAllowed(f)) continue;

                // 보관소로 복사해 둔다. 원본이 지워져도 즐겨찾기는 남는다.
                string file = Apps.Import(f);
                string path = Apps.PathOf(file);
                if (path == null || !Apps.IsAllowed(path)) continue;

                string key = path.ToLowerInvariant();
                bool dup = false;
                foreach (var a in _cfg.Apps) if (a.Key == key) { dup = true; break; }
                if (dup) continue;

                _cfg.Apps.Add(new AppDef { Path = path, File = file, Label = Apps.NameOf(f) });
                added = true;
            }

            if (!added) return;
            RebuildAppViews();
            _cfg.Save();
        }

        /// <summary>
        /// 바로가기를 골라서 담는다.
        ///
        /// 끌어다 놓기가 막히는 경우가 있어서 길을 하나 더 둔다. 위젯이 관리자 권한으로 떠 있으면
        /// Windows UIPI 가 탐색기에서 오는 드래그를 통째로 차단하는데, 그건 우리가 손쓸 수 없다.
        /// 고르기는 우리 프로세스 안에서 일어나므로 그 제약을 받지 않는다.
        /// </summary>
        private void PickApps()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Title = "바로가기 고르기";
                dlg.Filter = "바로가기 (*.lnk)|*.lnk";
                dlg.Multiselect = true;
                dlg.CheckFileExists = true;
                try { dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
                catch { }

                if (dlg.ShowDialog(this) != true) return;
                AddAppPaths(dlg.FileNames);
            }
            catch { }
        }

        private void RebuildAppViews()
        {
            _dockAppsSig = "";   // 붙어 있는 바에도 반영되어야 한다
            RefreshPanelBars();
            _appViews.Clear();
            _appsPanel.Children.Clear();

            for (int i = 0; i < _cfg.Apps.Count; i++)
            {
                _appsPanel.Children.Add(BuildSepSlot(i, false, AppTile, 3, true));
                _appsPanel.Children.Add(BuildAppTile(_cfg.Apps[i]).Root);
            }
            _appsPanel.Children.Add(BuildSepSlot(_cfg.Apps.Count, false, AppTile, 3, true));

            // 더 담을 수 있을 때만 '고르기' 자리를 남긴다
            if (_cfg.Apps.Count < Apps.MaxApps)
                _appsPanel.Children.Add(BuildAppPicker());

            // 비어 있을 때만 안내를 띄운다. 담기고 나면 자리를 차지하지 않는다.
            _appsHint.Visibility = (_cfg.Apps.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            _appsHint.Text = Apps.IsElevated()
                ? "관리자 권한이라 끌어다 놓기가 막힙니다 · + 를 누르세요"
                : "바로가기를 끌어다 놓거나 + 를 누르세요";
            if (_editMode) ApplyEditModeToApps();
        }

        private AppView BuildAppTile(AppDef def)
        {
            var def2 = def;

            var img = Apps.LoadIcon(def.Path);
            UIElement face;
            if (img != null)
            {
                face = new Image { Source = img, Width = AppTile - 12, Height = AppTile - 12 };
            }
            else
            {
                // 아이콘을 못 읽으면 첫 글자로 대신한다
                string t = string.IsNullOrEmpty(def.Label) ? "?" : def.Label.Substring(0, 1);
                face = new TextBlock
                {
                    Text = t,
                    FontSize = 14,
                    Foreground = Palette.TextDim,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }

            var del = BuildDeleteBadge(delegate { RemoveApp(def2); });
            del.HorizontalAlignment = HorizontalAlignment.Left;
            del.VerticalAlignment = VerticalAlignment.Top;
            del.Margin = new Thickness(-3, -3, 0, 0);

            // 그림 갈아끼우기. 지우기(왼쪽 위)와 헷갈리지 않게 아래 가운데에 둔다.
            var swap = BuildIconBadge(def2);

            var cell = new Grid();
            cell.Children.Add(face);
            cell.Children.Add(del);
            cell.Children.Add(swap);

            var wiggle = new RotateTransform(0);

            var root = new Border
            {
                Child = cell,
                Width = AppTile,
                Height = AppTile,
                // 좌우를 같게 둔다. 오른쪽에만 여백을 주면 구분선이 한쪽 아이콘에 붙어 보인다.
                Margin = new Thickness(3, 0, 3, 5),
                CornerRadius = new CornerRadius(8),
                Background = Palette.Tile,
                BorderBrush = Palette.TileEdge,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = def.Label,
                RenderTransform = wiggle,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };

            root.MouseEnter += (s, e) => { if (!_editMode) root.Background = Palette.TileHover; };
            root.MouseLeave += (s, e) => { if (!_editMode) root.Background = Palette.Tile; CancelPress(); };
            root.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (!_editMode) BeginPress();
            };
            root.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                bool wasLong = _longFired;
                CancelPress();
                if (wasLong) return;
                if (_editMode) { ExitEditMode(); return; }
                Apps.Open(def2);
            };

            var v = new AppView { Def = def2, Root = root, DelBtn = del, IconBtn = swap, Wiggle = wiggle };
            _appViews.Add(v);
            return v;
        }

        /// <summary>바로가기를 고르는 빈 타일. 끌어다 놓기가 막힌 환경에서도 담을 수 있어야 한다.</summary>
        private UIElement BuildAppPicker()
        {
            var plus = new TextBlock
            {
                Text = "+",
                FontSize = 17,
                Foreground = Palette.TextGhost,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var box = new Border
            {
                Child = plus,
                Width = AppTile,
                Height = AppTile,
                // 좌우를 같게 둔다. 오른쪽에만 여백을 주면 구분선이 한쪽 아이콘에 붙어 보인다.
                Margin = new Thickness(3, 0, 3, 5),
                CornerRadius = new CornerRadius(8),
                Background = Palette.Clear,
                BorderBrush = Palette.TileEdge,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "바로가기(.lnk) 고르기",
            };

            box.MouseEnter += (s, e) => { box.Background = Palette.TileHover; };
            box.MouseLeave += (s, e) => { box.Background = Palette.Clear; };
            box.MouseLeftButtonDown += (s, e) => { e.Handled = true; };
            box.MouseLeftButtonUp += (s, e) => { e.Handled = true; ShowAddMenu(box); };
            return box;
        }

        /// <summary>
        /// 무엇을 담을지 고른다.
        ///
        /// 바탕화면 바로가기는 파일 선택창으로, Claude·ChatGPT·Gemini 같은
        /// 스토어·웹앱은 .lnk 가 아예 없으므로 설치된 앱 목록에서 고른다.
        /// </summary>
        private void ShowAddMenu(UIElement anchor)
        {
            var m = NewMenu();

            var file = NewItem("바로가기 파일…");
            file.Click += (s, e) => PickApps();
            m.Items.Add(file);

            var store = NewItem("설치된 앱…");
            store.Click += (s, e) => PickInstalledApp();
            m.Items.Add(store);

            m.PlacementTarget = anchor;
            m.IsOpen = true;
        }

        /// <summary>설치된 앱 목록에서 골라 담는다 (스토어·웹앱처럼 .lnk 가 없는 것들).</summary>
        private void PickInstalledApp()
        {
            try
            {
                var w = new AppPickWindow(_cfg.Opacity);
                w.Owner = this;
                if (w.ShowDialog() != true || w.Chosen == null) return;

                if (_cfg.Apps.Count >= Apps.MaxApps) return;

                string file = Apps.ImportApp(w.Chosen);
                string path = Apps.PathOf(file);
                if (path == null || !Apps.IsAllowed(path)) return;

                string key = path.ToLowerInvariant();
                foreach (var a in _cfg.Apps) if (a.Key == key) return;   // 이미 있다

                _cfg.Apps.Add(new AppDef { Path = path, File = file, Label = w.Chosen.Name });
                RebuildAppViews();
                _cfg.Save();
            }
            catch { }
        }

        private void RemoveApp(AppDef def)
        {
            for (int i = 0; i < _cfg.Apps.Count; i++)
            {
                if (_cfg.Apps[i].Key != def.Key) continue;
                Apps.Forget(_cfg.Apps[i].File);   // 보관소에서도 지운다
                _cfg.Apps.RemoveAt(i);
                break;
            }
            RebuildAppViews();
            _cfg.Save();
        }

        private void ApplyEditModeToApps()
        {
            foreach (var v in _appViews)
            {
                ShowBadge(v.DelBtn, _editMode);
                ShowBadge(v.IconBtn, _editMode);
                v.Root.Background = Palette.Tile;

                if (_editMode)
                {
                    var a = new DoubleAnimation(-0.55, 0.55, new Duration(TimeSpan.FromMilliseconds(155)))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                    };
                    v.Wiggle.BeginAnimation(RotateTransform.AngleProperty, a);
                }
                else
                {
                    v.Wiggle.BeginAnimation(RotateTransform.AngleProperty, null);
                    v.Wiggle.Angle = 0;
                }
            }
        }

        private void ApplyAppsVisibility()
        {
            if (_appsRow == null) return;
            _appsRow.Visibility = SectionOn(_cfg.ShowApps) ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 그 섹션을 완전히 닫는다. 접기와 달리 '펴기' 줄도 남기지 않는다.
        /// 되살리는 길은 우클릭 메뉴뿐이다 - 그래서 메뉴의 '표시' 항목이 닫힘도 같이 푼다.
        /// </summary>
        private void CloseSection(string key)
        {
            if (key == "시세") { _cfg.ShowQuotes = false; _cfg.QuotesClosed = true; }
            else if (key == "날씨") { _cfg.ShowWeather = false; _cfg.WeatherClosed = true; }
            else if (key == "즐겨찾기") { _cfg.ShowApps = false; _cfg.AppsClosed = true; }
            else if (key == "시계") { _cfg.ShowClock = false; _cfg.ClockClosed = true; }
            else return;

            ApplyMinimized();
            _cfg.Save();
        }


        /// <summary>
        /// 섹션 알맹이를 보일지.
        ///
        /// 조각 창으로 떼어냈으면 본 창이 접혔든 가장자리에 붙었든 상관없다.
        /// 창이 따로 떠 있는데 본 창을 따라 비어 버리면 되살릴 방법이 없다.
        /// 합쳐져 있을 때만 본 창의 접힘·붙임을 따른다.
        /// </summary>
        private bool SectionOn(bool show)
        {
            if (!show) return false;
            if (_cfg.Separated) return true;
            return !_cfg.Minimized && !Docked;
        }

        // ---------- 창 나누기 ----------

        /// <summary>
        /// 날씨·즐겨찾기·시계를 카드에서 떼어 각자 창으로 띄우거나, 도로 합친다.
        ///
        /// 화면 요소를 '그대로 옮겨 담기만' 한다. 새로 만들지 않으므로
        /// 데이터 갱신도 편집 모드도 손댈 것이 없다.
        /// </summary>
        private void ApplySeparation()
        {
            if (_cfg.Separated) SeparatePanels();
            else MergePanels();

            SyncPanelChrome();
            ApplyMinimized();
        }

        private void SeparatePanels()
        {
            double sc = _cfg.Scale;

            // 먼저 다 떼어낸다. 카드가 줄어든 뒤라야 그 아래 자리를 제대로 잡는다.
            UIElement wRow = null, aRow = null, cRow = null;
            if (_panelWeather == null && _weatherRow != null)
            {
                wRow = _weatherRow;
                _rootGrid.Children.Remove(_weatherRow);
            }
            if (_panelApps == null && _appsRow != null)
            {
                aRow = _appsRow;
                _rootGrid.Children.Remove(_appsRow);
            }
            if (_panelClock == null && _clockRow != null)
            {
                cRow = _clockRow;
                _rootGrid.Children.Remove(_clockRow);
            }

            // 떼어낸 창에는 구분선이 필요 없다. 카드 테두리가 이미 구분해 주고,
            // 머리 버튼 줄까지 얹히면 선 하나 때문에 카드가 괜히 길어진다.
            if (_dividerEl != null) _dividerEl.Visibility = Visibility.Collapsed;
            if (_clockDivider != null) _clockDivider.Visibility = Visibility.Collapsed;
            if (_appsDivider != null) _appsDivider.Visibility = Visibility.Collapsed;
            try { UpdateLayout(); } catch { }

            if (wRow != null)
            {
                _panelWeather = new PanelWindow("날씨", wRow, PanelScaleOf(_cfg.WeatherScale),
                    delegate(double x, double y) { _cfg.WeatherX = x; _cfg.WeatherY = y; SaveSoon(); },
                    delegate(double s) { _cfg.WeatherScale = s; SaveSoon(); });
                WirePanelDock(_panelWeather, _cfg.WeatherEdge);
                ShowPanel(_panelWeather, _cfg.WeatherX, _cfg.WeatherY);
            }
            if (aRow != null)
            {
                _panelApps = new PanelWindow("즐겨찾기", aRow, PanelScaleOf(_cfg.AppsScale),
                    delegate(double x, double y) { _cfg.AppsX = x; _cfg.AppsY = y; SaveSoon(); },
                    delegate(double s) { _cfg.AppsScale = s; SaveSoon(); });
                WirePanelDock(_panelApps, _cfg.AppsEdge);
                ShowPanel(_panelApps, _cfg.AppsX, _cfg.AppsY);
            }
            if (cRow != null)
            {
                _panelClock = new PanelWindow("시계", cRow, PanelScaleOf(_cfg.ClockScale),
                    delegate(double x, double y) { _cfg.ClockX = x; _cfg.ClockY = y; SaveSoon(); },
                    delegate(double s) { _cfg.ClockScale = s; SaveSoon(); });
                WirePanelDock(_panelClock, _cfg.ClockEdge);
                ShowPanel(_panelClock, _cfg.ClockX, _cfg.ClockY);
            }

            // 기억해 둔 자리가 없는 창은 카드 아래로 쌓는다.
            // SizeToContent 로 카드가 줄어드는 것은 레이아웃 한 바퀴 뒤라,
            // 지금 높이를 재면 떼어내기 전 값이 나온다. 그래서 한 박자 미룬다.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)delegate
            {
                try { StackLoosePanels(); } catch { }
            });
        }

        /// <summary>
        /// 자리를 정해준 적이 없거나, 정해준 자리가 화면 밖인 창을 카드 아래로 차곡차곡 놓는다.
        /// 화면 밖까지 보는 이유: 한 번 이상한 좌표가 저장되면 다시 켜도 영영 안 보이기 때문이다.
        /// </summary>
        private void StackLoosePanels()
        {
            double y = Top + (ActualHeight > 0 ? ActualHeight : 200) + 6;
            bool moved = false;

            if (_panelWeather != null && Loose(_panelWeather, _cfg.WeatherX))
            { y = PlaceUnder(_panelWeather, y); moved = true; }
            if (_panelApps != null && Loose(_panelApps, _cfg.AppsX))
            { y = PlaceUnder(_panelApps, y); moved = true; }
            if (_panelClock != null && Loose(_panelClock, _cfg.ClockX))
            { y = PlaceUnder(_panelClock, y); moved = true; }

            if (moved) SavePanelPlaces();
        }

        private bool Loose(PanelWindow w, double savedX)
        {
            return double.IsNaN(savedX) || OffScreen(w);
        }

        private double PlaceUnder(PanelWindow w, double y)
        {
            double hh = w.ActualHeight > 0 ? w.ActualHeight : 48;

            // 아래로 넘치면 화면 안으로 되돌린다. 겹쳐 놓이더라도 안 보이는 것보다는 낫다.
            Rect box;
            if (ScreenBoxOf(Left, Top, 200, 200, out box) && y + hh > box.Bottom)
                y = box.Bottom - hh;

            w.Left = Left;
            w.Top = y;
            return y + hh;
        }

        private void MergePanels()
        {
            if (_appsDivider != null) _appsDivider.Visibility = Visibility.Visible;

            // 붙어 있는 채로 합치면 확보한 공간이 남는다. 먼저 떼어낸다.
            foreach (var w in EachPanel())
            {
                if (w == null) continue;
                try { w.Undock(); } catch { }
            }

            if (_panelWeather != null)
            {
                var c = _panelWeather.Detach();
                _panelWeather = null;
                if (c != null) { Grid.SetRow(c, 3); _rootGrid.Children.Add(c); }
            }
            if (_panelApps != null)
            {
                var c = _panelApps.Detach();
                _panelApps = null;
                if (c != null) { Grid.SetRow(c, 6); _rootGrid.Children.Add(c); }
            }
            if (_panelClock != null)
            {
                var c = _panelClock.Detach();
                _panelClock = null;
                if (c != null) { Grid.SetRow(c, 5); _rootGrid.Children.Add(c); }
            }
        }

        /// <summary>떼어낸 창을 띄운다. 자리는 기억해 둔 것이 있으면 그대로, 없으면 나중에 쌓는다.</summary>
        private void ShowPanel(PanelWindow w, double x, double y)
        {
            if (!double.IsNaN(x) && !double.IsNaN(y)) { w.Left = x; w.Top = y; }
            else { w.Left = Left; w.Top = Top; }   // 잠깐 카드 위에 뒀다가 StackLoosePanels 가 옮긴다

            w.Opacity = _cfg.Opacity;
            w.SetTopmost(_cfg.Topmost);
            w.Show();
        }

        /// <summary>겉모습(투명도·항상 위·배율)과 보이기를 본 창에 맞춘다.</summary>
        private void SyncPanelChrome()
        {
            foreach (var w in EachPanel())
            {
                if (w == null) continue;
                w.Opacity = _cfg.Opacity;
                w.SetTopmost(_cfg.Topmost);
                w.SetScale(PanelScaleOf(SavedScaleOf(w.Key)));
            }
            SyncPanelVisibility();
        }

        /// <summary>섹션을 접거나 최소화하면 그 조각 창도 같이 숨는다.</summary>
        private void SyncPanelVisibility()
        {
            // 조각 창은 각자 붙고 각자 접힌다. 본 창을 따라 사라지지 않는다.
            // 접었을 때도 창째로 숨기지 않는다 - 숨기면 다시 펼 방법이 없다.
            SyncPanel(_panelWeather, _cfg.ShowWeather, _cfg.WeatherClosed);
            SyncPanel(_panelApps, _cfg.ShowApps, _cfg.AppsClosed);
            SyncPanel(_panelClock, _cfg.ShowClock, _cfg.ClockClosed);
        }

        private void SyncPanel(PanelWindow w, bool show, bool closed)
        {
            if (w == null) return;
            w.SetState(show, closed);          // 붙어 있으면 자리까지 내놓는다
            w.SetFolded(!show && !closed);     // 닫았으면 '펴기' 줄도 없다
        }

        /// <summary>
        /// 가장자리에 붙기 직전, 조각 창들이 카드에서 얼마나 떨어져 있었는지 적어 둔다.
        /// </summary>
        private void RememberPanelOffsets()
        {
            _panelOff.Clear();
            foreach (var w in EachPanel())
            {
                if (w == null) continue;
                _panelOff[w.Key] = new Point(w.Left - Left, w.Top - Top);
            }
        }

        /// <summary>
        /// 떼어낸 뒤 조각 창들을 붙이기 전 자리로 돌려놓는다.
        ///
        /// 붙는 동안 카드는 화면 끝의 바가 되었다가 원래 자리로 돌아온다.
        /// 조각들을 같이 돌려놓지 않으면 바가 있던 가장자리에 남아 사라진 것처럼 보인다.
        /// 적어둔 것이 없으면(붙은 채로 위젯을 다시 켠 경우) 설정에 남은 자리를 쓰되,
        /// 그것마저 화면 밖이면 카드 아래에 다시 쌓아 준다.
        /// </summary>
        private void RestorePanelPlaces()
        {
            foreach (var w in EachPanel())
            {
                if (w == null) continue;
                Point off;
                if (!_panelOff.TryGetValue(w.Key, out off)) continue;
                w.Left = Left + off.X;
                w.Top = Top + off.Y;
            }
            _panelOff.Clear();

            // 카드가 카드 크기로 돌아오는 것은 레이아웃 한 바퀴 뒤다.
            // 그 전에 재면 바 크기가 나오므로 '아래에 쌓기' 가 엉뚱한 자리로 간다.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)delegate
            {
                try
                {
                    double y = Top + (ActualHeight > 0 ? ActualHeight : 200) + 6;
                    foreach (var w in EachPanel())
                    {
                        if (w == null || !OffScreen(w)) continue;
                        y = PlaceUnder(w, y);
                    }
                    SavePanelPlaces();
                }
                catch { }
            });
        }

        /// <summary>
        /// 어느 모니터에도 걸치지 않으면 참. 잃어버린 창을 찾아내는 데 쓴다.
        /// 여기서도 감싸는 사각형이 아니라 모니터 하나하나와 겹치는지 본다
        /// (이유는 ClampToScreen 주석 참고).
        /// </summary>
        private bool OffScreen(Window w)
        {
            try
            {
                double sx, sy;
                Dock.GetDpiScale(this, out sx, out sy);
                if (sx <= 0 || sy <= 0) return false;

                double ww = w.ActualWidth > 0 ? w.ActualWidth : 120;
                double hh = w.ActualHeight > 0 ? w.ActualHeight : 40;
                var r = new Rect(w.Left * sx, w.Top * sy, ww * sx, hh * sy);

                var all = Dock.AllScreens();
                if (all == null || all.Count == 0) return false;   // 모르면 건드리지 않는다

                foreach (var s in all) if (r.IntersectsWith(s.Bounds)) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 조각 창에 '가장자리 붙이기' 를 달아 준다.
        ///
        /// 바에 무엇을 실을지는 본 창이 안다. 조각 창은 자리만 잡고 내용은 여기서 만들어 넘긴다.
        /// </summary>
        private void WirePanelDock(PanelWindow w, DockEdge saved)
        {
            var me = w;
            me.MakeBarContent = delegate(bool vertical) { return BuildPanelBar(me.Key, vertical); };
            // 떼어낸 창에는 본 창 머리에 손이 안 닿는다. 제 머리를 달아 준다.
            // 날씨는 제 안에 이미 갖고 있어 건너뛴다(BuildWeather 의 wxTools).
            if (me.Key != "날씨")
            {
                me.SetHeaderTools(BuildPanelTools(me));
                // 즐겨찾기는 아이콘 여백(5) 때문에 버튼 아래가 더 벌어져 보인다. 그만큼 더 올린다.
                me.SetToolsLift(me.Key == "즐겨찾기" ? 8 : 4);
                me.SetFolded(false);
            }
            me.ContextMenu = BuildContextMenu(me);   // 우클릭으로도 설정에 닿게
            me.ClearBackdrop = _cfg.ClearBars.Contains(me.Key);   // 지난번에 고른 것을 되살린다

            me.Restore = delegate
            {
                if (me.Key == "날씨") { _cfg.ShowWeather = true; RequestWeatherRefresh(); }
                else if (me.Key == "즐겨찾기") _cfg.ShowApps = true;
                else if (me.Key == "시계") _cfg.ShowClock = true;
                ApplyMinimized();
                _cfg.Save();
            };
            me.EdgeChanged = delegate(DockEdge e)
            {
                // 어느 변에 붙었는지와 **어느 모니터에** 붙었는지를 함께 남긴다.
                // 떼면(None) 모니터 기억도 지운다 - 다음엔 놓인 자리에서 새로 고르는 게 맞다.
                string dev = (e == DockEdge.None) ? null : me.DockDevice;

                if (me.Key == "날씨") { _cfg.WeatherEdge = e; _cfg.WeatherDevice = dev; }
                else if (me.Key == "즐겨찾기") { _cfg.AppsEdge = e; _cfg.AppsDevice = dev; }
                else if (me.Key == "시계") { _cfg.ClockEdge = e; _cfg.ClockDevice = dev; }

                me.PreferDevice = dev;
                SaveSoon();
                // 자리 다시 잡기는 PanelWindow 가 DockStack 에 맡긴다(DockTo/Undock).
                // 그 Apply 는 본 창까지 포함해 그 변 전체를 한 번에 앉히므로 여기서 할 일이 없다.
            };

            if (saved == DockEdge.None) return;

            // 붙이기 전에 '지난번 모니터' 를 일러둔다. 안 그러면 저장된 창 좌표를 보고 고른다.
            if (me.Key == "날씨") me.PreferDevice = _cfg.WeatherDevice;
            else if (me.Key == "즐겨찾기") me.PreferDevice = _cfg.AppsDevice;
            else if (me.Key == "시계") me.PreferDevice = _cfg.ClockDevice;

            // 창이 뜬 뒤에 붙여야 한다. 핸들이 있어야 AppBar 로 등록할 수 있다.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)delegate
            {
                try { me.DockTo(saved); SyncPanelVisibility(); } catch { }
            });
        }

        /// <summary>
        /// 조각 창 오른쪽 위에 얹을 버튼들.
        ///
        /// 날씨의 '보기 전환(▣)' 에 해당하는 것이 시계·즐겨찾기에는 없으므로 그 자리에 설정(≡)을 둔다.
        /// 접기(─)는 날씨와 같다 - 접으면 '펴기' 줄만 남아 다시 펼 수 있다.
        /// </summary>
        private UIElement BuildPanelTools(PanelWindow w)
        {
            var me = w;

            var menu = MakeIconButton("≡", "설정", false, delegate
            {
                if (me.ContextMenu == null) return;
                me.ContextMenu.PlacementTarget = me;
                me.ContextMenu.IsOpen = true;
            });

            // ★ ≡ 옆에는 ─(U+2500) 를 쓰지 않는다 ★
            //   박스 그리기 글자라 기준선이 달라 위아래로 어긋난다.
            //   −(U+2212, 수학 빼기) 는 ≡ 와 같은 축에 놓여 나란히 선다.
            var fold = MakeIconButton("−", me.Key + " 접기", false, delegate
            {
                if (me.Key == "즐겨찾기") _cfg.ShowApps = false;
                else if (me.Key == "시계") _cfg.ShowClock = false;
                else _cfg.ShowWeather = false;
                ApplyMinimized();
                _cfg.Save();
            });
            fold.Margin = new Thickness(7, 0, 0, 0);

            var close = MakeIconButton("×", me.Key + " 닫기 (우클릭 메뉴로 다시 열기)", false, delegate
            {
                CloseSection(me.Key);
            });
            close.Margin = new Thickness(7, 0, 0, 0);

            var tools = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
            };
            tools.Children.Add(menu);
            tools.Children.Add(fold);
            tools.Children.Add(close);
            return tools;
        }

        /// <summary>조각 창이 가장자리에 붙었을 때 바에 실을 내용.</summary>
        private UIElement BuildPanelBar(string key, bool vertical)
        {
            // 즐겨찾기는 아이콘만 실으므로 가로로 붙어도 가운데에 모은다.
            // 날씨·시계는 글자라 왼쪽에 붙는 편이 읽기 좋다.
            bool center = vertical || key == "즐겨찾기";

            var box = new StackPanel
            {
                Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
                HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                VerticalAlignment = vertical ? VerticalAlignment.Top : VerticalAlignment.Center,
                // 가로 날씨 바는 좌우를 좁히고 상하를 넉넉히 - 한 줄 글자가 답답해 보였다
                Margin = vertical ? new Thickness(0, 8, 0, 0)
                                  : (key == "날씨" ? new Thickness(6, 4, 6, 4) : new Thickness(12, 0, 12, 0)),
            };

            if (key == "날씨")
            {
                var def = MainWeatherDef;
                WeatherInfo info;
                if (def != null && _weatherData.TryGetValue(def.Key, out info) && info.Ok)
                    box.Children.Add(BuildDockWeather(def, info, vertical));
                else
                    box.Children.Add(BarLabel("날씨"));
            }
            else if (key == "즐겨찾기")
            {
                if (_cfg.Apps.Count == 0) box.Children.Add(BarLabel("즐겨찾기"));
                else
                {
                    // 세로 바에서는 위에서 아래로 쌓는다. 크기는 그 창의 배율을 따른다.
                    double ps = (_panelApps != null && _panelApps.Scale > 0) ? _panelApps.Scale : 1;
                    DockEdge be = _panelApps != null ? _panelApps.Edge : DockEdge.Bottom;

                    // ★ 아이콘 줄은 붙은 변에 붙인다 ★
                    //   바 두께에는 호버로 커질 몫이 들어 있다. 줄을 한가운데 두면 그 몫이
                    //   위아래로 반씩 갈려, 아래에 아무것도 없는 여백이 남는다(실측 14px).
                    //   커지는 방향은 안쪽 하나뿐이니, 여유도 안쪽에만 있으면 된다.
                    var wrap = new WrapPanel
                    {
                        Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
                        HorizontalAlignment = !vertical
                            ? HorizontalAlignment.Center
                            : (be == DockEdge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right),
                        VerticalAlignment = vertical
                            ? VerticalAlignment.Center
                            : (be == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom),
                    };
                    double sz = DockIconSize(ps, vertical,
                        _panelApps != null ? _panelApps.BarThicknessDip : 0);

                    var row = new List<HoverItem>();
                    for (int i = 0; i < _cfg.Apps.Count; i++)
                    {
                        wrap.Children.Add(BuildSepSlot(i, vertical, sz));
                        UIElement el = BuildDockApp(_cfg.Apps[i], sz, vertical, i,
                            _panelApps != null ? _panelApps.Edge : DockEdge.Bottom,
                            DockIconPad(_panelApps != null ? _panelApps.BarThicknessDip : 0));
                        wrap.Children.Add(el);
                        AddHover(row, el);
                    }
                    wrap.Children.Add(BuildSepSlot(_cfg.Apps.Count, vertical, sz));
                    WireHoverRow(row, vertical, sz);
                    box.Children.Add(wrap);
                }
            }
            else   // 시계
            {
                _panelBarClock = new TextBlock
                {
                    FontSize = DockFontBase,
                    Foreground = Palette.Text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = vertical ? TextAlignment.Center : TextAlignment.Left,
                    Text = ClockBarText(vertical),
                };
                box.Children.Add(_panelBarClock);
            }

            // ★ 배경이 없는 바는 잡을 데가 마땅치 않다 ★
            //   투명해도 히트 테스트는 되지만, 눈에 안 보이니 어디를 잡아야 할지 알 수가 없다.
            //   끝에 버튼을 하나 둬서 눌러 바로 창으로 돌아갈 수 있게 한다.
            // 투명한 바는 바탕화면 위에 글자만 뜬다. 뒤가 밝으면 검게, 어두우면 희게 바꾼다.
            if (key == "날씨" && !vertical) PaintForBackdrop(box, PanelOf(key));

            if (key == "즐겨찾기" || (key == "날씨" && !vertical))
            {
                // ★ 손잡이는 내용 위에 겹쳐 놓지 않는다 ★
                //   Grid 로 포개 두면 바 두 개가 한 줄에 모였을 때 알약이 아이콘 위로 올라앉는다.
                //   줄지어 놓으면 겹칠 수가 없고, 알약이 제 바의 것이라는 것도 눈에 보인다.
                // 손잡이는 끝으로, 알맹이는 남은 자리로. DockPanel 이라 자리를 나눠 가지므로
                // 알약이 아이콘 위로 올라앉는 일이 없다 (Grid 로 포개면 겹쳤다).
                var host = new DockPanel { LastChildFill = true };

                if (key == "날씨")
                {
                    // 글자·아이콘을 통째로 키운다. LayoutTransform 이라 자리도 그만큼 잡는다
                    // (RenderTransform 으로 하면 커진 만큼 옆으로 삐져나가 잘린다).
                    box.LayoutTransform = new ScaleTransform(PanelWindow.WeatherZoom, PanelWindow.WeatherZoom);
                    box.HorizontalAlignment = HorizontalAlignment.Left;   // 날씨는 왼쪽 끝
                }
                // ★ 아이콘 덩어리를 띠 한가운데로 띄우지 않는다 ★
                //   아이콘은 저마다 붙은 변을 딛고 안쪽으로 커지게 되어 있다(BuildDockApp).
                //   덩어리를 가운데로 띄우면 그 발밑에 쓸모없는 여백이 남는다.
                //   띠를 그대로 채우게 두면 아이콘이 제 발로 화면 끝에 붙는다.
                // ★ StackPanel 은 '쌓는 축' 에서 자식 정렬을 무시한다 ★
                //   가로 바(가로로 쌓음)에서 가로로 늘리면 아이콘이 왼쪽에 처박힌다.
                //   늘리는 것은 쌓지 않는 축뿐 - 그 축에서만 wrap 이 변에 가서 붙는다.
                if (key == "즐겨찾기")
                {
                    box.VerticalAlignment = vertical ? VerticalAlignment.Center : VerticalAlignment.Stretch;
                    box.HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
                    box.Margin = new Thickness(0);   // 가운데로 맞추는데 한쪽 여백이 있으면 밀린다
                }
                else box.VerticalAlignment = VerticalAlignment.Center;

                UIElement pill = BarPopOut(PanelOf(key), vertical);
                DockPanel.SetDock(pill, vertical
                    ? System.Windows.Controls.Dock.Bottom
                    : System.Windows.Controls.Dock.Right);

                host.Children.Add(pill);   // 먼저 넣은 것이 끝자리를 가져간다
                host.Children.Add(box);
                return host;
            }
            return box;
        }

        /// <summary>
        /// 투명 바에 실은 글자색을 뒤 배경에 맞춰 통으로 바꾼다.
        ///
        /// 만들어진 뒤에 갈아끼우는 이유: 바에 싣는 것들(BuildDockWeather 등)은 카드에서도
        /// 쓰는 코드라 거기까지 색을 뚫고 들어가면 카드가 같이 바뀐다.
        /// 여기서 겉만 칠하면 카드는 그대로 둘 수 있다.
        /// </summary>
        private void PaintForBackdrop(DependencyObject root, PanelWindow w)
        {
            if (root == null || w == null) return;
            try
            {
                double sx, sy;
                Dock.GetDpiScale(this, out sx, out sy);

                var rect = new Rect(w.Left * sx, w.Top * sy,
                                    Math.Max(2, w.ActualWidth * sx), Math.Max(2, w.ActualHeight * sy));
                bool bright = Dock.IsBrightBehind(rect);

                Brush fg = bright ? Brushes.Black : Brushes.White;
                Brush dim = bright ? (Brush)new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))
                                   : (Brush)new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
                fg.Freeze();
                if (dim.CanFreeze) dim.Freeze();

                PaintTexts(root, fg, dim);
            }
            catch { }
        }

        /// <summary>글자만 골라 칠한다. 아이콘·그림은 건드리지 않는다.</summary>
        private static void PaintTexts(DependencyObject node, Brush strong, Brush weak)
        {
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(node, i);
                var tb = c as TextBlock;
                if (tb != null)
                {
                    // 원래 흐릿하던 글자는 흐릿한 쪽으로 - 강약을 뒤집지 않는다
                    bool faint = ReferenceEquals(tb.Foreground, Palette.TextFaint)
                              || ReferenceEquals(tb.Foreground, Palette.TextDim)
                              || ReferenceEquals(tb.Foreground, Palette.TextGhost);
                    tb.Foreground = faint ? weak : strong;
                }
                PaintTexts(c, strong, weak);
            }
        }

        private PanelWindow PanelOf(string key)
        {
            if (key == "날씨") return _panelWeather;
            if (key == "즐겨찾기") return _panelApps;
            if (key == "시계") return _panelClock;
            return null;
        }

        /// <summary>
        /// 바 끝에 두는 버튼 둘.
        ///   −  창으로 되돌린다
        ///   ×  아예 닫는다 (우클릭 메뉴로 다시 연다)
        /// 배경이 없는 바는 어디를 잡아야 할지 눈에 안 보여서, 손잡이를 이렇게 내어 둔다.
        /// </summary>
        private UIElement BarPopOut(PanelWindow w, bool vertical)
        {
            var me = w;

            // 붙어 있는 동안에도 설정에 닿아야 한다. 창으로 되돌린 뒤에야 열 수 있으면
            // '바 배경 없애기' 처럼 바에만 쓰는 항목을 고르러 매번 뗐다 붙여야 한다.
            var menu = MakeIconButton("≡", "설정", false, delegate
            {
                if (me == null || me.ContextMenu == null) return;
                me.ContextMenu.PlacementTarget = me;
                me.ContextMenu.IsOpen = true;
            });

            var back = MakeIconButton("−", "창으로 되돌리기", false, delegate
            {
                if (me != null) me.Undock();
            });
            back.Margin = vertical ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, 0, 0);

            var shut = MakeIconButton("×", "닫기 (우클릭 메뉴로 다시 열기)", false, delegate
            {
                if (me != null) CloseSection(me.Key);
            });
            shut.Margin = vertical ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, 0, 0);

            var row = new StackPanel
            {
                Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            // ★ 바탕화면 위에 그냥 놓으면 안 보인다 ★
            //   투명 바에서는 회색 글리프가 배경에 묻힌다. 잔디밭 위에서는 특히.
            //   옅은 알약을 깔고 글자를 희게 해서, 어떤 바탕화면에서도 눈에 띄고
            //   '여기를 잡으면 된다' 는 것이 보이게 한다.
            menu.Foreground = Brushes.White;
            back.Foreground = Brushes.White;
            shut.Foreground = Brushes.White;

            var chip = new SolidColorBrush(Color.FromArgb(0x77, 0x00, 0x00, 0x00));
            chip.Freeze();

            row.Children.Add(menu);
            row.Children.Add(back);
            row.Children.Add(shut);

            var pill = new Border
            {
                Child = row,
                Background = chip,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 4, 10, 4),
                // 자리는 담는 쪽(DockPanel)이 잡는다. 여기서는 내용과 떨어질 만큼만 띄운다.
                Margin = vertical ? new Thickness(0, 10, 0, 10) : new Thickness(12, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = PillRest,
            };

            // ★ 평소에는 옅게 물러나 있는다 ★
            //   늘 또렷하면 바탕화면 위에 웬 검은 알약이 하나 떠 있는 꼴이다.
            //   손이 오면 그때 제 색을 낸다.
            pill.MouseEnter += delegate { Ease(pill, UIElement.OpacityProperty, 1); };
            pill.MouseLeave += delegate { Ease(pill, UIElement.OpacityProperty, PillRest); };
            return pill;
        }

        /// <summary>손 대기 전 알약의 진하기.</summary>
        private const double PillRest = 0.3;

        private TextBlock _panelBarClock;

        private static TextBlock BarLabel(string t)
        {
            return new TextBlock
            {
                Text = t,
                FontSize = 11,
                Foreground = Palette.TextGhost,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        /// <summary>
        /// 조각 바에 실을 시계 글자.
        ///
        /// ★ 문화권을 반드시 넘긴다 ★
        ///   형식 문자열의 '/' 는 리터럴이 아니라 '현재 문화권의 날짜 구분자' 로 바뀐다.
        ///   한국 로캘은 '-' 라서 그냥 두면 본 바는 9/1, 여기는 9-1 로 갈린다.
        ///   요일도 ddd 를 쓰면 문화권을 타므로 DayNames 를 쓴다 (다른 시계와 같은 방식).
        /// </summary>
        private string ClockBarText(bool vertical)
        {
            var now = DateTime.Now;
            string date = string.Format(CultureInfo.InvariantCulture, "{0}/{1}", now.Month, now.Day);

            if (vertical)
                return now.ToString("HH:mm", CultureInfo.InvariantCulture) + "\n" + date;

            return string.Format(CultureInfo.InvariantCulture, "{0} ({1})  {2}",
                                 date, DayNames[(int)now.DayOfWeek],
                                 now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        }

        /// <summary>붙어 있는 조각 창들의 바를 다시 그린다. 값이 바뀌면 부른다.</summary>
        private void RefreshPanelBars()
        {
            foreach (var w in EachPanel())
            {
                if (w == null || w.Edge == DockEdge.None) continue;
                if (w.Key == "시계") continue;      // 시계는 글자만 갈아끼운다
                try { w.RebuildBar(); } catch { }
            }
        }

        /// <summary>조각 창 배율. 따로 정해둔 것이 없으면 카드 배율을 따른다.</summary>
        private double PanelScaleOf(double saved)
        {
            if (double.IsNaN(saved)) return _cfg.Scale;
            return saved;
        }

        private double SavedScaleOf(string key)
        {
            if (key == "날씨") return _cfg.WeatherScale;
            if (key == "즐겨찾기") return _cfg.AppsScale;
            if (key == "시계") return _cfg.ClockScale;
            return double.NaN;
        }

        private IEnumerable<PanelWindow> EachPanel()
        {
            yield return _panelWeather;
            yield return _panelApps;
            yield return _panelClock;
        }

        private void ClosePanels()
        {
            foreach (var w in EachPanel())
            {
                if (w == null) continue;
                try { w.Undock(); } catch { }   // 확보한 화면 공간을 돌려준다
                try { w.Close(); } catch { }
            }
            _panelWeather = null; _panelApps = null; _panelClock = null;
        }

        /// <summary>본 창에 닿아 있는 조각 창들과 그 자리.</summary>
        private List<KeyValuePair<PanelWindow, Point>> CarryPanels()
        {
            var list = new List<KeyValuePair<PanelWindow, Point>>();
            if (!_cfg.Separated) return list;
            try
            {
                foreach (var w in PanelWindow.ConnectedWith(this))
                {
                    var p = w as PanelWindow;
                    if (p != null) list.Add(new KeyValuePair<PanelWindow, Point>(p, new Point(p.Left, p.Top)));
                }
            }
            catch { }
            return list;
        }

        private void MovePanels(List<KeyValuePair<PanelWindow, Point>> carry, double dx, double dy)
        {
            if (carry == null || carry.Count == 0) return;
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) return;

            foreach (var kv in carry)
            {
                kv.Key.Left = kv.Value.X + dx;
                kv.Key.Top = kv.Value.Y + dy;
            }
            SavePanelPlaces();
        }

        /// <summary>끌어 놓은 조각 창이 빈 구역에 떨어졌으면 카드 아래로 되돌린다.</summary>
        private void RescuePanelsIfLost()
        {
            double y = Top + (ActualHeight > 0 ? ActualHeight : 200) + 6;
            bool moved = false;
            foreach (var w in EachPanel())
            {
                if (w == null || w.Edge != DockEdge.None) continue;
                if (!OffScreen(w)) continue;
                y = PlaceUnder(w, y);
                moved = true;
            }
            if (moved) SavePanelPlaces();
        }

        /// <summary>조각 창들의 지금 자리를 설정에 적어 둔다.</summary>
        private void SavePanelPlaces()
        {
            if (_panelWeather != null) { _cfg.WeatherX = _panelWeather.Left; _cfg.WeatherY = _panelWeather.Top; }
            if (_panelApps != null) { _cfg.AppsX = _panelApps.Left; _cfg.AppsY = _panelApps.Top; }
            if (_panelClock != null) { _cfg.ClockX = _panelClock.Left; _cfg.ClockY = _panelClock.Top; }
            SaveSoon();
        }

        private void ApplyClockVisibility()
        {
            var v = SectionOn(_cfg.ShowClock) ? Visibility.Visible : Visibility.Collapsed;
            if (_clockRow != null) _clockRow.Visibility = v;
            // 위에 아무것도 없으면 구분선도 필요 없다
            bool anythingAbove = _cfg.ShowQuotes || _cfg.ShowWeather;
            if (_clockDivider != null)
                _clockDivider.Visibility = (v == Visibility.Visible && anythingAbove)
                                         ? Visibility.Visible : Visibility.Collapsed;
            UpdateClockTimer();
        }

        /// <summary>실제로 화면에 보여줄 항목 수. 설정이 0이거나 전체보다 크면 전부 보여준다.</summary>
        private int VisibleLimit
        {
            get
            {
                int n = _cfg.Symbols.Count;
                if (_cfg.ListLimit <= 0 || _cfg.ListLimit > n) return n;
                return _cfg.ListLimit;
            }
        }

        // 손잡이를 끄는 동안 '다음에 접힐 항목'을 눌러 보여주기 위한 상태
        private readonly List<QuoteView> _squeezed = new List<QuoteView>();

        /// <summary>다음 칸에 접힐 항목을 진행도만큼 납작하게 눌러 저항하는 느낌을 준다.</summary>
        private void ApplySqueeze(double frac)
        {
            ClearSqueeze();
            if (frac >= -0.02) return;   // 접히는 방향으로 끌 때만

            double t = (-frac) / 0.5;
            if (t > 1) t = 1;

            var views = _cfg.GridView ? _tiles : _rows;
            int limit = VisibleLimit;
            int n = StepCount();

            for (int k = 0; k < n; k++)
            {
                int idx = limit - 1 - k;
                if (idx < 0 || idx >= views.Count) continue;
                var v = views[idx];
                if (v.Root.Visibility != Visibility.Visible) continue;

                v.Scale.ScaleY = 1.0 - 0.09 * t;   // 살짝만 눌린다 (떨림은 넣지 않는다)
                _squeezed.Add(v);
            }
        }

        private void ClearSqueeze()
        {
            for (int i = 0; i < _squeezed.Count; i++)
            {
                _squeezed[i].Scale.ScaleY = 1;
                _squeezed[i].Translate.X = 0;
            }
            _squeezed.Clear();
        }

        private int _scrollOffset;   // 접어둔 목록에서 몇 번째부터 보여줄지 (휠로 움직인다)

        private void ApplyVisibleLimit()
        {
            ClearSqueeze();

            int limit = VisibleLimit;
            int total = _cfg.Symbols.Count;

            int maxOffset = total - limit;
            if (maxOffset < 0) maxOffset = 0;
            if (_scrollOffset > maxOffset) _scrollOffset = maxOffset;
            if (_scrollOffset < 0) _scrollOffset = 0;

            int from = _scrollOffset, to = _scrollOffset + limit;
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].Root.Visibility = (i >= from && i < to) ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < _tiles.Count; i++)
                _tiles[i].Root.Visibility = (i >= from && i < to) ? Visibility.Visible : Visibility.Collapsed;

            int hidden = total - limit;
            if (_limitHint != null)
            {
                if (hidden > 0)
                {
                    // 위아래로 몇 개가 숨어 있는지 알려준다 (휠로 넘길 수 있다)
                    int above = _scrollOffset;
                    int below = total - to;
                    string s;
                    if (above > 0 && below > 0) s = "▲ " + above + "   ·   ▼ " + below;
                    else if (above > 0) s = "▲ " + above + "개 더";
                    else s = "▼ " + below + "개 더";
                    SetText(_limitHint, s);
                    if (_limitHintBox != null) _limitHintBox.Visibility = Visibility.Visible;
                }
                else if (_limitHintBox != null) _limitHintBox.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>접혀 있을 때 휠로 목록을 넘긴다.</summary>
        private void OnListWheel(object sender, MouseWheelEventArgs e)
        {
            int total = _cfg.Symbols.Count;
            if (VisibleLimit >= total) return;   // 다 보이면 넘길 것이 없다

            e.Handled = true;
            int step = _cfg.GridView ? _cfg.GridColumns : 1;
            _scrollOffset += (e.Delta > 0) ? -step : step;
            ApplyVisibleLimit();
        }

        private void SetListLimit(int n)
        {
            int max = _cfg.Symbols.Count;
            if (max <= 0) return;

            // 타일은 한 줄(가로 개수) 단위로 접힌다. 마지막 줄이 덜 차면 그대로 둔다.
            if (_cfg.GridView && n < max)
            {
                int c = _cfg.GridColumns;
                n = ((n + c - 1) / c) * c;
                if (n > max) n = max;
            }

            int min = _cfg.GridView ? Math.Min(_cfg.GridColumns, max) : 1;
            if (n < min) n = min;
            if (n > max) n = max;
            if (n == VisibleLimit) return;

            _cfg.ListLimit = (n >= max) ? 0 : n;   // 전부 보이면 '제한 없음' 으로 저장한다
            ApplyVisibleLimit();
        }

        /// <summary>손잡이를 한 칸 끌 때 늘고 주는 개수. 타일은 한 줄에 가로 개수만큼이다.</summary>
        private int StepCount() { return _cfg.GridView ? _cfg.GridColumns : 1; }

        /// <summary>카드 아래 가장자리. 위아래로 끌면 보이는 개수가 줄고 는다.</summary>
        private UIElement BuildBottomGrip()
        {
            var bar = new Rectangle
            {
                Width = 26,
                Height = 2.5,
                RadiusX = 1.25,
                RadiusY = 1.25,
                Fill = Palette.GripDot,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var grip = new Border
            {
                Height = 9,
                Background = Palette.Clear,
                Cursor = Cursors.SizeNS,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -11),
                Child = bar,
                ToolTip = "위아래로 끌어서 보이는 개수 조절",
            };

            AttachLimitDrag(grip, delegate(bool on)
            {
                bar.Fill = on ? Palette.IconHover : Palette.GripDot;
            });

            return grip;
        }

        /// <summary>
        /// 위아래로 끌어 '보이는 개수'를 조절하는 동작을 붙인다.
        /// 카드 아래 가장자리 손잡이와 "n개 더" 줄이 같은 동작을 쓴다.
        /// setActive 는 잡거나 올렸을 때의 겉모습만 바꾼다.
        /// </summary>
        private void AttachLimitDrag(FrameworkElement handle, Action<bool> setActive)
        {
            bool dragging = false;
            double startY = 0;
            int startCount = 0;

            handle.MouseEnter += (s, e) => setActive(true);
            handle.MouseLeave += (s, e) => { if (!dragging) setActive(false); };

            handle.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                dragging = true;
                startY = PointToScreen(e.GetPosition(this)).Y;
                startCount = VisibleLimit;
                handle.CaptureMouse();
            };
            handle.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                // 손잡이 자신이 목록과 함께 움직이므로, 좌표는 창 기준으로만 잰다
                double sxl, syl;
                Dock.GetDpiScale(this, out sxl, out syl);
                // PointToScreen 은 물리 픽셀, StepHeight 는 DIP 다
                double dy = (PointToScreen(e.GetPosition(this)).Y - startY) / syl;
                double stepH = StepHeight();
                int steps = (int)Math.Round(dy / stepH);
                SetListLimit(startCount + steps * StepCount());

                // 접는 방향(위로 끌 때)에만 저항감을 준다.
                // 펼 때는 반올림 경계에서 진행도가 잠깐 음수가 되므로 방향으로 막아야 한다.
                ApplySqueeze(dy < 0 ? (dy / stepH - steps) : 0);
            };
            handle.MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                handle.ReleaseMouseCapture();
                setActive(false);
                ClearSqueeze();
                _cfg.Save();
            };
            handle.LostMouseCapture += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                setActive(false);
                ClearSqueeze();
                _cfg.Save();
            };
        }

        /// <summary>손잡이를 한 칸 끌 때의 세로 이동량(화면 기준). 타일이면 한 줄 높이다.</summary>
        private double StepHeight()
        {
            double h;
            if (_cfg.GridView) h = TileSize + 6;   // 타일 한 줄
            else h = (_rows.Count > 0 && _rows[0].Root.ActualHeight > 0) ? _rows[0].Root.ActualHeight : 21;
            h *= _cfg.Scale;
            return h < 6 ? 6 : h;
        }

        // 타일 한 칸의 가로 폭. 카드 안쪽 폭을 열 수로 나눈 값이 이만큼 되도록 카드 폭을 정한다.
        private const double TileWidth = 127;
        private const double CardPadX = 16;

        /// <summary>지금 상태에서의 카드 폭. 타일 보기일 때만 열 수에 따라 넓어진다.</summary>
        private double CurrentCardWidth
        {
            get
            {
                if (_cfg.Minimized) return double.NaN;
                if (_cfg.Expanded && _cfg.GridView) return CardPadX * 2 + _cfg.GridColumns * TileWidth;
                return CardWidth;
            }
        }


        // ---------- 모니터 가장자리 도킹 ----------

        /// <summary>드래그가 끝난 자리가 모니터 가장자리 근처면 거기에 붙인다.</summary>
        private bool TryDockAfterDrag()
        {
            if (Docked) return false;
            if (ActualWidth <= 0 || ActualHeight <= 0) return false;

            double sx, sy;
            Dock.GetDpiScale(this, out sx, out sy);

            var all = Dock.AllScreens();

            // 판정 기준은 창 가장자리가 아니라 '커서가 화면 끝에 닿았는가' 다.
            //
            // 창 가장자리를 보면 카드를 어디를 잡았느냐에 따라 결과가 갈린다.
            // 마우스는 화면 밖으로 못 나가므로, 카드 왼쪽을 잡고 오른쪽으로 밀면
            // 창의 오른쪽 끝은 화면 밖으로 한참 나가버려 영영 걸리지 않는다.
            // (실제로 "왼쪽에는 붙는데 오른쪽에는 안 붙는다" 로 드러났다)
            // 커서를 기준으로 하면 네 방향이 똑같이 걸리고, 무엇보다
            // 사용자가 기대하는 동작(화면 끝까지 밀기)과 일치한다.
            var cur = CursorOnScreen();
            var scr = Dock.ScreenAt(all, cur);

            // 후하게 잡는다. 14px 은 4K 200% 화면에서 눈으로 7px 밖에 안 돼 너무 빡빡했다.
            // 커서 기준이 우선이고, 창 자체가 가장자리에 걸쳐 있어도 받아준다.
            double snapCur = 56 * Math.Max(sx, sy);
            double snapWin = 28 * Math.Max(sx, sy);

            double wl = Left * sx, wt = Top * sy;
            double wr = wl + ActualWidth * sx, wb = wt + ActualHeight * sy;

            var edge = DockEdge.None;
            double best = double.MaxValue;

            double d;
            d = Math.Min(cur.X - scr.Bounds.Left,   snapCur + 1);
            if (cur.X - scr.Bounds.Left   < snapCur && d < best) { best = d; edge = DockEdge.Left; }
            d = Math.Min(cur.Y - scr.Bounds.Top,    snapCur + 1);
            if (cur.Y - scr.Bounds.Top    < snapCur && d < best) { best = d; edge = DockEdge.Top; }
            d = Math.Min(scr.Bounds.Right - cur.X,  snapCur + 1);
            if (scr.Bounds.Right - cur.X  < snapCur && d < best) { best = d; edge = DockEdge.Right; }
            d = Math.Min(scr.Bounds.Bottom - cur.Y, snapCur + 1);
            if (scr.Bounds.Bottom - cur.Y < snapCur && d < best) { best = d; edge = DockEdge.Bottom; }

            if (edge == DockEdge.None)
            {
                // 커서가 못 닿았어도 창을 가장자리에 붙여 놓았으면 받아준다
                double e;
                e = Math.Abs(wl - scr.Bounds.Left);   if (e < snapWin && e < best) { best = e; edge = DockEdge.Left; }
                e = Math.Abs(wt - scr.Bounds.Top);    if (e < snapWin && e < best) { best = e; edge = DockEdge.Top; }
                e = Math.Abs(scr.Bounds.Right - wr);  if (e < snapWin && e < best) { best = e; edge = DockEdge.Right; }
                e = Math.Abs(scr.Bounds.Bottom - wb); if (e < snapWin && e < best) { best = e; edge = DockEdge.Bottom; }
            }

            if (edge == DockEdge.None) return false;

            // 듀얼 모니터의 연결지점에는 붙이지 않는다.
            // 거기는 화면 끝이 아니라 옆 모니터로 넘어가는 통로다.
            if (Dock.EdgeShared(all, scr, edge)) return false;

            ApplyDock(edge, true);
            return true;
        }

        /// <summary>가장자리에 붙인다. rememberPlace 면 지금 자리를 '돌아갈 곳'으로 남긴다.</summary>
        private void ApplyDock(DockEdge edge, bool rememberPlace)
        {
            if (edge == DockEdge.None) { Undock(); return; }
            DockEdge prevEdge = _cfg.DockedEdge;   // 변을 갈아탈 때 옛 변도 다시 쌓아야 한다

            // 아직 카드 자리에 있을 때 재야 한다. 조금 뒤면 창이 바 자리로 옮겨간다.
            if (!Docked)
            {
                RememberPanelOffsets();
                // ★ 지난번에 붙었던 모니터 기억을 지운다 ★
                //   안 지우면 어느 모니터로 끌고 가든 늘 그 모니터로 돌아간다.
                //   (붙어 있는 동안에만 모니터를 고정하는 것이 목적이다)
                _dockScreen = null;
            }

            if (rememberPlace)
            {
                _cfg.UndockX = Left;
                _cfg.UndockY = Top;
            }
            _cfg.DockedEdge = edge;

            if (_dockBar == null) BuildDockBar();

            SizeToContent = SizeToContent.Manual;
            Content = _dockBar;
            Topmost = true;                       // 붙은 바는 늘 위에 있어야 의미가 있다
            ExitEditMode();

            // 좌·우는 글자를 돌리지 않는다. 폭을 3배로 잡고 위에서 아래로 쌓는다.
            bool vertical = (edge == DockEdge.Left || edge == DockEdge.Right);
            _dockContent.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            _dockItems.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            _dockContent.HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            _dockContent.VerticalAlignment = vertical ? VerticalAlignment.Top : VerticalAlignment.Center;
            _dockContent.Margin = vertical ? new Thickness(0, 9, 0, 0) : new Thickness(14, 0, 0, 0);
            // 흐르는 값이 시계에 바짝 붙지 않게 왼쪽을 20px 비워 둔다
            _dockClock.Margin = vertical ? new Thickness(0, 2, 0, 0) : new Thickness(20, 0, 14, 0);
            _dockClock.TextAlignment = vertical ? TextAlignment.Center : TextAlignment.Left;

            // 손잡이는 늘 '화면 안쪽' 가장자리에 둔다
            if (_dockGrip != null)
            {
                const double Grab = 6;
                _dockGrip.Width = vertical ? Grab : double.NaN;
                _dockGrip.Height = vertical ? double.NaN : Grab;
                _dockGrip.HorizontalAlignment = vertical
                    ? (edge == DockEdge.Left ? HorizontalAlignment.Right : HorizontalAlignment.Left)
                    : HorizontalAlignment.Stretch;
                _dockGrip.VerticalAlignment = vertical
                    ? VerticalAlignment.Stretch
                    : (edge == DockEdge.Top ? VerticalAlignment.Bottom : VerticalAlignment.Top);
                _dockGrip.Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS;
                _dockGrip.ToolTip = "끌어서 두께 조절";
            }

            // ★ 자리는 작업영역에서 뽑지 않는다 ★
            //   작업영역의 그 변은 '등록된 바들의 띠 안쪽 경계 중 최솟값' 이라, 바깥 바가
            //   빠지면 아무도 안 앉은 띠까지 포함한 값을 준다(실측). 그 값으로 앉히면
            //   화면 끝에 그만큼 구멍이 남는다. DockStack 주석 참고.
            _dockSig = "";              // 방향이 바뀌었을 수 있으니 다시 만들게 한다

            string prevDev = _dockScreen != null ? _dockScreen.Device : null;
            if (DockTargetScreen() == null) return;

            // ★ 셸이 우리 창을 밀어낼 수 있다 ★ (DockStack._placed 주석 참고)
            //   WM_MOVE 로만 알 수 있으므로 여기서 듣는다. 우리가 앉히는 동안 온 것은
            //   DockStack.Busy 로 걸러진다.
            if (!_moveWatch)
            {
                _moveWatch = true;
                LocationChanged += delegate { if (Docked) RelayoutSoon(); };
            }

            DockStack.Add(this);
            DockStack.Apply(_dockScreen.Device, edge, true);   // 붙을 때는 남의 몫을 새로 잰다
            if (prevDev != null && prevDev != _dockScreen.Device)
                DockStack.Apply(prevDev, prevEdge, true);      // 떠난 모니터에 구멍을 남기지 않는다

            UpdateClockTimer();
            RefreshDockBar();
            RequestRefresh();
            _cfg.Save();
        }

        /// <summary>셸에 자리를 물어 확보하고, 창을 그 자리에 맞춘다.</summary>
        /// <summary>
        /// 붙을 모니터를 고른다. ★ 작업영역은 돌려주지 않는다 ★
        ///   작업영역을 여기서 돌려주던 것이 예전 구조의 출발점이었다. 그것으로 띠를 앉히면
        ///   같은 변에 둘이 붙었다가 하나가 빠질 때 화면 끝에 그 두께만큼 구멍이 남는다.
        /// </summary>
        private ScreenInfo DockTargetScreen()
        {
            double sx, sy;
            Dock.GetDpiScale(this, out sx, out sy);

            double w = ActualWidth > 0 ? ActualWidth : 200;
            double h = ActualHeight > 0 ? ActualHeight : 200;
            var all = Dock.AllScreens();
            if (all == null || all.Count == 0) return null;

            // 이미 붙어 있으면 그때 정한 모니터를 계속 쓴다 (Dock.ScreenByDevice 주석 참고)
            ScreenInfo scr = null;
            if (Docked && _dockScreen != null)
                scr = Dock.ScreenByDevice(all, _dockScreen.Device);
            if (scr == null)
                scr = Dock.ScreenAt(all, new Point((Left + w / 2) * sx, (Top + h / 2) * sy));
            _dockScreen = scr;
            return scr;
        }

        // ---------- IDockBar : DockStack 이 이 창을 다루는 창구 ----------

        Window IDockBar.BarWindow { get { return this; } }
        AppBar IDockBar.BarAppBar { get { return _appBar; } }
        uint IDockBar.BarCallbackMsg { get { return (uint)AppBarCallbackMsg; } }
        DockEdge IDockBar.BarEdge { get { return _cfg.DockedEdge; } }

        string IDockBar.BarDevice
        {
            get { return (Docked && _dockScreen != null) ? _dockScreen.Device : null; }
        }

        /// <summary>
        /// 본 창이 늘 화면 끝에 가장 가깝다.
        /// 붙은 순서로 매기면 다시 켤 때마다 앞뒤가 달라져 배치가 안 지켜진다.
        /// </summary>
        int IDockBar.BarOrder { get { return 0; } }

        int IDockBar.BarThicknessPx
        {
            get
            {
                double sx, sy;
                Dock.GetDpiScale(this, out sx, out sy);
                return DockThicknessPx(sx, sy);
            }
        }

        bool IDockBar.BarActive { get { return true; } }   // 본 창은 붙어 있으면 늘 산다
        bool IDockBar.BarCentered { get { return false; } }
        int IDockBar.BarOverhangPx { get { return 0; } }

        /// <summary>시세 바는 흐르는 값이 길어 한 줄을 통째로 쓴다.</summary>
        // 같은 변에 붙은 것은 시세까지 한 줄로 모은다. 줄을 나누면 화면을 두 번 뺏는다.
        bool IDockBar.BarOwnRow { get { return false; } }
        int IDockBar.BarLengthPx { get { return 0; } }

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
                Topmost = !full;
                if (_appBar.Registered) { _appBar.SetZOrder(!full); return; }
                // 대표가 아닌 바는 셸에 등록돼 있지 않아 SetZOrder 가 통하지 않는다.
                IntPtr h = new WindowInteropHelper(this).Handle;
                if (h != IntPtr.Zero)
                    Dock.SetWindowPos(h, full ? Dock.HWND_BOTTOM : Dock.HWND_TOPMOST,
                                      0, 0, 0, 0,
                                      Dock.SWP_NOSIZE | Dock.SWP_NOMOVE | Dock.SWP_NOACTIVATE);
            }
            catch { }
        }


        /// <summary>바 두께 (이 프로세스가 보는 픽셀).</summary>
        private int DockThicknessPx(double sx, double sy)
        {
            bool vertical = (_cfg.DockedEdge == DockEdge.Left || _cfg.DockedEdge == DockEdge.Right);
            double axis = vertical ? sx : sy;
            double thick = vertical ? DockThicknessSide : DockThickness;
            int t = (int)Math.Round(thick * _cfg.DockScale * axis);
            return t < 14 ? 14 : t;
        }

        /// <summary>
        /// 자리를 다시 잡는다. 계산도 확보도 DockStack 이 한다.
        ///
        /// 이름을 그대로 둔 이유: 두께 손잡이(AttachDockResize)와 셸 알림이 이것을 부른다.
        /// remeasure 를 false 로 준다 - 두께만 바뀐 것이라 '남이 먹은 몫' 은 그대로다.
        /// 여기서 다시 재면 손잡이를 끌 때마다 ABM_REMOVE/NEW 왕복이 돌아 못 쓰게 된다.
        /// </summary>
        private void PositionDockBar()
        {
            if (!Docked) return;
            if (_dockScreen == null && DockTargetScreen() == null) return;
            DockStack.ApplyFor(this, false);
        }

        /// <summary>
        /// 작업영역을 다시 잰다.
        ///
        /// 다른 바가 붙고 떨어지면 작업영역이 바뀌므로 매번 다시 본다.
        /// 우리 몫은 Dock.WithoutOwnBar 가 되돌려 준다 (셸에 물어보면 갱신 시점을 알 수 없다).
        /// </summary>
        // Redock() / RelayoutBars() 는 지웠다.
        //   Redock 의 DispatcherPriority.Background 한 박자는 정확성에 아무 기여도 없었다 -
        //   SHAppBarMessage 는 동기라 호출이 돌아온 순간 작업영역이 이미 새 값이다(실측).
        //   진짜 문제는 RelayoutBars 가 '나머지 전부' 에게 작업영역을 다시 재게 한 것이다.
        //   그 값은 min 규칙에 오염돼 있어서, 고치는 코드가 아니라 오염을 퍼뜨리는 코드였다.
        //   이제는 DockStack.Apply(모니터, 변) 하나가 그 변 전체를 한 번에 앉힌다.

        /// <summary>
        /// 셸 쪽이 바뀌었다. 한 박자 뒤에 한 번만 다시 앉힌다.
        ///
        /// ★ 플래그는 일을 '마친 뒤' 에 내린다 ★
        ///   일하는 동안 쌓인 알림을 여기서 삼켜야 한다. 시작할 때 내리면 우리가 낸 알림이
        ///   그대로 우리를 다시 깨운다. 그래도 새는 것은 DockStack.OnShellChanged 의
        ///   '실측 여백이 그대로면 아무것도 안 한다' 가 걸러 준다. 두 겹이다.
        /// </summary>
        private void RelayoutSoon()
        {
            if (DockStack.Busy || _relayoutPending) return;
            _relayoutPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
            {
                try { DockStack.OnShellChanged(this); }
                catch { }
                finally { _relayoutPending = false; }
            });
        }

        /// <summary>가장자리에서 떼고 원래 카드로 돌아간다.</summary>
        private void Undock()
        {
            if (!Docked) return;

            // ★ DockedEdge 를 지우기 '전' 에 Leave 를 부른다 ★
            //   Leave 가 내 등록을 내리고, 남은 바들을 화면 끝부터 다시 앉힌다.
            //   여기서 다시 앉히지 않으면 내가 있던 바깥 자리가 그대로 비어 버린다.
            try { DockStack.Leave(this); } catch { }

            _cfg.DockedEdge = DockEdge.None;
            StopMarquee();
            _appBar.Unregister();
            _dockScreen = null;   // 다음에 붙을 때 그때 놓인 모니터를 새로 고른다

            Content = _card;
            SizeToContent = SizeToContent.WidthAndHeight;
            Width = double.NaN;
            Height = double.NaN;
            Topmost = _cfg.Topmost;

            if (!double.IsNaN(_cfg.UndockX) && !double.IsNaN(_cfg.UndockY))
            {
                Left = _cfg.UndockX;
                Top = _cfg.UndockY;
            }
            ClampToScreen();
            SavePlacement();

            // ★ 한 번 더 ★
            //   방금 SizeToContent 를 켠 참이라 이 시점의 ActualHeight 는 아직 '바 높이' 다.
            //   그 값으로 맞추면 카드가 아래로 화면을 넘어간다. 크기가 잡힌 뒤 다시 맞춘다.
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)delegate
                {
                    ClampToScreen();
                    RescueIfLost();
                    SavePlacement();
                });
            }
            catch { }

            RestorePanelPlaces();
            ApplyMinimized();
            UpdateClockTimer();
            RefreshAll();
            _cfg.Save();
        }

        /// <summary>셸이 보내는 AppBar 알림. 자리가 밀리면 다시 잡는다.</summary>
        private IntPtr AppBarHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == AppBarCallbackMsg && Docked)
            {
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
                    // 전체화면 앱이 뜨면 뒤로 빠진다. 안 그러면 게임 위에 계속 떠 있다.
                    // lParam 이 켜져 있으면 '전체화면 시작', 꺼져 있으면 '끝'.
                    // 이 알림은 셸에 등록된 바(그 변의 대표) 에게만 오므로 나머지에도 전한다.
                    DockStack.SetFullScreen(lParam != IntPtr.Zero);
                }
                handled = true;
            }

            // 화면 구성이 바뀌면 재어 둔 '남이 먹은 몫' 은 못 쓴다. 처음부터 다시 잰다.
            //
            // ★ WM_SETTINGCHANGE 는 일부러 안 본다 ★
            //   SPI_SETWORKAREA 브로드캐스트의 발원지가 우리 자신의 ABM_SETPOS 다.
            //   그것까지 받으면 우리가 우리를 계속 깨우는 자기순환이 된다.
            //   작업표시줄이 조용히 바뀌는 경우는 ABN_POSCHANGED 가 대신 잡아 준다.
            const int WM_DISPLAYCHANGE = 0x007E;
            if (msg == WM_DISPLAYCHANGE)
            {
                try { DockStack.Invalidate(); } catch { }   // Invalidate 가 ABM_REMOVE 를 먼저 보낸다
                RelayoutSoon();
            }
            return IntPtr.Zero;
        }

        // ---------- 얇은 바 ----------

        private void BuildDockBar()
        {
            _dockItems = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _dockClock = new TextBlock
            {
                FontSize = DockFont - 1,
                Foreground = Palette.TextDim,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 14, 0),
            };

            _dockContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0),
            };
            // 넘치는 내용은 버리지 않고 흐르게 한다. 잘리는 자리를 따로 둔다.
            _dockScroll = new TranslateTransform(0, 0);
            _dockItems.RenderTransform = _dockScroll;

            // ★ Canvas 를 한 겹 둔다 ★
            //   Border 에 바로 넣으면 Arrange 때 DesiredSize 를 쓰는데 그 값이 클립 폭으로
            //   잘려 있어서, 넘치는 항목이 좁은 폭에 우겨넣어지며 글씨가 끊긴다.
            //   Canvas 는 자식을 무한 공간에서 재고 제 크기대로 배치하므로 그 문제가 없다.
            _dockCanvas = new Canvas();
            _dockCanvas.Children.Add(_dockItems);

            _dockClip = new Border
            {
                Child = _dockCanvas,
                ClipToBounds = true,
                Background = Palette.Clear,   // 히트 테스트(호버로 멈추기)를 받으려면 필요하다
                VerticalAlignment = VerticalAlignment.Center,
            };
            // 마우스를 올리면 멈춘다. 읽는 중에 흘러가면 곤란하다.
            _dockClip.MouseEnter += (s, e) => { if (_dockScrollClock != null) _dockScrollClock.Controller.Pause(); };
            _dockClip.MouseLeave += (s, e) => { if (!_scrubbing && _dockScrollClock != null) _dockScrollClock.Controller.Resume(); };

            // 잡아서 좌우로 끌면 원하는 자리로 돌려볼 수 있다.
            // 항목 더블클릭은 자식이 먼저 처리하므로 여기까지 오지 않는다.
            // ★ 누르는 순간에는 무엇을 할지 정하지 않는다 ★
            //   가로로 끌면 마퀴 되돌리기, 세로로 끌면 바 떼어내기다.
            //   누르자마자 마퀴로 정해 버리면 상하로 끌어 떼는 손짓이 통째로 막힌다.
            _dockClip.MouseLeftButtonDown += (s, e) =>
            {
                if (_marqueeLoop <= 0) return;   // 흐를 것이 없으면 바 끌기로 넘긴다
                if (e.ClickCount == 2) return;   // 더블클릭은 바가 받는다 (떼어내기)
                e.Handled = true;

                _clipPending = true;
                _scrubbing = false;
                _clipStart = CursorOnScreen();
                _scrubStartCursor = _clipStart.X;
                _scrubStartX = _dockScroll.X;
                _dockClip.CaptureMouse();
            };
            _dockClip.MouseMove += (s, e) =>
            {
                if (!_clipPending && !_scrubbing) return;

                double sxq, syq;
                Dock.GetDpiScale(this, out sxq, out syq);
                var now = CursorOnScreen();

                if (_clipPending)
                {
                    double dx = (now.X - _clipStart.X) / sxq;
                    double dy = (now.Y - _clipStart.Y) / syq;
                    const double Decide = 5;   // 이만큼 움직여야 방향을 판정한다 (DIP)
                    if (Math.Abs(dx) < Decide && Math.Abs(dy) < Decide) return;

                    _clipPending = false;

                    if (Math.Abs(dy) > Math.Abs(dx))
                    {
                        // 세로로 끌었다 - 떼어내는 손짓이다. 클립은 손을 뗀다.
                        _dockClip.ReleaseMouseCapture();
                        BeginUndockWatch(_clipStart);
                        return;
                    }

                    // 가로로 끌었다 - 마퀴를 되돌린다.
                    // 애니메이션이 값을 쥐고 있으면 직접 대입이 무시된다. 먼저 떼어낸다.
                    _scrubbing = true;
                    _dockScroll.ApplyAnimationClock(TranslateTransform.XProperty, null);
                    _dockScrollClock = null;
                    _dockScroll.X = _scrubStartX;
                }

                double x = _scrubStartX + (now.X - _scrubStartCursor) / sxq;
                // 한 바퀴 범위 안으로 감아 준다. 두 벌을 이어 붙여 놨으므로 티가 안 난다.
                while (x <= -_marqueeLoop) x += _marqueeLoop;
                while (x > 0) x -= _marqueeLoop;
                _dockScroll.X = x;
            };
            _dockClip.MouseLeftButtonUp += (s, e) =>
            {
                if (_clipPending)
                {
                    _clipPending = false;
                    _dockClip.ReleaseMouseCapture();
                    return;
                }
                if (!_scrubbing) return;
                _scrubbing = false;
                _dockClip.ReleaseMouseCapture();
                ResumeMarqueeFrom(_dockScroll.X);
            };
            _dockClip.LostMouseCapture += (s, e) =>
            {
                if (!_scrubbing) return;
                _scrubbing = false;
                ResumeMarqueeFrom(_dockScroll.X);
            };

            // 즐겨찾기는 흐르는 자리 밖에 둔다. 값은 흘러가도 되지만 이건 눌러야 하는 것이다.
            _dockApps = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            _dockContent.Children.Add(_dockClip);
            _dockContent.Children.Add(_dockApps);
            _dockContent.Children.Add(_dockClock);

            var dockHost = new Grid();
            dockHost.Children.Add(_dockContent);

            // 안쪽 가장자리를 잡아당겨 두께(=배율)를 바꾼다. 글자도 같이 커진다.
            _dockGrip = new Border
            {
                Background = Palette.Clear,   // 히트 테스트를 받으려면 필요하다
                Opacity = 1,
            };
            AttachDockResize(_dockGrip);
            dockHost.Children.Add(_dockGrip);

            _dockBar = new Border
            {
                Background = Palette.Card,
                Child = dockHost,
                ClipToBounds = true,
                ToolTip = "안쪽으로 끌면 떨어집니다 · 더블클릭해도 됩니다",
            };

            // 안쪽으로 충분히 끌면 떼어낸다. 더블클릭은 확실한 탈출구로 남겨둔다.
            _dockBar.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (e.ClickCount == 2) { Undock(); return; }
                BeginUndockWatch(CursorOnScreen());
            };
            _dockBar.MouseMove += (s, e) => { StepUndockWatch(); };
            _dockBar.MouseLeftButtonUp += (s, e) => { CancelUndockWatch(); };
        }

        // ---------- 바를 안쪽으로 끌어 떼어내기 ----------
        //
        // 흐르는 자리(클립)에서 세로로 끌기 시작해도 여기로 넘어온다.
        // 그래서 감시 상태를 지역 변수가 아니라 필드로 둔다.

        private bool _undockWatch;
        private Point _undockStart;   // 물리 픽셀

        private void BeginUndockWatch(Point startPhys)
        {
            _undockWatch = true;
            _undockStart = startPhys;
            if (_dockBar != null) _dockBar.CaptureMouse();
        }

        private void CancelUndockWatch()
        {
            if (!_undockWatch) return;
            _undockWatch = false;
            if (_dockBar != null) _dockBar.ReleaseMouseCapture();
        }

        /// <summary>
        /// 화면 안쪽으로 충분히 끌었으면 떼어낸다.
        /// 방향을 안 보면 바 위에서 조금만 움직여도 떨어져 나가 성가시다.
        /// </summary>
        private void StepUndockWatch()
        {
            if (!_undockWatch || !Docked) return;

            var now = CursorOnScreen();
            double away;
            switch (_cfg.DockedEdge)
            {
                case DockEdge.Left: away = now.X - _undockStart.X; break;
                case DockEdge.Right: away = _undockStart.X - now.X; break;
                case DockEdge.Top: away = now.Y - _undockStart.Y; break;
                default: away = _undockStart.Y - now.Y; break;   // Bottom
            }

            double sxu, syu;
            Dock.GetDpiScale(this, out sxu, out syu);
            if (away < 90 * Math.Max(sxu, syu)) return;   // away 는 물리 픽셀

            _undockWatch = false;
            if (_dockBar != null) _dockBar.ReleaseMouseCapture();
            Undock();

            // 떼자마자 이어서 끌게 된다. 조각 창들도 같이 딸려와야 덩어리가 흩어지지 않는다.
            var carry = CarryPanels();
            double bx = Left, by = Top;
            try { DragMove(); } catch { }
            MovePanels(carry, Left - bx, Top - by);
            SavePlacement();
        }

        /// <summary>
        /// 바 안쪽 가장자리를 끌어 두께를 바꾼다.
        /// 두께는 배율에서 나오고 글자 크기도 배율을 따르므로, 늘리면 글자도 같이 커진다.
        /// </summary>
        private void AttachDockResize(Border grip)
        {
            bool dragging = false;
            double startPos = 0;
            double startScale = 1;

            grip.MouseEnter += (s, e) => grip.Background = Palette.Hover;
            grip.MouseLeave += (s, e) => { if (!dragging) grip.Background = Palette.Clear; };

            grip.MouseLeftButtonDown += (s, e) =>
            {
                if (!Docked) return;
                e.Handled = true;
                dragging = true;
                var c = CursorOnScreen();
                bool vertical = (_cfg.DockedEdge == DockEdge.Left || _cfg.DockedEdge == DockEdge.Right);
                startPos = vertical ? c.X : c.Y;
                startScale = _cfg.DockScale;
                grip.CaptureMouse();
            };
            grip.MouseMove += (s, e) =>
            {
                if (!dragging) return;

                double sx, sy;
                Dock.GetDpiScale(this, out sx, out sy);
                bool vertical = (_cfg.DockedEdge == DockEdge.Left || _cfg.DockedEdge == DockEdge.Right);

                var c = CursorOnScreen();
                double moved = (vertical ? c.X : c.Y) - startPos;
                // 화면 안쪽으로 끌면 두꺼워진다. 오른쪽·아래에 붙었으면 방향이 뒤집힌다.
                if (_cfg.DockedEdge == DockEdge.Right || _cfg.DockedEdge == DockEdge.Bottom) moved = -moved;

                double axis = vertical ? sx : sy;
                double thick = vertical ? DockThicknessSide : DockThickness;
                double newScale = startScale + (moved / axis) / thick;

                if (newScale < Config.MinScale) newScale = Config.MinScale;
                if (newScale > Config.MaxScale) newScale = Config.MaxScale;
                if (Math.Abs(newScale - _cfg.DockScale) < 0.005) return;

                // 카드 배율(_cfg.Scale)은 건드리지 않는다. 바와 카드는 쓰임새가 다르다.
                _cfg.DockScale = newScale;
                PositionDockBar();
                RefreshDockBar();
            };
            grip.MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                grip.ReleaseMouseCapture();
                grip.Background = Palette.Clear;
                SaveSoon();
            };
            grip.LostMouseCapture += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                grip.Background = Palette.Clear;
                SaveSoon();
            };
        }

        /// <summary>바 내용을 다시 채운다. 자리에 들어가는 만큼만 담고 나머지는 생략한다.</summary>
        private void RefreshDockBar()
        {
            if (!Docked || _dockItems == null) return;

            bool vertical = (_cfg.DockedEdge == DockEdge.Left || _cfg.DockedEdge == DockEdge.Right);
            // 담을 수 있는 '길이'. 가로 바는 창 폭, 세로 바는 창 높이다.
            // 붙인 직후에는 ActualWidth/Height 가 아직 갱신 전일 수 있어 정해둔 값을 먼저 본다.
            double span = vertical
                ? (double.IsNaN(Height) ? ActualHeight : Height)
                : (double.IsNaN(Width) ? ActualWidth : Width);

            // 무엇을 담을지가 그대로면 글자만 갈아끼운다.
            // 1초 주기로 돌 때 매번 수십 개 요소를 새로 만드는 것이 가장 큰 낭비였다.
            RefreshDockApps(vertical);

            // 나눠 놓았으면 시계도 제 창을 갖고 있다. 여기 또 실으면 두 번 나온다.
            // (날씨는 AddDockRun/AddDockWeather 에서, 즐겨찾기는 RefreshDockApps 에서 뺀다)
            if (_dockClock != null)
                _dockClock.Visibility = _cfg.Separated ? Visibility.Collapsed : Visibility.Visible;

            // 즐겨찾기 개수와 나누기 여부가 바뀌면 흐를 자리도 달라지므로 구성이 바뀐 것으로 본다
            string sig = DockSignature(vertical, span) + "&" + _cfg.Apps.Count
                       + (_cfg.Separated ? "&S" : "");
            if (sig == _dockSig) { UpdateDockValues(); return; }


            _dockSig = sig;

            // 시계 자리(왼쪽 20px 여백 포함). 시계를 뺐으면 가장자리 여백만 남긴다.
            bool clockOn = (_dockClock == null || _dockClock.Visibility == Visibility.Visible);
            double clockRoom = clockOn ? (vertical ? 64 : 136) : (vertical ? 8 : 24);
            double avail = span - clockRoom - DockAppsRoom(vertical);
            if (avail < 80) avail = 80;

            _dockViews.Clear();
            _dockItems.Children.Clear();
            StopMarquee();

            _dockItems.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            if (!vertical) _dockItems.Width = double.NaN;   // 가로 바는 흘러야 하므로 폭을 풀어 둔다
            // 가로 바는 흐를 자리를 확보해야 하므로 폭을 고정한다.
            // 세로 바는 담긴 만큼만 차지해야 시계가 항목 바로 아래에 붙는다.
            // (높이를 화면 전체로 고정했더니 시계가 바닥까지 밀려 빈 자리가 생겼다)


            if (vertical)
            {
                // ★ 폭을 정해 줘야 글자가 접힌다 ★
                //   _dockItems 는 Canvas 안에 있고, Canvas 는 자식에게 무한 폭을 준다.
                //   그대로 두면 TextWrapping 이 걸려 있어도 절대 안 접히고 바 밖으로 삐져나가
                //   ClipToBounds 에 잘린다 - 실제로 겪은 '텍스트 짤림' 이 그것이다.
                double barW = double.IsNaN(Width) ? ActualWidth : Width;
                if (barW > 12) _dockItems.Width = barW - 6;

                // 세로 바는 자리가 넉넉하다. 들어가는 만큼만 담는다.
                foreach (var def in _cfg.Symbols)
                {
                    Quote q;
                    if (!_quotes.TryGetValue(def.Key, out q) || !q.Ok) continue;

                    var dv = BuildDockQuote(def, q, true);
                    if (!AddDockItem(dv.Item2, avail, true)) break;
                    _dockViews.Add(dv.Item1);
                }
                AddDockWeather(avail, true);
                SizeDockClip(true, avail);
                UpdateDockClock();
                return;
            }

            // 가로 바는 전부 담는다. 넘치면 흐르게 할 것이므로 버리지 않는다.
            AddDockRun();
            double used = MeasureItems();

            if (used > avail)
            {
                // 이어 붙여 한 벌 더 담는다. 끝과 처음이 맞물려야 끊김 없이 돈다.
                _dockItems.Children.Add(new Border { Width = MarqueeGap });
                AddDockRun();
                StartMarquee(used + MarqueeGap);
            }
            SizeDockClip(false, avail);

            UpdateDockClock();
        }

        /// <summary>
        /// 담은 내용을 재서 자리를 잡는다.
        /// Canvas 는 스스로 크기를 갖지 않으므로 여기서 정해줘야 Border 가 접히지 않는다.
        /// </summary>
        private void SizeDockClip(bool vertical, double avail)
        {
            _dockItems.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size nat = _dockItems.DesiredSize;

            _dockCanvas.Width = nat.Width;
            _dockCanvas.Height = nat.Height;

            _dockClip.Height = vertical ? double.NaN : nat.Height;
            // 넘칠 때만 폭을 묶어 둔다. 안 넘치면 내용만큼만 차지해 시계가 바로 붙는다.
            _dockClip.Width = (!vertical && nat.Width > avail) ? avail : double.NaN;
        }

        /// <summary>
        /// 붙은 바에도 즐겨찾기를 싣는다.
        ///
        /// 흐르는 자리(_dockItems)가 아니라 시계 옆 고정 자리에 둔다.
        /// 값은 흘러가도 읽히지만 즐겨찾기는 눌러야 하는 것이라 제자리에 있어야 한다.
        /// </summary>
        private void RefreshDockApps(bool vertical)
        {
            if (_dockApps == null) return;

            // ★ 서명에는 '무엇을 그릴지 정하는 값' 이 빠짐없이 들어가야 한다 ★
            //   Separated 를 빠뜨렸더니 창 나누기를 켜도 서명이 그대로라 다시 만들지 않았고,
            //   나누기 전에 그려둔 아이콘이 바에 남아 즐겨찾기가 두 군데 보였다.
            var sb = new StringBuilder(64);
            sb.Append(vertical ? 'V' : 'H')
              .Append(_cfg.ShowApps ? '1' : '0')
              .Append(_cfg.Separated ? 'S' : '-')
              .Append('@').Append(Math.Round(_cfg.DockScale, 3));
            foreach (var a in _cfg.Apps) sb.Append('|').Append(a.Key);
            foreach (int v in _cfg.AppSeps) sb.Append('/').Append(v);

            string sig = sb.ToString();
            if (sig == _dockAppsSig) return;
            _dockAppsSig = sig;

            _dockApps.Children.Clear();

            // 나눠 놓았으면 즐겨찾기는 제 창을 갖고 있다
            bool on = _cfg.ShowApps && _cfg.Apps.Count > 0 && !_cfg.Separated;
            _dockApps.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;

            // 세로 바에서는 아이콘도 위에서 아래로 쌓는다
            _dockApps.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            _dockApps.Margin = vertical ? new Thickness(0, 7, 0, 0) : new Thickness(2, 0, 8, 0);

            {
                // 본 바에 실릴 때는 시세 글자와 같은 줄을 쓴다. 그 두께 안에 들어가야 안 잘린다.
                //   세로 바 - 폭이 넓으니 절반만 쓴다 (나머지는 글자 몫)
                //   가로 바 - 줄 높이가 곧 한계다. 0 을 넘기면 배율만 보고 커져서 잘렸다.
                double barDip = vertical ? (DockThicknessSide * _cfg.DockScale) * 0.5
                                         : (DockThickness * _cfg.DockScale);
                double sz = DockIconSize(_cfg.DockScale, vertical, barDip);

                var row = new List<HoverItem>();
                for (int i = 0; i < _cfg.Apps.Count; i++)
                {
                    _dockApps.Children.Add(BuildSepSlot(i, vertical, sz));
                    UIElement el = BuildDockApp(_cfg.Apps[i], sz, vertical, i, _cfg.DockedEdge,
                        DockIconPad(barDip));
                    _dockApps.Children.Add(el);
                    AddHover(row, el);
                }
                _dockApps.Children.Add(BuildSepSlot(_cfg.Apps.Count, vertical, sz));
                WireHoverRow(row, vertical, sz);
            }
        }

        /// <summary>
        /// 바에 실은 즐겨찾기 아이콘 크기.
        ///
        /// 글자(DockFont)를 따라가게 했더니 두께를 아무리 늘려도 20에서 멈췄다.
        /// 글자는 너무 커지면 읽기 나쁘라 상한이 있지만 아이콘은 커질수록 좋다.
        /// 그래서 배율을 직접 본다.
        ///
        /// 세로 바는 폭이 DockThicknessSide(60) 이나 되는데 15 만 쓰면 좌우가 휑하다.
        /// 좌우 여백을 줄이고 그만큼 아이콘을 키운다.
        /// </summary>
        /// <summary>바에 실은 아이콘 사방 여백. 이웃끼리는 합쳐서 그 두 배가 뜬다.</summary>
        private const double IconPad = 7;

        /// <summary>
        /// 아이콘·구분선 둘레에 더 두는 틈.
        /// 선이 아이콘에 닿아 보이지 않게 하려는 값이고, 아이콘 사이도 그만큼 더 벌어진다.
        /// 이 둘(IconPad·IconGap)만 만지면 간격이 전부 따라온다.
        /// </summary>
        private const double IconGap = 4;

        /// <summary>구분선 자리(빈칸)의 두께. 호버로 이웃이 밀려와도 눌리게 넉넉히 잡는다.</summary>
        private const double SlotThick = 16;

        /// <summary>구분선 두께. 1px 은 화면에서 너무 가늘었다.</summary>
        private const double SepThick = 3;

        /// <summary>
        /// 아이콘 둘레 여백.
        ///
        /// 고정값(9)으로 뒀더니 얇은 바에서 여백만 위아래 18 이 되어 두께(24)를 넘었다.
        /// 아이콘을 아무리 줄여도 상자가 커서 잘렸다. 여백도 두께를 따라가야 한다.
        /// </summary>
        private static double DockIconPad(double barDip)
        {
            if (barDip <= 0) return IconPad + 2;
            double p = (barDip / MaxGrow) * 0.18;
            if (p < 2) p = 2;
            if (p > IconPad + 2) p = IconPad + 2;
            return p;
        }

        /// <param name="barDip">그 바의 두께(DIP). 0 이면 모른다는 뜻이고 배율만 본다.</param>
        private static double DockIconSize(double scale, bool vertical, double barDip)
        {
            double s0 = scale > 0 ? scale : 1;

            // 바 두께를 알면 가로·세로 가리지 않고 거기서 뽑는다.
            // 가로 바에서만 15*배율 같은 딴 값을 쓰면 두께에 눌려 아이콘이 잘린다.
            if (barDip > 0)
            {
                // ★ 쉴 때 크기는 '커졌을 때가 두께에 딱 맞는' 값이다 ★
                //   커지는 것은 아이콘이 아니라 여백까지 포함한 상자다.
                //   (아이콘 + 여백×2) × 배수 ≤ 두께 라야 안 잘린다.
                double v = barDip / MaxGrow - DockIconPad(barDip) * 2;
                if (v < 8) v = 8;
                if (v > 96) v = 96;
                return v;
            }

            if (vertical)
            {
                double d = 42 * s0;
                if (d < 16) d = 16;
                if (d > 96) d = 96;
                return d;
            }
            double s = 15 * s0;
            if (s < 11) s = 11;
            if (s > 56) s = 56;
            return s;
        }

        // ---------- 호버 확대 (macOS 독처럼) ----------
        //
        // 커지는 것은 RenderTransform 만 쓴다. 레이아웃을 건드리면 줄 전체가 다시 배치되면서
        // 흔들려 보이고, 1초마다 도는 위젯에서 그만큼 일이 늘어난다.
        // 이웃은 '밀리기만' 한다 - 편집 모드의 흔들림(RotateTransform 왕복)과는 다른 것이다.

        private sealed class HoverItem
        {
            public Border Box;
            public ScaleTransform Grow;
            public TranslateTransform Move;
            public RotateTransform Tilt;

            // 끌어서 순서를 바꾸려면 제 이웃을 알아야 한다. 줄이 짜인 뒤에 채워 넣는다.
            public List<HoverItem> Row;
            public int Index;
            public bool Dragged;      // 이번 누르기가 '끌기' 였나 (참이면 뗄 때 열지 않는다)
        }

        private static void AddHover(List<HoverItem> row, UIElement el)
        {
            var b = el as Border;
            if (b == null) return;
            var h = b.Tag as HoverItem;
            if (h != null) row.Add(h);
        }

        // ★ 바 두께는 '가장 크게 커진 순간' 을 품어야 한다 ★
        //   호버보다 꾹 누를 때가 더 커지므로, 자리 계산은 MaxGrow 로 한다.
        //   전에는 호버 값으로만 재어서 누르는 순간 잘렸다.
        private const double HoverGrowBar = 1.38;   // 마우스를 올렸을 때
        private const double HoldGrow = 1.5;        // 꾹 누르는 동안 (여기까지가 한계)
        private const double MaxGrow = HoldGrow;    // 두께를 잡을 때 쓰는 값
        private const double HoverPushMin = 7;      // 계산값이 3px 밖에 안 돼 눈에 안 띄었다
        private const int HoldMs = 480;             // 이만큼 누르고 있으면 구분선이 생긴다

        private static void EaseTo(ScaleTransform t, double to)
        {
            Ease(t, ScaleTransform.ScaleXProperty, to);
            Ease(t, ScaleTransform.ScaleYProperty, to);
        }

        /// <summary>
        /// 그 자리의 구분선을 켜거나 끈다.
        ///
        /// 구분선은 즐겨찾기 항목이 아니라 '자리' 로만 저장한다(Config.AppSeps 주석 참고).
        /// 카드·본 바·조각 바가 같은 자리를 보므로 한 군데서 바꾸면 셋 다 따라온다.
        /// </summary>
        private void ToggleSep(int at)
        {
            if (at < 0 || at > _cfg.Apps.Count) return;

            bool had = _cfg.AppSeps.Remove(at);
            if (!had)
            {
                _cfg.AppSeps.Add(at);
                _cfg.AppSeps.Sort();
            }

            // 새로 넣은 자리는 다시 만들 때 스르르 켜지게 알려 준다.
            // 한 번 쓰고 지운다 - 안 그러면 그다음 갱신마다 계속 다시 켜진다.
            _sepFadeIn = had ? -1 : at;
            RefreshAppBars();
            _sepFadeIn = -1;

            _cfg.Save();
        }
        private const int HoverMs = 130;
        private const int SepMs = 420;     // 구분선이 켜지고 꺼지는 데 걸리는 시간
        private int _sepFadeIn = -1;       // 방금 넣은 구분선 자리. 그 자리만 스르르 켠다
        private const double TiltDeg = 10;      // 이웃이 밀릴 때 기우는 각도
        private const double DragStartPx = 6;   // 이만큼 움직이면 '끌기' 로 본다 (그 전까지는 누르기)

        /// <summary>
        /// 한 줄에 늘어선 아이콘들에 호버 확대를 건다.
        /// 구분선은 목록에 넣지 않는다 - 밀리기만 하면 되는데 그건 자기 자리에서 저절로 된다.
        /// </summary>
        private void WireHoverRow(List<HoverItem> row, bool vertical, double size)
        {
            for (int i = 0; i < row.Count; i++)
            {
                var list = row;
                int me = i;
                row[i].Row = row;
                row[i].Index = me;
                row[i].Box.MouseEnter += delegate { if (_dragFrom < 0) HoverRow(list, me, vertical, size); };
                row[i].Box.MouseLeave += delegate { if (_dragFrom < 0) HoverRow(list, -1, vertical, size); };
            }
        }

        // 끌기는 한 번에 하나뿐이라 창 단위로 들고 있어도 된다.
        private bool _dragArmed;      // 눌렸다. 아직 끌기인지 누르기인지 모른다
        private int _dragFrom = -1;   // 끌기가 시작된 자리. -1 이면 안 끌고 있다
        private int _dragTo = -1;     // 지금 놓으면 갈 자리
        private Point _dragStart;

        /// <summary>
        /// 끄는 동안 줄을 정리한다.
        ///
        /// 끄는 것은 손끝을 그대로 따라오고, 지나온 이웃들은 한 칸씩 비켜서 자리를 내어 준다.
        /// 자리를 내어 주는 모습이 없으면 어디에 놓이는지 알 수가 없다.
        /// </summary>
        private void DragRow(List<HoverItem> row, int from, double moved, bool vert, double size)
        {
            double step = StepOf(row, vert, size);
            if (step < 1) step = 1;

            int to = from + (int)Math.Round(moved / step);
            if (to < 0) to = 0;
            if (to > row.Count - 1) to = row.Count - 1;
            _dragTo = to;

            var prop = vert ? TranslateTransform.YProperty : TranslateTransform.XProperty;

            for (int i = 0; i < row.Count; i++)
            {
                double off;
                if (i == from) off = moved;                                  // 손끝을 따라온다
                else if (from < to && i > from && i <= to) off = -step;      // 앞으로 당겨진다
                else if (from > to && i >= to && i < from) off = step;       // 뒤로 밀린다
                else off = 0;

                if (i == from) row[i].Move.SetValue(prop, off);   // 즉각 따라와야 한다
                else Ease(row[i].Move, prop, off);
            }
        }

        /// <summary>이웃 사이의 실제 간격. 못 재면 계산값으로 돌아간다.</summary>
        private static double StepOf(List<HoverItem> row, bool vert, double size)
        {
            if (row.Count >= 2)
            {
                try
                {
                    Point d = row[0].Box.TranslatePoint(new Point(0, 0), row[1].Box);
                    double v = vert ? Math.Abs(d.Y) : Math.Abs(d.X);
                    if (v > 1) return v;
                }
                catch { }
            }
            return size + IconPad * 2 + IconGap * 2;
        }

        /// <summary>
        /// 바뀐 순서를 설정에 남긴다.
        ///
        /// 구분선(AppSeps)은 건드리지 않는다. 구분선은 '몇 번째 앱' 이 아니라 '몇 번째 자리' 로
        /// 저장하기 때문이다 - 앱이 옮겨 다녀도 선은 제자리에 있는 것이 맞다.
        /// </summary>
        private void CommitReorder(int from, int to)
        {
            if (from < 0 || to < 0 || from == to ||
                from >= _cfg.Apps.Count || to >= _cfg.Apps.Count)
            {
                RefreshAppBars();   // 옮겨 놓은 것을 제자리로
                return;
            }

            var moved = _cfg.Apps[from];
            _cfg.Apps.RemoveAt(from);
            _cfg.Apps.Insert(to, moved);
            _cfg.Save();
            RefreshAppBars();
        }

        /// <summary>즐겨찾기를 실은 곳을 전부 다시 만든다 - 카드·본 바·조각 바.</summary>
        private void RefreshAppBars()
        {
            _dockAppsSig = "";
            RebuildAppViews();
            RefreshPanelBars();
            if (Docked) RefreshDockBar();
        }

        /// <summary>at 번째에 마우스가 올라갔다. -1 이면 아무 데도 없다.</summary>
        private void HoverRow(List<HoverItem> row, int at, bool vertical, double size)
        {
            // 커진 만큼 넘치는 폭의 절반이 한쪽으로 삐져나온다. 이웃은 그만큼 비켜 준다.
            // 다만 그 값이 작으면(아이콘이 작을 때 3px 남짓) 밀린 티가 안 나서 최소치를 둔다.
            double spill = size * (HoverGrowBar - 1) / 2;
            if (spill < HoverPushMin) spill = HoverPushMin;

            for (int i = 0; i < row.Count; i++)
            {
                double scale = 1, push = 0;

                if (at >= 0)
                {
                    int d = i - at;
                    int away = d < 0 ? -d : d;
                    int dir = d < 0 ? -1 : 1;

                    if (away == 0) scale = HoverGrowBar;
                    else if (away == 1) push = dir * spill;
                    else if (away == 2) push = dir * spill * 0.35;   // 두 칸 건너는 살짝만
                }

                Ease(row[i].Grow, ScaleTransform.ScaleXProperty, scale);
                Ease(row[i].Grow, ScaleTransform.ScaleYProperty, scale);
                Ease(row[i].Move,
                     vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty,
                     push);

                // 밀리는 쪽으로 살짝 기운다. 미는 힘을 받는 것처럼 보이라고 넣은 것이라
                // 미는 양과 같은 비율로 줄어든다. 세로 바는 부호가 반대다 -
                // 아래로 밀리면 시계 반대로 기울어야 '밀려났다' 로 보인다.
                double tilt = (push / spill) * TiltDeg;
                if (vertical) tilt = -tilt;
                Ease(row[i].Tilt, RotateTransform.AngleProperty, tilt);
            }
        }

        /// <summary>화면 요소용. Border 같은 것은 Animatable 이 아니라 따로 받는다.</summary>
        private static void Ease(UIElement el, DependencyProperty prop, double to)
        {
            var a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(HoverMs)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(prop, a);
        }

        private static void Ease(Animatable target, DependencyProperty prop, double to)
        {
            var a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(HoverMs)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            target.BeginAnimation(prop, a);
        }

        /// <summary>
        /// 아이콘 사이의 빈칸. 구분선이 놓이는 자리이자, 꾹 눌러 선을 넣고 빼는 손잡이다.
        ///
        /// ★ 아이콘이 아니라 빈칸을 누른다 ★
        ///   아이콘을 꾹 누르는 것은 '연다' 와 헷갈린다. 빈칸은 눌러도 잃을 것이 없다.
        ///   빈칸은 늘 있고 선만 나타났다 사라진다 - 그래서 선을 넣어도 자리가 밀리지 않는다.
        ///
        /// 선은 2px 에 끝을 둥글게 만다. 1px 은 화면에서 너무 가늘어 잘 안 보였다.
        /// </summary>
        private UIElement BuildSepSlot(int at, bool vertical, double size)
        {
            return BuildSepSlot(at, vertical, size, 0);
        }

        /// <param name="lift">선을 위로 올릴 픽셀. 카드는 타일 아래 여백 때문에 낮아 보인다.</param>
        private UIElement BuildSepSlot(int at, bool vertical, double size, double lift)
        {
            return BuildSepSlot(at, vertical, size, lift, false);
        }

        /// <param name="spread">
        /// 참이면 선이 있을 때 **실제로 자리를 차지해** 좌우 아이콘을 벌린다.
        ///
        /// 카드에서만 참이다. 바에서는 거짓이어야 한다 - 바는 확보한 길이가 정해져 있어서,
        /// 선을 넣고 뺄 때마다 줄 전체가 늘었다 줄었다 하면 옆 바까지 밀린다.
        /// </param>
        private UIElement BuildSepSlot(int at, bool vertical, double size, double lift, bool spread)
        {
            double len = size > 8 ? size * 0.62 : 6;

            bool on = HasSep(at);

            var line = new Border
            {
                Background = Palette.Divider,
                CornerRadius = new CornerRadius(1),   // 끝을 둥글게
                Opacity = on ? 1 : 0,
            };

            // ★ 방금 만들어진 선이면 스르르 나타난다 ★
            //   선을 넣고 빼면 줄을 통째로 다시 만든다. 그냥 두면 새 선이 이미 다 그려진 채로
            //   튀어나온다 - 넣은 사람이 '어디에' 생겼는지 눈으로 좇을 수가 없다.
            //   ToggleSep 이 방금 넣은 자리를 알려주면, 그 자리만 0 에서 시작해 천천히 켠다.
            if (on && at == _sepFadeIn)
            {
                line.Opacity = 0;
                var appear = new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(SepMs)));
                appear.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                line.BeginAnimation(UIElement.OpacityProperty, appear);
            }
            if (vertical)
            {
                line.Height = SepThick;
                line.Width = len;
                line.HorizontalAlignment = HorizontalAlignment.Center;
                line.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                line.Width = SepThick;
                line.Height = len;
                line.HorizontalAlignment = HorizontalAlignment.Center;
                line.VerticalAlignment = VerticalAlignment.Center;
                // 가운데 정렬이라 아래 여백을 주면 그 절반만큼 위로 올라간다
                if (lift > 0) line.Margin = new Thickness(0, 0, 0, lift * 2);
            }

            var slot = new Border
            {
                Child = line,
                Background = Palette.Grab,   // 알파 0 이면 OS 가 흘려보낸다 (Palette.Grab 주석)
                Cursor = Cursors.Hand,
                ToolTip = "꾹 누르면 구분선",
            };

            // ★ 빈칸은 넉넉해야 한다 ★
            //   IconGap 두 배(8px)로 뒀더니 호버로 밀려온 이웃 아이콘이 그 위를 덮어,
            //   두 번째로 누를 때 빈칸이 아니라 아이콘이 눌렸다.
            //
            //   그리고 가로지르는 쪽도 꽉 채워야 한다. 선 길이만큼만 잡아 뒀더니
            //   위아래로 조금만 벗어나도 눌리지 않았다 - '선을 눌러도 반응 없음' 이 그것이다.
            // ★ 빈칸은 자리를 차지하지 않는다 ★
            //   폭을 그대로 두면 아이콘 사이가 그만큼 더 벌어진다. 선을 넣고 뺄 때마다
            //   줄이 늘었다 줄었다 하는 것도 보기 나쁘다.
            //   그래서 크기만큼 음수 여백을 줘서 레이아웃에는 0 으로 잡히게 하고,
            //   그림과 히트 테스트만 이웃 여백 위에 겹쳐 놓는다 (z-순서가 위라 가려지지 않는다).
            //   다만 **카드에서 선이 실제로 있을 때는** 자리를 차지하게 둔다. 선이 이웃 타일에
            //   바짝 붙어 보이던 것을 벌려 주려는 것이다. 없을 때는 그대로 0 이라,
            //   선을 안 쓰는 사람에게는 줄 간격이 예전과 똑같다.
            bool room = spread && HasSep(at);

            if (vertical)
            {
                slot.Height = SlotThick;
                slot.Margin = room ? new Thickness(0) : new Thickness(0, -SlotThick / 2, 0, -SlotThick / 2);
                slot.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            else
            {
                slot.Width = SlotThick;
                slot.Margin = room ? new Thickness(0) : new Thickness(-SlotThick / 2, 0, -SlotThick / 2, 0);
                slot.VerticalAlignment = VerticalAlignment.Stretch;
            }

            // 그리고 밀려온 아이콘보다 위에 둔다. 자리는 안 차지하고 눌리기만 한다.
            Panel.SetZIndex(slot, 2);

            int mySlot = at;
            DispatcherTimer hold = null;
            bool removing = false;   // 지우는 중에는 손을 떼도 선을 도로 켜지 않는다

            // ★ 누르는 동안 마우스를 붙잡는다 ★
            //   안 잡으면 호버로 밀려온 이웃 아이콘이 포인터 밑으로 들어오면서 MouseLeave 가 뜨고,
            //   세던 것이 취소된다. 새로 넣을 때는 줄 끝이라 밀릴 이웃이 없어 되지만,
            //   이미 있는 선을 지울 때는 양옆에 아이콘이 있어 늘 취소됐다.
            slot.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                slot.CaptureMouse();
                Ease(line, UIElement.OpacityProperty, HasSep(mySlot) ? 0.35 : 0.45);   // 세는 중

                hold = new DispatcherTimer(DispatcherPriority.Input);
                hold.Interval = TimeSpan.FromMilliseconds(HoldMs);
                hold.Tick += delegate
                {
                    if (hold == null) return;
                    hold.Stop();
                    hold = null;
                    slot.ReleaseMouseCapture();

                    if (!HasSep(mySlot)) { ToggleSep(mySlot); return; }

                    // ★ 없애는 쪽은 순서가 반대다 ★
                    //   줄을 다시 만들면 선은 그 순간 사라져 버린다. 그래서 **먼저** 스르르
                    //   지우고, 다 지워진 뒤에 실제로 뺀다.
                    removing = true;
                    var gone = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(SepMs)));
                    gone.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
                    gone.Completed += delegate { ToggleSep(mySlot); };
                    line.BeginAnimation(UIElement.OpacityProperty, gone);
                };
                hold.Start();
            };
            slot.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (hold != null) { hold.Stop(); hold = null; }
                slot.ReleaseMouseCapture();
                if (!removing) Ease(line, UIElement.OpacityProperty, HasSep(mySlot) ? 1 : 0);
            };
            slot.LostMouseCapture += (s, e) =>
            {
                if (hold != null) { hold.Stop(); hold = null; }
                Ease(line, UIElement.OpacityProperty, HasSep(mySlot) ? 1 : 0);
            };
            return slot;
        }

        /// <summary>그 자리에 구분선이 있는가.</summary>
        private bool HasSep(int index)
        {
            for (int i = 0; i < _cfg.AppSeps.Count; i++)
                if (_cfg.AppSeps[i] == index) return true;
            return false;
        }

        /// <summary>바에 실은 즐겨찾기가 차지하는 길이. 흐를 자리를 계산할 때 빼 준다.</summary>
        private double DockAppsRoom(bool vertical)
        {
            if (vertical || _dockApps == null) return 0;
            if (_dockApps.Visibility != Visibility.Visible) return 0;
            _dockApps.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return _dockApps.DesiredSize.Width + 8;
        }

        /// <summary>
        /// 바에 실은 즐겨찾기 아이콘 하나.
        ///
        /// 아이콘 사이는 어느 쪽이든 10px 띄운다 (양쪽에서 5px 씩 낸다).
        /// 세로 바는 좌우 여백을 없애 그만큼 아이콘을 키운다.
        /// </summary>
        private UIElement BuildDockApp(AppDef def, double size, bool vertical)
        {
            return BuildDockApp(def, size, vertical, -1);
        }

        /// <param name="slot">몇 번째 아이콘인가. 꾹 눌러 구분선을 넣을 때 쓴다. -1 이면 안 쓴다.</param>
        private UIElement BuildDockApp(AppDef def, double size, bool vertical, int slot)
        {
            return BuildDockApp(def, size, vertical, slot, DockEdge.Bottom);
        }

        /// <param name="barEdge">붙은 변. 커질 때 어느 쪽을 붙박아 둘지 정한다.</param>
        private UIElement BuildDockApp(AppDef def, double size, bool vertical, int slot, DockEdge barEdge)
        {
            return BuildDockApp(def, size, vertical, slot, barEdge, IconPad + 2);
        }

        private UIElement BuildDockApp(AppDef def, double size, bool vertical, int slot,
                                       DockEdge barEdge, double pad)
        {
            var def2 = def;

            var img = Apps.LoadIcon(def.Path);
            UIElement face;
            if (img != null)
            {
                face = new Image { Source = img, Width = size, Height = size };
            }
            else
            {
                string t = string.IsNullOrEmpty(def.Label) ? "?" : def.Label.Substring(0, 1);
                face = new TextBlock { Text = t, FontSize = size * 0.8, Foreground = Palette.TextDim };
            }

            var box = new Border
            {
                Child = face,
                Padding = new Thickness(pad),
                CornerRadius = new CornerRadius(5),
                // ★ 커졌다 작아졌다 반복하던 것의 정체 ★
                //   알파 0 이면 OS 가 이 자리를 '창이 없는 곳' 으로 봐서, 잡는 칸이 아니라
                //   **아이콘 그림의 불투명한 점**만 마우스를 받는다. 호버로 그림이 커지면
                //   그 점무늬가 움직인다. 쉴 때 불투명하고 커지면 투명해지는 픽셀에 커서를 두면
                //     불투명 → 커짐 → 투명 → WM_MOUSELEAVE → 작아짐 → 불투명 → ...
                //   이 되먹임이 끝없이 돈다 (실측: 커서를 세워 둔 채 주기 약 180ms).
                //   알파를 1 만 줘도 잡는 칸 전체가 늘 창의 일부라 그 고리가 끊긴다.
                Background = Palette.Grab,
                Cursor = Cursors.Hand,
                ToolTip = def.Label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // 커지고 밀리는 데 쓸 변환. 기울기(Tilt)는 지금 안 쓰지만 자리를 미리 잡아 둔다 -
            // 나중에 강조를 더 하고 싶을 때 구조를 뒤집지 않으려는 것이다.
            var grow = new ScaleTransform(1, 1);
            var move = new TranslateTransform(0, 0);
            var tilt = new RotateTransform(0);
            var tg = new TransformGroup();
            tg.Children.Add(grow);
            tg.Children.Add(tilt);
            tg.Children.Add(move);
            // ★ 툴팁이 커서를 덮으면 호버가 끊긴다 ★
            //   기본 배치는 '마우스 기준' 이라 툴팁 창이 커서 바로 밑에 뜬다. 화면 아래쪽
            //   바에서는 셸이 그것을 위로 뒤집어 아이콘 위에 얹는다. 그 창이 커서를 가리는
            //   순간 MouseLeave 가 나서 아이콘이 작아지고, 툴팁이 닫히면 다시 커진다.
            //   커졌다 작아졌다 하는 것의 정체가 이것이었다.
            //
            //   그래서 **아이콘 기준**으로 바깥쪽에 붙이고, 커진 아이콘 너머까지 밀어낸다.
            //   커서는 잡는 칸 안에 그대로 있으므로 끊길 일이 없다.
            {
                double clear = size * MaxGrow * 0.6 + 8;   // 가장 크게 커졌을 때도 안 닿게
                ToolTipService.SetPlacementTarget(box, box);
                ToolTipService.SetInitialShowDelay(box, 450);

                switch (barEdge)
                {
                    case DockEdge.Top:
                        ToolTipService.SetPlacement(box, PlacementMode.Bottom);
                        ToolTipService.SetVerticalOffset(box, clear);
                        break;
                    case DockEdge.Left:
                        ToolTipService.SetPlacement(box, PlacementMode.Right);
                        ToolTipService.SetHorizontalOffset(box, clear);
                        break;
                    case DockEdge.Right:
                        ToolTipService.SetPlacement(box, PlacementMode.Left);
                        ToolTipService.SetHorizontalOffset(box, -clear);
                        break;
                    default:   // Bottom
                        ToolTipService.SetPlacement(box, PlacementMode.Top);
                        ToolTipService.SetVerticalOffset(box, -clear);
                        break;
                }
            }

            // ★ 보이는 것은 마우스를 받지 않는다 ★
            //   WPF 는 자식이 부모 밖으로 나가도 히트 테스트에 걸리게 둔다(클립이 없으면).
            //   그래서 변환을 face 로 옮기는 것만으로는 모자랐다 - 커지거나 밀려서
            //   box 밖으로 삐져나온 face 가 여전히 box 의 MouseEnter 를 일으켰다.
            //   이웃이 밀려 커서 밑으로 들어왔다 나갔다 하면 그대로 되먹임이 된다.
            //   face 를 히트 테스트에서 빼면 잡는 칸은 box 의 붙박인 사각형 하나뿐이다.
            face.IsHitTestVisible = false;

            // ★ 변환은 '잡는 칸' 이 아니라 '보이는 것' 에만 건다 ★
            //   box 에 걸면 커질 때 히트 영역까지 같이 커진다. 그러면 커서가
            //   '쉴 때 크기 밖 · 커진 크기 안' 에 놓였을 때 커짐과 작아짐이 서로를 되먹여
            //   아이콘이 끝없이 커졌다 작아졌다 한다.
            //   잡는 칸을 붙박아 두면 그 되먹임이 생길 수가 없다.
            face.RenderTransform = tg;

            // ★ 바깥쪽 끝을 붙박아 두고 안쪽으로 자란다 ★
            //   아래 바면 아래를 딛고 위로, 위 바면 위를 딛고 아래로 커진다.
            //   가운데를 기준으로 하면 커질 때 바 밖으로 삐져나가 잘린다.
            switch (barEdge)
            {
                case DockEdge.Top: face.RenderTransformOrigin = new Point(0.5, 0); break;
                case DockEdge.Left: face.RenderTransformOrigin = new Point(0, 0.5); break;
                case DockEdge.Right: face.RenderTransformOrigin = new Point(1, 0.5); break;
                default: face.RenderTransformOrigin = new Point(0.5, 1); break;   // Bottom
            }

            // 쉴 때도 그 끝에 붙어 있어야 커질 자리가 안쪽에 남는다
            if (barEdge == DockEdge.Top) box.VerticalAlignment = VerticalAlignment.Top;
            else if (barEdge == DockEdge.Bottom) box.VerticalAlignment = VerticalAlignment.Bottom;
            else if (barEdge == DockEdge.Left) box.HorizontalAlignment = HorizontalAlignment.Left;
            else box.HorizontalAlignment = HorizontalAlignment.Right;
            box.Tag = new HoverItem { Box = box, Grow = grow, Move = move, Tilt = tilt };

            // 호버 배경은 깔지 않는다. 아이콘만 커지는 편이 깔끔하다.
            // 여기서 끝낸다. 안 그러면 바를 끄는 동작으로 넘어가 위젯이 떨어져 나온다.
            //
            // 꾹 누르면 이 아이콘 뒤에 구분선이 생긴다. 다시 꾹 누르면 없어진다.
            // 카드 쪽 꾹 누르기는 이미 '편집 모드' 라 거기엔 얹지 않는다 - 바에만 둔다.
            int mySlot = slot;
            var myGrow = grow;
            var myMove = move;
            var myEdge = barEdge;
            DispatcherTimer hold = null;
            bool fired = false;

            // ★ 한 번의 누르기가 두 가지 뜻을 갖는다 ★
            //   짧게 눌렀다 떼면 '열기', 누른 채로 움직이면 '순서 바꾸기'.
            //   누르는 순간에는 어느 쪽인지 알 수 없으므로, 열기는 뗄 때까지 미룬다.
            //   (구분선 넣기는 아이콘이 아니라 아이콘 사이의 빈칸이 맡는다 - BuildSepSlot)
            box.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (mySlot < 0) return;

                fired = false;
                var it0 = box.Tag as HoverItem;
                if (it0 != null) it0.Dragged = false;

                _dragArmed = true;
                _dragFrom = -1;
                _dragStart = CursorOnScreen();
                box.CaptureMouse();

                EaseTo(myGrow, HoldGrow);   // 누르는 동안 살짝 더 커진다 (여기가 한계라 안 잘린다)
            };

            box.MouseMove += (s, e) =>
            {
                if (!_dragArmed) return;
                if (e.LeftButton != MouseButtonState.Pressed) return;

                var it = box.Tag as HoverItem;
                if (it == null || it.Row == null || it.Row.Count < 2) return;

                bool vert = (myEdge == DockEdge.Left || myEdge == DockEdge.Right);

                double sx, sy;
                Dock.GetDpiScale(this, out sx, out sy);
                double axis = vert ? sy : sx;
                if (axis <= 0) axis = 1;

                Point now = CursorOnScreen();
                double moved = (vert ? (now.Y - _dragStart.Y) : (now.X - _dragStart.X)) / axis;

                if (_dragFrom < 0)
                {
                    // 아직 '누르기' 다. 손이 흔들린 정도로는 순서를 흩뜨리지 않는다.
                    if (Math.Abs(moved) < DragStartPx) return;

                    _dragFrom = it.Index;
                    _dragTo = it.Index;
                    it.Dragged = true;

                    HoverRow(it.Row, -1, vert, size);   // 호버 확대·밀기를 걷어낸다
                    Panel.SetZIndex(box, 10);          // 끄는 것이 이웃 위로 온다

                    // 애니메이션이 값을 쥐고 있으면 직접 못 옮긴다. 놓아준 뒤에 쓴다.
                    var p0 = vert ? TranslateTransform.YProperty : TranslateTransform.XProperty;
                    it.Move.BeginAnimation(p0, null);
                    EaseTo(it.Grow, HoldGrow);
                }

                DragRow(it.Row, _dragFrom, moved, vert, size);
            };

            box.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (hold != null) { hold.Stop(); hold = null; }

                // ★ 캡처를 놓기 '전' 에 끌기 상태를 챙긴다 ★
                //   ReleaseMouseCapture() 는 LostMouseCapture 를 그 자리에서 일으킨다.
                //   그 핸들러는 '빼앗겼다' 고 보고 순서를 제자리로 되돌리므로,
                //   먼저 지우지 않으면 방금 옮긴 것을 제 손으로 무르게 된다.
                int from = _dragFrom, to = _dragTo;
                _dragArmed = false;
                _dragFrom = -1;
                box.ReleaseMouseCapture();

                var it = box.Tag as HoverItem;

                if (from >= 0)
                {
                    Panel.SetZIndex(box, 0);
                    CommitReorder(from, to);   // 바를 다시 만든다 - 여기서 끝이다
                    return;
                }

                EaseTo(myGrow, box.IsMouseOver ? HoverGrowBar : 1);

                if (fired) { fired = false; return; }
                if (it != null && it.Dragged) { it.Dragged = false; return; }

                Apps.Open(def2);
                BounceIcon(myMove, myEdge, size);   // 열렸다는 것을 눈으로 알려준다
            };

            // Alt+Tab·화면 잠금 등으로 캡처를 빼앗기면 ButtonUp 이 오지 않는다.
            // 그대로 두면 다음에 지나가기만 해도 순서가 바뀐다.
            box.LostMouseCapture += (s, e) =>
            {
                if (!_dragArmed && _dragFrom < 0) return;
                _dragArmed = false;
                if (_dragFrom >= 0)
                {
                    _dragFrom = -1;
                    Panel.SetZIndex(box, 0);
                    RefreshAppBars();   // 옮겨 놓은 것을 제자리로 되돌린다
                }
            };

            box.MouseLeave += (s, e) =>
            {
                if (hold != null) { hold.Stop(); hold = null; }
                fired = false;
            };
            return box;
        }

        /// <summary>
        /// 아이콘이 세 번 튄다. macOS 독이 앱을 띄울 때 하는 그것.
        ///
        /// ★ 등속으로 오르내리면 물체로 안 보인다 ★
        ///   올라갈 때는 **느려지고**(EaseOut) 떨어질 때는 **빨라진다**(EaseIn).
        ///   그게 던져 올린 물체가 실제로 그리는 곡선이라, 눈이 무게를 느낀다.
        ///   튈 때마다 높이를 절반 남짓으로 줄여 힘을 잃어가는 것처럼 보이게 한다.
        ///
        /// ★ 안쪽으로만 튄다 ★
        ///   바깥으로 튀면 화면 밖이고, 바 창이 거기서 잘라낸다(ClipToBounds).
        ///   아래 바면 위로, 위 바면 아래로, 옆 바면 화면 안쪽으로.
        ///
        /// 미는 축과 튀는 축이 서로 달라(가로 바는 밀기 X · 튀기 Y) 호버 밀기와 부딪히지 않는다.
        /// </summary>
        private static void BounceIcon(TranslateTransform move, DockEdge edge, double size)
        {
            if (move == null) return;

            bool horiz = (edge == DockEdge.Left || edge == DockEdge.Right);
            double dir = (edge == DockEdge.Top || edge == DockEdge.Left) ? 1 : -1;

            double h = size * 0.5;
            if (h < 6) h = 6;
            if (h > 26) h = 26;   // 바 안에 머물러야 한다

            var rise = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var fall = new QuadraticEase { EasingMode = EasingMode.EaseIn };

            double[] amp = { 1.0, 0.5, 0.22 };
            int[] half = { 210, 145, 100 };   // 한 번 튀는 데 걸리는 시간의 절반

            var k = new DoubleAnimationUsingKeyFrames();
            double t = 0;
            for (int i = 0; i < 3; i++)
            {
                t += half[i];
                k.KeyFrames.Add(new EasingDoubleKeyFrame(dir * h * amp[i],
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t)), rise));
                t += half[i];
                k.KeyFrames.Add(new EasingDoubleKeyFrame(0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t)), fall));
            }
            k.FillBehavior = FillBehavior.Stop;   // 끝나면 값을 놓아준다 (밀기가 다시 쓴다)

            var prop = horiz ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            move.BeginAnimation(prop, null);
            move.BeginAnimation(prop, k);
        }

        /// <summary>가로 바 한 벌을 담는다. 흐를 때는 이걸 두 번 부른다.</summary>
        private void AddDockRun()
        {
            foreach (var def in _cfg.Symbols)
            {
                Quote q;
                if (!_quotes.TryGetValue(def.Key, out q) || !q.Ok) continue;

                var dv = BuildDockQuote(def, q, false);
                _dockItems.Children.Add(dv.Item2);
                _dockViews.Add(dv.Item1);   // 두 벌 모두 등록해야 양쪽이 같이 번쩍인다
            }

            // 나눠 놓았으면 날씨는 제 창을 갖고 있다. 여기 또 실으면 두 번 나온다.
            if (_cfg.Separated) return;

            var wdef = MainWeatherDef;
            if (wdef != null)
            {
                WeatherInfo w;
                if (_weatherData.TryGetValue(wdef.Key, out w) && w.Ok)
                    _dockItems.Children.Add(BuildDockWeather(wdef, w, false));
            }
        }

        private void AddDockWeather(double avail, bool vertical)
        {
            if (_cfg.Separated) return;   // 나눠 놓았으면 날씨는 제 창에 있다
            var wdef = MainWeatherDef;
            if (wdef == null) return;
            WeatherInfo w;
            if (_weatherData.TryGetValue(wdef.Key, out w) && w.Ok)
                AddDockItem(BuildDockWeather(wdef, w, vertical), avail, vertical);
        }

        private double MeasureItems()
        {
            _dockItems.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return _dockItems.DesiredSize.Width;
        }

        /// <summary>한 벌 길이만큼 왼쪽으로 밀었다가 처음으로 돌아온다. 속도를 일정하게 유지한다.</summary>
        private void StartMarquee(double loop)
        {
            StopMarquee();
            if (_dockScroll == null || loop <= 0) return;

            _marqueeLoop = loop;
            _marqueeSecs = loop / MarqueeSpeed;
            if (_marqueeSecs < 4) _marqueeSecs = 4;

            ResumeMarqueeFrom(0);
        }

        /// <summary>
        /// 지금 자리에서 이어서 흐르게 한다.
        /// 잡아 끌다 놓았을 때 처음으로 튀지 않게 그 위치에 맞춰 시간을 옮겨 준다.
        /// </summary>
        private void ResumeMarqueeFrom(double x)
        {
            if (_dockScroll == null || _marqueeLoop <= 0) return;

            var a = new DoubleAnimation(0, -_marqueeLoop, new Duration(TimeSpan.FromSeconds(_marqueeSecs)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            _dockScrollClock = a.CreateClock();
            _dockScroll.ApplyAnimationClock(TranslateTransform.XProperty, _dockScrollClock);

            try
            {
                double frac = (-x) / _marqueeLoop;
                if (frac < 0) frac = 0;
                if (frac > 1) frac = frac - Math.Floor(frac);
                _dockScrollClock.Controller.Seek(
                    TimeSpan.FromSeconds(frac * _marqueeSecs), TimeSeekOrigin.BeginTime);

                // 아직 마우스가 올라가 있으면 멈춘 채로 둔다
                if (_dockBar != null && _dockBar.IsMouseOver) _dockScrollClock.Controller.Pause();
            }
            catch { }
        }

        private void StopMarquee()
        {
            _scrubbing = false;
            _marqueeLoop = 0;
            if (_dockScroll == null) return;
            _dockScroll.ApplyAnimationClock(TranslateTransform.XProperty, null);
            _dockScrollClock = null;
            _dockScroll.X = 0;
        }

        /// <summary>바에 무엇이 담겨야 하는지를 한 줄로 요약한다. 이게 그대로면 다시 만들지 않는다.</summary>
        private string DockSignature(bool vertical, double span)
        {
            var sb = new StringBuilder(96);
            // 배율도 넣는다. 바뀌면 글자 크기가 달라지므로 다시 만들어야 한다.
            sb.Append(vertical ? 'V' : 'H').Append((int)span)
              .Append('@').Append(Math.Round(_cfg.DockScale, 3)).Append('|');
            foreach (var def in _cfg.Symbols)
            {
                Quote q;
                if (_quotes.TryGetValue(def.Key, out q) && q.Ok) sb.Append(def.Key).Append(';');
            }
            var wdef = MainWeatherDef;
            if (wdef != null)
            {
                WeatherInfo w;
                if (_weatherData.TryGetValue(wdef.Key, out w) && w.Ok) sb.Append('#').Append(wdef.Key);
            }
            return sb.ToString();
        }

        /// <summary>이미 만들어 둔 항목의 값만 갈아끼운다. 같은 값이면 SetText 가 알아서 넘어간다.</summary>
        private void UpdateDockValues()
        {
            for (int i = 0; i < _dockViews.Count; i++)
            {
                var v = _dockViews[i];
                Quote q;
                if (!_quotes.TryGetValue(v.Key, out q) || !q.Ok) continue;

                SetText(v.Price, q.Price);
                if (v.Ratio != null)
                {
                    SetText(v.Ratio, (q.Ratio ?? "") + (q.RatioSuffix ?? ""));
                    SetBrush(v.Ratio, string.IsNullOrEmpty(q.RatioSuffix) ? Palette.TextDim : Palette.ForDir(q.Dir));
                }
            }
            SyncMarquee();
            UpdateDockClock();
        }

        /// <summary>
        /// 흐름 상태를 실제 마우스 위치에 다시 맞춘다.
        /// MouseLeave 를 놓치면 (창이 다시 그려지거나 포인터가 자식으로 넘어갈 때 생긴다)
        /// 멈춘 채로 갇혀서 '한 바퀴 돌고 멈췄다' 처럼 보인다.
        /// </summary>
        private void SyncMarquee()
        {
            if (_scrubbing || _dockScrollClock == null || _dockBar == null) return;
            try
            {
                if (_dockBar.IsMouseOver) _dockScrollClock.Controller.Pause();
                else _dockScrollClock.Controller.Resume();
            }
            catch { }
        }

        /// <summary>넣어보고 길이를 넘으면 도로 뺀다. 넣었으면 true.</summary>
        private bool AddDockItem(UIElement item, double avail, bool vertical)
        {
            _dockItems.Children.Add(item);
            _dockItems.Measure(vertical
                ? new Size(Math.Max(double.IsNaN(Width) ? ActualWidth : Width, 1), double.PositiveInfinity)
                : new Size(double.PositiveInfinity, double.PositiveInfinity));

            double used = vertical ? _dockItems.DesiredSize.Height : _dockItems.DesiredSize.Width;
            if (used <= avail) return true;

            _dockItems.Children.Remove(item);
            return false;
        }

        private Tuple<DockView, UIElement> BuildDockQuote(SymbolDef def, Quote q, bool vertical)
        {
            Brush ratioBrush = string.IsNullOrEmpty(q.RatioSuffix) ? Palette.TextDim : Palette.ForDir(q.Dir);
            string ratio = (q.Ratio ?? "") + (q.RatioSuffix ?? "");
            bool hasRatio = !string.IsNullOrEmpty(q.Ratio);

            TextBlock price, ratioTb = null;
            var inner = new StackPanel();
            double f = DockFont;   // 바 두께가 배율을 따라가므로 글자도 같이 간다

            if (vertical)
            {
                // 좁은 세로 바 - 이름/값/등락을 세 줄로 쌓는다. 글자는 눕히지 않는다.
                // 긴 숫자는 줄인다. 접혀서 세 줄이 되면 읽히지도 않고 바만 길어진다.
                price = DockLine(ShortNumber(q.Price), f, Palette.Text, true);
                if (hasRatio) ratioTb = DockLine(ratio, f - 2, ratioBrush, true);

                inner.Children.Add(DockLine(def.Label, f - 2, Palette.TextFaint, true));
                inner.Children.Add(price);
                if (ratioTb != null) inner.Children.Add(ratioTb);
            }
            else
            {
                inner.Orientation = Orientation.Horizontal;
                price = DockLine(q.Price, f, Palette.Text, false);
                if (hasRatio) ratioTb = DockLine(ratio, f - 1, ratioBrush, false, new Thickness(5, 0, 0, 0));

                inner.Children.Add(DockLine(def.Label, f - 1, Palette.TextFaint, false, new Thickness(0, 0, 5, 0)));
                inner.Children.Add(price);
                if (ratioTb != null) inner.Children.Add(ratioTb);
            }

            var scale = new ScaleTransform(1, 1);

            // 배경을 항목마다 따로 둔다. 바 전체를 물들이면 어느 것이 움직였는지 알 수 없다.
            var box = new Border
            {
                Child = inner,
                Background = Palette.Clear,
                CornerRadius = new CornerRadius(4),
                Padding = vertical ? new Thickness(2, 2, 2, 2) : new Thickness(5, 1, 5, 1),
                Margin = vertical ? new Thickness(2, 0, 2, 7) : new Thickness(0, 0, 8, 0),
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };

            // 더블클릭으로 해당 페이지를 연다.
            // 한 번 누르는 것은 처리하지 않고 바로 흘려보내야 바를 끌어 뗄 수 있다.
            // (바 전체에 걸린 '더블클릭 = 떼기' 보다 이쪽이 먼저 잡아야 한다)
            var key = def.Key;
            box.Cursor = Cursors.Hand;
            box.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount != 2) return;
                e.Handled = true;
                OpenQuoteLink(key);
            };

            // 세로 바는 이름도 값도 줄거나 접힌다. 정확한 것은 올려 보면 나오게 한다.
            if (vertical)
                box.ToolTip = def.Label + "   " + q.Price + (hasRatio ? "   " + ratio : "");

            var dv = new DockView { Key = def.Key, Box = box, Price = price, Ratio = ratioTb, Scale = scale };
            return Tuple.Create(dv, (UIElement)box);
        }

        /// <summary>
        /// 좁은 세로 바에서 긴 숫자를 줄인다.  106,110,000 -> 1.06억
        ///
        /// 접어서 세 줄이 되면 값이 읽히지도 않고 바만 길어진다. 자리 수를 줄이는 편이 낫다.
        /// 정확한 값은 마우스를 올리면 나온다(BuildDockQuote 의 ToolTip).
        ///
        /// **백만 미만은 건드리지 않는다** - 71,000 을 7.1만 으로 바꾸면 오히려 읽기 어렵다.
        /// 통화 기호처럼 숫자에 붙어 오는 것은 떼지 않고 그대로 둔다($131.71 의 '$').
        /// </summary>
        private static string ShortNumber(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            // 숫자 덩어리의 처음과 끝을 찾는다
            int a = -1, b = -1;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool part = (c >= '0' && c <= '9') || c == ',' || c == '.';
                if (part) { if (a < 0) a = i; b = i; }
                else if (a >= 0) break;
            }
            if (a < 0) return s;

            double v;
            if (!double.TryParse(s.Substring(a, b - a + 1).Replace(",", ""),
                                 NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return s;

            double av = Math.Abs(v);
            if (av < 1000000) return s;   // 여섯 자리까지는 그냥 읽힌다

            string body;
            if (av >= 1e8)
            {
                double e = v / 1e8;
                // 한 자리 수는 소수 둘까지(1.06억), 두 자리부터는 하나만(12.3억)
                body = e.ToString(Math.Abs(e) < 10 ? "0.##" : "0.#", CultureInfo.InvariantCulture) + "억";
            }
            else
            {
                body = (v / 1e4).ToString("0", CultureInfo.InvariantCulture) + "만";
            }

            return s.Substring(0, a) + body + s.Substring(b + 1);
        }

        private static TextBlock DockLine(string text, double size, Brush fg, bool centered)
        {
            return DockLine(text, size, fg, centered, new Thickness(0));
        }

        /// <summary>
        /// 바에 들어가는 글자 한 줄.
        ///
        /// centered 는 세로 바(좌·우에 붙었을 때)에서만 참이다. 거기서는 폭이 좁아
        /// '한국 기준금리' 나 '107,720,000' 같은 것이 한 줄에 안 들어간다.
        /// 잘라내는 대신 **접는다** - 값이 보이지 않으면 바가 있을 이유가 없다.
        /// (접히려면 폭이 정해져 있어야 한다. RefreshDockBar 의 세로 분기에서 _dockItems 에 폭을 준다)
        /// </summary>
        private static TextBlock DockLine(string text, double size, Brush fg, bool centered, Thickness margin)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = fg,
                Margin = margin,
                TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
                HorizontalAlignment = centered ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = centered ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = centered ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                LineHeight = centered ? size * 1.15 : double.NaN,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
        }

        private UIElement BuildDockWeather(SymbolDef def, WeatherInfo w, bool vertical)
        {
            double f = DockFont;
            double iconSize = (vertical ? 22 : 15) * (f / DockFontBase);
            var icon = WeatherIcon.Create(iconSize);
            WeatherIcon.Draw(icon, w.Code, w.IsDay);
            var iconHost = new Grid
            {
                Width = iconSize,
                Height = iconSize,
                Children = { icon },
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                Margin = vertical ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 5, 0),
            };
            string temp = w.Temp.ToString("0.#", CultureInfo.InvariantCulture) + "°";

            var inner = new StackPanel();
            if (vertical)
            {
                inner.Children.Add(iconHost);
                inner.Children.Add(DockLine(temp, f, Palette.Text, true));
                inner.Children.Add(DockLine(def.Label, f - 2, Palette.TextFaint, true));
            }
            else
            {
                inner.Orientation = Orientation.Horizontal;
                inner.Children.Add(iconHost);
                inner.Children.Add(DockLine(temp, f, Palette.Text, false));
                inner.Children.Add(DockLine(def.Label, f - 1, Palette.TextFaint, false, new Thickness(5, 0, 0, 0)));
            }

            var box = new Border
            {
                Child = inner,
                Background = Palette.Clear,
                CornerRadius = new CornerRadius(4),
                Padding = vertical ? new Thickness(2, 2, 2, 2) : new Thickness(5, 1, 5, 1),
                Margin = vertical ? new Thickness(2, 0, 2, 7) : new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
            };

            // 종목과 같게 - 더블클릭이면 열고, 한 번 누르는 건 바로 흘려보낸다
            var def2 = def;
            box.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount != 2) return;
                e.Handled = true;
                OpenWeatherLink(def2);
            };
            return box;
        }

        private void UpdateDockClock()
        {
            if (_panelBarClock != null && _panelClock != null && _panelClock.Edge != DockEdge.None)
            {
                bool pv = (_panelClock.Edge == DockEdge.Left || _panelClock.Edge == DockEdge.Right);
                _panelBarClock.Text = ClockBarText(pv);
            }

            if (_dockClock == null) return;

            // 시계는 BuildDockBar 에서 한 번만 만들어진다.
            // 두께를 끌어 바꿔도 여기서 맞춰주지 않으면 글자 크기가 그대로 남는다.
            double fs = DockFont - 1;
            if (Math.Abs(_dockClock.FontSize - fs) > 0.01) _dockClock.FontSize = fs;
            var now = DateTime.Now;
            bool vertical = (_cfg.DockedEdge == DockEdge.Left || _cfg.DockedEdge == DockEdge.Right);
            string sep = vertical ? Environment.NewLine : " ";   // 좁은 세로 바에서는 두 줄로
            SetText(_dockClock, string.Format(CultureInfo.InvariantCulture, "{0}/{1}{2}{3}",
                now.Month, now.Day, sep, now.ToString("HH:mm", CultureInfo.InvariantCulture)));
        }


        // ---------- 급등·급락 알림 ----------

        /// <summary>
        /// 직전에 받은 값과 비교해 이번 변동이 '단기 급등·급락' 인지 본다.
        /// 새 값은 늘 기록해 두고, 판정 여부만 돌려준다.
        /// </summary>
        private double NoteSurge(SymbolDef def, Quote q)
        {
            double now = ParsePrice(q.Price);
            if (double.IsNaN(now) || now <= 0) return 0;

            PricePoint prev;
            if (!_lastPrice.TryGetValue(def.Key, out prev) || prev.Value <= 0)
            {
                _lastPrice[def.Key] = new PricePoint { Value = now, At = DateTime.UtcNow };
                return 0;
            }

            // 창이 지나기 전에는 기준값을 건드리지 않는다.
            // 이렇게 해야 1초 주기로 돌려도 '직전 1초' 가 아니라 '30초 전' 과 비교한다.
            // 가장자리에 붙이거나 새로고침을 눌러 강제 조회가 돌 때 헛깜빡이던 것도 이걸로 막힌다.
            double gap = (DateTime.UtcNow - prev.At).TotalSeconds;
            if (gap < SurgeWindowSec) return 0;

            double pct = 0;
            if (_cfg.SurgeAlert && gap <= SurgeMaxGapSec)   // 접어뒀다 편 경우는 '단기' 가 아니다
            {
                double moved = (now - prev.Value) / prev.Value * 100.0;
                if (Math.Abs(moved) >= SurgeThreshold(def.Kind)) pct = moved;
            }

            _lastPrice[def.Key] = new PricePoint { Value = now, At = DateTime.UtcNow };
            return pct;
        }

        /// <summary>
        /// 한 번의 갱신 사이에 이만큼 움직이면 알린다(%).
        /// 환율과 코인은 평소 흔들리는 폭이 자릿수부터 달라서 종류별로 나눠 둔다.
        /// </summary>
        private double SurgeThreshold(SourceKind kind)
        {
            if (_cfg.SurgePercent > 0) return _cfg.SurgePercent;
            switch (kind)
            {
                case SourceKind.Fx: return 0.15;
                case SourceKind.Ecos: return 0.50;
                case SourceKind.Index: return 0.30;
                case SourceKind.Coin: return 1.00;
                default: return 0.70;   // 국내·해외 주식
            }
        }

        /// <summary>"1,383.60" 이나 "$134.48" 같은 표시용 문자열에서 숫자만 뽑는다.</summary>
        private static double ParsePrice(string s)
        {
            if (string.IsNullOrEmpty(s)) return double.NaN;

            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if ((c >= '0' && c <= '9') || c == '.' || (c == '-' && sb.Length == 0)) sb.Append(c);

            double v;
            if (double.TryParse(sb.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return double.NaN;
        }

        /// <summary>
        /// 급등·급락한 항목을 알린다.
        /// 타일 보기에서만 크기까지 키우고, 목록·도킹에서는 배경만 번쩍인다.
        /// </summary>
        private void FlashSurge(List<SurgeHit> hits)
        {
            if (hits == null || hits.Count == 0 || !_cfg.SurgeAlert) return;
            if (_editMode) return;   // 흔들림·드래그와 같은 속성을 쓰므로 편집 중에는 비켜준다

            if (Docked)
            {
                // 움직인 항목만 물들이고 부풀린다. 바 전체를 물들이면 어느 것인지 알 수 없다.
                double growAt = _cfg.SurgeGrowPercent > 0 ? _cfg.SurgeGrowPercent : DockGrowMinPercent;
                for (int i = 0; i < hits.Count; i++)
                {
                    double abs = Math.Abs(hits[i].Pct);
                    for (int k = 0; k < _dockViews.Count; k++)
                    {
                        if (_dockViews[k].Key != hits[i].Key) continue;
                        FlashBackground(_dockViews[k].Box, Palette.Clear);
                        if (abs >= growAt) GrowDockItem(_dockViews[k], abs);
                    }
                }
                return;
            }

            bool bump = _cfg.GridView && !_cfg.Minimized;
            for (int i = 0; i < hits.Count; i++)
            {
                string key = hits[i].Key;
                foreach (var v in _rows) if (v.Def.Key == key) FlashView(v, false);
                foreach (var v in _tiles) if (v.Def.Key == key) FlashView(v, bump);
            }
        }

        /// <summary>
        /// 가장자리 바에서 크게 움직인 항목을 부풀린다.
        /// 움직인 만큼 커지되 10% 에서 멈춘다. 튀어나오지 않고 천천히 부풀었다 돌아온다.
        /// </summary>
        private static void GrowDockItem(DockView v, double absPct)
        {
            if (v.Scale == null) return;

            // 움직인 만큼 커지되, 눈에 띄어야 하므로 아래위로 가둔다.
            double grow = absPct / 100.0;
            if (grow < DockGrowFloor) grow = DockGrowFloor;
            if (grow > DockGrowMaxScale) grow = DockGrowMaxScale;

            v.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            v.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            var a = new DoubleAnimation(1.0, 1.0 + grow, new Duration(TimeSpan.FromMilliseconds(850)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop,
            };
            v.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            v.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        private void FlashView(QuoteView v, bool bump)
        {
            FlashBackground(v.Root, v.IsTile ? Palette.Tile : Palette.Clear);
            if (!bump || v.Scale == null) return;

            v.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            v.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            var a = new DoubleAnimation(1.0, SurgeScale, new Duration(TimeSpan.FromMilliseconds(SurgeHalfMs)))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(SurgeCycles),
                FillBehavior = FillBehavior.Stop,
            };
            v.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            v.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        /// <summary>배경을 잠깐 붉게 물들였다가 원래 브러시로 되돌린다.</summary>
        private static void FlashBackground(Border target, Brush restore)
        {
            if (target == null) return;

            Color from = Colors.Transparent;
            var solid = target.Background as SolidColorBrush;
            if (solid != null) from = solid.Color;

            // 얼어 있는 팔레트 브러시는 애니메이션할 수 없으므로 사본을 하나 만들어 쓴다
            var live = new SolidColorBrush(from);
            target.Background = live;

            var a = new ColorAnimation(Palette.SurgeColor, new Duration(TimeSpan.FromMilliseconds(SurgeHalfMs)))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(SurgeCycles),
                FillBehavior = FillBehavior.Stop,
            };
            a.Completed += delegate { target.Background = restore; };
            live.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }


        private void ApplyCardWidth()
        {
            if (_gridPanel != null) _gridPanel.Columns = _cfg.GridColumns;
            if (_card != null) _card.Width = CurrentCardWidth;
            var vis = (_cfg.Expanded && _cfg.GridView && !_cfg.Minimized)
                    ? Visibility.Visible : Visibility.Collapsed;
            if (_leftGrip != null) _leftGrip.Visibility = vis;
            if (_rightGrip != null) _rightGrip.Visibility = vis;
        }

        private void SetColumns(int n, bool anchorRight)
        {
            if (n < Config.MinColumns) n = Config.MinColumns;
            if (n > Config.MaxColumns) n = Config.MaxColumns;
            if (n == _cfg.GridColumns) return;

            int before = _cfg.GridColumns;

            // 접어둔 상태라면 '보이던 줄 수'를 유지한다.
            // 그래야 옆으로 늘렸을 때 숨어 있던 항목들이 순서대로 채워진다.
            if (_cfg.ListLimit > 0 && before > 0)
            {
                int visibleRows = (_cfg.ListLimit + before - 1) / before;
                int want = visibleRows * n;
                _cfg.ListLimit = (want >= _cfg.Symbols.Count) ? 0 : want;
            }

            _cfg.GridColumns = n;
            ApplyCardWidth();

            // 왼쪽 손잡이로 늘릴 때는 오른쪽 끝이 제자리에 있어야 자연스럽다
            if (anchorRight)
            {
                double delta = (n - before) * TileWidth * _cfg.Scale;
                Left -= delta;
            }

            ApplyVisibleLimit();
            ClampToScreen();
        }

        /// <summary>카드 좌우 가장자리. 옆으로 끌면 타일 가로 개수가 늘고 준다.</summary>
        private UIElement BuildSideGrip(bool isLeft)
        {
            var bar = new Rectangle
            {
                Width = 2.5,
                Height = 26,
                RadiusX = 1.25,
                RadiusY = 1.25,
                Fill = Palette.GripDot,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var grip = new Border
            {
                Width = 9,
                Background = Palette.Clear,
                Cursor = Cursors.SizeWE,
                HorizontalAlignment = isLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = isLeft ? new Thickness(-11, 0, 0, 0) : new Thickness(0, 0, -11, 0),
                Child = bar,
                Visibility = Visibility.Collapsed,
                ToolTip = "좌우로 끌어서 가로 개수 조절 (최대 10)",
            };

            bool dragging = false;
            double startX = 0;
            int startCols = 0;

            grip.MouseEnter += (s, e) => bar.Fill = Palette.IconHover;
            grip.MouseLeave += (s, e) => { if (!dragging) bar.Fill = Palette.GripDot; };

            grip.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                dragging = true;
                startX = PointToScreen(e.GetPosition(this)).X;
                startCols = _cfg.GridColumns;
                grip.CaptureMouse();
            };
            grip.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                double sxc, syc;
                Dock.GetDpiScale(this, out sxc, out syc);
                double dx = (PointToScreen(e.GetPosition(this)).X - startX) / sxc;   // 물리 -> DIP
                if (isLeft) dx = -dx;                       // 왼쪽 손잡이는 바깥으로 끌 때 늘어난다
                double unit = TileWidth * _cfg.Scale;
                SetColumns(startCols + (int)Math.Round(dx / unit), isLeft);
            };
            grip.MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                grip.ReleaseMouseCapture();
                bar.Fill = Palette.GripDot;
                SavePlacement();
            };
            grip.LostMouseCapture += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                bar.Fill = Palette.GripDot;
                SavePlacement();
            };

            return grip;
        }

        private UIElement BuildGrip()
        {
            var grip = new Canvas
            {
                Width = 14, Height = 14,
                Background = Palette.Clear,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -8, -8),
                Cursor = Cursors.SizeNWSE,
                ToolTip = "끌어서 크기 조절 (100% ~ 150%)",
            };
            // 대각선 점 3개
            AddGripDot(grip, 10, 4);
            AddGripDot(grip, 10, 8);
            AddGripDot(grip, 6, 8);

            bool dragging = false;
            double startScale = 1, startX = 0, startY = 0;

            grip.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                dragging = true;
                startScale = _cfg.Scale;
                var p = PointToScreen(e.GetPosition(this));
                startX = p.X; startY = p.Y;
                grip.CaptureMouse();
            };
            grip.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                var p = PointToScreen(e.GetPosition(this));
                double delta = ((p.X - startX) + (p.Y - startY)) / 2.0;
                // 카드 폭 기준으로 환산 - 그립이 손끝을 자연스럽게 따라온다
                double sxg, syg;
                Dock.GetDpiScale(this, out sxg, out syg);
                double next = startScale + (delta / Math.Max(sxg, syg)) / CardWidth;   // 물리 -> DIP
                SetScale(next, save: false);
            };
            grip.MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                grip.ReleaseMouseCapture();
                _cfg.Save();
            };
            // Alt+Tab, 화면 잠금, UAC 창 등으로 마우스 캡처를 빼앗기면 ButtonUp 이 오지 않는다.
            // 그대로 두면 다음에 그립 위를 지나가기만 해도 크기가 바뀐다.
            grip.LostMouseCapture += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                _cfg.Save();
            };

            return grip;
        }

        private static void AddGripDot(Canvas c, double x, double y)
        {
            var d = new Ellipse { Width = 2.6, Height = 2.6, Fill = Palette.GripDot };
            Canvas.SetLeft(d, x);
            Canvas.SetTop(d, y);
            c.Children.Add(d);
        }

        private void SetScale(double v, bool save)
        {
            if (double.IsNaN(v)) return;
            if (v < Config.MinScale) v = Config.MinScale;
            if (v > Config.MaxScale) v = Config.MaxScale;
            if (Math.Abs(v - _cfg.Scale) < 0.002) return;

            _cfg.Scale = v;
            _scale.ScaleX = v;
            _scale.ScaleY = v;
            SyncPanelChrome();
            ClampToScreen();
            if (save) _cfg.Save();
        }

        // ---------- 메뉴 ----------

        private void ShowSymbolMenu(UIElement target)
        {
            var m = NewMenu();
            var cur = CurrentDef;
            foreach (var def in _cfg.Symbols)
            {
                var def2 = def;
                var mi = NewItem(def.Label);
                mi.IsCheckable = true;
                mi.IsChecked = (cur != null && cur.Key == def.Key) && !_cfg.Expanded;
                mi.Click += (s, e) =>
                {
                    _cfg.Symbol = def2.Key;
                    _cfg.Expanded = false;
                    _cfg.Save();
                    ApplyLayoutMode();
                    RefreshAll();
                };
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());

            var all = NewItem(_cfg.Expanded ? "접기" : "전체 펼쳐보기");
            all.Click += (s, e) =>
            {
                _cfg.Expanded = !_cfg.Expanded;
                _cfg.Save();
                ApplyLayoutMode();
                RefreshAll();
            };
            m.Items.Add(all);

            Popup(m, target);
        }

        private void ShowBankMenu(UIElement target)
        {
            var m = NewMenu();
            AddBank(m, "HANA", "하나은행 고시");
            AddBank(m, "SHB", "신한은행 고시");
            Popup(m, target);
        }

        private void AddBank(ContextMenu m, string key, string label)
        {
            var mi = NewItem(label);
            mi.IsCheckable = true;
            mi.IsChecked = _cfg.Bank == key;
            mi.Click += (s, e) =>
            {
                if (_cfg.Bank == key) return;
                _cfg.Bank = key;
                _cfg.Save();
                // 은행이 바뀌면 환율 값이 달라지므로 즉시 다시 받는다
                _quotes.Remove("USD");
                _quotes.Remove("JPY");
                RefreshAll();
                RequestRefresh();
            };
            m.Items.Add(mi);
        }

        private ContextMenu BuildContextMenu() { return BuildContextMenu(null); }

        /// <param name="owner">이 메뉴가 달린 조각 창. 본 창이면 null.</param>
        private ContextMenu BuildContextMenu(PanelWindow owner)
        {
            var m = NewMenu();

            // ★ 붙어 있을 때만 뜻이 있는 항목 ★
            //   조각 창에서만 낸다. 본 창의 시세 바는 값이 여러 줄이라 판이 있어야 읽힌다.
            if (owner != null && owner.Key != "즐겨찾기")
            {
                var me = owner;
                var clear = NewItem("바 배경 없애기");
                clear.IsCheckable = true;
                clear.IsChecked = _cfg.ClearBars.Contains(me.Key);
                clear.Click += delegate
                {
                    if (clear.IsChecked)
                    {
                        if (!_cfg.ClearBars.Contains(me.Key)) _cfg.ClearBars.Add(me.Key);
                    }
                    else _cfg.ClearBars.Remove(me.Key);

                    me.ClearBackdrop = clear.IsChecked;
                    _cfg.Save();
                };
                m.Items.Add(clear);
                m.Items.Add(new Separator());
            }

            var refresh = NewItem("새로고침");
            refresh.Click += (s, e) => RequestRefresh();
            m.Items.Add(refresh);
            m.Items.Add(new Separator());

            var top = NewItem("항상 위에 표시");
            top.IsCheckable = true;
            top.IsChecked = _cfg.Topmost;
            top.Click += (s, e) =>
            {
                _cfg.Topmost = top.IsChecked;
                Topmost = top.IsChecked;
                SyncPanelChrome();
                _cfg.Save();
            };
            m.Items.Add(top);

            // 섹션별 접기. 접힌 섹션은 조회도 멈춘다.
            var showQ = NewItem("시세 표시");
            showQ.IsCheckable = true;
            showQ.IsChecked = _cfg.ShowQuotes;
            showQ.Click += (s, e) =>
            {
                // 메뉴에서 끄는 것은 '닫기' 다. 잠깐 접어두는 것은 머리의 − 버튼이 한다.
                _cfg.ShowQuotes = showQ.IsChecked;
                _cfg.QuotesClosed = !showQ.IsChecked;
                ApplyMinimized();
                _cfg.Save();
                if (showQ.IsChecked) RequestQuoteRefresh();   // 다시 펴면 바로 받아온다
            };
            m.Items.Add(showQ);

            var showW = NewItem("날씨 표시");
            showW.IsCheckable = true;
            showW.IsChecked = _cfg.ShowWeather;
            showW.Click += (s, e) =>
            {
                _cfg.ShowWeather = showW.IsChecked;
                _cfg.WeatherClosed = !showW.IsChecked;
                ApplyMinimized();
                _cfg.Save();
                if (showW.IsChecked) RequestWeatherRefresh();
            };
            m.Items.Add(showW);

            var clock = NewItem("시계 표시");
            clock.IsCheckable = true;
            clock.IsChecked = _cfg.ShowClock;
            clock.Click += (s, e) =>
            {
                _cfg.ShowClock = clock.IsChecked;
                _cfg.ClockClosed = !clock.IsChecked;
                ApplyMinimized();
                _cfg.Save();
            };
            m.Items.Add(clock);

            var mini = NewItem("위젯 최소화");
            mini.Click += (s, e) => ToggleMinimized();
            m.Items.Add(mini);

            var auto = NewItem("Windows 시작 시 자동 실행");
            auto.IsCheckable = true;
            auto.IsChecked = Startup.IsEnabled();
            auto.Click += (s, e) => Startup.Set(auto.IsChecked);
            m.Items.Add(auto);

            var noti = NewItem("새 버전 알림");
            noti.IsCheckable = true;
            noti.IsChecked = _cfg.NotifyUpdate;
            noti.Click += (s, e) =>
            {
                _cfg.NotifyUpdate = noti.IsChecked;
                ApplyNotice(null);
                _cfg.Save();
            };
            m.Items.Add(noti);
            m.Items.Add(new Separator());

            // 갱신 주기
            var qi = NewItem("시세 갱신 주기");
            AddInterval(qi, 1, "1초"); AddInterval(qi, 5, "5초"); AddInterval(qi, 10, "10초");
            AddInterval(qi, 30, "30초"); AddInterval(qi, 60, "1분"); AddInterval(qi, 300, "5분");
            m.Items.Add(qi);

            var wi = NewItem("날씨 갱신 주기");
            AddWeatherInterval(wi, 300, "5분"); AddWeatherInterval(wi, 600, "10분");
            AddWeatherInterval(wi, 1800, "30분"); AddWeatherInterval(wi, 3600, "1시간");
            m.Items.Add(wi);

            // 크기
            // ★ 크기도 연 창의 것이다 ★
            //   조각 창은 제 배율을 따로 갖는다(WeatherScale 등). 그런데 이 메뉴는 늘
            //   카드 배율(_cfg.Scale)을 건드려서, 조각에서 열어도 본 창만 커졌다.
            //   투명도는 다르다 - SyncPanelChrome 이 조각까지 전파하므로 본래 전역이 맞다.
            var sz = NewItem("크기");
            AddScale(sz, owner, 0.80, "80%"); AddScale(sz, owner, 0.90, "90%");
            AddScale(sz, owner, 1.00, "100%"); AddScale(sz, owner, 1.20, "120%  (기본)");
            AddScale(sz, owner, 1.40, "140%"); AddScale(sz, owner, 1.60, "160%");
            AddScale(sz, owner, 1.80, "180%");
            m.Items.Add(sz);

            // 어두운 카드는 밝은 배경 위에서 실제 수치보다 더 투명해 보인다.
            // 그래서 불투명한 쪽을 촘촘하게 나눠 뒀다.
            var op = NewItem("투명도");
            AddOpacity(op, 1.00, "100%  (불투명)");
            AddOpacity(op, 0.97, "97%"); AddOpacity(op, 0.94, "94%");
            AddOpacity(op, 0.90, "90%"); AddOpacity(op, 0.85, "85%");
            AddOpacity(op, 0.80, "80%"); AddOpacity(op, 0.70, "70%");
            AddOpacity(op, 0.60, "60%"); AddOpacity(op, 0.50, "50%");
            AddOpacity(op, 0.40, "40%"); AddOpacity(op, 0.30, "30%");
            m.Items.Add(op);
            m.Items.Add(new Separator());

            var reset = NewItem("위치 초기화 (우측 상단)");
            reset.Click += (s, e) =>
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - (CardWidth + ShadowMargin * 2) * _cfg.Scale;
                Top = wa.Top;
                SavePlacement();
            };
            m.Items.Add(reset);

            var open = NewItem("설정 파일 열기");
            open.Click += (s, e) =>
            {
                _cfg.Save();
                try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + _cfg.Path + "\"") { UseShellExecute = true }); }
                catch { }
            };
            m.Items.Add(open);

            var about = NewItem("정보");
            about.Click += (s, e) => AboutWindow.ShowSingle(this, Program.BaseDir, _latestVersion, _cfg, delegate
            {
                // 키가 바뀌면 바로 다시 받아본다. 금리 항목이 '연결 실패' 로 남아 있지 않게.
                Sources.EcosKey = _cfg.EcosKey;
                RequestQuoteRefresh();
            });
            m.Items.Add(about);
            m.Items.Add(new Separator());

            var split = NewItem("창 나누기");
            split.IsCheckable = true;
            split.IsChecked = _cfg.Separated;
            split.Click += (s, e) =>
            {
                _cfg.Separated = split.IsChecked;
                ApplySeparation();
                _cfg.Save();
            };
            m.Items.Add(split);

            var showApps = NewItem("즐겨찾기 표시");
            showApps.IsCheckable = true;
            showApps.IsChecked = _cfg.ShowApps;
            showApps.Click += (s, e) =>
            {
                _cfg.ShowApps = showApps.IsChecked;
                _cfg.AppsClosed = !showApps.IsChecked;
                ApplyMinimized();
                _cfg.Save();
            };
            m.Items.Add(showApps);

            var surge = NewItem("급등·급락 알림");
            surge.IsCheckable = true;
            surge.IsChecked = _cfg.SurgeAlert;
            surge.Click += (s, e) => { _cfg.SurgeAlert = surge.IsChecked; _cfg.Save(); };
            m.Items.Add(surge);

            // ★ 연 창을 뗀다 ★
            //   조각 창에도 저마다 Undock() 이 있는데 여기서는 늘 본 창 것을 불렀다.
            //   그래서 즐겨찾기 바에서 '떼기' 를 누르면 엉뚱하게 시세 창이 떨어졌다.
            var owner2 = owner;
            var undock = NewItem("가장자리에서 떼기");
            undock.Click += delegate
            {
                if (owner2 != null) owner2.Undock();
                else Undock();
            };
            m.Items.Add(undock);

            // ★ 조각 창에서 '종료' 는 그 조각만 닫는다 ★
            //   조각 창의 메뉴에서 누른 '종료' 가 앱 전체를 끄면 뜻이 어긋난다 -
            //   눈앞의 창을 닫으려던 것이지 앱을 끄려던 것이 아니다.
            //   앱을 끄는 길은 같은 메뉴 아래 '앱 종료' 로 따로 남겨 둔다.
            if (owner != null)
            {
                var me3 = owner;
                var shut = NewItem(me3.Key + " 닫기 (우클릭 메뉴로 다시 열기)");
                shut.Click += delegate { CloseSection(me3.Key); };
                m.Items.Add(shut);
            }

            var quit = NewItem(owner != null ? "앱 종료" : "종료");
            quit.Click += (s, e) => Close();
            m.Items.Add(quit);

            // 메뉴가 열릴 때마다 체크 상태를 실제 값으로 맞춘다
            m.Opened += (s, e) =>
            {
                // 붙어 있는지도 연 창 기준으로 본다 (PanelWindow 에는 Docked 가 없다)
                bool onEdge = (owner2 != null) ? (owner2.Edge != DockEdge.None) : Docked;
                undock.Visibility = onEdge ? Visibility.Visible : Visibility.Collapsed;
                surge.IsChecked = _cfg.SurgeAlert;
                showApps.IsChecked = _cfg.ShowApps;
                split.IsChecked = _cfg.Separated;
                top.IsChecked = _cfg.Topmost;
                showQ.IsChecked = _cfg.ShowQuotes;
                showW.IsChecked = _cfg.ShowWeather;
                clock.IsChecked = _cfg.ShowClock;
                auto.IsChecked = Startup.IsEnabled();
                SyncChecks(qi, _cfg.QuoteIntervalSec);
                SyncChecks(wi, _cfg.WeatherIntervalSec);
                SyncDoubleChecks(sz, owner2 != null ? owner2.Scale : _cfg.Scale);
                SyncDoubleChecks(op, _cfg.Opacity);
            };
            return m;
        }

        private void AddInterval(MenuItem parent, int sec, string label)
        {
            var mi = NewItem(label);
            mi.IsCheckable = true;
            mi.Tag = sec;
            mi.IsChecked = _cfg.QuoteIntervalSec == sec;
            mi.Click += (s, e) =>
            {
                _cfg.QuoteIntervalSec = sec;
                _cfg.Save();
                RequestRefresh();
            };
            parent.Items.Add(mi);
        }

        private void AddWeatherInterval(MenuItem parent, int sec, string label)
        {
            var mi = NewItem(label);
            mi.IsCheckable = true;
            mi.Tag = sec;
            mi.IsChecked = _cfg.WeatherIntervalSec == sec;
            mi.Click += (s, e) =>
            {
                _cfg.WeatherIntervalSec = sec;
                _cfg.Save();
                RequestRefresh();
            };
            parent.Items.Add(mi);
        }

        /// <param name="owner">이 메뉴가 달린 조각 창. 본 창이면 null.</param>
        private void AddScale(MenuItem parent, PanelWindow owner, double v, string label)
        {
            var me = owner;
            var mi = NewItem(label);
            mi.IsCheckable = true;
            mi.Tag = v;
            mi.IsChecked = Math.Abs((me != null ? me.Scale : _cfg.Scale) - v) < 0.005;
            mi.Click += delegate
            {
                if (me == null) { SetScale(v, save: true); return; }

                me.SetScale(v);
                SavePanelScale(me.Key, v);   // 조각은 제 배율을 따로 기억한다
                _cfg.Save();
            };
            parent.Items.Add(mi);
        }

        /// <summary>조각 창 배율을 설정에 남긴다. 크기 손잡이(_onScaled)와 같은 자리를 쓴다.</summary>
        private void SavePanelScale(string key, double v)
        {
            if (key == "날씨") _cfg.WeatherScale = v;
            else if (key == "즐겨찾기") _cfg.AppsScale = v;
            else if (key == "시계") _cfg.ClockScale = v;
        }

        private void AddOpacity(MenuItem parent, double v, string label)
        {
            var mi = NewItem(label);
            mi.IsCheckable = true;
            mi.Tag = v;
            mi.IsChecked = Math.Abs(_cfg.Opacity - v) < 0.005;
            mi.Click += (s, e) =>
            {
                _cfg.Opacity = v;
                Opacity = v;
                SyncPanelChrome();
                _cfg.Save();
            };
            parent.Items.Add(mi);
        }

        private static void SyncChecks(MenuItem parent, int value)
        {
            foreach (var o in parent.Items)
            {
                var mi = o as MenuItem;
                if (mi != null && mi.Tag is int) mi.IsChecked = (int)mi.Tag == value;
            }
        }

        private static void SyncDoubleChecks(MenuItem parent, double value)
        {
            foreach (var o in parent.Items)
            {
                var mi = o as MenuItem;
                if (mi != null && mi.Tag is double) mi.IsChecked = Math.Abs((double)mi.Tag - value) < 0.005;
            }
        }

        // 색·모양은 Theme 의 스타일이 담당한다 (여기서 인라인으로 지정하지 않는다)
        private static ContextMenu NewMenu()
        {
            return new ContextMenu { FontFamily = new FontFamily("Segoe UI, Malgun Gothic") };
        }

        private static MenuItem NewItem(string header)
        {
            return new MenuItem { Header = header };
        }

        private void Popup(ContextMenu m, UIElement target)
        {
            m.PlacementTarget = target;
            m.Placement = PlacementMode.Bottom;
            m.IsOpen = true;
        }

        // ---------- 표시 갱신 ----------

        /// <summary>
        /// 지금 실제로 조회할 필요가 있는가.
        /// 섹션을 접었거나 위젯을 최소화한 상태에서는 화면에 쓰이지 않으므로 호출하지 않는다.
        /// </summary>
        /// <summary>모니터 가장자리에 붙어 있는가.</summary>
        private bool Docked { get { return _cfg.DockedEdge != DockEdge.None; } }

        // 붙어 있을 때는 접기·최소화와 무관하게 둘 다 받아온다. 바에 실을 값이 필요하다.
        //
        // ★ 닫은 섹션은 예외다 ★
        //   접기는 '잠깐 안 보기' 라 값은 계속 받아 둔다. 닫기는 '안 쓴다' 는 뜻이므로
        //   조회도 멈춘다 - 안 보는 것 때문에 남의 서버를 두드릴 이유가 없다.
        private bool QuotesActive
        {
            get { return !_cfg.QuotesClosed && (Docked || (_cfg.ShowQuotes && !_cfg.Minimized)); }
        }
        private bool WeatherActive
        {
            get { return !_cfg.WeatherClosed && (Docked || (_cfg.ShowWeather && !_cfg.Minimized)); }
        }

        private void ToggleMinimized()
        {
            _cfg.Minimized = !_cfg.Minimized;
            ApplyMinimized();
            _cfg.Save();
            if (!_cfg.Minimized) RequestRefresh();   // 다시 펴면 바로 최신값을 받아온다
        }

        /// <summary>
        /// 최소화하면 라벨까지 전부 감추고 복원 버튼 하나만 남긴다.
        /// 카드 폭 고정도 풀어서 창 자체가 버튼 크기로 줄어들게 한다.
        /// </summary>
        private void ApplyMinimized()
        {
            bool m = _cfg.Minimized;
            bool q = !m && _cfg.ShowQuotes;    // 시세 섹션
            bool w = SectionOn(_cfg.ShowWeather);   // 날씨 섹션 (떼어냈으면 본 창과 무관하다)

            var v = q ? Visibility.Visible : Visibility.Collapsed;
            var wv = w ? Visibility.Visible : Visibility.Collapsed;

            if (_bodyHost != null) _bodyHost.Visibility = v;
            // 시세를 접으면 헤더 대신 가운데 정렬된 얇은 줄만 남긴다 (날씨와 같은 모양).
            // 최소화 상태에서는 복원 버튼이 헤더에 있으므로 그때는 남겨둔다.
            if (_headerRow != null)
                _headerRow.Visibility = (m || q) ? Visibility.Visible : Visibility.Collapsed;
            if (_weatherRow != null) _weatherRow.Visibility = wv;

            // 섹션을 접으면 다시 펼 수 있는 얇은 줄을 대신 남긴다
            // 닫은 섹션은 '펴기' 줄조차 남기지 않는다. 우클릭 메뉴로만 되살린다.
            bool weatherBarOn = !m && !_cfg.ShowWeather && !_cfg.WeatherClosed;
            bool quotesBarOn = !m && !_cfg.ShowQuotes && !_cfg.QuotesClosed;
            if (_weatherBar != null)
                _weatherBar.Visibility = weatherBarOn ? Visibility.Visible : Visibility.Collapsed;
            if (_quotesBar != null)
                _quotesBar.Visibility = quotesBarOn ? Visibility.Visible : Visibility.Collapsed;

            // 구분선은 위아래가 다 있을 때만
            if (_dividerEl != null)
                _dividerEl.Visibility = ((q || quotesBarOn) && (w || weatherBarOn))
                                      ? Visibility.Visible : Visibility.Collapsed;
            // 접혀 있어도 라벨은 남겨야 거기를 눌러 다시 펼 수 있다
            if (_symBtn != null) _symBtn.Visibility = m ? Visibility.Collapsed : Visibility.Visible;
            if (_srcBtn != null) _srcBtn.Visibility = v;
            if (_quoteRefresh != null) _quoteRefresh.Visibility = v;

            if (_minBtn != null)
            {
                if (m) { _minBtn.Text = "▢"; _minBtn.ToolTip = "펼치기"; }
                else if (!_cfg.ShowQuotes) { _minBtn.Text = "+"; _minBtn.ToolTip = "시세 펴기"; }
                else { _minBtn.Text = "─"; _minBtn.ToolTip = "시세 접기"; }
                _minBtn.Visibility = Visibility.Visible;
                _minBtn.Margin = m ? new Thickness(0) : new Thickness(7, 0, 0, 0);
                _minBtn.FontSize = m ? 11 : 12.5;
            }

            if (_card != null)
            {
                // 폭 고정을 풀면 남은 버튼 크기에 맞춰 창이 줄어든다
                ApplyCardWidth();
                // 여백을 조금 남겨야 최소화 상태에서도 잡아서 옮길 수 있다
                _card.Padding = m ? new Thickness(9, 6, 9, 6) : new Thickness(CardPadX, 10, CardPadX, 11);
                _card.CornerRadius = new CornerRadius(m ? 9 : 16);
            }

            if (_bottomGrip != null)
                _bottomGrip.Visibility = (q && _cfg.Expanded) ? Visibility.Visible : Visibility.Collapsed;

            ApplyCardWidth();
            ApplyClockVisibility();   // 최소화하면 시계 타이머도 멈춘다
            ApplyAppsVisibility();
            SyncPanelVisibility();
            ApplyNotice(null);        // 최소화하면 공지 줄도 접는다
        }

        private void ApplyLayoutMode()
        {
            bool ex = _cfg.Expanded;
            _collapsedBody.Visibility = ex ? Visibility.Collapsed : Visibility.Visible;
            _expandedBody.Visibility = ex ? Visibility.Visible : Visibility.Collapsed;
            _symbolCaret.Text = ex ? "▴" : "▾";
            // 개수 조절 손잡이는 펼쳤을 때만 의미가 있다
            if (_bottomGrip != null)
                _bottomGrip.Visibility = (ex && !_cfg.Minimized) ? Visibility.Visible : Visibility.Collapsed;
            ApplyClockVisibility();   // 카운트다운 타이머 조건도 펼침 여부를 본다
            RefreshHeader();
        }

        private void RefreshAll()
        {
            RefreshHeader();
            RefreshQuote();
            RefreshSymbolViews();
            RefreshWeather();
            RefreshPanelBars();
        }

        private void RefreshHeader()
        {
            var def = CurrentDef;
            bool folded = !_cfg.ShowQuotes && !_cfg.Minimized;
            SetText(_symbolLabel, (folded || _cfg.Expanded || def == null) ? "시세" : def.Header);

            // 전환 버튼·상태등·카운트다운은 펼쳤을 때만 의미가 있다
            bool showExtras = _cfg.Expanded && !_cfg.Minimized && _cfg.ShowQuotes;
            var extraVis = showExtras ? Visibility.Visible : Visibility.Collapsed;
            if (_viewToggle != null) _viewToggle.Visibility = extraVis;
            if (_statusDot != null) _statusDot.Visibility = extraVis;
            if (_countdown != null) _countdown.Visibility = extraVis;
            UpdateStatusDot();
            UpdateCountdown();

            // 은행 선택은 환율 종목에서만 의미가 있다
            bool bankable = _cfg.Expanded || (def != null && def.BankSwitchable);
            _sourceCaret.Visibility = bankable ? Visibility.Visible : Visibility.Collapsed;

            if (_cfg.Expanded || def == null)
            {
                SetText(_sourceLabel, _cfg.Bank == "SHB" ? "신한은행" : "하나은행");
                SetText(_timeLabel, IsStale ? "지연" : "");
                ApplyStaleStyle();
                return;
            }

            Quote q;
            if (_quotes.TryGetValue(def.Key, out q) && q.Ok)
            {
                SetText(_sourceLabel, q.Source ?? "");
                SetText(_timeLabel, (q.Time ?? "") + (IsStale ? " 지연" : ""));
            }
            else
            {
                SetText(_sourceLabel, bankable ? (_cfg.Bank == "SHB" ? "신한은행" : "하나은행") : "");
                SetText(_timeLabel, IsStale ? "지연" : "");
            }
            ApplyStaleStyle();
        }

        /// <summary>
        /// 값이 낡았는가. 표시된 숫자는 그대로인데 실제로는 한참 전 값인 상황을 사용자가 알 수 있어야 한다.
        /// (네이버는 비공식 API 라 어느 날 갑자기 응답 구조가 바뀔 수 있다)
        /// </summary>
        private bool IsStale
        {
            get
            {
                if (_lastQuoteOkAt == DateTime.MinValue) return false;   // 아직 첫 수신 전
                return (DateTime.UtcNow - _lastQuoteOkAt).TotalSeconds > _cfg.QuoteIntervalSec * 2.5 + 30;
            }
        }

        private void ApplyStaleStyle()
        {
            bool stale = IsStale;
            SetBrush(_timeLabel, stale ? Palette.Stale : Palette.TextGhost);
            if (stale)
            {
                int min = (int)(DateTime.UtcNow - _lastQuoteOkAt).TotalMinutes;
                _timeLabel.ToolTip = "마지막 갱신 후 " + min + "분 경과 — 시세를 받아오지 못하고 있습니다";
            }
            else if (_timeLabel.ToolTip != null) _timeLabel.ToolTip = null;
        }

        private void RefreshQuote()
        {
            if (_cfg.Expanded) return;

            var cur = CurrentDef;
            if (cur == null) return;

            Quote q;
            if (!_quotes.TryGetValue(cur.Key, out q) || !q.Ok)
            {
                SetText(_price, "- - - -");
                SetText(_diff, _quotes.ContainsKey(cur.Key) ? "연결 실패" : "");
                SetBrush(_diff, Palette.Flat);
                return;
            }

            SetText(_price, q.Price);
            SetBrush(_diff, Palette.ForDir(q.Dir));

            string suffix = q.RatioSuffix ?? "";
            string arrow = q.Dir > 0 ? "▲" : (q.Dir < 0 ? "▼" : "–");
            string text = q.Diff == null
                ? (q.Ratio == null ? "" : q.Ratio + suffix)
                : arrow + " " + q.Diff + "   " + (q.Ratio ?? "") + suffix;
            SetText(_diff, text);
            if (string.IsNullOrEmpty(suffix)) SetBrush(_diff, Palette.TextDim);   // 날씨는 등락 색을 쓰지 않는다
        }

        private void RefreshSymbolViews()
        {
            if (!_cfg.Expanded) return;

            var views = _cfg.GridView ? _tiles : _rows;
            var cur = CurrentDef;
            string curKey = cur == null ? null : cur.Key;

            foreach (var v in views)
            {
                Quote q;
                bool ok = _quotes.TryGetValue(v.Def.Key, out q) && q.Ok;

                bool isCurrent = v.Def.Key == curKey;
                SetBrush(v.Name, isCurrent ? Palette.Text : Palette.TextDim);
                SetWeight(v.Name, isCurrent ? FontWeights.SemiBold : FontWeights.Normal);

                if (!ok)
                {
                    SetText(v.Price, "- -");
                    SetText(v.Ratio, "");
                    SetBrush(v.Ratio, Palette.Flat);
                    continue;
                }
                SetText(v.Price, q.Price);
                SetText(v.Ratio, (q.Ratio ?? "") + (q.RatioSuffix ?? ""));
                SetBrush(v.Ratio, string.IsNullOrEmpty(q.RatioSuffix) ? Palette.TextDim : Palette.ForDir(q.Dir));
            }
        }

        private void RefreshWeather()
        {
            RefreshDockBar();
            foreach (var v in _weatherViews)
            {
                WeatherInfo w;
                bool has = _weatherData.TryGetValue(v.Def.Key, out w);
                if (!has || !w.Ok)
                {
                    SetText(v.Temp, "- -");
                    SetText(v.Desc, "");
                    if (v.City != null) SetText(v.City, v.Def.Label);
                    SetText(v.Sub, has ? "날씨 연결 실패" : "불러오는 중");
                    continue;
                }

                WeatherIcon.Draw(v.Icon, w.Code, w.IsDay);
                SetText(v.Temp, w.Temp.ToString("0.#", CultureInfo.InvariantCulture) + "°");
                SetText(v.Desc, WeatherIcon.Describe(w.Code));

                string detail = string.Format(CultureInfo.InvariantCulture,
                    "체감 {0:0}° · {1:0}°/{2:0}° · 습도 {3}%",
                    w.Feels, w.Max, w.Min, w.Hum);

                if (v.City != null)
                {
                    // 큰 카드는 지역 이름을 따로 보여주므로 밑줄에는 넣지 않는다
                    SetText(v.City, v.Def.Label);
                    SetText(v.Sub, detail);
                }
                else
                {
                    string city = string.IsNullOrEmpty(v.Def.Label) ? "" : v.Def.Label + " · ";
                    SetText(v.Sub, city + detail);
                }
            }
        }

        private static void SetText(TextBlock tb, string v)
        {
            v = v ?? "";
            if (!string.Equals(tb.Text, v, StringComparison.Ordinal)) tb.Text = v;
        }

        // 브러시/굵기도 같은 값이면 다시 넣지 않는다. 대입할 때마다 렌더가 무효화되기 때문이다.
        private static void SetBrush(TextBlock tb, Brush b)
        {
            if (!ReferenceEquals(tb.Foreground, b)) tb.Foreground = b;
        }

        private static void SetWeight(TextBlock tb, FontWeight w)
        {
            if (tb.FontWeight != w) tb.FontWeight = w;
        }

        // ---------- 링크 ----------

        private void OpenQuoteLink(string symbolKey)
        {
            Quote q;
            if (_quotes.TryGetValue(symbolKey, out q) && q.Ok && !string.IsNullOrEmpty(q.Link))
            {
                Net.OpenLink(q.Link);
                return;
            }
            // 아직 값을 못 받았어도 종목 페이지는 열어준다
            SymbolDef def = null;
            foreach (var d in _cfg.Symbols) if (d.Key == symbolKey) { def = d; break; }
            if (def == null) return;

            switch (def.Kind)
            {
                case SourceKind.Fx:
                    Net.OpenLink("https://m.stock.naver.com/marketindex/exchange/" +
                                 def.Code + (_cfg.Bank == "SHB" ? "_SHB" : ""));
                    break;
                case SourceKind.Index:
                    Net.OpenLink("https://m.stock.naver.com/domestic/index/" + def.Code + "/total");
                    break;
                case SourceKind.DomesticStock:
                    Net.OpenLink("https://m.stock.naver.com/domestic/stock/" + def.Code + "/total");
                    break;
                case SourceKind.WorldStock:
                    Net.OpenLink("https://m.stock.naver.com/worldstock/stock/" + def.Code + "/total");
                    break;
                case SourceKind.Coin:
                    string t = def.Code.StartsWith("KRW-", StringComparison.Ordinal)
                             ? def.Code.Substring(4) : def.Code;
                    Net.OpenLink("https://stock.naver.com/crypto/UPBIT/" + t + "/price");
                    break;
                case SourceKind.Weather:
                    Net.OpenLink(string.IsNullOrEmpty(def.Code)
                               ? "https://weather.naver.com/"
                               : "https://weather.naver.com/today/" + def.Code);
                    break;
                case SourceKind.Ecos:
                    Net.OpenLink("https://ecos.bok.or.kr/");
                    break;
            }
        }

        private void OpenWeatherLink(SymbolDef def)
        {
            if (def != null && !string.IsNullOrEmpty(def.Code))
            {
                Net.OpenLink("https://weather.naver.com/today/" + def.Code);
                return;
            }
            if (def != null && !string.IsNullOrEmpty(def.Label))
            {
                Net.OpenLink("https://search.naver.com/search.naver?query=" +
                             Uri.EscapeDataString(def.Label + " 날씨"));
                return;
            }
            Net.OpenLink("https://weather.naver.com/");
        }

        // ---------- 데이터 루프 ----------

        private void RequestRefresh()
        {
            _forceQuote = true;
            _forceWeather = true;
            Wake();
        }

        private void RequestQuoteRefresh()
        {
            _scrollOffset = 0;      // 새로고침하면 목록 맨 위로 돌아온다
            ApplyVisibleLimit();
            _forceQuote = true;
            Wake();
        }

        private void RequestWeatherRefresh() { _forceWeather = true; Wake(); }

        /// <summary>대기 중인 루프를 즉시 깨운다. 세마포어 상한(1)을 넘으면 예외가 나므로 삼킨다.</summary>
        private void Wake()
        {
            try { _wake.Release(); } catch { }
        }

        /// <summary>
        /// 새로고침 아이콘. 누르면 한 바퀴 돌면서 해당 항목만 다시 받아온다.
        /// </summary>
        private TextBlock MakeRefreshButton(string tip, Action onClick)
        {
            return MakeIconButton("↻", tip, true, onClick);
        }

        private TextBlock MakeIconButton(string glyph, string tip, bool spin, Action onClick)
        {
            var rot = new RotateTransform(0);
            var t = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"),
                FontSize = 12.5,
                Foreground = Palette.IconIdle,
                Background = Palette.Clear,   // 히트 테스트를 받으려면 필요하다
                Cursor = Cursors.Hand,
                ToolTip = tip,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = rot,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            t.MouseEnter += (s, e) => t.Foreground = Palette.IconHover;
            t.MouseLeave += (s, e) => t.Foreground = Palette.IconIdle;
            t.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;   // 창 드래그로 넘어가지 않게 막는다
                if (spin)
                {
                    var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(650)));
                    anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                    rot.BeginAnimation(RotateTransform.AngleProperty, anim);
                }
                onClick();
            };
            return t;
        }

        private async void StartLoop()
        {
            try { await Loop(_cts.Token); }
            catch (OperationCanceledException) { }
            catch { }
        }

        private async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Tick(ct); }
                catch (OperationCanceledException)
                {
                    // 종료일 때만 루프를 끝낸다. 타임아웃 때문에 루프가 죽으면 복구 수단이 없다.
                    if (ct.IsCancellationRequested) throw;
                }
                catch { }

                // 다음 작업까지 남은 시간만큼만 잔다. 새로고침 요청이 오면 즉시 깨어난다.
                int waitMs = NextWaitMs();

                // 유휴 구간이 충분히 길면, 화면 갱신이 자리를 잡은 뒤에 워킹셋을 반환한다.
                if (waitMs > 20000)
                {
                    try { await Task.Delay(4000, ct); }
                    catch (OperationCanceledException) { throw; }
                    waitMs -= 4000;
                    TrimMemory();
                }
                else if ((DateTime.UtcNow - _lastTrimAt).TotalSeconds >= TrimEverySec)
                {
                    // 갱신 주기가 짧으면 유휴 구간이 없어 위 조건에 영영 걸리지 않는다.
                    // 그대로 두면 1초 주기에서 워킹셋이 10배 가까이 부풀어 있게 된다.
                    TrimMemory();
                }

                try { await _wake.WaitAsync(waitMs, ct); }
                catch (OperationCanceledException) { throw; }
            }
        }

        private int NextWaitMs()
        {
            if (_forceQuote || _forceWeather) return 0;

            // 접어둔 섹션은 대기 시간 계산에서도 뺀다
            double next = double.MaxValue;
            if (QuotesActive)
                next = Math.Min(next, _cfg.QuoteIntervalSec - (DateTime.UtcNow - _lastQuoteAt).TotalSeconds);
            if (WeatherActive)
                next = Math.Min(next, _cfg.WeatherIntervalSec - (DateTime.UtcNow - _lastWeatherAt).TotalSeconds);
            if (next == double.MaxValue) next = 600;   // 전부 접혀 있으면 길게 잔다
            if (next < 1) next = 1;
            if (next > 300) next = 300;      // 설정 변경을 반영하기 위한 상한
            return (int)(next * 1000);
        }

        private async Task Tick(CancellationToken ct)
        {
            bool forceQuote = _forceQuote;
            bool forceWeather = _forceWeather;
            _forceQuote = false;
            _forceWeather = false;

            await EnsureLocation(ct);

            // 접어둔 섹션과 최소화 상태에서는 아예 조회하지 않는다
            if (QuotesActive &&
                (forceQuote || (DateTime.UtcNow - _lastQuoteAt).TotalSeconds >= _cfg.QuoteIntervalSec))
            {
                // 5개를 순차로 부르면 최악 50초가 걸리고, 느린 소스 하나가 나머지 전부를 붙잡는다.
                // 서로 독립적이므로 동시에 부른다.
                string bank = _cfg.Bank;
                var defs = _cfg.Symbols.ToArray();   // 받는 동안 목록이 바뀔 수 있으므로 복사해 쓴다
                var tasks = new Task<Quote>[defs.Length];
                for (int i = 0; i < defs.Length; i++) tasks[i] = Sources.FetchAsync(defs[i], bank, ct);
                await Task.WhenAll(tasks);

                bool anyOk = false;
                var surged = new List<SurgeHit>();
                for (int i = 0; i < defs.Length; i++)
                {
                    var q = tasks[i].Result;
                    if (q.Ok) { _quotes[defs[i].Key] = q; anyOk = true; }
                    else if (!_quotes.ContainsKey(defs[i].Key)) _quotes[defs[i].Key] = q;   // 첫 실패는 표시

                    if (q.Ok)
                    {
                        double pct = NoteSurge(defs[i], q);
                        if (pct != 0) surged.Add(new SurgeHit { Key = defs[i].Key, Pct = pct });
                    }
                }

                // 실패해도 '시도 시각' 은 전진시킨다.
                // 그러지 않으면 다음 대기 시간이 0 으로 계산되어 1초마다 무한 재시도한다.
                _lastQuoteAt = DateTime.UtcNow;
                _lastFetchOk = anyOk;
                if (anyOk) _lastQuoteOkAt = DateTime.UtcNow;

                // 붙어 있으면 카드는 화면에 없다. 거기까지 다시 그릴 이유가 없다.
                // (1초 주기에서 이 한 줄이 가장 크게 아낀다)
                if (Docked) RefreshDockBar();
                else
                {
                    RefreshHeader();
                    RefreshQuote();
                    RefreshSymbolViews();
                }
                FlashSurge(surged);
            }

            if (WeatherActive &&
                (forceWeather || (DateTime.UtcNow - _lastWeatherAt).TotalSeconds >= _cfg.WeatherIntervalSec))
            {
                var wdefs = _cfg.Weathers.ToArray();
                if (wdefs.Length > 0)
                {
                    var wtasks = new Task<WeatherInfo>[wdefs.Length];
                    for (int i = 0; i < wdefs.Length; i++)
                        wtasks[i] = Sources.FetchWeatherAsync(wdefs[i].Lat, wdefs[i].Lon, ct);
                    await Task.WhenAll(wtasks);

                    for (int i = 0; i < wdefs.Length; i++)
                    {
                        var w = wtasks[i].Result;
                        if (w.Ok || !_weatherData.ContainsKey(wdefs[i].Key)) _weatherData[wdefs[i].Key] = w;
                    }
                    _lastWeatherAt = DateTime.UtcNow;   // 시세와 마찬가지로 실패해도 전진시킨다
                    RefreshWeather();
                }
            }

            // 새 버전 확인 - 하루 한 번, 주소가 설정돼 있을 때만.
            // 실패는 조용히 넘긴다 (Sources 가 null 을 돌려주고 ApplyNotice 가 이전 상태를 유지한다).
            if (_cfg.NotifyUpdate && !string.IsNullOrEmpty(_cfg.UpdateUrl) &&
                (DateTime.UtcNow - _lastUpdateCheckAt).TotalHours >= UpdateCheckHours)
            {
                _lastUpdateCheckAt = DateTime.UtcNow;
                ApplyNotice(await Sources.LatestVersionAsync(_cfg.UpdateUrl, ct));
            }
            // 메모리 반환은 여기서 하지 않는다. 유휴 구간에 들어간 뒤 Loop 에서 처리한다.
        }

        private async Task EnsureLocation(CancellationToken ct)
        {
            // 날씨 목록이 이미 있으면 위치를 감지할 필요가 없다
            if (_cfg.Weathers.Count > 0) return;

            if (double.IsNaN(_cfg.Lat) || double.IsNaN(_cfg.Lon))
            {
                var geo = await Sources.DetectLocationAsync(ct);
                if (geo != null)
                {
                    _cfg.Lat = geo.Lat;
                    _cfg.Lon = geo.Lon;
                    if (string.IsNullOrEmpty(_cfg.City)) _cfg.City = geo.City;
                }
                else
                {
                    _cfg.Lat = 37.5665; _cfg.Lon = 126.9780;
                    if (string.IsNullOrEmpty(_cfg.City)) _cfg.City = "서울";
                }
                _cfg.Save();
            }

            // 네이버 날씨 링크에 쓸 지역코드를 조회한다.
            // 지역명이 영문이면 매칭되는 행정동이 없어 계속 실패하므로 실행당 한 번만 시도한다.
            if (!_areaCodeTried && string.IsNullOrEmpty(_cfg.WeatherAreaCode) && !string.IsNullOrEmpty(_cfg.City))
            {
                _areaCodeTried = true;
                var code = await Sources.FindWeatherAreaCodeAsync(_cfg.City, ct);
                if (!string.IsNullOrEmpty(code))
                {
                    _cfg.WeatherAreaCode = code;
                    _cfg.Save();
                }
            }

            // 날씨 목록이 비어 있으면 감지된 현재 위치를 첫 항목으로 넣는다
            if (_cfg.Weathers.Count == 0 && !double.IsNaN(_cfg.Lat) && !double.IsNaN(_cfg.Lon))
            {
                var w = new SymbolDef(SourceKind.Weather, _cfg.WeatherAreaCode ?? "", _cfg.City ?? "현재 위치");
                w.Lat = _cfg.Lat;
                w.Lon = _cfg.Lon;
                _cfg.Weathers.Add(w);
                _cfg.Save();
                RebuildWeatherViews();
            }
        }

        // ---------- 메모리 ----------

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        /// <summary>
        /// 유휴 구간에 들어간 뒤 물리 메모리를 반환한다.
        ///
        /// 이 위젯은 몇 분에 한 번만 일하고 나머지 시간은 대기한다. 그동안 워킹셋을 붙들고
        /// 있을 이유가 없으므로 반환해서 다른 프로그램이 쓰게 둔다.
        ///
        /// 다만 호출 시점이 중요하다. 갱신 '직후' 에 비우면 방금 만든 렌더링 자원과
        /// JIT 코드 페이지까지 밀려나 곧바로 페이지 폴트로 되읽게 된다.
        /// 그래서 UI 갱신이 끝나고 잠시 지난 뒤에 부른다 (Loop 참조).
        ///
        /// gen2 전체 수집(GC.Collect(2) + WaitForPendingFinalizers)은 하지 않는다.
        /// UI 스레드를 붙잡는 시간에 비해 얻는 게 없다. gen0/1 만 비블로킹으로 훑는다.
        /// </summary>
        private static DateTime _lastTrimAt = DateTime.MinValue;
        private const double TrimEverySec = 60;   // 주기가 짧아도 최소 이 간격으로는 반환한다

        private static void TrimMemory()
        {
            _lastTrimAt = DateTime.UtcNow;
            try
            {
                GC.Collect(1, GCCollectionMode.Optimized, false);
                using (var p = Process.GetCurrentProcess())
                    SetProcessWorkingSetSize(p.Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }

        private void Shutdown()
        {
            try { ClosePanels(); } catch { }
            try { _cts.Cancel(); } catch { }
            try { _wake.Release(); } catch { }
            try { if (_clockTimer != null) _clockTimer.Stop(); } catch { }
            try { _cfg.Save(); } catch { }
        }
    }

    /// <summary>시작 프로그램 등록 (현재 사용자 레지스트리 Run 키).</summary>
    internal static class Startup
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "오늘은";
        private const string OldName = "DeskWidget";   // 이전 이름으로 등록된 항목 정리용

        /// <summary>
        /// 시작 프로그램에 등록할 명령.
        /// PowerShell 런처로 띄운 경우 런처가 알려준 명령을 쓴다
        /// (그러지 않으면 powershell.exe 경로가 등록돼 버린다).
        /// </summary>
        private static string LaunchTarget
        {
            get
            {
                if (!string.IsNullOrEmpty(Program.LaunchCommand)) return Program.LaunchCommand;

                // exe 로 직접 실행됐더라도 옆에 런처가 있으면 그쪽을 등록한다.
                // exe 경로를 그대로 등록하면 Smart App Control 이 막는 PC 에서
                // 로그온 때마다 아무 표시 없이 실행에 실패한다.
                try
                {
                    string dir = Program.BaseDir;
                    if (!string.IsNullOrEmpty(dir))
                    {
                        // System.Windows.Shapes.Path 와 이름이 겹치므로 전체 이름으로 쓴다
                        string vbs = System.IO.Path.Combine(dir, "launch.vbs");
                        if (File.Exists(vbs))
                        {
                            string wscript = System.IO.Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.System), "wscript.exe");
                            return "\"" + wscript + "\" \"" + vbs + "\"";
                        }
                    }
                }
                catch { }

                try { return "\"" + Process.GetCurrentProcess().MainModule.FileName + "\""; }
                catch { return null; }
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key, false))
                {
                    if (k == null) return false;
                    return k.GetValue(Name) != null;
                }
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key, true))
                {
                    if (k == null) return;
                    k.DeleteValue(OldName, false);
                    if (on)
                    {
                        string cmd = LaunchTarget;
                        if (!string.IsNullOrEmpty(cmd)) k.SetValue(Name, cmd);
                    }
                    else k.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }
}
