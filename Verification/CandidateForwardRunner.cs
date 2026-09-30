using System.IO;
using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

/// <summary>Explicit CLI for separate frozen variants; does not use the live submission or rules files.</summary>
internal static class CandidateForwardRunner
{
    public static (string Key, KillRuleDefinition[] Rules)[] ReadVariants(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("FormatVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported candidate manifest.");
        var original = JsonSerializer.Deserialize<KillRuleDefinition[]>(root.GetProperty("BaselineDefinitions").GetRawText())!;
        if (original.Length != 100 || original.Select(r => r.RuleId).Distinct().Count() != 100
            || KillRuleDefinition.RulesHash(original) != root.GetProperty("BaselineHash").GetString())
            throw new InvalidDataException("Frozen baseline definitions changed.");
        var variants = root.GetProperty("Variants").EnumerateArray().Select(v =>
        {
            string key = v.GetProperty("Key").GetString()!;
            if (key is not ("baseline" or "blue-right" or "red-wide-return" or "both"))
                throw new InvalidDataException("Unexpected variant key.");
            var overrides = JsonSerializer.Deserialize<KillRuleDefinition[]>(v.GetProperty("Overrides").GetRawText())!;
            if (overrides.Select(r => r.RuleId).Distinct().Count() != overrides.Length
                || overrides.Any(r => !original.Any(b => b.RuleId == r.RuleId))) throw new InvalidDataException("Invalid override IDs.");
            var byId = overrides.ToDictionary(r => r.RuleId);
            var rules = original.Select(r => byId.GetValueOrDefault(r.RuleId, r)).ToArray();
            if (rules.Length != 100 || rules.Select(r => r.RuleId).Distinct().Count() != rules.Length
                || KillRuleDefinition.RulesHash(rules) != v.GetProperty("RuleSetHash").GetString())
                throw new InvalidDataException("Candidate definitions or hash changed; regenerate the manifest instead of overwriting it.");
            return (Key: key, Rules: rules);
        }).ToArray();
        if (variants.Length != 4 || variants.Select(v => v.Key).Distinct().Count() != 4)
            throw new InvalidDataException("Expected four distinct variants.");
        var baseline = variants.Single(v => v.Key == "baseline").Rules.ToDictionary(r => r.RuleId);
        foreach (var variant in variants)
        {
            var expected = variant.Key switch
            {
                "baseline" => Array.Empty<string>(), "blue-right" => new[] { "B-S-B-022" },
                "red-wide-return" => new[] { "B-G-R-001-HS" }, _ => new[] { "B-S-B-022", "B-G-R-001-HS" }
            };
            if (!variant.Rules.Select(r => r.RuleId).ToHashSet().SetEquals(baseline.Keys)
                || variant.Rules.Any(r => baseline[r.RuleId].BallType != r.BallType)
                || !variant.Rules.Where(r => baseline[r.RuleId].Fingerprint != r.Fingerprint).Select(r => r.RuleId).ToHashSet().SetEquals(expected))
                throw new InvalidDataException("Candidate variant changes an unexpected rule.");
        }
        return variants;
    }

    public static void Run(string manifest, string input, string directory, string mode, Func<DateTime>? utcNow = null)
    {
        if (mode is not ("freeze" or "settle")) throw new ArgumentException("Mode must be freeze or settle.");
        var variants = ReadVariants(manifest);
        var records = KillResearchService.SnapshotRecords(File.ReadLines(input).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l =>
        {
            var p = l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return new DrawRecord { Period = int.Parse(p[0]), DrawDate = DateTime.Parse(p[1]), RedBalls = p.Skip(2).Take(6).Select(int.Parse).ToArray(), BlueBall = int.Parse(p[8]) };
        }));
        if (records.Count == 0) throw new InvalidDataException("No historical draws.");
        DateTime targetDate = KillDrawSchedule.NextDate(records[^1].DrawDate);
        if (mode == "freeze")
        {
            DateTime today = TimeZoneInfo.ConvertTimeFromUtc((utcNow ?? (() => DateTime.UtcNow))(), TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")).Date;
            if (targetDate <= today) throw new InvalidOperationException("Local history is stale for a forward freeze; update it before recording a future draw.");
        }
        int targetPeriod = KillDrawSchedule.NextPeriod(records[^1].Period, records[^1].DrawDate);
        foreach (var variant in variants)
        {
            var store = new KillForwardStore(Path.Combine(directory, variant.Key + ".json"), utcNow);
            if (mode == "freeze")
                store.Freeze(variant.Rules.Select(r => r.ToRule()), records, targetPeriod, targetDate, new JintRuleExecutor());
            else store.Settle(records);
            Console.WriteLine(variant.Key + "\n" + store.ToText());
        }
    }
}
