using System.Text;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 完整杀号报告：被预测期号 + 启用规则列表 + 各规则结果 + 被杀明细 + 推荐集。
/// </summary>
public sealed class KillReport
{
    public DateTime? TargetDate { get; init; }
    public int SourceThroughPeriod { get; init; }
    public string? SourceDataHash { get; init; }
    public IReadOnlyList<KillRuleDefinition> RuleDefinitions { get; init; } = Array.Empty<KillRuleDefinition>();
    public BacktestWindow? EvaluationWindow { get; init; }
    public required int TargetPeriod { get; init; }                    // 被预测期号
    public required DateTime GeneratedAt { get; init; }
    public required IReadOnlyList<IKillRule> EnabledRules { get; init; }
    public required IReadOnlyList<KillResult> Results { get; init; }
    public required IReadOnlyList<KilledBallDetail> KilledRedBalls { get; init; }
    public required IReadOnlyList<KilledBallDetail> KilledBlueBalls { get; init; }
    public required IReadOnlyList<int> RecommendedRedBalls { get; init; }    // 1-33 扣杀
    public required IReadOnlyList<int> RecommendedBlueBalls { get; init; }   // 1-16 扣杀

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 第 {TargetPeriod} 期杀号报告");
        sb.AppendLine();
        sb.AppendLine($"- 生成时间：{KillDrawSchedule.ChinaTime(GeneratedAt.ToUniversalTime()):yyyy-MM-dd HH:mm:ss}（北京时间）");
        sb.AppendLine($"- 启用规则：{EnabledRules.Count} 条");
        if (TargetDate is { } date) sb.AppendLine($"- 预计开奖日期：{date:yyyy-MM-dd}（常规日历推算）");
        if (SourceThroughPeriod > 0) sb.AppendLine($"- 历史数据截至：{SourceThroughPeriod}");
        foreach (var failed in Results.Where(r => r.ExecutionError is not null))
            sb.AppendLine($"- 执行异常 {failed.RuleId}：{failed.ExecutionError}");
        string windowLabel = EvaluationWindow switch
        {
            BacktestWindow.Last30Triggers => "近 30 次触发",
            BacktestWindow.Last50Triggers => "近 50 次触发",
            BacktestWindow.Last100Triggers => "近 100 次触发",
            BacktestWindow.All => "全部历史",
            _ => "各规则最近一次回测"
        };
        sb.AppendLine($"- 评价窗口：{windowLabel}（不回退使用其他窗口）");
        sb.AppendLine();

        sb.AppendLine("## 红球杀号明细");
        if (KilledRedBalls.Count == 0)
            sb.AppendLine("- 无");
        else
        {
            foreach (var d in KilledRedBalls)
            {
                var traces = string.Join("、", d.Traces.Select(t => $"{t.RuleName}({t.BacktestAccuracy:P0})"));
                sb.AppendLine($"- **{d.Ball:D2}** [达标规则支持 {d.PassingRuleCount} 条]：{d.Summary} → {traces}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## 蓝球杀号明细");
        foreach (var d in KilledBlueBalls)
            sb.AppendLine($"- {d.Ball:D2}：共 {d.Traces.Count} 条规则支持，其中达标 {d.PassingRuleCount} 条");
        sb.AppendLine("支持数不代表独立证据或下一期命中概率。");
        sb.AppendLine();
        sb.AppendLine("## 推荐红球");
        sb.AppendLine(string.Join(" ", RecommendedRedBalls.Select(n => n.ToString("D2"))));
        sb.AppendLine();

        sb.AppendLine("## 推荐蓝球");
        sb.AppendLine(string.Join(" ", RecommendedBlueBalls.Select(n => n.ToString("D2"))));

        return sb.ToString();
    }
}
