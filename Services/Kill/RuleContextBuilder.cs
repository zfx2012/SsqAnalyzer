using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// RuleContext 构建器实现：切片 + 预计算遗漏矩阵 + 同期/周期/单双期过滤。
/// 时间护栏（§7.3）：规则只能看 [0, currentIndex) 的数据，绝不能看到 currentIndex（被预测期）。
///
/// 性能优化（瓶颈3）：
///   - <see cref="BuildWithMiss"/> 接收外部预计算的全量遗漏矩阵，避免每次 Build 重算
///   - 回测 100 期：100 次 MissMatrixCalculator.Compute → 1 次
/// </summary>
public sealed class RuleContextBuilder : IRuleContextBuilder
{
    private readonly IDataService? _dataService;
    private const int BallCount = 49;

    public RuleContextBuilder(IDataService? dataService = null)
    {
        _dataService = dataService;
    }

    public RuleContext Build(IReadOnlyList<DrawRecord> allRecords, int currentIndex)
    {
        if (allRecords == null) throw new ArgumentNullException(nameof(allRecords));
        if (currentIndex < 0 || currentIndex > allRecords.Count)
            throw new ArgumentOutOfRangeException(nameof(currentIndex),
                $"currentIndex={currentIndex} 越界，allRecords.Count={allRecords.Count}");

        if (currentIndex == 0)
        {
            // 没有历史数据：返回空上下文
            return new RuleContext(
                currentIndex: 0,
                latestRecord: null,
                historyRecords: Array.Empty<DrawRecord>(),
                currentCycle: CycleType.None,
                currentParity: ParityType.None,
                currentShortPeriodSuffix: 0,
                parameters: new Dictionary<string, object>(),
                missMatrix: new int[0, BallCount]);
        }

        // 时间护栏：HistoryRecords = allRecords[0..currentIndex]（不含 currentIndex）
        var history = new List<DrawRecord>(currentIndex);
        for (int i = 0; i < currentIndex; i++)
            history.Add(allRecords[i]);
        var latest = allRecords[currentIndex - 1];

        // 预计算遗漏矩阵（history 作为 records，allRecords 作为 fullData 用于前缀遗漏）
        var missMatrix = MissMatrixCalculator.Compute(history, allRecords);

        var (currentCycle, currentParity, currentShortPeriodSuffix) = ResolveCurrentPeriod(allRecords, currentIndex, latest);

        return new RuleContext(
            currentIndex: currentIndex,
            latestRecord: LatestRecordView.From(latest),
            historyRecords: history,
            currentCycle: currentCycle,
            currentParity: currentParity,
            currentShortPeriodSuffix: currentShortPeriodSuffix,
            parameters: new Dictionary<string, object>(),
            missMatrix: missMatrix);
    }

    /// <summary>
    /// 复用外部预计算的全量遗漏矩阵构建上下文（性能优化路径，瓶颈3）。
    /// 用于回测：循环外算一次 <c>fullMissMatrix = MissMatrixCalculator.Compute(allRecords, allRecords)</c>，
    /// 循环内调本方法切片复用，避免每期重算遗漏矩阵。
    ///
    /// 切片正确性：
    ///   MissMatrixCalculator.Compute(history=allRecords[0..currentIndex], allRecords) 内部
    ///   定位 history[0]=allRecords[0] 在 allRecords 中的位置（fullStartIdx=0），
    ///   因 fullStartIdx &gt; 0 为 false，跳过前缀扫描，矩阵从 records[0]=allRecords[0] 起累加 miss。
    ///   这与 MissMatrixCalculator.Compute(allRecords, allRecords) 的 [0..currentIndex] 行完全一致——
    ///   同一起点、同一累加规则、同一前缀（无）。所以 fullMissMatrix[0..currentIndex, n] ==
    ///   原算法结果。RuleContext.GetMiss/GetMissValues 用 CurrentIndex 限制访问行数，传 N 行矩阵安全。
    /// </summary>
    /// <param name="fullMissMatrix">全量遗漏矩阵 = MissMatrixCalculator.Compute(allRecords, allRecords)</param>
    public RuleContext BuildWithMiss(IReadOnlyList<DrawRecord> allRecords, int currentIndex, int[,] fullMissMatrix)
    {
        if (allRecords == null) throw new ArgumentNullException(nameof(allRecords));
        if (fullMissMatrix == null) throw new ArgumentNullException(nameof(fullMissMatrix));
        if (currentIndex < 0 || currentIndex > allRecords.Count)
            throw new ArgumentOutOfRangeException(nameof(currentIndex),
                $"currentIndex={currentIndex} 越界，allRecords.Count={allRecords.Count}");

        if (currentIndex == 0)
        {
            return new RuleContext(
                currentIndex: 0,
                latestRecord: null,
                historyRecords: Array.Empty<DrawRecord>(),
                currentCycle: CycleType.None,
                currentParity: ParityType.None,
                currentShortPeriodSuffix: 0,
                parameters: new Dictionary<string, object>(),
                missMatrix: new int[0, BallCount]);
        }

        var history = new List<DrawRecord>(currentIndex);
        for (int i = 0; i < currentIndex; i++)
            history.Add(allRecords[i]);
        var latest = allRecords[currentIndex - 1];

        var (currentCycle, currentParity, currentShortPeriodSuffix) = ResolveCurrentPeriod(allRecords, currentIndex, latest);

        // 直接复用 fullMissMatrix（不切片、不重算）：
        // RuleContext 内部 GetMiss/GetMissValues 通过 CurrentIndex 限制访问行数，
        // 仅读取 [0..currentIndex) 行，多余行不被访问。
        return new RuleContext(
            currentIndex: currentIndex,
            latestRecord: LatestRecordView.From(latest),
            historyRecords: history,
            currentCycle: currentCycle,
            currentParity: currentParity,
            currentShortPeriodSuffix: currentShortPeriodSuffix,
            parameters: new Dictionary<string, object>(),
            missMatrix: fullMissMatrix);
    }

    public RuleContext BuildLatest()
    {
        if (_dataService == null)
            throw new InvalidOperationException("BuildLatest 需要 IDataService，但 RuleContextBuilder 构造时未注入");
        var records = _dataService.GetAllRecords();
        return Build(records, records.Count);
    }

    public RuleContext BuildForTarget(IReadOnlyList<DrawRecord> history, int period, DateTime date)
    {
        if (history.Count == 0 || period <= history[^1].Period || date.Date <= history[^1].DrawDate.Date)
            throw new ArgumentException("目标期必须在历史之后。");
        return new RuleContext(history.Count, LatestRecordView.From(history[^1]), history.ToList(),
            ToCycleType(date.DayOfWeek), period % 2 == 1 ? ParityType.Odd : ParityType.Even,
            period % 1000, new Dictionary<string, object>(), MissMatrixCalculator.Compute(history, history));
    }

    /// <summary>
    /// 解析当前期归属：被预测期 = allRecords[currentIndex]（如果存在）。
    /// 如果 currentIndex == allRecords.Count（最新一期杀号，被预测期是未来期），
    /// 用 latest 的下一期推断（期号+1，周期按下次开奖日推断）。
    /// </summary>
    private static (CycleType cycle, ParityType parity, int shortPeriodSuffix) ResolveCurrentPeriod(
        IReadOnlyList<DrawRecord> allRecords, int currentIndex, DrawRecord latest)
    {
        CycleType currentCycle;
        ParityType currentParity;
        int currentShortPeriodSuffix;

        if (currentIndex < allRecords.Count)
        {
            var predicted = allRecords[currentIndex];
            currentCycle = ToCycleType(predicted.DrawDate.DayOfWeek);
            currentParity = predicted.Period % 2 == 1 ? ParityType.Odd : ParityType.Even;
            currentShortPeriodSuffix = predicted.Period % 1000;
        }
        else
        {
            // 未来期：基于 latest 推断下一期
            currentCycle = InferNextCycle(latest.DrawDate);
            int nextPeriod = latest.Period + 1;
            currentParity = nextPeriod % 2 == 1 ? ParityType.Odd : ParityType.Even;
            currentShortPeriodSuffix = nextPeriod % 1000;
        }

        return (currentCycle, currentParity, currentShortPeriodSuffix);
    }

    private static CycleType ToCycleType(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Tuesday => CycleType.Tuesday,
        DayOfWeek.Thursday => CycleType.Thursday,
        DayOfWeek.Sunday => CycleType.Sunday,
        _ => CycleType.None
    };

    /// <summary>双色球开奖日为周二/四/日。给定最近一次开奖日，推断下一次开奖日的周期类型。</summary>
    private static CycleType InferNextCycle(DateTime lastDrawDate)
    {
        // 二/四/日 按时间顺序：周二 → 周四（+2）→ 周日（+3）→ 周二（+2）
        int offset = lastDrawDate.DayOfWeek switch
        {
            DayOfWeek.Tuesday => 2,    // 周二 → 周四
            DayOfWeek.Thursday => 3,   // 周四 → 周日
            DayOfWeek.Sunday => 2,     // 周日 → 周二
            _ => 2
        };
        var next = lastDrawDate.AddDays(offset);
        return ToCycleType(next.DayOfWeek);
    }
}
