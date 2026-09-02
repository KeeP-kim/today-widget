// 날씨 벡터 아이콘 + 공용 브러시. 브러시/지오메트리는 전부 Freeze 해서 재사용한다.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DeskWidget
{
    internal static class Palette
    {
        public static readonly SolidColorBrush Text      = Frozen(0xF3, 0xF3, 0xF6);
        public static readonly SolidColorBrush TextDim   = Frozen(0x8A, 0x8A, 0x99);
        public static readonly SolidColorBrush TextFaint = Frozen(0x75, 0x75, 0x83);
        public static readonly SolidColorBrush TextGhost = Frozen(0x5F, 0x5F, 0x6E);
        public static readonly SolidColorBrush Stale     = Frozen(0xC9, 0x88, 0x4B);   // 값이 낡았을 때
        public static readonly SolidColorBrush IconIdle  = Frozen(0x3E, 0x3E, 0x48);   // 새로고침 - 평소엔 거의 안 보이게
        public static readonly SolidColorBrush IconHover = Frozen(0x9A, 0x9A, 0xA8);   // 새로고침 - 올렸을 때
        public static readonly SolidColorBrush Up        = Frozen(0xFF, 0x6B, 0x6B);   // 상승 = 빨강
        public static readonly SolidColorBrush Down      = Frozen(0x4D, 0xA3, 0xFF);   // 하락 = 파랑
        public static readonly SolidColorBrush Flat      = Frozen(0x8A, 0x8A, 0x99);
        public static readonly SolidColorBrush Divider   = Frozen(0xFF, 0xFF, 0xFF, 0x20);
        public static readonly SolidColorBrush Hover     = Frozen(0xFF, 0xFF, 0xFF, 0x14);
        public static readonly SolidColorBrush Tile      = Frozen(0xFF, 0xFF, 0xFF, 0x0E);
        public static readonly SolidColorBrush TileEdge  = Frozen(0xFF, 0xFF, 0xFF, 0x18);
        public static readonly SolidColorBrush TileHover = Frozen(0xFF, 0xFF, 0xFF, 0x22);
        public static readonly SolidColorBrush Delete    = Frozen(0xE0, 0x53, 0x4B);
        // 편집 모드 배지 중 '지우기가 아닌 것'. 빨강과 확실히 갈라 놓아야 잘못 누르지 않는다.
        public static readonly SolidColorBrush Accent    = Frozen(0x4D, 0xA3, 0xFF);
        public static readonly SolidColorBrush Online    = Frozen(0x4C, 0xD0, 0x7A);   // 수신 중
        public static readonly SolidColorBrush Offline   = Frozen(0xE0, 0x53, 0x4B);   // 끊김
        public static readonly SolidColorBrush Notice    = Frozen(0xC8, 0xA8, 0x62);   // 맨 아래 공지 줄
        public static readonly SolidColorBrush CardEdge  = Frozen(0xFF, 0xFF, 0xFF, 0x30);
        public static readonly SolidColorBrush GripDot   = Frozen(0xFF, 0xFF, 0xFF, 0x38);
        public static readonly SolidColorBrush Clear     = Frozen(0xFF, 0xFF, 0xFF, 0x00);

        /// <summary>
        /// ★ 알파 0 은 '투명한 픽셀' 이 아니라 '창에 없는 픽셀' 이다 ★
        ///
        ///   AllowsTransparency 창은 WS_EX_LAYERED 라, 윈도우가 **픽셀마다** 알파를 보고
        ///   0 인 자리는 마우스를 그냥 아래 창으로 흘려보낸다. WPF 의 히트 테스트는
        ///   메시지가 우리 창에 닿은 '뒤' 의 이야기라, 그 자리에서는 아예 불리지 않는다.
        ///
        ///   실측 (WindowFromPoint):
        ///     알파 0  → 바탕화면 창을 돌려준다. 우리 창은 그 점을 갖고 있지도 않다.
        ///     알파 1  → 우리 창을 돌려준다. 한 비트면 충분하다.
        ///
        ///   그래서 '보이지는 않되 잡히기는 해야 하는' 자리에는 Clear 가 아니라 이것을 깐다.
        ///   1/255 는 눈에 안 보인다.
        ///
        ///   ※ Clear 를 전부 바꾸면 안 된다. 나머지 자리는 불투명한 카드 위라 알파 0 이 맞다.
        /// </summary>
        public static readonly SolidColorBrush Grab      = Frozen(0xFF, 0xFF, 0xFF, 0x01);

        // 날씨용
        public static readonly SolidColorBrush Sun       = Frozen(0xFF, 0xC6, 0x4B);
        public static readonly SolidColorBrush Moon      = Frozen(0xEF, 0xE0, 0xA0);
        public static readonly SolidColorBrush CloudLite = Frozen(0xCF, 0xD4, 0xDE);
        public static readonly SolidColorBrush CloudDark = Frozen(0x9A, 0xA1, 0xAE);
        public static readonly SolidColorBrush Rain      = Frozen(0x5A, 0xA9, 0xF2);
        public static readonly SolidColorBrush Snow      = Frozen(0xA9, 0xDC, 0xFF);
        public static readonly SolidColorBrush Fog       = Frozen(0xB6, 0xBC, 0xC7);

        public static readonly LinearGradientBrush Card = FrozenCard();

        // 급등·급락 알림. 방향과 무관하게 '눈에 띄어야 한다' 는 뜻의 빨강이다.
        public static readonly Color SurgeColor = Color.FromArgb(0x9E, 0xE0, 0x53, 0x4B);
        public static readonly SolidColorBrush SurgeFill = Frozen(0xE0, 0x53, 0x4B);

        private static SolidColorBrush Frozen(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        private static SolidColorBrush Frozen(byte r, byte g, byte b, byte a)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        private static LinearGradientBrush FrozenCard()
        {
            var g = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0.7, 1),
            };
            // 완전 불투명. 투명도는 창 Opacity(설정)로만 조절한다.
            // 여기에 알파를 주면 '투명도 100%' 로 둬도 바탕화면이 비쳐 보인다.
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0x25, 0x25, 0x2C), 0));
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0x15, 0x15, 0x1A), 1));
            g.Freeze();
            return g;
        }

        public static SolidColorBrush ForDir(int dir)
        {
            return dir > 0 ? Up : (dir < 0 ? Down : Flat);
        }
    }

    /// <summary>36x36 좌표계로 그린 뒤 원하는 크기로 스케일하는 날씨 아이콘.</summary>
    internal static class WeatherIcon
    {
        private const double Base = 36.0;

        public static Canvas Create(double size)
        {
            double k = size / Base;
            var cv = new Canvas
            {
                Width = size,
                Height = size,
                Background = null,
                IsHitTestVisible = false,
            };
            if (Math.Abs(k - 1.0) > 0.001)
            {
                var st = new ScaleTransform(k, k);
                st.Freeze();
                cv.RenderTransform = st;
            }
            return cv;
        }

        public static void Draw(Canvas cv, int code, bool isDay)
        {
            cv.Children.Clear();

            switch (Group(code))
            {
                case 0: // 맑음
                    if (isDay) Sun(cv, 18, 18, 7.5); else Moon(cv, 18, 18, 9.5);
                    break;
                case 1: // 대체로 맑음
                    if (isDay) Sun(cv, 13, 12, 5.8); else Moon(cv, 13, 12, 7);
                    Cloud(cv, 21, 22, 0.88, Palette.CloudLite);
                    break;
                case 2: // 구름 조금
                    if (isDay) Sun(cv, 11, 10, 5.2); else Moon(cv, 11, 10, 6.4);
                    Cloud(cv, 20, 20, 0.98, Palette.CloudLite);
                    break;
                case 3: // 흐림
                    Cloud(cv, 21, 13, 0.78, Palette.CloudDark);
                    Cloud(cv, 16, 20, 1.0, Palette.CloudLite);
                    break;
                case 4: // 안개
                    Cloud(cv, 18, 12, 0.9, Palette.CloudDark);
                    Line(cv, 7, 25, 29, 25, Palette.Fog, 2.0);
                    Line(cv, 10, 30, 26, 30, Palette.Fog, 2.0);
                    Line(cv, 8, 34.5, 24, 34.5, Palette.Fog, 2.0);
                    break;
                case 5: // 비
                    Cloud(cv, 18, 12, 1.0, Palette.CloudLite);
                    Line(cv, 12, 25, 10, 31, Palette.Rain, 2.2);
                    Line(cv, 18.5, 25, 16.5, 31, Palette.Rain, 2.2);
                    Line(cv, 25, 25, 23, 31, Palette.Rain, 2.2);
                    break;
                case 6: // 눈
                    Cloud(cv, 18, 12, 1.0, Palette.CloudLite);
                    Dot(cv, 11.5, 27.5, 2.0, Palette.Snow);
                    Dot(cv, 18.5, 31.0, 2.0, Palette.Snow);
                    Dot(cv, 25.5, 27.5, 2.0, Palette.Snow);
                    break;
                case 7: // 뇌우
                    Cloud(cv, 18, 11, 1.0, Palette.CloudDark);
                    Bolt(cv);
                    break;
                default:
                    Cloud(cv, 18, 16, 1.0, Palette.CloudLite);
                    break;
            }
        }

        private static int Group(int c)
        {
            if (c == 0) return 0;
            if (c == 1) return 1;
            if (c == 2) return 2;
            if (c == 3) return 3;
            if (c == 45 || c == 48) return 4;
            if ((c >= 51 && c <= 57) || (c >= 61 && c <= 67) || (c >= 80 && c <= 82)) return 5;
            if ((c >= 71 && c <= 77) || c == 85 || c == 86) return 6;
            if (c >= 95 && c <= 99) return 7;
            return 3;
        }

        public static string Describe(int c)
        {
            switch (c)
            {
                case 0: return "맑음";
                case 1: return "대체로 맑음";
                case 2: return "구름 조금";
                case 3: return "흐림";
                case 45: case 48: return "안개";
                case 51: case 53: case 55: return "이슬비";
                case 56: case 57: return "언 비";
                case 61: return "약한 비";
                case 63: return "비";
                case 65: return "강한 비";
                case 66: case 67: return "언 비";
                case 71: return "약한 눈";
                case 73: return "눈";
                case 75: return "강한 눈";
                case 77: return "싸락눈";
                case 80: case 81: return "소나기";
                case 82: return "강한 소나기";
                case 85: case 86: return "소낙눈";
                case 95: case 96: case 99: return "뇌우";
                default: return "";
            }
        }

        // ----- 도형 -----

        private static void Dot(Canvas cv, double cx, double cy, double r, Brush b)
        {
            var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = b };
            Canvas.SetLeft(e, cx - r);
            Canvas.SetTop(e, cy - r);
            cv.Children.Add(e);
        }

        private static void Line(Canvas cv, double x1, double y1, double x2, double y2, Brush b, double th)
        {
            var l = new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = b, StrokeThickness = th,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            cv.Children.Add(l);
        }

        private static void Sun(Canvas cv, double cx, double cy, double r)
        {
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                Line(cv, cx + ca * (r + 3.0), cy + sa * (r + 3.0),
                         cx + ca * (r + 6.2), cy + sa * (r + 6.2), Palette.Sun, 2.0);
            }
            Dot(cv, cx, cy, r, Palette.Sun);
        }

        private static void Moon(Canvas cv, double cx, double cy, double r)
        {
            var g1 = new EllipseGeometry(new Point(cx, cy), r, r);
            var g2 = new EllipseGeometry(new Point(cx + r * 0.62, cy - r * 0.55), r * 0.92, r * 0.92);
            var cg = new CombinedGeometry(GeometryCombineMode.Exclude, g1, g2);
            cg.Freeze();
            cv.Children.Add(new Path { Data = cg, Fill = Palette.Moon });
        }

        private static void Cloud(Canvas cv, double cx, double cy, double s, Brush b)
        {
            Dot(cv, cx - 5.2 * s, cy + 1.2 * s, 5.0 * s, b);
            Dot(cv, cx + 4.6 * s, cy + 1.6 * s, 4.4 * s, b);
            Dot(cv, cx - 0.2 * s, cy - 2.4 * s, 6.0 * s, b);
            var r = new Rectangle
            {
                Width = 20.0 * s, Height = 6.2 * s,
                RadiusX = 3.0 * s, RadiusY = 3.0 * s, Fill = b,
            };
            Canvas.SetLeft(r, cx - 10.0 * s);
            Canvas.SetTop(r, cy);
            cv.Children.Add(r);
        }

        private static void Bolt(Canvas cv)
        {
            var pts = new PointCollection
            {
                new Point(20.5, 21), new Point(13, 30), new Point(17.5, 30),
                new Point(14.5, 35.5), new Point(24, 26), new Point(19, 26),
            };
            pts.Freeze();
            cv.Children.Add(new Polygon { Points = pts, Fill = Palette.Sun });
        }
    }
}
