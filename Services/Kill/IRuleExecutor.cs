namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 规则执行器抽象（可 mock）。JintRuleExecutor 为默认实现。
/// </summary>
public interface IRuleExecutor
{
    /// <summary>在指定上下文中执行规则 JS，返回杀号列表；执行失败抛 RuleExecutionException。</summary>
    KillResult Execute(IKillRule rule, RuleContext ctx);
}
