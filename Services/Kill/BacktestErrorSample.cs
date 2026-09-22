namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 单次回测的错误样本：被杀且实际开出。用于 UI 排查规则错杀模式。
/// </summary>
public sealed record BacktestErrorSample(
    int Period,
    IReadOnlyList<int> KilledBalls,
    IReadOnlyList<int> ActualNextBalls,
    IReadOnlyList<int> HitBalls);
