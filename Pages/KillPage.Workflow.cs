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
    private IReadOnlyList<KillSubmissionEntry> _actualEntries = Array.Empty<KillSubmissionEntry>();
    private readonly TextBlock _overview = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(10, 8, 10, 8) };
    private readonly TextBlock _workflowMessage = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange, Margin = new Thickness(0, 5, 0, 0) };
    private readonly Expander _preview = new() { Header = "本期执行预览 · 尚未执行", Margin = new Thickness(0, 8, 0, 0) };
    private readonly WrapPanel _previewBalls = new();
    private readonly TextBlock _previewInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private Button _openReport = null!;
    private bool _previewValid;
    private string? _currentDataHash;

    private void InitializeWorkflow(KillSubmissionStore? store, KillReviewCoordinator? reviews)
    {
        _submissions = store; _reviews = reviews;
        var messageStyle = new Style(typeof(TextBlock));
        var emptyMessage = new Trigger { Property = TextBlock.TextProperty, Value = "" };
        emptyMessage.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed)); messageStyle.Triggers.Add(emptyMessage); _workflowMessage.Style = messageStyle;
        WorkflowHost.Children.Add(new Border { Background = (Brush)FindResource("AccentSoft"), CornerRadius = new CornerRadius(7), Child = _overview });
        WorkflowHost.Children.Add(_workflowMessage);
        var content = new StackPanel();
        var previewActions = new WrapPanel();
        _openReport = KillReportUi.Button("完整报告 / 提交", (_, _) => OpenCurrentReport()); previewActions.Children.Add(_openReport);
        content.Children.Add(previewActions); content.Children.Add(_previewInfo); content.Children.Add(_previewBalls);
        _preview.Content = new ScrollViewer { Content = content, MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        WorkflowHost.Children.Add(_preview);
        foreach (var (name, binding, width) in new[] { ("本期执行", "ExecutionLabel", 130d), ("实际提交", "ActualLabel", 190d) })
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(binding == "ExecutionLabel" ? "ExecutionHint" : binding == "ActualLabel" ? "ActualHint" : "RuleId")));
            var column = new DataGridTextColumn { Header = name, Binding = new Binding(binding), SortMemberPath = binding == "ActualLabel" ? "ActualWrongCount" : binding, Width = width, ElementStyle = style };
            RulesGrid.Columns.Add(column);
            if (binding == "ExecutionLabel") column.DisplayIndex = 4;
        }
        foreach (var (key, position) in new[] { ("Name", 2), ("GateSortValue", 3), ("ExecutionLabel", 4), ("AccuracyValue", 5), ("ActualWrongCount", 6) })
            RulesGrid.Columns.Single(c => c.SortMemberPath == key).DisplayIndex = position;
        foreach (var column in RulesGrid.Columns)
        {
            var key = ColumnKey(column);
            if (column.Width.IsAbsolute) column.MinWidth = Math.Min(column.Width.Value, key is "RuleId" or "Name" or "ExecutionLabel" or "ActualWrongCount" ? 120 : 80);
            if (key == "操作") column.MinWidth = 112;
            if (key == "ActualWrongCount") column.MinWidth = 150;
        }
    }
    private static string ColumnKey(DataGridColumn column) => string.IsNullOrEmpty(column.SortMemberPath) ? column.Header?.ToString() ?? "" : column.SortMemberPath;
    private void PopulateWorkflowRow(RuleRowViewModel row, IKillRule rule, string accuracy)
    {
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
        row.ActualWrongCount = actual?.WrongPeriods ?? 0;
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
        _previewValid = false; _preview.Header = "本期预览已过期 · 请重新执行";
        _previewInfo.Text = reason; _previewBalls.Children.Clear(); _openReport.IsEnabled = false;
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
        var report = _lastReport!; _previewValid = true;
        _preview.Header = $"第 {report.TargetPeriod} 期执行预览"; _preview.IsExpanded = true; _openReport.IsEnabled = true;
        _previewInfo.Text = $"合并排除红球 {report.KilledRedBalls.Count} 个、蓝球 {report.KilledBlueBalls.Count} 个；保留红球 {report.RecommendedRedBalls.Count} 个、蓝球 {report.RecommendedBlueBalls.Count} 个。\n" + KillSubmissionChecks.Describe(report, _ds.GetAllRecords());
        _previewBalls.Children.Clear();
        foreach (var detail in report.KilledRedBalls.Concat(report.KilledBlueBalls))
        {
            _previewBalls.Children.Add(new Border
            {
                Background = (Brush)FindResource("BgSurface"), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 6),
                Child = new TextBlock
                {
                    Text = $"{(detail.BallType == BallType.Red ? "红" : "蓝")} {detail.Ball:D2}",
                    Foreground = detail.BallType == BallType.Red ? Brushes.Firebrick : Brushes.RoyalBlue,
                    FontWeight = FontWeights.SemiBold
                }
            });
        }
    }
}
