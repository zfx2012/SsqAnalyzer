namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 单规则一次执行的结果。Triggered 由 KilledBalls 非空推导，不需要 JS 显式声明。
/// </summary>
public sealed class KillResult
{
    public required string RuleId { get; init; }
    public required BallType BallType { get; init; }
    public IReadOnlyList<int> KilledBalls { get; init; } = Array.Empty<int>();
    public string Reason { get; init; } = "";          // 引擎推断或 JS 写入
    public bool Triggered => KilledBalls.Count > 0;
    public TimeSpan Elapsed { get; init; }
}
