namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 单个被杀号码的完整溯源：触发它的规则链 + 置信度。
/// 置信度由 KillEngine 在汇总阶段按"达标规则数"打标（High≥2 / Medium=1 / Low=0）。
/// </summary>
public sealed class KilledBallDetail
{
    public int Ball { get; init; }
    public BallType BallType { get; init; }
    public ConfidenceLevel Confidence { get; init; }
    public IReadOnlyList<BallRuleTrace> Traces { get; init; } = Array.Empty<BallRuleTrace>();
    public string Summary => $"{Traces.Count} 条规则杀";
}
