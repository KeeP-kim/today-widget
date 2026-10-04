using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskWidget
{
    // WPF TextBlock has no letter-spacing property. Measure and draw graphemes with -0.05em tracking.
    internal sealed class BriefTextBlock : Control
    {
        internal const double Tracking = -0.05;
        internal bool KoreanTrackingOnly;
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(BriefTextBlock), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public string Text { get { return (string)GetValue(TextProperty); } set { SetValue(TextProperty, value); } }
        public TextAlignment TextAlignment { get; set; }
        public TextWrapping TextWrapping { get; set; }
        public TextTrimming TextTrimming { get; set; }
        public double LineHeight { get; set; }
        public LineStackingStrategy LineStackingStrategy { get; set; }
        public BriefTextBlock() { TextWrapping = TextWrapping.Wrap; LineHeight = double.NaN; }
        private double RowHeight { get { return double.IsNaN(LineHeight) ? FontSize * KoreanTextBlock.LineHeightRatio : LineHeight; } }
        private FormattedText Format(string text)
        { return new FormattedText(text, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip); }
        /// <summary>
        /// 자소 하나의 폭. 글꼴·크기가 그대로면 같은 자소는 같은 폭이다.
        /// ★ 같은 글자를 몇 백 번 다시 재고 있었다 ★
        ///   줄 나누기는 낱말을 더할 때마다 줄 전체를 처음부터 다시 쟀고, 재는 방식은
        ///   자소마다 FormattedText 를 새로 만드는 것이었다. 한 줄이 n 글자면 n²/2 개다.
        ///   재는 일은 Measure 와 OnRender 가 각각 또 한다.
        /// </summary>
        private readonly Dictionary<string, double> _glyphWidth = new Dictionary<string, double>(StringComparer.Ordinal);
        private string _widthKey;
        private double GlyphWidth(string glyph)
        {
            string key = FontSize.ToString(CultureInfo.InvariantCulture) + "|" + FontFamily + "|" + FontWeight + "|" + FontStyle + "|" + FontStretch;
            if (_widthKey != key) { _glyphWidth.Clear(); _widthKey = key; }
            double width;
            if (_glyphWidth.TryGetValue(glyph, out width)) return width;
            width = Format(glyph).WidthIncludingTrailingWhitespace;
            _glyphWidth[glyph] = width;
            return width;
        }
        private List<string> Glyphs(string text)
        {
            var result = new List<string>(); var iter = StringInfo.GetTextElementEnumerator(text);
            while (iter.MoveNext()) result.Add(iter.GetTextElement()); return result;
        }
        internal double TextWidth(string text)
        {
            var glyphs = Glyphs(text); double width = 0;
            for (int i = 0; i < glyphs.Count; i++)
                width += GlyphWidth(glyphs[i]) + (i + 1 < glyphs.Count ? Gap(glyphs[i], glyphs[i + 1]) : 0);
            return Math.Max(0, width);
        }
        private double Gap(string left, string right)
        { return !KoreanTrackingOnly || KoreanTextBlock.HasHangul(left) && KoreanTextBlock.HasHangul(right) ? FontSize * Tracking : 0; }
        internal List<string> Lines(double width)
        {
            var lines = new List<string>();
            if (TextWrapping == TextWrapping.NoWrap) {
                foreach (string paragraph in (Text ?? "").Replace("\r", "").Split('\n')) lines.Add(TrimLine(paragraph, width)); return lines;
            }
            foreach (string paragraph in (Text ?? "").Replace("\r", "").Split('\n')) {
                string line = "";
                foreach (Match match in Regex.Matches(paragraph, @"\S+\s*")) {
                    string word = match.Value;
                    if (line.Length > 0 && TextWidth(line + word.TrimEnd()) > width) { lines.Add(line.TrimEnd()); line = ""; }
                    line += word;
                }
                lines.Add(line.TrimEnd());
            }
            return lines;
        }
        private string TrimLine(string text, double width)
        {
            if (TextTrimming == TextTrimming.None || TextWidth(text) <= width) return text;
            var glyphs = Glyphs(text); int low = 0, high = glyphs.Count;
            while (low < high) {
                int middle = (low + high + 1) / 2;
                if (TextWidth(string.Join("", glyphs.GetRange(0, middle)) + "…") <= width) low = middle; else high = middle - 1;
            }
            string value = string.Join("", glyphs.GetRange(0, low));
            if (TextTrimming == TextTrimming.WordEllipsis && value.LastIndexOf(' ') > 0) value = value.Substring(0, value.LastIndexOf(' '));
            return value + "…";
        }
        protected override Size MeasureOverride(Size constraint)
        {
            var lines = Lines(Math.Max(1, constraint.Width)); double width = 0;
            foreach (string line in lines) width = Math.Max(width, TextWidth(line));
            return new Size(Math.Min(constraint.Width, width), lines.Count * RowHeight);
        }
        protected override void OnRender(DrawingContext context)
        {
            double y = 0;
            foreach (string line in Lines(Math.Max(1, ActualWidth))) {
                double natural = TextWidth(line);
                double scale = natural > ActualWidth && ActualWidth > 0 ? ActualWidth / natural : 1;
                context.PushTransform(new TranslateTransform(0, y));
                context.PushTransform(new ScaleTransform(scale, 1));
                double x = TextAlignment == TextAlignment.Center ? Math.Max(0, (ActualWidth - TextWidth(line)) / 2) : TextAlignment == TextAlignment.Right ? Math.Max(0, ActualWidth - TextWidth(line)) : 0;
                var glyphs = Glyphs(line);
                for (int i = 0; i < glyphs.Count; i++) {
                    context.DrawText(Format(glyphs[i]), new Point(x, 0));
                    x += GlyphWidth(glyphs[i]) + (i + 1 < glyphs.Count ? Gap(glyphs[i], glyphs[i + 1]) : 0);
                }
                context.Pop(); context.Pop();
                y += RowHeight;
            }
        }
    }
}
