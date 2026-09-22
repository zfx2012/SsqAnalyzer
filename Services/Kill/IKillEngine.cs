namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 杀号执行引擎（架构设计 §3.5）。
/// 协调 IRuleRepository / IRuleContextBuilder / IRuleExecutor，对最近一期执行所有启用规则，
/// 合并被杀号码、打置信度标（门槛为红球 82%、蓝球 94%）、生成完整 <see cref="KillReport"/>。
/// </summary>
public interface IKillEngine
{
    /// <summary>用所有 IsEnabled=true 的规则对最近一期执行杀号，生成完整报告。</summary>
    KillReport Execute();

    /// <summary>指定规则子集执行杀号（用于试运行 / 单规则预览）。调用方负责过滤 IsEnabled。</summary>
    KillReport Execute(IEnumerable<IKillRule> rules);

    /// <summary>以指定回测窗口评价报告；不回退到其他窗口。null 使用最近一次回测。</summary>
    KillReport Execute(IEnumerable<IKillRule> rules, BacktestWindow? evaluationWindow);
}
