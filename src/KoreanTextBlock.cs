using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using NativeTextBlock = System.Windows.Controls.TextBlock;

namespace DeskWidget
{
    // Plain Korean labels use tracking; Latin text and styled clock runs retain native shaping.
    [ContentProperty("Text")]
    internal sealed class KoreanTextBlock : Control
    {
        internal const double LineHeightRatio = 1.3;
        private readonly NativeTextBlock _native;
        private readonly BriefTextBlock _tracked;
        private FrameworkElement _renderer;
        private bool _inlines;
        private const FrameworkPropertyMetadataOptions Layout = FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender;
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(KoreanTextBlock), new FrameworkPropertyMetadata("", Layout, Changed));
        public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register("TextWrapping", typeof(TextWrapping), typeof(KoreanTextBlock), new FrameworkPropertyMetadata(TextWrapping.NoWrap, Layout, Changed));
        public static readonly DependencyProperty TextTrimmingProperty = DependencyProperty.Register("TextTrimming", typeof(TextTrimming), typeof(KoreanTextBlock), new FrameworkPropertyMetadata(TextTrimming.None, Layout, Changed));
        public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register("TextAlignment", typeof(TextAlignment), typeof(KoreanTextBlock), new FrameworkPropertyMetadata(TextAlignment.Left, Layout, Changed));
        public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register("LineHeight", typeof(double), typeof(KoreanTextBlock), new FrameworkPropertyMetadata(double.NaN, Layout, Changed));
        public static readonly DependencyProperty LineStackingStrategyProperty = DependencyProperty.Register("LineStackingStrategy", typeof(LineStackingStrategy), typeof(KoreanTextBlock), new FrameworkPropertyMetadata(LineStackingStrategy.BlockLineHeight, Layout, Changed));
        public string Text { get { return _inlines ? _native.Text : (string)GetValue(TextProperty); } set { _inlines = false; SetValue(TextProperty, value ?? ""); Sync(); } }
        public TextWrapping TextWrapping { get { return (TextWrapping)GetValue(TextWrappingProperty); } set { SetValue(TextWrappingProperty, value); } }
        public TextTrimming TextTrimming { get { return (TextTrimming)GetValue(TextTrimmingProperty); } set { SetValue(TextTrimmingProperty, value); } }
        public TextAlignment TextAlignment { get { return (TextAlignment)GetValue(TextAlignmentProperty); } set { SetValue(TextAlignmentProperty, value); } }
        public double LineHeight { get { return (double)GetValue(LineHeightProperty); } set { SetValue(LineHeightProperty, value); } }
        public LineStackingStrategy LineStackingStrategy { get { return (LineStackingStrategy)GetValue(LineStackingStrategyProperty); } set { SetValue(LineStackingStrategyProperty, value); } }
        public InlineCollection Inlines { get { _inlines = true; Select(_native); Sync(); InvalidateMeasure(); return _native.Inlines; } }
        internal bool UsesTracking { get { return _renderer == _tracked; } }
        internal double EffectiveLineHeight { get { return double.IsNaN(LineHeight) ? FontSize * LineHeightRatio : LineHeight; } }
        public KoreanTextBlock()
        {
            Focusable = false;
            _native = new NativeTextBlock { IsHitTestVisible = false };
            _tracked = new BriefTextBlock { IsHitTestVisible = false, KoreanTrackingOnly = true };
            Sync();
        }
        internal static bool HasHangul(string text)
        {
            foreach (char ch in text ?? "") if (ch >= '\uAC00' && ch <= '\uD7A3' || ch >= '\u1100' && ch <= '\u11FF' ||
                ch >= '\u3130' && ch <= '\u318F' || ch >= '\uA960' && ch <= '\uA97F' || ch >= '\uD7B0' && ch <= '\uD7FF') return true;
            return false;
        }
        private static bool NeedsTracking(string text)
        {
            // A lone unit such as "원" has no Korean pair to tighten. Keep numeric shaping native.
            var elements = StringInfo.GetTextElementEnumerator(text);
            bool previous = false;
            while (elements.MoveNext()) {
                bool current = HasHangul(elements.GetTextElement());
                if (previous && current) return true;
                previous = current;
            }
            return false;
        }
        private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        { ((KoreanTextBlock)obj).Sync(); }
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == FontSizeProperty || e.Property == FontFamilyProperty || e.Property == FontWeightProperty ||
                e.Property == FontStyleProperty || e.Property == FontStretchProperty || e.Property == ForegroundProperty || e.Property == FlowDirectionProperty) Sync();
        }
        private void Sync()
        {
            if (_native == null) return;
            foreach (var child in new FrameworkElement[] { _native, _tracked }) {
                child.SetValue(FontFamilyProperty, FontFamily); child.SetValue(FontSizeProperty, FontSize);
                child.SetValue(FontWeightProperty, FontWeight); child.SetValue(FontStyleProperty, FontStyle);
                child.SetValue(FontStretchProperty, FontStretch); child.SetValue(ForegroundProperty, Foreground);
                child.FlowDirection = FlowDirection;
            }
            _native.TextWrapping = TextWrapping; _tracked.TextWrapping = TextWrapping;
            _native.TextTrimming = TextTrimming; _tracked.TextTrimming = TextTrimming;
            _native.TextAlignment = TextAlignment; _tracked.TextAlignment = TextAlignment;
            _native.LineHeight = EffectiveLineHeight; _tracked.LineHeight = EffectiveLineHeight;
            _native.LineStackingStrategy = _inlines ? LineStackingStrategy.MaxHeight : LineStackingStrategy;
            _tracked.LineStackingStrategy = LineStackingStrategy;
            if (!_inlines) {
                string value = (string)GetValue(TextProperty) ?? "";
                _native.Text = value; _tracked.Text = value;
                Select(NeedsTracking(value) && FlowDirection == FlowDirection.LeftToRight ? (FrameworkElement)_tracked : _native);
            }
            _tracked.InvalidateMeasure(); _tracked.InvalidateVisual();
        }
        private void Select(FrameworkElement next)
        {
            if (_renderer == next) return;
            if (_renderer != null) { RemoveVisualChild(_renderer); RemoveLogicalChild(_renderer); }
            _renderer = next; AddLogicalChild(next); AddVisualChild(next); InvalidateMeasure();
        }
        protected override int VisualChildrenCount { get { return _renderer == null ? 0 : 1; } }
        protected override Visual GetVisualChild(int index)
        { if (index != 0 || _renderer == null) throw new ArgumentOutOfRangeException("index"); return _renderer; }
        protected override Size MeasureOverride(Size constraint)
        {
            _renderer.Measure(new Size(Math.Max(0, constraint.Width - Padding.Left - Padding.Right), Math.Max(0, constraint.Height - Padding.Top - Padding.Bottom)));
            return new Size(_renderer.DesiredSize.Width + Padding.Left + Padding.Right, _renderer.DesiredSize.Height + Padding.Top + Padding.Bottom);
        }
        protected override Size ArrangeOverride(Size size)
        {
            _renderer.Arrange(new Rect(Padding.Left, Padding.Top, Math.Max(0, size.Width - Padding.Left - Padding.Right), Math.Max(0, size.Height - Padding.Top - Padding.Bottom)));
            return size;
        }
        protected override AutomationPeer OnCreateAutomationPeer() { return new KoreanTextPeer(this); }
        public override string ToString() { return Text; }
        private sealed class KoreanTextPeer : FrameworkElementAutomationPeer
        {
            internal KoreanTextPeer(KoreanTextBlock owner) : base(owner) { }
            protected override string GetNameCore() { return ((KoreanTextBlock)Owner).Text; }
            protected override AutomationControlType GetAutomationControlTypeCore() { return AutomationControlType.Text; }
            protected override string GetClassNameCore() { return "TextBlock"; }
        }
    }
}
