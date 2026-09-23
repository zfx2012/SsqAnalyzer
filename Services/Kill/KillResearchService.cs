using System.Text;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services.Kill;

public sealed record KillResearchRow(string RuleId, string Name, BallType BallType, bool Combined,
    KillEvaluationMetrics Development, KillEvaluationMetrics Validation);
public sealed record KillResearchReport(DateTime GeneratedAt, string RuleSetHash, string DataHash,
    int DevelopmentStart, int DevelopmentEnd, int ValidationStart, int ValidationEnd,
    IReadOnlyList<KillRuleDefinition> Rules, IReadOnlyList<KillResearchRow> Rows, int MissingPeriods)
{
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("按时间划分的历史验证（回顾性分析）");
        sb.AppendLine($"开发区间 {DevelopmentStart}—{DevelopmentEnd}；验证区间 {ValidationStart}—{ValidationEnd}");
        sb.AppendLine($"固定规则 {Rules.Count} 条；同年缺失期号约 {MissingPeriods} 期。");
        sb.AppendLine("不自动根据验证成绩挑规则。已用这些历史调过规则时，验证区间并非真正未见数据。");
        sb.AppendLine("合并结果按同一期各规则杀号并集计算；任一参与规则失败，该球种该期标记失败。");
        sb.AppendLine(KillMetricText.MethodNotes);
        foreach (var row in Rows)
        {
            sb.AppendLine($"\n{(row.Combined ? "【合并】" : "【规则】")}{row.Name} / {(row.BallType == BallType.Red ? "红球" : "蓝球")}");
            sb.AppendLine("开发：" + KillMetricText.Format(row.Development));
            sb.AppendLine("验证：" + KillMetricText.Format(row.Validation));
        }
        sb.AppendLine($"\n规则版本：{RuleSetHash}\n数据版本：{DataHash}");
        return sb.ToString();
    }
}

public static class KillMetricText
{
    public const string MethodNotes = "口径：触发率=触发期/成功执行期；整期错杀率和完整保留率以触发期为分母。失败单列。\n"
        + "随机对照保留每期触发和杀号数量，以超几何分布模拟 2000 次；比较概率仅为条件探索性对照，并非预测有效性证明。\n"
        + "95% 区间采用按开奖期的移动块重采样（1000 次），不是下期概率；小样本和全对样本的区间可能偏乐观。\n"
        + "调整概率使用本次比较数量的 Bonferroni 校正，不覆盖之前反复试规则/窗口造成的选择偏差。";
    public static string Percent(double? value) => value is { } v ? $"{v:P1}" : "—";
    public static string Format(KillEvaluationMetrics? m)
    {
        if (m is null) return "旧统计未包含这些指标，请重新回测。";
        return $"检查 {m.Evaluated} 期 / 失败 {m.Failed} 期 / 触发 {m.Triggered} 期；触发率 {Percent(m.TriggerRate)}\n"
            + $"平均杀号 {m.AverageKilled?.ToString("F2") ?? "—"} 个；号码排除正确率 {Percent(m.Accuracy)}；随机基准 {Percent(m.Baseline)}；超出基准 {(m.Excess is { } e ? $"{e * 100:+0.00;-0.00;0.00} 个百分点" : "—")}\n"
            + $"整期错杀率 {Percent(m.WrongPeriodRate)}；完整保留率 {Percent(m.PreserveRate)}；最长连续错杀 {m.LongestWrongStreak} 个检查期\n"
            + $"历史估计 95% 区间 {Percent(m.Lower95)}—{Percent(m.Upper95)}；随机对照 95% 范围 {Percent(m.RandomLower95)}—{Percent(m.RandomUpper95)}\n"
            + $"对照尾部概率 {m.RandomTailProbability?.ToString("F4") ?? "—"}；调整后 {m.AdjustedProbability?.ToString("F4") ?? "—"}（{m.ComparisonCount} 项比较）；{m.Evidence}";
    }
}

public sealed class KillResearchService(IRuleExecutor executor)
{
    public KillResearchReport Evaluate(IEnumerable<IKillRule> selectedRules, IReadOnlyList<DrawRecord> data,
        int developmentPercent = 70, IProgress<(int done, int total)>? progress = null, CancellationToken ct = default)
    {
        if (developmentPercent is < 50 or > 90) throw new ArgumentOutOfRangeException(nameof(developmentPercent), "开发区间比例应为 50—90%。");
        var definitions = selectedRules.Select(KillRuleDefinition.Capture).ToArray();
        if (definitions.Length == 0) throw new InvalidOperationException("请先启用至少一条规则。");
        if (definitions.Select(r => r.RuleId).Distinct().Count() != definitions.Length) throw new InvalidOperationException("规则 ID 重复。");
        var records = SnapshotRecords(data);
        int split = 1 + (records.Count - 1) * developmentPercent / 100;
        if (split - 1 < 30 || records.Count - split < 30) throw new InvalidOperationException("开发和验证区间都至少需要 30 个待预测开奖点，请增加历史或调整比例。");
        var rules = definitions.Select(r => r.ToRule()).ToArray();
        var builder = new RuleContextBuilder();
        var miss = MissMatrixCalculator.Compute(records, records);
        var observations = rules.ToDictionary(r => r.RuleId, _ => new List<KillEvaluationPeriod>());
        var red = new List<KillEvaluationPeriod>(); var blue = new List<KillEvaluationPeriod>();
        for (int i = 1; i < records.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var context = builder.BuildWithMiss(records, i, miss);
            var redKills = new HashSet<int>(); var blueKills = new HashSet<int>();
            bool redFailed = false, blueFailed = false;
            foreach (var rule in rules)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var result = executor.Execute(rule, context);
                    var killed = result.KilledBalls.Distinct().ToArray();
                    var actual = rule.BallType == BallType.Red ? records[i].RedBalls : new[] { records[i].BlueBall };
                    observations[rule.RuleId].Add(new(records[i].Period, killed.Length, killed.Count(actual.Contains)));
                    (rule.BallType == BallType.Red ? redKills : blueKills).UnionWith(killed);
                }
                catch (RuleExecutionException)
                {
                    observations[rule.RuleId].Add(new(records[i].Period, 0, 0, true));
                    if (rule.BallType == BallType.Red) redFailed = true; else blueFailed = true;
                }
            }
            red.Add(redFailed ? new(records[i].Period, 0, 0, true) : new(records[i].Period, redKills.Count, redKills.Count(records[i].RedBalls.Contains)));
            blue.Add(blueFailed ? new(records[i].Period, 0, 0, true) : new(records[i].Period, blueKills.Count, blueKills.Contains(records[i].BlueBall) ? 1 : 0));
            progress?.Report((i, records.Count - 1));
        }
        int comparisons = (rules.Length + 2) * 2;
        KillResearchRow Summarize(string id, string name, BallType ball, bool combined, List<KillEvaluationPeriod> periods) => new(id, name, ball, combined,
            KillEvaluationMetrics.Compute(periods.Take(split - 1), ball, comparisons, ct),
            KillEvaluationMetrics.Compute(periods.Skip(split - 1), ball, comparisons, ct));
        var rows = new List<KillResearchRow>
        {
            Summarize("combined-red", "已启用规则的红球并集", BallType.Red, true, red),
            Summarize("combined-blue", "已启用规则的蓝球并集", BallType.Blue, true, blue)
        };
        rows.AddRange(rules.Select(r => Summarize(r.RuleId, r.Name, r.BallType, false, observations[r.RuleId])));
        ct.ThrowIfCancellationRequested();
        int missing = records.Zip(records.Skip(1)).Where(p => p.First.Period / 1000 == p.Second.Period / 1000)
            .Sum(p => Math.Max(0, p.Second.Period - p.First.Period - 1));
        return new(DateTime.UtcNow, KillRuleDefinition.RulesHash(definitions), KillRuleDefinition.DataHash(records),
            records[1].Period, records[split - 1].Period, records[split].Period, records[^1].Period, definitions, rows, missing);
    }

    public static List<DrawRecord> SnapshotRecords(IEnumerable<DrawRecord> data)
    {
        var records = data.Select(r => new DrawRecord
        {
            Period = r.Period, DrawDate = r.DrawDate, RedBalls = r.RedBalls.ToArray(), BlueBall = r.BlueBall
        }).OrderBy(r => r.Period).ToList();
        if (records.Select(r => r.Period).Distinct().Count() != records.Count
            || records.Any(r => r.RedBalls.Count != 6 || r.RedBalls.Distinct().Count() != 6 || r.RedBalls.Any(b => b is < 1 or > 33) || r.BlueBall is < 1 or > 16)
            || records.Zip(records.Skip(1)).Any(p => p.First.DrawDate.Date >= p.Second.DrawDate.Date))
            throw new InvalidOperationException("历史数据存在重复期号、无效号码或日期顺序异常，请先修复数据。");
        return records;
    }
}
