namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 单规则单窗口回测统计。Accuracy = Σ对球 / Σ杀球（per-ball pooled，见 §7.4）。
/// </summary>
public sealed class BacktestStat
{
    public required string RuleId { get; init; }
    public required BacktestWindow Window { get; init; }
    public required int TriggeredCount { get; init; }      // 实际触发次数（≤ 窗口大小）
    public required int KillBallCount { get; init; }       // Σ杀球数（pooled 分母）
    public required int CorrectBallCount { get; init; }    // Σ对球数（pooled 分子）
    public double Accuracy => KillBallCount == 0 ? 0 : (double)CorrectBallCount / KillBallCount;
    public const int MinimumAllTriggers = 30;
    public bool SampleInsufficient => KillBallCount == 0 || TriggeredCount < RequiredTriggers;
    public int RequiredTriggers => Window switch
    {
        BacktestWindow.Last30Triggers => 30,
        BacktestWindow.Last50Triggers => 50,
        BacktestWindow.Last100Triggers => 100,
        _ => MinimumAllTriggers
    };
    public IReadOnlyList<BacktestErrorSample> ErrorSamples { get; init; } = Array.Empty<BacktestErrorSample>();
    public DateTime RunAt { get; init; } = DateTime.UtcNow;
    /// <summary>本次回测耗时（毫秒），由 BacktestEngine 用 Stopwatch 测量。</summary>
    public long ElapsedMs { get; init; }
    public KillEvaluationMetrics? Metrics { get; init; }
    public string? RuleFingerprint { get; init; }
    public string? DataFingerprint { get; init; }
    public int EvaluatedCount { get; init; }
    public int FailureCount { get; init; }
    public string? LastExecutionError { get; init; }
    public int? FirstPeriod { get; init; }
    public int? LastPeriod { get; init; }

}
