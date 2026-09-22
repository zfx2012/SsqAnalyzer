using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 遗漏矩阵纯计算（抽取自 MatrixGrid.ComputeMissValues）。
/// MatrixGrid 与 RuleContext 共享同一份算法，避免遗漏矩阵计算逻辑在两处漂移。
/// </summary>
public static class MissMatrixCalculator
{
    private const int RedCount = 33;
    private const int BlueCount = 16;
    private const int BallCount = RedCount + BlueCount;  // 49

    /// <summary>
    /// 计算 [row, ball] 遗漏矩阵；ball 0-32=红球, 33-48=蓝球。
    /// 算法与原 MatrixGrid.ComputeMissValues 完全一致：
    ///   1) 在 fullDataForPrefixMiss 中按 ReferenceEquals 定位 records[0] 起始位置；
    ///   2) 对每个球，从 fullData 倒序找最近一次出现作为 miss 初值；
    ///   3) 从最旧往最新扫描 records，遇到该球置 0 并重置 miss，否则 miss++ 并写入矩阵。
    /// </summary>
    public static int[,] Compute(IReadOnlyList<DrawRecord> records, IReadOnlyList<DrawRecord>? fullDataForPrefixMiss = null)
    {
        if (records == null || records.Count == 0)
            return new int[0, BallCount];

        // 在 fullData 中定位 records[0] 的起始索引（ReferenceEquals 语义，与原 MatrixGrid 一致）
        int fullStartIdx = -1;
        if (fullDataForPrefixMiss != null && records.Count > 0)
        {
            var firstRecord = records[0];
            for (int i = 0; i < fullDataForPrefixMiss.Count; i++)
            {
                if (ReferenceEquals(fullDataForPrefixMiss[i], firstRecord))
                {
                    fullStartIdx = i;
                    break;
                }
            }
        }

        int rowCount = records.Count;
        var matrix = new int[rowCount, BallCount];

        for (int n = 0; n < BallCount; n++)
        {
            int ballNum = n < RedCount ? n + 1 : n - RedCount + 1;
            bool isBlue = n >= RedCount;
            int miss = 0;

            // 从 records 之前的历史数据倒序扫描，找最近一次出现（结果不计入矩阵，只作为 miss 初值）
            if (fullDataForPrefixMiss != null && fullStartIdx > 0)
            {
                for (int fi = fullStartIdx - 1; fi >= 0; fi--)
                {
                    var rec = fullDataForPrefixMiss[fi];
                    if (isBlue ? rec.BlueBall == ballNum : rec.RedBalls.Contains(ballNum))
                        break;
                    miss++;
                }
            }

            // 从最旧往最新扫描 records，累加模式
            for (int ri = 0; ri < rowCount; ri++)
            {
                var rec = records[ri];
                if (isBlue ? rec.BlueBall == ballNum : rec.RedBalls.Contains(ballNum))
                {
                    matrix[ri, n] = 0;
                    miss = 0;
                }
                else
                {
                    miss++;
                    matrix[ri, n] = miss;
                }
            }
        }

        return matrix;
    }

    /// <summary>
    /// 计算截止 cutoffRow 的最近 window 期内每个球的当前遗漏值。
    /// cutoffRow 是被预测期在矩阵中的索引（不含），即规则能看到 [0, cutoffRow)。
    /// endRow = min(cutoffRow-1, rowCount-1)，返回 matrix[endRow, n]（真实累加遗漏值）。
    /// </summary>
    public static IReadOnlyList<MissValue> GetMissValues(int[,] matrix, int cutoffRow, int window, BallType ballType)
    {
        int rowCount = matrix.GetLength(0);
        if (rowCount == 0) return Array.Empty<MissValue>();

        int endRow = Math.Min(cutoffRow - 1, rowCount - 1);
        if (endRow < 0) return Array.Empty<MissValue>();

        int ballStart = ballType == BallType.Red ? 0 : RedCount;
        int ballEnd = ballType == BallType.Red ? RedCount : BallCount;

        var result = new MissValue[ballEnd - ballStart];
        for (int n = ballStart; n < ballEnd; n++)
        {
            int ballNum = n < RedCount ? n + 1 : n - RedCount + 1;
            result[n - ballStart] = new MissValue(ballNum, matrix[endRow, n]);
        }
        return result;
    }

    /// <summary>从矩阵末行提取每个球的当前遗漏值数组（兼容 MatrixGrid._missValues 语义）。</summary>
    public static int[] GetLastRowValues(int[,] matrix)
    {
        int rowCount = matrix.GetLength(0);
        int colCount = matrix.GetLength(1);
        var values = new int[colCount];
        if (rowCount == 0) return values;
        int lastRow = rowCount - 1;
        for (int n = 0; n < colCount; n++)
            values[n] = matrix[lastRow, n];
        return values;
    }
}
