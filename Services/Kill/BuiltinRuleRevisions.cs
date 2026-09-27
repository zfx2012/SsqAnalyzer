using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SsqAnalyzer.Services.Kill;

/// <summary>Explicit condition revisions and optional retirement manifest; never tunes conditions at runtime.</summary>
internal static class BuiltinRuleRevisions
{
    internal sealed record GeometryCondition(int Direction = 0, int MinimumGap = 1, int? MissThreshold = null)
    {
        internal string Label => MissThreshold is { } threshold ? $"累计遗漏阈值改为{threshold}行"
            : (Direction == 0 ? "左右镜像均识别" : Direction > 0 ? "仅识别向右的相邻角" : "仅识别向左的相邻角") + $"，B到最新行至少间隔{MinimumGap}行";
        internal KillRule Apply(KillRule rule)
        {
            var node = JsonNode.Parse(rule.ToJson())!.AsObject();
            if (MissThreshold is { } threshold) node["params"]!["missThreshold"] = threshold;
            else
            {
                string code = rule.JsCode;
                if (Direction != 0) code = code.Replace("var delta = -1; delta <= 1; delta += 2", $"var delta = {Direction}; delta <= {Direction}; delta += 2", StringComparison.Ordinal);
                code = code.Replace("if (miss < 1) continue;", $"if (miss < {MinimumGap}) continue;", StringComparison.Ordinal);
                node["jsCode"] = code;
            }
            node["description"] = rule.Description + "；图形修订：" + Label + "。近期成绩参与历史优化，尚未前向验证。";
            return KillRule.FromJson(node.ToJsonString());
        }
    }
    internal static GeometryCondition? FindGeometryCondition(string ruleId)
    {
        using var stream = typeof(BuiltinRuleRevisions).Assembly.GetManifestResourceStream("SsqAnalyzer.Resources.builtin-rule-revisions.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.TryGetProperty("geometryConditions", out var conditions) && conditions.TryGetProperty(ruleId, out var value)
            ? JsonSerializer.Deserialize<GeometryCondition>(value.GetRawText()) : null;
    }
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
        var geometry = document.RootElement.TryGetProperty("geometryConditions", out var shapes)
            ? shapes.EnumerateObject().ToDictionary(p => p.Name, p => JsonSerializer.Deserialize<GeometryCondition>(p.Value.GetRawText())!)
            : new Dictionary<string, GeometryCondition>();
        if (geometry.Any(p => !ids.Contains(p.Key) || p.Value is null || p.Value.Direction is < -1 or > 1 || p.Value.MinimumGap < 1
            || (p.Value.MissThreshold is not null ? !p.Key.StartsWith("B-G-R-001", StringComparison.Ordinal) && p.Key != "B-G-B-001" : !p.Key.StartsWith("B-G-R-008", StringComparison.Ordinal))))
            throw new InvalidDataException("图形修订清单无效。");
        return original.Where(r => !excluded.Contains(r.RuleId)).Select(r => conditions.TryGetValue(r.RuleId, out var condition) ? condition.Apply(r) : r)
            .Select(r => geometry.TryGetValue(r.RuleId, out var condition) ? condition.Apply(r) : r).ToArray();
    }
}
