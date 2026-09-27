using System.IO;
using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

internal static class GeometryOptimizationAudit
{
    internal sealed record Score(int Triggers, int Killed, int Correct, int Evaluated)
    { public double Accuracy => Killed == 0 ? 0 : (double)Correct / Killed; }
    public static void Run(string input, string output)
    {
        var records = KillResearchService.SnapshotRecords(File.ReadLines(input).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l =>
        {
            var p = l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return new DrawRecord { Period = int.Parse(p[0]), DrawDate = DateTime.Parse(p[1]), RedBalls = p.Skip(2).Take(6).Select(int.Parse).ToArray(), BlueBall = int.Parse(p[8]) };
        }));
        var builder = new RuleContextBuilder(); var matrix = MissMatrixCalculator.Compute(records, records);
        var contexts = Enumerable.Range(0, records.Count).Select(i => builder.BuildWithMiss(records, i, matrix)).ToArray();
        var executor = new JintRuleExecutor();
        Score Evaluate(KillRule rule)
        {
            int triggers = 0, killed = 0, correct = 0, evaluated = 0;
            for (int i = records.Count - 1; i >= 1 && triggers < 50; i--)
            {
                var balls = executor.Execute(rule, contexts[i]).KilledBalls; evaluated++;
                if (balls.Count == 0) continue;
                triggers++; killed += balls.Count;
                correct += balls.Count(b => rule.BallType == BallType.Red ? !records[i].RedBalls.Contains(b) : records[i].BlueBall != b);
            }
            return new(triggers, killed, correct, evaluated);
        }
        var originals = BuiltinRules.LoadOriginalCatalog().ToDictionary(r => r.RuleId);
        var result = new List<object>();
        foreach (var rule in BuiltinRules.LoadAll())
        {
            var current = Evaluate(rule);
            Score? withoutFilter = null;
            if (current.Accuracy < KillSettings.For(rule.BallType) && originals.TryGetValue(rule.RuleId, out var original) && original.JsCode != rule.JsCode)
                withoutFilter = Evaluate(original);
            var candidates = new List<object>();
            if (current.Accuracy < KillSettings.For(rule.BallType) && rule.RuleId.StartsWith("B-G-", StringComparison.Ordinal))
            {
                var conditions = rule.RuleId.Contains("-008", StringComparison.Ordinal)
                    ? new[] { -1, 0, 1 }.SelectMany(d => new[] { 1, 2, 3 }.Select(g => new BuiltinRuleRevisions.GeometryCondition(d, g))).ToArray()
                    : rule.RuleId.Contains("-001", StringComparison.Ordinal)
                        ? new[] { 10, 12, 14, 16, 18 }.Select(t => new BuiltinRuleRevisions.GeometryCondition(MissThreshold: t)).ToArray() : [];
                foreach (var condition in conditions)
                {
                    var candidate = condition.Apply(rule); var score = Evaluate(candidate);
                    candidates.Add(new { Condition = condition, Score = score });
                }
            }
            result.Add(new { rule.RuleId, rule.Name, Current = current, WithoutFilter = withoutFilter, Candidates = candidates, Fingerprint = KillRuleDefinition.Capture(rule).Fingerprint });
            Console.WriteLine($"{rule.RuleId}: {current.Accuracy:P2}/{current.Triggers}" + (withoutFilter is null ? "" : $" original {withoutFilter.Accuracy:P2}/{withoutFilter.Triggers}"));
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { Last = records[^1].Period, Count = records.Count, DataHash = KillRuleDefinition.DataHash(records), Rules = result }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
