using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{

    internal static IReadOnlyDictionary<int, double> GetDynamicHierarchicalBallProbabilities(
        IReadOnlyList<DrawRecord> history,
        bool includeShapeFeatures = false) => FitDynamicHierarchicalProbabilities(
            history,
            includeShapeFeatures,
            fixedDrawLikelihood: false).BallProbabilities;

    internal static IReadOnlyDictionary<int, double>
        GetAnnualShortPreviousYearSharedTransferBlendBallProbabilities(
            IReadOnlyList<DrawRecord> history,
            int issue)
    {
        var shortHistory = GetAnnualShortHistory(history, issue);
        var current = GetDynamicHierarchicalBallProbabilities(shortHistory);
        int targetYear = issue / 1000;
        var previousYearTail = history
            .Where(record => record.Period / 1000 == targetYear - 1)
            .TakeLast(AnnualShortHistoryWindow)
            .ToArray();
        if (previousYearTail.Length <= 5) return current;

        var previousYearShared = GetDynamicHierarchicalFeatureCoefficients(previousYearTail);
        var transferred = FitDynamicHierarchicalProbabilities(
            shortHistory,
            includeShapeFeatures: false,
            fixedDrawLikelihood: false,
            previousYearShared).BallProbabilities;
        return Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball => (current[ball] + transferred[ball]) / 2.0);
    }

    internal static IReadOnlyList<double> GetDynamicHierarchicalFeatureCoefficients(
        IReadOnlyList<DrawRecord> history) => FitDynamicHierarchicalProbabilities(
        history,
        includeShapeFeatures: false,
        fixedDrawLikelihood: false).FeatureCoefficients;

    internal static IReadOnlyDictionary<int, double> GetDynamicConditionalHierarchicalBallProbabilities(
        IReadOnlyList<DrawRecord> history) => FitDynamicHierarchicalProbabilities(
        history,
        includeShapeFeatures: false,
        fixedDrawLikelihood: true).BallProbabilities;

    internal static IReadOnlyDictionary<int, double> GetDynamicConditionalHierarchicalRangeEventScores(
        IReadOnlyList<DrawRecord> history) => FitDynamicHierarchicalProbabilities(
        history,
        includeShapeFeatures: false,
        fixedDrawLikelihood: true).RangeProbabilities;

    private static DynamicHierarchicalProbabilityResult FitDynamicHierarchicalProbabilities(
        IReadOnlyList<DrawRecord> history,
        bool includeShapeFeatures,
        bool fixedDrawLikelihood,
        IReadOnlyList<double>? initialFeatureCoefficients = null)
    {
        const int ballCount = 33;
        const int minimumContext = 5;
        int featureCount = includeShapeFeatures ? 14 : 9;
        const int epochs = 60;
        const double expectedRate = 6.0 / ballCount;
        const double learningRate = 0.45;
        const double sharedPenalty = 0.08;
        const double ballPenalty = 0.80;

        var window = history.TakeLast(AnnualShortHistoryWindow).ToArray();
        if (window.Length <= minimumContext)
        {
            var uniform = Enumerable.Range(1, ballCount)
                .ToDictionary(ball => ball, _ => expectedRate);
            return new DynamicHierarchicalProbabilityResult(
                uniform,
                fixedDrawLikelihood
                    ? GetConditionedRangeEventScores(uniform)
                    : Enumerable.Range(2, 31).ToDictionary(
                        point => point,
                        point => 1 - Math.Pow(1 - expectedRate, PointRange(point).Length)),
                new double[featureCount]);
        }

        var rows = new List<HierarchicalBallRow>((window.Length - minimumContext) * ballCount);
        for (int targetIndex = minimumContext; targetIndex < window.Length; targetIndex++)
        {
            double recencyWeight = Math.Pow(0.5, (window.Length - 1 - targetIndex) / 15.0);
            for (int ball = 1; ball <= ballCount; ball++)
                rows.Add(new HierarchicalBallRow(
                    ball,
                    CreateHierarchicalBallFeatures(
                        window,
                        targetIndex,
                        ball,
                        includeShapeFeatures),
                    window[targetIndex].RedBalls.Contains(ball) ? 1.0 : 0.0,
                    recencyWeight));
        }

        var shared = new double[featureCount + 1];
        shared[0] = Math.Log(expectedRate / (1 - expectedRate));
        if (initialFeatureCoefficients is not null)
        {
            if (initialFeatureCoefficients.Count != featureCount)
                throw new ArgumentException(
                    $"Expected {featureCount} initial feature coefficients.",
                    nameof(initialFeatureCoefficients));
            for (int feature = 0; feature < featureCount; feature++)
                shared[feature + 1] = initialFeatureCoefficients[feature];
        }
        var ballEffects = new double[ballCount + 1];
        double totalWeight = rows.Sum(row => row.Weight);
        // Reuse only within this fit. Every interior cell is overwritten on each draw;
        // the zero boundary rows remain unchanged, preserving the original arithmetic order.
        var conditionalPrefix = fixedDrawLikelihood ? new double[ballCount + 1, 6 + 1] : null;
        var conditionalSuffix = fixedDrawLikelihood ? new double[ballCount + 1, 6 + 1] : null;
        for (int epoch = 0; epoch < epochs; epoch++)
        {
            var sharedGradient = new double[shared.Length];
            var ballGradient = new double[ballEffects.Length];
            if (fixedDrawLikelihood)
            {
                for (int start = 0; start < rows.Count; start += ballCount)
                {
                    var linears = Enumerable.Range(0, ballCount)
                        .Select(offset => Linear(rows[start + offset], shared, ballEffects))
                        .ToArray();
                    var probabilities = ConditionalInclusionProbabilities(
                        ExponentialWeights(linears),
                        6,
                        conditionalPrefix!,
                        conditionalSuffix!);
                    for (int offset = 0; offset < ballCount; offset++)
                        AccumulateGradient(
                            rows[start + offset],
                            probabilities[offset],
                            sharedGradient,
                            ballGradient);
                }
            }
            else
            {
                foreach (var row in rows)
                    AccumulateGradient(
                        row,
                        Sigmoid(Linear(row, shared, ballEffects)),
                        sharedGradient,
                        ballGradient);
            }

            shared[0] -= learningRate * sharedGradient[0] / totalWeight;
            for (int feature = 1; feature < shared.Length; feature++)
                shared[feature] -= learningRate
                    * (sharedGradient[feature] / totalWeight + sharedPenalty * shared[feature]);
            for (int ball = 1; ball <= ballCount; ball++)
                ballEffects[ball] -= learningRate
                    * (ballGradient[ball] / totalWeight + ballPenalty * ballEffects[ball]);
        }


        var predictionRows = Enumerable.Range(1, ballCount)
            .Select(ball => new HierarchicalBallRow(
                ball,
                CreateHierarchicalBallFeatures(
                    window,
                    window.Length,
                    ball,
                    includeShapeFeatures),
                0,
                1))
            .ToArray();
        var predictionLinears = predictionRows
            .Select(row => Linear(row, shared, ballEffects))
            .ToArray();
        if (fixedDrawLikelihood)
        {
            var weights = ExponentialWeights(predictionLinears);
            var ballProbabilities = ConditionalInclusionProbabilities(weights, 6)
                .Select((probability, index) => (Ball: index + 1, Probability: probability))
                .ToDictionary(pair => pair.Ball, pair => pair.Probability);
            return new DynamicHierarchicalProbabilityResult(
                ballProbabilities,
                GetConditionedRangeEventScoresFromWeights(
                    weights.Select((weight, index) => (Ball: index + 1, Weight: weight))
                        .ToDictionary(pair => pair.Ball, pair => pair.Weight)),
                shared.Skip(1).ToArray());
        }

        var independentProbabilities = predictionLinears
            .Select((linear, index) => (Ball: index + 1, Probability: Math.Clamp(Sigmoid(linear), 0.01, 0.60)))
            .ToDictionary(pair => pair.Ball, pair => pair.Probability);
        return new DynamicHierarchicalProbabilityResult(
            independentProbabilities,
            Enumerable.Range(2, 31).ToDictionary(
                point => point,
                point => 1 - PointRange(point).Aggregate(
                    1.0,
                    (missProbability, ball) => missProbability * (1 - independentProbabilities[ball]))),
            shared.Skip(1).ToArray());
    }

    private static double Linear(
        HierarchicalBallRow row,
        IReadOnlyList<double> shared,
        IReadOnlyList<double> ballEffects)
    {
        double value = shared[0] + ballEffects[row.Ball];
        for (int feature = 0; feature < row.Features.Length; feature++)
            value += shared[feature + 1] * row.Features[feature];
        return value;
    }

    private static void AccumulateGradient(
        HierarchicalBallRow row,
        double probability,
        double[] sharedGradient,
        double[] ballGradient)
    {
        double error = (probability - row.Outcome) * row.Weight;
        sharedGradient[0] += error;
        for (int feature = 0; feature < row.Features.Length; feature++)
            sharedGradient[feature + 1] += error * row.Features[feature];
        ballGradient[row.Ball] += error;
    }

    internal static IReadOnlyDictionary<int, double> GetDynamicHierarchicalEnsembleBallProbabilities(
        IReadOnlyList<DrawRecord> history)
    {
        var baseline = GetDynamicHierarchicalBallProbabilities(history);
        var shape = GetDynamicHierarchicalBallProbabilities(history, includeShapeFeatures: true);
        return Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball => (baseline[ball] + shape[ball]) / 2.0);
    }

    private static IReadOnlyList<int> SelectAnnualShortPreviousYearSharedTransferBlendPoints(
        IReadOnlyList<DrawRecord> history,
        int issue)
    {
        var probabilities = GetAnnualShortPreviousYearSharedTransferBlendBallProbabilities(
            history,
            issue);
        var rangeScores = Enumerable.Range(2, 31).ToDictionary(
            point => point,
            point => PointRange(point).Sum(ball => probabilities[ball]));
        var shortHistory = GetAnnualShortHistory(history, issue);
        return SelectGlobalRangeEvents(
            rangeScores,
            CreateRangeEventScores(shortHistory, 0.40, 0.35, 0.25)).Balls;
    }

    internal static IReadOnlyDictionary<int, double> GetConditionedRangeEventScores(
        IReadOnlyDictionary<int, double> ballProbabilities)
    {
        if (ballProbabilities.Count != 33
            || Enumerable.Range(1, 33).Any(ball => !ballProbabilities.ContainsKey(ball)))
            throw new ArgumentException("Exactly 33 red-ball probabilities are required.", nameof(ballProbabilities));

        var odds = ballProbabilities.ToDictionary(
            pair => pair.Key,
            pair => Math.Clamp(pair.Value, 1e-9, 1 - 1e-9)
                / (1 - Math.Clamp(pair.Value, 1e-9, 1 - 1e-9)));
        return GetConditionedRangeEventScoresFromWeights(odds);
    }

    private static IReadOnlyDictionary<int, double> GetConditionedRangeEventScoresFromWeights(
        IReadOnlyDictionary<int, double> weights)
    {
        double allCombinations = ElementarySymmetricSum(weights.Values, 6);
        return Enumerable.Range(2, 31).ToDictionary(
            point => point,
            point => 1 - ElementarySymmetricSum(
                weights.Where(pair => !PointRange(point).Contains(pair.Key)).Select(pair => pair.Value),
                6) / allCombinations);
    }

    private static double[] ExponentialWeights(IReadOnlyList<double> linears)
    {
        double maximum = linears.Max();
        return linears.Select(value => Math.Exp(value - maximum)).ToArray();
    }

    private static double[] ConditionalInclusionProbabilities(
        IReadOnlyList<double> weights,
        int drawSize) => ConditionalInclusionProbabilities(
            weights,
            drawSize,
            new double[weights.Count + 1, drawSize + 1],
            new double[weights.Count + 1, drawSize + 1]);

    private static double[] ConditionalInclusionProbabilities(
        IReadOnlyList<double> weights,
        int drawSize,
        double[,] prefix,
        double[,] suffix)
    {
        int count = weights.Count;
        prefix[0, 0] = 1;
        for (int index = 0; index < count; index++)
        {
            prefix[index + 1, 0] = 1;
            for (int selected = 1; selected <= drawSize; selected++)
                prefix[index + 1, selected] = prefix[index, selected]
                    + weights[index] * prefix[index, selected - 1];
        }
        suffix[count, 0] = 1;
        for (int index = count - 1; index >= 0; index--)
        {
            suffix[index, 0] = 1;
            for (int selected = 1; selected <= drawSize; selected++)
                suffix[index, selected] = suffix[index + 1, selected]
                    + weights[index] * suffix[index + 1, selected - 1];
        }

        double denominator = prefix[count, drawSize];
        return Enumerable.Range(0, count).Select(index =>
        {
            double excluded = 0;
            for (int left = 0; left < drawSize; left++)
                excluded += prefix[index, left] * suffix[index + 1, drawSize - 1 - left];
            return weights[index] * excluded / denominator;
        }).ToArray();
    }

    private static double ElementarySymmetricSum(IEnumerable<double> weights, int order)
    {
        var sums = new double[order + 1];
        sums[0] = 1;
        foreach (double weight in weights)
            for (int index = order; index >= 1; index--)
                sums[index] += weight * sums[index - 1];
        return sums[order];
    }

    private static double[] CreateHierarchicalBallFeatures(
        IReadOnlyList<DrawRecord> history,
        int priorCount,
        int ball,
        bool includeShapeFeatures)
    {
        const double expectedRate = 6.0 / 33;
        double scale = Math.Sqrt(expectedRate * (1 - expectedRate));
        var latest = history[priorCount - 1].RedBalls;
        int omission = 0;
        for (int index = priorCount - 1; index >= 0; index--)
        {
            if (history[index].RedBalls.Contains(ball)) break;
            omission++;
        }

        double[] baseFeatures =
        [
            (ExponentiallyWeightedBallRate(history, priorCount, ball, 3) - expectedRate) / scale,
            (ExponentiallyWeightedBallRate(history, priorCount, ball, 7) - expectedRate) / scale,
            (ExponentiallyWeightedBallRate(history, priorCount, ball, 15) - expectedRate) / scale,
            Math.Min(omission, 12) / 12.0 - 0.5,
            latest.Contains(ball) ? 1 : 0,
            latest.Any(value => Math.Abs(value - ball) == 1) ? 1 : 0,
            (latest.Count(value => Zone(value) == Zone(ball)) - 2) / 2.0,
            (latest.Count(value => value % 3 == ball % 3) - 2) / 2.0,
            (latest.Count(value => value % 2 == ball % 2) - 3) / 3.0
        ];
        if (!includeShapeFeatures) return baseFeatures;

        var previous = history[Math.Max(0, priorCount - 2)].RedBalls;
        int recentStart = Math.Max(0, priorCount - 3);
        double localDensity = history.Skip(recentStart).Take(priorCount - recentStart)
            .Sum(record => record.RedBalls.Count(value => Math.Abs(value - ball) <= 2))
            / (double)((priorCount - recentStart) * 6);
        return
        [
            ..baseFeatures,
            previous.Contains(ball) ? 1 : 0,
            previous.Any(value => Math.Abs(value - ball) == 1) ? 1 : 0,
            latest.Any(value => Math.Abs(value - ball) == 2) ? 1 : 0,
            (latest.Count(value => IsPrime(value) == IsPrime(ball)) - 3) / 3.0,
            localDensity - 5.0 / 33
        ];
    }

    private static double ExponentiallyWeightedBallRate(
        IReadOnlyList<DrawRecord> history,
        int priorCount,
        int ball,
        double halfLife)
    {
        double weightedHits = 0;
        double totalWeight = 0;
        for (int index = 0; index < priorCount; index++)
        {
            double weight = Math.Pow(0.5, (priorCount - 1 - index) / halfLife);
            totalWeight += weight;
            if (history[index].RedBalls.Contains(ball)) weightedHits += weight;
        }
        return totalWeight == 0 ? 6.0 / 33 : weightedHits / totalWeight;
    }

    private static double Sigmoid(double value) => value >= 0
        ? 1 / (1 + Math.Exp(-value))
        : Math.Exp(value) / (1 + Math.Exp(value));

    private static IReadOnlyDictionary<int, double> RankNormalize(
        IReadOnlyDictionary<int, double> scores)
    {
        if (scores.Count <= 1)
            return scores.Keys.ToDictionary(key => key, _ => 0.5);

        return scores.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                int lower = scores.Values.Count(value => value < pair.Value - 1e-12);
                int equal = scores.Values.Count(value => Math.Abs(value - pair.Value) <= 1e-12);
                return (lower + (equal - 1) / 2.0) / (scores.Count - 1);
            });
    }
}
