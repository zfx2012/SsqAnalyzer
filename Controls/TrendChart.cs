using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Diagnostics;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Controls
{
    /// <summary>
    /// 通用 WPF 图表控件，支持折线图和柱状图
    /// </summary>
    public class TrendChart : Canvas
    {
        // 依赖属性
        public static readonly DependencyProperty ChartTitleProperty =
            DependencyProperty.Register(nameof(ChartTitle), typeof(string), typeof(TrendChart),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ShowLegendProperty =
            DependencyProperty.Register(nameof(ShowLegend), typeof(bool), typeof(TrendChart),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public string ChartTitle
        {
            get => (string)GetValue(ChartTitleProperty);
            set => SetValue(ChartTitleProperty, value);
        }
        public bool ShowLegend
        {
            get => (bool)GetValue(ShowLegendProperty);
            set => SetValue(ShowLegendProperty, value);
        }

        private List<ChartSeries>? _series;
        private List<string>? _labels;
        private double _minY, _maxY;

        // 边距
        private const double MarginLeft = 45;
        private const double MarginRight = 15;
        private const double MarginTop = 25;
        private const double MarginBottom = 30;

        /// <summary>
        /// 设置折线图数据
        /// </summary>
        public void SetLineData(List<string> labels, List<double> values, string lineColor = "#e04848", string fillColor = "")
        {
            _series = new List<ChartSeries>
            {
                new() { Name = "", Values = values, StrokeColor = lineColor, FillColor = fillColor, IsBar = false }
            };
            _labels = labels;
            ComputeYRange();
            InvalidateVisual();
        }

        /// <summary>
        /// 设置柱状图数据（支持多系列）
        /// </summary>
        public void SetBarData(List<ChartSeries> series, List<string> labels)
        {
            _series = series;
            _labels = labels;
            ComputeYRange();
            InvalidateVisual();
        }

        public void SetSingleBarData(List<string> labels, List<double> values,
            string fillColor = "#e04848", string strokeColor = "#e04848")
        {
            _series = new List<ChartSeries>
            {
                new() { Name = "", Values = values, FillColor = fillColor, StrokeColor = strokeColor, IsBar = true }
            };
            _labels = labels;
            ComputeYRange();
            InvalidateVisual();
        }

        private void ComputeYRange()
        {
            if (_series == null || _series.Count == 0) { _minY = 0; _maxY = 10; return; }
            var allValues = _series.SelectMany(s => s.Values).ToList();
            if (allValues.Count == 0) { _minY = 0; _maxY = 10; return; }
            _minY = 0; // 从0开始
            _maxY = allValues.Max();
            if (_maxY < 1) _maxY = 10;
            _maxY = Math.Ceiling(_maxY * 1.15); // 留15%顶部空间
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (_series == null || _labels == null || _labels.Count == 0) return;

            var w = ActualWidth;
            var h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            var chartW = w - MarginLeft - MarginRight;
            var chartH = h - MarginTop - MarginBottom;
            if (chartW <= 10 || chartH <= 10) return;

            var bg = TryFindResource("BgContent") as Brush ?? new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7));
            dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

            // 绘制网格
            var gridColor = new Pen(new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)), 0.5);
            var gridCount = 5;
            for (int i = 0; i <= gridCount; i++)
            {
                var y = MarginTop + chartH * i / gridCount;
                dc.DrawLine(gridColor, new Point(MarginLeft, y), new Point(MarginLeft + chartW, y));

                // Y轴标签
                var val = _maxY - (_maxY - _minY) * i / gridCount;
                var label = val >= 1 ? val.ToString("F0") : val.ToString("F1");
                var ft = new FormattedText(label,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 10, new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)));
                dc.DrawText(ft, new Point(2, y - ft.Height / 2));
            }

            bool isFirstSeriesBar = _series[0].IsBar;

            if (isFirstSeriesBar && _series.Count > 0)
            {
                // 柱状图
                var barWidth = Math.Min(chartW / _labels.Count * 0.7, 20);
                var spacing = chartW / _labels.Count;

                for (int si = 0; si < _series.Count; si++)
                {
                    var series = _series[si];
                    for (int i = 0; i < Math.Min(series.Values.Count, _labels.Count); i++)
                    {
                        var x = MarginLeft + i * spacing + (spacing - barWidth) / 2 + si * (barWidth / _series.Count);
                        var valHeight = (series.Values[i] / (_maxY - _minY)) * chartH;
                        var y = MarginTop + chartH - valHeight;

                        var brush = ParseColor(series.FillColor);
                        dc.DrawRectangle(brush, null, new Rect(x, y, barWidth / _series.Count, valHeight));
                    }
                }
            }
            else if (!isFirstSeriesBar && _series.Count > 0)
            {
                // 折线图
                var spacing = chartW / Math.Max(_labels.Count - 1, 1);

                foreach (var series in _series)
                {
                    var points = new List<Point>();
                    var strokeColor = ParseColor(series.StrokeColor);

                    for (int i = 0; i < Math.Min(series.Values.Count, _labels.Count); i++)
                    {
                        var x = MarginLeft + i * spacing;
                        var y = MarginTop + chartH - (series.Values[i] / (_maxY - _minY)) * chartH;
                        points.Add(new Point(x, y));

                        // 数据点圆点
                        dc.DrawEllipse(strokeColor, null, new Point(x, y), 3, 3);
                    }

                    // 绘制折线
                    if (points.Count >= 2)
                    {
                        var pen = new Pen(strokeColor, 1.5);
                        for (int i = 0; i < points.Count - 1; i++)
                            dc.DrawLine(pen, points[i], points[i + 1]);

                        // 填充区域
                        if (!string.IsNullOrEmpty(series.FillColor))
                        {
                            var fillColor = ParseColor(series.FillColor);
                            var geo = new StreamGeometry();
                            using (var ctx = geo.Open())
                            {
                                ctx.BeginFigure(points[0], true, true);
                                for (int i = 1; i < points.Count; i++)
                                    ctx.LineTo(points[i], true, false);
                                ctx.LineTo(new Point(points[^1].X, MarginTop + chartH), true, false);
                                ctx.LineTo(new Point(points[0].X, MarginTop + chartH), true, false);
                            }
                            dc.DrawGeometry(fillColor, null, geo);
                        }
                    }
                }
            }

            // X轴标签
            var xLabelStep = Math.Max(1, _labels.Count / 10);
            var xSpacing = chartW / Math.Max(_labels.Count - 1, 1);
            for (int i = 0; i < _labels.Count; i += xLabelStep)
            {
                var x = MarginLeft + i * xSpacing;
                var ft = new FormattedText(_labels[i],
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 9, new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)));
                dc.DrawText(ft, new Point(x - ft.Width / 2, MarginTop + chartH + 5));
            }

            // 图标题
            if (!string.IsNullOrEmpty(ChartTitle))
            {
                var ftTitle = new FormattedText(ChartTitle,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 12, new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0)));
                dc.DrawText(ftTitle, new Point((w - ftTitle.Width) / 2, 3));
            }
        }

        private static SolidColorBrush ParseColor(string? color)
        {
            if (string.IsNullOrEmpty(color)) return new SolidColorBrush(Colors.Red);
            try
            {
                if (color.StartsWith("#"))
                {
                    var hex = color.TrimStart('#');
                    if (hex.Length == 8)
                    {
                        var a = byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber);
                        var r = byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber);
                        var g = byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber);
                        var b = byte.Parse(hex[6..8], System.Globalization.NumberStyles.HexNumber);
                        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
                    }
                    if (hex.Length == 6)
                    {
                        var r = byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber);
                        var g = byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber);
                        var b = byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber);
                        return new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                }
                // 处理 rgba(...) 格式
                if (color.StartsWith("rgba("))
                {
                    var parts = color.Replace("rgba(", "").Replace(")", "").Split(',');
                    if (parts.Length == 4)
                    {
                        var r = byte.Parse(parts[0].Trim());
                        var g = byte.Parse(parts[1].Trim());
                        var b = byte.Parse(parts[2].Trim());
                        var a = (byte)(double.Parse(parts[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) * 255);
                        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrendChart.ParseColor] 颜色解析失败: {ex.Message}");
            }
            return new SolidColorBrush(Colors.Red);
        }
    }
}
