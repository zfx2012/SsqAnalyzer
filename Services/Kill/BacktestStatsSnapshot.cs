namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 规则完整回测统计：30/50/100/全部四档独立保存，记录最近执行窗口。
/// 由规则仓储持久化，保留旧版无法区分窗口的统计供查看。
/// </summary>
public sealed record BacktestStatsSnapshot(
    BacktestWindowStat Window30,
    BacktestWindowStat Window50,
    BacktestWindowStat Window100,
    DateTime LastRunAt)
{
    public BacktestWindowStat WindowAll { get; init; } = BacktestWindowStat.Empty;
    public BacktestWindow? LastWindow { get; init; }
    // Preserve old ambiguous data for inspection, never treat it as a verified window.
    public BacktestWindowStat? LegacyCombinedWindow { get; init; }

    public BacktestWindowStat ForWindow(BacktestWindow window) => window switch
    {
        BacktestWindow.Last30Triggers => Window30,
        BacktestWindow.Last50Triggers => Window50,
        BacktestWindow.Last100Triggers => Window100,
        BacktestWindow.All => WindowAll,
        _ => throw new ArgumentOutOfRangeException(nameof(window))
    };

    // A failed/insufficient latest run must not fall back to older favourable results.
    public BacktestWindowStat Effective => LastWindow is { } window ? ForWindow(window)
        : Window50.IsUsable ? Window50 : Window30.IsUsable ? Window30
        : Window100.IsUsable ? Window100 : WindowAll;
}
