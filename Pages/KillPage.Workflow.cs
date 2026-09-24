using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SsqAnalyzer.Services.Kill;

namespace SsqAnalyzer.Pages;

public partial class KillPage
{
    private KillSubmissionStore? _submissions;
    private KillReviewCoordinator? _reviews;
    private KillPagePreferenceStore? _preferenceStore;
    private KillPagePreferences _preferences = new();
    private IReadOnlyList<KillSubmissionEntry> _actualEntries = Array.Empty<KillSubmissionEntry>();
    private readonly TextBlock _overview = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(10, 8, 10, 8) };
    private readonly TextBlock _workflowMessage = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange, Margin = new Thickness(0, 5, 0, 0) };
    private readonly ComboBox _quickFilter = new() { Width = 135, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _batchScope = new() { Width = 140, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Expander _preview = new() { Header = "本期执行预览 · 尚未执行", Margin = new Thickness(0, 8, 0, 0) };
    private readonly WrapPanel _previewBalls = new();
    private readonly TextBlock _previewInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _linkedInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private Button _batchEnable = null!, _batchDisable = null!, _scopedBacktest = null!, _openReport = null!, _cancelBacktest = null!;
    private (BallType Type, int Ball)? _linkedBall;
    private bool _previewValid;
    private bool _savingPreferences;
    private string? _currentDataHash;

    private void InitializeWorkflow(KillSubmissionStore? store, KillReviewCoordinator? reviews, KillPagePreferenceStore? preferences)
    {
        _submissions = store; _reviews = reviews; _preferenceStore = preferences;
        var messageStyle = new Style(typeof(TextBlock));
        var emptyMessage = new Trigger { Property = TextBlock.TextProperty, Value = "" };
        emptyMessage.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed)); messageStyle.Triggers.Add(emptyMessage); _workflowMessage.Style = messageStyle;
        try { _preferences = preferences?.Read() ?? new(); }
        catch (Exception ex) { _workflowMessage.Text = $"读取列表偏好失败：{ex.Message}"; _preferenceStore = null; }
        _periodCount = new[] { 30, 50, 100, 0 }.Contains(_preferences.Window) ? _preferences.Window : 50;
        _sortKey = _preferences.SortKey; _sortDirection = _preferences.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        _sortClickCount = _sortKey is null ? 0 : _preferences.SortDescending ? 2 : 1;
        if (_sortKey == "IsEnabled") _enabledSortSnapshot = _ruleRepo.GetAll().ToDictionary(r => r.RuleId, r => r.IsEnabled);
        WorkflowHost.Children.Add(new Border { Background = (Brush)FindResource("AccentSoft"), CornerRadius = new CornerRadius(7), Child = _overview });
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var text in new[] { "全部规则", "已收藏", "本期触发", "本期执行异常", "待重新回测", "实际曾错杀" }) _quickFilter.Items.Add(text);
        _quickFilter.SelectedIndex = 0; _quickFilter.SelectionChanged += (_, _) => { if (_loaded) RenderRuleList(); };
        actions.Children.Add(_quickFilter);
        foreach (var text in new[] { "操作选中行", "操作筛选结果" }) _batchScope.Items.Add(text);
        _batchScope.SelectedIndex = 0; _batchScope.SelectionChanged += (_, _) => UpdateBatchLabels(); actions.Children.Add(_batchScope);
        _batchEnable = KillReportUi.Button("启用 0 条", (_, _) => SetBatchEnabled(true));
        _batchDisable = KillReportUi.Button("停用 0 条", (_, _) => SetBatchEnabled(false));
        _scopedBacktest = KillReportUi.Button("回测 0 条", async (_, _) => await RunScopedBacktest());
        actions.Children.Add(_batchEnable); actions.Children.Add(_batchDisable); actions.Children.Add(_scopedBacktest);
        actions.Children.Add(KillReportUi.Button("收藏 / 取消", (_, _) => ToggleFavorites()));
        actions.Children.Add(KillReportUi.Button("全选筛选结果", (_, _) => RulesGrid.SelectAll()));
        actions.Children.Add(KillReportUi.Button("显示列", (sender, _) => ShowColumns((Button)sender)));
        actions.Children.Add(KillReportUi.Button("保存布局", (_, _) => { if (SavePreferences()) _workflowMessage.Text = "列宽、显示列、排序、回测窗口和收藏已保存。"; }));
        _cancelBacktest = KillReportUi.Button("取消回测", (_, _) => { _backtestCts?.Cancel(); }); _cancelBacktest.IsEnabled = false;
        actions.Children.Add(_cancelBacktest);
        actions.Children.Add(new TextBlock { Text = "Ctrl / Shift 多选行；选中不改变启用状态", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextTertiary") });
        WorkflowHost.Children.Add(actions); WorkflowHost.Children.Add(_workflowMessage);
        var content = new StackPanel();
        var previewActions = new WrapPanel();
        _openReport = KillReportUi.Button("完整报告 / 提交", (_, _) => OpenCurrentReport()); previewActions.Children.Add(_openReport);
        previewActions.Children.Add(KillReportUi.Button("清除号码联动", (_, _) => { _linkedBall = null; _linkedInfo.Text = ""; RenderRuleList(); }));
        content.Children.Add(previewActions); content.Children.Add(_previewInfo); content.Children.Add(_previewBalls); content.Children.Add(_linkedInfo);
        _preview.Content = new ScrollViewer { Content = content, MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        WorkflowHost.Children.Add(_preview);
        foreach (var (name, binding, width) in new[] { ("收藏", "IsFavorite", 55d), ("本期执行", "ExecutionLabel", 130d), ("实际提交", "ActualLabel", 190d) })
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(binding == "ExecutionLabel" ? "ExecutionHint" : binding == "ActualLabel" ? "ActualHint" : "RuleId")));
            var column = new DataGridTextColumn { Header = name, Binding = new Binding(binding == "IsFavorite" ? "FavoriteLabel" : binding), SortMemberPath = binding == "ActualLabel" ? "ActualWrongCount" : binding, Width = width, ElementStyle = style };
            RulesGrid.Columns.Add(column);
            if (binding == "ExecutionLabel") column.DisplayIndex = 4;
        }
        foreach (var (key, position) in new[] { ("Name", 2), ("GateSortValue", 3), ("ExecutionLabel", 4), ("AccuracyValue", 5), ("ActualWrongCount", 6), ("IsFavorite", 7) })
            RulesGrid.Columns.Single(c => c.SortMemberPath == key).DisplayIndex = position;
        foreach (var column in RulesGrid.Columns)
        {
            var key = ColumnKey(column);
            if (column.Width.IsAbsolute) column.MinWidth = Math.Min(column.Width.Value, key is "RuleId" or "Name" or "ExecutionLabel" or "ActualWrongCount" ? 120 : 80);
            if (key == "操作") column.MinWidth = 112;
            if (key == "ActualWrongCount") column.MinWidth = 150;
            if (_preferences.ColumnWidths.TryGetValue(key, out var width) && double.IsFinite(width) && width >= 35 && width <= 1200) column.Width = width;
            if (_preferences.HiddenColumns.Contains(key) && CanHide(column)) column.Visibility = Visibility.Collapsed;
        }
        RulesGrid.SelectionChanged += (_, _) => { UpdateBatchLabels(); UpdateLinkedRule(); };
    }
    private static string ColumnKey(DataGridColumn column) => string.IsNullOrEmpty(column.SortMemberPath) ? column.Header?.ToString() ?? "" : column.SortMemberPath;
    private static bool CanHide(DataGridColumn column) => column.SortMemberPath is not ("Name" or "IsEnabled") && column.Header?.ToString() != "操作";
    private void ShowColumns(Button source)
    {
        var menu = new ContextMenu();
        foreach (var column in RulesGrid.Columns.Where(CanHide))
        {
            var item = new MenuItem { Header = column.Header, IsCheckable = true, IsChecked = column.Visibility == Visibility.Visible };
            item.Click += (_, _) => { column.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed; SavePreferences(); };
            menu.Items.Add(item);
        }
        source.ContextMenu = menu; menu.PlacementTarget = source; menu.IsOpen = true;
    }
    private bool SavePreferences()
    {
        if (_preferenceStore is null || _savingPreferences) return false;
        _savingPreferences = true;
        try
        {
            _preferences.Window = _periodCount; _preferences.SortKey = _sortKey; _preferences.SortDescending = _sortDirection == ListSortDirection.Descending;
            _preferences.ColumnWidths = RulesGrid.Columns.ToDictionary(ColumnKey, c => c.ActualWidth);
            _preferences.HiddenColumns = RulesGrid.Columns.Where(c => c.Visibility != Visibility.Visible).Select(ColumnKey).ToHashSet();
            _preferenceStore.Save(_preferences);
            return true;
        }
        catch (Exception ex) { _workflowMessage.Text = $"保存列表偏好失败：{ex.Message}"; return false; }
        finally { _savingPreferences = false; }
    }
    private void ResetWorkflowFilters() { _quickFilter.SelectedIndex = 0; _linkedBall = null; }
    private void PopulateWorkflowRow(RuleRowViewModel row, IKillRule rule, string accuracy)
    {
        row.IsFavorite = _preferences.Favorites.Contains(rule.RuleId);
        if (accuracy is "规则已变更" or "需重新回测" or "历史已更新") row.GateLabel = "结果过期";
        else if (accuracy == "未回测") row.GateLabel = "未回测";
        else if (accuracy is "无有效样本" or "样本不足") row.GateLabel = accuracy;
        // Enablement has its own checkbox; the historical gate remains a separate fact.
        else if (row.GateLabel == "强制启用") row.GateLabel = "✗不达标";
        var result = _previewValid ? _lastReport?.Results.FirstOrDefault(r => r.RuleId == rule.RuleId) : null;
        row.ExecutionLabel = result is null ? _lastReport is null ? "未执行" : _previewValid ? "未参与" : "预览已过期"
            : result.ExecutionError is not null ? "执行异常" : result.Triggered ? "杀 " + string.Join(" ", result.KilledBalls.Select(n => n.ToString("D2"))) : "未触发";
        row.ExecutionHint = result is null ? "执行杀号后可查看本期结果" : result.ExecutionError ?? result.Reason;
        var actual = KillActualPerformance.Summarize(rule, _actualEntries).FirstOrDefault(v => v.Current);
        row.ActualLabel = actual is null ? "暂无记录" : $"提交 {actual.Submitted} / 触发 {actual.Triggered} / 错杀 {actual.WrongPeriods}";
        row.ActualHint = KillActualPerformance.Text(rule, _actualEntries);
        row.ActualWrong = actual?.WrongPeriods > 0;
        row.ActualWrongCount = actual?.WrongPeriods ?? 0;
    }
    private bool WorkflowMatches(RuleRowViewModel row)
    {
        bool quick = _quickFilter.SelectedIndex switch
        {
            1 => row.IsFavorite, 2 => row.ExecutionLabel.StartsWith("杀 "), 3 => row.ExecutionLabel == "执行异常",
            4 => row.GateLabel is "结果过期" or "未回测" or "无有效样本" or "样本不足" or "执行异常",
            5 => row.ActualWrong, _ => true
        };
        return quick && (_linkedBall is not { } ball || (_previewValid && _lastReport!.Results.Any(r => r.RuleId == row.RuleId && r.BallType == ball.Type && r.KilledBalls.Contains(ball.Ball))));
    }
    private void UpdateOverview(IEnumerable<RuleRowViewModel> rows)
    {
        var latest = _data?.LastOrDefault();
        var target = latest is null ? 0 : KillDrawSchedule.NextPeriod(latest.Period, latest.DrawDate);
        var snapshot = rows.ToArray();
        _overview.Text = $"目标期号 {(target == 0 ? "—" : target.ToString())}  ·  历史截至 {latest?.Period.ToString() ?? "无"}  ·  启用 {_ruleRepo.GetEnabled().Count} 条  ·  当前窗口达标 {snapshot.Count(r => r.GateSortValue == 0)} 条  ·  待回测/核对 {snapshot.Count(r => r.GateSortValue is 2 or 3 or 5)} 条  ·  {(_actualEntries.Any(e => e.Submission.TargetPeriod == target) ? "本期已提交，可打开提交记录" : "本期未提交")}";
    }
    private void ReadActualEntries()
    {
        if (_submissions is null) return;
        try { _actualEntries = _submissions.Read(); }
        catch (Exception ex) { _actualEntries = Array.Empty<KillSubmissionEntry>(); _workflowMessage.Text = $"实际记录读取失败：{ex.Message}"; }
        if (_reviews?.LastError is { } error) _workflowMessage.Text = error;
        else if (_reviews?.Issues.Count > 0) _workflowMessage.Text = $"有 {_reviews.Issues.Count} 期实际记录需要核对，请打开提交记录查看提示。";
    }
    private void OnReviewUpdated() => PostWhileLoaded(() => { ReadActualEntries(); RenderRuleList(); });
    private void OpenSubmissionHistory(int? period)
    {
        if (_submissions is null || _reviews is null) return;
        new KillSubmissionHistoryWindow(_ds, _submissions, _reviews, period) { Owner = Window.GetWindow(this) }.ShowDialog();
        ReadActualEntries(); RenderRuleList();
    }
    private void OpenCurrentReport()
    {
        if (!_previewValid || _lastReport is null || _submissions is null || _reviews is null) return;
        new KillReportWindow(_lastReport, _ds, _submissions, _reviews) { Owner = Window.GetWindow(this) }.ShowDialog();
        ReadActualEntries(); RenderRuleList();
    }
    private void InvalidatePreview(string reason)
    {
        if (_lastReport is null || !_previewValid) return;
        _previewValid = false; _linkedBall = null; _preview.Header = "本期预览已过期 · 请重新执行";
        _previewInfo.Text = reason; _previewBalls.Children.Clear(); _linkedInfo.Text = ""; _openReport.IsEnabled = false;
    }
    private void CheckPreviewRules()
    {
        if (_previewValid && _lastReport is not null && (_lastReport.SourceDataHash != _currentDataHash || _lastReport.EvaluationWindow != SelectedWindow))
            InvalidatePreview("历史数据或评价窗口已变化，请重新执行杀号。");
        if (_previewValid && _lastReport is not null && KillRuleDefinition.RulesHash(_ruleRepo.GetEnabled().Select(KillRuleDefinition.Capture)) != KillRuleDefinition.RulesHash(_lastReport.RuleDefinitions))
            InvalidatePreview("启用规则或规则条件已变化，请重新执行杀号。");
    }
    private void ShowExecutionPreview()
    {
        var report = _lastReport!; _previewValid = true; _linkedBall = null;
        _preview.Header = $"第 {report.TargetPeriod} 期执行预览 · 点击号码查看关联规则"; _preview.IsExpanded = true; _openReport.IsEnabled = true;
        _previewInfo.Text = $"合并排除红球 {report.KilledRedBalls.Count} 个、蓝球 {report.KilledBlueBalls.Count} 个；保留红球 {report.RecommendedRedBalls.Count} 个、蓝球 {report.RecommendedBlueBalls.Count} 个。\n" + KillSubmissionChecks.Describe(report, _ds.GetAllRecords());
        _previewBalls.Children.Clear();
        foreach (var detail in report.KilledRedBalls.Concat(report.KilledBlueBalls))
        {
            var button = KillReportUi.Button($"{(detail.BallType == BallType.Red ? "红" : "蓝")} {detail.Ball:D2} · {detail.Traces.Count} 条", (_, _) =>
            {
                _linkedBall = (detail.BallType, detail.Ball);
                _quickFilter.SelectedIndex = 0;
                _linkedInfo.Text = $"当前联动：{(detail.BallType == BallType.Red ? "红球" : "蓝球")} {detail.Ball:D2}；关联规则：{string.Join("、", detail.Traces.Select(t => t.RuleName))}。仍叠加搜索和球种筛选。";
                RenderRuleList();
            });
            button.Foreground = detail.BallType == BallType.Red ? Brushes.Firebrick : Brushes.RoyalBlue;
            button.Tag = detail; _previewBalls.Children.Add(button);
        }
    }
    private void UpdateLinkedRule()
    {
        var selected = RulesGrid.SelectedItem as RuleRowViewModel;
        foreach (var button in _previewBalls.Children.OfType<Button>())
            if (button.Tag is KilledBallDetail detail)
                button.Background = selected is not null && detail.Traces.Any(t => t.RuleId == selected.RuleId) ? (Brush)FindResource("AccentSoft") : (Brush)FindResource("BgSurface");
        if (_linkedBall is null && selected is not null && _previewValid) _linkedInfo.Text = $"{selected.Name}：{selected.ExecutionLabel}。{selected.ExecutionHint}";
    }
    private List<IKillRule> BatchRules() => (_batchScope.SelectedIndex == 1 ? RulesGrid.Items.Cast<RuleRowViewModel>() : RulesGrid.SelectedItems.Cast<RuleRowViewModel>())
        .Select(r => _ruleRepo.Find(r.RuleId)).OfType<IKillRule>().ToList();
    private void UpdateBatchLabels()
    {
        if (_batchEnable is null) return;
        int count = BatchRules().Count; string scope = _batchScope.SelectedIndex == 1 ? "筛选" : "选中";
        _batchEnable.Content = $"启用{scope} {count} 条"; _batchDisable.Content = $"停用{scope} {count} 条"; _scopedBacktest.Content = $"回测{scope} {count} 条";
        _batchEnable.IsEnabled = _batchDisable.IsEnabled = _scopedBacktest.IsEnabled = count > 0 && !_isBacktesting;
        if (_cancelBacktest is not null) _cancelBacktest.IsEnabled = _isBacktesting;
    }
    private void SetBatchEnabled(bool enabled)
    {
        if (_isBacktesting) return;
        var rules = BatchRules().OfType<KillRule>().ToArray(); int saved = 0;
        foreach (var rule in rules)
        {
            if (rule.IsEnabled == enabled) continue;
            bool previous = rule.IsEnabled;
            try { rule.IsEnabled = enabled; _ruleRepo.Update(rule); saved++; }
            catch (Exception ex) { rule.IsEnabled = previous; _workflowMessage.Text = $"已保存 {saved} 条；{rule.Name} 保存失败：{ex.Message}。其余未处理。"; RenderRuleList(); return; }
        }
        _workflowMessage.Text = $"已{(enabled ? "启用" : "停用")} {saved} 条规则。"; RenderRuleList();
    }
    private void ToggleFavorites()
    {
        var rules = BatchRules(); if (rules.Count == 0) { _workflowMessage.Text = "请先选中规则，或选择操作筛选结果。"; return; }
        bool remove = rules.All(r => _preferences.Favorites.Contains(r.RuleId));
        foreach (var rule in rules) { if (remove) _preferences.Favorites.Remove(rule.RuleId); else _preferences.Favorites.Add(rule.RuleId); }
        SavePreferences(); RenderRuleList();
    }
    private async Task RunScopedBacktest()
    {
        if (_isBacktesting) return;
        var rules = BatchRules(); if (rules.Count == 0) return;
        var window = SelectedWindow; int version = _loadVersion;
        using var cts = new CancellationTokenSource(); _backtestCts = cts;
        SetBacktestingState(true); UpdateBatchLabels(); int completed = 0;
        try
        {
            foreach (var rule in rules)
            {
                cts.Token.ThrowIfCancellationRequested();
                _workflowMessage.Text = $"回测 {completed}/{rules.Count} · {rule.Name}";
                await Task.Run(() => _backtestEngine.Run(rule, window, cts.Token), cts.Token); completed++;
            }
            if (_subscribed && version == _loadVersion) { _workflowMessage.Text = $"已完成所选范围回测 {completed}/{rules.Count} 条。"; RenderRuleList(); }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { if (_subscribed && version == _loadVersion) _workflowMessage.Text = $"回测已取消，保留已完成的 {completed}/{rules.Count} 条结果。"; }
        catch (Exception ex) { if (_subscribed) _workflowMessage.Text = $"已完成 {completed}/{rules.Count} 条，回测停止：{ex.Message}"; }
        finally { if (ReferenceEquals(_backtestCts, cts)) _backtestCts = null; SetBacktestingState(false); UpdateBatchLabels(); }
    }
}
