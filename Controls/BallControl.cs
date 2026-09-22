using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SsqAnalyzer.Controls
{
    /// <summary>
    /// 双色球号码球控件（红球/蓝球）
    /// </summary>
    public class BallControl : Control
    {
        public static readonly DependencyProperty NumberProperty =
            DependencyProperty.Register(nameof(Number), typeof(int), typeof(BallControl),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty BallTypeProperty =
            DependencyProperty.Register(nameof(BallType), typeof(string), typeof(BallControl),
                new FrameworkPropertyMetadata("red", FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty SizeProperty =
            DependencyProperty.Register(nameof(Size), typeof(double), typeof(BallControl),
                new FrameworkPropertyMetadata(30.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public int Number
        {
            get => (int)GetValue(NumberProperty);
            set => SetValue(NumberProperty, value);
        }

        public string BallType
        {
            get => (string)GetValue(BallTypeProperty);
            set => SetValue(BallTypeProperty, value);
        }

        public double Size
        {
            get => (double)GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        static BallControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(BallControl),
                new FrameworkPropertyMetadata(typeof(BallControl)));
        }

        protected override Size MeasureOverride(Size constraint)
        {
            return new Size(Size, Size);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var s = Size;
            var isRed = BallType == "red";
            var color = isRed ? Color.FromRgb(0xE0, 0x48, 0x48) : Color.FromRgb(0x3B, 0x82, 0xF6);
            var glowColor = isRed ? Color.FromArgb(77, 224, 72, 72) : Color.FromArgb(77, 59, 130, 246);

            // 阴影
            dc.DrawEllipse(new SolidColorBrush(glowColor), null, new Point(s / 2 + 1, s / 2 + 1), s / 2, s / 2);
            // 球体
            dc.DrawEllipse(new SolidColorBrush(color), null, new Point(s / 2, s / 2), s / 2, s / 2);

            // 号码文字
            var ft = new FormattedText(Number.ToString("D2"),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Consolas"), s * 0.40, Brushes.White);
            dc.DrawText(ft, new Point((s - ft.Width) / 2, (s - ft.Height) / 2));
        }
    }
}
