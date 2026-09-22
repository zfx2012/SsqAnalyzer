namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 回测窗口：取最近 N 次触发。窗口语义为「触发次数」而非「期数」。
/// </summary>
public enum BacktestWindow
{
    Last30Triggers,
    Last50Triggers,
    Last100Triggers,
    All
}
