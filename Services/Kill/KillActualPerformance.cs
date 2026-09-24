namespace SsqAnalyzer.Services.Kill;

public sealed record KillActualVersion(string Fingerprint, bool Current, int Submitted, int Reviewed, int Triggered,
    int WrongPeriods, int Failed, int[] ErrorPeriods);

public static class KillActualPerformance
{
    public static IReadOnlyList<KillActualVersion> Summarize(IKillRule rule, IEnumerable<KillSubmissionEntry> entries)
    {
        var current = KillRuleDefinition.Capture(rule).Fingerprint;
        return entries.SelectMany(e => e.Submission.Rules.Where(r => r.Definition.RuleId == rule.RuleId)
            .Select(r => new { Entry = e, Rule = r }))
            .GroupBy(x => x.Rule.Definition.Fingerprint)
            .Select(g =>
            {
                var reviewed = g.Where(x => x.Entry.Review is not null).ToArray();
                var triggered = reviewed.Where(x => x.Rule.ExecutionError is null && x.Rule.KilledBalls.Length > 0).ToArray();
                var errors = triggered.Where(x => x.Rule.KilledBalls.Intersect(x.Rule.Definition.BallType == BallType.Red
                    ? x.Entry.Review!.Reds : new[] { x.Entry.Review!.Blue }).Any()).Select(x => x.Entry.Submission.TargetPeriod).OrderDescending().ToArray();
                return new KillActualVersion(g.Key, g.Key == current, g.Count(), reviewed.Length, triggered.Length, errors.Length,
                    reviewed.Count(x => x.Rule.ExecutionError is not null), errors);
            }).OrderByDescending(v => v.Current).ToArray();
    }
    public static string Text(IKillRule rule, IEnumerable<KillSubmissionEntry> entries)
    {
        var versions = Summarize(rule, entries);
        return versions.Count == 0 ? "暂无实际提交记录。历史回测与实际提交表现分别统计。"
            : string.Join("\n\n", versions.Select(v => $"{(v.Current ? "当前版本" : "历史版本")} · {v.Fingerprint[..10]}\n"
                + $"参与提交 {v.Submitted} 期 · 已复盘 {v.Reviewed} 期 · 触发 {v.Triggered} 期 · 错杀 {v.WrongPeriods} 期 · 执行异常 {v.Failed} 期\n"
                + $"错杀期号：{(v.ErrorPeriods.Length == 0 ? "无" : string.Join("、", v.ErrorPeriods))}"))
                + "\n\n只统计实际提交时的规则版本；未触发和异常不计成功，少量记录不代表长期效果。";
    }
}
