using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{

    private static Dictionary<int, double> BuildRedBalanceFeatures(
        IReadOnlyList<DrawRecord> recent,
        IReadOnlyList<int> balls)
    {
        if (recent.Count == 0) return balls.ToDictionary(ball => ball, _ => 0.0);
        double total = recent.Count * 6.0;
        var route = Enumerable.Range(0, 3).ToDictionary(key => key,
            key => recent.Sum(record => record.RedBalls.Count(ball => ball % 3 == key)) / total);
        var parity = Enumerable.Range(0, 2).ToDictionary(key => key,
            key => recent.Sum(record => record.RedBalls.Count(ball => ball % 2 == key)) / total);
        var zone = Enumerable.Range(0, 3).ToDictionary(key => key,
            key => recent.Sum(record => record.RedBalls.Count(ball => Zone(ball) == key)) / total);
        return balls.ToDictionary(ball => ball, ball =>
            (CategoryDeficit(route[ball % 3], balls.Count(n => n % 3 == ball % 3) / 33.0)
           + CategoryDeficit(parity[ball % 2], balls.Count(n => n % 2 == ball % 2) / 33.0)
           + CategoryDeficit(zone[Zone(ball)], balls.Count(n => Zone(n) == Zone(ball)) / 33.0)) / 3.0);
    }

    private static double ConditionalTransitionRate(IReadOnlyList<DrawRecord> history, int ball)
    {
        if (history.Count < 2) return 0;
        bool latestState = history[^1].RedBalls.Contains(ball);
        int trials = 0, hits = 0;
        for (int i = 1; i < history.Count; i++)
        {
            if (history[i - 1].RedBalls.Contains(ball) != latestState) continue;
            trials++;
            if (history[i].RedBalls.Contains(ball)) hits++;
        }
        return (hits + 1.0) / (trials + 2.0);
    }

    private static double NeighborTransitionRate(IReadOnlyList<DrawRecord> history, int ball)
    {
        if (history.Count < 2) return 0;
        bool latestHasNeighbor = history[^1].RedBalls.Any(number => Math.Abs(number - ball) == 1);
        int trials = 0, hits = 0;
        for (int i = 1; i < history.Count; i++)
        {
            bool priorHasNeighbor = history[i - 1].RedBalls.Any(number => Math.Abs(number - ball) == 1);
            if (priorHasNeighbor != latestHasNeighbor) continue;
            trials++;
            if (history[i].RedBalls.Contains(ball)) hits++;
        }
        return (hits + 1.0) / (trials + 2.0);
    }

    private static double CycleStability(IReadOnlyList<DrawRecord> history, int ball, bool isBlue) =>
        CycleStability(history, ball, isBlue, history.Count);

    private static double CycleStability(IReadOnlyList<DrawRecord> history, int ball, bool isBlue, int endExclusive)
    {
        var positions = new List<int>();
        for (int i = 0; i < endExclusive; i++)
            if (Contains(history[i], ball, isBlue)) positions.Add(i);
        if (positions.Count < 2) return 0;
        double averageGap = positions.Zip(positions.Skip(1), (left, right) => right - left).Average();
        int currentOmission = endExclusive - 1 - positions[^1];
        return 1.0 / (1.0 + Math.Abs(currentOmission - averageGap) / (averageGap + 1.0));
    }

    private static Dictionary<int, double> Ranks<T>(IReadOnlyDictionary<int, T> values) where T : IConvertible =>
        PercentileRanks(values.ToDictionary(pair => pair.Key, pair => Convert.ToDouble(pair.Value)));

    private static Dictionary<int, double> PercentileRanks(IReadOnlyDictionary<int, double> values)
    {
        var distinct = values.Values.Distinct().OrderBy(value => value).ToArray();
        if (distinct.Length == 1) return values.Keys.ToDictionary(key => key, _ => 0.5);
        var ranks = distinct.Select((value, index) => (value, rank: index / (double)(distinct.Length - 1)))
            .ToDictionary(item => item.value, item => item.rank);
        return values.ToDictionary(pair => pair.Key, pair => ranks[pair.Value]);
    }

    private static Dictionary<int, double> CategoryRanks(IReadOnlyDictionary<int, double> values) =>
        PercentileRanks(values);

    private static int CountOccurrences(IReadOnlyList<DrawRecord> records, int ball, bool isBlue) =>
        records.Count(record => Contains(record, ball, isBlue));

    private static int CalculateOmission(IReadOnlyList<DrawRecord> records, int ball, bool isBlue)
    {
        int omission = 0;
        for (int i = records.Count - 1; i >= 0; i--)
        {
            if (Contains(records[i], ball, isBlue)) return omission;
            omission++;
        }
        return omission;
    }

    private static bool Contains(DrawRecord record, int ball, bool isBlue) =>
        isBlue ? record.BlueBall == ball : record.RedBalls.Contains(ball);

    private static bool IsNearIssuePosition(int historicalIssue, int targetIssue, int radius) =>
        Math.Abs(historicalIssue % 1000 - targetIssue % 1000) <= radius;

    private static int Zone(int ball) => ball <= 11 ? 0 : ball <= 22 ? 1 : 2;

    private static bool IsPrime(int value) =>
        value is 2 or 3 or 5 or 7 or 11 or 13 or 17 or 19 or 23 or 29 or 31;
    private static int Wrap16(int value) => ((value - 1) % 16 + 16) % 16 + 1;
    private static int DigitSum(int value) => value.ToString().Sum(character => character - '0');
    private static string BlueContext(int ball) => $"{ball % 2}:{ball % 3}:{(ball <= 8 ? 0 : 1)}";
    private static double Rate(int count, int total) => total == 0 ? 0 : count / (double)total;
    private static double CategoryDeficit(double observed, double expected) => expected - observed;
    private static double RoundScore(double value) => Math.Round(value, 6);
}
