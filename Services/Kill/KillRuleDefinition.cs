using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services.Kill;

/// <summary>A value snapshot independent of mutable repository enablement and later edits.</summary>
public sealed record KillRuleDefinition(string RuleId, string Name, BallType BallType, RuleCategory Category, string Code, string Parameters)
{
    public static KillRuleDefinition Capture(IKillRule rule) => new(rule.RuleId, rule.Name, rule.BallType, rule.Category, rule.JsCode,
        JsonSerializer.Serialize(rule.Params.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value)));
    public KillRule ToRule() => new()
    {
        RuleId = RuleId, Name = Name, BallType = BallType, Category = Category, JsCode = Code,
        Params = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Parameters)!.ToDictionary(p => p.Key, p => (object)p.Value)
    };
    public string Fingerprint => Hash(JsonSerializer.Serialize(this with { Name = "" }, Options));
    // Explicit DTO avoids serializing computed Fingerprint recursively.
    private static readonly JsonSerializerOptions Options = new() { IgnoreReadOnlyProperties = true };
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string RulesHash(IEnumerable<KillRuleDefinition> rules) => Hash(string.Join("\n", rules.OrderBy(r => r.RuleId, StringComparer.Ordinal).Select(r => r.Fingerprint)));
    public static string DataHash(IEnumerable<DrawRecord> records) => Hash(string.Join("\n", records.Select(r =>
        $"{r.Period}|{r.DrawDate:yyyy-MM-dd}|{string.Join(',', r.RedBalls)}|{r.BlueBall}")));
}
