// 정보 창 - 버전, 데이터 출처, 전체 구조
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskWidget
{
    internal sealed class AboutWindow : Window
    {
        private static AboutWindow _open;   // 중복으로 뜨지 않게

        private Config _cfg;
        private Action _onKeySaved;

        public static void ShowSingle(Window owner, string baseDir, string latestVersion,
                                      Config cfg, Action onKeySaved)
        {
            if (_open != null)
            {
                try { _open.Activate(); return; }
                catch { _open = null; }
            }
            var w = new AboutWindow(baseDir, latestVersion);
            w._cfg = cfg;
            w._onKeySaved = onKeySaved;
            w.FillKey();
            _open = w;
            w.Closed += (s, e) => { if (ReferenceEquals(_open, w)) _open = null; };
            try { w.Owner = owner; } catch { }
            w.Show();
        }

        private readonly string _latest;   // 서버가 알려준 최신 버전 (없으면 null)

        private AboutWindow(string baseDir, string latestVersion)
        {
            _latest = latestVersion;
            Title = "오늘은 - 정보";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 452;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Topmost = true;

            MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };

            var card = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                BorderBrush = Palette.CardEdge,
                Background = Palette.Card,
                Padding = new Thickness(22, 20, 22, 18),
                Margin = new Thickness(4),
            };

            var body = new StackPanel();
            body.Children.Add(SectionTitle("데이터 출처"));
            body.Children.Add(BuildSources());
            body.Children.Add(Divider(14, 14));
            body.Children.Add(SectionTitle("쓰는 법"));
            body.Children.Add(BuildUsage());
            body.Children.Add(Divider(14, 14));
            body.Children.Add(SectionTitle("구조"));
            body.Children.Add(BuildArchitecture());

            var root = new StackPanel();
            root.Children.Add(BuildHeader(baseDir));
            root.Children.Add(Divider(16, 14));
            root.Children.Add(new ScrollViewer
            {
                Content = body,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            root.Children.Add(BuildFooter());

            card.Child = root;
            Content = card;
        }

        // ---------- 헤더 ----------

        private UIElement BuildHeader(string baseDir)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = BuildIcon(baseDir);
            icon.Margin = new Thickness(0, 0, 16, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(icon, 0);
            g.Children.Add(icon);

            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(new TextBlock
            {
                Text = "오늘은",
                FontSize = 21,
                Foreground = Palette.Text,
                VerticalAlignment = VerticalAlignment.Bottom,
            });
            nameRow.Children.Add(new TextBlock
            {
                Text = "v" + Config.AppVersion,
                FontSize = 11.5,
                Foreground = Palette.TextFaint,
                Margin = new Thickness(9, 0, 0, 3),
                VerticalAlignment = VerticalAlignment.Bottom,
            });
            if (Config.IsNewer(_latest))
            {
                nameRow.Children.Add(new TextBlock
                {
                    Text = "· 새 버전 v" + _latest + " 있음",
                    FontSize = 11,
                    Foreground = Palette.Notice,
                    Margin = new Thickness(8, 0, 0, 3),
                    VerticalAlignment = VerticalAlignment.Bottom,
                });
            }

            col.Children.Add(nameRow);

            col.Children.Add(new TextBlock
            {
                Text = "환율 · 시세 · 날씨 데스크톱 위젯",
                FontSize = 11.5,
                Foreground = Palette.TextDim,
                Margin = new Thickness(0, 5, 0, 0),
            });
            col.Children.Add(new TextBlock
            {
                Text = "제작자 : keep kim",
                FontSize = 11,
                Foreground = Palette.TextFaint,
                Margin = new Thickness(0, 3, 0, 0),
            });

            Grid.SetColumn(col, 1);
            g.Children.Add(col);
            return g;
        }

        /// <summary>assets\widget.ico 를 띄운다. 없으면 같은 모양을 코드로 그린다.</summary>
        private FrameworkElement BuildIcon(string baseDir)
        {
            const double Size = 56;

            if (!string.IsNullOrEmpty(baseDir))
            {
                try
                {
                    string path = Path.Combine(baseDir, "assets\\widget.ico");
                    if (File.Exists(path))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.DecodePixelWidth = (int)(Size * 2);   // 여러 크기 중 선명한 것을 고르게
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        return new Image { Source = bmp, Width = Size, Height = Size };
                    }
                }
                catch { }
            }

            // 아이콘 파일이 없을 때의 대체 표시
            return new Border
            {
                Width = Size,
                Height = Size,
                CornerRadius = new CornerRadius(12),
                Background = Palette.Card,
                BorderBrush = Palette.CardEdge,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = "₩",
                    FontSize = 30,
                    FontWeight = FontWeights.Bold,
                    Foreground = Palette.Sun,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        // ---------- 데이터 출처 ----------

        private static readonly string[,] SourceRows =
        {
            { "환율",          "네이버 금융 — 하나은행 / 신한은행 고시 (13개 통화)" },
            { "국내주식·지수", "네이버 금융 실시간" },
            { "해외주식",      "네이버 해외주식 (나스닥·뉴욕 등)" },
            { "코인",          "업비트 공개 API (원화 마켓)" },
            { "금리·통계",     "한국은행 ECOS — 기준금리, 국고채, 물가 등" },
            { "날씨",          "Open-Meteo" },
            { "종목 검색",     "네이버 자동완성 — 한글·영문·티커·종목코드" },
            { "지역 검색",     "네이버 날씨 + OpenStreetMap (좌표)" },
            { "위치 자동감지", "ipapi.co / ipwho.is" },
            { "새 버전 확인",  "api.github.com — 이 앱의 릴리스 (끌 수 있다)" },
        };

        private UIElement BuildSources()
        {
            var sp = new StackPanel();
            for (int i = 0; i < SourceRows.GetLength(0); i++)
                sp.Children.Add(Row(SourceRows[i, 0], SourceRows[i, 1]));

            sp.Children.Add(new TextBlock
            {
                Text = "한국은행만 무료 인증키가 필요하고, 나머지는 전부 키 없이 쓰는 공개 엔드포인트입니다.",
                FontSize = 10.5,
                Foreground = Palette.TextFaint,
                Margin = new Thickness(0, 7, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });

            sp.Children.Add(BuildKeyRow());
            return sp;
        }

        // ---------- 한국은행 인증키 ----------
        //
        // 설정 파일을 손으로 고치지 않고 여기서 넣는다.
        // 키는 사람마다 다르므로 저장소에 담을 수 없다 - 공개 배포의 전제이기도 하다.

        private TextBox _keyBox;
        private TextBlock _keyNote;

        private UIElement BuildKeyRow()
        {
            _keyBox = new TextBox
            {
                FontSize = 11.5,
                Background = Palette.Tile,
                Foreground = Palette.Text,
                CaretBrush = Palette.Text,
                BorderBrush = Palette.TileEdge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 3, 6, 3),
                MinWidth = 190,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var save = new Border
            {
                Child = new TextBlock
                {
                    Text = "저장",
                    FontSize = 11.5,
                    Foreground = Palette.Text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                Padding = new Thickness(11, 4, 11, 4),
                Margin = new Thickness(6, 0, 0, 0),
                CornerRadius = new CornerRadius(6),
                Background = Palette.Tile,
                BorderBrush = Palette.TileEdge,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
            };
            save.MouseEnter += delegate { save.Background = Palette.TileHover; };
            save.MouseLeave += delegate { save.Background = Palette.Tile; };
            save.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                SaveKey();
            };

            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
            line.Children.Add(new TextBlock
            {
                Text = "인증키",
                FontSize = 11.5,
                Foreground = Palette.TextDim,
                Width = 62,
                VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(_keyBox);
            line.Children.Add(save);

            _keyNote = new TextBlock
            {
                Text = "ecos.bok.or.kr → 오픈API 에서 무료로 발급받아 붙여넣으세요. 비워두면 금리·물가만 빠지고 나머지는 그대로 됩니다.",
                FontSize = 10.5,
                Foreground = Palette.TextFaint,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };

            var box = new StackPanel();
            box.Children.Add(line);
            box.Children.Add(_keyNote);
            return box;
        }

        private void FillKey()
        {
            if (_keyBox == null || _cfg == null) return;
            _keyBox.Text = _cfg.EcosKey ?? "";
        }

        private void SaveKey()
        {
            if (_keyBox == null || _cfg == null) return;
            try
            {
                _cfg.SetEcosKey(_keyBox.Text);
                _cfg.Save();
                _keyBox.Text = _cfg.EcosKey ?? "";

                bool has = !string.IsNullOrEmpty(_cfg.EcosKey);
                _keyNote.Text = has
                    ? "저장했습니다. 금리·물가를 다시 받아옵니다."
                    : "키를 비웠습니다. 금리·물가만 빠지고 나머지는 그대로 동작합니다.";
                _keyNote.Foreground = Palette.TextDim;

                if (_onKeySaved != null) _onKeySaved();
            }
            catch { }
        }

        private static readonly string[,] UsageRows =
        {
            { "종목 추가",   "꾹 눌러 편집 모드 → + → 검색해서 선택" },
            { "순서 바꾸기", "편집 모드에서 끌어서 이동 (밀려서 자리가 바뀜)" },
            { "삭제",        "편집 모드의 빨간 − · 되돌리기는 Alt + Z" },
            { "보기 전환",   "목록 ↔ 타일 · 타일은 가로 2~10개" },
            { "크기",        "우하단 모서리로 배율, 좌우·하단 가장자리로 개수" },
            { "접기",        "섹션별 − 로 접힘 · 접힌 목록은 휠로 넘김" },
            { "열어보기",    "숫자나 날씨를 더블클릭하면 네이버에서 열림" },
        };

        private UIElement BuildUsage()
        {
            var sp = new StackPanel();
            for (int i = 0; i < UsageRows.GetLength(0); i++)
                sp.Children.Add(Row(UsageRows[i, 0], UsageRows[i, 1]));
            return sp;
        }

        // ---------- 구조 ----------

        private UIElement BuildArchitecture()
        {
            var sp = new StackPanel();

            sp.Children.Add(Mono(
                "launch.vbs  →  launch.ps1\n" +
                "                 ├─ 오늘은.exe          (평소 경로)\n" +
                "                 └─ 오늘은.dll → 메모리 로드\n" +
                "                    (Smart App Control 이 exe 를 막을 때)"));

            sp.Children.Add(Row("빌드", ".NET Framework 4.8 / WPF · Windows 내장 csc.exe"));
            sp.Children.Add(Row("갱신", "시세 5분 · 날씨 10분 (변경 가능) · 종목 동시 호출"));
            sp.Children.Add(Row("절전", "접은 섹션과 최소화 상태에서는 호출하지 않음"));
            sp.Children.Add(Row("네트워크", "HttpClient 싱글톤 · HTTPS 전용 · 인증서 검증 유지"));
            sp.Children.Add(Row("링크", "네이버 · 업비트 · 한국은행 · GitHub 도메인만 허용"));
            sp.Children.Add(Row("메모리", "유휴 시 워킹셋 반환 — 대기 상태에서 약 8MB"));
            sp.Children.Add(Row("설정", "config.json — 종목 · 지역 · 배율 · 투명도 · 갱신 주기"));

            return sp;
        }

        // ---------- 푸터 ----------

        private UIElement BuildFooter()
        {
            var g = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hint = new TextBlock
            {
                Text = "Esc 를 누르거나 닫기를 눌러 나갑니다",
                FontSize = 10,
                Foreground = Palette.TextGhost,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(hint, 0);
            g.Children.Add(hint);

            var close = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = Palette.Hover,
                BorderBrush = Palette.CardEdge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 6, 18, 6),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = "닫기", FontSize = 12, Foreground = Palette.Text },
            };
            close.MouseLeftButtonDown += (s, e) => { e.Handled = true; Close(); };
            Grid.SetColumn(close, 2);
            g.Children.Add(close);

            var log = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = Palette.Clear,
                BorderBrush = Palette.CardEdge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = "로그", FontSize = 12, Foreground = Palette.TextDim },
            };
            log.MouseEnter += (s, e) => log.Background = Palette.Hover;
            log.MouseLeave += (s, e) => log.Background = Palette.Clear;
            log.MouseLeftButtonDown += (s, e) => { e.Handled = true; ChangelogWindow.ShowSingle(this); };
            Grid.SetColumn(log, 1);
            g.Children.Add(log);

            return g;
        }

        // ---------- 부품 ----------

        private static UIElement Row(string label, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var l = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = Palette.TextFaint,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var v = new TextBlock
            {
                Text = value,
                FontSize = 11.5,
                Foreground = Palette.TextDim,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(l, 0);
            Grid.SetColumn(v, 1);
            g.Children.Add(l);
            g.Children.Add(v);
            return g;
        }

        private static UIElement Mono(string text)
        {
            return new Border
            {
                Background = Palette.Hover,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 2, 0, 9),
                Child = new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily("Consolas, D2Coding, Malgun Gothic"),
                    FontSize = 10.5,
                    Foreground = Palette.TextDim,
                    LineHeight = 16,
                },
            };
        }

        private static UIElement SectionTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Palette.TextGhost,
                Margin = new Thickness(0, 0, 0, 7),
            };
        }

        private static UIElement Divider(double top, double bottom)
        {
            return new Border
            {
                Height = 1,
                Background = Palette.Divider,
                Margin = new Thickness(0, top, 0, bottom),
            };
        }
    }

    /// <summary>
    /// 변경 내역 팝업. 정보 창의 '로그' 버튼으로 연다.
    /// 내용은 Config.Changelog 를 그대로 그리므로, 버전을 올릴 때 그 배열만 고치면 된다.
    /// </summary>
    internal sealed class ChangelogWindow : Window
    {
        private static ChangelogWindow _open;   // 중복으로 뜨지 않게

        public static void ShowSingle(Window owner)
        {
            if (_open != null)
            {
                try { _open.Activate(); return; }
                catch { _open = null; }
            }
            var w = new ChangelogWindow();
            _open = w;
            w.Closed += (s, e) => { if (ReferenceEquals(_open, w)) _open = null; };
            try { w.Owner = owner; } catch { }
            w.Show();
        }

        private ChangelogWindow()
        {
            Title = "오늘은 - 변경 내역";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 430;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Topmost = true;

            MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };

            var card = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                BorderBrush = Palette.CardEdge,
                Background = Palette.Card,
                Padding = new Thickness(22, 20, 22, 18),
                Margin = new Thickness(4),
            };

            var body = new StackPanel();
            var log = Config.Changelog;
            for (int i = 0; i < log.Length; i++)
            {
                if (i > 0)
                    body.Children.Add(new Border
                    {
                        Height = 1,
                        Background = Palette.Divider,
                        Margin = new Thickness(0, 13, 0, 13),
                    });
                body.Children.Add(Entry(log[i], i == 0));
            }

            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = "변경 내역",
                FontSize = 15,
                Foreground = Palette.Text,
                Margin = new Thickness(0, 0, 0, 4),
            });
            root.Children.Add(new TextBlock
            {
                Text = "위에 있을수록 최근입니다",
                FontSize = 10.5,
                Foreground = Palette.TextGhost,
                Margin = new Thickness(0, 0, 0, 14),
            });
            root.Children.Add(new ScrollViewer
            {
                Content = body,
                MaxHeight = 460,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            root.Children.Add(BuildFooter());

            card.Child = root;
            Content = card;
        }

        /// <summary>한 버전 묶음. item[0] 이 버전이고 그 뒤가 항목들이다.</summary>
        private static UIElement Entry(string[] item, bool current)
        {
            var sp = new StackPanel();

            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock
            {
                Text = "v" + item[0],
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = current ? Palette.Text : Palette.TextDim,
            });
            if (current)
                head.Children.Add(new TextBlock
                {
                    Text = "지금 쓰는 버전",
                    FontSize = 10,
                    Foreground = Palette.Notice,
                    Margin = new Thickness(9, 0, 0, 1),
                    VerticalAlignment = VerticalAlignment.Bottom,
                });
            sp.Children.Add(head);

            for (int i = 1; i < item.Length; i++)
            {
                var g = new Grid { Margin = new Thickness(2, 6, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var dot = new TextBlock
                {
                    Text = "·",
                    FontSize = 11.5,
                    Foreground = Palette.TextGhost,
                    Margin = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                };
                var tx = new TextBlock
                {
                    Text = item[i],
                    FontSize = 11.5,
                    Foreground = Palette.TextDim,
                    TextWrapping = TextWrapping.Wrap,
                };
                Grid.SetColumn(dot, 0);
                Grid.SetColumn(tx, 1);
                g.Children.Add(dot);
                g.Children.Add(tx);
                sp.Children.Add(g);
            }
            return sp;
        }

        private UIElement BuildFooter()
        {
            var g = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hint = new TextBlock
            {
                Text = "Esc 를 누르거나 닫기를 눌러 나갑니다",
                FontSize = 10,
                Foreground = Palette.TextGhost,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(hint, 0);
            g.Children.Add(hint);

            var close = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = Palette.Hover,
                BorderBrush = Palette.CardEdge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 6, 18, 6),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = "닫기", FontSize = 12, Foreground = Palette.Text },
            };
            close.MouseLeftButtonDown += (s, e) => { e.Handled = true; Close(); };
            Grid.SetColumn(close, 1);
            g.Children.Add(close);

            return g;
        }
    }

}
