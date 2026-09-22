using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Diagnostics;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

namespace SsqAnalyzer.Controls
{
    /// <summary>
    /// 走势矩阵表控件 — 仿 ssq.html 的完整表格功能
    /// 支持：遗漏数据显示、红/蓝球标记、走势形态变色、冷号标记、预选行、点击选号
    /// </summary>
    public class MatrixGrid : Grid
    {
        private const int RedCount = 33;
        private const int BlueCount = 16;
        private const int ColWeekDay = 1; // 星期列索引

        // 颜色令牌（部分引用全局定义避免重复）
        private static readonly Brush BgHeader = UiColors.BgHeader;
        private static readonly Brush BgRowEven = UiColors.BgRow1;
        private static readonly Brush BgRowOdd = UiColors.BgRow0;
        private static readonly Brush BgRowHi = UiColors.BgRowHi;
        private static readonly Brush BgDivider = UiColors.BgDivider;
        private static readonly Brush BorderColor = UiColors.Border;
        private static readonly Brush BorderDivider = UiColors.BorderDivider;
        private static readonly Brush BgZoneEmpty = UiColors.BgZoneEmpty;
        private static readonly Brush TextSecondary = UiColors.TextSec;
        private static readonly Brush TextTertiary = UiColors.TextTri;
        private static readonly Brush RedBallFill = UiColors.Red;
        private static readonly Brush BlueBallFill = UiColors.Blue;
        private static readonly Brush White = UiColors.White;
        private static readonly Brush ColdStroke = UiColors.ColdStroke;
        private static readonly Brush BgPredRow = UiColors.BgPredRow;

        // 缓存 Consolas 字体和常用颜色
        private static readonly FontFamily ConsolasFont = new("Consolas");
        private static readonly Typeface ConsolasTypeface = new(ConsolasFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface ConsolasBoldTypeface = new(ConsolasFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Brush GoldText = FreezeBrush(new SolidColorBrush(Color.FromRgb(0xCC, 0xAA, 0x66)));

        // 缓存的球体发光和背景刷（避免每帧 new）
        private static readonly Brush RedGlow = FreezeBrush(new SolidColorBrush(Color.FromArgb(77, 224, 72, 72)));
        private static readonly Brush BlueGlow = FreezeBrush(new SolidColorBrush(Color.FromArgb(77, 59, 130, 246)));
        private static readonly Brush RedBg = FreezeBrush(new SolidColorBrush(Color.FromArgb(25, 224, 72, 72)));
        private static readonly Brush BlueBg = FreezeBrush(new SolidColorBrush(Color.FromArgb(25, 59, 130, 246)));
        private static readonly Brush RepeatFill = FreezeBrush(new SolidColorBrush(Color.FromRgb(0x91, 0x22, 0xD0)));
        private static readonly Brush DiagonalFill = FreezeBrush(new SolidColorBrush(Color.FromRgb(0xFF, 0x78, 0x00)));
        private static readonly Brush ConsecutiveFill = FreezeBrush(new SolidColorBrush(Color.FromRgb(0x5F, 0x73, 0x6F)));

        private static Brush FreezeBrush(Brush b) { b.Freeze(); return b; }

        // 状态缓存
        private List<DrawRecord> _records = new();
        private bool _showMiss = true, _showCold = true, _showZO2 = false;
        private int _periodCount = 50;
        private int _lastFullRenderPeriods = -1;
        private List<DrawRecord>? _recent;
        private int[]? _missValues;
        private int[,]? _missMatrix; // [rowIdx, ballIdx] 预计算遗漏值矩阵
        private List<DrawRecord>? _fullData; // 完整数据集（用于跨窗口遗漏计算）
        private RenderedRecord[]? _renderedRecords;
        private int[,]? _renderedMissMatrix;
        private bool _displayChangedSinceFullRender;

        // Copy values, not DrawRecord references: imported records and their ball lists are mutable.
        private sealed record RenderedRecord(int Period, DateTime DrawDate, int BlueBall, int[] RedBalls);

        // ⚡ UpdateDisplay 快速路径缓存 — 避免遍历所有子元素
        private readonly List<Border> _missCells = new();
        private readonly List<Border> _predCells = new();

        // Reuse only plain text cells during a full rebuild. Ball/cold panels and
        // their interaction state continue through the existing construction path.
        private readonly List<Border> _textCells = new();
        private readonly Queue<Border> _reusableTextCells = new();

        private Border RentTextCell()
        {
            var border = _reusableTextCells.Count > 0
                ? _reusableTextCells.Dequeue()
                : new Border { Child = new TextBlock() };
            _textCells.Add(border);
            return border;
        }

        private readonly Dictionary<(int Row, bool IsRed, int Number), bool> _selections = new();

        private enum BallPattern { None, Consecutive, Diagonal, Repeat }

        // 012路排序数组
        public static readonly int[] RedOrderNormal = Enumerable.Range(1, 33).ToArray();
        public static readonly int[] RedOrderZO2 = Enumerable.Range(1, 33)
            .OrderBy(n => n % 3).ThenBy(n => n).ToArray();
        public static readonly int[] BlueOrderNormal = Enumerable.Range(1, 16).ToArray();
        public static readonly int[] BlueOrderZO2 = Enumerable.Range(1, 16)
            .OrderBy(n => n % 3).ThenBy(n => n).ToArray();

        // Tag 常量
        private const string TAG_PREFIX_MISS = "miss:";
        private const string TAG_PREFIX_BALL = "ball:";
        private const string TAG_PREFIX_SEP = "sep";
        private const string TAG_PREFIX_PERIOD = "period";
        private const string TAG_PREFIX_PRED = "pred:";
        private const string TAG_PREFIX_PRED_SEL = "predsel:";

        private int GetRedBallAt(int idx) => (_showZO2 ? RedOrderZO2 : RedOrderNormal)[idx];

        /// <summary>获取号码在当前走势排列中的连续列索引。</summary>
        public int GetTrendColumn(int ballNumber, bool isRed)
        {
            var order = isRed
                ? (_showZO2 ? RedOrderZO2 : RedOrderNormal)
                : (_showZO2 ? BlueOrderZO2 : BlueOrderNormal);
            int position = Array.IndexOf(order, ballNumber);
            return isRed ? position : RedCount + position;
        }

        /// <summary>将连续走势列索引转为包含期号、周期和分隔列的 Grid 列。</summary>
        public int GetVisualColumnFromTrendColumn(int trendColumn) =>
            trendColumn < RedCount ? trendColumn + ColWeekDay + 1 : trendColumn + ColWeekDay + 2;

        /// <summary>
        /// 渲染走势矩阵（支持增量模式）
        /// </summary>
        public void Render(List<DrawRecord> records, bool showMiss, bool showCold, int periodCount, bool showZO2 = false)
        {
            bool dataRefChanged = _records != records;
            bool firstBuild = Children.Count == 0;
            bool zo2Changed = _showZO2 != showZO2;
            bool optionsChanged = _showMiss != showMiss || _showCold != showCold;

            _records = records;
            _showMiss = showMiss;
            _showCold = showCold;
            _showZO2 = showZO2;
            _periodCount = periodCount;
            _recent = records.TakeLast(periodCount).ToList();
            ComputeMissValues();

            // A fresh list alone does not change the chart. Keep the full-render path after
            // display/selection changes so existing cell styles are retained on data reload.
            if (firstBuild || zo2Changed || _lastFullRenderPeriods != periodCount
                || (dataRefChanged && (optionsChanged || _displayChangedSinceFullRender))
                || !MatchesRenderedData())
            {
                _lastFullRenderPeriods = periodCount;
                FullRender();
                _renderedRecords = _recent.Select(record => new RenderedRecord(
                    record.Period, record.DrawDate, record.BlueBall, record.RedBalls.ToArray())).ToArray();
                _renderedMissMatrix = _missMatrix;
                _displayChangedSinceFullRender = false;
            }
            else if (optionsChanged)
            {
                UpdateDisplay();
                _displayChangedSinceFullRender = true;
            }
        }

        private bool MatchesRenderedData()
        {
            if (_renderedRecords is null || _recent is null || _renderedRecords.Length != _recent.Count)
                return false;
            for (int row = 0; row < _recent.Count; row++)
            {
                var current = _recent[row];
                var rendered = _renderedRecords[row];
                if (rendered.Period != current.Period || rendered.DrawDate != current.DrawDate
                    || rendered.BlueBall != current.BlueBall || rendered.RedBalls.Length != current.RedBalls.Count)
                    return false;
                for (int index = 0; index < rendered.RedBalls.Length; index++)
                    if (rendered.RedBalls[index] != current.RedBalls[index]) return false;
            }
            if (_recent.Count == 0) return true;
            if (_missMatrix is null || _renderedMissMatrix is null
                || _missMatrix.GetLength(0) != _renderedMissMatrix.GetLength(0)
                || _missMatrix.GetLength(1) != _renderedMissMatrix.GetLength(1))
                return false;
            for (int row = 0; row < _missMatrix.GetLength(0); row++)
                for (int ball = 0; ball < _missMatrix.GetLength(1); ball++)
                    if (_missMatrix[row, ball] != _renderedMissMatrix[row, ball]) return false;
            return true;
        }

        /// <summary>
        /// 设置完整数据集引用（用于跨窗口遗漏值计算）
        /// </summary>
        public void SetFullData(List<DrawRecord> fullData) => _fullData = fullData;

        private void ComputeMissValues()
        {
            if (_recent == null || _recent.Count == 0) return;
            // 抽取到 MissMatrixCalculator 共享，行为与原内联实现完全一致：
            //   1) 在 _fullData 中按 ReferenceEquals 定位 _recent[0] 起始位置；
            //   2) 倒序扫描 _fullData 前缀作为 miss 初值；
            //   3) 顺序扫描 _recent 累加写入 _missMatrix。
            _missMatrix = MissMatrixCalculator.Compute(_recent, _fullData);
            _missValues = MissMatrixCalculator.GetLastRowValues(_missMatrix);
        }

        // ==================== 全量渲染 ====================

        private void FullRender()
        {
            // ⚡ BeginInit 暂停全部布局更新，EndInit 后一次性完成
            BeginInit();
            try
            {

                _missCells.Clear();
                _predCells.Clear();
                foreach (var cell in _textCells)
                    if (cell.Child is TextBlock) _reusableTextCells.Enqueue(cell);
                _textCells.Clear();
                Children.Clear();
                ColumnDefinitions.Clear();
                RowDefinitions.Clear();

                // 列定义
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72), MinWidth = 60 });
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32), MinWidth = 28 });
                for (int i = 0; i < RedCount; i++)
                    ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 18 });
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10), MinWidth = 10 });
                for (int i = 0; i < BlueCount; i++)
                    ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 18 });

                var tf = ConsolasTypeface;
                var tfBold = ConsolasBoldTypeface;

                // 表头行
                RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
                AddHeaderCell(0, 0, "期号", TextSecondary, tfBold, 11);
                AddHeaderCell(0, ColWeekDay, "周期", TextSecondary, tfBold, 11);
                var hdrOrder = _showZO2 ? RedOrderZO2 : RedOrderNormal;
                for (int i = 0; i < RedCount; i++)
                    AddHeaderCell(0, i + ColWeekDay + 1, hdrOrder[i].ToString("D2"), RedBallFill, tf, 11);

                var sepBorder = new Border { Background = BgDivider, BorderThickness = new Thickness(0, 0, 2, 0), BorderBrush = BorderDivider, Tag = TAG_PREFIX_SEP };
                SetElement(sepBorder, 0, RedCount + ColWeekDay + 1);
                Children.Add(sepBorder);

                var blueHdrOrder = _showZO2 ? BlueOrderZO2 : BlueOrderNormal;
                for (int i = 0; i < BlueCount; i++)
                    AddHeaderCell(0, i + RedCount + ColWeekDay + 2, blueHdrOrder[i].ToString("D2"), BlueBallFill, tf, 11);

                if (_recent == null || _recent.Count == 0) return;
                var ballPatterns = FindBallPatterns(_recent);

                // 数据行
                for (int ri = 0; ri < _recent.Count; ri++)
                {
                    RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
                    var record = _recent[ri];
                    var rowBg = BgRowOdd;
                    var rowIdx = ri + 1;

                    AddCell(rowIdx, 0, record.Period.ToString(), rowBg, TextSecondary, tfBold, 11, false, TAG_PREFIX_PERIOD);
                    AddCell(rowIdx, ColWeekDay, record.WeekDayName, rowBg, TextSecondary, tf, 11, false, TAG_PREFIX_PERIOD);
                    var redSet = new HashSet<int>(record.RedBalls);

                    bool zone1Empty = true, zone2Empty = true, zone3Empty = true;
                    if (_showZO2)
                    {
                        foreach (var n in record.RedBalls)
                        {
                            int rem = n % 3;
                            if (rem == 0) zone1Empty = false;
                            else if (rem == 1) zone2Empty = false;
                            else zone3Empty = false;
                        }
                    }
                    else
                    {
                        foreach (var n in record.RedBalls)
                        {
                            if (n <= 11) zone1Empty = false;
                            else if (n <= 22) zone2Empty = false;
                            else zone3Empty = false;
                        }
                    }

                    var order = _showZO2 ? RedOrderZO2 : RedOrderNormal;
                    for (int ri2 = 0; ri2 < RedCount; ri2++)
                    {
                        int n = order[ri2];
                        int gridCol = ri2 + ColWeekDay + 1;
                        int zoneIdx = _showZO2 ? (n % 3) : (n <= 11 ? 0 : (n <= 22 ? 1 : 2));
                        bool zoneEmpty = zoneIdx switch { 0 => zone1Empty, 1 => zone2Empty, _ => zone3Empty };
                        var cellBg = zoneEmpty ? BgZoneEmpty : BgRowOdd;
                        if (redSet.Contains(n))
                            AddBallCell(rowIdx, gridCol, n, "red", ballPatterns.GetValueOrDefault((ri, true, n)));
                        else
                        {
                            var miss = _missMatrix?[ri, n - 1] ?? 0;
                            AddCell(rowIdx, gridCol, _showMiss ? miss.ToString() : "", cellBg,
                                GetMissColor(miss), tf, 10, false, TAG_PREFIX_MISS + miss);
                        }
                    }

                    AddCell(rowIdx, RedCount + ColWeekDay + 1, "", BgDivider, TextSecondary, tf, 10, false, TAG_PREFIX_SEP);

                    // 蓝球区 012 路断区检测
                    bool blueRem0Empty = false, blueRem1Empty = false, blueRem2Empty = false;
                    if (_showZO2)
                    {
                        int bb = record.BlueBall;
                        int rem = bb % 3;
                        if (rem == 0) { blueRem1Empty = true; blueRem2Empty = true; }
                        else if (rem == 1) { blueRem0Empty = true; blueRem2Empty = true; }
                        else { blueRem0Empty = true; blueRem1Empty = true; }
                    }

                    var blueOrder = _showZO2 ? BlueOrderZO2 : BlueOrderNormal;
                    for (int bi = 0; bi < BlueCount; bi++)
                    {
                        int n = blueOrder[bi];
                        int gridCol = bi + RedCount + ColWeekDay + 2;

                        Brush cellBg = rowBg;
                        if (_showZO2)
                        {
                            int rem = n % 3;
                            bool zoneEmpty = rem switch { 0 => blueRem0Empty, 1 => blueRem1Empty, _ => blueRem2Empty };
                            cellBg = zoneEmpty ? BgZoneEmpty : rowBg;
                        }

                        if (record.BlueBall == n)
                            AddBallCell(rowIdx, gridCol, n, "blue", ballPatterns.GetValueOrDefault((ri, false, n)));
                        else
                        {
                            var miss = _missMatrix?[ri, RedCount + n - 1] ?? 0;
                            AddCell(rowIdx, gridCol, _showMiss ? miss.ToString() : "",
                                cellBg, GetMissBlueColor(miss), tf, 9, false, TAG_PREFIX_MISS + miss);
                        }
                    }
                }

                // 预选行
                int totalRows = _recent.Count;
                for (int p = 0; p < 3; p++)
                {
                    RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
                    var rowIdx = totalRows + 1 + p;
                    var predLabelBorder = new Border
                    {
                        Background = BgPredRow,
                        BorderThickness = new Thickness(0.5),
                        BorderBrush = BorderColor,
                        Tag = TAG_PREFIX_PERIOD
                    };
                    predLabelBorder.Child = new TextBlock
                    {
                        Text = $"预选行{p + 1}",
                        FontFamily = ConsolasFont,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = GoldText,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    SetElement(predLabelBorder, rowIdx, 0);
                    Grid.SetColumnSpan(predLabelBorder, ColWeekDay + 1);
                    Children.Add(predLabelBorder);

                    var orderPred = _showZO2 ? RedOrderZO2 : RedOrderNormal;
                    for (int ri2 = 0; ri2 < RedCount; ri2++)
                    {
                        int n = orderPred[ri2];
                        int gridCol = ri2 + ColWeekDay + 1;
                        var key = (p, true, n);
                        bool selected = _selections.TryGetValue(key, out var s) && s;
                        if (selected)
                            AddBallCell(rowIdx, gridCol, n, "red");
                        else
                        {
                            bool cold = p == 0 && _showCold && _missValues != null && n - 1 < _missValues.Length && _missValues[n - 1] >= 10;
                            if (cold)
                                AddColdCell(rowIdx, gridCol, n);
                            else
                                AddCell(rowIdx, gridCol, n.ToString("D2"), BgPredRow, TextTertiary, tf, 9, false, TAG_PREFIX_PRED + n);
                        }
                    }

                    AddCell(rowIdx, RedCount + ColWeekDay + 1, "", BgDivider, TextSecondary, tf, 10, false, TAG_PREFIX_SEP);

                    var blueOrderP = _showZO2 ? BlueOrderZO2 : BlueOrderNormal;
                    for (int bi = 0; bi < BlueCount; bi++)
                    {
                        int bn = blueOrderP[bi];
                        int visCol = bi + RedCount + ColWeekDay + 2;
                        var key = (p, false, bn);
                        bool selected = _selections.TryGetValue(key, out var s) && s;
                        if (selected)
                            AddBallCell(rowIdx, visCol, bn, "blue");
                        else
                        {
                            bool cold = false;
                            if (p == 0 && _showCold && _missValues != null)
                            {
                                int blueIdx = 33 + (bn - 1);
                                cold = blueIdx < _missValues.Length && _missValues[blueIdx] >= 16;
                            }
                            if (cold)
                                AddColdCell(rowIdx, visCol, bn);
                            else
                                AddCell(rowIdx, visCol, bn.ToString("D2"), BgPredRow, TextTertiary, tf, 9, false, TAG_PREFIX_PRED + bn);
                        }
                    }
                }

                MouseDown -= OnMatrixMouseDown;
                MouseDown += OnMatrixMouseDown;

            }
            finally
            {
                // Do not retain the excess cells after switching to a smaller window.
                _reusableTextCells.Clear();
                // ⚡ 恢复布局，一次性完成
                EndInit();
            }
        }

        // ==================== 增量更新 ====================

        private void UpdateDisplay()
        {
            if (_missCells.Count == 0 && _predCells.Count == 0) return;

            bool showMiss = _showMiss;

            // ⚡ 仅遍历缓存列表，避免 10,000+ Children 全量遍历
            foreach (var border in _missCells)
            {
                if (border.Child is TextBlock tb)
                {
                    if (showMiss)
                    {
                        // 从 Tag 恢复遗漏数字（tag 格式: "miss:N"）
                        string? tag = border.Tag as string;
                        tb.Text = tag != null && tag.StartsWith(TAG_PREFIX_MISS) ? tag[5..] : "";
                    }
                    tb.Visibility = showMiss ? Visibility.Visible : Visibility.Hidden;
                }
            }

            foreach (var border in _predCells)
                UpdatePredCell(border, border.Tag as string ?? "");
        }

        private void UpdatePredCell(Border border, string tag)
        {
            if (!int.TryParse(tag[5..], out int number)) return;

            bool isCold = _showCold && _missValues != null;

            int row = GetRow(border);
            int col = GetColumn(border);
            if (row < 0 || col < 0) return;

            int totalDataRows = _recent?.Count ?? 0;
            bool isFirstPredRow = row == totalDataRows + 1;
            bool shouldBeCold = isCold && isFirstPredRow;
            bool isRedZone = col >= ColWeekDay + 1 && col <= ColWeekDay + RedCount;
            bool isBlueZone = col >= RedCount + ColWeekDay + 2 && col <= RedCount + ColWeekDay + 1 + BlueCount;

            if (shouldBeCold && _missValues != null)
            {
                int pos = isRedZone ? (col - ColWeekDay - 1) : (col - RedCount - ColWeekDay - 2);
                int ballNum = isRedZone
                    ? (_showZO2 ? RedOrderZO2[pos] : RedOrderNormal[pos])
                    : (_showZO2 ? BlueOrderZO2[pos] : BlueOrderNormal[pos]);
                int missIdx = isRedZone ? (ballNum - 1) : (33 + ballNum - 1);
                int threshold = isRedZone ? 10 : 16;
                shouldBeCold = missIdx < _missValues.Length && _missValues[missIdx] >= threshold;
            }

            bool currentlyCold = border.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Ellipse;

            if (shouldBeCold != currentlyCold)
            {
                if (shouldBeCold)
                    border.Child = BuildColdCircle(number);
                else
                {
                    var tb = new TextBlock
                    {
                        Text = number.ToString("D2"),
                        FontFamily = ConsolasFont,
                        FontSize = 10,
                        Foreground = TextTertiary,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    border.Child = tb;
                }
            }
        }

        // ==================== 点击事件 ====================

        private void OnMatrixMouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            int totalDataRows = _recent?.Count ?? 0;
            int headerRows = 1;
            int firstPredRow = totalDataRows + headerRows;

            int rowIdx = -1;
            double y = 0;
            for (int r = 0; r < RowDefinitions.Count; r++)
            {
                double rh = RowDefinitions[r].ActualHeight;
                if (pos.Y >= y && pos.Y < y + rh) { rowIdx = r; break; }
                y += rh;
            }
            if (rowIdx < firstPredRow) return;

            int predRowNum = rowIdx - firstPredRow;

            int colIdx = -1;
            double x = 0;
            for (int c = 0; c < ColumnDefinitions.Count; c++)
            {
                double cw = ColumnDefinitions[c].ActualWidth;
                if (pos.X >= x && pos.X < x + cw) { colIdx = c; break; }
                x += cw;
            }
            if (colIdx <= 0 || colIdx == ColWeekDay || colIdx == RedCount + ColWeekDay + 1) return;

            bool isRed = colIdx >= ColWeekDay + 1 && colIdx <= ColWeekDay + RedCount;
            int number = isRed
                ? (_showZO2 ? RedOrderZO2 : RedOrderNormal)[colIdx - ColWeekDay - 1]
                : (_showZO2 ? BlueOrderZO2 : BlueOrderNormal)[colIdx - RedCount - ColWeekDay - 2];
            var key = (predRowNum, isRed, number);
            bool cur = _selections.TryGetValue(key, out var s) && s;
            _selections[key] = !cur;

            UpdatePredRowCells(predRowNum);
        }

        private void UpdatePredRowCells(int predRowNum)
        {
            _displayChangedSinceFullRender = true;
            int totalDataRows = _recent?.Count ?? 0;
            int rowIdx = totalDataRows + 1 + predRowNum;
            var tf = ConsolasTypeface;

            var updateOrder = _showZO2 ? RedOrderZO2 : RedOrderNormal;
            for (int ri2 = 0; ri2 < RedCount; ri2++)
            {
                int n = updateOrder[ri2];
                int gridCol = ri2 + ColWeekDay + 1;
                var key = (predRowNum, true, n);
                bool selected = _selections.TryGetValue(key, out var s) && s;
                var cell = FindCell(rowIdx, gridCol);
                if (cell != null)
                {
                    if (selected)
                    {
                        bool isRed = true;
                        cell.Child = BuildBallPanel(n, isRed);
                        cell.Tag = TAG_PREFIX_BALL + "red:" + n;
                    }
                    else
                    {
                        bool cold = predRowNum == 0 && _showCold && _missValues != null && n - 1 < _missValues.Length && _missValues[n - 1] >= 10;
                        if (cold)
                            cell.Child = BuildColdCircle(n);
                        else
                            cell.Child = new TextBlock { Text = n.ToString("D2"), FontFamily = ConsolasFont, FontSize = 10, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                        cell.Tag = TAG_PREFIX_PRED + n;
                    }
                }
            }

            var blueUpdateOrder = _showZO2 ? BlueOrderZO2 : BlueOrderNormal;
            for (int bi = 0; bi < BlueCount; bi++)
            {
                int bn = blueUpdateOrder[bi];
                int visCol = bi + RedCount + ColWeekDay + 2;
                var key = (predRowNum, false, bn);
                bool selected = _selections.TryGetValue(key, out var s) && s;
                var cell = FindCell(rowIdx, visCol);
                if (cell != null)
                {
                    if (selected)
                    {
                        cell.Child = BuildBallPanel(bn, false);
                        cell.Tag = TAG_PREFIX_BALL + "blue:" + bn;
                    }
                    else
                    {
                        bool cold = false;
                        if (predRowNum == 0 && _showCold && _missValues != null)
                        {
                            int blueIdx = 33 + (bn - 1);
                            cold = blueIdx < _missValues.Length && _missValues[blueIdx] >= 16;
                        }
                        if (cold)
                            cell.Child = BuildColdCircle(bn);
                        else
                            cell.Child = new TextBlock { Text = bn.ToString("D2"), FontFamily = ConsolasFont, FontSize = 10, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                        cell.Tag = TAG_PREFIX_PRED + bn;
                    }
                }
            }
        }

        private Border? FindCell(int row, int col)
        {
            foreach (var child in Children)
            {
                if (child is Border b && GetRow(b) == row && GetColumn(b) == col)
                    return b;
            }
            return null;
        }

        private static Grid BuildBallPanel(int number, bool isRed)
        {
            var fill = isRed ? RedBallFill : BlueBallFill;
            var glow = isRed ? RedGlow : BlueGlow;
            var panel = new Grid();
            panel.Children.Add(new Ellipse { Width = 17, Height = 17, Fill = fill, Stroke = glow, StrokeThickness = 1 });
            panel.Children.Add(new TextBlock
            {
                Text = number.ToString("D2"),
                FontFamily = ConsolasFont,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return panel;
        }

        private static Grid BuildColdCircle(int number)
        {
            var panel = new Grid();
            panel.Children.Add(new Ellipse
            {
                Width = 16,
                Height = 16,
                Stroke = ColdStroke,
                StrokeThickness = 1,
                Fill = Brushes.Transparent
            });
            panel.Children.Add(new TextBlock
            {
                Text = number.ToString("D2"),
                FontFamily = ConsolasFont,
                FontSize = 10,
                Foreground = ColdStroke,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return panel;
        }

        // ==================== 辅助方法 ====================

        // 遗漏颜色缓存（引用全局）
        private static readonly Brush MissHigh = UiColors.MissHigh;
        private static readonly Brush MissMid = UiColors.MissMid;
        private static readonly Brush MissLow = UiColors.MissLow;
        private static readonly Brush MissNone = UiColors.MissNone;
        private static readonly Brush MissBlueHot = UiColors.MissBlueHot;
        private static readonly Brush MissBlueWarm = UiColors.MissBlueWarm;

        private static Brush GetMissColor(int miss)
        {
            if (miss >= 15) return MissHigh;
            if (miss >= 10) return MissMid;
            if (miss >= 5) return MissLow;
            return MissNone;
        }

        private static Brush GetMissBlueColor(int miss)
        {
            if (miss >= 20) return MissBlueHot;
            if (miss >= 10) return MissBlueWarm;
            return MissNone;
        }

        private Dictionary<(int Row, bool IsRed, int Number), BallPattern> FindBallPatterns(
            IReadOnlyList<DrawRecord> records)
        {
            var result = new Dictionary<(int Row, bool IsRed, int Number), BallPattern>();

            static bool Contains(DrawRecord record, bool isRed, int number) =>
                isRed ? record.RedBalls.Contains(number) : record.BlueBall == number;

            static void Mark(Dictionary<(int Row, bool IsRed, int Number), BallPattern> patterns,
                int row, bool isRed, int number, BallPattern pattern)
            {
                var key = (row, isRed, number);
                if (!patterns.TryGetValue(key, out var current) || pattern > current)
                    patterns[key] = pattern;
            }

            // 单期内连续 3 个及以上红球。
            for (int row = 0; row < records.Count; row++)
            {
                var numbers = records[row].RedBalls.Distinct().OrderBy(number => number).ToArray();
                for (int start = 0; start < numbers.Length;)
                {
                    int end = start;
                    while (end + 1 < numbers.Length && numbers[end + 1] == numbers[end] + 1) end++;
                    if (end - start + 1 >= 3)
                        for (int index = start; index <= end; index++)
                            Mark(result, row, true, numbers[index], BallPattern.Consecutive);
                    start = end + 1;
                }
            }

            // 连续显示期中的三斜连（号码逐期 ±1）和三重号；滑动窗口自然覆盖 3 期以上。
            foreach (bool isRed in new[] { true, false })
            {
                int maxNumber = isRed ? RedCount : BlueCount;
                var order = isRed
                    ? (_showZO2 ? RedOrderZO2 : RedOrderNormal)
                    : (_showZO2 ? BlueOrderZO2 : BlueOrderNormal);
                for (int row = 0; row + 2 < records.Count; row++)
                {
                    for (int number = 1; number <= maxNumber; number++)
                    {
                        if (Contains(records[row], isRed, number)
                            && Contains(records[row + 1], isRed, number)
                            && Contains(records[row + 2], isRed, number))
                        {
                            for (int offset = 0; offset < 3; offset++)
                                Mark(result, row + offset, isRed, number, BallPattern.Repeat);
                        }

                    }

                    for (int position = 0; position < order.Length; position++)
                    {
                        foreach (int direction in new[] { -1, 1 })
                        {
                            int secondPosition = position + direction;
                            int thirdPosition = position + direction * 2;
                            if (thirdPosition < 0 || thirdPosition >= order.Length) continue;
                            int number = order[position];
                            int second = order[secondPosition];
                            int third = order[thirdPosition];
                            if (!Contains(records[row], isRed, number)
                                || !Contains(records[row + 1], isRed, second)
                                || !Contains(records[row + 2], isRed, third)) continue;

                            Mark(result, row, isRed, number, BallPattern.Diagonal);
                            Mark(result, row + 1, isRed, second, BallPattern.Diagonal);
                            Mark(result, row + 2, isRed, third, BallPattern.Diagonal);
                        }
                    }
                }
            }

            return result;
        }

        private bool IsThickBorderCol(int col) =>
            col == RedCount + ColWeekDay || col == ColWeekDay + 11 || col == ColWeekDay + 22
            || (_showZO2 && BlueZO2DividerCol(col));

        private bool BlueZO2DividerCol(int col)
        {
            int zo0End = RedCount + ColWeekDay + 1 + 5;
            int zo1End = zo0End + 6;
            return col == zo0End || col == zo1End;
        }

        private void AddHeaderCell(int row, int col, string text, Brush fg, Typeface tf, double fontSize)
        {
            var border = RentTextCell();
            border.Background = BgHeader;
            border.BorderThickness = new Thickness(0.5);
            border.BorderBrush = BorderColor;
            border.Tag = null;
            if (IsThickBorderCol(col)) border.BorderThickness = new Thickness(0.5, 0.5, 2, 0.5);
            var tb = (TextBlock)border.Child;
            tb.Text = text;
            tb.FontFamily = ConsolasFont;
            tb.FontSize = fontSize;
            tb.FontWeight = FontWeights.SemiBold;
            tb.Foreground = fg;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            tb.VerticalAlignment = VerticalAlignment.Center;
            tb.Visibility = Visibility.Visible;
            SetElement(border, row, col);
            Children.Add(border);
        }

        private Border AddCell(int row, int col, string text, Brush bg, Brush fg,
            Typeface tf, double fontSize, bool isBold, string tag)
        {
            var border = RentTextCell();
            border.Background = bg;
            border.BorderThickness = new Thickness(0.5);
            border.BorderBrush = BorderColor;
            border.Tag = tag;
            if (IsThickBorderCol(col)) border.BorderThickness = new Thickness(0.5, 0.5, 2, 0.5);
            var tb = (TextBlock)border.Child;
            tb.Text = text;
            tb.FontFamily = ConsolasFont;
            tb.FontSize = fontSize;
            tb.FontWeight = isBold ? FontWeights.SemiBold : FontWeights.Normal;
            tb.Foreground = fg;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            tb.VerticalAlignment = VerticalAlignment.Center;
            tb.Visibility = Visibility.Visible;
            border.Child = tb;
            SetElement(border, row, col);
            Children.Add(border);

            // ⚡ 缓存 miss 列和 pred 列 Border，避免 UpdateDisplay 全量遍历
            if (tag.StartsWith(TAG_PREFIX_MISS))
                _missCells.Add(border);
            else if (tag.StartsWith(TAG_PREFIX_PRED))
                _predCells.Add(border);

            return border;
        }

        private Border AddBallCell(int row, int col, int number, string ballType,
            BallPattern pattern = BallPattern.None)
        {
            bool isRed = ballType == "red";
            var fill = pattern switch
            {
                BallPattern.Repeat => RepeatFill,
                BallPattern.Diagonal => DiagonalFill,
                BallPattern.Consecutive => ConsecutiveFill,
                _ => isRed ? RedBallFill : BlueBallFill
            };
            var glow = pattern == BallPattern.None ? (isRed ? RedGlow : BlueGlow) : fill;

            var border = new Border
            {
                Background = isRed ? RedBg : BlueBg,
                BorderThickness = new Thickness(0.5),
                BorderBrush = BorderColor,
                Tag = TAG_PREFIX_BALL + ballType + ":" + number
            };
            if (IsThickBorderCol(col)) border.BorderThickness = new Thickness(0.5, 0.5, 2, 0.5);

            var panel = new Grid();
            var ellipse = new Ellipse { Width = 17, Height = 17, Fill = fill, Stroke = glow, StrokeThickness = 1 };
            var tb = new TextBlock
            {
                Text = number.ToString("D2"),
                FontFamily = ConsolasFont,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(ellipse);
            panel.Children.Add(tb);
            border.Child = panel;
            SetElement(border, row, col);
            Children.Add(border);
            return border;
        }

        private Border AddColdCell(int row, int col, int number)
        {
            var border = new Border { Background = BgPredRow, BorderThickness = new Thickness(0.5), BorderBrush = BorderColor, Tag = TAG_PREFIX_PRED + number };
            if (IsThickBorderCol(col)) border.BorderThickness = new Thickness(0.5, 0.5, 2, 0.5);
            border.Child = BuildColdCircle(number);
            SetElement(border, row, col);
            Children.Add(border);
            _predCells.Add(border); // ⚡ 缓存 pred 列
            return border;
        }

        private static void SetElement(UIElement el, int row, int col)
        {
            Grid.SetRow(el, row);
            Grid.SetColumn(el, col);
        }

        private static new int GetRow(UIElement element)
        {
            try { return Grid.GetRow(element); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MatrixGrid.GetRow/GetColumn] Grid 操作异常: {ex.Message}");
                return -1;
            }
        }

        private static new int GetColumn(UIElement element)
        {
            try { return Grid.GetColumn(element); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MatrixGrid.GetRow/GetColumn] Grid 操作异常: {ex.Message}");
                return -1;
            }
        }
    }
}
