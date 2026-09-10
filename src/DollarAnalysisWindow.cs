// 본체와 합체하거나 AppBar 자리를 확보하지 않는 독립 달러 분석 창.
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DeskWidget
{
    internal sealed class DollarAnalysisWindow : Window
    {
        private static readonly System.Collections.Generic.List<DollarAnalysisWindow> OpenWindows = new System.Collections.Generic.List<DollarAnalysisWindow>();
        private readonly PredictionTarget _target;
        private readonly TextBlock _quoteRatio;
        private readonly Button _quoteSite;
        private string _quoteSiteLink;
        private Action _fitQuoteHeader;
        private readonly Button _sparkHelp;
        private readonly TextBlock _loginHelp;
        private readonly Config _cfg;
        private readonly Func<CancellationToken, Task<DollarAnalysisResult>> _fetch;
        private readonly Func<CancellationToken, Task<bool>> _checkLogin;
        private readonly Func<CancellationToken, Task> _login;
        private readonly Func<DollarAnalysisResult, CancellationToken, Task<DollarSparkResult>> _analyze;
        private readonly Func<DateTime> _utcNow;
        private readonly DispatcherTimer _sparkTimer;
        private bool _sparkConnected;
        private bool _sparkAutoPaused;
        private CancellationTokenSource _refreshCancellation, _loginCancellation;
        private readonly Border _sparkCard;
        private string _model;
        private readonly BriefTextBlock _brief;
        private readonly TextBlock _performance;
        private readonly TextBlock _primaryStatus;
        private readonly StackPanel _primaryHost, _forecastPanel, _comparisonBody;
        private readonly ScaleTransform _textScale;
        private double _roundTrip;
        private readonly Expander _comparison;
        private readonly BriefTextBlock _comparisonSummary;
        private readonly BriefTextBlock[] _periodChecks = new BriefTextBlock[3];
        private int _sparkInterval;
        private DateTime _nextSparkAt = DateTime.MaxValue;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _busy, _closed, _appClosing, _loginBusy;
        private bool _positionedNearOwner;
        private DateTime _lastAttempt = DateTime.MinValue;
        private DollarAnalysisResult _lastRendered;
        private readonly DollarAnalysisResult[] _styleResults = new DollarAnalysisResult[2];
        private readonly ToggleButton _analysisMode;
        private readonly TextBlock _status, _price, _quoteSource, _patternInfo, _newsInfo;
        private readonly TextBlock _up, _flat, _down, _method;
        private readonly TextBlock _counterEvidence, _score, _summary, _forecastBasis, _forecastDetail, _factorDetail;
        private readonly TextBlock[] _forecastValues = new TextBlock[3], _forecastStates = new TextBlock[3];
        private readonly Expander _details;
        private readonly TextBlock[,] _periodCells = new TextBlock[3, 3];
        private readonly StackPanel _news;
        private readonly Canvas _chart;
        private readonly Button _refresh;
        private readonly TextBlock _sparkLabel;
        private readonly Ellipse _sparkDot;
        private readonly Button _sparkCountdown;
        private readonly Button _sparkLogin;
        private readonly TextBlock _sparkState;
        private readonly Expander[] _periodEvidence = new Expander[3];
        private readonly TextBlock[] _periodHeadings = new TextBlock[3], _evidenceCounts = new TextBlock[3];
        private readonly TextBlock[] _periodDirections = new TextBlock[3], _periodPoints = new TextBlock[3], _periodReasons = new TextBlock[3];
        private readonly TextBlock[] _forecastChanges = new TextBlock[3];
        private readonly StackPanel[] _evidenceLists = new StackPanel[3];

        public static void ShowSingle(Window owner, Config cfg)
        { ShowSingle(owner, cfg, new SymbolDef(SourceKind.Fx, "FX_USDKRW", "달러")); }

        public static void ShowSingle(Window owner, Config cfg, SymbolDef def)
        {
            // ★ 날씨는 예측하지 않는다 ★ (사용자 요청, 2026-09-10)
            //   창을 열면 Open-Meteo 예보를 보여 주면서 화면 문구는 시세용 그대로였다 -
            //   '새 정책·수급 확인 중', '기사 요인 기반 추론' 처럼 날씨에는 성립하지 않는 말이
            //   섞였다. 우리가 기상 예보를 하는 것도 아니고, 그렇게 보이게 두는 것이 더 나쁘다.
            //   버튼을 숨기는 것으로는 부족하다 - 더블클릭·메뉴 같은 다른 길이 또 생긴다.
            //   그래서 창을 만드는 이 문 하나에서 막는다.
            if (def == null || def.Kind == SourceKind.Weather) return;

            var open = OpenWindows.FirstOrDefault(w => w.Owner == owner && w._target.Key == def.Key);
            if (open != null)
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.PositionNearOwner(owner);
                open.Activate();
                return;
            }
            var target = new PredictionTarget(def);
            var window = new DollarAnalysisWindow(cfg, ct => PredictionData.FetchAsync(target, cfg.Bank, ct), null, null, target);
            OpenWindows.Add(window);
            window.Owner = owner;
            window.PositionNearOwner(owner);
            if (target.Dollar) cfg.ShowDollarAnalysis = true;
            cfg.Save();
            window.Closed += delegate { OpenWindows.Remove(window); };
            window.Show();
        }

        public static void CloseForOwner(Window owner)
        {
            foreach (var window in OpenWindows.Where(w => w.Owner == owner).ToArray())
            { window._appClosing = true; window.Close(); }
        }

        internal DollarAnalysisWindow(Config cfg, Func<CancellationToken, Task<DollarAnalysisResult>> fetch,
            Func<CancellationToken, Task<bool>> checkLogin = null, Func<CancellationToken, Task> login = null, PredictionTarget target = null,
            Func<DollarAnalysisResult, CancellationToken, Task<DollarSparkResult>> analyze = null, Func<DateTime> utcNow = null)
        {
            _target = target ?? new PredictionTarget(null);
            _cfg = cfg;
            _fetch = fetch;
            _checkLogin = checkLogin ?? DollarSpark.CheckLoginAsync;
            _login = login ?? DollarSpark.LoginAsync;
            _model = DollarSpark.ModelId(cfg.AnalysisModel);
            _analyze = analyze ?? ((r, ct) => DollarSpark.AnalyzeAsync(r, ct, _model));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _sparkInterval = Config.SparkInterval(cfg.SparkRefreshIntervalSec);
            _sparkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sparkTimer.Tick += async (s, e) => await SparkTimerTickAsync();
            Title = "오늘은 - " + _target.Name + " 예측";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = true;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Width = Math.Min(440, SystemParameters.WorkArea.Width - 20);
            Height = Math.Min(820, SystemParameters.WorkArea.Height - 30);
            MinWidth = Math.Min(340, Width);
            MinHeight = Math.Min(400, Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = cfg.Topmost;
            FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var card = new Border { Background = Palette.Card, BorderBrush = Palette.CardEdge,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20, 17, 20, 16), Margin = new Thickness(3) };
            Content = card;
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.Child = root;
            // 글자 크기. 뿌리에 걸어야 머리글·본문·꼬리가 함께 움직인다.
            // 창 크기는 그대로이고 안쪽 ScrollViewer 가 넘치는 만큼을 맡는다.
            _textScale = new ScaleTransform(Config.ScaleOrDefault(cfg.AnalysisScale), Config.ScaleOrDefault(cfg.AnalysisScale));
            root.LayoutTransform = _textScale;

            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 17), Background = Palette.Grab };
            header.MouseLeftButtonDown += (s, e) => { if (!e.Handled && e.ClickCount == 1) { try { DragMove(); } catch { } } };
            var close = ActionButton("닫기", 48);
            close.Click += (s, e) => Close();
            DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
            header.Children.Add(close);
            _refresh = ActionButton("↻ 갱신", 68);
            _refresh.Margin = new Thickness(0, 0, 8, 0);
            _refresh.Click += async (s, e) => await RefreshAsync();
            DockPanel.SetDock(_refresh, System.Windows.Controls.Dock.Right);
            header.Children.Add(_refresh);
            _analysisMode = new ToggleButton { Content = "참고", IsChecked = false, Width = 62, Height = 30,
                FontSize = 11, Foreground = Palette.Text, Template = DollarAnalysisStyles.ModeToggle,
                Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0), ToolTip = "참고: AI 호출 없음 / AI 전망: 로그인 후 주전망 분석" };
            DockPanel.SetDock(_analysisMode, System.Windows.Controls.Dock.Right); header.Children.Add(_analysisMode);
            var heading = new StackPanel();
            var title = Label(_target.Name, 18, Palette.Text);
            title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; title.ToolTip = _target.Name;
            heading.Children.Add(title);
            var subtitle = Label(_target.Def.Header, 10, Palette.TextDim);
            subtitle.TextWrapping = TextWrapping.NoWrap; subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            heading.Children.Add(subtitle);
            header.Children.Add(heading);
            root.Children.Add(header);

            var body = new StackPanel();
            var scroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 5, 0) };
            Grid.SetRow(scroller, 1);
            root.Children.Add(scroller);
            _price = Label("—", 30, Palette.Text);
            _price.TextWrapping = TextWrapping.NoWrap; _price.TextTrimming = TextTrimming.CharacterEllipsis;
            var priceRow = new Grid { HorizontalAlignment = HorizontalAlignment.Left }; priceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            priceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            priceRow.Children.Add(_price);
            _quoteRatio = Label("", 14, Palette.TextDim); _quoteRatio.VerticalAlignment = VerticalAlignment.Bottom;
            _quoteRatio.Margin = new Thickness(10, 0, 0, 6); Grid.SetColumn(_quoteRatio, 1); priceRow.Children.Add(_quoteRatio);
            var priceHeader = new Grid(); priceHeader.ColumnDefinitions.Add(new ColumnDefinition());
            priceHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            priceHeader.Children.Add(priceRow);
            _quoteSiteLink = _target.SiteLink(null, cfg.Bank);
            _quoteSite = ActionButton(PredictionTarget.SiteName(_quoteSiteLink), 64); _quoteSite.FontSize = 10;
            _quoteSite.Height = 27; _quoteSite.Margin = new Thickness(8, 0, 0, 6);
            _quoteSite.VerticalAlignment = VerticalAlignment.Bottom; _quoteSite.ToolTip = _quoteSiteLink;
            _quoteSite.Click += (s, e) => Net.OpenLink(_quoteSiteLink);
            Grid.SetColumn(_quoteSite, 1); priceHeader.Children.Add(_quoteSite); body.Children.Add(priceHeader);
            _fitQuoteHeader = () => {
                if (priceHeader.ActualWidth <= 0) return;
                var typeface = new Typeface(_price.FontFamily, _price.FontStyle, _price.FontWeight, _price.FontStretch);
                double dpi = VisualTreeHelper.GetDpi(_price).PixelsPerDip;
                double ratioWidth = new FormattedText(_quoteRatio.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 14, _quoteRatio.Foreground, dpi).Width;
                double available = Math.Max(60, priceHeader.ActualWidth - 74 - ratioWidth - 12);
                double natural = new FormattedText(_price.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 30, _price.Foreground, dpi).Width;
                _price.MaxWidth = available; _price.FontSize = Math.Max(12, Math.Min(30, 30 * available / Math.Max(1, natural)));
            };
            priceHeader.SizeChanged += (s, e) => _fitQuoteHeader();
            _quoteSource = Label("오늘은 시세를 확인합니다", 11, Palette.TextDim);
            body.Children.Add(_quoteSource);
            var sparkPanel = new StackPanel();
            var sparkRow = new Grid(); sparkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sparkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sparkRow.ColumnDefinitions.Add(new ColumnDefinition());
            sparkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sparkName = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _sparkLabel = Label(DollarSpark.ModelLabel(_model) + " ▾", 12, Palette.Text); sparkName.Children.Add(_sparkLabel);
            _sparkLabel.Cursor = Cursors.Hand;
            _sparkLabel.ToolTip = "분석 모델 선택 · 추론 강도 High · 변경 후 갱신 버튼으로 실행";
            _sparkLabel.MouseLeftButtonUp += (s, e) => {
                var menu = new ContextMenu();
                foreach (var id in new[] { DollarSpark.Model, "gpt-6-astra" }) {
                    string chosen = id; var item = new MenuItem { Header = DollarSpark.ModelLabel(id), IsChecked = id == _model };
                    item.Click += (sender, args) => ChangeModel(chosen); menu.Items.Add(item);
                }
                menu.PlacementTarget = _sparkLabel; menu.IsOpen = true;
            };
            _sparkDot = new Ellipse { Width = 7, Height = 7, Fill = Palette.TextFaint,
                Margin = new Thickness(7, 0, 0, 0), ToolTip = "로그인 전", VerticalAlignment = VerticalAlignment.Center };
            sparkName.Children.Add(_sparkDot); sparkRow.Children.Add(sparkName);
            _sparkLogin = ActionButton("로그인", 68); _sparkLogin.Height = 28;
            _sparkLogin.Click += async (s, e) => { if (_sparkConnected) DisconnectSpark(); else await EnsureSparkLoginAsync(); };
            Grid.SetColumn(_sparkLogin, 3); sparkRow.Children.Add(_sparkLogin); sparkPanel.Children.Add(sparkRow);
            _sparkCountdown = ActionButton("", double.NaN); _sparkCountdown.Height = 28;
            _sparkCountdown.Padding = new Thickness(5, 2, 5, 2); _sparkCountdown.HorizontalAlignment = HorizontalAlignment.Right;
            _sparkCountdown.Margin = new Thickness(2, 0, 5, 0); _sparkCountdown.FontSize = 10;
            _sparkCountdown.Click += (s, e) => ShowSparkIntervalMenu();
            Grid.SetColumn(_sparkCountdown, 2); sparkRow.Children.Add(_sparkCountdown);
            _sparkState = Label("로그인하면 첫 분석 · 선택 모델의 Codex 한도 사용", 10, Palette.TextDim);
            _sparkState.Margin = new Thickness(0, 2, 0, 0); sparkPanel.Children.Add(_sparkState);
            _loginHelp = Label(DollarSpark.LoginHelp + "\n선택한 품목의 공개 기사와 시세 통계를 전송합니다.", 10, Palette.TextDim);
            _loginHelp.Margin = new Thickness(0, 5, 0, 0); _loginHelp.Visibility = Visibility.Collapsed;
            _sparkHelp = new Button { Content = "?", Width = 21, Height = 21, FontSize = 11, Foreground = Palette.TextDim,
                Template = DollarAnalysisStyles.SmallButton, Cursor = Cursors.Hand, ToolTip = "Spark 연결 도움말", Margin = new Thickness(7, 0, 0, 0) };
            _sparkHelp.Click += (s, e) => _loginHelp.Visibility = _loginHelp.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            Grid.SetColumn(_sparkHelp, 1); sparkRow.Children.Add(_sparkHelp); sparkPanel.Children.Add(_loginHelp);
            _sparkLogin.IsEnabled = !_target.Weather; _sparkCountdown.IsEnabled = !_target.Weather;
            if (_target.Weather) _sparkState.Text = "날씨는 기상 예보 자료를 사용합니다";
            UpdateSparkCountdown();
            _sparkCard = new Border { Child = sparkPanel, Background = Palette.Tile, BorderBrush = Palette.TileEdge,
                Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8), Margin = new Thickness(0, 12, 0, 12) };
            body.Children.Add(_sparkCard);
            _primaryStatus = Label("주전망 대기 · AI 전망으로 전환해 분석하세요", 12, Palette.Text);
            _primaryStatus.Margin = new Thickness(0, 8, 0, 6); body.Children.Add(_primaryStatus);
            _performance = Label("예측 성적 · 기록 대기", 10, Palette.TextDim);
            _performance.Margin = new Thickness(0, 6, 0, 8); body.Children.Add(_performance);
            _primaryHost = new StackPanel(); body.Children.Add(_primaryHost);
            _forecastPanel = new StackPanel();
            _comparisonBody = new StackPanel();
            _comparisonSummary = new BriefTextBlock { FontSize = 11, Foreground = Brushes.White };
            _comparisonBody.Children.Add(_comparisonSummary);
            _comparisonBody.Children.Add(_forecastPanel);
            // 기본으로 펼쳐 둔다. 주전망 하나를 앞세우되(v1.011) 무엇과 견준 값인지는
            // 접어 두지 않는다 - 접어 두면 규칙·과거 통계를 본 사람이 거의 없었다.
            _comparison = new Expander { Header = "비교 기준 · 규칙·과거 통계", IsExpanded = true,
                Template = DollarAnalysisStyles.Disclosure, Foreground = Palette.TextDim,
                Content = _comparisonBody, Margin = new Thickness(0, 10, 0, 0) };
            for (int i = 0; i < 3; i++)
            {
                var headingRow = new Grid();
                headingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                headingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                headingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
                headingRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                headingRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _periodHeadings[i] = RowLabel(new[] { "단기", "중기", "장기" }[i], 12, Palette.Text);
                var horizon = RowLabel(new[] { "1일", "1주", "1개월" }[i], 11, Palette.TextDim);
                _periodDirections[i] = RowLabel("확인 중", 12, Palette.Text);
                _evidenceCounts[i] = RowLabel("자료 0건", 10.5, Palette.TextDim);
                _evidenceCounts[i].TextAlignment = TextAlignment.Right;
                Grid.SetColumn(horizon, 1); Grid.SetColumn(_periodDirections[i], 2); Grid.SetColumn(_evidenceCounts[i], 3);
                headingRow.Children.Add(_periodHeadings[i]); headingRow.Children.Add(horizon);
                headingRow.Children.Add(_periodDirections[i]); headingRow.Children.Add(_evidenceCounts[i]);
                var reasonRow = new Grid { Margin = new Thickness(0, 3, 0, 0) };
                reasonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(57) });
                reasonRow.ColumnDefinitions.Add(new ColumnDefinition());
                _periodPoints[i] = RowLabel("—점", 10, Palette.TextDim);
                _periodReasons[i] = RowLabel("갱신 후 근거 표시", 10, Palette.TextDim);
                Grid.SetColumn(_periodReasons[i], 1); reasonRow.Children.Add(_periodPoints[i]); reasonRow.Children.Add(_periodReasons[i]);
                Grid.SetRow(reasonRow, 1); Grid.SetColumnSpan(reasonRow, 4); headingRow.Children.Add(reasonRow);
                headingRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _periodChecks[i] = new BriefTextBlock { FontSize = 10, Foreground = Palette.TextDim,
                    Margin = new Thickness(0, 4, 0, 6), Visibility = Visibility.Collapsed };
                Grid.SetRow(_periodChecks[i], 2); Grid.SetColumnSpan(_periodChecks[i], 4); headingRow.Children.Add(_periodChecks[i]);
                _evidenceLists[i] = new StackPanel();
                _periodEvidence[i] = new Expander { Header = headingRow, Content = _evidenceLists[i], IsExpanded = false,
                    Template = DollarAnalysisStyles.Disclosure, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Foreground = Palette.TextDim, Margin = new Thickness(0, 0, 0, 2),
                    ToolTip = "누르면 실제 사용한 기사와 반대 근거를 펼칩니다" };
                _forecastPanel.Children.Add(_periodEvidence[i]);
            }
            _summary = _periodDirections[0];
            var forecastCards = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            string[] forecastNames = { "1일 후", "1주 후", "1개월 후" };
            for (int i = 0; i < 3; i++)
            {
                forecastCards.ColumnDefinitions.Add(new ColumnDefinition());
                var panel = new StackPanel { Margin = new Thickness(7, 6, 7, 6) };
                panel.Children.Add(Label(forecastNames[i], 11, Palette.TextDim));
                _forecastValues[i] = Label("—", 17, Palette.Text);
                _forecastStates[i] = Label("확인 중", 10, Palette.TextDim);
                _forecastChanges[i] = Label("—", 10, Palette.TextDim);
                panel.Children.Add(new Viewbox { Child = _forecastValues[i], StretchDirection = StretchDirection.DownOnly, Stretch = Stretch.Uniform,
                    Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
                panel.Children.Add(_forecastChanges[i]); panel.Children.Add(_forecastStates[i]);
                var tile = new Border { Background = Palette.Tile, CornerRadius = new CornerRadius(7),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0), Child = panel };
                Grid.SetColumn(tile, i); forecastCards.Children.Add(tile);
            }
            _forecastPanel.Children.Add(Label(_target.Weather ? "기온 예보" : "가격 시나리오 · 적중률 미검증", 10, Palette.TextDim));
            _forecastPanel.Children.Add(forecastCards);
            _chart = new Canvas { Height = 155, ClipToBounds = true, Margin = new Thickness(0, 10, 0, 0) };
            _forecastPanel.Children.Add(_chart);
            _forecastBasis = Label("", 10, Palette.TextDim);
            _forecastPanel.Children.Add(_forecastBasis);
            _brief = new BriefTextBlock { Text = "주요 근거를 확인합니다", FontSize = 11, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 2) };
            _brief.Margin = new Thickness(0, 12, 0, 0);
            _forecastPanel.Children.Add(_brief);
            body.Children.Add(_comparison);
            body.Children.Add(Divider());
            var detailBody = new StackPanel();
            _score = Label("", 11, Palette.TextDim);
            detailBody.Children.Add(Label("요인별 해석과 조합", 12, Palette.Text));
            _factorDetail = Label("", 11, Palette.TextDim);
            detailBody.Children.Add(_factorDetail);
            detailBody.Children.Add(Divider());
            detailBody.Children.Add(Label("점수와 계산", 12, Palette.Text));
            detailBody.Children.Add(_score);
            _forecastDetail = Label("", 11, Palette.TextDim);
            detailBody.Children.Add(_forecastDetail);
            detailBody.Children.Add(Divider());
            detailBody.Children.Add(Label("참고 · 기간별 과거 유사 사례", 12, Palette.TextDim));
            detailBody.Children.Add(Label(_target.PeriodBasis + " 후의 누적 변동", 10.5, Palette.TextDim));
            var ratios = new Grid { Margin = new Thickness(0, 12, 0, 10) };
            ratios.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            for (int i = 0; i < 3; i++) ratios.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 4; i++) ratios.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] periodNames = { "일간", "주간", "월간" };
            string[] directionNames = { "상승", "보합", "하락" };
            Brush[] colors = { Palette.Up, Palette.Flat, Palette.Down };
            for (int column = 0; column < 3; column++)
            {
                var label = Label(directionNames[column], 11, colors[column]);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(label, column + 1);
                ratios.Children.Add(label);
                for (int row = 0; row < 3; row++)
                {
                    var cell = Label("—", 21, colors[column]);
                    cell.HorizontalAlignment = HorizontalAlignment.Center;
                    cell.Margin = new Thickness(0, 5, 0, 5);
                    Grid.SetColumn(cell, column + 1); Grid.SetRow(cell, row + 1);
                    ratios.Children.Add(cell); _periodCells[row, column] = cell;
                }
            }
            for (int row = 0; row < 3; row++)
            {
                var label = Label(periodNames[row], 12, Palette.Text);
                label.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(label, row + 1); ratios.Children.Add(label);
            }
            _up = _periodCells[0, 0]; _flat = _periodCells[0, 1]; _down = _periodCells[0, 2];
            detailBody.Children.Add(ratios);
            _patternInfo = Label("과거 환율을 불러오는 중입니다", 11, Palette.TextDim);
            detailBody.Children.Add(_patternInfo);
            _method = Label("", 10.5, Palette.TextDim);
            var details = new Expander { Header = "분석 기준과 과거 검증", Foreground = Palette.TextDim,
                Template = DollarAnalysisStyles.Disclosure,
                FontSize = 11, IsExpanded = true, Margin = new Thickness(0, 10, 0, 0), Content = _method };
            detailBody.Children.Add(details);
            var caveatCard = new Border { Background = Palette.Tile, CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10), Margin = new Thickness(0, 12, 0, 0) };
            var caveats = new StackPanel();
            caveats.Children.Add(Label("반대 근거", 12, Palette.Stale));
            _counterEvidence = Label("자료를 확인한 뒤 통계와 상반되는 근거를 표시합니다", 10.5, Palette.TextDim);
            caveats.Children.Add(_counterEvidence);
            caveatCard.Child = caveats;
            detailBody.Children.Add(caveatCard);
            detailBody.Children.Add(Divider());
            detailBody.Children.Add(Label("국내외 뉴스", 14, Palette.Text));
            _newsInfo = Label("최근 24시간 · 기사 제목에서 요인을 분류합니다", 10.5, Palette.TextDim);
            detailBody.Children.Add(_newsInfo);
            _news = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            detailBody.Children.Add(_news);

            _details = new Expander { Header = "상세 보기 · 점수·뉴스·검증", IsExpanded = false,
                Template = DollarAnalysisStyles.Disclosure,
                Foreground = Palette.TextDim, FontSize = 11, Content = detailBody };
            body.Children.Add(_details);

            var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            _status = Label("첫 조회를 준비합니다", 10.5, Palette.TextDim);
            footer.Children.Add(_status);
            footer.Children.Add(Label("기사 요인 기반 추론 · 예측 성능 미검증", 10, Palette.TextFaint));
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            _analysisMode.Checked += (s, e) => ChangeStyle();
            _analysisMode.Unchecked += (s, e) => ChangeStyle();

            // 이 품목의 왕복 거래비용. 설정에 없으면 0 이고 문턱은 종전대로다.
            _roundTrip = Config.CostOf(cfg.RoundTripCost, _target.Key);
            Loaded += async (s, e) => { RestorePosition(); await RefreshAsync(); };
            _chart.SizeChanged += delegate
            {
                if (_lastRendered != null) { _chart.Children.Clear(); DrawChart(_lastRendered, _utcNow()); }
            };
            // ★ 글자 크기는 KeyDown 이 아니라 PreviewKeyDown 으로 받아야 한다 ★
            //   KeyDown 은 버블링이라 Alt 조합이 AccessKeyManager 와 자식 컨트롤을 먼저 거친다.
            //   실제로 KeyDown 에 걸었더니 눌러도 아무 일이 없었다. 터널링은 창이 먼저 본다.
            PreviewKeyDown += (s, e) =>
            {
                // Alt 조합은 Key 가 System 으로 오고 실제 키는 SystemKey 에 담긴다.
                double step = ZoomStep((e.Key == Key.System) ? e.SystemKey : e.Key, Keyboard.Modifiers);
                if (double.IsNaN(step)) return;
                e.Handled = true;
                ZoomText(step);
            };
            KeyDown += async (s, e) =>
            {
                Key k = (e.Key == Key.System) ? e.SystemKey : e.Key;
                if (k == Key.Escape) { Close(); e.Handled = true; }
                else if (k == Key.F5) { e.Handled = true; await RefreshAsync(); }
            };
            Closed += delegate
            {
                _closed = true;
                _sparkTimer.Stop();
                _lifetime.Cancel();
                if (!double.IsNaN(Left) && !double.IsInfinity(Left)) cfg.DollarX = Left;
                if (!double.IsNaN(Top) && !double.IsInfinity(Top)) cfg.DollarY = Top;
                Sources.QuoteUpdated -= OnQuoteUpdated;
                if (!_appClosing && _target.Dollar) cfg.ShowDollarAnalysis = false;
                cfg.Save();
                // 요청 중이면 finally에서 해제한다. 취소 토큰은 완료 전 Dispose하지 않는다.
                if (!_busy && !_loginBusy) _lifetime.Dispose();
            };
            Sources.QuoteUpdated += OnQuoteUpdated;
            var shared = Sources.SharedQuote(_target.Def, cfg.Bank);
            if (shared != null) RenderQuote(shared);
        }

        private void OnQuoteUpdated(string key, Quote quote)
        {
            if (_closed || key != Sources.QuoteKey(_target.Def, _cfg.Bank)) return;
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnQuoteUpdated(key, quote))); return; }
            if (_lastRendered != null && _lastRendered.Quote != null && quote.ReceivedUtc < _lastRendered.Quote.ReceivedUtc) return;
            foreach (var cached in _styleResults.Where(r => r != null)) cached.Quote = quote;
            RenderQuote(quote);
            if (_lastRendered != null) { _lastRendered.Quote = quote; _chart.Children.Clear(); DrawChart(_lastRendered, _utcNow()); }
        }

        private void RenderQuote(Quote quote)
        {
            if (quote == null || !quote.Ok) return;
            if (_performance != null) {
                try { PredictionJournal.Observe(quote, _utcNow()); _performance.Text = PredictionJournal.Summary(quote); }
                catch { _performance.Text = "예측 채점 기록 확인 실패"; }
            }
            _quoteSiteLink = _target.SiteLink(quote, _cfg.Bank);
            _quoteSite.Content = PredictionTarget.SiteName(_quoteSiteLink); _quoteSite.ToolTip = _quoteSiteLink;
            double value = PredictionTarget.Number(quote);
            _price.Text = double.IsNaN(value) ? quote.Price : _target.Format(value, quote);
            _price.ToolTip = _price.Text;
            double ratio;
            _quoteRatio.Text = quote.RatioSuffix == "%" && double.TryParse(quote.Ratio, NumberStyles.Number, CultureInfo.InvariantCulture, out ratio)
                ? (Math.Abs(ratio) < 0.005 ? "0.00%" : (ratio > 0 ? "▲ " : "▼ ") + ratio.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%") : "";
            _quoteRatio.Foreground = double.TryParse(quote.Ratio, NumberStyles.Number, CultureInfo.InvariantCulture, out ratio) && Math.Abs(ratio) >= 0.005 ? (ratio > 0 ? Palette.Up : Palette.Down) : Palette.TextDim;
            if (_fitQuoteHeader != null) _fitQuoteHeader();
            _quoteSource.Text = DollarAnalysis.Clean(quote.Source, 50) + " · " + DollarAnalysis.Clean(quote.Time, 40) + " · 오늘은 공유 시세";
        }

        private void RestorePosition()
        {
            // Widget-launched windows follow the invoking widget, never a saved monitor.
            if (_positionedNearOwner) return;
            if (double.IsNaN(_cfg.DollarX) || double.IsNaN(_cfg.DollarY)) return;
            double sx, sy;
            Dock.GetDpiScale(this, out sx, out sy);
            var desired = new Rect(_cfg.DollarX * sx, _cfg.DollarY * sy, Width * sx, Height * sy);
            foreach (var screen in Dock.AllScreens())
            {
                Rect overlap = Rect.Intersect(screen.Work, desired);
                if (overlap.IsEmpty || overlap.Width < 120 * sx || overlap.Height < 80 * sy) continue;
                Left = Math.Max(screen.Work.Left / sx, Math.Min(_cfg.DollarX, screen.Work.Right / sx - Width));
                Top = Math.Max(screen.Work.Top / sy, Math.Min(_cfg.DollarY, screen.Work.Bottom / sy - Height));
                return;
            }
        }

        internal void PositionNearOwner(Window owner, System.Collections.Generic.List<ScreenInfo> screens = null, Point? cursor = null)
        {
            if (owner == null) return;
            double ox, oy, sx, sy;
            Dock.GetDpiScale(owner, out ox, out oy);
            Dock.GetDpiScale(this, out sx, out sy);
            double w = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
            double h = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;
            if (new[] { owner.Left, owner.Top, w, h }.Any(n => double.IsNaN(n) || double.IsInfinity(n)) || w <= 0 || h <= 0) return;
            // ScreenInfo.Work/Bounds and CursorPos use process pixels, not raw physical pixels.
            var ownerPx = new Rect(owner.Left * ox, owner.Top * oy, w * ox, h * oy);
            var pointer = cursor ?? Dock.CursorPos();
            bool clicked = ownerPx.Contains(pointer);
            var bar = owner as IDockBar;
            var all = screens ?? Dock.AllScreens();
            if (all.Count == 0) return;
            var screen = Dock.ScreenByDevice(all, bar == null ? null : bar.BarDevice) ??
                Dock.ScreenAt(all, clicked ? pointer : new Point(ownerPx.Left + ownerPx.Width / 2, ownerPx.Top + ownerPx.Height / 2));
            if (screen.Work.IsEmpty || screen.Work.Width <= 0 || screen.Work.Height <= 0) return;
            var ownerDip = new Rect(ownerPx.Left / sx, ownerPx.Top / sy, ownerPx.Width / sx, ownerPx.Height / sy);
            var work = new Rect(screen.Work.Left / sx, screen.Work.Top / sy, screen.Work.Width / sx, screen.Work.Height / sy);
            var bounds = NearbyBounds(ownerDip, work, new Size(IsLoaded ? Width : 440, IsLoaded ? Height : 820),
                clicked ? (Point?)new Point(pointer.X / sx, pointer.Y / sy) : null, bar == null ? DockEdge.None : bar.BarEdge);
            WindowStartupLocation = WindowStartupLocation.Manual;
            MinWidth = Math.Min(340, bounds.Width); MinHeight = Math.Min(400, bounds.Height);
            Width = bounds.Width; Height = bounds.Height;
            Left = bounds.Left; Top = bounds.Top;
            _positionedNearOwner = true;
        }

        internal static Rect NearbyBounds(Rect owner, Rect work, Size desired, Point? clicked, DockEdge edge)
        {
            const double gap = 10, margin = 8;
            double width = Math.Min(desired.Width, Math.Max(1, work.Width - 2 * margin));
            double height = Math.Min(desired.Height, Math.Max(1, work.Height - 2 * margin));
            double x = owner.Right + gap, y = clicked.HasValue ? clicked.Value.Y - 24 : owner.Top;
            if (edge == DockEdge.Top || edge == DockEdge.Bottom) {
                x = clicked.HasValue ? clicked.Value.X - 40 : owner.Left;
                y = edge == DockEdge.Top ? owner.Bottom + gap : owner.Top - gap - height;
            } else if (edge == DockEdge.Right) x = owner.Left - gap - width;
            else if (edge == DockEdge.None && x + width > work.Right - margin) {
                if (owner.Left - gap - width >= work.Left + margin) x = owner.Left - gap - width;
                else if (owner.Bottom + gap + height <= work.Bottom - margin) { x = owner.Left; y = owner.Bottom + gap; }
                else if (owner.Top - gap - height >= work.Top + margin) { x = owner.Left; y = owner.Top - gap - height; }
            }
            x = Math.Max(work.Left + margin, Math.Min(x, work.Right - margin - width));
            y = Math.Max(work.Top + margin, Math.Min(y, work.Bottom - margin - height));
            return new Rect(x, y, width, height);
        }

        private void ChangeStyle()
        {
            if (_closed) return;
            bool extreme = _analysisMode.IsChecked == true;
            if (_refreshCancellation != null) _refreshCancellation.Cancel();
            if (_loginCancellation != null) _loginCancellation.Cancel();
            _sparkCard.Visibility = extreme && !_target.Weather ? Visibility.Visible : Visibility.Collapsed;
            if (_sparkCountdown.ContextMenu != null) _sparkCountdown.ContextMenu.IsOpen = false;
            ScheduleSparkRefresh();
            _analysisMode.Content = extreme ? "AI 전망" : "참고";
            var saved = _styleResults[extreme ? 1 : 0];
            if (saved != null) Render(saved);
            else
            {
                ClearResults();
                _status.Text = (extreme ? "AI 전망" : "참고") + " 선택 · 갱신하면 자료를 확인합니다";
            }
        }

        internal async Task EnsureSparkLoginAsync()
        {
            if (_closed || _busy || _loginBusy || _target.Weather || _sparkConnected || _analysisMode.IsChecked != true) return;
            _loginCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var loginToken = _loginCancellation.Token;
            bool startAnalysis = false;
            _loginBusy = true; _refresh.IsEnabled = false; _sparkLogin.IsEnabled = false;
            _sparkState.Text = "로그인 상태 확인 중…";
            UpdateSparkCountdown();
            try
            {
                if (!await _checkLogin(loginToken))
                {
                    if (_closed || loginToken.IsCancellationRequested) return;
                    _sparkState.Text = "브라우저에서 로그인해 주세요…";
                    await _login(loginToken);
                    if (_closed || loginToken.IsCancellationRequested) return;
                    if (!await _checkLogin(loginToken)) throw new InvalidOperationException("로그인이 완료되지 않았습니다 · 다시 로그인해 주세요");
                }
                if (!_closed && !loginToken.IsCancellationRequested && _analysisMode.IsChecked == true)
                {
                    _sparkConnected = true;
                    _sparkDot.Fill = Palette.Online; _sparkDot.ToolTip = "ChatGPT 로그인 확인됨";
                    _sparkLogin.Content = "연결 해제";
                    _sparkLogin.ToolTip = "이 창의 Spark 분석과 자동 갱신을 끕니다. Codex 계정 로그인은 유지합니다.";
                    _sparkState.Text = "로그인됨 · 첫 분석 시작";
                    startAnalysis = true;
                }
            }
            catch (OperationCanceledException) { if (!_closed) _sparkState.Text = "로그인 취소 · 다시 로그인해 주세요"; }
            catch (Exception ex) { if (!_closed) _sparkState.Text = ex is InvalidOperationException ? ex.Message : "로그인 연결 실패 · 연결 도움말 확인"; }
            finally
            {
                _loginBusy = false;
                _loginCancellation.Dispose(); _loginCancellation = null;
                if (_closed) { if (!_busy) _lifetime.Dispose(); }
                else { _refresh.IsEnabled = true; _sparkLogin.IsEnabled = true; _analysisMode.IsEnabled = true; UpdateSparkCountdown(); }
            }
            // 창을 열 때 실행한 일반 조회의 30초 제한 때문에 첫 AI 분석이 막히면 안 된다.
            if (startAnalysis && !_closed && _analysisMode.IsChecked == true) await RefreshCoreAsync(true);
        }

        private void DisconnectSpark()
        {
            _sparkConnected = false; _sparkTimer.Stop(); _nextSparkAt = DateTime.MaxValue;
            _sparkDot.Fill = Palette.TextFaint; _sparkDot.ToolTip = "Spark 연결 안 됨";
            _sparkLogin.Content = "로그인"; _sparkLogin.ToolTip = "로그인 확인 후 Spark 분석 시작";
            _sparkState.Text = "AI 연결 해제 · 비교 기준만 갱신";
            UpdateSparkCountdown();
        }

        private void ShowSparkIntervalMenu()
        {
            var menu = new ContextMenu();
            foreach (int seconds in new[] { 0, 60, 300, 600, 1800, 3600, 21600 })
            {
                int value = seconds;
                var item = new MenuItem { Header = value == 0 ? "수동 갱신" : value + "초 · " + (value / 60) + "분",
                    IsCheckable = true, IsChecked = _cfg.SparkRefreshIntervalSec == value };
                item.Click += (s, e) => SetSparkInterval(value);
                menu.Items.Add(item);
            }
            _sparkCountdown.ContextMenu = menu; menu.PlacementTarget = _sparkCountdown;
            menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
        }

        internal void SetSparkInterval(int seconds)
        {
            if (_closed) return;
            _cfg.SparkRefreshIntervalSec = Config.SparkInterval(seconds); _cfg.Save();
            ScheduleSparkRefresh();
            foreach (var window in OpenWindows.Where(w => w != this && object.ReferenceEquals(w._cfg, _cfg)))
                window.ScheduleSparkRefresh();
        }

        private void ScheduleSparkRefresh()
        {
            _sparkTimer.Stop(); _sparkInterval = Config.SparkInterval(_cfg.SparkRefreshIntervalSec);
            bool active = !_closed && _sparkConnected && _sparkInterval > 0 && !_sparkAutoPaused && _analysisMode.IsChecked == true;
            _nextSparkAt = active ? _utcNow().AddSeconds(_sparkInterval) : DateTime.MaxValue;
            if (active) _sparkTimer.Start();
            if (!_closed) UpdateSparkCountdown();
        }

        private void UpdateSparkCountdown()
        {
            _sparkCountdown.Content = _busy ? "분석 중" : _loginBusy ? "확인 중" : _sparkAutoPaused ? "중지" : _sparkInterval == 0 ? "수동" :
                (_sparkConnected && _nextSparkAt != DateTime.MaxValue
                    ? Math.Max(0, Math.Ceiling((_nextSparkAt - _utcNow()).TotalSeconds)).ToString("0", CultureInfo.InvariantCulture)
                    : _sparkInterval.ToString(CultureInfo.InvariantCulture)) + "초";
            _sparkCountdown.ToolTip = _sparkAutoPaused ? "자동 분석 중지 · 갱신 버튼을 눌러 다시 시도하세요" : "클릭하여 갱신 주기 설정 · " + (_sparkInterval == 0 ? "수동" : _sparkInterval + "초 간격") +
                (_sparkConnected ? " · 분석할 때마다 Spark 한도 사용" : " · 로그인 후 시작");
        }

        internal async Task SparkTimerTickAsync()
        {
            if (_closed || !_sparkConnected || _sparkAutoPaused || _analysisMode.IsChecked != true) return;
            if (_sparkInterval != Config.SparkInterval(_cfg.SparkRefreshIntervalSec)) ScheduleSparkRefresh();
            UpdateSparkCountdown();
            if (_busy || _loginBusy || _sparkInterval == 0 || _utcNow() < _nextSparkAt) return;
            await RefreshAsync();
        }

        internal void ChangeModel(string model)
        {
            model = DollarSpark.ModelId(model); if (model == _model) return;
            if (_refreshCancellation != null) _refreshCancellation.Cancel();
            if (_loginCancellation != null) _loginCancellation.Cancel();
            _model = model; _cfg.AnalysisModel = model; _cfg.Save();
            _sparkLabel.Text = DollarSpark.ModelLabel(model) + " ▾";
            _styleResults[1] = null; _sparkAutoPaused = true; _lastAttempt = DateTime.MinValue;
            if (_analysisMode.IsChecked == true) ClearResults();
            _sparkState.Text = "모델 변경 · 갱신을 눌러 분석하세요"; ScheduleSparkRefresh();
        }

        internal Task RefreshAsync() { return RefreshCoreAsync(false); }

        private async Task RefreshCoreAsync(bool firstLogin)
        {
            if (_closed || _busy || _loginBusy) return;
            var since = _utcNow() - _lastAttempt;
            if (!firstLogin && since.TotalSeconds < 30)
            {
                _status.Text = "방금 조회했습니다 · " + Math.Ceiling(30 - since.TotalSeconds).ToString("0") + "초 후 다시 갱신할 수 있습니다";
                return;
            }
            _busy = true;
            bool extreme = _analysisMode.IsChecked == true;
            _refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var refreshToken = _refreshCancellation.Token;
            _styleResults[_analysisMode.IsChecked == true ? 1 : 0] = null;
            _lastAttempt = _utcNow();
            _refresh.IsEnabled = false;
            _sparkLogin.IsEnabled = false;
            _refresh.Content = "조회 중";
            UpdateSparkCountdown();
            _status.Text = _target.Name + " 시세와 국내외 뉴스를 확인하고 있습니다…";
            try
            {
                var result = await _fetch(refreshToken);
                // 이 품목의 왕복 거래비용을 결과에 실어 보낸다. 문턱과 기록이 같은 값을 쓴다.
                if (result != null) result.RoundTripPercent = _roundTrip;
                if (_closed || refreshToken.IsCancellationRequested) return;
                if (result != null) result = result.ForStyle(extreme);
                if (!_closed && result != null && _sparkConnected && !_target.Weather && extreme)
                {
                    result.Spark = null;
                    _status.Text = "AI가 기사와 반대 근거를 분석하고 있습니다…";
                    try
                    {
                        bool loggedIn;
                        try { loggedIn = await _checkLogin(refreshToken); }
                        catch { if (!_closed && !refreshToken.IsCancellationRequested) DisconnectSpark(); throw; }
                        if (_closed || refreshToken.IsCancellationRequested) return;
                        if (!loggedIn)
                        {
                            if (_closed) return;
                            DisconnectSpark();
                            throw new InvalidOperationException("로그인이 만료됐습니다 · 다시 로그인해 주세요");
                        }
                        if (_closed) return;
                        var spark = await _analyze(result, refreshToken);
                        if (_closed || refreshToken.IsCancellationRequested) return;
                        result.Spark = spark; result.SparkStatus = DollarSpark.ModelName(_model) + " 분석 완료" + (spark.MergedCitations > 0 ? " · 중복 인용 정리" : ""); _sparkAutoPaused = false;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { _sparkAutoPaused = true; result.SparkStatus = DollarSpark.AnalysisError(ex) + " · 자동 갱신 중지"; }
                    if (!_closed && !refreshToken.IsCancellationRequested) _sparkState.Text = (_sparkConnected ? "로그인됨 · " : "") + result.SparkStatus;
                }
                if (!_closed && !refreshToken.IsCancellationRequested) {
                    // Freeze the model's actual input anchor before Render adopts a newer shared quote.
                    var recorded = result == null ? null : result.Snapshot();
                    Render(result);
                    if (recorded != null && _lastRendered != null) {
                        try { PredictionJournal.Record(recorded, _utcNow()); _performance.Text = PredictionJournal.Summary(_lastRendered.Quote); }
                        catch { _performance.Text = "예측 기록 저장 실패 · 폴더 쓰기 권한 확인"; }
                    }
                }
            }
            catch (OperationCanceledException) { if (!_closed && extreme == (_analysisMode.IsChecked == true)) _status.Text = "조회가 취소됐습니다 · 다시 갱신해 주세요"; }
            catch { if (!_closed) { ClearResults(); _status.Text = "갱신 실패 · 연결을 확인한 뒤 다시 시도해 주세요"; } }
            finally
            {
                _busy = false;
                _refreshCancellation.Dispose(); _refreshCancellation = null;
                if (_closed) _lifetime.Dispose();
                else { _refresh.IsEnabled = true; _refresh.Content = "↻ 갱신"; _sparkLogin.IsEnabled = !_target.Weather;
                    _analysisMode.IsEnabled = true; ScheduleSparkRefresh(); }
            }
        }

        private void ClearResults()
        {
            _lastRendered = null;
            SetForecastRole(null);
            _score.Text = "자료 없음";
            _summary.Text = "자료 부족";
            _summary.Foreground = Palette.TextDim;
            for (int i = 0; i < 3; i++)
            {
                _periodDirections[i].Text = "자료 부족"; _periodDirections[i].Foreground = Palette.TextDim;
                _evidenceCounts[i].Text = "자료 0건"; _evidenceLists[i].Children.Clear();
                _periodPoints[i].Text = "—점"; _periodPoints[i].Foreground = Palette.TextDim;
                _periodReasons[i].Text = "갱신 후 근거 표시"; _periodReasons[i].ToolTip = null;
                _periodChecks[i].Text = ""; _periodChecks[i].Visibility = Visibility.Collapsed;
            }
            _brief.Text = "자료를 확인한 뒤 전망을 표시합니다.";
            _forecastBasis.Text = _forecastDetail.Text = _factorDetail.Text = "";
            foreach (var value in _forecastValues) value.Text = "—";
            foreach (var state in _forecastStates) state.Text = "자료 부족";
            foreach (var change in _forecastChanges) { change.Text = "—\n—"; change.Foreground = Palette.TextDim; }
            _price.Text = "—"; _quoteRatio.Text = "";
            _quoteSource.Text = "현재 시세를 받지 못했습니다";
            _up.Text = _flat.Text = _down.Text = "—";
            foreach (var cell in _periodCells) cell.Text = "—";
            _patternInfo.Text = "판단 보류 · 시세 이력을 확인할 수 없습니다";
            _method.Text = "";
            _counterEvidence.Text = "자료가 없어 결론을 낼 수 없습니다";
            _chart.Children.Clear();
            _news.Children.Clear();
            _newsInfo.Text = "뉴스를 받지 못했습니다";
        }

        private void SetForecastRole(DollarAnalysisResult result)
        {
            bool primary = result != null && (result.Spark != null || _target.Weather);
            var host = primary ? _primaryHost : _comparisonBody;
            var previous = _forecastPanel.Parent as Panel;
            if (previous != host) { if (previous != null) previous.Children.Remove(_forecastPanel); host.Children.Add(_forecastPanel); }
            _comparison.Visibility = _target.Weather ? Visibility.Collapsed : Visibility.Visible;
            _comparisonSummary.Visibility = primary ? Visibility.Visible : Visibility.Collapsed;
            _comparisonSummary.Text = "";
            _primaryStatus.Text = _target.Weather ? "기상 예보" : primary ? "AI 주전망 · " + DollarSpark.ModelName(result.Spark.ModelId) + " · 성능 검증 중" :
                result != null && !string.IsNullOrEmpty(result.SparkStatus) ? "주전망 생성 실패 · 비교 기준만 제공" :
                _analysisMode.IsChecked == true ? "주전망 대기 · 로그인 후 분석하세요" : "주전망 대기 · AI 전망으로 전환해 분석하세요";
            if (primary && !_target.Weather) {
                var basic = result.ForStyle(false);
                _comparisonSummary.Text = "같은 입력 자료의 기본 계산 · AI와 비교 채점하는 기준입니다.\n";
                foreach (int h in new[] { 1, 5, 20 }) {
                    double? change = ScenarioChange(basic, h, _utcNow());
                    _comparisonSummary.Text += PeriodName(h) + " " + ScenarioOutlook(basic, h, _utcNow()) + " · " +
                        (change.HasValue ? (change.Value * 100).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%" : "변동폭 미산정") + "\n";
                }
                _comparisonSummary.Text += BandText(result.RoundTripPercent).Replace("보합 범위", "보합 기준") + ". 점수는 확률이 아닙니다.";
            }
        }

        internal void Render(DollarAnalysisResult result)
        {
            // ★ 한 번 그리는 동안은 한 시각만 산다 ★
            //   전에는 화면 문구를 만드는 자리마다 DateTime.UtcNow 를 새로 읽었다. 실행 중에는
            //   차이가 없어 보이지만 두 가지가 걸린다. 하나, 검사가 시계를 옮겨도 절반은 진짜
            //   시계를 보므로 '24시간 지난 기사' 같은 경계를 검사가 볼 수 없었다. 둘, 값이 매번
            //   달라서 Score 결과를 기억해 둘 수가 없었다 - 한 번 그리는 데 같은 계산을 70번 했다.
            DateTime now = _utcNow();
            if (result != null && result.Extreme != (_analysisMode.IsChecked == true)) return;
            if (result != null && !result.Extreme && result.Spark != null) result = result.ForStyle(false);
            ClearResults();
            if (result != null && result.Target.Key != _target.Key) result = null;
            if (result == null) { _styleResults[_analysisMode.IsChecked == true ? 1 : 0] = null; _status.Text = "갱신 실패 · 자료를 받지 못했습니다"; return; }
            bool suppliedAi = result.Spark != null;
            if (result.Spark != null && (result.Spark.TargetKey != _target.Key || result.Spark.Extreme != result.Extreme)) result.Spark = null;
            if (result.Spark != null && new[] { 1, 5, 20 }.Any(h => !DollarAnalysis.Score(result, h, now).IsAi)) result.Spark = null;
            if (suppliedAi && result.Spark == null) {
                result.SparkStatus = "AI 응답 검증 실패"; _sparkState.Text = result.SparkStatus;
                _sparkAutoPaused = true; ScheduleSparkRefresh();
            }
            result.News = result.News.Where(n => DollarAnalysis.ForTarget(n, _target)).ToList();
            var sharedQuote = Sources.SharedQuote(_target.Def, _cfg.Bank);
            if (result.Quote != null && result.Quote.IdentityKey != null && result.Quote.IdentityKey != Sources.QuoteKey(_target.Def, _cfg.Bank)) result.Quote = null;
            if (sharedQuote != null && (result.Quote == null || sharedQuote.ReceivedUtc > result.Quote.ReceivedUtc)) result.Quote = sharedQuote;
            _lastRendered = result;
            _styleResults[result.Extreme ? 1 : 0] = result;
            SetForecastRole(result);
            _counterEvidence.Text = string.Join("\n", DollarAnalysis.CounterEvidence(result, now).Select(x => "· " + x));
            RenderQuote(result.Quote);
            var pattern = result.Pattern;
            var periods = result.Patterns.Count > 0 ? result.Patterns :
                pattern == null ? new System.Collections.Generic.List<DollarPattern>() :
                new System.Collections.Generic.List<DollarPattern> { pattern };
            foreach (var period in periods)
            {
                int row = period.Horizon == 20 ? 2 : period.Horizon == 5 ? 1 : 0;
                if (!DollarAnalysis.Fresh(period, now)) continue;
                _periodCells[row, 0].Text = period.UpPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                _periodCells[row, 1].Text = period.FlatPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                _periodCells[row, 2].Text = period.DownPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            }
            if (DollarAnalysis.Fresh(result, now))
            {
                _up.Text = pattern.UpPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                _flat.Text = pattern.FlatPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                _down.Text = pattern.DownPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                _patternInfo.Text = string.Format(CultureInfo.InvariantCulture,
                    "유사 사례 {0}회 · 최근 5관측일 {1:+0.00;-0.00;0.00}%\n" + result.HistorySource + " 기준 {2:yyyy-MM-dd}",
                    pattern.Count, pattern.FiveDayPercent, pattern.LatestDate);
            }
            else if (pattern != null)
                _patternInfo.Text = "판단 보류 · " + (pattern.Enough ? "이력이 오래됐습니다" : "유사 사례가 30회 미만입니다") +
                    "\n기준일 " + pattern.LatestDate.ToString("yyyy-MM-dd") + " · 사례 " + pattern.Count + "회";
            if (pattern != null)
            {
                _patternInfo.Text += "\n" + BandText(result.RoundTripPercent);
                // 뉴스가 아닌 숫자 맥락. 없으면 없다고 하지, 빈칸을 그럴듯하게 채우지 않는다.
                if (result.Context != null && result.Context.Ok) _patternInfo.Text += "\n" + result.Context.Summary();
                if (result.Context != null && result.Context.YieldOk) _patternInfo.Text += "\n" + result.Context.YieldSummary();
                _patternInfo.Text += "\n사례: " + string.Join(" · ", periods.Select(p => PeriodName(p.Horizon) + " " + p.Count + "회"));
                // 제외 폭은 기간마다 다르다(Sample 의 target - 4 - horizon). 일간 5, 주간 9, 월간 24.
                // 한 숫자로 적으면 주간·월간에서 틀린 말이 된다.
                _method.Text = "수신 이력 중 전일 등락 부호가 같은 사례입니다(v1.039 부터 - 전 조건은 기후값보다 유의하게 나빴습니다). 결과가 기준일과 겹치는 최근 사례는 제외합니다(일간 5·주간 9·월간 24개).\n" +
                    result.HistorySource + "의 과거 변동 폭을 사용하며 그래프의 시작점은 오늘은 공유 시세입니다. " + _target.PeriodBasis + ".\n";
                foreach (var period in periods)
                {
                    if (period.ValidationCount == 0) { _method.Text += PeriodName(period.Horizon) + ": 검증 사례 부족\n"; continue; }
                    _method.Text += string.Format(CultureInfo.InvariantCulture,
                        "{0} 검증 {1}회: 적중 {2:0.0}% · 단순 빈도 기준 {3:0.0}%.\n",
                        PeriodName(period.Horizon), period.ValidationCount, 100.0 * period.ValidationHits / period.ValidationCount,
                        100.0 * period.BaselineHits / period.ValidationCount);
                }
                foreach (var period in periods.Where(p => p.ValidationCount > 0))
                    _method.Text += string.Format(CultureInfo.InvariantCulture,
                        "{0} 가격 역검증: 평균 오차 {1:N2} / 값 유지 기준 {2:N2} · 범위 포함 {3:0.0}% ({4}회).\n",
                        PeriodName(period.Horizon), period.PriceErrorSum / period.ValidationCount,
                        period.PersistenceErrorSum / period.ValidationCount,
                        100.0 * period.IntervalHits / period.ValidationCount, period.ValidationCount);
                // ★ 기후값 대비 성적. '무작위 0.667 보다 낫다' 는 잘못된 기준이었다.
                //   상대는 그때까지의 실현 빈도를 그대로 내놓는 예측기다.
                foreach (var p in periods.Where(x => x.Score != null && x.Score.Count > 0)) {
                    var sc = p.Score;
                    _method.Text += string.Format(CultureInfo.InvariantCulture,
                        "{0} 기후값 대비: 실력 {1:+0.000;-0.000;0.000} (Brier {2:0.000} / 기후값 {3:0.000}) · 구별력 {4:0.000} · 눈금오차 {5:0.000} · 잔차 {8:0.000} · {6}회\n   → {7}\n",
                        PeriodName(p.Horizon), sc.Skill, sc.Brier, sc.Climatology, sc.Resolution, sc.Reliability, sc.Count,
                        ProbabilityCalibration.Diagnosis(sc), sc.Residual);
                }
                foreach (var p in periods.Where(p => p.Calibration != null)) {
                    var c = p.Calibration;
                    if (c.Count == 0) { _method.Text += PeriodName(p.Horizon) + " 확률 보정: 평가 자료 부족 · 적용 보류.\n"; continue; }
                    _method.Text += string.Format(CultureInfo.InvariantCulture, "{0} 확률 보정: {1} · 순차 평가 {2}회, Brier {3:0.000} / 기존 {4:0.000} / 전체 빈도 {5:0.000}.\n",
                        PeriodName(p.Horizon), c.Ready ? "통계 보조값에 적용" : "적용 보류", c.Count, c.Brier, c.RawBrier, c.BaselineBrier);
                }
                _method.Text += "가격 역검증은 통계 중앙값만 평가합니다. 종합 점수·뉴스 결합 추정은 과거 뉴스 자료가 없어 미검증입니다. 범위는 과거 10~90백분위이며 미래 80% 신뢰구간이 아닙니다.\n";
                _method.Text += "주·월 검증은 결과 기간이 겹치지 않게 간격을 두므로 검증 횟수가 적습니다.\n";
                // 비용을 안 넣었으면 그렇다고 말한다. 넣은 척하는 것보다 낫다.
                if (result.Context != null && result.Context.YieldOk)
                    _method.Text += result.Context.YieldSummary() + ". 참고용이며 확률에 반영되지 않았습니다.\n";
                if (result.Context != null && result.Context.Ok)
                    _method.Text += result.Context.Summary() +
                        ". 이 숫자는 참고용이며 위 확률에는 반영되지 않았습니다 - 반영하려면 기후값 대비 실력이 오르는지 먼저 재야 합니다.\n";
                _method.Text += _roundTrip > 0
                    ? "왕복 거래비용 " + _roundTrip.ToString("0.###", CultureInfo.InvariantCulture) + "% 를 보합 문턱에 반영했습니다. 그 안의 변동은 맞혀도 쓸모가 없습니다.\n"
                    : "왕복 거래비용이 설정되지 않아 보합 문턱에 반영되지 않았습니다(roundTripCost). 지금 문턱은 비용보다 좁을 수 있습니다.\n";
                // ★ 규칙을 더 넣기 전에 잴 자리 ★
                //   남은 감사 지적은 전부 '규칙이 없어서 침묵' 이다. 규칙을 넣으면 그 침묵은
                //   사라지지만, 차원이 늘면 자료가 더 쪼개져 표본이 문턱 아래로 떨어진다.
                //   넣어도 되는지 여기서 먼저 본다.
                if (result.Quote != null)
                {
                    _method.Text += RuleSkill.Report(RuleSkill.Ledger(
                        PredictionJournal.Folder, result.Quote.IdentityKey, result.Quote.Source));
                    // 채점은 신선한 시세가 있어야 돈다. 위젯이 접혀 있거나 꺼져 있으면
                    // 그만큼 표본을 잃는데, 그 손실이 아무 데도 안 보이면 없는 것처럼 여겨진다.
                    int missed = PredictionJournal.Missed(result.Quote.IdentityKey, result.Quote.Source, _utcNow());
                    if (missed > 0)
                        _method.Text += "채점 창(만기+6시간)을 놓쳐 영영 채점할 수 없게 된 예측 " + missed +
                            "건. 시세를 접어 두거나 위젯이 꺼져 있으면 그만큼 표본을 잃습니다.\n";
                }
                // 여러 번 고쳐 보고 좋아 보일 때 고르면 그 성적은 우연일 수 있다.
                // 몇 번 고쳤는지를 밝혀야 성적을 얼마나 깎아 읽을지 판단할 수 있다.
                _method.Text += "채점에 영향을 준 변경 " + Config.ScoringRevisions.Length + "회(v" +
                    string.Join(", v", Config.ScoringRevisions) + "). 여러 번 고친 뒤의 성적은 그만큼 우연일 여지가 큽니다.\n";
                _method.Text += pattern.ValidationCount < 60 ? "검증 사례가 적어 예측 성능을 판단하기 어렵습니다." :
                    pattern.ValidationHits <= pattern.BaselineHits ? "단순 기준보다 나은 예측 성능은 확인되지 않았습니다." :
                    "과거 구간의 비교 결과이며, 미래의 성능이나 확률 보정을 보장하지 않습니다.";
            }
            _score.Text = ScoreText(result, now);
            _score.Text += result.Spark != null ? "Spark의 기간별 판단 점수 · 뉴스 최대 80 / 과거 최대 20. 확률이나 검증된 적중률이 아닙니다." :
                "규칙 참고: 뉴스 요인 최대 80·과거 최대 20점. 기사 요인 기반 계수 0.5, 수집 누락 시 추가 감점.\n24시간 뉴스의 주간 가중 0.5·월간 0.25. 실증 학습된 가중치가 아닙니다.";
            // ★ _summary 는 일간 방향 칸과 같은 개체다 ★
            //   여기서 문구와 색을 정해도 바로 아래 RenderEvidence 가 같은 칸에 ScenarioOutlook 을
            //   다시 써서 덮었다. 그래서 '변화 주시'·'과거 참고' 와 ±5점 색 규칙은 한 번도 화면에
            //   오지 못한 죽은 코드였다. 문구는 한 곳에서만 정한다 - ScenarioOutlook 이 맡는다.
            _brief.Text = DollarFactors.Brief(result, now);
            _factorDetail.Text = DollarFactors.Narrative(result, now);
            var foundCategories = DollarFactors.Collect(result, now).Select(f => f.Category).Distinct().ToList();
            _factorDetail.Text += "\n확인한 요인: " + string.Join(" · ", foundCategories);
            if (_target.Dollar) _factorDetail.Text += "\n추가 확인: " + string.Join(" · ", DollarFactors.Categories.Except(foundCategories));
            RenderEvidence(result, now);
            if (result.Spark != null)
            {
                var ai = result.Spark.Periods.First(p => p.Horizon == 1);
                _brief.Text = ai.Reason + "\n반대: " + ai.Counter + "\n판단 변경: " + ai.Change;
                _factorDetail.Text = string.Join("\n\n", result.Spark.Periods.Select(p => PeriodName(p.Horizon) + ": " + p.Reason + "\n반대: " + p.Counter + "\n판단 변경: " + p.Change));
            }
            DrawChart(result, now);
            int upNews = result.News.Count(n => n.EvidenceDirection > 0);
            int downNews = result.News.Count(n => n.EvidenceDirection < 0);
            _newsInfo.Text = "최근 24시간 · " + _target.Name + " 영향: 상승 " + upNews + " / 하락 " + downNews +
                "\n선택 품목의 정책·수급·실적과 반대 해석을 표시합니다. 제목·요약·수집된 공개 본문 범위를 따릅니다.";
            _newsInfo.Text += "\n기사 " + result.News.Count + "건 · 본문 발췌 " + result.News.Count(n => n.BodyRead) + "건 · 추가 수집 " + result.TopicFeedsAvailable + "/" + result.TopicFeedsExpected;
            RenderNews(result, true, "국내", result.DomesticAvailable || result.News.Any(n => n.Domestic));
            RenderNews(result, false, "해외", result.GlobalAvailable || result.News.Any(n => !n.Domestic));
            bool any = (result.Quote != null && result.Quote.Ok) || result.Rates.Count > 0 || result.DomesticAvailable || result.GlobalAvailable;
            _status.Text = (any ? "조회 완료 " : "조회 실패 ") + result.CheckedUtc.ToLocalTime().ToString("MM-dd HH:mm:ss") +
                (any && (!result.DomesticAvailable || !result.GlobalAvailable || result.TopicFeedsAvailable < result.TopicFeedsExpected || result.Rates.Count == 0 || result.Quote == null || !result.Quote.Ok) ? " · 일부 자료 누락" : "");
            _status.Text += "\n" + (!result.Extreme ? "규칙 참고" : result.Spark != null ? DollarSpark.ModelName(result.Spark.ModelId) + " 분석" : string.IsNullOrEmpty(result.SparkStatus) ? "규칙 참고 · Spark 미사용" : result.SparkStatus + " · 규칙 참고");
            _status.Text += " · " + (result.Spark != null ? "주전망" : "비교 기준");
            if (result.Extreme && result.Spark == null) _status.Text += "\nAI 주전망은 아직 생성되지 않았습니다";
            if (_target.Economic) _method.Text = "현재 ECOS 공표값과 " + result.Rates.Count + "개 월간 관측값을 참고합니다. 월간 값을 일별 가격 패턴으로 바꾸지 않습니다.\n지표 예상값은 기사 근거와 현재 값이 있는 Spark 응답의 절대 변화량으로 표시하며, 금리 변화는 %p입니다.";
            if (_target.Weather) { _brief.Text = "Open-Meteo 일평균 기온 예보입니다. 1개월 예보는 제공 범위를 벗어나 표시하지 않습니다."; _status.Text = "Open-Meteo 예보 · " + result.CheckedUtc.ToLocalTime().ToString("MM-dd HH:mm"); }
        }

        private void RenderEvidence(DollarAnalysisResult result, DateTime now)
        {
            int index = 0;
            foreach (int h in new[] { 1, 5, 20 })
            {
                var score = DollarAnalysis.Score(result, h, now);
                var list = _evidenceLists[index];
                _periodDirections[index].Text = ScenarioOutlook(result, h, now);
                _periodDirections[index].Foreground = _periodDirections[index].Text == "보합" ? Palette.TextDim : DirectionColor(score);
                _periodPoints[index].Text = score.Available ? DisplayPoints(score.Value) : "—점";
                _periodPoints[index].ToolTip = "방향 압력 점수 · 적중 확률이 아닙니다";
                _periodPoints[index].Foreground = DirectionColor(score);
                _evidenceCounts[index].Text = (score.IsAi ? "인용 " : "검토 ") + score.Evidence.Count + "건";
                var p = result.Patterns.FirstOrDefault(v => v.Horizon == h) ?? (result.Pattern != null && result.Pattern.Horizon == h ? result.Pattern : null);
                list.Children.Add(Label((score.IsAi ? "Spark 인용 " : "규칙 산출·교차 확인 ") + score.Evidence.Count + "건 / 검토 " + score.ReviewedCount +
                    "건 · 과거 가격 사례 " + (DollarAnalysis.Fresh(p, now) ? p.Count : 0) + "회 별도", 10, Palette.TextDim));
                var ai = result.Spark == null ? null : result.Spark.Periods.FirstOrDefault(v => v.Horizon == h);
                string pressure = _periodDirections[index].Text == "보합" ? Pressure(score) : "";
                _periodReasons[index].Text = (pressure.Length > 0 ? pressure + " · " : "근거: ") + (ai != null ? ai.Reason : MainReason(score));
                _periodReasons[index].ToolTip = _periodReasons[index].Text;
                if (ai != null) {
                    _periodChecks[index].Visibility = Visibility.Visible;
                    _periodChecks[index].Text = "반박: " + Compact(ai.Counter) + "\n전환: " + Compact(ai.Change);
                    _periodChecks[index].ToolTip = "반박: " + ai.Counter + "\n전환: " + ai.Change;
                    list.Children.Add(Label(ai.Reason + "\n반대: " + ai.Counter + "\n판단 변경: " + ai.Change + "\n근거 충실도: " +
                        (ai.Confidence == "high" ? "높음" : ai.Confidence == "medium" ? "보통" : "낮음") + " (모델 자체 평가 · 적중률 아님)", 11, Palette.Text));
                }
                else list.Children.Add(Label("동일한 24시간 뉴스에 기간별 가중치를 적용한 규칙 참고입니다.", 10, Palette.TextDim));
                foreach (var entry in score.Evidence)
                {
                    var n = entry.News; var text = new StackPanel();
                    text.Children.Add(Label(n.Title, 11, Palette.Text));
                    text.Children.Add(Label(n.Source + " · " + n.PublishedUtc.AddHours(9).ToString("MM-dd HH:mm") + " KST · " +
                        (n.BodyRead ? "공개 본문 발췌" : string.IsNullOrEmpty(n.Context) ? "제목" : "제목·요약"), 9.5, Palette.TextDim));
                    string role = entry.Role == "counter" ? "반대" : entry.Role == "mixed" ? "양면" : entry.Role == "support" ? "지지" : "참고";
                    text.Children.Add(Label(score.IsAi ? role + " · “" + entry.Quote + "”" : entry.CrossCheckOnly ? "다른 매체의 교차 확인 · 중복 점수 없음" :
                        string.Format(CultureInfo.InvariantCulture, "점수 기여 {0:+0.00;-0.00;0.00} · {1}", entry.Up - entry.Down, n.EvidenceReason), 10, Palette.TextDim));
                    if (!score.IsAi) foreach (var factor in DollarFactors.Analyze(n))
                        text.Children.Add(Label(factor.Mechanism + "\n반대 해석: " + factor.Counter, 10, Palette.TextDim));
                    var link = ActionButton("", double.NaN); link.Content = text; link.Height = double.NaN;
                    link.HorizontalContentAlignment = HorizontalAlignment.Stretch; link.Margin = new Thickness(0, 4, 0, 4);
                    string url = n.Url; link.IsEnabled = DollarAnalysis.IsNewsLink(url);
                    link.Click += (s, e) => { if (DollarAnalysis.IsNewsLink(url)) Net.OpenLink(url); };
                    list.Children.Add(link);
                }
                index++;
            }
        }

        private void RenderNews(DollarAnalysisResult result, bool domestic, string heading, bool available)
        {
            _news.Children.Add(Label(heading, 12, Palette.Text));
            var articles = result.News.Where(n => n.Domestic == domestic).ToList();
            if (!available || articles.Count == 0)
                _news.Children.Add(Label(available ? "최근 24시간에 조건에 맞는 기사가 없습니다" : "뉴스 수신 실패 · 다시 갱신해 주세요", 11, Palette.TextDim));
            foreach (var news in articles)
            {
                var content = new StackPanel();
                content.Children.Add(Label(news.Title, 12, Palette.Text));
                DollarAnalysis.Classify(news);
                string direction = news.EvidenceDirection > 0 ? "상승 근거 (+)" : news.EvidenceDirection < 0 ? "하락·반박 근거 (-)" : "중립 (0)";
                content.Children.Add(Label(news.Topic + " · " + direction + " · " +
                    news.PublishedUtc.AddHours(9).ToString("MM-dd HH:mm") + " KST", 10,
                    news.Direction > 0 ? Palette.Up : news.Direction < 0 ? Palette.Down : Palette.TextDim));
                content.Children.Add(Label(news.EvidenceReason, 10, Palette.TextDim));
                content.Children.Add(Label(news.BodyRead ? "분석 원문 범위: 공개 본문 발췌" : string.IsNullOrEmpty(news.Context) ? "분석 원문 범위: 제목" : "분석 원문 범위: 제목·RSS 제공 요약", 9.5, Palette.TextFaint));
                var button = ActionButton("", double.NaN);
                button.Content = content;
                button.Height = double.NaN;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.Margin = new Thickness(0, 5, 0, 6);
                button.ToolTip = news.Source + " · 기사 열기";
                string url = news.Url;
                button.IsEnabled = DollarAnalysis.IsNewsLink(url);
                button.Click += (s, e) => { if (DollarAnalysis.IsNewsLink(url)) Net.OpenLink(url); };
                _news.Children.Add(button);
            }
            _news.Children.Add(new Border { Height = 10 });
        }

        private void DrawChart(DollarAnalysisResult result, DateTime now)
        {
            if (_target.Economic || _target.Weather) { DrawDirectForecast(result, now); return; }
            var periods = (result.Patterns.Count > 0 ? result.Patterns :
                new System.Collections.Generic.List<DollarPattern> { result.Pattern })
                .Where(p => p != null && DollarAnalysis.Fresh(p, now) && p.Returns.Count >= 30)
                .OrderBy(p => p.Horizon).ToList();
            if (result.Rates.Count < 2 || periods.Count == 0)
            {
                _chart.Children.Add(Label("예상 그래프를 만들 자료가 부족합니다", 11, Palette.TextDim));
                return;
            }
            double anchor = PredictionTarget.Number(result.Quote);
            if (double.IsNaN(anchor) || anchor <= 0) { _chart.Children.Add(Label("현재 기준 시세를 확인한 뒤 예측합니다", 11, Palette.TextDim)); return; }
            double min = Math.Min(anchor, periods.Min(p => anchor * (1 + p.LowerReturn)));
            double max = Math.Max(anchor, periods.Max(p => anchor * (1 + p.UpperReturn)));
            min = Math.Min(min, periods.Min(p => anchor * (1 + DollarAnalysis.ForecastReturn(p, DollarAnalysis.Score(result, p.Horizon, now)))));
            max = Math.Max(max, periods.Max(p => anchor * (1 + DollarAnalysis.ForecastReturn(p, DollarAnalysis.Score(result, p.Horizon, now)))));
            double padding = Math.Max(anchor * 0.0001, (max - min) * 0.12);
            min -= padding; max += padding;
            double width = Math.Max(180, _chart.ActualWidth > 0 ? _chart.ActualWidth : Width - 64);
            // Horizontal distance is proportional to the number of future reference-rate observations.
            Func<int, double> x = h => 42 + h * (width - 48) / 20;
            Func<double, double> y = price => 105 - (price - min) / (max - min) * 82;
            var band = new Polygon { Fill = Palette.Accent, Opacity = 0.16 };
            band.Points.Add(new Point(x(0), y(anchor)));
            foreach (var p in periods) band.Points.Add(new Point(x(p.Horizon), y(anchor * (1 + p.UpperReturn))));
            foreach (var p in periods.AsEnumerable().Reverse()) band.Points.Add(new Point(x(p.Horizon), y(anchor * (1 + p.LowerReturn))));
            _chart.Children.Add(band);
            var line = new Polyline { Stroke = Palette.Accent, StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 } };
            line.Points.Add(new Point(x(0), y(anchor)));
            foreach (var p in periods) line.Points.Add(new Point(x(p.Horizon), y(anchor * (1 + p.MedianReturn))));
            if (result.Spark == null) _chart.Children.Add(line);
            line.Stroke = Palette.TextFaint;
            var integrated = new Polyline { Stroke = Palette.Accent, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 } };
            integrated.Points.Add(new Point(x(0), y(anchor)));
            foreach (var p in periods)
            {
                var score = DollarAnalysis.Score(result, p.Horizon, now);
                if (score.Available) integrated.Points.Add(new Point(x(p.Horizon), y(anchor * (1 + DollarAnalysis.ForecastReturn(p, score)))));
            }
            if (integrated.Points.Count > 1) _chart.Children.Add(integrated);
            foreach (var point in result.Spark != null ? integrated.Points : line.Points)
            {
                var dot = new Ellipse { Width = 5, Height = 5, Fill = Palette.Accent };
                Canvas.SetLeft(dot, point.X - 2.5); Canvas.SetTop(dot, point.Y - 2.5); _chart.Children.Add(dot);
            }
            var top = Label(max.ToString(Math.Abs(max) < 100 ? "0.####" : "N0"), 9, Palette.TextFaint);
            var bottom = Label(min.ToString(Math.Abs(min) < 100 ? "0.####" : "N0"), 9, Palette.TextFaint);
            Canvas.SetTop(top, 15); Canvas.SetTop(bottom, 94);
            _chart.Children.Add(top); _chart.Children.Add(bottom);
            foreach (int h in new[] { 0, 5, 20 })
            {
                var tick = Label(h == 0 ? "현재" : "+" + _target.Steps(h) + "일", 9, Palette.TextFaint);
                Canvas.SetLeft(tick, x(h) - (h == 20 ? 28 : 8)); Canvas.SetTop(tick, 112);
                _chart.Children.Add(tick);
            }
            var key = Label(result.Spark != null ? "파랑: AI 시나리오 · 음영: 과거 참고" : "파랑: 규칙 시나리오 · 회색·음영: 과거 참고", 9.5, Palette.TextDim);
            key.Width = width; Canvas.SetTop(key, 135); _chart.Children.Add(key);
            int row = 0;
            _forecastDetail.Text = "예상값과 참고 범위\n";
            foreach (int h in new[] { 1, 5, 20 })
            {
                var p = periods.FirstOrDefault(v => v.Horizon == h);
                var score = DollarAnalysis.Score(result, h, now);
                if (p != null)
                {
                    double estimate = anchor * (1 + DollarAnalysis.ForecastReturn(p, score));
                    _forecastValues[row].Text = "약 " + _target.Format(estimate, result.Quote, true);
                    _forecastValues[row].ToolTip = _target.Format(estimate, result.Quote);
                    _forecastChanges[row].Text = _target.Change(anchor, estimate, result.Quote);
                    _forecastChanges[row].Foreground = Math.Abs(estimate - anchor) < 0.00005 ? Palette.TextDim : estimate > anchor ? Palette.Up : Palette.Down;
                    _forecastStates[row].Text = ScenarioOutlook(result, h, now);
                    _forecastDetail.Text += string.Format(CultureInfo.InvariantCulture,
                        "{0} (+{1}관측일): {2} ({3}~{4}){5}\n", PeriodName(h), _target.Steps(h), _target.Format(estimate, result.Quote),
                        _target.Format(anchor * (1 + p.LowerReturn), result.Quote), _target.Format(anchor * (1 + p.UpperReturn), result.Quote), score.Available ? "" : " · 통계 참고");
                }
                row++;
            }
            _forecastBasis.Text = "시작: 오늘은 " + _target.Format(anchor, result.Quote) + "\n변동 참고: " + result.HistorySource + " " + result.Rates.Last().Date.ToString("MM-dd") + " · " + _target.PeriodBasis;
            _forecastDetail.Text += "뉴스 판단이 있으면 점수/100 × 과거 10·90백분위 변동의 최대 절댓값으로 예상 시나리오를 만듭니다. " +
                "표시가 0.0점이면 기준값을 유지하며, 뉴스 판단이 없으면 과거 중앙값을 참고합니다. " +
                "방향을 유지하는 임시 환산이며 가격 예측 성능은 미검증입니다. 음영은 과거 참고 범위입니다.";
        }

        private void DrawDirectForecast(DollarAnalysisResult result, DateTime now)
        {
            double anchor = PredictionTarget.Number(result.Quote);
            var points = result.DirectForecasts.ToList();
            if (_target.Economic && result.Spark != null)
                foreach (var period in result.Spark.Periods.Where(p => p.ValueChange.HasValue))
                    if (DollarAnalysis.Score(result, period.Horizon, now).IsAi)
                        points.Add(new PredictionPoint { Horizon = period.Horizon, Value = anchor + period.ValueChange.Value });
            _forecastBasis.Text = "시작: 오늘은 " + (double.IsNaN(anchor) ? "시세 확인 중" : _target.Format(anchor, result.Quote)) + "\n" + _target.PeriodBasis;
            if (double.IsNaN(anchor) || points.Count == 0)
            {
                _chart.Children.Add(Label(_target.Weather ? "예보 자료를 받지 못했습니다" : "지표 예상값은 Spark로 기사·발표 근거를 분석한 뒤 표시합니다", 11, Palette.TextDim));
                return;
            }
            double min = Math.Min(anchor, points.Min(p => p.Value)), max = Math.Max(anchor, points.Max(p => p.Value));
            double margin = Math.Max(0.1, (max - min) * 0.2); min -= margin; max += margin;
            double width = Math.Max(180, _chart.ActualWidth > 0 ? _chart.ActualWidth : Width - 64);
            Func<int, double> x = h => 42 + _target.Steps(h) * (width - 48) / 30;
            Func<double, double> y = value => 105 - (value - min) / (max - min) * 82;
            var line = new Polyline { Stroke = Palette.Accent, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 } };
            line.Points.Add(new Point(42, y(anchor)));
            foreach (var point in points.OrderBy(p => p.Horizon)) line.Points.Add(new Point(x(point.Horizon), y(point.Value)));
            _chart.Children.Add(line);
            int index = 0;
            foreach (int h in new[] { 1, 5, 20 })
            {
                var point = points.FirstOrDefault(p => p.Horizon == h);
                if (point != null)
                {
                    _forecastValues[index].Text = (_target.Weather ? "" : "약 ") + _target.Format(point.Value, result.Quote, true);
                    _forecastChanges[index].Text = _target.Change(anchor, point.Value, result.Quote);
                    _forecastChanges[index].Foreground = point.Value > anchor ? Palette.Up : point.Value < anchor ? Palette.Down : Palette.TextDim;
                    _forecastStates[index].Text = _target.Weather ? "일평균 예보" : "Spark 시나리오";
                    if (_target.Weather)
                    {
                        _periodDirections[index].Text = "기상 예보"; _periodReasons[index].Text = point.Date.ToString("MM-dd") + " 일평균 기온"; _evidenceCounts[index].Text = "예보 1건";
                        _evidenceLists[index].Children.Clear();
                        _evidenceLists[index].Children.Add(Label("Open-Meteo · " + _target.Name + "\n" + point.Date.ToString("yyyy-MM-dd") + " 일평균 기온 " + _target.Format(point.Value, result.Quote) + "\n조회 " + result.CheckedUtc.ToLocalTime().ToString("MM-dd HH:mm"), 11, Palette.Text));
                    }
                }
                else { _forecastStates[index].Text = "미제공"; }
                index++;
            }
            foreach (int h in new[] { 0, 5, 20 })
            {
                var tick = Label(h == 0 ? "현재" : "+" + _target.Steps(h) + "일", 9, Palette.TextFaint);
                Canvas.SetLeft(tick, h == 0 ? 34 : x(h) - (h == 20 ? 28 : 8)); Canvas.SetTop(tick, 112); _chart.Children.Add(tick);
            }
            var top = Label(max.ToString("0.##"), 9, Palette.TextFaint); Canvas.SetTop(top, 15); _chart.Children.Add(top);
            var bottom = Label(min.ToString("0.##"), 9, Palette.TextFaint); Canvas.SetTop(bottom, 94); _chart.Children.Add(bottom);
            _forecastDetail.Text = _target.Weather ? "동일 지역의 Open-Meteo 일평균 기온 예보. 30일 예보를 지어내지 않습니다." : "각 기간의 기사 근거를 해석한 절대 변화 시나리오입니다. 과거 통계로 보정한 확률이 아니며 예측 성능은 미검증입니다.";
        }

        internal static double? ScenarioChange(DollarAnalysisResult result, int horizon, DateTime now)
        {
            if (result == null || result.Target.Economic || result.Target.Weather) return null;
            var score = DollarAnalysis.Score(result, horizon, now);
            var pattern = result.Patterns.FirstOrDefault(p => p.Horizon == horizon) ??
                (result.Pattern != null && result.Pattern.Horizon == horizon ? result.Pattern : null);
            if (!score.Available || !DollarAnalysis.Fresh(pattern, now) || pattern.Returns.Count < 30) return null;
            double change = DollarAnalysis.ForecastReturn(pattern, score);
            return double.IsNaN(change) || double.IsInfinity(change) ? null : (double?)change;
        }

        /// <summary>
        /// 누른 키가 글자 크기 조절이면 그 크기 변화량을, 아니면 NaN 을 준다.
        /// 키 배선과 따로 떼어 두어야 검사가 조합을 직접 확인할 수 있다 -
        /// 전에는 ZoomText 만 검사해서 키가 함수까지 오는지는 아무도 안 봤다.
        /// </summary>
        internal static double ZoomStep(Key key, ModifierKeys modifiers)
        {
            if ((modifiers & ModifierKeys.Alt) == 0) return double.NaN;
            if (key == Key.OemMinus || key == Key.Subtract) return -0.1;
            if (key == Key.OemPlus || key == Key.Add) return 0.1;
            if (key == Key.D0 || key == Key.NumPad0) return 0;
            return double.NaN;
        }

        /// <summary>글자 크기를 한 칸 키우거나 줄인다. step 0 은 기본값으로 되돌린다.</summary>
        internal void ZoomText(double step)
        {
            // ★ 한 칸 움직일 기준은 이 창의 지금 크기다 ★
            //   품목마다 창이 따로 열리고 다른 창은 배율을 따라가지 않는데, 다음 값을 설정 파일에서
            //   구하면 다른 창에서 키운 만큼 이 창이 갑자기 건너뛴다.
            double next = Config.ScaleOrDefault(step == 0 ? 1.0 : Math.Round(_textScale.ScaleX + step, 2));
            if (Math.Abs(next - _textScale.ScaleX) < 0.0005) return;
            _cfg.AnalysisScale = next;
            _textScale.ScaleX = _textScale.ScaleY = next;
            // 다음에 여는 창도 같은 크기로 뜬다. 이미 열려 있는 다른 창은 그대로 둔다.
            _cfg.Save();
        }

        /// <summary>
        /// 이 기간의 규칙 예측을 내놓지 말아야 하는가.
        ///
        /// ★ 스스로 잰 성적이 '기후값보다 못하다' 고 말하면 방향을 말하지 않는다 ★
        ///   맞히지 못한다는 것을 알면서 방향을 내놓는 것은 사용자를 속이는 것이다.
        ///   기후값은 그때까지의 실현 빈도를 그대로 내놓는 예측기이고, 그보다 못하면
        ///   조건을 나눈 것이 도움이 되지 않았다는 뜻이다(v1.017 실측: 실력 -0.032).
        ///   표본이 모자라면 판단하지 않는다 - 모르는 것과 나쁜 것은 다르다.
        ///   AI 예측에는 걸지 않는다. 이 성적은 과거 가격 패턴을 잰 것이지 AI 를 잰 것이 아니다.
        /// </summary>
        internal static bool Abstains(DollarAnalysisResult result, int horizon, DateTime now)
        {
            if (result == null || result.Target.Weather || result.Target.Economic) return false;
            if (DollarAnalysis.Score(result, horizon, now).IsAi) return false;
            var pattern = result.Patterns.FirstOrDefault(p => p.Horizon == horizon) ??
                (result.Pattern != null && result.Pattern.Horizon == horizon ? result.Pattern : null);
            if (pattern == null || pattern.Score == null || !pattern.Score.Enough) return false;
            return pattern.Score.Skill <= 0;
        }

        internal static string ScenarioOutlook(DollarAnalysisResult result, int horizon, DateTime now)
        {
            if (result == null) return "자료 부족";
            if (result.Target.Economic) {
                var ai = result.Spark == null ? null : result.Spark.Periods.FirstOrDefault(p => p.Horizon == horizon);
                if (ai == null || !ai.ValueChange.HasValue || !DollarAnalysis.Score(result, horizon, now).IsAi) return "자료 부족";
                return ai.ValueChange.Value > 0 ? "상승" : ai.ValueChange.Value < 0 ? "하락" : "보합";
            }
            var score = DollarAnalysis.Score(result, horizon, now);
            if (!score.Available) return "과거 참고";
            // 스스로 잰 성적이 기후값보다 못하면 방향을 내놓지 않는다.
            if (Abstains(result, horizon, now)) return "판단 보류";
            double? change = ScenarioChange(result, horizon, now);
            if (!change.HasValue) return "변동폭 미산정";
            // 왕복 비용 아래 구간은 맞혀도 쓸모가 없다. 비용을 안 넣었으면 종전 문턱이다.
            int direction = DollarAnalysis.DirectionAt(change.Value, DollarAnalysis.Threshold(horizon, result.RoundTripPercent));
            return direction > 0 ? "상승" : direction < 0 ? "하락" : "보합";
        }

        internal static string Pressure(DollarScore score)
        { return !score.Available || Math.Abs(score.Value) < 0.05 ? "" : score.Value > 0 ? "미세 상승 압력" : "미세 하락 압력"; }

        private static string Compact(string text)
        {
            text = DollarAnalysis.Clean(text, 2000);
            if (text.Length <= 56) return text;
            int length = char.IsHighSurrogate(text[55]) ? 55 : 56;
            return text.Substring(0, length).TrimEnd() + "…";
        }

        /// <summary>
        /// 근거가 어느 쪽으로 기울었나. ★ 이것은 최종 방향이 아니다 ★
        ///
        ///   같은 화면에서 점수 줄은 '상승 우세', 방향 줄은 '보합' 을 동시에 말했다.
        ///   둘 다 틀린 게 아니라 서로 다른 것을 잰다 - 이쪽은 근거 점수의 부호이고,
        ///   저쪽은 그 점수로 만든 예상 변동폭이 보합 범위를 넘는지다. 화면이 그 말을
        ///   하지 않아서 보는 사람에게는 앞뒤가 안 맞는 두 결론으로만 보였다.
        ///   그래서 말을 '근거' 로 못박고, 어긋날 때는 왜 어긋나는지 한 줄 적는다.
        /// </summary>
        internal static string Outlook(DollarScore score)
        {
            return !score.Available ? "자료 부족" : score.Value > 5 ? "상승 근거 우세" : score.Value < -5 ? "하락 근거 우세" :
                score.Value >= 0.05 ? "상승 쪽" : score.Value <= -0.05 ? "하락 쪽" : "혼조";
        }

        /// <summary>근거와 최종 방향이 어긋나면 그 까닭을 한 줄로. 같으면 빈 문자열.</summary>
        internal static string Reconcile(string evidence, string direction)
        {
            // ★ 아는 방향만 다룬다 ★
            //   막을 것을 나열하면 새 문구가 생길 때마다 빠뜨린다. 실제로 '자료 부족' 이
            //   방향 쪽에도 올 수 있다는 것을 빠뜨려, 변동폭이 산정된 적도 없는데
            //   '보합 범위 안이라 보합' 이라고 적었다. 아는 것만 받는 쪽으로 뒤집는다.
            if (evidence == "자료 부족") return "";
            if (direction != "상승" && direction != "하락" && direction != "보합") return "";
            int e = evidence.StartsWith("상승", StringComparison.Ordinal) ? 1 : evidence.StartsWith("하락", StringComparison.Ordinal) ? -1 : 0;
            int d = direction == "상승" ? 1 : direction == "하락" ? -1 : 0;
            if (e == d) return "";
            if (d == 0) return "  근거는 " + (e > 0 ? "상승" : "하락") + " 쪽이지만 예상 변동폭이 보합 범위 안이라 방향은 보합입니다.\n";
            if (e == 0) return "  근거는 혼조인데 과거 통계가 방향을 " + (d > 0 ? "상승" : "하락") + "으로 정했습니다.\n";
            return "  근거는 " + (e > 0 ? "상승" : "하락") + " 쪽인데 방향은 " + (d > 0 ? "상승" : "하락") + "입니다. 과거 통계가 근거를 뒤집었습니다.\n";
        }

        internal static string DisplayPoints(double value) { return (Math.Abs(value) < 0.05 ? 0 : value).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "점"; }

        internal static string PriceChange(double anchor, double estimate)
        {
            double delta = estimate - anchor, percent = (estimate / anchor - 1) * 100;
            return (Math.Abs(delta) < 0.05 ? 0 : delta).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "원\n" +
                (Math.Abs(percent) < 0.005 ? 0 : percent).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%";
        }

        private static Brush DirectionColor(DollarScore score)
        {
            return !score.Available ? Palette.TextDim : score.Value >= 0.05 ? Palette.Up : score.Value <= -0.05 ? Palette.Down : Palette.TextDim;
        }

        internal static string MainReason(DollarScore score)
        {
            if (!score.Available) return "과거 흐름 참고 · 새 정책·수급 확인 중";
            if (Math.Abs(score.Value) < 0.05) return "상승 재료와 하락 재료가 상쇄";
            if (Math.Abs(score.History) > Math.Abs(score.UpEvidence - score.DownEvidence))
                return "과거 가격 흐름의 " + (score.History > 0 ? "상승" : "하락") + " 편향이 점수에 반영";
            int direction = score.Value > 0 ? 1 : -1;
            var entry = score.Evidence.Where(e => !e.CrossCheckOnly && (e.Up - e.Down) * direction > 0)
                .OrderByDescending(e => Math.Abs(e.Up - e.Down)).FirstOrDefault();
            if (entry == null) return "상반된 기사와 과거 흐름을 합산한 방향";
            var factor = DollarFactors.Analyze(entry.News).Where(f => f.Direction == direction).OrderByDescending(f => f.Weight).FirstOrDefault();
            return factor != null ? factor.Mechanism.Replace("이 요인만 보면 ", "") : entry.News.EvidenceReason;
        }

        private static TextBlock RowLabel(string text, double size, Brush color)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        }

        /// <summary>
        /// 이 기간에서 방향이 나오려면 근거가 최대치의 몇 %여야 하는가. 모르면 NaN.
        ///
        /// ★ 두 보수성이 겹쳐 있다 ★
        ///   24시간 뉴스의 기간 가중치(주 0.5·월 0.25)가 점수를 줄이는데, 보합 문턱은
        ///   기간이 길수록 넓어진다. 둘은 따로 정해졌고 아무도 그 곱을 본 적이 없다.
        ///   실측: 보합을 벗어나려면 필요한 근거가 USD/KRW 는 일간 21%·주간 54%·월간 65%,
        ///   BTC 는 7%·18%·27%, Dollar Tree 는 5%·14%·23% 다(최대치 대비).
        ///   ★ 진짜 원인은 기간 가중치가 아니라 고정 비율 문턱이다 - 품목마다 변동성이
        ///     서너 배 다른데 문턱은 같아서, 저변동 품목(USD/KRW)에서만 벽이 된다.
        ///   그래서 이 값은 품목마다 따로 재고, 50%를 넘을 때만 화면에 적는다.
        ///   고칠지는 채점이 쌓인 뒤에 정한다 - 지금 잣대를 또 옮기면 성적을 못 읽는다.
        ///   다만 그 사실을 화면이 말하지 않으면, 보는 사람은 '월간은 늘 보합' 을
        ///   시장이 조용해서라고 읽는다.
        /// </summary>
        internal static double Headroom(DollarAnalysisResult result, int horizon, DateTime now)
        {
            if (result == null) return double.NaN;
            var pattern = result.Patterns.FirstOrDefault(p => p.Horizon == horizon) ??
                (result.Pattern != null && result.Pattern.Horizon == horizon ? result.Pattern : null);
            if (!DollarAnalysis.Fresh(pattern, now)) return double.NaN;
            double need = DollarAnalysis.DirectionalNeed(pattern, DollarAnalysis.Threshold(horizon, result.RoundTripPercent));
            double ceiling = DollarAnalysis.ScoreCeiling(horizon, DollarAnalysis.Score(result, horizon, now).Reliability);
            if (double.IsNaN(need) || ceiling <= 0) return double.NaN;
            return need / ceiling * 100;
        }

        /// <summary>
        /// 기간별 점수 줄. ★ 화면에 붙이는 일과 따로 떼어 둔다 ★
        ///   전에 Alt 글자 크기에서 겪은 그대로다 - 계산 함수만 검사하면 그 함수가
        ///   화면까지 오는지는 아무도 안 본다. 문구를 통째로 돌려주게 해서, 검사가
        ///   '실제로 적히는 글' 을 확인할 수 있게 한다.
        /// </summary>
        internal static string ScoreText(DollarAnalysisResult result, DateTime now)
        {
            string text = "";
            foreach (int h in new[] { 1, 5, 20 })
            {
                var score = DollarAnalysis.Score(result, h, now);
                text += string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1:+0.0;-0.0;0.0}점 · {2}\n  상승 +{3:0.0} / 하락·반박 -{4:0.0} / 과거 {5:+0.0;-0.0;0.0} × 근거계수 {6:0.00}\n",
                    PeriodName(h), score.Value, Outlook(score),
                    score.UpEvidence, score.DownEvidence, score.History, score.Reliability);
                // 근거와 방향이 어긋나면 그 자리에서 까닭을 밝힌다. 두 결론을 나란히
                // 놓고 설명하지 않으면, 보는 사람은 둘 중 하나가 고장 났다고 읽는다.
                text += Reconcile(Outlook(score), ScenarioOutlook(result, h, now));
                // 구조상 방향이 나오기 어려운 기간이면 그렇다고 말한다. 말하지 않으면
                // 보는 사람은 '늘 보합' 을 시장이 조용해서라고 읽는다.
                double headroom = Headroom(result, h, now);
                if (!double.IsNaN(headroom) && headroom >= 50)
                    text += "  이 기간은 구조상 방향이 나오기 어렵습니다: 근거가 최대치의 " +
                        headroom.ToString("0", CultureInfo.InvariantCulture) + "%를 넘어야 보합을 벗어납니다.\n";
            }
            return text;
        }

        /// <summary>
        /// 화면에 적는 보합 범위. 실제 판정에 쓰는 문턱 그대로다.
        ///
        /// ★ 판정과 표시가 갈라지면 안 된다 ★
        ///   왕복 비용을 문턱에 반영해 놓고 화면에는 옛 숫자를 적으면, 보는 사람은
        ///   왜 보합이 나왔는지 영원히 알 수 없다. 같은 함수에서 뽑아 쓴다.
        /// </summary>
        internal static string BandText(double roundTripPercent)
        {
            return "보합 범위: " + string.Join(" · ", new[] { 1, 5, 20 }.Select(h =>
                PeriodName(h) + " ±" + (DollarAnalysis.Threshold(h, roundTripPercent) * 100)
                    .ToString("0.###", CultureInfo.InvariantCulture) + "%"));
        }

        private static string PeriodName(int horizon) { return horizon == 20 ? "월간" : horizon == 5 ? "주간" : "일간"; }

        private static TextBlock Label(string text, double size, Brush color)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
        }

        private static Border Divider()
        {
            return new Border { Height = 1, Background = Palette.Divider, Margin = new Thickness(0, 16, 0, 14) };
        }

        /// <summary>
        /// 단추 모양은 하나면 된다.
        /// ★ 템플릿은 여러 컨트롤이 나눠 쓸 수 있다 ★
        ///   전에는 단추를 만들 때마다 ControlTemplate 과 그 안의 요소 공장을 새로 지었다.
        ///   근거 목록은 기사 수 × 기간 3 만큼 단추를 만들고 갱신마다 전부 버리고 다시 만든다.
        ///   같은 파일의 다른 단추(SmallButton)는 이미 공유 템플릿을 쓰고 있었다.
        /// </summary>
        private static readonly ControlTemplate ActionTemplate = BuildActionTemplate();
        private static Button ActionButton(string text, double width)
        {
            var button = new Button { Content = text, Width = width, Height = 30, Cursor = Cursors.Hand,
                Foreground = Palette.TextDim, Background = Palette.Tile, FontSize = 11,
                Padding = new Thickness(9, 6, 9, 6), HorizontalContentAlignment = HorizontalAlignment.Center,
                Template = ActionTemplate };
            button.MouseEnter += delegate { button.Background = Palette.TileHover; };
            button.MouseLeave += delegate { button.Background = Palette.Tile; };
            return button;
        }
        private static ControlTemplate BuildActionTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }
    }
}
