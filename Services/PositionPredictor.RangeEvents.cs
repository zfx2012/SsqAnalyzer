using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{

    private static bool ShouldUseStableSignalExpert(
        IReadOnlyList<DrawRecord> history,
        double window30Weight,
        double window15Weight,
        double window5Weight)
    {
        double baseAuc = 0;
        double stableAuc = 0;
        for (int targetIndex = 1; targetIndex < history.Count; targetIndex++)
        {
            var training = history.Take(targetIndex).ToArray();
            var target = history[targetIndex];
            baseAuc += CalculateRangeScoreAuc(
                CreateRangeEventScores(
                    training,
                    window30Weight,
                    window15Weight,
                    window5Weight),
                target);
            stableAuc += CalculateRangeScoreAuc(
                GetAnnualShortStableSignalRankScores(training),
                target);
        }
        return stableAuc > baseAuc + 1e-12;
    }

    private static double CalculateRangeScoreAuc(
        IReadOnlyDictionary<int, double> scores,
        DrawRecord target)
    {
        double wins = 0;
        int pairs = 0;
        foreach (int positive in Enumerable.Range(2, 31).Where(point =>
                     IsRangeEventLit(target, point)))
        {
            foreach (int negative in Enumerable.Range(2, 31).Where(point =>
                         !IsRangeEventLit(target, point)))
            {
                pairs++;
                if (scores[positive] > scores[negative]) wins++;
                else if (Math.Abs(scores[positive] - scores[negative]) <= 1e-12) wins += 0.5;
            }
        }
        return pairs == 0 ? 0.5 : wins / pairs;
    }

    private static Dictionary<int, double> CreateRangeEventScores(
        IReadOnlyList<DrawRecord> history,
        double window30Weight,
        double window15Weight,
        double window5Weight,
        bool hierarchicalShrinkage = false)
    {
        var window30 = history.TakeLast(AnnualShortHistoryWindow).ToList();
        var window15 = window30.TakeLast(15).ToList();
        var window5 = window30.TakeLast(5).ToList();
        return Enumerable.Range(2, 31).ToDictionary(
            point => point,
            point =>
            {
                double rate30 = RangeEventRate(window30, point);
                double rate15 = hierarchicalShrinkage
                    ? ShrinkRangeEventRate(window15, point, rate30, 15)
                    : RangeEventRate(window15, point);
                double rate5 = hierarchicalShrinkage
                    ? ShrinkRangeEventRate(window5, point, rate15, 5)
                    : RangeEventRate(window5, point);
                return window30Weight * rate30
                     + window15Weight * rate15
                     + window5Weight * rate5;
            });
    }

    private static double[] CalculatePrequentialRegionOffsets(
        IReadOnlyList<DrawRecord> history,
        double window30Weight,
        double window15Weight,
        double window5Weight)
    {
        const int minimumTrainingSize = 5;
        var residualSums = new double[3];
        var observationCounts = new int[3];
        for (int targetIndex = minimumTrainingSize; targetIndex < history.Count; targetIndex++)
        {
            var training = history.Take(targetIndex).ToArray();
            var forecasts = CreateRangeEventScores(
                training,
                window30Weight,
                window15Weight,
                window5Weight);
            foreach (int point in Enumerable.Range(2, 31))
            {
                int region = Zone(point);
                double outcome = history[targetIndex].RedBalls.Any(ball =>
                    PositionPointRange.Contains(point, ball, 33)) ? 1 : 0;
                residualSums[region] += outcome - forecasts[point];
                observationCounts[region]++;
            }
        }

        return Enumerable.Range(0, 3)
            .Select(region => residualSums[region] / (observationCounts[region] + 1.0))
            .ToArray();
    }

    private static Dictionary<int, double> ApplyRangeEventStateTransition(
        IReadOnlyList<DrawRecord> history,
        IReadOnlyDictionary<int, double> baseScores)
    {
        const double priorStrength = 5;
        if (history.Count < 2) return baseScores.ToDictionary(pair => pair.Key, pair => pair.Value);

        return Enumerable.Range(2, 31).ToDictionary(
            point => point,
            point =>
            {
                bool currentState = IsRangeEventLit(history[^1], point);
                int matchingTransitions = 0;
                int litTransitions = 0;
                for (int index = 1; index < history.Count; index++)
                {
                    if (IsRangeEventLit(history[index - 1], point) != currentState) continue;
                    matchingTransitions++;
                    if (IsRangeEventLit(history[index], point)) litTransitions++;
                }
                return (litTransitions + priorStrength * baseScores[point])
                    / (matchingTransitions + priorStrength);
            });
    }

    private static Dictionary<int, double> ApplyRangeEventStructureAnalog(
        IReadOnlyList<DrawRecord> history,
        IReadOnlyDictionary<int, double> baseScores)
    {
        const double priorStrength = 5;
        if (history.Count < 2) return baseScores.ToDictionary(pair => pair.Key, pair => pair.Value);

        var targetFeatures = CreateRangeAnalogFeatures(history[^1]);
        var weightedHits = Enumerable.Range(2, 31).ToDictionary(point => point, _ => 0.0);
        double totalWeight = 0;
        for (int index = 1; index < history.Count; index++)
        {
            var predecessorFeatures = CreateRangeAnalogFeatures(history[index - 1]);
            double distance = targetFeatures.Zip(
                predecessorFeatures,
                (target, predecessor) => Math.Abs(target - predecessor)).Sum();
            double weight = 1.0 / (1.0 + distance);
            totalWeight += weight;
            foreach (int point in Enumerable.Range(2, 31))
            {
                if (IsRangeEventLit(history[index], point)) weightedHits[point] += weight;
            }
        }

        return Enumerable.Range(2, 31).ToDictionary(
            point => point,
            point => (weightedHits[point] + priorStrength * baseScores[point])
                / (totalWeight + priorStrength));
    }

    private static double[] CreateRangeAnalogFeatures(DrawRecord record)
    {
        var balls = record.RedBalls.OrderBy(ball => ball).ToArray();
        var gaps = balls.Zip(balls.Skip(1), (left, right) => right - left).ToArray();
        return new[]
        {
            balls.Count(ball => Zone(ball) == 0) / 6.0,
            balls.Count(ball => Zone(ball) == 1) / 6.0,
            balls.Count(ball => Zone(ball) == 2) / 6.0,
            balls.Count(ball => ball % 3 == 0) / 6.0,
            balls.Count(ball => ball % 3 == 1) / 6.0,
            balls.Count(ball => ball % 3 == 2) / 6.0,
            balls.Count(ball => ball % 2 != 0) / 6.0,
            balls.Count(IsPrime) / 6.0,
            gaps.Count(gap => gap == 1) / 5.0,
            gaps.Count(gap => gap is >= 2 and <= 4) / 5.0,
            gaps.Count(gap => gap >= 5) / 5.0
        };
    }

    private static double RangeEventRate(IReadOnlyList<DrawRecord> history, int point) =>
        Rate(history.Count(record => IsRangeEventLit(record, point)), history.Count);

    private static bool IsRangeEventLit(DrawRecord record, int point) =>
        record.RedBalls.Any(ball => PositionPointRange.Contains(point, ball, 33));

    private static double ShrinkRangeEventRate(
        IReadOnlyList<DrawRecord> history,
        int point,
        double priorMean,
        int priorStrength)
    {
        int hits = history.Count(record => record.RedBalls.Any(ball =>
            PositionPointRange.Contains(point, ball, 33)));
        return (hits + priorStrength * priorMean) / (history.Count + priorStrength);
    }

    private static int SelectRangeEventWindowByPrequentialBrier(
        IReadOnlyList<DrawRecord> history)
    {
        int[] windows = { 30, 15, 5 };
        return windows
            .Select(window => new
            {
                Window = window,
                Loss = CalculateRangeEventBrierLoss(history, window)
            })
            .OrderBy(result => result.Loss)
            .ThenByDescending(result => result.Window)
            .First()
            .Window;
    }

    private static double CalculateRangeEventBrierLoss(
        IReadOnlyList<DrawRecord> history,
        int window)
    {
        if (history.Count < 2) return 0;

        double totalLoss = 0;
        int observationCount = 0;
        for (int targetIndex = 1; targetIndex < history.Count; targetIndex++)
        {
            int start = Math.Max(0, targetIndex - window);
            var training = history.Skip(start).Take(targetIndex - start).ToArray();
            foreach (int point in Enumerable.Range(2, 31))
            {
                double probability = RangeEventRate(training, point);
                double outcome = history[targetIndex].RedBalls.Any(ball =>
                    PositionPointRange.Contains(point, ball, 33)) ? 1 : 0;
                totalLoss += Math.Pow(probability - outcome, 2);
                observationCount++;
            }
        }
        return totalLoss / observationCount;
    }

    private static CombinationResult SelectGreedyRangeEvents(
        IReadOnlyDictionary<int, double> scores)
    {
        var selected = new List<int>(6);
        var covered = new HashSet<int>();
        while (selected.Count < 6)
        {
            int point = Enumerable.Range(2, 31)
                .Where(candidate => PointRange(candidate).All(ball => !covered.Contains(ball)))
                .OrderByDescending(candidate => scores[candidate])
                .ThenBy(candidate => candidate)
                .First();
            selected.Add(point);
            covered.UnionWith(PointRange(point));
        }
        return new CombinationResult(
            selected.OrderBy(point => point).ToArray(),
            RoundScore(selected.Sum(point => scores[point]) / 6.0));
    }

    private static CombinationResult SelectGlobalRangeEvents(
        IReadOnlyDictionary<int, double> scores,
        IReadOnlyDictionary<int, double>? secondaryScores = null)
    {
        var current = new List<int>(6);
        int[]? best = null;
        double bestScore = double.NegativeInfinity;
        double bestSecondaryScore = double.NegativeInfinity;

        void Search(int minimumPoint, double score, double secondaryScore)
        {
            if (current.Count == 6)
            {
                if (score > bestScore + 1e-12
                    || (Math.Abs(score - bestScore) <= 1e-12
                        && secondaryScore > bestSecondaryScore + 1e-12))
                {
                    best = current.ToArray();
                    bestScore = score;
                    bestSecondaryScore = secondaryScore;
                }
                return;
            }
            int remaining = 6 - current.Count;
            int maximumPoint = 32 - 3 * (remaining - 1);
            for (int point = minimumPoint; point <= maximumPoint; point++)
            {
                current.Add(point);
                Search(
                    point + 3,
                    score + scores[point],
                    secondaryScore + (secondaryScores?[point] ?? 0));
                current.RemoveAt(current.Count - 1);
            }
        }

        Search(2, 0, 0);
        if (best is null) throw new InvalidOperationException("无法生成六个年度范围事件点位");
        return new CombinationResult(best, RoundScore(bestScore / 6.0));
    }

    private static CombinationResult SelectGlobalRangeEventsWithinOneStandardError(
        IReadOnlyDictionary<int, double> eventScores,
        IReadOnlyDictionary<int, double> ballCoverageScores,
        IReadOnlyList<DrawRecord> history)
    {
        var bestEvent = SelectGlobalRangeEvents(eventScores);
        double bestEventScore = bestEvent.Balls.Sum(point => eventScores[point]);
        double standardError = CalculatePointLightingStandardError(history, bestEvent.Balls);
        double minimumEventScore = bestEventScore - standardError * bestEvent.Balls.Length;

        var current = new List<int>(6);
        int[]? best = null;
        double bestBallCoverageScore = double.NegativeInfinity;
        double selectedEventScore = double.NegativeInfinity;

        void Search(int minimumPoint, double eventScore, double ballCoverageScore)
        {
            if (current.Count == 6)
            {
                if (eventScore + 1e-12 < minimumEventScore) return;
                if (ballCoverageScore > bestBallCoverageScore + 1e-12
                    || (Math.Abs(ballCoverageScore - bestBallCoverageScore) <= 1e-12
                        && eventScore > selectedEventScore + 1e-12))
                {
                    best = current.ToArray();
                    bestBallCoverageScore = ballCoverageScore;
                    selectedEventScore = eventScore;
                }
                return;
            }

            int remaining = 6 - current.Count;
            int maximumPoint = 32 - 3 * (remaining - 1);
            for (int point = minimumPoint; point <= maximumPoint; point++)
            {
                current.Add(point);
                Search(
                    point + 3,
                    eventScore + eventScores[point],
                    ballCoverageScore + ballCoverageScores[point]);
                current.RemoveAt(current.Count - 1);
            }
        }

        Search(2, 0, 0);
        if (best is null) return bestEvent;
        return new CombinationResult(best, RoundScore(selectedEventScore / 6.0));
    }

    private static double CalculatePointLightingStandardError(
        IReadOnlyList<DrawRecord> history,
        IReadOnlyList<int> points)
    {
        if (history.Count < 2) return 0;
        var values = history.Select(record =>
            points.Count(point => IsRangeEventLit(record, point)) / (double)points.Count).ToArray();
        double mean = values.Average();
        double variance = values.Sum(value => Math.Pow(value - mean, 2)) / (values.Length - 1);
        return Math.Sqrt(variance / values.Length);
    }
}
