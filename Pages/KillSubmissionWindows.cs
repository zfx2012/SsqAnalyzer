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
        window.Title = title; window.Width = 1100; window.Height = 760;
        window.MinWidth = 760; window.MinHeight = 480;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.SetResourceReference(Control.BackgroundProperty, "BgContent");
        window.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
    }
}

public sealed class KillReportWindow : Window
{
    public KillReportWindow(KillReport report, IDataService data, KillSubmissionStore store, KillReviewCoordinator coordinator)
    {
        KillReportUi.Setup(this, $"第 {report.TargetPeriod} 期杀号报告");
        var root = new DockPanel { Margin = new Thickness(16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
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
        DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);
        root.Children.Add(KillReportUi.Text(report.ToMarkdown())); Content = root;
    }
}

public sealed class KillSubmissionHistoryWindow : Window
{
    private readonly IDataService _data;
    private readonly KillSubmissionStore _store;
    private readonly KillReviewCoordinator _coordinator;
    private readonly DataGrid _list = new() { IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single,
        CanUserAddRows = false, CanUserDeleteRows = false, MinHeight = 100 };
    private readonly TextBox _review = KillReportUi.Text();
    private readonly TextBox _original = KillReportUi.Text();
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
        _list.RowHeight = 32; _list.ColumnHeaderHeight = 34; _list.FontSize = 13;
        _list.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _list.HeadersVisibility = DataGridHeadersVisibility.Column;
        _list.SetResourceReference(Control.BackgroundProperty, "BgSurface");
        KillReportUi.Setup(this, "杀号提交记录 / 错误报告");
        var root = new DockPanel { Margin = new Thickness(16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _refresh = KillReportUi.Button("刷新复盘", async (_, _) => await Refresh(false));
        _update = KillReportUi.Button("更新开奖并复盘", async (_, _) => await Refresh(true));
        actions.Children.Add(_refresh); actions.Children.Add(_update);
        actions.Children.Add(KillReportUi.Button("导出当前报告", (_, _) => Export()));
        DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        foreach (var (header, path, width) in new[] { ("期号", "Period", 110), ("提交时间（北京时间）", "Submitted", 220), ("复盘状态", "State", 200), ("错杀红球数", "RedErrors", 110), ("错杀蓝球数", "BlueErrors", 110) })
            _list.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width });
        _list.SelectionChanged += (_, _) => Select(); grid.Children.Add(_list);
        var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(splitter, 1); grid.Children.Add(splitter);
        var tabs = new TabControl();
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
        catch (Exception ex) { _list.ItemsSource = null; _review.Clear(); _original.Clear(); _status.Text = $"读取记录失败：{ex.Message}"; }
    }
    private void Select()
    {
        if (_list.SelectedItem is not Row row) { _review.Clear(); _original.Clear(); return; }
        var warning = _coordinator.Issues.TryGetValue(row.Period, out var issue) ? $"数据核对提示：{issue}\n\n" : "";
        _review.Text = warning + KillSubmissionStore.ReviewText(row.Entry);
        _original.Text = row.Entry.Submission.OriginalReport;
        _review.ScrollToHome(); _original.ScrollToHome();
    }
    private void Export()
    {
        if (_list.SelectedItem is not Row row) { _status.Text = "请先选择一条提交记录。"; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"杀号复盘-{row.Period}.txt", Filter = "文本报告 (*.txt)|*.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, _review.Text + "\n\n" + _original.Text); _status.Text = "报告已导出。"; }
        catch (Exception ex) { _status.Text = $"导出失败：{ex.Message}"; }
    }
}
