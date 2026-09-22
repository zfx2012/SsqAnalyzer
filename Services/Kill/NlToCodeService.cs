using System.Text.Json;
using SsqAnalyzer.Services;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// <see cref="INlToCodeService"/> 默认实现。
/// 走任意 OpenAI 兼容端点：POST {baseUrl}/chat/completions
/// 请求体 {model, messages:[{system},{user}], temperature}；响应解析 choices[0].message.content。
/// baseUrl 由调用方提供（如 https://api.deepseek.com/v1），不再硬编码 DashScope。
/// </summary>
public sealed class NlToCodeService : INlToCodeService
{
    private readonly AiAnalysisService _ai;

    public NlToCodeService(AiAnalysisService ai)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
    }

    public (string System, string User) BuildPrompt(string nlDescription, BallType ballType, RuleCategory category)
    {
        if (string.IsNullOrWhiteSpace(nlDescription))
            throw new ArgumentException("自然语言描述不能为空", nameof(nlDescription));

        var system = BuildSystemPrompt();
        var ballRange = ballType == BallType.Red ? "红球 1-33" : "蓝球 1-16";
        var catLabel = category switch
        {
            RuleCategory.Pattern => "图形",
            RuleCategory.Formula => "公式",
            _ => "其他"
        };

        var user = $@"请生成一条双色球杀号规则代码。
- 球种：{(ballType == BallType.Red ? "红球" : "蓝球")}（返回 {ballRange} 的整数）
- 类别：{catLabel}
- 自然语言描述：{nlDescription.Trim()}

只输出一个 ```js 代码块，不要任何解释文字。";
        return (system, user);
    }

    public string? ExtractJsCode(string llmContent)
    {
        if (string.IsNullOrWhiteSpace(llmContent)) return null;

        // 1) 优先匹配 ```js / ```javascript / ``` 围栏
        var fenced = TryExtractFenced(llmContent);
        if (fenced is not null) return fenced.Trim();

        // 2) fallback：直接包含 function getKillBalls —— 取从该关键字到末尾
        var idx = llmContent.IndexOf("function getKillBalls", StringComparison.Ordinal);
        if (idx >= 0) return llmContent[idx..].Trim();

        return null;
    }

    public async Task<string> GenerateAsync(
        string nlDescription, BallType ballType, RuleCategory category,
        string apiKey, string model, string? baseUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("未配置 API Key，请先在「设置」中配置 LLM API");
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("未配置模型名，请先在「设置」中配置 LLM 模型");
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("未配置 LLM BaseUrl，请先在「设置 → LLM API 配置」中配置");

        var (system, user) = BuildPrompt(nlDescription, ballType, category);
        var body = BuildRequestBody(model, system, user);
        var endpoint = BuildEndpoint(baseUrl!);

        var raw = await _ai.CurlPostAsync(apiKey, body, endpoint, ct);
        var content = ParseAssistantContent(raw);
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("LLM 返回内容为空，无法抽取代码");

        var js = ExtractJsCode(content);
        if (js is null || !js.Contains("getKillBalls", StringComparison.Ordinal))
            throw new InvalidOperationException("未能从 LLM 响应中抽取有效的 getKillBalls 代码");

        return js;
    }

    // ==================== 内部 ====================

    private static string BuildSystemPrompt() => @"你是一个双色球杀号规则代码生成器。根据用户的自然语言描述，生成可在沙箱执行的 JavaScript 函数 getKillBalls(ctx)。

【函数签名】
function getKillBalls(ctx) {
  // 计算逻辑（只能用 ctx 提供的历史数据，绝不能偷看被预测期）
  return [要杀的球号];  // number[]，红球 1-33，蓝球 1-16
}

【ctx 对象 API】（只读，全部为历史数据，绝不包含被预测期）
- ctx.params: 对象，规则参数（用户规则一般为空 {}）
- ctx.latestRecord: 最近一期开奖记录，或 null。结构：{ period, drawDate, redBalls:[], blueBall, redSum, redSpan, oddCount, evenCount, zoneLabel, bigSmallLabel, primeLabel, zo2Label, linkCount }
- ctx.history(n): 返回最近 n 期记录数组（升序），每条结构同 latestRecord
- ctx.getMiss(ball, window): 返回指定球（红球 1-33，蓝球 34-49）在最近 window 期的遗漏值（int）
- ctx.getMissValues(window): 返回 [{ball, value}]，红球 1-33 的遗漏值数组
- ctx.getSamePeriodRecords(suffix3): 历史同期（期号后3位=suffix3）记录数组
- ctx.getCycleRecords(cycleType): 指定周期（""Tuesday""/""Thursday""/""Sunday""/""None""）记录数组
- ctx.getParityRecords(parityType): 指定奇偶（""Odd""/""Even""/""None""）记录数组
- ctx.currentCycle / ctx.currentParity / ctx.currentShortPeriodSuffix: 当前期属性

【硬约束】
1. 必须定义顶层函数 getKillBalls(ctx)，返回 number[]
2. 红球返回 1-33 的整数，蓝球返回 1-16 的整数，不要返回越界值
3. 必须确定性：禁止 Math.random / Date.now / 网络 / 文件操作（沙箱已禁用 Math.random）
4. 杀号意味着""排除""，杀错会扣分；不确信就返回空数组 []
5. 单次执行 < 5 秒，内存 < 16MB

【输出格式】
只输出一个 JavaScript 代码块，用 ```js 围栏包裹，不要任何解释文字。

【示例】
用户：从遗漏值≥12的红球中只杀最冷的1个
```js
function getKillBalls(ctx) {
  var miss = ctx.getMissValues(30);
  var best = null;
  for (var i = 0; i < miss.length; i++) {
    if (miss[i].value >= 12 && (best === null || miss[i].value > best.value)) best = miss[i];
  }
  return best === null ? [] : [best.ball];
}
```";

    private static string BuildRequestBody(string model, string system, string user)
    {
        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            },
            temperature = 0.2
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        });
    }

    private static string BuildEndpoint(string baseUrl)
        => $"{baseUrl.Trim().TrimEnd('/')}/chat/completions";

    /// <summary>解析 OpenAI 兼容响应 choices[0].message.content；解析失败返回空。</summary>
    private static string ParseAssistantContent(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                return "";
            foreach (var choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("message", out var msg)) continue;
                if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    return content.GetString() ?? "";
            }
            return "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>抽取 ```js / ```javascript / ``` 围栏内第一个代码块；无围栏返回 null。</summary>
    private static string? TryExtractFenced(string text)
    {
        int start = text.IndexOf("```", StringComparison.Ordinal);
        while (start >= 0)
        {
            // 跳过可能的语言标识（js / javascript / 空）
            int lineEnd = text.IndexOf('\n', start);
            if (lineEnd < 0) return null;
            int contentStart = lineEnd + 1;

            int end = text.IndexOf("```", contentStart, StringComparison.Ordinal);
            if (end < 0) return null;

            var code = text[contentStart..end];
            // 验证确实是 getKillBalls 代码块（避免误取示例之外的围栏）
            if (code.Contains("getKillBalls", StringComparison.Ordinal))
                return code;

            // 继续找下一个围栏
            start = text.IndexOf("```", end + 3, StringComparison.Ordinal);
        }
        return null;
    }
}
