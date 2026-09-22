using System.Text.Json;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// LLM 提供方预设（仅支持 OpenAI 兼容协议的文本模型）。
/// 用于 LlmApiConfigDialog 的模型预设下拉与 URL 自动补全。
/// </summary>
public static class LlmProviderPresets
{
    /// <summary>单个提供方预设：Id（内部键）、显示名、默认 BaseUrl（含协议与路径前缀）。</summary>
    public sealed record Preset(string Id, string DisplayName, string DefaultBaseUrl);

    /// <summary>全部预设（顺序即为下拉顺序）。"custom" 排在最后，BaseUrl 为空由用户自填。</summary>
    public static readonly IReadOnlyList<Preset> All = new[]
    {
        new Preset("deepseek", "DeepSeek（默认）",       "https://api.deepseek.com/v1"),
        new Preset("glm",      "GLM（智谱）",            "https://open.bigmodel.cn/api/paas/v4"),
        new Preset("minimax",  "Minimax",                "https://api.minimaxi.com/v1"),
        new Preset("kimi",     "Kimi（Moonshot）",       "https://api.moonshot.cn/v1"),
        new Preset("custom",   "自定义（OpenAI 兼容）",  "")
    };

    public static Preset? FindById(string? id)
        => id is null ? null : All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>根据当前 BaseUrl 猜测最匹配的预设 Id（用于初始化时反推下拉选项）。</summary>
    public static string? GuessPresetIdByBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        var trimmed = baseUrl!.Trim().TrimEnd('/').ToLowerInvariant();
        foreach (var p in All)
        {
            if (string.IsNullOrEmpty(p.DefaultBaseUrl)) continue;
            if (trimmed == p.DefaultBaseUrl.TrimEnd('/').ToLowerInvariant()) return p.Id;
        }
        return "custom";
    }

    /// <summary>
    /// 解析 OpenAI 兼容 <c>GET /v1/models</c> 响应，提取模型 id 列表。
    /// 期望格式：<c>{"object":"list","data":[{"id":"...", ...}, ...]}</c>。
    /// 容错：data 不是数组、JSON 损坏等均返回空列表而非抛异常。
    /// </summary>
    public static List<string> ParseModelIds(string? json)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var s = id.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) result.Add(s!);
                }
            }
        }
        catch
        {
            // 解析失败：返回空列表（调用方按"获取失败"提示用户手动输入）
        }
        return result;
    }
}