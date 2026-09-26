using System.IO;
using System.Text;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    public static void VerifyPatternGuides()
    {
        VerifyBuiltinKillRuleCuration();
        var original = BuiltinRules.LoadOriginalCatalog().ToDictionary(r => r.RuleId);
        var executor = new JintRuleExecutor();
        var builder = new RuleContextBuilder();
        int comparisons = 0;
        foreach (var (scope, suffix) in new[] { ("samePeriod", "HS"), ("parity", "OE"), ("cycle", "CY") })
        {
            var records = new List<DrawRecord>();
            int[][] shape = [[6, 12, 16, 20, 24, 28], [7, 13, 17, 21, 25, 29], [6, 14, 18, 22, 26, 30]];
            for (int year = 2020; year <= 2023; year++)
            {
                var date = new DateTime(year, 1, 1);
                while (date.DayOfWeek != DayOfWeek.Thursday) date = date.AddDays(1);
                records.Add(new DrawRecord { Period = year * 1000 + 2, DrawDate = date,
                    RedBalls = year < 2023 ? shape[year - 2020] : new[] { 1, 2, 3, 4, 5, 7 }, BlueBall = 4 });
                if (year < 2023) records.Add(new DrawRecord { Period = year * 1000 + 3, DrawDate = date.AddDays(2), RedBalls = new[] { 7, 9, 11, 15, 23, 33 }, BlueBall = 9 });
            }
            var context = builder.Build(records, records.Count - 1);
            Assert(context.GetMiss(7, 0) == 0 && context.GetMissFor(scope, 7, 0) == 1,
                $"{scope}: full-history noise must not replace scoped omission");
            var scopedRule = original[$"B-G-R-008-{suffix}"];
            Assert(executor.Execute(scopedRule, context).KilledBalls.Contains(7), $"{scope}: missing parallelogram corner detected");
            records[^1].RedBalls = new[] { 1, 2, 3, 4, 5, 6 };
            Assert(executor.Execute(scopedRule, builder.Build(records, records.Count - 1)).KilledBalls.Contains(7), $"{scope}: target balls isolated");
            records[2].RedBalls = new[] { 6, 7, 17, 21, 25, 29 };
            Assert(!executor.Execute(scopedRule, builder.Build(records, records.Count - 1)).KilledBalls.Contains(7), $"{scope}: blocked corner rejected");
        }

        // All 12 red mechanisms must agree with executing the base rule on the same filtered rows.
        // Deterministic varied draws exercise bounds, no-match cases and mixed-scope histories.
        var random = new Random(260926);
        var sample = Enumerable.Range(0, 180).Select(i => new DrawRecord
        {
            Period = (2020 + i / 30) * 1000 + i % 30 + 1,
            DrawDate = new DateTime(2020, 1, 2).AddDays(i * 2),
            RedBalls = Enumerable.Range(1, 33).OrderBy(_ => random.Next()).Take(6).Order().ToArray(), BlueBall = random.Next(1, 17)
        }).ToArray();
        foreach (int index in Enumerable.Range(1, sample.Length - 1))
        foreach (var (scope, suffix) in new[] { ("samePeriod", "HS"), ("parity", "OE"), ("cycle", "CY") })
        {
            var context = builder.Build(sample, index);
            var filtered = context.HistoryFor(scope, int.MaxValue);
            var reference = builder.Build(filtered, filtered.Count);
            foreach (var baseRule in original.Values.Where(r => r.BallType == BallType.Red && r.RuleId.Length == 9))
            {
                var expected = executor.Execute(baseRule, reference).KilledBalls;
                var actual = executor.Execute(original[$"{baseRule.RuleId}-{suffix}"], context).KilledBalls;
                Assert(actual.SequenceEqual(expected), $"scope equivalence {baseRule.RuleId}/{suffix}/{index}");
                comparisons++;
            }
        }
        var guides = BuiltinRules.LoadAll().Where(r => r.RuleId.StartsWith("B-G-", StringComparison.Ordinal))
            .Select(r => (Rule: r, Guide: BuiltinPatternGuide.For(r))).ToArray();
        Assert(guides.Length == 50 && guides.All(g => g.Guide is not null), "all original 50 rules have a guide");
        Assert(guides.Count(g => g.Guide!.Kind == "图形结构") == 40, "40 geometric variants are distinguished from omission and mapping rules");
        foreach (var (_, guide) in guides)
            Assert(guide!.Cells.Length == guide.RowLabels.Length && guide.Cells.All(row => row.Length == guide.Columns.Length), "diagram dimensions");
        Assert(guides.Single(g => g.Rule.RuleId == "B-G-R-005").Guide!.Cells.SequenceEqual(new[] { "●", "○", "○", "●", "○", "○", "×" }), "current two-gap alternation shown");
        Assert(BuiltinPatternGuide.For(BuiltinRules.LoadAll().Single(r => r.RuleId == "B-S-R-001")) is { Kind: "轨迹图形" }, "converted extra has a geometry guide");
        Console.WriteLine($"PASS pattern guides and {comparisons} scoped/base comparisons");
    }

    public static void ExportPatternGuides(string path)
    {
        var text = new StringBuilder("# 原50条规则：图形与附加条件清单\n\n结构示意用于解释定义，不是当期预测或效果证明。●必须开出；○必须未开；·不限制；×下一行候选。\n\n");
        foreach (var rule in BuiltinRules.LoadAll().Where(r => r.RuleId.StartsWith("B-G-", StringComparison.Ordinal)))
        {
            var guide = BuiltinPatternGuide.For(rule)!;
            text.AppendLine($"## {rule.RuleId} {rule.Name}\n\n- 类型：{guide.Kind}\n- 图层：{guide.Scope}\n- 识别：{guide.Shape}\n- 杀号：{guide.Target}\n- 附加：{guide.Condition}\n");
            if (guide.Columns.Length == 0) continue;
            text.AppendLine("|行|" + string.Join("|", guide.Columns) + "|\n|---|" + string.Join("|", guide.Columns.Select(_ => "---")) + "|");
            for (int i = 0; i < guide.Cells.Length; i++) text.AppendLine($"|{guide.RowLabels[i]}|" + string.Join("|", guide.Cells[i].Select(c => c.ToString())) + "|");
            text.AppendLine();
        }
        File.WriteAllText(path, text.ToString().TrimEnd() + "\n", new UTF8Encoding(false));
    }
}
