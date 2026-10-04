using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NativeTextBlock = System.Windows.Controls.TextBlock;

namespace DeskWidget
{
    internal static class TypographyTests
    {
        private static int count;
        private static void Check(bool value, string name)
        { if (!value) throw new Exception("Typography: " + name); count++; }
        private static void Layout(FrameworkElement element, double width = 420)
        {
            element.Measure(new Size(width, double.PositiveInfinity));
            element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height)); element.UpdateLayout();
        }
        private static T Find<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T) return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
                var found = Find<T>(VisualTreeHelper.GetChild(root, i)); if (found != null) return found;
            }
            return null;
        }
        internal static int Run(string work, bool preview)
        {
            count = 0;
            if (Application.Current == null) Theme.Apply(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
            var family = new FontFamily("Malgun Gothic");
            foreach (double size in new[] { 12.0, 20.0, 28.0 }) {
                var label = new KoreanTextBlock { Text = "가나다라", FontFamily = family, FontSize = size };
                Layout(label);
                var native = new FormattedText(label.Text, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
                    new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Brushes.Black, 1);
                Check(label.UsesTracking && Math.Abs(label.DesiredSize.Width - (native.Width - size * 0.05 * 3)) < 0.15,
                    "Korean width does not match -0.05em at " + size);
                Check(Math.Abs(label.DesiredSize.Height - size * 1.3) < 0.01, "single row line height at " + size);
                label.Text = "한글 설명\n달러 전망"; Layout(label);
                Check(Math.Abs(label.DesiredSize.Height - size * 1.3 * 2) < 0.01, "multiline spacing at " + size);
                label.FontSize = size + 3; Layout(label);
                Check(Math.Abs(label.DesiredSize.Height - (size + 3) * 1.3 * 2) < 0.01, "font size change reused stale spacing");
            }
            foreach (string text in new[] { "1,345.90원", "약 1,346원", "Sol 6.1 High", "23:59" }) {
                var label = new KoreanTextBlock { Text = text, FontFamily = family, FontSize = 17, TextWrapping = TextWrapping.Wrap };
                var native = new NativeTextBlock { Text = text, FontFamily = family, FontSize = 17,
                    TextWrapping = TextWrapping.Wrap, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 22.1 };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); native.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Check(!label.UsesTracking && Math.Abs(label.DesiredSize.Width - native.DesiredSize.Width) < 0.01,
                    "numeric or Latin shaping changed: " + text);
                Check(Math.Abs(label.DesiredSize.Height - native.DesiredSize.Height) < 0.01, "numeric line height changed: " + text);
            }
            var mixed = new KoreanTextBlock { Text = "Sol 6.1 · 한글 전망 1,345원", FontFamily = family, FontSize = 16 };
            Layout(mixed);
            var tracked = Find<BriefTextBlock>(mixed);
            Check(mixed.UsesTracking && tracked.TextWidth(mixed.Text) <= mixed.ActualWidth, "mixed Korean label exceeds measured width");
            mixed.TextTrimming = TextTrimming.CharacterEllipsis; Layout(mixed, 90);
            var lines = tracked.Lines(90);
            Check(lines.Count == 1 && lines[0].EndsWith("…") && tracked.TextWidth(lines[0]) <= 90,
                "mixed label ellipsis overflows");
            mixed.TextTrimming = TextTrimming.None; mixed.TextWrapping = TextWrapping.Wrap; Layout(mixed, 90);
            Check(mixed.DesiredSize.Height > 16 * 1.3, "wrapping change did not invalidate layout");
            mixed.Text = "12:34"; Layout(mixed);
            Check(!mixed.UsesTracking, "renderer did not switch back to native");
            mixed.Inlines.Clear(); mixed.Inlines.Add(new Run("10/01 ") { FontSize = 9 }); mixed.Inlines.Add(new Run("12:34") { FontSize = 17 }); Layout(mixed);
            Check(mixed.Text == "10/01 12:34" && mixed.Inlines.Count == 2 && !mixed.UsesTracking, "styled clock runs were flattened");
            mixed.Text = "한글 설명"; Layout(mixed);
            Check(mixed.UsesTracking && mixed.Text == "한글 설명", "styled to plain transition lost content");

            var host = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(30, 30, 35)) };
            foreach (double size in new[] { 12.0, 16.0, 24.0 }) {
                host.Children.Add(new KoreanTextBlock { Text = "달러와 도지코인 · Sol 6.1 High\n한글 자간 -0.05em · 행간 1.3", FontFamily = family,
                    FontSize = size, Foreground = Brushes.White, Margin = new Thickness(12, 6, 12, 6) });
            }
            var button = new Button { Content = "분석 갱신", FontFamily = family, FontSize = 16, Foreground = Brushes.DarkBlue,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12), Padding = new Thickness(12, 6, 12, 6) };
            host.Children.Add(button); Layout(host);
            var buttonLabel = Find<KoreanTextBlock>(button);
            Check(buttonLabel != null && buttonLabel.UsesTracking && buttonLabel.Text == "분석 갱신", "button string template not applied");
            Check(buttonLabel.FontSize == 16 && buttonLabel.Foreground == Brushes.DarkBlue && button.Content is string,
                "button label changed font, color, or content contract");
            button.Content = "갱신 완료"; Layout(host);
            Check(Find<KoreanTextBlock>(button).Text == "갱신 완료", "dynamic button content binding stale");
            var menu = new MenuItem { Header = "주전망 분석", FontSize = 14 }; host.Children.Add(menu); Layout(host);
            Check(Find<KoreanTextBlock>(menu) != null && Find<KoreanTextBlock>(menu).UsesTracking, "menu header string template not applied");
            if (preview) {
                int height = (int)Math.Ceiling(host.ActualHeight * 1.5);
                var bitmap = new RenderTargetBitmap(630, height, 144, 144, PixelFormats.Pbgra32); bitmap.Render(host);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string path = Path.Combine(work, "typography-150.png"); using (var stream = File.Create(path)) encoder.Save(stream);
                Console.WriteLine("PREVIEW: " + path);
            }
            return count;
        }
    }
}
