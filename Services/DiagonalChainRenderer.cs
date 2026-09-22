using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SsqAnalyzer.Models;
using SsqAnalyzer.Controls;

namespace SsqAnalyzer.Services;

/// <summary>
/// 对角线链预测渲染器 — 在走势矩阵上绘制斜连预测线。
/// 被 HistoryPage 和 TrendPage 共享使用，避免代码重复。
/// </summary>
public class DiagonalChainRenderer
{
    internal class DiagChain
    {
        public List<(int col, int rowIdx, int ballIdx)> Chain { get; set; } = new();
        public int PredCol { get; set; }
        public int Step { get; set; }
        public int Gap { get; set; }
    }

    private readonly MatrixGrid _matrixView;
    private readonly Grid _overlay;
    private List<List<int>> _recentReds = new();
    private List<List<int>> _recentBlues = new();
    private List<DrawRecord> _lastSource = new();
    private int _lastGapVal;
    private bool _lastStacked;
    private bool _pending;

    public DiagonalChainRenderer(MatrixGrid matrixView, Grid overlayContainer)
    {
        _matrixView = matrixView;
        _overlay = overlayContainer;
        _matrixView.SizeChanged += OnMatrixSizeChanged;
        _matrixView.LayoutUpdated += OnMatrixLayoutUpdated;
    }

    private void OnMatrixSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_overlay.Visibility == Visibility.Visible)
        {
            _pending = true;
            _matrixView.Dispatcher.InvokeAsync(RenderPending,
                System.Windows.Threading.DispatcherPriority.Render);
        }
        else if (_pending)
        {
            RenderPending();
        }
    }

    private void OnMatrixLayoutUpdated(object? sender, EventArgs e)
    {
        // 每次布局完成后触发；仅当有待渲染请求时重绘一次，确保坐标基于最终尺寸。
        if (_pending) RenderPending();
    }

    private void RenderPending()
    {
        if (!_pending) return;
        _pending = false;
        Render(_lastSource, _lastGapVal, _lastStacked);
    }

    /// <summary>
    /// 请求重绘（数据/选项变化时调用）。实际绘制延迟到矩阵布局完成后再执行，
    /// 由 SizeChanged / LayoutUpdated 事件驱动，彻底消除切换期数时的坐标偏差。
    /// </summary>
    public void Update(List<DrawRecord> source, int gapVal, bool stacked = false)
    {
        _lastSource = source;
        _lastGapVal = gapVal;
        _lastStacked = stacked;
        _pending = true;
        // 兜底：若尺寸未变化（如仅数据刷新），布局事件可能不触发，主动安排一次渲染。
        if (_matrixView.IsLoaded)
        {
            _matrixView.Dispatcher.InvokeAsync(RenderPending,
                System.Windows.Threading.DispatcherPriority.Render);
        }
    }

    /// <summary>取消待渲染请求并隐藏覆盖层。</summary>
    public void Clear()
    {
        _pending = false;
        _overlay.Children.Clear();
        _overlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>在矩阵上渲染斜连预测线</summary>
    public void Render(List<DrawRecord> source, int gapVal, bool stacked = false)
    {
        _overlay.Children.Clear();

        // 强制矩阵立即完成布局，确保后续读取的 RowDefinitions.ActualHeight /
        // ColumnDefinitions.ActualWidth 为最新值（避免切换期数/缩放时尺寸错位）。
        _matrixView.UpdateLayout();

        if (source.Count < 4)
        {
            _overlay.Visibility = Visibility.Collapsed;
            return;
        }

        int takeCount = Math.Min(source.Count, 20);
        var recentReds = new List<List<int>>();
        var recentBlues = new List<List<int>>();
        var recentRows = new List<int>();

        for (int ri = source.Count - 1; ri >= 0 && recentRows.Count < takeCount; ri--)
        {
            recentReds.Add(source[ri].RedBalls.Select(n => _matrixView.GetTrendColumn(n, true)).ToList());
            recentBlues.Add(new List<int> { _matrixView.GetTrendColumn(source[ri].BlueBall, false) });
            recentRows.Add(ri);
        }
        _recentReds = recentReds;
        _recentBlues = recentBlues;

        var allChains = new List<DiagChain>();
        int gStart = stacked ? 0 : gapVal;
        for (int g = gStart; g <= gapVal; g++)
        {
            allChains.AddRange(FindChains(recentReds, recentRows, 0, 32, 3, g));
            allChains.AddRange(FindChains(recentBlues, recentRows, 33, 48, 3, g));
        }

        if (allChains.Count == 0)
        {
            _overlay.Visibility = Visibility.Collapsed;
            return;
        }

        _overlay.Visibility = Visibility.Visible;

        // 清空历史显式尺寸，让 overlay 恢复自动贴合矩阵（Stretch 叠放）。
        _overlay.Width = double.NaN;
        _overlay.Height = double.NaN;

        var canvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false
        };
        _overlay.Children.Add(canvas);

        var pink = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x81));
        int totalRows = source.Count;

        foreach (var chain in allChains)
        {
            var points = new List<Point>();
            bool valid = true;

            // 收集每条链的画布坐标点
            foreach (var item in chain.Chain)
            {
                if (item.rowIdx >= totalRows) { valid = false; break; }
                var pt = GetCellCenter(item.rowIdx, item.col, totalRows);
                if (pt.HasValue) points.Add(pt.Value);
                else { valid = false; break; }
            }

            if (valid)
            {
                var predPt = GetPredCellCenter(chain.PredCol, totalRows, 0);
                if (predPt.HasValue) points.Add(predPt.Value);
                else valid = false;
            }

            if (!valid || points.Count < 2) continue;

            // 简单直连：点与点之间直接画线，透明度统一
            for (int i = 0; i < points.Count - 1; i++)
            {
                DrawLine(canvas, points[i], points[i + 1], 0.9, pink);
            }

            foreach (var pt in points)
            {
                canvas.Children.Add(new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = pink,
                    Opacity = 0.9,
                    Margin = new Thickness(pt.X - 2.5, pt.Y - 2.5, 0, 0)
                });
            }

            if (points.Count >= 2)
            {
                var last = points[^1];
                var prev = points[^2];
                var angle = Math.Atan2(last.Y - prev.Y, last.X - prev.X);
                double arrowLen = 8, arrowAngle = 0.5;

                var a1 = new Point(last.X - arrowLen * Math.Cos(angle - arrowAngle),
                                   last.Y - arrowLen * Math.Sin(angle - arrowAngle));
                var a2 = new Point(last.X - arrowLen * Math.Cos(angle + arrowAngle),
                                   last.Y - arrowLen * Math.Sin(angle + arrowAngle));

                canvas.Children.Add(new Line { X1 = last.X, Y1 = last.Y, X2 = a1.X, Y2 = a1.Y, Stroke = pink, StrokeThickness = 1.5 });
                canvas.Children.Add(new Line { X1 = last.X, Y1 = last.Y, X2 = a2.X, Y2 = a2.Y, Stroke = pink, StrokeThickness = 1.5 });
            }
        }
    }

    private Point? GetCellCenter(int dataRowIdx, int col, int totalRows)
    {
        try
        {
            int visRow = dataRowIdx + 1;
            if (visRow >= _matrixView.RowDefinitions.Count) return null;

            double y = 0;
            for (int i = 0; i < visRow; i++)
                y += _matrixView.RowDefinitions[i].ActualHeight;
            y += _matrixView.RowDefinitions[visRow].ActualHeight / 2;

            double x = 0;
            int visCol = _matrixView.GetVisualColumnFromTrendColumn(col);
            for (int i = 0; i < visCol; i++)
                x += _matrixView.ColumnDefinitions[i].ActualWidth;
            x += _matrixView.ColumnDefinitions[visCol].ActualWidth / 2;

            return new Point(x, y);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiagonalChainRenderer.GetCellCenter] 坐标计算异常: {ex.Message}");
            return null;
        }
    }

    private Point? GetPredCellCenter(int col, int totalRows, int predRow)
    {
        try
        {
            int visRow = totalRows + 1 + predRow;
            if (visRow >= _matrixView.RowDefinitions.Count) return null;

            double y = 0;
            for (int i = 0; i < visRow; i++)
                y += _matrixView.RowDefinitions[i].ActualHeight;
            y += _matrixView.RowDefinitions[visRow].ActualHeight * 0.25;

            double x = 0;
            int visCol = _matrixView.GetVisualColumnFromTrendColumn(col);
            for (int i = 0; i < visCol; i++)
                x += _matrixView.ColumnDefinitions[i].ActualWidth;
            x += _matrixView.ColumnDefinitions[visCol].ActualWidth / 2;

            return new Point(x, y);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiagonalChainRenderer.GetPredCellCenter] 坐标计算异常: {ex.Message}");
            return null;
        }
    }

    internal static List<DiagChain> FindChains(List<List<int>> recentBalls, List<int> recentRows,
        int colMin, int colMax, int level, int gap)
    {
        var res = new List<DiagChain>();
        int need = level - 1;
        int maxIdx = gap + (need - 1) * (gap + 1);
        if (gap != 0) maxIdx = Math.Max(maxIdx, 2 * gap + 1);
        if (recentBalls.Count <= maxIdx) return res;

        for (int col = colMin; col <= colMax; col++)
        {
            for (int step = -6; step <= 6; step++)
            {
                if (step == 0) continue;
                var chain = new List<(int col, int rowIdx, int ballIdx)>();
                bool valid = true;

                for (int p = 0; p < need && valid; p++)
                {
                    int idx = gap == 0 ? p : gap + p * (gap + 1);
                    int exp = col - step * (p + 1);
                    if (exp < colMin || exp > colMax) { valid = false; break; }
                    if (idx >= recentBalls.Count) { valid = false; break; }
                    if (recentBalls[idx].Contains(exp))
                        chain.Add((exp, recentRows[idx], idx));
                    else
                        valid = false;
                }

                if (valid && chain.Count == need)
                {
                    chain.Reverse();
                    res.Add(new DiagChain { Chain = chain, PredCol = col, Step = step, Gap = gap });
                }
            }
        }
        return res;
    }

    private static void DrawLine(Canvas canvas, Point from, Point to, double opacity, Brush color)
    {
        canvas.Children.Add(new Line
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            Stroke = color,
            StrokeThickness = 1.5,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = opacity
        });
    }
}
