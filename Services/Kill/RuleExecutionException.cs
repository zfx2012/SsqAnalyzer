namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 规则执行异常。Jint 报错经包装后抛出，避免向用户泄露 JS 堆栈。
/// </summary>
public sealed class RuleExecutionException : Exception
{
    public string RuleId { get; }
    public RuleExecutionException(string ruleId, string message, Exception? inner = null)
        : base(message, inner)
        => RuleId = ruleId;
}
