using System.IO;
using System.Text.Json;

namespace SsqAnalyzer.Services.Kill;

/// <summary>Explicit condition revisions and optional retirement manifest; never tunes conditions at runtime.</summary>
internal static class BuiltinRuleRevisions
{
    internal static KillRuleCondition? FindCondition(string ruleId)
    {
        using var stream = typeof(BuiltinRuleRevisions).Assembly.GetManifestResourceStream("SsqAnalyzer.Resources.builtin-rule-revisions.json")
            ?? throw new InvalidOperationException("内置规则修订清单缺失。");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("conditions").TryGetProperty(ruleId, out var value)
            ? JsonSerializer.Deserialize<KillRuleCondition>(value.GetRawText()) : null;
    }

    internal static IReadOnlyList<KillRule> Apply(IReadOnlyList<KillRule> original)
    {
        using var stream = typeof(BuiltinRuleRevisions).Assembly.GetManifestResourceStream("SsqAnalyzer.Resources.builtin-rule-revisions.json")
            ?? throw new InvalidOperationException("内置规则修订清单缺失。");
        using var document = JsonDocument.Parse(stream);
        var retired = document.RootElement.GetProperty("retired").EnumerateArray().Select(v => v.GetString()!).ToArray();
        var ids = original.Select(r => r.RuleId).ToHashSet(StringComparer.Ordinal);
        if (retired.Distinct(StringComparer.Ordinal).Count() != retired.Length || retired.Any(id => !ids.Contains(id)))
            throw new InvalidDataException("内置规则修订清单存在重复或无效条目。");
        var excluded = retired.ToHashSet(StringComparer.Ordinal);
        var conditions = document.RootElement.TryGetProperty("conditions", out var entries)
            ? entries.EnumerateObject().ToDictionary(p => p.Name, p => JsonSerializer.Deserialize<KillRuleCondition>(p.Value.GetRawText())!)
            : new Dictionary<string, KillRuleCondition>();
        if (conditions.Any(p => !ids.Contains(p.Key) || excluded.Contains(p.Key) || p.Value is null))
            throw new InvalidDataException("规则条件清单存在无效条目。");
        return original.Where(r => !excluded.Contains(r.RuleId)).Select(r => conditions.TryGetValue(r.RuleId, out var condition) ? condition.Apply(r) : r).ToArray();
    }
}
