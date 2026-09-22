using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SsqAnalyzer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages
{
    public partial class TicketsPage : UserControl
    {
    private readonly ITicketStore _store;
    private int _toastVersion;
        private int RedMax => _store.RedMax;
        private int BlueMax => _store.BlueMax;
        private const int HeaderRowCount = 1;

        private static readonly Brush BgHdr = UiColors.BgHeader;
        private static readonly Brush BgR0 = UiColors.BgRow0;
        private static readonly Brush BgR1 = UiColors.BgRow1;
        private static readonly Brush BgDiv = UiColors.BgDivider;
        private static readonly Brush Bdr = UiColors.Border;
        private static readonly Brush Red = UiColors.Red;
        private static readonly Brush Blu = UiColors.Blue;
        private static readonly Brush T2 = UiColors.TextSec;
        private static readonly Brush T3 = UiColors.TextTri;
        private static readonly Brush SBg = UiColors.StatsBg;

        public TicketsPage() : this(App.Services.GetRequiredService<ITicketStore>()) { }

        public TicketsPage(ITicketStore store)
        {
            _store = store;
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (_subscribed) return;
            _subscribed = true;
            _loadVersion++;
            CheckAndLoad();
            _store.TicketsChanged += OnTicketsChanged;
            _store.PeriodChanged += OnPeriodChanged;
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _subscribed = false;
            _loadVersion++;
            _toastVersion++;
            _store.TicketsChanged -= OnTicketsChanged;
            _store.PeriodChanged -= OnPeriodChanged;
        }

        private bool _subscribed;
        private int _loadVersion;
        private void OnTicketsChanged() => PostWhileLoaded(() => { RebuildTable(); UpdatePeriod(); });
        private void OnPeriodChanged() => PostWhileLoaded(UpdatePeriod);
        private void PostWhileLoaded(Action action)
        {
            int version = _loadVersion;
            Dispatcher.InvokeAsync(() =>
            {
                if (_subscribed && version == _loadVersion) action();
            });
        }

        private void OpenCompoundData_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.NavigateTo("compound");
        }

        /// <summary>缓存为空时自动加载最新数据</summary>
        public void CheckAndLoad()
        {
            if (_store.Tickets.Count == 0)
                _store.LoadLatestData();
            RebuildTable();
            UpdatePeriod();
        }

        private void LotteryType_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                var type = tag == "DLT" ? LotteryType.DLT : LotteryType.SSQ;
                _store.CurrentType = type;
                _store.LoadLatestData();
                BtnSSQ.Style = (Style)FindResource(type == LotteryType.SSQ ? "FilterTabActive" : "FilterTab");
                BtnDLT.Style = (Style)FindResource(type == LotteryType.DLT ? "FilterTabActive" : "FilterTab");
            }
        }

        private void UpdatePeriod()
        {
            PeriodLabel.Text = _store.Period.HasValue
                ? $"期数：第 {_store.Period} 期"
                : "期数：--";
        }

        private void RebuildTable()
        {
            TablePanel.Children.Clear();
            if (_store.Tickets.Count == 0) return;

            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            ApplyTableColumns(grid);
            for (int i = 0; i < HeaderRowCount + _store.Tickets.Count + 2; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            BuildHeader(grid);
            BuildTicketRows(grid, HeaderRowCount, _store.Tickets.Count - 1);
            BuildDivider(grid);
            BuildStatsRow(grid);
            TablePanel.Children.Add(grid);
        }

        private int sepCol => RedMax + 1;
        private int delCol => sepCol + BlueMax + 1;

        private void BuildHeader(Grid grid)
        {
            int rowIdx = 0;
            var hi = MkHdrCell("#", T2); Grid.SetRow(hi, rowIdx); Grid.SetColumn(hi, 0); grid.Children.Add(hi);
            for (int n = 1; n <= RedMax; n++) { var c = MkHdrCell(n.ToString("D2"), Red); Grid.SetRow(c, rowIdx); Grid.SetColumn(c, n); grid.Children.Add(c); }
            var sep = new Border { Background = BgDiv, BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Height = 32 };
            Grid.SetRow(sep, rowIdx); Grid.SetColumn(sep, sepCol); grid.Children.Add(sep);
            for (int n = 1; n <= BlueMax; n++) { var c = MkHdrCell(n.ToString("D2"), Blu); Grid.SetRow(c, rowIdx); Grid.SetColumn(c, sepCol + n); grid.Children.Add(c); }
            var dh = new Border { Background = BgHdr, BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Child = new TextBlock { Text = "操作", FontSize = 12, FontFamily = new FontFamily("Consolas"), Foreground = T2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Grid.SetRow(dh, rowIdx); Grid.SetColumn(dh, delCol); grid.Children.Add(dh);
        }

        private void BuildTicketRows(Grid grid, int startRow, int endIdx)
        {
            var tickets = _store.Tickets;
            for (int i = 0; i <= endIdx && i < tickets.Count; i++)
            {
                int rowIdx = startRow + i;
                if (rowIdx >= grid.RowDefinitions.Count)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var t = tickets[i];
                var bg = i % 2 == 0 ? BgR0 : BgR1;
                var rs = new HashSet<int>(t.Reds); var bs = new HashSet<int>(t.Blues);
                var ic = new Border { Background = bg, BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 12, FontFamily = new FontFamily("Consolas"), Foreground = T2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                Grid.SetRow(ic, rowIdx); Grid.SetColumn(ic, 0); grid.Children.Add(ic);
                for (int n = 1; n <= RedMax; n++) { var c = MakeBallCell(rs.Contains(n), n.ToString("D2"), bg, Red, rs.Contains(n)); Grid.SetRow(c, rowIdx); Grid.SetColumn(c, n); grid.Children.Add(c); }
                var sp = new Border { Background = BgDiv, BorderThickness = new Thickness(0.5), BorderBrush = Bdr };
                Grid.SetRow(sp, rowIdx); Grid.SetColumn(sp, sepCol); grid.Children.Add(sp);
                for (int n = 1; n <= BlueMax; n++) { var c = MakeBallCell(bs.Contains(n), n.ToString("D2"), bg, Blu, bs.Contains(n)); Grid.SetRow(c, rowIdx); Grid.SetColumn(c, sepCol + n); grid.Children.Add(c); }
                int id = i;
                var db = new Button { Content = "✕", FontSize = 12, Foreground = T3, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = "删除", Width = 30, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                db.Click += (_, _) => { _store.RemoveTicketAt(id); };
                var dw = new Border { Background = bg, BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Child = db };
                Grid.SetRow(dw, rowIdx); Grid.SetColumn(dw, delCol); grid.Children.Add(dw);
            }
        }

        private void BuildDivider(Grid grid)
        {
            int rowIdx = HeaderRowCount + _store.Tickets.Count;
            if (rowIdx >= grid.RowDefinitions.Count)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var dividerRow = new Border { Background = new SolidColorBrush(Colors.Black) { Opacity = 0.15 }, Height = 3, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetRow(dividerRow, rowIdx); Grid.SetColumn(dividerRow, 0); Grid.SetColumnSpan(dividerRow, delCol + 1); grid.Children.Add(dividerRow);
        }

        private void BuildStatsRow(Grid grid)
        {
            var tickets = _store.Tickets;
            int rowIdx = HeaderRowCount + tickets.Count + 1;
            if (rowIdx >= grid.RowDefinitions.Count)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var rf = new int[RedMax + 1]; var bf = new int[BlueMax + 1];
            foreach (var t in tickets) { foreach (var r in t.Reds) if (r <= RedMax) rf[r]++; foreach (var b in t.Blues) if (b <= BlueMax) bf[b]++; }
            int mr = rf.Max(), mb = bf.Max();
            double avgR = rf.Where(x => x > 0).DefaultIfEmpty(1).Average();
            double avgB = bf.Where(x => x > 0).DefaultIfEmpty(1).Average();
            var sic = new Border { Background = BgDiv, BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Child = new TextBlock { Text = "统", FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), Foreground = T2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, Height = 34 };
            Grid.SetRow(sic, rowIdx); Grid.SetColumn(sic, 0); grid.Children.Add(sic);
            for (int n = 1; n <= RedMax; n++)
            {
                int f = rf[n];
                bool isMax = f == mr && f > 0;
                var cell = new Border { BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Height = 34, Width = 28, CornerRadius = new CornerRadius(6), Background = StatsCellBg(f, avgR, Red), Margin = new Thickness(0, 1, 0, 1) };
                cell.Child = new TextBlock { Text = f.ToString(), FontSize = 12, FontWeight = isMax ? FontWeights.Bold : FontWeights.Normal, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(cell, rowIdx); Grid.SetColumn(cell, n); grid.Children.Add(cell);
            }
            var ssep = new Border { Background = BgDiv, BorderThickness = new Thickness(0.5), BorderBrush = Bdr };
            Grid.SetRow(ssep, rowIdx); Grid.SetColumn(ssep, sepCol); grid.Children.Add(ssep);
            for (int n = 1; n <= BlueMax; n++)
            {
                int f = bf[n];
                bool isMax = f == mb && f > 0;
                var cell = new Border { BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Height = 34, Width = 28, CornerRadius = new CornerRadius(6), Background = StatsCellBg(f, avgB, Blu), Margin = new Thickness(0, 1, 0, 1) };
                cell.Child = new TextBlock { Text = f.ToString(), FontSize = 12, FontWeight = isMax ? FontWeights.Bold : FontWeights.Normal, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(cell, rowIdx); Grid.SetColumn(cell, sepCol + n); grid.Children.Add(cell);
            }
            var sdbg = new Border { Background = BgDiv, BorderThickness = new Thickness(0.5), BorderBrush = Bdr };
            Grid.SetRow(sdbg, rowIdx); Grid.SetColumn(sdbg, delCol); grid.Children.Add(sdbg);
        }

        private void ApplyTableColumns(Grid grid)
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            for (int n = 1; n <= RedMax; n++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            for (int n = 1; n <= BlueMax; n++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        }

        private Border MkHdrCell(string text, Brush fg)
        {
            return new Border
            {
                Background = BgHdr, BorderThickness = new Thickness(0.5), BorderBrush = Bdr,
                Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        }

        private Border MakeBallCell(bool filled, string text, Brush bg, Brush? ballColor = null, bool bold = false)
        {
            var cell = new Border { Background = bg, BorderThickness = new Thickness(0.5), BorderBrush = Bdr, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            if (filled)
            {
                var vb = new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(1), StretchDirection = StretchDirection.DownOnly };
                var ball = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = ballColor ?? Red, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                vb.Child = ball; cell.Child = vb;
            }
            else cell.Child = new TextBlock { Text = text, FontSize = bold ? 10 : 9, FontFamily = new FontFamily("Consolas"), Foreground = bold ? T2 : T3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return cell;
        }

        /// <summary>统计行背景色：0=亮红，随频率增大渐变至深红，>=平均=白色</summary>
        private static Brush StatsCellBg(int freq, double avg, Brush fallback)
        {
            if (freq >= avg) return Brushes.White;
            double ratio = freq / Math.Max(avg, 1); // 0 → 亮红(255,50,50) → 近avg → 深红(120,0,0)
            int r = 255 - (int)(ratio * 135);
            int gb = 50 - (int)(ratio * 50);
            return new SolidColorBrush(Color.FromRgb((byte)r, (byte)gb, (byte)gb));
        }

        private static string BuildStatCell(int freq, int maxFreq, double avg)
        {
            string bold = freq == maxFreq && freq > 0 ? "font-weight:700;" : "";
            if (freq >= avg) return $"<td style=\"{bold}background:#fff\">{freq}</td>";
            double ratio = freq / Math.Max(avg, 1);
            int r = 255 - (int)(ratio * 135);
            int gb = 50 - (int)(ratio * 50);
            return $"<td style=\"{bold}background:rgb({r},{gb},{gb});color:#000\">{freq}</td>";
        }

        private void ImportData_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DataFilePicker();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedFile))
            {
                // 校验数据类型是否匹配
                var (hasError, rangeMsg) = _store.ValidateTicketRange(dialog.SelectedFile);
                if (hasError)
                {
                    ShowExportToast(rangeMsg!);
                    return;
                }
                if (_store.LoadFromFile(dialog.SelectedFile))
                    ShowExportToast($"✅ 已加载: {System.IO.Path.GetFileName(dialog.SelectedFile)}");
                else
                    ShowExportToast("❌ 文件加载失败");
            }
        }

        private async void ShowExportToast(string msg)
        {
            var version = ++_toastVersion;
            ExportStatus.Text = msg;
            await System.Threading.Tasks.Task.Delay(3000);
            if (version == _toastVersion)
                ExportStatus.Text = "";
        }

        private void ExportHtml_Click(object sender, RoutedEventArgs e)
        {
            if (_store.Tickets.Count == 0) return;

            var expType = _store.CurrentType == LotteryType.DLT ? "大乐透" : "双色球";
            var dlg = new SaveFileDialog
            {
                Title = "导出票行HTML",
                Filter = "HTML文件|*.html",
                DefaultExt = "html",
                FileName = $"复式票统计_{expType}_{_store.Period}.html"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                var periodStr = _store.Period.HasValue ? $"第 {_store.Period} 期" : "未知期数";
                var typeStr = _store.CurrentType == LotteryType.DLT ? "大乐透" : "双色球";
                sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"UTF-8\"><title>复式票统计</title>");
                sb.AppendLine("<style>body{font-family:'Microsoft YaHei',sans-serif;background:#f5f5f5;padding:20px}");
                sb.AppendLine("h2{color:#333}table{border-collapse:collapse;margin-top:8px;background:#fff;box-shadow:0 1px 4px rgba(0,0,0,.1)}");
                sb.AppendLine("th,td{border:1px solid #ddd;padding:4px 6px;text-align:center;font-size:13px}");
                sb.AppendLine("th{background:#e8e8e8;font-weight:600}.r-fill{color:#fff;background:#e04848;border-radius:50%;display:inline-block;width:22px;height:22px;line-height:22px;font-size:11px;font-weight:700}");
                sb.AppendLine(".b-fill{color:#fff;background:#3b82f6;border-radius:50%;display:inline-block;width:22px;height:22px;line-height:22px;font-size:11px;font-weight:700}");
                sb.AppendLine(".sep{background:#e0e0e0}.stat{background:#f0f8ff;font-weight:600}");
                sb.AppendLine(".period{color:#999;font-size:13px;margin-bottom:10px}</style></head><body>");
                sb.AppendLine($"<h2>🔥 复式票统计 {WebUtility.HtmlEncode(typeStr)}</h2><p class=\"period\">{WebUtility.HtmlEncode(periodStr)}</p>");

                if (_store.Tickets.Count > 0)
                {
                    sb.AppendLine("<table>");
                    // 表头
                    sb.Append("<tr><th>#</th>");
                    for (int n = 1; n <= RedMax; n++) sb.Append($"<th style=\"color:#e04848\">{n:D2}</th>");
                    sb.Append("<th class=\"sep\"></th>");
                    for (int n = 1; n <= BlueMax; n++) sb.Append($"<th style=\"color:#3b82f6\">{n:D2}</th>");
                    sb.AppendLine("</tr>");

                    // 票行
                    for (int i = 0; i < _store.Tickets.Count; i++)
                    {
                        var t = _store.Tickets[i];
                        var rs = new HashSet<int>(t.Reds);
                        var bs = new HashSet<int>(t.Blues);
                        var bg = i % 2 == 0 ? "#fafafa" : "#f5f5f5";
                        sb.Append($"<tr style=\"background:{bg}\"><td>{i + 1}</td>");
                        for (int n = 1; n <= RedMax; n++)
                            sb.Append(rs.Contains(n) ? $"<td><span class=\"r-fill\">{n:D2}</span></td>" : $"<td style=\"color:#ccc;font-size:10px\">{n:D2}</td>");
                        sb.Append("<td class=\"sep\"></td>");
                        for (int n = 1; n <= BlueMax; n++)
                            sb.Append(bs.Contains(n) ? $"<td><span class=\"b-fill\">{n:D2}</span></td>" : $"<td style=\"color:#ccc;font-size:10px\">{n:D2}</td>");
                        sb.AppendLine("</tr>");
                    }

                    // 统计行
                    var rf = new int[RedMax + 1]; var bf = new int[BlueMax + 1];
                    foreach (var t in _store.Tickets) { foreach (var r in t.Reds) if (r <= RedMax) rf[r]++; foreach (var b in t.Blues) if (b <= BlueMax) bf[b]++; }
                    int mr = rf.Max(), mb = bf.Max();
                    double avgR = rf.Where(x => x > 0).DefaultIfEmpty(1).Average();
                    double avgB = bf.Where(x => x > 0).DefaultIfEmpty(1).Average();
                    sb.Append("<tr class=\"stat\"><td>统</td>");
                    for (int n = 1; n <= RedMax; n++) { int f = rf[n]; sb.Append(BuildStatCell(f, mr, avgR)); }
                    sb.Append("<td class=\"sep\"></td>");
                    for (int n = 1; n <= BlueMax; n++) { int f = bf[n]; sb.Append(BuildStatCell(f, mb, avgB)); }
                    sb.AppendLine("</tr></table>");
                }
                else
                {
                    sb.AppendLine("<p style=\"color:#999\">暂无视据</p>");
                }

                sb.AppendLine("</body></html>");
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                ShowExportToast("✅ HTML 导出成功");
            }
            catch (Exception ex)
            {
                ShowExportToast($"❌ 导出失败: {ex.Message}");
            }
        }

        private void ExportImage_Click(object sender, RoutedEventArgs e)
        {
            if (_store.Tickets.Count == 0) return;

            var imgType = _store.CurrentType == LotteryType.DLT ? "大乐透" : "双色球";
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出表格图片",
                Filter = "PNG图片|*.png",
                DefaultExt = "png",
                FileName = $"复式票统计_{imgType}_{_store.Period}.png"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                // 重建表格到临时容器（白色背景 + 标题 + 边距）
                var outer = new Grid { Background = Brushes.White };
                var stack = new StackPanel { Margin = new Thickness(16) };
                var periodStr = _store.Period.HasValue ? $"第 {_store.Period} 期" : "";
                var typeStr = _store.CurrentType == LotteryType.DLT ? "大乐透" : "双色球";
                if (!string.IsNullOrEmpty(periodStr))
                    stack.Children.Add(new TextBlock { Text = $"🔥 复式票统计 {typeStr} {periodStr}", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), Margin = new Thickness(0, 0, 0, 10) });
                var inner = new Grid();
                ApplyTableColumns(inner);

                int totalRows = 3 + _store.Tickets.Count; // 表头 + N票行 + 分隔 + 统计
                for (int r = 0; r < totalRows; r++)
                    inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int rowIdx = 0; int sepCol = RedMax + 1; int delCol = sepCol + BlueMax + 1;

                // 表头
                var hi = MkHdrCell("#", T2); Grid.SetRow(hi, rowIdx); Grid.SetColumn(hi, 0); inner.Children.Add(hi);
                for (int n = 1; n <= RedMax; n++) { var c = MkHdrCell(n.ToString("D2"), Red); Grid.SetRow(c, rowIdx); Grid.SetColumn(c, n); inner.Children.Add(c); }
                var sep = new Border { Background = BgDiv, BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Height = 32 };
                Grid.SetRow(sep, rowIdx); Grid.SetColumn(sep, sepCol); inner.Children.Add(sep);
                for (int n = 1; n <= BlueMax; n++) { var c = MkHdrCell(n.ToString("D2"), Blu); Grid.SetRow(c, rowIdx); Grid.SetColumn(c, sepCol + n); inner.Children.Add(c); }
                var dh = new Border { Background = BgHdr, BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Child = new TextBlock { Text = "操作", FontSize = 12, FontFamily = new FontFamily("Consolas"), Foreground = T2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                Grid.SetRow(dh, rowIdx); Grid.SetColumn(dh, delCol); inner.Children.Add(dh);
                rowIdx++;

                // 票行
                for (int i = 0; i < _store.Tickets.Count; i++)
                {
                    var t = _store.Tickets[i];
                    var bg = i % 2 == 0 ? BgR0 : BgR1;
                    var rs = new HashSet<int>(t.Reds); var bs = new HashSet<int>(t.Blues);
                    var ic = new Border { Background = bg, BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 12, FontFamily = new FontFamily("Consolas"), Foreground = T2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                    Grid.SetRow(ic, rowIdx); Grid.SetColumn(ic, 0); inner.Children.Add(ic);
                    for (int n = 1; n <= RedMax; n++) { var c = MakeBallCell(rs.Contains(n), n.ToString("D2"), bg, Red, rs.Contains(n)); Grid.SetRow(c, rowIdx); Grid.SetColumn(c, n); inner.Children.Add(c); }
                    var sp = new Border { Background = BgDiv, BorderThickness = new Thickness(0.5), BorderBrush = Bdr };
                    Grid.SetRow(sp, rowIdx); Grid.SetColumn(sp, sepCol); inner.Children.Add(sp);
                    for (int n = 1; n <= BlueMax; n++) { var c = MakeBallCell(bs.Contains(n), n.ToString("D2"), bg, Blu, bs.Contains(n)); Grid.SetRow(c, rowIdx); Grid.SetColumn(c, sepCol + n); inner.Children.Add(c); }
                    var dw = new Border { Background = bg, BorderThickness = new Thickness(0.5), BorderBrush = Bdr };
                    Grid.SetRow(dw, rowIdx); Grid.SetColumn(dw, delCol); inner.Children.Add(dw);
                    rowIdx++;
                }

                // 分隔线
                var divider = new Border { Background = new SolidColorBrush(Colors.Black) { Opacity = 0.15 }, Height = 3, Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(divider, rowIdx); Grid.SetColumn(divider, 0); Grid.SetColumnSpan(divider, delCol + 1); inner.Children.Add(divider);
                rowIdx++;

                // 统计行
                var rf = new int[RedMax + 1]; var bf = new int[BlueMax + 1];
                foreach (var t in _store.Tickets) { foreach (var r in t.Reds) if (r <= RedMax) rf[r]++; foreach (var b in t.Blues) if (b <= BlueMax) bf[b]++; }
                int mr = rf.Max(), mb = bf.Max();
                double avgR = rf.Where(x => x > 0).DefaultIfEmpty(1).Average();
                double avgB = bf.Where(x => x > 0).DefaultIfEmpty(1).Average();
                var sic = new Border { Background = BgDiv, BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Child = new TextBlock { Text = "统", FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), Foreground = T2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, Height = 34 };
                Grid.SetRow(sic, rowIdx); Grid.SetColumn(sic, 0); inner.Children.Add(sic);
                for (int n = 1; n <= RedMax; n++) { int f = rf[n]; bool isMax = f == mr && f > 0; var c = new Border { BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Height = 34, Width = 28, CornerRadius = new CornerRadius(6), Background = StatsCellBg(f, avgR, Red), Margin = new Thickness(0, 1, 0, 1) }; c.Child = new TextBlock { Text = f.ToString(), FontSize = 12, FontWeight = isMax ? FontWeights.Bold : FontWeights.Normal, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(c, rowIdx); Grid.SetColumn(c, n); inner.Children.Add(c); }
                var ssep = new Border { Background = BgDiv, BorderThickness = new Thickness(0.5), BorderBrush = Bdr }; Grid.SetRow(ssep, rowIdx); Grid.SetColumn(ssep, sepCol); inner.Children.Add(ssep);
                for (int n = 1; n <= BlueMax; n++) { int f = bf[n]; bool isMax = f == mb && f > 0; var c = new Border { BorderThickness = new Thickness(0.5), BorderBrush = Bdr, Height = 34, Width = 28, CornerRadius = new CornerRadius(6), Background = StatsCellBg(f, avgB, Blu), Margin = new Thickness(0, 1, 0, 1) }; c.Child = new TextBlock { Text = f.ToString(), FontSize = 12, FontWeight = isMax ? FontWeights.Bold : FontWeights.Normal, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(c, rowIdx); Grid.SetColumn(c, sepCol + n); inner.Children.Add(c); }
                var sdbg = new Border { Background = BgDiv, BorderThickness = new Thickness(0.5), BorderBrush = Bdr }; Grid.SetRow(sdbg, rowIdx); Grid.SetColumn(sdbg, delCol); inner.Children.Add(sdbg);

                stack.Children.Add(inner);
                outer.Children.Add(stack);

                // 无限大区域测量获取真实尺寸
                var maxSize = new System.Windows.Size(20000, 20000);
                outer.Measure(maxSize);
                outer.Arrange(new Rect(0, 0, outer.DesiredSize.Width, outer.DesiredSize.Height));

                // 2倍 DPI 渲染高清图
                int dpi = 192;
                int w = (int)Math.Ceiling(outer.ActualWidth * dpi / 96.0);
                int h = (int)Math.Ceiling(outer.ActualHeight * dpi / 96.0);
                var bmp = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
                bmp.Render(outer);

                using var fs = new FileStream(dlg.FileName, FileMode.Create);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
                ShowExportToast("✅ 图片导出成功");
            }
            catch (Exception ex)
            {
                ShowExportToast($"❌ 导出失败: {ex.Message}");
            }
        }
    }
}
