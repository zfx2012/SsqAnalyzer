using System.IO;
using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

internal static class PriorityCandidateAudit
{
    public static void Run(string input, string proposalsPath, string output)
    {
        var records = KillResearchService.SnapshotRecords(File.ReadLines(input).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l =>
        {
            var p = l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return new DrawRecord { Period = int.Parse(p[0]), DrawDate = DateTime.Parse(p[1]), RedBalls = p.Skip(2).Take(6).Select(int.Parse).ToArray(), BlueBall = int.Parse(p[8]) };
        }));
        using var source = File.OpenRead(proposalsPath);
        using Stream decoded = proposalsPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new System.IO.Compression.GZipStream(source, System.IO.Compression.CompressionMode.Decompress) : source;
        using var document = JsonDocument.Parse(decoded);
        var root = document.RootElement;
        if (KillRuleDefinition.DataHash(records) != root.GetProperty("DataHash").GetString())
            throw new InvalidDataException("Candidate audit input differs from the recorded data version.");
        var builder = new RuleContextBuilder(); var miss = MissMatrixCalculator.Compute(records, records);
        var contexts = Enumerable.Range(0, records.Count + 1).Select(i => builder.BuildWithMiss(records, i, miss)).ToArray();
        var executor = new JintRuleExecutor(); int comparisons = 0, futureChecks = 0;
        var verified = new List<object>(); var ids = new HashSet<string>();
        foreach (var proposal in root.GetProperty("Proposals").EnumerateArray())
        {
            string key = proposal.GetProperty("Key").GetString()!;
            if (!ids.Add(key)) throw new InvalidDataException("Duplicate candidate key.");
            var rule = KillRule.FromJson(proposal.GetProperty("Rule").GetRawText());
            var snapshot = KillRuleDefinition.Capture(rule);
            var replay = snapshot.ToRule();
            var expected = proposal.GetProperty("ExpectedOutputs").EnumerateArray().Select(a => a.EnumerateArray().Select(b => b.GetInt32()).ToArray()).ToArray();
            if (expected.Length != 501) throw new InvalidDataException("Expected 500 settled outputs and one pending output.");
            for (int j = 0; j < expected.Length; j++)
            {
                int index = records.Count - 500 + j;
                var actual = executor.Execute(replay, contexts[index]).KilledBalls;
                if (!actual.SequenceEqual(expected[j])) throw new InvalidOperationException($"Candidate mismatch: {key}/{index}");
                comparisons++;
            }
            object Summarize(int stop)
            {
                int triggers = 0, killed = 0, correct = 0;
                for (int i = stop - 1; i >= 1 && triggers < 50; i--)
                {
                    var balls = executor.Execute(replay, contexts[i]).KilledBalls;
                    if (balls.Count == 0) continue;
                    triggers++; killed += balls.Count;
                    correct += balls.Count(b => rule.BallType == BallType.Red ? !records[i].RedBalls.Contains(b) : records[i].BlueBall != b);
                }
                return new { Triggers = triggers, Killed = killed, Correct = correct };
            }
            foreach (var (name, stop) in new[] { ("Train50", records.Count - 100), ("Latest50", records.Count) })
            {
                var measured = JsonSerializer.SerializeToElement(Summarize(stop));
                var reference = proposal.GetProperty(name);
                foreach (string field in new[] { "Triggers", "Killed", "Correct" })
                    if (measured.GetProperty(field).GetInt32() != reference.GetProperty(field).GetInt32())
                        throw new InvalidOperationException($"Candidate score mismatch: {key}/{name}/{field}");
            }
            var altered = KillResearchService.SnapshotRecords(records);
            int target = records.Count - 1;
            altered[target].RedBalls = new[] { 1, 2, 3, 4, 5, 6 }; altered[target].BlueBall = 16;
            var alteredContext = builder.Build(altered, target);
            if (!executor.Execute(replay, alteredContext).KilledBalls.SequenceEqual(executor.Execute(rule, contexts[target]).KilledBalls))
                throw new InvalidOperationException($"Future draw affects candidate: {key}");
            futureChecks++;
            verified.Add(new { Key = key, Fingerprint = snapshot.Fingerprint, Definition = snapshot,
                Latest50 = proposal.GetProperty("Latest50").Clone(), LatestBalls = expected[^1] });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var baseline = BuiltinRules.LoadAll().Select(KillRuleDefinition.Capture).ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(new { Through = records[^1].Period, DataHash = root.GetProperty("DataHash").GetString(),
            BaselineHash = KillRuleDefinition.RulesHash(baseline), BaselineDefinitions = baseline,
            BaselineFingerprints = baseline.Select(r => new { r.RuleId, r.Fingerprint }),
            IndependentComparisons = comparisons, FutureChecks = futureChecks, Candidates = verified }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Verified {verified.Count} candidates; {comparisons} independent outputs, {futureChecks} future-isolation checks.");
    }
}
