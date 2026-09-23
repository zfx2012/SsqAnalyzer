using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 内置规则静态加载器。
/// 从嵌入式资源 SsqAnalyzer.Resources.builtin-rules.json 加载已通过回测初选的内置规则。
/// 资源缺失属致命错误，加载失败抛 InvalidOperationException。
/// 内置规则在进程内单例缓存，回测统计挂到 KillRule.BacktestStats（不持久化，见 §7.5）。
/// </summary>
public static class BuiltinRules
{
    private const string PrimaryResourceName = "SsqAnalyzer.Resources.builtin-rules.json";
    private const string ResourceSuffix = "builtin-rules.json";

    private static readonly IReadOnlyList<KillRule> _cache = LoadInternal();
    private static readonly IReadOnlyList<KillRule> _active = LoadActive();

    private static IReadOnlyList<KillRule> LoadActive()
    {
        using var stream = typeof(BuiltinRules).Assembly.GetManifestResourceStream("SsqAnalyzer.Resources.builtin-rules-extra.json")
            ?? throw new InvalidOperationException("新增内置规则资源缺失。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var extra = ParseJson(reader.ReadToEnd(), "builtin-rules-extra.json");
        var active = BuiltinRuleRevisions.Apply(_cache).Concat(extra).ToArray();
        if (active.Select(r => r.RuleId).Distinct(StringComparer.Ordinal).Count() != active.Length
            || extra.Any(r => !r.IsBuiltin || r.ForceEnabled))
            throw new InvalidOperationException("新增内置规则存在重复 ID 或无效属性。");
        return active;
    }

    /// <summary>加载全部内置规则（单例缓存，进程内不变）。</summary>
    public static IReadOnlyList<KillRule> LoadAll() => _active;
    // Keep original definitions available to reproduce the revision audit and verify archived algorithms.
    internal static IReadOnlyList<KillRule> LoadOriginalCatalog() => _cache;

    /// <summary>随机选择一个号码进行杀号时，该号码不开出的理论概率。</summary>
    public static double RandomKillAccuracy(BallType ballType) => ballType switch
    {
        BallType.Red => 27.0 / 33.0,
        BallType.Blue => 15.0 / 16.0,
        _ => throw new ArgumentOutOfRangeException(nameof(ballType))
    };

    /// <summary>样本充足且表现同时超过用户门槛和随机基准，才进入内置初选。</summary>
    public static bool IsCuratedCandidate(BallType ballType, BacktestWindowStat stat, double minAccuracy) =>
        stat.IsUsable
        && stat.Accuracy > Math.Max(minAccuracy, RandomKillAccuracy(ballType));

    private static IReadOnlyList<KillRule> LoadInternal()
    {
        var asm = Assembly.GetExecutingAssembly();
        Stream? stream = asm.GetManifestResourceStream(PrimaryResourceName);
        string? usedName = PrimaryResourceName;

        // 回退：按后缀搜索（防止 RootNamespace/LogicalName 命名差异）
        if (stream is null)
        {
            var allNames = asm.GetManifestResourceNames();
            var match = Array.Find(allNames, n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal));
            if (match is null)
            {
                throw new InvalidOperationException(
                    $"内置规则嵌入式资源缺失：未找到 {PrimaryResourceName}" +
                    (allNames.Length == 0 ? "（程序集无任何嵌入式资源）" : $"（已检索 {allNames.Length} 个资源：{string.Join(", ", allNames)}）"));
            }
            stream = asm.GetManifestResourceStream(match);
            usedName = match;
        }

        try
        {
            using var reader = new StreamReader(stream!, Encoding.UTF8);
            var json = reader.ReadToEnd();
            return ExpandScopedVariants(ParseJson(json, usedName!));
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private static IReadOnlyList<KillRule> ParseJson(string json, string resourceName)
    {
        List<KillRule> result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"内置规则资源 {resourceName} 根元素必须是数组");
            result = new List<KillRule>(doc.RootElement.GetArrayLength());
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                result.Add(KillRule.FromJson(item.GetRawText()));
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"内置规则资源 {resourceName} JSON 解析失败：{ex.Message}", ex);
        }

        if (result.Count == 0)
            throw new InvalidOperationException($"内置规则资源 {resourceName} 不含任何规则");

        return result;
    }

    private static IReadOnlyList<KillRule> ExpandScopedVariants(IReadOnlyList<KillRule> baseRules)
    {
        var result = new List<KillRule>(baseRules.Count * 4);
        result.AddRange(baseRules);

        var scopes = new[]
        {
            (Key: "samePeriod", Label: "历史同期图", Suffix: "HS"),
            (Key: "parity", Label: "奇偶图", Suffix: "OE"),
            (Key: "cycle", Label: "周期图", Suffix: "CY")
        };

        foreach (var rule in baseRules.Where(rule => rule.IsBuiltin && rule.BallType == BallType.Red))
        foreach (var scope in scopes)
        {
            string description = rule.Description;
            int colon = description.IndexOf('：');
            description = $"{scope.Label}：{(colon >= 0 ? description[(colon + 1)..].TrimStart() : description)}";
            result.Add(new KillRule
            {
                RuleId = $"{rule.RuleId}-{scope.Suffix}",
                Name = $"{rule.Name}（{scope.Label}）",
                Category = rule.Category,
                SubCategory = scope.Label,
                BallType = rule.BallType,
                IsBuiltin = true,
                IsEnabled = true,
                IsVisible = rule.IsVisible,
                MinAccuracy = rule.MinAccuracy,
                ForceEnabled = false,
                JsCode = ScopeJs(rule.JsCode, scope.Key),
                Params = new Dictionary<string, object>(rule.Params),
                Description = description,
                Tags = new List<string>(rule.Tags) { scope.Label },
                Source = rule.Source,
                CreatedAt = rule.CreatedAt
            });
        }

        return result;
    }

    private static string ScopeJs(string jsCode, string scope)
    {
        string quoted = $"'{scope}'";
        return jsCode
            .Replace("ctx.latestRecord", $"ctx.latestFor({quoted})", StringComparison.Ordinal)
            .Replace("ctx.history(", $"ctx.historyFor({quoted}, ", StringComparison.Ordinal)
            .Replace("ctx.getMissValues(", $"ctx.getMissValuesFor({quoted}, ", StringComparison.Ordinal)
            .Replace("ctx.getPreviousRedOccurrenceDistance(",
                $"ctx.getPreviousRedOccurrenceDistanceFor({quoted}, ", StringComparison.Ordinal);
    }
}
