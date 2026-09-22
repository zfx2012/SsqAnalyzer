using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// RuleContext 构建器抽象。回测与实际杀号共用同一构建逻辑。
/// </summary>
public interface IRuleContextBuilder
{
    /// <summary>从完整数据集构建指定 currentIndex 的只读上下文（用于回测）。</summary>
    RuleContext Build(IReadOnlyList<DrawRecord> allRecords, int currentIndex);

    /// <summary>构建"最新一期"上下文（用于实际杀号）。currentIndex = allRecords.Count。</summary>
    RuleContext BuildLatest();
}
