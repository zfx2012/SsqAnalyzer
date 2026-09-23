namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 单窗口回测统计快照（不可变）。由 BacktestEngine 计算后挂到 IKillRule.BacktestStats。
/// </summary>
/// <remarks>
/// ErrorSamples 用 init 属性（非位置参数）+ Array.Empty 默认值：
/// ① 旧 JSON（无 errorSamples 字段）反序列化时走默认值，不崩；
/// ② 现有 new BacktestWindowStat(...) 构造点位置参数不变，无需改造；
/// ③ RuleRepository 手动解析时 TryGetProperty 容错。
/// </remarks>
public sealed record BacktestWindowStat(
    int TriggeredCount,
    int KillBallCount,
    int CorrectBallCount,
    double Accuracy,
    bool SampleInsufficient)
{
    /// <summary>错误样本（被杀且实际开出），最多 5 条；旧持久化数据无此字段时默认空列表。</summary>
    public IReadOnlyList<BacktestErrorSample> ErrorSamples { get; init; } = Array.Empty<BacktestErrorSample>();
    /// <summary>本次回测耗时（毫秒）；旧持久化数据无此字段时默认 0（详情页显示「—」）。</summary>
    public long ElapsedMs { get; init; } = 0;
    public DateTime? RunAt { get; init; }
    public KillEvaluationMetrics? Metrics { get; init; }
    public string? RuleFingerprint { get; init; }
    public string? DataFingerprint { get; init; }
    public int EvaluatedCount { get; init; }
    public int FailureCount { get; init; }
    public string? LastExecutionError { get; init; }
    public int? FirstPeriod { get; init; }
    public int? LastPeriod { get; init; }
    public bool IsUsable => !SampleInsufficient && KillBallCount > 0 && FailureCount == 0;
    public static BacktestWindowStat Empty { get; } = new(0, 0, 0, 0, true);
}
