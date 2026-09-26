using System.IO;
using System.Text;
using System.Text.Json;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 杀号规则实体（实现 IKillRule）。
/// 字段命名与 builtin-rules.json schema 一致；MinAccuracy 保留用于旧文件兼容；实际判定由 KillSettings 按球种固定。
/// </summary>
public sealed class KillRule : IKillRule
{
    public required string RuleId { get; init; }
    public required string Name { get; init; }
    public required RuleCategory Category { get; init; }
    public string? SubCategory { get; init; }
    public required BallType BallType { get; init; }
    public bool IsBuiltin { get; init; }
    public bool IsEnabled { get; set; } = true;
    public bool IsVisible { get; set; } = true;           // 是否在杀号页列表显示（独立于 IsEnabled）
    public double MinAccuracy { get; init; } = 0.80;     // 旧文件兼容字段，不参与当前门槛判定
    public bool ForceEnabled { get; set; }
    public required string JsCode { get; init; }
    public Dictionary<string, object> Params { get; init; } = new();
    IReadOnlyDictionary<string, object> IKillRule.Params => Params;
    public string Description { get; init; } = "";
    public List<string> Tags { get; init; } = new();
    IReadOnlyList<string> IKillRule.Tags => Tags;
    public string Source { get; init; } = "builtin";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public BacktestStatsSnapshot? BacktestStats { get; set; }

    /// <summary>从 JSON 反序列化（schema 见架构设计 §1.4）。object 字段需要手动解析。</summary>
    public static KillRule FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var rule = new KillRule
        {
            RuleId = root.GetProperty("ruleId").GetString()!,
            Name = root.GetProperty("name").GetString()!,
            Category = Enum.Parse<RuleCategory>(root.GetProperty("category").GetString()!, true),
            SubCategory = root.TryGetProperty("subCategory", out var sc) && sc.ValueKind == JsonValueKind.String
                ? sc.GetString() : null,
            BallType = Enum.Parse<BallType>(root.GetProperty("ballType").GetString()!, true),
            IsBuiltin = root.GetProperty("isBuiltin").GetBoolean(),
            IsEnabled = root.TryGetProperty("isEnabled", out var en) ? en.GetBoolean() : true,
            IsVisible = root.TryGetProperty("isVisible", out var isVisibleEl) ? isVisibleEl.GetBoolean() : true,
            MinAccuracy = root.TryGetProperty("minAccuracy", out var ma) ? ma.GetDouble() : 0.80,
            ForceEnabled = root.TryGetProperty("forceEnabled", out var fe) ? fe.GetBoolean() : false,
            JsCode = root.GetProperty("jsCode").GetString()!,
            Description = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
            Source = root.TryGetProperty("source", out var s) ? s.GetString() ?? "builtin" : "builtin",
            CreatedAt = root.TryGetProperty("createdAt", out var ca) && ca.ValueKind == JsonValueKind.String
                ? ca.GetDateTime() : DateTime.UtcNow
        };
        if (root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in p.EnumerateObject())
            {
                rule.Params[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number when prop.Value.TryGetInt32(out var iv) => iv,
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.String => (object)prop.Value.GetString()!,
                    JsonValueKind.Array or JsonValueKind.Object => prop.Value.Clone(),
                    _ => prop.Value.GetRawText()
                };
            }
        }
        if (root.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in t.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    rule.Tags.Add(item.GetString()!);
        }
        return rule;
    }

    /// <summary>序列化为 JSON（不含 BacktestStats，避免污染规则定义）。</summary>
    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("ruleId", RuleId);
            writer.WriteString("name", Name);
            writer.WriteString("category", Category.ToString());
            writer.WriteString("subCategory", SubCategory);
            writer.WriteString("ballType", BallType.ToString());
            writer.WriteBoolean("isBuiltin", IsBuiltin);
            writer.WriteBoolean("isEnabled", IsEnabled);
            writer.WriteBoolean("isVisible", IsVisible);
            writer.WriteNumber("minAccuracy", MinAccuracy);
            writer.WriteBoolean("forceEnabled", ForceEnabled);
            writer.WriteString("jsCode", JsCode);
            writer.WriteStartObject("params");
            foreach (var kv in Params)
            {
                switch (kv.Value)
                {
                    case bool b: writer.WriteBoolean(kv.Key, b); break;
                    case int i: writer.WriteNumber(kv.Key, i); break;
                    case long l: writer.WriteNumber(kv.Key, l); break;
                    case double dd: writer.WriteNumber(kv.Key, dd); break;
                    case string ss: writer.WriteString(kv.Key, ss); break;
                    case JsonElement element: writer.WritePropertyName(kv.Key); element.WriteTo(writer); break;
                    default: writer.WriteString(kv.Key, kv.Value?.ToString()); break;
                }
            }
            writer.WriteEndObject();
            writer.WriteString("description", Description);
            writer.WriteStartArray("tags");
            foreach (var tag in Tags) writer.WriteStringValue(tag);
            writer.WriteEndArray();
            writer.WriteString("source", Source);
            writer.WriteString("createdAt", CreatedAt);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
