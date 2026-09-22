namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 单个被杀号码的溯源链：哪些规则杀了它、各自的回测准确率。
/// </summary>
public sealed record BallRuleTrace(
    string RuleId,
    string RuleName,
    RuleCategory Category,
    string Reason,
    double BacktestAccuracy);
