using System.IO;
using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

internal static class KillConditionTuning
{
    public static void TuneGap(string input, string output)
    {
        var records = File.ReadLines(input).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l =>
        {
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new DrawRecord { Period = int.Parse(p[0]), DrawDate = DateTime.Parse(p[1]), RedBalls = p.Skip(2).Take(6).Select(int.Parse).ToArray(), BlueBall = int.Parse(p[8]) };
        }).ToList();
        records = KillResearchService.SnapshotRecords(records);
        var executor = new JintRuleExecutor(); var builder = new RuleContextBuilder(); var miss = MissMatrixCalculator.Compute(records, records);
        var original = BuiltinRules.LoadOriginalCatalog().Single(r => r.RuleId == "B-G-R-005");
        int split = 1 + (records.Count - 1) * 70 / 100;
        var trials = new List<object>();
        foreach (int gap in new[] { 2, 3, 4 })
        {
            var condition = new KillRuleCondition("alternationGap", 0, gap, gap); var rule = condition.Apply(original);
            var observations = new List<KillEvaluationPeriod>();
            for (int i = 1; i < records.Count; i++)
            {
                var killed = executor.Execute(rule, builder.BuildWithMiss(records,i,miss)).KilledBalls;
                observations.Add(new(records[i].Period,killed.Count,killed.Count(records[i].RedBalls.Contains)));
            }
            var recent = Summarize(observations.Where(p=>p.KilledCount>0).TakeLast(50));
            trials.Add(new { Condition=condition, Recent=recent, All=Summarize(observations), Reference=Summarize(observations.Take(split-1)), LaterRetrospective=Summarize(observations.Skip(split-1)),
                Windows = new[]{30,100}.Select(n=>new {Window=n,Stats=Summarize(observations.Where(p=>p.KilledCount>0).TakeLast(n))}).ToArray() });
            Console.WriteLine($"gap {gap}: {recent.Accuracy:P2}, {recent.Triggered} triggers, all {Summarize(observations).Triggered}");
        }
        File.WriteAllText(output,JsonSerializer.Serialize(trials,new JsonSerializerOptions {WriteIndented=true}));
    }
    private sealed record Score(int Triggered, int Killed, int Correct, int Wrong, int? First, int? Last)
    {
        public double Accuracy => Killed == 0 ? 0 : (double)Correct / Killed;
    }
    public static void Run(string input, string output, string? secondPassIds = null, int gap = 0)
    {
        Directory.CreateDirectory(output);
        var records = KillResearchService.SnapshotRecords(File.ReadLines(input).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l =>
        {
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new DrawRecord { Period = int.Parse(p[0]), DrawDate = DateTime.Parse(p[1]), RedBalls = p.Skip(2).Take(6).Select(int.Parse).ToArray(), BlueBall = int.Parse(p[8]) };
        }));
        var miss = MissMatrixCalculator.Compute(records, records); var builder = new RuleContextBuilder(); var executor = new JintRuleExecutor();
        int split = 1 + (records.Count - 1) * 70 / 100;
        var frequencies = new Dictionary<int, int[,]>();
        foreach (int window in new[] { 10, 20, 50, 100 })
        {
            var counts = new int[records.Count, 34];
            for (int i = 1; i < records.Count; i++)
            {
                for (int b = 1; b <= 33; b++) counts[i, b] = counts[i - 1, b];
                foreach (int b in records[i - 1].RedBalls) counts[i, b]++;
                if (i > window) foreach (int b in records[i - window - 1].RedBalls) counts[i, b]--;
            }
            frequencies.Add(window, counts);
        }
        var candidates = new List<KillRuleCondition>();
        foreach (var (min, max) in new[] { (0,0),(1,3),(4,8),(9,99),(0,3),(0,8),(1,99),(3,99),(6,99),(12,99),(0,1),(0,5) }) candidates.Add(new("miss", 0, min, max));
        foreach (var (min, max) in new[] { (0,1),(2,3),(4,5),(6,20),(0,3),(2,20),(0,2),(0,4),(3,20),(4,20),(5,20),(0,5) }) candidates.Add(new("frequency", 20, min, max));
        foreach (var (min, max) in new[] { (0,5),(6,10),(11,50),(0,8),(0,10),(6,50),(8,50),(10,50) }) candidates.Add(new("frequency", 50, min, max));
        foreach (var (min, max) in new[] { (1,1),(2,2),(3,33),(1,2),(2,33) }) candidates.Add(new("count", 0, min, max));
        if (secondPassIds is not null)
        {
            foreach (var (min, max) in new[] { (0,0),(1,1),(2,2),(3,3),(4,10),(0,1),(0,2),(1,10),(2,10),(3,10) }) candidates.Add(new("frequency", 10, min, max));
            foreach (var (min, max) in new[] { (0,12),(13,18),(19,25),(26,100),(0,18),(0,22),(13,100),(18,100),(22,100) }) candidates.Add(new("frequency", 100, min, max));
        }
        if (gap > 0) candidates = candidates.Select(c => c with { Gap = gap }).ToList();
        var results = new List<object>(); var selected = new Dictionary<string, KillRuleCondition>();
        foreach (var catalogRule in BuiltinRules.LoadOriginalCatalog())
        {
            if (secondPassIds is not null && !secondPassIds.Split(',').Contains(catalogRule.RuleId)) continue;
            var rule = gap > 0 ? new KillRuleCondition("alternationGap", 0, gap, gap).Apply(catalogRule) : catalogRule;
            var raw = new int[records.Count][]; raw[0] = Array.Empty<int>();
            for (int i = 1; i < records.Count; i++) raw[i] = executor.Execute(rule, builder.BuildWithMiss(records, i, miss)).KilledBalls.ToArray();
            var original = Observations(raw); var before = Summarize(original.Where(p => p.KilledCount > 0).TakeLast(50));
            var trials = new List<(KillRuleCondition Condition, Score Recent, Score Reference, Score All, int[][] Output)>();
            if (before.Accuracy < KillSettings.For(rule.BallType) || before.Triggered < 50)
            {
                foreach (var condition in candidates)
                {
                    var filtered = new int[records.Count][]; filtered[0] = Array.Empty<int>();
                    for (int i = 1; i < records.Count; i++) filtered[i] = raw[i].Where(b => Eligible(condition, i, b, raw[i].Length)).ToArray();
                    var observations = Observations(filtered);
                    trials.Add((condition, Summarize(observations.Where(p => p.KilledCount > 0).TakeLast(50)), Summarize(observations.Take(split - 1)), Summarize(observations), filtered));
                }
            }
            // Explicit retrospective fitting to the user-selected window, not unseen validation.
            // Rank passing conditions by older reference evidence, then retained sample size.
            var chosen = trials.Where(t => t.Recent.Triggered == 50 && t.Recent.Accuracy >= KillSettings.For(rule.BallType)
                    && t.All.Triggered >= 100 && t.Recent.Last >= records[^50].Period)
                .OrderByDescending(t => t.Reference.Triggered >= 100 && t.Reference.Accuracy >= KillSettings.For(rule.BallType))
                .ThenByDescending(t => t.All.Triggered).ThenByDescending(t => t.Reference.Accuracy).FirstOrDefault();
            if (chosen.Condition is not null)
            {
                var revised = chosen.Condition.Apply(rule);
                // Verify the deployed JS condition against independently computed search results on every draw.
                for (int i = 1; i < records.Count; i++)
                    if (!executor.Execute(revised, builder.BuildWithMiss(records, i, miss)).KilledBalls.SequenceEqual(chosen.Output[i]))
                        throw new InvalidOperationException($"JS parity mismatch {rule.RuleId} {records[i].Period}");
                selected.Add(rule.RuleId, chosen.Condition);
            }
            var final = chosen.Condition is null ? original : Observations(chosen.Output);
            results.Add(new { rule.RuleId, rule.Name, Before = before, Condition = chosen.Condition, After = Summarize(final.Where(p => p.KilledCount > 0).TakeLast(50)),
                Reference = Summarize(final.Take(split - 1)), LaterRetrospective = Summarize(final.Skip(split - 1)), All = Summarize(final),
                Windows = new[] {30,100}.Select(n => new {Window=n, Stats=Summarize(final.Where(p=>p.KilledCount>0).TakeLast(n))}).ToArray(),
                Trials = trials.Select(t => new { t.Condition, t.Recent, t.Reference, t.All }).ToArray() });
            Console.WriteLine($"{rule.RuleId}: {before.Accuracy:P2} -> {Summarize(final.Where(p=>p.KilledCount>0).TakeLast(50)).Accuracy:P2} {chosen.Condition?.Label ?? (trials.Count == 0 ? "unchanged" : "no qualifying condition")}");
            File.WriteAllText(Path.Combine(output, "conditions.json"), JsonSerializer.Serialize(new { evaluatedThrough = records[^1].Period, dataHash = KillRuleDefinition.DataHash(records), retired = Array.Empty<string>(), conditions = selected }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { DataHash = KillRuleDefinition.DataHash(records), Count = records.Count, Last = records[^1].Period,
                ReferenceEnd = records[split - 1].Period, LaterStart = records[split].Period, CandidateCount = candidates.Count, Results = results }, new JsonSerializerOptions { WriteIndented = true }));

            List<KillEvaluationPeriod> Observations(int[][] kills) => Enumerable.Range(1, records.Count - 1).Select(i => new KillEvaluationPeriod(records[i].Period, kills[i].Length,
                kills[i].Count(b => rule.BallType == BallType.Red ? records[i].RedBalls.Contains(b) : records[i].BlueBall == b))).ToList();
        }
        bool Eligible(KillRuleCondition c, int i, int b, int count)
        {
            if (c.Feature == "frequency" && i < c.Window) return false;
            int value = c.Feature == "miss" ? miss[i - 1, b - 1] : c.Feature == "count" ? count : frequencies[c.Window][i, b];
            return value >= c.Minimum && value <= c.Maximum;
        }
    }
    private static Score Summarize(IEnumerable<KillEvaluationPeriod> source)
    {
        var a = source.ToArray(); var triggered = a.Where(p => p.KilledCount > 0).ToArray();
        return new(triggered.Length, a.Sum(p=>p.KilledCount), a.Sum(p=>p.KilledCount-p.HitCount), a.Count(p=>p.HitCount>0), triggered.FirstOrDefault()?.Period, triggered.LastOrDefault()?.Period);
    }
}
