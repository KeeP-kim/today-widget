// 종목 추가 창 - 네이버 자동완성으로 한글 검색
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskWidget
{
    internal sealed class SearchWindow : Window
    {
        private readonly Action<SymbolDef> _onPick;
        private readonly HashSet<string> _already;
        private readonly bool _weatherOnly;

        private TextBox _input;
        private StackPanel _results;
        private TextBlock _status;
        private DispatcherTimer _debounce;
        private CancellationTokenSource _cts;

        public static void Open(Window owner, IEnumerable<SymbolDef> current, Action<SymbolDef> onPick)
        {
            Show(owner, current, onPick, false);
        }

        /// <summary>날씨 지역만 검색하는 모드.</summary>
        public static void OpenWeather(Window owner, IEnumerable<SymbolDef> current, Action<SymbolDef> onPick)
        {
            Show(owner, current, onPick, true);
        }

        private static void Show(Window owner, IEnumerable<SymbolDef> current,
                                 Action<SymbolDef> onPick, bool weatherOnly)
        {
            var w = new SearchWindow(current, onPick, weatherOnly);
            try { w.Owner = owner; } catch { }
            w.Show();
            w.Activate();
        }

        private SearchWindow(IEnumerable<SymbolDef> current, Action<SymbolDef> onPick, bool weatherOnly)
        {
            _onPick = onPick;
            _weatherOnly = weatherOnly;
            _already = new HashSet<string>(StringComparer.Ordinal);
            if (current != null) foreach (var d in current) _already.Add(d.Key);

            Title = weatherOnly ? "날씨 지역 추가" : "종목 추가";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 356;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
            UseLayoutRounding = true;
            Topmost = true;

            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Closed += (s, e) =>
            {
                if (_debounce != null) _debounce.Stop();
                if (_cts != null) { try { _cts.Cancel(); } catch { } }
            };

            Content = BuildUi();
            Loaded += (s, e) => _input.Focus();
        }

        private UIElement BuildUi()
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = Palette.CardEdge,
                Background = Palette.Card,
                Padding = new Thickness(18, 16, 18, 14),
                Margin = new Thickness(4),
            };

            var root = new StackPanel();

            // 제목 (여기를 잡고 창을 옮길 수 있다)
            var title = new TextBlock
            {
                Text = _weatherOnly ? "날씨 지역 추가" : "종목 추가",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Palette.Text,
                Margin = new Thickness(0, 0, 0, 3),
            };
            title.MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
            root.Children.Add(title);

            root.Children.Add(new TextBlock
            {
                Text = _weatherOnly
                     ? "동·읍·면 이름으로 찾습니다\n예:  여의도 / 해운대 / 제주 / 정자동"
                     : "한글 · 영문 · 티커 · 종목코드 모두 됩니다\n예:  엔비디아 / NVDA / 삼성전자 / 005930 / 비트코인 / 코스닥",
                FontSize = 10.5,
                Foreground = Palette.TextFaint,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 15,
            });

            // 입력창
            _input = new TextBox
            {
                FontSize = 13,
                Foreground = Palette.Text,
                CaretBrush = Palette.Text,
                Background = Palette.Hover,
                BorderBrush = Palette.CardEdge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 7, 9, 7),
                SelectionBrush = Palette.Down,
            };
            _input.TextChanged += (s, e) => Schedule();
            root.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Child = _input,
                ClipToBounds = true,
            });

            _status = new TextBlock
            {
                Text = "",
                FontSize = 10.5,
                Foreground = Palette.TextFaint,
                Margin = new Thickness(2, 9, 0, 0),
            };
            root.Children.Add(_status);

            _results = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            root.Children.Add(new ScrollViewer
            {
                Content = _results,
                MaxHeight = 268,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            // 닫기
            var close = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = Palette.Hover,
                BorderBrush = Palette.CardEdge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 5, 16, 5),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
                Child = new TextBlock { Text = "닫기", FontSize = 12, Foreground = Palette.Text },
            };
            close.MouseLeftButtonDown += (s, e) => { e.Handled = true; Close(); };
            root.Children.Add(close);

            card.Child = root;
            return card;
        }

        // ---------- 검색 ----------

        private void Schedule()
        {
            if (_debounce == null)
            {
                _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
                _debounce.Tick += (s, e) => { _debounce.Stop(); Run(); };
            }
            _debounce.Stop();
            _debounce.Start();
        }

        private async void Run()
        {
            string q = _input.Text.Trim();
            if (q.Length == 0)
            {
                _results.Children.Clear();
                _status.Text = "";
                return;
            }

            if (_cts != null) { try { _cts.Cancel(); } catch { } }
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            _status.Text = "찾는 중...";
            List<SearchHit> hits = null;
            try
            {
                hits = _weatherOnly
                     ? await Sources.SearchWeatherAreasAsync(q, ct)
                     : await Sources.SearchAsync(q, ct);
            }
            catch (OperationCanceledException) { return; }
            catch { }

            if (ct.IsCancellationRequested) return;

            _results.Children.Clear();
            if (hits == null || hits.Count == 0)
            {
                _status.Text = "결과가 없습니다";
                return;
            }

            _status.Text = hits.Count + "건";
            foreach (var h in hits) _results.Children.Add(BuildHit(h));
        }

        private UIElement BuildHit(SearchHit hit)
        {
            bool dup = _already.Contains(hit.Def.Key);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = hit.Def.Label,
                FontSize = 12.5,
                Foreground = dup ? Palette.TextFaint : Palette.Text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(name, 0);
            g.Children.Add(name);

            var type = new TextBlock
            {
                Text = dup ? "이미 있음" : hit.TypeName,
                FontSize = 10.5,
                Foreground = Palette.TextFaint,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };
            Grid.SetColumn(type, 1);
            g.Children.Add(type);

            var row = new Border
            {
                Child = g,
                Padding = new Thickness(9, 7, 9, 7),
                CornerRadius = new CornerRadius(7),
                Background = Palette.Clear,
                Cursor = dup ? Cursors.Arrow : Cursors.Hand,
            };

            if (!dup)
            {
                row.MouseEnter += (s, e) => row.Background = Palette.Hover;
                row.MouseLeave += (s, e) => row.Background = Palette.Clear;
                row.MouseLeftButtonDown += async (s, e) =>
                {
                    e.Handled = true;

                    // 날씨는 좌표가 있어야 조회할 수 있다. 고른 시점에 한 번만 구한다.
                    if (hit.Def.Kind == SourceKind.Weather)
                    {
                        _status.Text = "위치 확인 중...";
                        bool ok = false;
                        try { ok = await Sources.ResolveCoordsAsync(hit.Def, hit.TypeName, CancellationToken.None); }
                        catch { }
                        if (!ok) { _status.Text = "이 지역의 위치를 찾지 못했습니다"; return; }
                    }

                    _already.Add(hit.Def.Key);
                    if (_onPick != null) _onPick(hit.Def);
                    Close();
                };
            }
            return row;
        }
    }
}
