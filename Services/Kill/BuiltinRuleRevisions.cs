using System.IO;
using System.Text.Json;

namespace SsqAnalyzer.Services.Kill;

/// <summary>Reviewed retirement manifest; retired definitions remain for audit, outside the repository.</summary>
internal static class BuiltinRuleRevisions
{
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
        return original.Where(r => !excluded.Contains(r.RuleId)).ToArray();
    }
}
