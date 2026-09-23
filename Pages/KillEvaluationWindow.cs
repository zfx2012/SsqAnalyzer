using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

namespace SsqAnalyzer.Pages;

public sealed class KillEvaluationWindow : Window
{
    private readonly IDataService _data;
    private readonly IRuleRepository _rules;
    private readonly IRuleExecutor _executor;
    private readonly KillForwardStore _store;
    private readonly TextBox _output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12), FontSize = 13 };
    private readonly TextBox _split = new() { Text = "70", Width = 44 };
    private readonly TextBox _period = new() { Width = 90 };
    private readonly DatePicker _date = new() { Width = 130 };
    private readonly WrapPanel _actions = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 8, 0, 8) };
    private CancellationTokenSource? _cts;
    private bool _closed;

    public KillEvaluationWindow(IDataService data, IRuleRepository rules, IRuleExecutor executor, KillForwardStore store)
    {
        _data = data; _rules = rules; _executor = executor; _store = store;
        Title = "杀号回测评估与前向验证"; Width = 1040; Height = 780; MinWidth = 720; MinHeight = 480;
        Background = (Brush)Application.Current.FindResource("BgContent");
        Foreground = (Brush)Application.Current.FindResource("TextPrimary");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "使用当前已启用规则的固定快照，逐期只读取此前历史。红球 82%、蓝球 94% 门槛保持不变。\n历史验证不等于未来有效；前向冻结必须在开奖日前完成，每期只能记录一次。日期建议不包含节假日调整，请按实际开奖安排填写。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        top.Children.Add(_actions);
        _actions.Children.Add(new TextBlock { Text = "开发占比 %", VerticalAlignment = VerticalAlignment.Center });
        _actions.Children.Add(_split);
        AddButton("历史分段与合并评估", Analyze);
        _actions.Children.Add(new TextBlock { Text = "  目标期号", VerticalAlignment = VerticalAlignment.Center });
        _actions.Children.Add(_period); _actions.Children.Add(_date);
        AddButton("开奖前冻结", Freeze); AddButton("结算 / 查看记录", Refresh);
        var bottom = new WrapPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var cancel = new Button { Content = "取消计算", Margin = new Thickness(0, 8, 8, 0) };
        cancel.Click += (_, _) => _cts?.Cancel(); bottom.Children.Add(cancel);
        var export = new Button { Content = "导出当前报告", Margin = new Thickness(0, 8, 8, 0) };
        export.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "文本报告|*.txt", FileName = "杀号评估.txt" };
            if (dialog.ShowDialog(this) == true)
                try { File.WriteAllText(dialog.FileName, _output.Text); } catch (Exception ex) { _status.Text = ex.Message; }
        };
        bottom.Children.Add(export); top.Children.Add(_status); root.Children.Add(_output); Content = root;
        var latest = data.GetAllRecords().LastOrDefault();
        if (latest is not null)
        {
            var next = latest.DrawDate.Date.AddDays(1);
            while (next.DayOfWeek is not (DayOfWeek.Tuesday or DayOfWeek.Thursday or DayOfWeek.Sunday)) next = next.AddDays(1);
            _date.SelectedDate = next;
            _period.Text = (next.Year > latest.DrawDate.Year ? next.Year * 1000 + 1 : latest.Period + 1).ToString();
        }
        _output.Text = KillMetricText.MethodNotes;
        Closed += (_, _) => { _closed = true; _cts?.Cancel(); };
    }

    private void AddButton(string title, Func<CancellationToken, Task<string>> action)
    {
        var button = new Button { Content = title, Margin = new Thickness(6, 2, 6, 2) };
        button.Click += async (_, _) =>
        {
            if (_cts is not null) return;
            using var cts = new CancellationTokenSource(); _cts = cts; _actions.IsEnabled = false; _status.Text = "正在处理…";
            try { var text = await action(cts.Token); if (!_closed) { _output.Text = text; _status.Text = "完成"; } }
            catch (OperationCanceledException) { if (!_closed) _status.Text = "已取消"; }
            catch (Exception ex) { if (!_closed) _status.Text = "未完成：" + ex.Message; }
            finally { _cts = null; if (!_closed) _actions.IsEnabled = true; }
        };
        _actions.Children.Add(button);
    }
    private IKillRule[] SnapshotRules() => _rules.GetEnabled().Select(r => (IKillRule)KillRuleDefinition.Capture(r).ToRule()).ToArray();
    private Task<string> Analyze(CancellationToken ct)
    {
        if (!int.TryParse(_split.Text, out var split)) throw new InvalidOperationException("请输入开发区间百分比。");
        var rules = SnapshotRules(); var records = _data.GetAllRecords();
        return Task.Run(() => new KillResearchService(_executor).Evaluate(rules, records, split, ct: ct).ToText(), ct);
    }
    private Task<string> Freeze(CancellationToken ct)
    {
        if (!int.TryParse(_period.Text, out var period) || _date.SelectedDate is not { } date) throw new InvalidOperationException("请输入期号与开奖日期。");
        var rules = SnapshotRules(); var records = _data.GetAllRecords();
        return Task.Run(() => { _store.Freeze(rules, records, period, date, _executor, ct); return _store.ToText(ct); }, ct);
    }
    private Task<string> Refresh(CancellationToken ct)
    {
        var records = _data.GetAllRecords();
        return Task.Run(() => { _store.Settle(records, ct); return _store.ToText(ct); }, ct);
    }
}
