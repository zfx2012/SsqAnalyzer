using System.IO;
using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

internal static class CoverageAudit
{
    public static void Run(string input, string output, string? preferences)
    {
        var records = KillResearchService.SnapshotRecords(File.ReadLines(input).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l =>
        {
            var p = l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return new DrawRecord { Period = int.Parse(p[0]), DrawDate = DateTime.Parse(p[1]), RedBalls = p.Skip(2).Take(6).Select(int.Parse).ToArray(), BlueBall = int.Parse(p[8]) };
        }));
        if (records.Count < 501) throw new ArgumentException("Coverage audit requires at least 501 draws.", nameof(input));
        var enabled = new Dictionary<string, bool>();
        if (preferences is not null)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(preferences));
            if (doc.RootElement.TryGetProperty("builtinOverrides", out var overrides))
                foreach (var item in overrides.EnumerateObject())
                    if (item.Value.TryGetProperty("isEnabled", out var value)) enabled[item.Name] = value.GetBoolean();
        }
        int start = Math.Max(1, records.Count - 500);
        var builder = new RuleContextBuilder(); var miss = MissMatrixCalculator.Compute(records, records);
        var contexts = Enumerable.Range(0, records.Count + 1).Select(i => builder.BuildWithMiss(records, i, miss)).ToArray();
        var executor = new JintRuleExecutor(); var results = new List<object>();
        foreach (var rule in BuiltinRules.LoadAll())
        {
            var outputs = Enumerable.Range(start, records.Count - start + 1)
                .Select(i => executor.Execute(rule, contexts[i]).KilledBalls.ToArray()).ToArray();
            int count = 0, killed = 0, correct = 0;
            for (int i = records.Count - 1; i >= 1 && count < 50; i--)
            {
                var balls = i >= start ? outputs[i - start] : executor.Execute(rule, contexts[i]).KilledBalls.ToArray();
                if (balls.Length == 0) continue;
                count++; killed += balls.Length;
                correct += balls.Count(b => rule.BallType == BallType.Red ? !records[i].RedBalls.Contains(b) : records[i].BlueBall != b);
            }
            results.Add(new { Rule = JsonSerializer.Deserialize<JsonElement>(rule.ToJson()), Enabled = enabled.GetValueOrDefault(rule.RuleId, rule.IsEnabled),
                Triggers = count, Killed = killed, Correct = correct, Accuracy = killed > 0 ? (double)correct / killed : 0, Outputs = outputs });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Through = records[^1].Period, DataHash = KillRuleDefinition.DataHash(records), Start = start,
            History = records.Skip(start).Select(r => new { r.Period, Red = r.RedBalls, Blue = r.BlueBall }), Rules = results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Coverage replay written: {output}");
    }
}
