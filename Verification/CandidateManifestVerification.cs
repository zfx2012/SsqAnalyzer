using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SsqAnalyzer.Services.Kill;

internal static class CandidateManifestVerification
{
    public static void Run(string path)
    {
        var variants = CandidateForwardRunner.ReadVariants(path);
        foreach (var variant in variants)
            foreach (var rule in variant.Rules)
                if (KillRuleDefinition.Capture(rule.ToRule()).Fingerprint != rule.Fingerprint)
                    throw new InvalidOperationException("Frozen definition round trip changed its fingerprint.");
        string directory = Path.Combine(Path.GetTempPath(), "ssq-candidate-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "manifest.json");
        string dataPath = Path.Combine(directory, "history.txt");
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path))!;
            root["Variants"]![1]!["Overrides"]![0]!["Code"] = "function getKillBalls(ctx) { return [1]; }";
            File.WriteAllText(temporary, root.ToJsonString());
            Reject(temporary);
            File.WriteAllText(dataPath, "2026001 2026-01-06 01 02 03 04 05 06 01\n");
            string unopened = Path.Combine(directory, "must-not-create");
            bool staleRejected = false;
            try { CandidateForwardRunner.Run(path, dataPath, unopened, "freeze", () => new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)); }
            catch (InvalidOperationException) { staleRejected = true; }
            if (!staleRejected || Directory.Exists(unopened)) throw new InvalidOperationException("Stale data created a forward record.");
            root = JsonNode.Parse(File.ReadAllText(path))!;
            var entry = root["Variants"]![1]!;
            var definition = variants.Single(v => v.Key == "blue-right").Rules.Single(r => r.RuleId == "B-S-B-022") with { RuleId = "B-G-R-001" };
            entry["Overrides"]![0] = JsonSerializer.SerializeToNode(definition, new JsonSerializerOptions { IgnoreReadOnlyProperties = true });
            var changed = variants.Single(v => v.Key == "baseline").Rules.Select(r => r.RuleId == definition.RuleId ? definition : r).ToArray();
            entry["RuleSetHash"] = KillRuleDefinition.RulesHash(changed);
            File.WriteAllText(temporary, root.ToJsonString());
            Reject(temporary);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(dataPath)) File.Delete(dataPath);
            Directory.Delete(directory);
        }
        Console.WriteLine("PASS four frozen variants, definition round trip, hash tampering, unexpected overrides and stale data rejected before writes.");
    }

    private static void Reject(string path)
    {
        try { CandidateForwardRunner.ReadVariants(path); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Tampered manifest was accepted.");
    }
}
