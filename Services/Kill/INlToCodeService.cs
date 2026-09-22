namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 自然语言 → 杀号规则 JS 代码 生成服务（架构设计 §3.3 NL→Code 三重校验）。
/// 调用 LLM（OpenAI 兼容 / DashScope compatible-mode）生成 getKillBalls(ctx) 函数。
/// </summary>
public interface INlToCodeService
{
    /// <summary>拼装给 LLM 的 (system, user) 提示文本，描述 ctx API 与硬约束。</summary>
    (string System, string User) BuildPrompt(string nlDescription, BallType ballType, RuleCategory category);

    /// <summary>
    /// 从 LLM 响应文本中抽取 JS 代码：优先 ```js/javascript 围栏，fallback 启发式找 function getKillBalls。
    /// 抽不到返回 null。
    /// </summary>
    string? ExtractJsCode(string llmContent);

    /// <summary>
    /// 调 LLM 生成代码并抽取 JS（内部完成 BuildPrompt → HTTP → 解析 content → ExtractJsCode）。
    /// 返回提取到的 JS 代码；LLM 失败或抽不到代码时抛异常。
    /// </summary>
    Task<string> GenerateAsync(
        string nlDescription, BallType ballType, RuleCategory category,
        string apiKey, string model, string? apiHost,
        CancellationToken ct = default);
}
