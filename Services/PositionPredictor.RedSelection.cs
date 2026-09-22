using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{

    private CombinationResult SelectRedCombination(
        IReadOnlyList<PositionBallScore> redScores,
        IReadOnlyList<DrawRecord> history)
    {
        var pool = redScores.OrderByDescending(score => score.TotalScore)
            .ThenByDescending(score => score.Omission)
            .ThenBy(score => score.Ball)
            .Take(_config.CandidatePoolSize)
            .Select(score => score.Ball)
            .OrderBy(ball => ball)
            .ToArray();
        var scoreByBall = redScores.ToDictionary(score => score.Ball, score => score.TotalScore);
        var patterns = MultiScaleCombinationPatterns.Create(history);
        var coverage = MultiScaleRangeCoverage.Create(history);

        int[]? best = null;
        double bestScore = double.NegativeInfinity;
        for (int a = 0; a < pool.Length - 5; a++)
        for (int b = a + 1; b < pool.Length - 4; b++)
        for (int c = b + 1; c < pool.Length - 3; c++)
        for (int d = c + 1; d < pool.Length - 2; d++)
        for (int e = d + 1; e < pool.Length - 1; e++)
        for (int f = e + 1; f < pool.Length; f++)
        {
            var candidate = new[] { pool[a], pool[b], pool[c], pool[d], pool[e], pool[f] };
            double baseScore = candidate.Average(ball => scoreByBall[ball]);
            double total = baseScore * _config.CombinationBaseWeight
                         + patterns.Score(candidate) * _config.CombinationPatternWeight
                         + coverage.Score(candidate) * _config.CombinationCoverageWeight;
            if (total > bestScore + 1e-12)
            {
                best = candidate;
                bestScore = total;
            }
        }

        if (best is null) throw new InvalidOperationException("红球候选池不足，无法生成点位组合");
        return new CombinationResult(best, RoundScore(bestScore));
    }

    private static CombinationResult SelectRangeCoverageCombination(IReadOnlyList<DrawRecord> history)
    {
        var recent100 = history.TakeLast(100).ToList();
        var coverageScores = Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball => 0.80 * Rate(CountOccurrences(history, ball, false), history.Count)
                  + 0.20 * Rate(CountOccurrences(recent100, ball, false), recent100.Count));
        return SelectGreedyRangeCoverageCombination(coverageScores);
    }

    private static CombinationResult SelectCategoryTransitionCoverageCombination(
        IReadOnlyList<DrawRecord> history,
        double strength = 0.20,
        bool useZone = true,
        bool useParity = true,
        bool useRoute = true,
        double priorStrength = 30)
    {
        var recent100 = history.TakeLast(100).ToList();
        var longRates = Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball => Rate(CountOccurrences(history, ball, false), history.Count));
        var coverageScores = Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball =>
            {
                double baseScore = 0.80 * longRates[ball]
                    + 0.20 * Rate(CountOccurrences(recent100, ball, false), recent100.Count);
                double zoneRatio = CategoryTransitionRatio(
                    history, Zone(ball), Zone, longRates, priorStrength);
                double parityRatio = CategoryTransitionRatio(
                    history, ball % 2, value => value % 2, longRates, priorStrength);
                double routeRatio = CategoryTransitionRatio(
                    history, ball % 3, value => value % 3, longRates, priorStrength);
                double transitionTotal = 0;
                int transitionCount = 0;
                if (useZone) { transitionTotal += zoneRatio; transitionCount++; }
                if (useParity) { transitionTotal += parityRatio; transitionCount++; }
                if (useRoute) { transitionTotal += routeRatio; transitionCount++; }
                double transitionRatio = transitionTotal / transitionCount;
                return baseScore * ((1 - strength) + strength * transitionRatio);
            });
        return SelectGreedyRangeCoverageCombination(coverageScores);
    }

    private static double CategoryTransitionRatio(
        IReadOnlyList<DrawRecord> history,
        int group,
        Func<int, int> groupSelector,
        IReadOnlyDictionary<int, double> longRates,
        double priorStrength = 30)
    {
        int latestState = history[^1].RedBalls.Count(ball => groupSelector(ball) == group);
        int exposures = 0;
        int nextCounts = 0;
        for (int index = 1; index < history.Count; index++)
        {
            int previousState = history[index - 1].RedBalls.Count(ball => groupSelector(ball) == group);
            if (previousState != latestState) continue;
            exposures++;
            nextCounts += history[index].RedBalls.Count(ball => groupSelector(ball) == group);
        }

        double longExpectedCount = Enumerable.Range(1, 33)
            .Where(ball => groupSelector(ball) == group)
            .Sum(ball => longRates[ball]);
        if (longExpectedCount <= 0) return 1;
        double expectedNextCount = (nextCounts + priorStrength * longExpectedCount)
            / (exposures + priorStrength);
        return Math.Clamp(expectedNextCount / longExpectedCount, 0.75, 1.25);
    }

    private static CombinationResult SelectGreedyRangeCoverageCombination(
        IReadOnlyDictionary<int, double> coverageScores)
    {
        var covered = new HashSet<int>();
        var selected = new List<int>(6);

        while (selected.Count < 6)
        {
            int bestPoint = Enumerable.Range(1, 33)
                .Where(point => !selected.Contains(point))
                .OrderByDescending(point => Enumerable.Range(
                        Math.Max(1, point - PositionPointRange.Radius),
                        Math.Min(33, point + PositionPointRange.Radius)
                            - Math.Max(1, point - PositionPointRange.Radius) + 1)
                    .Where(ball => !covered.Contains(ball))
                    .Sum(ball => coverageScores[ball]))
                .ThenBy(point => point)
                .First();
            selected.Add(bestPoint);
            for (int ball = Math.Max(1, bestPoint - PositionPointRange.Radius);
                 ball <= Math.Min(33, bestPoint + PositionPointRange.Radius);
                 ball++)
                covered.Add(ball);
        }

        double score = covered.Sum(ball => coverageScores[ball]) / 6.0;
        return new CombinationResult(selected.OrderBy(point => point).ToArray(), RoundScore(score));
    }

    private static CombinationResult SelectRecentStructureCoverageCombination(
        IReadOnlyList<PositionBallScore> redScores,
        IReadOnlyList<DrawRecord> history)
    {
        var scoreByBall = redScores.ToDictionary(score => score.Ball, score => score.TotalScore);
        var patterns = RecentStructurePatterns.Create(history);
        var beam = new List<PointCoverageCandidate>
        {
            new(Array.Empty<int>(), new HashSet<int>(), 0)
        };

        // A compact beam keeps export/backtest cost bounded while scoring the six-point pool as a whole.
        for (int depth = 0; depth < 6; depth++)
        {
            var next = new List<PointCoverageCandidate>();
            foreach (var candidate in beam)
            {
                for (int point = 1; point <= 33; point++)
                {
                    if (candidate.Points.Contains(point)) continue;
                    var range = PointRange(point);
                    if (range.Any(candidate.Covered.Contains)) continue;

                    var covered = new HashSet<int>(candidate.Covered);
                    covered.UnionWith(range);
                    var points = candidate.Points.Append(point).OrderBy(value => value).ToArray();
                    double coverage = covered.Average(ball => scoreByBall[ball]);
                    var anchors = covered.OrderByDescending(ball => scoreByBall[ball])
                        .ThenBy(ball => ball)
                        .Take(Math.Min(6, covered.Count))
                        .OrderBy(ball => ball)
                        .ToArray();
                    double structure = patterns.Score(anchors);
                    double total = 0.78 * coverage + 0.22 * structure;
                    next.Add(new PointCoverageCandidate(points, covered, total));
                }
            }

            beam = next
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => string.Join(',', candidate.Points))
                .Take(96)
                .ToList();
        }

        var best = beam.FirstOrDefault()
            ?? throw new InvalidOperationException("红球候选池不足，无法生成近期结构点位组合");
        return new CombinationResult(best.Points, RoundScore(best.Score));
    }

    private static CombinationResult SelectShrunkSeasonTransitionCoverageCombination(
        IReadOnlyList<DrawRecord> history,
        DateTime targetDrawDate)
    {
        const int seasonRadius = 14;
        const double seasonWeight = 0.15;
        const double seasonPriorStrength = 240;

        var recent100 = history.TakeLast(100).ToList();
        var longRates = Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball => Rate(CountOccurrences(history, ball, false), history.Count));
        var baselineScores = Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball => 0.80 * longRates[ball]
                  + 0.20 * Rate(CountOccurrences(recent100, ball, false), recent100.Count));

        var seasonRecords = history.Where(record =>
        {
            int distance = Math.Abs(record.DrawDate.DayOfYear - targetDrawDate.DayOfYear);
            return Math.Min(distance, 366 - distance) <= seasonRadius;
        }).ToList();
        var seasonScores = Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball =>
            {
                double seasonRate = (CountOccurrences(seasonRecords, ball, false)
                        + seasonPriorStrength * longRates[ball])
                    / (seasonRecords.Count + seasonPriorStrength);
                return (1 - seasonWeight) * baselineScores[ball] + seasonWeight * seasonRate;
            });

        var transitionScores = Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball =>
            {
                double zoneRatio = CategoryTransitionRatio(
                    history, Zone(ball), Zone, longRates, priorStrength: 120);
                double routeRatio = CategoryTransitionRatio(
                    history, ball % 3, value => value % 3, longRates, priorStrength: 120);
                double transitionRatio = (zoneRatio + routeRatio) / 2.0;
                return baselineScores[ball] * (0.95 + 0.05 * transitionRatio);
            });

        double seasonTotal = seasonScores.Values.Sum();
        double transitionTotal = transitionScores.Values.Sum();
        var blended = Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball => 0.50 * seasonScores[ball] / seasonTotal
                  + 0.50 * transitionScores[ball] / transitionTotal);
        return SelectGreedyRangeCoverageCombination(blended);
    }

    private static int[] PointRange(int point) => Enumerable.Range(
        Math.Max(1, point - PositionPointRange.Radius),
        Math.Min(33, point + PositionPointRange.Radius)
            - Math.Max(1, point - PositionPointRange.Radius) + 1).ToArray();
}
