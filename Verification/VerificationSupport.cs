using System.IO;
using SsqAnalyzer.Models;

internal static partial class VerificationSuite
{
    private static PositionPrediction Prediction(
        int issue,
        int asOfIssue,
        string version,
        string runId,
        string runMode = "live",
        int singleBlue = 5,
        IReadOnlyList<int>? doubleBlue = null,
        IReadOnlyList<int>? tripleBlue = null,
        IReadOnlyList<int>? redPoints = null) => new()
        {
            Issue = issue,
            AsOfIssue = asOfIssue,
            SnapshotId = "snapshot",
            RuleVersionId = version,
            RunId = runId,
            RunMode = runMode,
            RedPoints = redPoints ?? new[] { 1, 5, 10, 15, 20, 25 },
            FormulaBlue = 5,
            ExclusionBlue = 9,
            SingleBlue = singleBlue,
            DoubleBlue = doubleBlue ?? new[] { 5, 9 },
            TripleBlue = tripleBlue ?? new[] { 5, 9, 13 }
        };
    private static List<DrawRecord> BuildRecords(int count)
    {
        var records = new List<DrawRecord>(count);
        for (int index = 0; index < count; index++)
        {
            int[] reds = Enumerable.Range(1, 33)
                .OrderBy(ball => (ball * 17 + index * 11) % 37)
                .ThenBy(ball => ball)
                .Take(6)
                .OrderBy(ball => ball)
                .ToArray();
            records.Add(new DrawRecord
            {
                Period = 2025001 + index,
                DrawDate = new DateTime(2025, 1, 2).AddDays(index * 2),
                RedBalls = reds,
                BlueBall = index * 5 % 16 + 1
            });
        }
        return records;
    }
    private static DrawRecord Clone(DrawRecord source) => new()
    {
        Period = source.Period,
        DrawDate = source.DrawDate,
        RedBalls = source.RedBalls.ToArray(),
        BlueBall = source.BlueBall
    };
    private static HashSet<int> ExpandCoverage(IEnumerable<int> points) => points
        .SelectMany(point => Enumerable.Range(
            Math.Max(1, point - PositionPointRange.Radius),
            Math.Min(33, point + PositionPointRange.Radius)
                - Math.Max(1, point - PositionPointRange.Radius) + 1))
        .ToHashSet();
    private static void WithTestDirectory(Action<string> test)
    {
        string testRoot = Path.Combine(Path.GetTempPath(), $"ssq-position-verification-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        try
        {
            test(testRoot);
        }
        finally
        {
            string resolved = Path.GetFullPath(testRoot);
            string temp = Path.GetFullPath(Path.GetTempPath());
            if (!resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("ssq-position-verification-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to clean an unexpected verification path.");
            Directory.Delete(resolved, recursive: true);
        }
    }
    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Assertion failed: {name}");
    }
}
