using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

/// <summary>分析服务接口 — 提供历史开奖数据的各类统计分析和推荐算法</summary>
public interface IAnalysisService
{
    /// <summary>获取最近 N 期的开奖记录</summary>
    /// <param name="n">期数</param>
    List<DrawRecord> GetRecent(int n);

    /// <summary>统计指定期数内各红球号码的出现次数</summary>
    /// <param name="periods">统计的期数范围</param>
    /// <returns>键为红球号码（1-33/1-35），值为出现次数</returns>
    Dictionary<int, int> GetRedFrequency(int periods);

    /// <summary>统计指定期数内各蓝球号码的出现次数</summary>
    /// <param name="periods">统计的期数范围</param>
    /// <returns>键为蓝球号码（1-16/1-12），值为出现次数</returns>
    Dictionary<int, int> GetBlueFrequency(int periods);

    /// <summary>获取杀号推荐列表</summary>
    /// <param name="periods">参考期数</param>
    [Obsolete("使用 IKillEngine.Execute 替代。旧实现为频率分桶占位逻辑，保留 1 个版本后移除。")]
    List<KillRecommendation> GetKillRecommendations(int periods);

    /// <summary>获取组号推荐列表</summary>
    /// <param name="periods">参考期数</param>
    List<GroupRecommendation> GetGroupRecommendations(int periods);

    /// <summary>获取号码共现频率统计</summary>
    /// <param name="periods">统计期数</param>
    List<PairFrequency> GetPairFrequencies(int periods);

    /// <summary>获取复式投注成本计算</summary>
    List<CompoundCost> GetCompoundCosts();

    /// <summary>获取红球复式推荐（核心号码 + 备选号码）</summary>
    (List<int> core, List<int> alt) GetRedCompoundRecommendation(int periods);

    /// <summary>获取蓝球复式推荐（核心号码 + 备选号码）</summary>
    (List<int> core, List<int> alt) GetBlueCompoundRecommendation(int periods);

    /// <summary>获取分区分布数据（用于走势图中的分区展示）</summary>
    List<ChartSeries> GetZoneDistribution(int periods);

    /// <summary>获取和值分布数据</summary>
    (List<string> labels, List<double> values) GetSumData(int periods);

    /// <summary>获取跨度分布数据</summary>
    (List<string> labels, List<double> values) GetSpanData(int periods);

    /// <summary>获取奇偶统计（奇数和、偶数和、最大奇号、最大偶号）</summary>
    (int oddSum, int evenSum, int maxOdd, int maxEven) GetOddEvenStats(int periods);

    /// <summary>获取跨度统计（平均值、最大值、最小值、大于 25 的比例）</summary>
    (double avg, int max, int min, double gt25Percent) GetSpanStats(int periods);
}
