using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 只读数据上下文 + 时间护栏。
/// 所有数据已截至 CurrentIndex 期，绝不包含未来期（CurrentIndex 及之后）。
/// 规则通过 Jint 沙箱白名单 API 访问（见 §7.1）。
/// </summary>
public sealed class RuleContext
{
    private const int RedCount = 33;
    private const int BlueCount = 16;
    private const int BallCount = RedCount + BlueCount;  // 49

    /// <summary>被预测期在 allRecords 中的索引（不含）。规则只能看 [0, CurrentIndex)。</summary>
    public int CurrentIndex { get; }
    /// <summary>最近一期开奖（HistoryRecords[CurrentIndex-1] 的投影）。CurrentIndex=0 时为 null。</summary>
    public LatestRecordView? LatestRecord { get; }
    /// <summary>升序历史记录，截止 CurrentIndex-1。</summary>
    public IReadOnlyList<DrawRecord> HistoryRecords { get; }

    public CycleType CurrentCycle { get; }
    public ParityType CurrentParity { get; }
    public int CurrentShortPeriodSuffix { get; }

    public IReadOnlyDictionary<string, object> Params { get; }

    // 预计算的遗漏矩阵（截止 HistoryRecords 末行）
    private readonly int[,] _missMatrix;

    internal RuleContext(
        int currentIndex,
        LatestRecordView? latestRecord,
        IReadOnlyList<DrawRecord> historyRecords,
        CycleType currentCycle,
        ParityType currentParity,
        int currentShortPeriodSuffix,
        IReadOnlyDictionary<string, object> parameters,
        int[,] missMatrix)
    {
        CurrentIndex = currentIndex;
        LatestRecord = latestRecord;
        HistoryRecords = historyRecords;
        CurrentCycle = currentCycle;
        CurrentParity = currentParity;
        CurrentShortPeriodSuffix = currentShortPeriodSuffix;
        Params = parameters;
        _missMatrix = missMatrix;
    }

    /// <summary>返回每个球截止当前期的遗漏值。ballType 默认 Red（MVP 所有规则均为红球）。</summary>
    public IReadOnlyList<MissValue> GetMissValues(int window, BallType ballType = BallType.Red)
        => MissMatrixCalculator.GetMissValues(_missMatrix, CurrentIndex, window, ballType);

    /// <summary>指定球在最近 window 期的遗漏值。ball 范围 1-33=红球, 34-49=蓝球（与矩阵列对齐）。</summary>
    public int GetMiss(int ball, int window)
    {
        if (ball < 1 || ball > BallCount) return 0;
        int rowCount = _missMatrix.GetLength(0);
        if (rowCount == 0) return 0;
        int endRow = Math.Min(CurrentIndex - 1, rowCount - 1);
        if (endRow < 0) return 0;
        return _missMatrix[endRow, ball - 1];
    }

    /// <summary>返回指定红球在最新一期之前的上次出现距离；未出现返回 -1。</summary>
    public int GetPreviousRedOccurrenceDistance(int ball)
    {
        if (ball is < 1 or > RedCount || HistoryRecords.Count < 2) return -1;
        int latestIndex = HistoryRecords.Count - 1;
        for (int index = latestIndex - 1; index >= 0; index--)
            if (HistoryRecords[index].RedBalls.Contains(ball))
                return latestIndex - index;
        return -1;
    }

    /// <summary>取最后 n 条历史记录（升序）。n ≤ 0 返回空，n ≥ HistoryRecords.Count 返回全部。</summary>
    public IReadOnlyList<DrawRecord> History(int n)
    {
        if (n <= 0) return Array.Empty<DrawRecord>();
        if (n >= HistoryRecords.Count) return HistoryRecords;
        return HistoryRecords.TakeLast(n).ToList();
    }

    /// <summary>按预测下一期的图层属性取最近 n 条历史，结果保持升序。</summary>
    public IReadOnlyList<DrawRecord> HistoryFor(string scope, int n)
    {
        if (n <= 0) return Array.Empty<DrawRecord>();
        var result = new List<DrawRecord>(Math.Min(n, HistoryRecords.Count));
        for (int i = HistoryRecords.Count - 1; i >= 0 && result.Count < n; i--)
        {
            if (ScopeMatches(HistoryRecords[i], scope)) result.Add(HistoryRecords[i]);
        }
        result.Reverse();
        return result;
    }

    /// <summary>按预测下一期的图层属性返回最近一条历史记录。</summary>
    public DrawRecord? LatestFor(string scope)
    {
        for (int i = HistoryRecords.Count - 1; i >= 0; i--)
            if (ScopeMatches(HistoryRecords[i], scope)) return HistoryRecords[i];
        return null;
    }

    /// <summary>按图层计算红球遗漏，避免规则把完整历史复制到 JS。</summary>
    public IReadOnlyList<MissValue> GetMissValuesFor(string scope, int window)
    {
        var records = HistoryFor(scope, int.MaxValue);
        if (records.Count == 0) return Array.Empty<MissValue>();
        var matrix = MissMatrixCalculator.Compute(records);
        return MissMatrixCalculator.GetMissValues(matrix, records.Count, window, BallType.Red);
    }

    /// <summary>按图层返回红球在最近一条匹配记录之前的上次出现距离。</summary>
    public int GetPreviousRedOccurrenceDistanceFor(string scope, int ball)
    {
        if (ball is < 1 or > RedCount) return -1;
        bool latestFound = false;
        int distance = 0;
        for (int i = HistoryRecords.Count - 1; i >= 0; i--)
        {
            var record = HistoryRecords[i];
            if (!ScopeMatches(record, scope)) continue;
            if (!latestFound)
            {
                latestFound = true;
                continue;
            }
            distance++;
            if (record.RedBalls.Contains(ball)) return distance;
        }
        return -1;
    }

    private bool ScopeMatches(DrawRecord record, string scope) => scope switch
    {
        "samePeriod" => record.Period % 1000 == CurrentShortPeriodSuffix,
        "parity" => CurrentParity != ParityType.None
            && (record.Period % 2 == 1) == (CurrentParity == ParityType.Odd),
        "cycle" => CurrentCycle != CycleType.None && record.DrawDate.DayOfWeek == CurrentCycle switch
        {
            CycleType.Tuesday => DayOfWeek.Tuesday,
            CycleType.Thursday => DayOfWeek.Thursday,
            CycleType.Sunday => DayOfWeek.Sunday,
            _ => DayOfWeek.Monday
        },
        _ => true
    };

    /// <summary>历史同期：期号后 3 位 = shortPeriodSuffix3 的子集。</summary>
    public IReadOnlyList<DrawRecord> GetSamePeriodRecords(int shortPeriodSuffix3)
    {
        var result = new List<DrawRecord>();
        foreach (var r in HistoryRecords)
            if (r.Period % 1000 == shortPeriodSuffix3)
                result.Add(r);
        return result;
    }

    /// <summary>周期历史：DrawDate.DayOfWeek 匹配的子集。None 返回全部。</summary>
    public IReadOnlyList<DrawRecord> GetCycleRecords(CycleType type)
    {
        if (type == CycleType.None) return HistoryRecords;
        DayOfWeek dow = type switch
        {
            CycleType.Tuesday => DayOfWeek.Tuesday,
            CycleType.Thursday => DayOfWeek.Thursday,
            CycleType.Sunday => DayOfWeek.Sunday,
            _ => DayOfWeek.Monday
        };
        var result = new List<DrawRecord>();
        foreach (var r in HistoryRecords)
            if (r.DrawDate.DayOfWeek == dow)
                result.Add(r);
        return result;
    }

    /// <summary>单双期历史：期号奇偶匹配的子集。None 返回全部。</summary>
    public IReadOnlyList<DrawRecord> GetParityRecords(ParityType parity)
    {
        if (parity == ParityType.None) return HistoryRecords;
        bool wantOdd = parity == ParityType.Odd;
        var result = new List<DrawRecord>();
        foreach (var r in HistoryRecords)
        {
            bool isOdd = r.Period % 2 == 1;
            if (isOdd == wantOdd) result.Add(r);
        }
        return result;
    }
}
