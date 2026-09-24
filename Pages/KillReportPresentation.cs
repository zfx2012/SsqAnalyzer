using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SsqAnalyzer.Services.Kill;

namespace SsqAnalyzer.Pages;

/// <summary>Presentation only; submission and review calculations remain in the store.</summary>
internal static class KillReportPresentation
{
    private static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    private static readonly Brush Ink = Brush("#243247"), Muted = Brush("#66758A"), Red = Brush("#C84151"), Blue = Brush("#276AC2");
    internal static TextBlock Label(string text, double size = 13, bool bold = false, Brush? color = null) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.6
    };
    internal static Border Card(UIElement child) => new()
    {
        Background = Brushes.White, BorderBrush = Brush("#E3E8EF"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 12), Child = child
    };
    internal static FrameworkElement Header(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(Label(title, 25, true));
        panel.Children.Add(Label(subtitle, 12, color: Muted));
        return panel;
    }
    internal static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0)
    };
    private static FrameworkElement Metrics(params (string Label, string Value, string Note)[] items)
    {
        var grid = new UniformGrid { Columns = items.Length, Margin = new Thickness(0, 0, -10, 2) };
        foreach (var item in items)
        {
            var panel = new StackPanel();
            panel.Children.Add(Label(item.Label, 12, color: Muted));
            panel.Children.Add(Label(item.Value, 27, true, item.Label.StartsWith("错杀") && item.Value != "0" ? Red : Ink));
            panel.Children.Add(Label(item.Note, 12, color: Muted));
            var card = Card(panel); card.Margin = new Thickness(0, 0, 10, 12); grid.Children.Add(card);
        }
        return grid;
    }
    private static FrameworkElement Balls(IEnumerable<int> values, BallType type, IEnumerable<int>? wrong = null)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var errors = wrong?.ToHashSet() ?? new();
        foreach (int value in values)
        {
            bool error = errors.Contains(value);
            var text = Label(value.ToString("D2"), 15, true, type == BallType.Red ? Red : Blue);
            text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(new Border
            {
                Child = text, Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 8, 8),
                Background = Brush(type == BallType.Red ? "#FFF0F2" : "#EDF4FF"),
                BorderBrush = error ? Red : Brushes.Transparent, BorderThickness = new Thickness(error ? 2 : 0),
                ToolTip = error ? "此开奖号码被提交规则错杀" : $"{(type == BallType.Red ? "红球" : "蓝球")} {value:D2}"
            });
        }
        if (panel.Children.Count == 0) panel.Children.Add(Label("无", 13, color: Muted));
        return panel;
    }
    private static FrameworkElement BallSection(string title, string note, IEnumerable<int> reds, IEnumerable<int> blues,
        IEnumerable<int>? wrongReds = null, IEnumerable<int>? wrongBlues = null)
    {
        var panel = new StackPanel(); panel.Children.Add(Label(title, 17, true));
        panel.Children.Add(Label(note, 12, color: Muted));
        var redTitle = Label("红球", 12, true, Red); redTitle.Margin = new Thickness(0, 10, 0, 0); panel.Children.Add(redTitle);
        panel.Children.Add(Balls(reds, BallType.Red, wrongReds));
        panel.Children.Add(Label("蓝球", 12, true, Blue)); panel.Children.Add(Balls(blues, BallType.Blue, wrongBlues));
        return Card(panel);
    }
    internal static FrameworkElement Report(KillReport report)
    {
        var rules = report.RuleDefinitions.Select(d =>
        {
            var result = report.Results.Single(r => r.RuleId == d.RuleId);
            return new SubmittedKillRule(d, result.KilledBalls.ToArray(), result.Reason, result.ExecutionError);
        }).ToArray();
        return Prediction(rules, report.RecommendedRedBalls, report.RecommendedBlueBalls, report.ToMarkdown());
    }
    internal static FrameworkElement Original(KillSubmission submission)
    {
        var panel = new StackPanel();
        panel.Children.Add(Header($"第 {submission.TargetPeriod} 期 · 提交快照",
            $"提交于 {KillDrawSchedule.ChinaTime(submission.SubmittedAtUtc):yyyy-MM-dd HH:mm:ss}   ·   历史截至 {submission.SourceThroughPeriod} 期"));
        panel.Children.Add(Prediction(submission.Rules,
            Enumerable.Range(1, 33).Except(submission.Rules.Where(r => r.Definition.BallType == BallType.Red).SelectMany(r => r.KilledBalls)),
            Enumerable.Range(1, 16).Except(submission.Rules.Where(r => r.Definition.BallType == BallType.Blue).SelectMany(r => r.KilledBalls)), submission.OriginalReport));
        return panel;
    }
    private static FrameworkElement Prediction(SubmittedKillRule[] rules, IEnumerable<int> reds, IEnumerable<int> blues, string original)
    {
        var panel = new StackPanel(); var redArray = reds.ToArray(); var blueArray = blues.ToArray();
        panel.Children.Add(Metrics(("参与规则", rules.Length.ToString(), $"触发 {rules.Count(r => r.ExecutionError is null && r.KilledBalls.Length > 0)} 条"),
            ("保留红球", redArray.Length.ToString(), $"排除 {33 - redArray.Length} 个"),
            ("保留蓝球", blueArray.Length.ToString(), $"排除 {16 - blueArray.Length} 个")));
        if (rules.Any(r => r.ExecutionError is not null)) panel.Children.Add(Notice($"有 {rules.Count(r => r.ExecutionError is not null)} 条规则执行异常，详见下方规则明细。", true));
        panel.Children.Add(BallSection("推荐号码", "以下为所有启用规则执行后保留的号码。", redArray, blueArray));
        panel.Children.Add(BallSection("本期排除", "同一号码按球种去重，规则支持数不代表下一期命中概率。",
            rules.Where(r => r.Definition.BallType == BallType.Red).SelectMany(r => r.KilledBalls).Distinct().Order(),
            rules.Where(r => r.Definition.BallType == BallType.Blue).SelectMany(r => r.KilledBalls).Distinct().Order()));
        panel.Children.Add(Rules(rules, null));
        panel.Children.Add(new Expander { Header = "查看 / 复制原始报告文本", Content = KillReportUi.Text(original), Margin = new Thickness(0, 4, 0, 16) });
        return panel;
    }
    internal static FrameworkElement Notice(string message, bool error = false) => new Border
    {
        Background = Brush(error ? "#FFF3E5" : "#EDF4FF"), CornerRadius = new CornerRadius(8),
        Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12),
        Child = Label(message, 13, color: error ? Brush("#925B17") : Blue)
    };
    internal static FrameworkElement Review(KillSubmissionEntry entry, string? warning)
    {
        var panel = new StackPanel(); var s = entry.Submission;
        panel.Children.Add(Header($"第 {s.TargetPeriod} 期 · {KillSubmissionStore.Status(entry)}",
            $"提交于 {KillDrawSchedule.ChinaTime(s.SubmittedAtUtc):yyyy-MM-dd HH:mm}（北京时间）   ·   {s.Rules.Length} 条规则"));
        if (!string.IsNullOrEmpty(warning)) panel.Children.Add(Notice(warning, true));
        if (entry.Review is not { } actual)
        {
            var empty = new StackPanel { Margin = new Thickness(12, 22, 12, 22) };
            empty.Children.Add(Label("等待开奖结果", 22, true));
            empty.Children.Add(Label($"预计开奖日期：{s.TargetDate:yyyy-MM-dd}\n获取该期有效开奖结果后将自动复盘。可点击上方“更新开奖并复盘”。", 14, color: Muted));
            panel.Children.Add(Card(empty)); return panel;
        }
        var red = KillSubmissionStore.WrongBalls(entry, BallType.Red); var blue = KillSubmissionStore.WrongBalls(entry, BallType.Blue);
        int failed = s.Rules.Count(r => r.ExecutionError is not null);
        panel.Children.Add(Metrics(("错杀红球", red.Length.ToString(), $"实际红球保留 {6 - red.Length} / 6"),
            ("错杀蓝球", blue.Length.ToString(), blue.Length == 0 ? "实际蓝球已保留" : "实际蓝球被排除"),
            ("执行异常", failed.ToString(), "异常与未触发不计成功")));
        panel.Children.Add(BallSection("实际开奖号码", $"开奖日期 {actual.DrawDate:yyyy-MM-dd}   ·   描边号码为本期错杀", actual.Reds, new[] { actual.Blue }, red, blue));
        string numbers(int[] values) => values.Length == 0 ? "无" : string.Join(" ", values.Select(n => n.ToString("D2")));
        panel.Children.Add(Notice($"错杀红球：{numbers(red)} ；错杀蓝球：{numbers(blue)}。同一号码被多条规则排除，整体仅计一次。", red.Length + blue.Length > 0));
        if (s.TargetDate != actual.DrawDate) panel.Children.Add(Notice($"预计日期 {s.TargetDate:yyyy-MM-dd} 与实际不同，本次按期号匹配实际开奖。", true));
        panel.Children.Add(Rules(s.Rules, actual));
        panel.Children.Add(Label("复盘仅使用提交时的结果。单期结果不能证明错误原因或长期效果，不会自动修改规则。", 12, color: Muted));
        return panel;
    }
    private static FrameworkElement Rules(SubmittedKillRule[] rules, KillSubmissionReview? actual)
    {
        var panel = new StackPanel();
        panel.Children.Add(Label(actual is null ? "规则明细" : "规则复盘", 17, true));
        panel.Children.Add(Label(actual is null ? "展开规则查看执行依据和完整编号。" : "错杀规则优先展示；展开查看当时的杀号与依据。", 12, color: Muted));
        var rows = rules.Select(r => new { Rule = r, Wrong = actual is null ? Array.Empty<int>() : r.KilledBalls.Intersect(r.Definition.BallType == BallType.Red ? actual.Reds : new[] { actual.Blue }).Order().ToArray() });
        foreach (var row in rows.OrderByDescending(r => r.Wrong.Length).ThenByDescending(r => r.Rule.ExecutionError is not null).ThenBy(r => r.Rule.Definition.RuleId))
        {
            var r = row.Rule;
            string state = r.ExecutionError is not null ? "执行异常" : r.KilledBalls.Length == 0 ? "未触发" : actual is null ? $"排除 {r.KilledBalls.Length} 个" : row.Wrong.Length > 0 ? $"错杀 {row.Wrong.Length} 个" : "无错杀";
            var heading = new StackPanel();
            heading.Children.Add(Label($"{(r.Definition.BallType == BallType.Red ? "红球" : "蓝球")} · {r.Definition.Name}", 14, true));
            heading.Children.Add(Label(state + (row.Wrong.Length > 0 ? "  ·  " + string.Join(" ", row.Wrong.Select(n => n.ToString("D2"))) : ""), 12, color: row.Wrong.Length > 0 || r.ExecutionError is not null ? Red : Muted));
            var body = new StackPanel { Margin = new Thickness(22, 8, 0, 0) };
            body.Children.Add(Balls(r.KilledBalls, r.Definition.BallType, row.Wrong));
            body.Children.Add(Label(r.ExecutionError is not null ? $"执行异常：{r.ExecutionError}（不计成功）" : r.KilledBalls.Length == 0 ? "未触发（不计成功）" : actual is not null ? $"正确排除 {r.KilledBalls.Length - row.Wrong.Length} 个 · 错杀 {row.Wrong.Length} 个" : "本次执行已触发", 13));
            body.Children.Add(Label($"当时依据：{r.Reason}\n规则编号：{r.Definition.RuleId}", 12, color: Muted));
            panel.Children.Add(new Border { BorderBrush = Brush("#EBEFF4"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(0, 12, 0, 0),
                Child = new Expander { Header = heading, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = body, IsExpanded = row.Wrong.Length > 0 } });
        }
        return Card(panel);
    }
}
