using System.IO;
using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

internal static class KillRuleRevisionAudit
{
    public static void Refresh()
    {
        var data = new SsqAnalyzer.Services.DataService();
        var repository = new RuleRepository();
        var engine = new BacktestEngine(new JintRuleExecutor(), new RuleContextBuilder(data), repository, data);
        foreach (var rule in repository.GetAll())
        {
            foreach (var window in new[] { BacktestWindow.All, BacktestWindow.Last100Triggers, BacktestWindow.Last30Triggers, BacktestWindow.Last50Triggers })
            {
                if (window == BacktestWindow.Last50Triggers && rule is KillRule mutable) mutable.IsEnabled = true;
                engine.Run(rule, window);
            }
            Console.WriteLine($"Refreshed {rule.RuleId}: {rule.BacktestStats!.Window50.Accuracy:P2}");
        }
    }
    // Two predeclared, history-only risk reductions; never tune on validation outcomes.
    internal static string CandidateCode(string code, int lookback) => code + $$"""

var originalKill = getKillBalls;
getKillBalls = function(ctx) {
  var candidates = originalKill(ctx);
  if (!candidates || candidates.length < 2) return candidates;
  var history = ctx.history({{lookback}});
  if (history.length < {{lookback}}) return [];
  var counts = {};
  for (var i = 0; i < history.length; i++)
    for (var j = 0; j < history[i].redBalls.length; j++) {
      var b = history[i].redBalls[j]; counts[b] = (counts[b] || 0) + 1;
    }
  candidates.sort(function(a,b) { return (counts[a] || 0) - (counts[b] || 0) || a-b; });
  return [candidates[0]];
};
""";

    public static void Run(string input, string output)
    {
        Directory.CreateDirectory(output);
        var records = KillResearchService.SnapshotRecords(File.ReadLines(input).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l =>
        {
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new DrawRecord { Period = int.Parse(p[0]), DrawDate = DateTime.Parse(p[1]), RedBalls = p.Skip(2).Take(6).Select(int.Parse).ToArray(), BlueBall = int.Parse(p[8]) };
        }));
        int split = 1 + (records.Count - 1) * 70 / 100;
        var builder = new RuleContextBuilder(); var miss = MissMatrixCalculator.Compute(records, records);
        var executor = new JintRuleExecutor();
        var results = new List<object>();
        foreach (var rule in BuiltinRules.LoadOriginalCatalog())
        {
            var original = Evaluate(rule, 1, records.Count);
            var latest = Summary(original.Where(p => p.KilledCount > 0).TakeLast(50));
            bool failed = !Pass(latest, rule.BallType, 50);
            var variants = new List<(int Lookback, List<KillEvaluationPeriod> Dev)>();
            if (failed && rule.BallType == BallType.Red)
                foreach (int lookback in new[] { 60, 180 })
                {
                    var candidate = (KillRuleDefinition.Capture(rule) with { Code = CandidateCode(rule.JsCode, lookback) }).ToRule();
                    variants.Add((lookback, Evaluate(candidate, 1, split)));
                }
            var chosen = variants.Where(v => Pass(Summary(v.Dev), rule.BallType, 100))
                .OrderByDescending(v => Summary(v.Dev).Accuracy).ThenBy(v => v.Lookback).FirstOrDefault();
            List<KillEvaluationPeriod>? replacement = null;
            if (chosen.Dev is not null)
            {
                var candidate = (KillRuleDefinition.Capture(rule) with { Code = CandidateCode(rule.JsCode, chosen.Lookback) }).ToRule();
                replacement = chosen.Dev.Concat(Evaluate(candidate, split, records.Count)).ToList();
            }
            bool accepted = replacement is not null && Pass(Summary(replacement.Skip(split - 1)), rule.BallType, 100)
                && new[] { 30, 50, 100 }.All(n => Pass(Summary(replacement.Where(p => p.KilledCount > 0).TakeLast(n)), rule.BallType, n))
                && Pass(Summary(replacement), rule.BallType, 100);
            var row = new { rule.RuleId, rule.Name, rule.BallType, Action = !failed ? "keep" : accepted ? "revise" : "retire", Lookback = chosen.Lookback,
                Original50 = latest, OriginalAll = Summary(original), OriginalValidation = Summary(original.Skip(split - 1)),
                Candidates = variants.Select(v => new { v.Lookback, Development = Summary(v.Dev) }).ToArray(),
                RevisedAll = replacement is null ? null : Summary(replacement), RevisedValidation = replacement is null ? null : Summary(replacement.Skip(split - 1)),
                RevisedWindows = replacement is null ? null : new[] { 30, 50, 100 }.Select(n => new { Window = n, Metrics = Summary(replacement.Where(p => p.KilledCount > 0).TakeLast(n)) }).ToArray() };
            results.Add(row);
            Console.WriteLine($"{rule.RuleId} {row.Action} original50={latest.Accuracy:P2} candidate={chosen.Lookback} validation={row.RevisedValidation?.Accuracy:P2}");
            File.WriteAllText(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(new { DataHash = KillRuleDefinition.DataHash(records), Count = records.Count,
                First = records[0].Period, Last = records[^1].Period, DevelopmentEnd = records[split - 1].Period, ValidationStart = records[split].Period, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        List<KillEvaluationPeriod> Evaluate(IKillRule rule, int start, int end)
        {
            var periods = new List<KillEvaluationPeriod>();
            for (int i = start; i < end; i++)
            {
                try
                {
                    var killed = executor.Execute(rule, builder.BuildWithMiss(records, i, miss)).KilledBalls.Distinct().ToArray();
                    var actual = rule.BallType == BallType.Red ? records[i].RedBalls : new[] { records[i].BlueBall };
                    periods.Add(new(records[i].Period, killed.Length, killed.Count(actual.Contains)));
                }
                catch (RuleExecutionException) { periods.Add(new(records[i].Period, 0, 0, true)); }
            }
            return periods;
        }
    }
    private sealed record Counts(int Triggered, int Killed, int Correct, int Failures, int Wrong)
    {
        public double Accuracy => Killed == 0 ? 0 : (double)Correct / Killed;
    }
    private static Counts Summary(IEnumerable<KillEvaluationPeriod> p)
    {
        var a = p.ToArray(); return new(a.Count(x => x.KilledCount > 0), a.Sum(x => x.KilledCount), a.Sum(x => x.KilledCount - x.HitCount), a.Count(x => x.Failed), a.Count(x => x.HitCount > 0));
    }
    private static bool Pass(Counts c, BallType ball, int minimum) => c.Failures == 0 && c.Triggered >= minimum && c.Accuracy >= KillSettings.For(ball);
}
