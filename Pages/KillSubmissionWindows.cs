using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

namespace SsqAnalyzer.Pages;

internal static class KillReportUi
{
    internal static TextBox Text(string value = "") => new()
    {
        Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 14,
        Padding = new Thickness(16), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    internal static Button Button(string label, RoutedEventHandler handler)
    {
        var button = new Button { Content = label, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ActionButton");
        button.Click += handler;
        return button;
    }
    internal static void Setup(Window window, string title)
    {
        window.Title = title; window.Width = 1100; window.Height = 860;
        window.MinWidth = 760; window.MinHeight = 480;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.SetResourceReference(Control.BackgroundProperty, "BgContent");
        window.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        window.FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        window.UseLayoutRounding = true;
    }
}

public sealed class KillReportWindow : Window
{
    public KillReportWindow(KillReport report, IDataService data, KillSubmissionStore store, KillReviewCoordinator coordinator)
    {
        KillReportUi.Setup(this, $"第 {report.TargetPeriod} 期杀号报告");
        var root = new DockPanel { Margin = new Thickness(24) };
        var header = KillReportPresentation.Header($"第 {report.TargetPeriod} 期杀号报告",
            $"预计开奖 {report.TargetDate:yyyy-MM-dd}   ·   历史截至 {report.SourceThroughPeriod} 期   ·   生成于 {KillDrawSchedule.ChinaTime(report.GeneratedAt):MM-dd HH:mm}（北京时间）");
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), FontSize = 13,
            Text = $"提交后每期保留一份原始记录，开奖后自动复盘。预计 {report.TargetDate:yyyy-MM-dd} 开奖，本程序提交截止：当日北京时间 21:00。" };
        Button submit = null!;
        submit = KillReportUi.Button("提交本期报告", async (_, _) =>
        {
            submit.IsEnabled = false;
            try
            {
                bool created = store.Submit(report, data.GetAllRecords());
                status.Text = created ? "已提交并保存。请在“提交记录 / 错误报告”查看开奖后的复盘。" : "该期已有提交记录，保留首次结果，未重复提交或覆盖。";
                submit.Content = "该期已提交";
                await coordinator.RefreshAsync();
            }
            catch (Exception ex) { status.Text = $"提交失败：{ex.Message}"; submit.IsEnabled = true; }
        });
        submit.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        try
        {
            if (store.Read().Any(e => e.Submission.TargetPeriod == report.TargetPeriod))
            {
                submit.IsEnabled = false; submit.Content = "该期已提交";
                status.Text = "该期已有提交记录，保留首次结果。当前生成的报告不会替换原记录，可在提交记录中查看原报告。";
            }
        }
        catch (Exception ex) { status.Text = $"读取提交状态失败：{ex.Message}"; }
        actions.Children.Add(submit);
        actions.Children.Add(KillReportUi.Button("提交记录 / 错误报告", (_, _) =>
            new KillSubmissionHistoryWindow(data, store, coordinator) { Owner = this }.ShowDialog()));
        actions.Children.Add(KillReportUi.Button("关闭", (_, _) => Close()));
        DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        var notice = KillReportPresentation.Card(status); notice.Padding = new Thickness(14, 8, 14, 0);
        DockPanel.SetDock(notice, Dock.Top); root.Children.Add(notice);
        root.Children.Add(KillReportPresentation.Scroll(KillReportPresentation.Report(report))); Content = root;
    }
}

public sealed class KillSubmissionHistoryWindow : Window
{
    private readonly IDataService _data;
    private readonly KillSubmissionStore _store;
    private readonly KillReviewCoordinator _coordinator;
    private readonly DataGrid _list = new() { IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single,
        CanUserAddRows = false, CanUserDeleteRows = false, MinHeight = 100 };
    private readonly ScrollViewer _review = KillReportPresentation.Scroll(KillReportPresentation.Notice("选择一条记录，查看开奖后的复盘结果。"));
    private readonly ScrollViewer _original = KillReportPresentation.Scroll(KillReportPresentation.Notice("选择一条记录，查看提交时的原始报告。"));
    private string _reviewText = "";
    private string _originalText = "";
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
    private readonly Button _refresh;
    private readonly Button _update;
    private bool _closed;
    private sealed record Row(KillSubmissionEntry Entry)
    {
        public int Period => Entry.Submission.TargetPeriod;
        public string Submitted => KillDrawSchedule.ChinaTime(Entry.Submission.SubmittedAtUtc).ToString("yyyy-MM-dd HH:mm:ss");
        public string State => KillSubmissionStore.Status(Entry);
        public string RedErrors => Entry.Review is null ? "—" : KillSubmissionStore.WrongBalls(Entry, BallType.Red).Length.ToString();
        public string BlueErrors => Entry.Review is null ? "—" : KillSubmissionStore.WrongBalls(Entry, BallType.Blue).Length.ToString();
    }
    public KillSubmissionHistoryWindow(IDataService data, KillSubmissionStore store, KillReviewCoordinator coordinator)
    {
        _data = data; _store = store; _coordinator = coordinator;
        _list.MinRowHeight = 38; _list.ColumnHeaderHeight = 38; _list.FontSize = 13;
        _list.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _list.HeadersVisibility = DataGridHeadersVisibility.Column;
        _list.SetResourceReference(Control.BackgroundProperty, "BgSurface");
        _list.BorderThickness = new Thickness(0);
        _list.HorizontalGridLinesBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 237, 244));
        _list.AlternatingRowBackground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 253));
        _list.RowHeaderWidth = 0;
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        var selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 243, 255))));
        selectedCell.Setters.Add(new Setter(Control.ForegroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 50, 71))));
        cellStyle.Triggers.Add(selectedCell); _list.CellStyle = cellStyle;
        var columnStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        columnStyle.Setters.Add(new Setter(Control.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 247, 251))));
        columnStyle.Setters.Add(new Setter(Control.ForegroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 117, 138))));
        columnStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        columnStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        _list.ColumnHeaderStyle = columnStyle;
        KillReportUi.Setup(this, "杀号提交记录 / 错误报告");
        var root = new DockPanel { Margin = new Thickness(24) };
        var header = KillReportPresentation.Header("提交记录与开奖复盘", "每一次提交都有记录，每一个错杀都可追溯。");
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var actions = new WrapPanel();
        _refresh = KillReportUi.Button("刷新复盘", async (_, _) => await Refresh(false));
        _update = KillReportUi.Button("更新开奖并复盘", async (_, _) => await Refresh(true));
        actions.Children.Add(_refresh); actions.Children.Add(_update);
        actions.Children.Add(KillReportUi.Button("导出当前报告", (_, _) => Export()));
        DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(135), MinHeight = 85 });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        foreach (var (columnTitle, path, width) in new[] { ("期号", "Period", 1d), ("提交时间（北京时间）", "Submitted", 2d), ("复盘状态", "State", 1.6d), ("错杀红球", "RedErrors", 1d), ("错杀蓝球", "BlueErrors", 1d) })
        {
            var textStyle = new Style(typeof(TextBlock));
            textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 6, 8, 6)));
            _list.Columns.Add(new DataGridTextColumn { Header = columnTitle, Binding = new Binding(path), ElementStyle = textStyle, Width = new DataGridLength(width, DataGridLengthUnitType.Star) });
        }
        _list.SelectionChanged += (_, _) => Select();
        var listCard = KillReportPresentation.Card(_list); listCard.Padding = new Thickness(1); listCard.Margin = new Thickness(0);
        grid.Children.Add(listCard);
        var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(splitter, 1); grid.Children.Add(splitter);
        var tabs = new TabControl { Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 14, 0, 0) };
        var tabStyle = new Style(typeof(TabItem));
        tabStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 9, 16, 9)));
        tabStyle.Setters.Add(new Setter(Control.FontSizeProperty, 14d));
        tabStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        tabs.Resources.Add(typeof(TabItem), tabStyle);
        tabs.Items.Add(new TabItem { Header = "复盘 / 错误报告", Content = _review });
        tabs.Items.Add(new TabItem { Header = "提交时的原始报告", Content = _original });
        Grid.SetRow(tabs, 2); grid.Children.Add(tabs); root.Children.Add(grid); Content = root;
        coordinator.Updated += OnUpdated;
        Closed += (_, _) => { _closed = true; coordinator.Updated -= OnUpdated; };
        Loaded += async (_, _) => await Refresh(false);
    }
    private void OnUpdated()
    {
        if (!_closed && !Dispatcher.HasShutdownStarted)
            Dispatcher.InvokeAsync(() => { if (!_closed) Reload(); });
    }
    private async Task Refresh(bool update)
    {
        _refresh.IsEnabled = _update.IsEnabled = false;
        _status.Text = update ? "正在更新开奖并复盘…" : "正在复盘…";
        try
        {
            string? updateError = null;
            if (update) { await _data.TryUpdateAsync(); updateError = _data.LastErrorMessage; }
            await _coordinator.RefreshAsync();
            if (!_closed) { Reload(); if (updateError is not null) _status.Text = $"开奖更新失败：{updateError}。已使用本地数据复盘。"; }
        }
        catch (Exception ex) { if (!_closed) _status.Text = ex.Message; }
        finally { if (!_closed) _refresh.IsEnabled = _update.IsEnabled = true; }
    }
    private void Reload()
    {
        try
        {
            var selected = (_list.SelectedItem as Row)?.Period;
            var rows = _store.Read().Select(e => new Row(e)).ToArray();
            _list.ItemsSource = rows;
            _list.SelectedItem = rows.FirstOrDefault(r => r.Period == selected) ?? rows.FirstOrDefault();
            int pending = rows.Count(r => r.Entry.Review is null);
            _status.Text = _coordinator.LastError ?? (rows.Length == 0 ? "暂无提交记录。执行杀号后，在报告中点击“提交本期报告”。"
                : $"共 {rows.Length} 期，待复盘 {pending} 期，有错杀 {rows.Count(r => r.State == "有错杀")} 期。启动程序或更新开奖数据时自动复盘。");
            if (_coordinator.Issues.Count > 0) _status.Text += $"  {_coordinator.Issues.Count} 期数据待核对，请查看详情。";
            Select();
        }
        catch (Exception ex) { _list.ItemsSource = null; ClearDetails(); _status.Text = $"读取记录失败：{ex.Message}"; }
    }
    private void Select()
    {
        if (_list.SelectedItem is not Row row) { ClearDetails(); return; }
        var warning = _coordinator.Issues.TryGetValue(row.Period, out var issue) ? $"数据核对提示：{issue}\n\n" : "";
        _reviewText = warning + KillSubmissionStore.ReviewText(row.Entry);
        _originalText = row.Entry.Submission.OriginalReport;
        _review.Content = KillReportPresentation.Review(row.Entry, warning);
        _original.Content = KillReportPresentation.Original(row.Entry.Submission);
        _review.ScrollToHome(); _original.ScrollToHome();
    }
    private void ClearDetails()
    {
        _reviewText = _originalText = "";
        _review.Content = KillReportPresentation.Notice("暂无选中记录。提交本期报告后，可在这里查看复盘。");
        _original.Content = KillReportPresentation.Notice("暂无选中记录。");
    }
    private void Export()
    {
        if (_list.SelectedItem is not Row row) { _status.Text = "请先选择一条提交记录。"; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"杀号复盘-{row.Period}.txt", Filter = "文本报告 (*.txt)|*.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, _reviewText + "\n\n" + _originalText); _status.Text = "报告已导出。"; }
        catch (Exception ex) { _status.Text = $"导出失败：{ex.Message}"; }
    }
}
