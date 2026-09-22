namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 回测引擎（架构设计 §3.6 + §4.2 时序图）。
/// per-ball pooled 概率算法（Q3 决策）：Σ对球 / Σ杀球，红蓝独立计算。
/// 门槛联动：回测完成后调 <see cref="IRuleRepository.SaveBacktestStats"/> 持久化快照，
/// 并按 §3.5/§3.6 注解执行准确率门槛判定（accuracy &lt; MinAccuracy 且 !ForceEnabled → IsEnabled=false）。
/// </summary>
public interface IBacktestEngine
{
    /// <summary>对单规则按指定窗口回测，返回统计。回测结果会写入 rule.BacktestStats 并持久化。</summary>
    BacktestStat Run(IKillRule rule, BacktestWindow window, CancellationToken ct = default);

    /// <summary>批量回测并刷新所有规则的 BacktestStats。每条规则回测后触发门槛联动 + 进度回传。</summary>
    Task RunAllAsync(BacktestWindow window, IProgress<(int done, int total)>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 全量回测（T13a）：一键对全部规则跑指定 N（默认 100），刷新所有规则的概率显示。
    /// targetN → BacktestWindow 映射（100→Last100Triggers, 50→Last50Triggers, 30→Last30Triggers, 0/其他→All），
    /// 语义等价于 RunAllAsync(window)，但提供"全量回测"按钮的独立入口与默认值。
    /// </summary>
    Task BacktestAll(int targetN = 100, IProgress<(int done, int total)>? progress = null, CancellationToken ct = default);
}
