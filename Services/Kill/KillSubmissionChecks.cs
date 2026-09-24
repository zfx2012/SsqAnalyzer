using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services.Kill;

public static class KillSubmissionChecks
{
    public static string Describe(KillReport report, IReadOnlyList<DrawRecord> history, DateTime? utcNow = null)
    {
        var messages = new List<string>();
        if (history.Count == 0 || report.SourceDataHash != KillRuleDefinition.DataHash(history)) messages.Add("开奖数据已变化，请更新数据并重新执行。");
        if (report.TargetDate is not { } date || KillDrawSchedule.ChinaTime(utcNow ?? DateTime.UtcNow) >= date.Date.AddHours(21))
            messages.Add("已过提交截止时间，请更新开奖数据并重新生成报告。");
        if (report.RecommendedRedBalls.Count < 6) messages.Add("红球保留不足 6 个，无法组成一注，请减少启用规则后重新执行。");
        if (report.RecommendedBlueBalls.Count == 0) messages.Add("蓝球已全部排除，请减少启用规则后重新执行。");
        int failed = report.Results.Count(r => r.ExecutionError is not null);
        if (failed > 0) messages.Add($"{failed} 条规则执行异常；可查看规则明细、修正规则并重新执行。提交将保留异常记录。");
        return messages.Count == 0 ? "提交检查：数据与报告一致，号码数量可组成一注。是否达标不代表下一期效果。"
            : "提交检查：" + string.Join("\n", messages);
    }
}
