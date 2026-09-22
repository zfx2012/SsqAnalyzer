using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{

    private static CombinationResult SelectAnnualShortTermCoverageCombination(
        IReadOnlyList<DrawRecord> history)
        => SelectAnnualShortTermCoverageCombination(
            history, 0.40, 0.35, 0.25, 0.08, AnnualShortStructureSignals.All,
            AnnualShortFrequencyMode.MultiWindow, 0);

    internal static IReadOnlyList<int> SelectAnnualShortTermPoints(
        IReadOnlyList<DrawRecord> history,
        int issue,
        double window30Weight,
        double window15Weight,
        double window5Weight,
        double structureWeight,
        AnnualShortStructureSignals structureSignals = AnnualShortStructureSignals.All,
        AnnualShortFrequencyMode frequencyMode = AnnualShortFrequencyMode.MultiWindow,
        double decayHalfLife = 0,
        AnnualShortSelectionObjective selectionObjective = AnnualShortSelectionObjective.BallCoverage,
        double rangeEventWeight = 1.0)
    {
        if (selectionObjective
            == AnnualShortSelectionObjective.DynamicHierarchicalPreviousYearSharedTransferBlendGlobal)
            return SelectAnnualShortPreviousYearSharedTransferBlendPoints(history, issue);
        var shortHistory = GetAnnualShortHistory(history, issue);
        if (selectionObjective != AnnualShortSelectionObjective.BallCoverage)
            return SelectAnnualShortRangeEventCombination(
                shortHistory,
                window30Weight,
                window15Weight,
                window5Weight,
                selectionObjective,
                rangeEventWeight).Balls;
        return SelectAnnualShortTermCoverageCombination(
            shortHistory,
            window30Weight,
            window15Weight,
            window5Weight,
            structureWeight,
            structureSignals,
            frequencyMode,
            decayHalfLife).Balls;
    }


    internal static IReadOnlyDictionary<int, double> GetAnnualShortRangeEventScores(
        IReadOnlyList<DrawRecord> history,
        int issue,
        double window30Weight,
        double window15Weight,
        double window5Weight)
    {
        var shortHistory = GetAnnualShortHistory(history, issue);
        return CreateRangeEventScores(
            shortHistory,
            window30Weight,
            window15Weight,
            window5Weight);
    }

    internal static IReadOnlyDictionary<int, double> GetAnnualShortDynamicRangeEventScores(
        IReadOnlyList<DrawRecord> history,
        int issue,
        bool includeShapeFeatures = false,
        bool ensemble = false)
    {
        var shortHistory = GetAnnualShortHistory(history, issue);
        var ballProbabilities = ensemble
            ? GetDynamicHierarchicalEnsembleBallProbabilities(shortHistory)
            : GetDynamicHierarchicalBallProbabilities(shortHistory, includeShapeFeatures);
        return Enumerable.Range(2, 31).ToDictionary(
            point => point,
            point => 1 - PointRange(point).Aggregate(
                1.0,
                (missProbability, ball) => missProbability * (1 - ballProbabilities[ball])));
    }

    internal static IReadOnlyDictionary<int, double> GetAnnualShortDynamicConditionalHierarchicalRangeEventScores(
        IReadOnlyList<DrawRecord> history,
        int issue) => GetDynamicConditionalHierarchicalRangeEventScores(
        GetAnnualShortHistory(history, issue));

    private static CombinationResult SelectAnnualShortRangeEventCombination(
        IReadOnlyList<DrawRecord> history,
        double window30Weight,
        double window15Weight,
        double window5Weight,
        AnnualShortSelectionObjective selectionObjective,
        double rangeEventWeight = 1.0)
    {
        bool global = selectionObjective != AnnualShortSelectionObjective.RangeEventGreedy;
        bool hierarchicalShrinkage = selectionObjective
            == AnnualShortSelectionObjective.RangeEventHierarchicalGlobal;
        bool adaptiveWindow = selectionObjective
            == AnnualShortSelectionObjective.RangeEventAdaptiveWindowGlobal;
        bool useBallTieBreak = selectionObjective
            == AnnualShortSelectionObjective.RangeEventGlobalBallTieBreak;
        bool useRegionCalibration = selectionObjective
            == AnnualShortSelectionObjective.RangeEventRegionCalibratedGlobal;
        bool useStateTransition = selectionObjective
            == AnnualShortSelectionObjective.RangeEventStateTransitionGlobal;
        bool useStructureAnalog = selectionObjective
            == AnnualShortSelectionObjective.RangeEventStructureAnalogGlobal;
        bool useOneStandardErrorBallCoverage = selectionObjective
            == AnnualShortSelectionObjective.RangeEventOneStandardErrorBallCoverageGlobal;
        bool useMomentumTieBreak = selectionObjective
            == AnnualShortSelectionObjective.RangeEventGlobalMomentumTieBreak;
        bool useJpfCompoundTieBreak = selectionObjective
            == AnnualShortSelectionObjective.RangeEventGlobalJpfCompoundTieBreak;
        bool useJpfMidpointAvoidanceTieBreak = selectionObjective
            == AnnualShortSelectionObjective.RangeEventGlobalJpfMidpointAvoidanceTieBreak;
        bool useStableSignalRankTieBreak = selectionObjective
            == AnnualShortSelectionObjective.RangeEventGlobalStableSignalRankTieBreak;
        bool useStableSignalRankPrimary = selectionObjective
            == AnnualShortSelectionObjective.StableSignalRankGlobal;
        bool usePrequentialSignalChampion = selectionObjective
            == AnnualShortSelectionObjective.PrequentialSignalChampionGlobal;
        bool useBaseStableRankConsensus = selectionObjective
            == AnnualShortSelectionObjective.BaseStableRankConsensusGlobal;
        bool useDiscountedBetaRange = selectionObjective
            == AnnualShortSelectionObjective.DiscountedBetaRangeGlobal;
        bool useDynamicHierarchicalBall = selectionObjective
            == AnnualShortSelectionObjective.DynamicHierarchicalBallGlobal;
        bool useDynamicConditionalHierarchicalRange = selectionObjective
            == AnnualShortSelectionObjective.DynamicConditionalHierarchicalRangeGlobal;
        bool useDynamicHierarchicalStableBlend = selectionObjective
            == AnnualShortSelectionObjective.DynamicHierarchicalStableBlendGlobal;
        bool useDynamicHierarchicalShapeBall = selectionObjective
            == AnnualShortSelectionObjective.DynamicHierarchicalShapeBallGlobal;
        bool useDynamicHierarchicalEnsemble = selectionObjective
            == AnnualShortSelectionObjective.DynamicHierarchicalEnsembleGlobal;
        bool usePositionPolarization = selectionObjective
            == AnnualShortSelectionObjective.PositionPolarizationGlobal;
        if (history.Count == 0)
        {
            var uniform = Enumerable.Range(2, 31).ToDictionary(point => point, _ => 1.0);
            return global ? SelectGlobalRangeEvents(uniform) : SelectGreedyRangeEvents(uniform);
        }
        if (adaptiveWindow)
        {
            int selectedWindow = SelectRangeEventWindowByPrequentialBrier(history);
            var selectedHistory = history.TakeLast(selectedWindow).ToList();
            var adaptiveScores = Enumerable.Range(2, 31).ToDictionary(
                point => point,
                point => RangeEventRate(selectedHistory, point));
            return SelectGlobalRangeEvents(adaptiveScores);
        }

        var window30 = history.TakeLast(AnnualShortHistoryWindow).ToList();
        var window15 = window30.TakeLast(15).ToList();
        var window5 = window30.TakeLast(5).ToList();
        var scores = CreateRangeEventScores(
            window30,
            window30Weight,
            window15Weight,
            window5Weight,
            hierarchicalShrinkage);
        if (usePositionPolarization)
        {
            // Keep the order-statistic residual as a small, shrunk research signal.
            var baseRanks = RankNormalize(scores);
            var polarizationRanks = RankNormalize(
                GetAnnualShortPositionPolarizationRangeScores(
                    window30,
                    window30Weight,
                    window15Weight,
                    window5Weight));
            var blended = Enumerable.Range(2, 31).ToDictionary(
                point => point,
                point => 0.85 * baseRanks[point] + 0.15 * polarizationRanks[point]);
            return SelectGlobalRangeEvents(blended, scores);
        }
        if (useDynamicConditionalHierarchicalRange)
            return SelectGlobalRangeEvents(
                GetDynamicConditionalHierarchicalRangeEventScores(window30),
                scores);
        if (useDynamicHierarchicalStableBlend)
        {
            var dynamicProbabilities = GetDynamicHierarchicalBallProbabilities(window30);
            var dynamicRangeScores = Enumerable.Range(2, 31).ToDictionary(
                point => point,
                point => PointRange(point).Sum(ball => dynamicProbabilities[ball]));
            var dynamicRanks = RankNormalize(dynamicRangeScores);
            var stableRanks = RankNormalize(GetAnnualShortStableSignalRankScores(window30));
            var blendedRanks = Enumerable.Range(2, 31).ToDictionary(
                point => point,
                point => 0.95 * dynamicRanks[point] + 0.05 * stableRanks[point]);
            return SelectGlobalRangeEvents(blendedRanks, scores);
        }
        if (useDynamicHierarchicalBall
            || useDynamicHierarchicalShapeBall
            || useDynamicHierarchicalEnsemble)
        {
            var ballProbabilities = useDynamicHierarchicalEnsemble
                ? GetDynamicHierarchicalEnsembleBallProbabilities(window30)
                : GetDynamicHierarchicalBallProbabilities(
                    window30,
                    useDynamicHierarchicalShapeBall);
            var rangeScores = Enumerable.Range(2, 31).ToDictionary(
                point => point,
                point => PointRange(point).Sum(ball => ballProbabilities[ball]));
            return SelectGlobalRangeEvents(rangeScores, scores);
        }
        if (useDiscountedBetaRange)
            return SelectGlobalRangeEvents(GetDiscountedBetaRangeScores(window30), scores);
        if (useBaseStableRankConsensus)
        {
            var baseRanks = RankNormalize(scores);
            var stableRanks = RankNormalize(GetAnnualShortStableSignalRankScores(window30));
            var consensus = Enumerable.Range(2, 31).ToDictionary(
                point => point,
                point => (baseRanks[point] + stableRanks[point]) / 2.0);
            return SelectGlobalRangeEvents(consensus, scores);
        }
        if (usePrequentialSignalChampion)
        {
            var stableScores = GetAnnualShortStableSignalRankScores(window30);
            return ShouldUseStableSignalExpert(
                    window30,
                    window30Weight,
                    window15Weight,
                    window5Weight)
                ? SelectGlobalRangeEvents(stableScores, scores)
                : SelectGlobalRangeEvents(scores);
        }
        if (useStableSignalRankPrimary)
            return SelectGlobalRangeEvents(
                GetAnnualShortStableSignalRankScores(window30),
                scores);
        if (useRegionCalibration)
        {
            var offsets = CalculatePrequentialRegionOffsets(
                window30,
                window30Weight,
                window15Weight,
                window5Weight);
            scores = scores.ToDictionary(
                pair => pair.Key,
                pair => Math.Clamp(pair.Value + offsets[Zone(pair.Key)], 0, 1));
        }
        if (useStateTransition)
            scores = ApplyRangeEventStateTransition(window30, scores);
        if (useStructureAnalog)
            scores = ApplyRangeEventStructureAnalog(window30, scores);
        Dictionary<int, double>? secondaryScores = null;
        if (useMomentumTieBreak)
        {
            secondaryScores = Enumerable.Range(2, 31).ToDictionary(
                point => point,
                point => RangeEventRate(window5, point) - RangeEventRate(window30, point));
        }
        else if (useJpfCompoundTieBreak)
        {
            secondaryScores = GetJpfCompoundTransitionScores(window30).ToDictionary(
                pair => pair.Key,
                pair => pair.Value);
        }
        else if (useJpfMidpointAvoidanceTieBreak)
        {
            secondaryScores = GetJpfMidpointSupportScores(window30).ToDictionary(
                pair => pair.Key,
                pair => -pair.Value);
        }
        else if (useStableSignalRankTieBreak)
        {
            secondaryScores = GetAnnualShortStableSignalRankScores(window30).ToDictionary(
                pair => pair.Key,
                pair => pair.Value);
        }
        if (rangeEventWeight is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(rangeEventWeight));
        Dictionary<int, double>? ballRangeScores = null;
        if (rangeEventWeight < 1 || useBallTieBreak || useOneStandardErrorBallCoverage)
        {
            var ballRates = Enumerable.Range(1, 33).ToDictionary(
                ball => ball,
                ball => window30Weight * Rate(CountOccurrences(window30, ball, false), window30.Count)
                      + window15Weight * Rate(CountOccurrences(window15, ball, false), window15.Count)
                      + window5Weight * Rate(CountOccurrences(window5, ball, false), window5.Count));
            ballRangeScores = Enumerable.Range(2, 31).ToDictionary(
                point => point,
                point => PointRange(point).Sum(ball => ballRates[ball]));
        }
        if (rangeEventWeight < 1)
        {
            double eventMaximum = Math.Max(scores.Values.Max(), 1e-12);
            double ballMaximum = Math.Max(ballRangeScores!.Values.Max(), 1e-12);
            scores = Enumerable.Range(2, 31).ToDictionary(
                point => point,
                point => (1 - rangeEventWeight) * ballRangeScores![point] / ballMaximum
                       + rangeEventWeight * scores[point] / eventMaximum);
        }
        if (useOneStandardErrorBallCoverage)
            return SelectGlobalRangeEventsWithinOneStandardError(
                scores,
                ballRangeScores!,
                window30);
        return global
            ? SelectGlobalRangeEvents(
                scores,
                useBallTieBreak ? ballRangeScores : secondaryScores)
            : SelectGreedyRangeEvents(scores);
    }

    internal static IReadOnlyDictionary<int, double> GetJpfCompoundTransitionScores(
        IReadOnlyList<DrawRecord> history) => Enumerable.Range(2, 31).ToDictionary(
        point => point,
        point =>
        {
            if (history.Count == 0) return 0.0;

            int support = IsRangeEventLit(history[^1], point) ? 1 : 0;
            bool intervalLit = false;
            for (int offset = 2; offset <= 4 && history.Count >= offset; offset++)
                intervalLit |= IsRangeEventLit(history[^offset], point);
            if (intervalLit) support++;

            int lowerNeighbor = point - PositionPointRange.Radius - 1;
            int upperNeighbor = point + PositionPointRange.Radius + 1;
            if (history[^1].RedBalls.Any(ball =>
                    (lowerNeighbor >= 1 && ball == lowerNeighbor)
                    || (upperNeighbor <= 33 && ball == upperNeighbor)))
                support++;
            return support;
        });

    internal static IReadOnlyDictionary<int, double> GetJpfMidpointSupportScores(
        IReadOnlyList<DrawRecord> history) => Enumerable.Range(2, 31).ToDictionary(
        point => point,
        point =>
        {
            if (history.Count == 0) return 0.0;

            var reds = history[^1].RedBalls;
            int support = 0;
            for (int first = 0; first < reds.Count; first++)
            {
                for (int second = first + 1; second < reds.Count; second++)
                {
                    int sum = reds[first] + reds[second];
                    if (sum % 2 == 0 && PositionPointRange.Contains(point, sum / 2, 33))
                        support++;
                }
            }
            return support;
        });

    internal static IReadOnlyDictionary<int, double> GetAnnualShortStableSignalRankScores(
        IReadOnlyList<DrawRecord> history)
    {
        var window30 = history.TakeLast(AnnualShortHistoryWindow).ToArray();
        var window5 = window30.TakeLast(5).ToArray();
        var momentum = Enumerable.Range(2, 31).ToDictionary(
            point => point,
            point => RangeEventRate(window5, point) - RangeEventRate(window30, point));
        var midpointAvoidance = GetJpfMidpointSupportScores(window30).ToDictionary(
            pair => pair.Key,
            pair => -pair.Value);
        var momentumRanks = RankNormalize(momentum);
        var midpointRanks = RankNormalize(midpointAvoidance);
        return Enumerable.Range(2, 31).ToDictionary(
            point => point,
            point => (momentumRanks[point] + midpointRanks[point]) / 2.0);
    }

    internal static IReadOnlyDictionary<int, double> GetAnnualShortPositionPolarizationRangeScores(
        IReadOnlyList<DrawRecord> history,
        double window30Weight = 0.40,
        double window15Weight = 0.35,
        double window5Weight = 0.25)
    {
        var window30 = history.TakeLast(AnnualShortHistoryWindow).ToArray();
        var window15 = window30.TakeLast(15).ToArray();
        var window5 = window30.TakeLast(5).ToArray();
        var ballScores = Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball => window30Weight * PositionPolarizationBallScore(window30, ball)
                + window15Weight * PositionPolarizationBallScore(window15, ball)
                + window5Weight * PositionPolarizationBallScore(window5, ball));
        return Enumerable.Range(2, 31).ToDictionary(
            point => point,
            point =>
            {
                double rawScore = PointRange(point).Sum(ball => ballScores[ball]);
                double prior = PositionPointRange.RandomHitProbability(point, 33, 6);
                return Math.Clamp(prior + 0.05 * Math.Tanh(rawScore / 2.0), 0.01, 0.99);
            });
    }

    private static double PositionPolarizationBallScore(
        IReadOnlyList<DrawRecord> history,
        int ball)
    {
        if (history.Count == 0) return 0;

        const double priorStrength = 60.0;
        double sampleWeight = history.Count / (history.Count + priorStrength);
        double denominator = DataService.Combination(33, 6);
        double score = 0;
        for (int position = 1; position <= 6; position++)
        {
            double probability = DataService.Combination(ball - 1, position - 1)
                * DataService.Combination(33 - ball, 6 - position)
                / denominator;
            double expected = history.Count * probability;
            if (expected < 1e-9) continue;
            int observed = history.Count(record =>
            {
                var reds = record.RedBalls.OrderBy(value => value).ToArray();
                return reds.Length >= position && reds[position - 1] == ball;
            });
            double standardError = Math.Sqrt(Math.Max(expected * (1 - probability), 1e-9));
            score += Math.Clamp((observed - expected) / standardError, -3.0, 3.0);
        }
        return sampleWeight * score;
    }



    internal static IReadOnlyDictionary<int, double> GetDiscountedBetaRangeScores(
        IReadOnlyList<DrawRecord> history)
    {
        const double discount = 14.0 / 15.0;
        double priorMean = PositionPointRange.RandomHitProbability(2, 33, 6);
        return Enumerable.Range(2, 31).ToDictionary(
            point => point,
            point =>
            {
                double alpha = priorMean;
                double beta = 1 - priorMean;
                foreach (var record in history.TakeLast(AnnualShortHistoryWindow))
                {
                    alpha = discount * alpha + (IsRangeEventLit(record, point) ? 1 : 0);
                    beta = discount * beta + (IsRangeEventLit(record, point) ? 0 : 1);
                }
                return alpha / (alpha + beta);
            });
    }

    private static CombinationResult SelectAnnualShortTermCoverageCombination(
        IReadOnlyList<DrawRecord> history,
        double window30Weight,
        double window15Weight,
        double window5Weight,
        double structureWeight,
        AnnualShortStructureSignals structureSignals,
        AnnualShortFrequencyMode frequencyMode,
        double decayHalfLife)
    {
        if (history.Count == 0)
        {
            var uniform = Enumerable.Range(1, 33).ToDictionary(ball => ball, _ => 1.0);
            return SelectGreedyRangeCoverageCombination(uniform);
        }
        if (window30Weight < 0 || window15Weight < 0 || window5Weight < 0
            || Math.Abs(window30Weight + window15Weight + window5Weight - 1.0) > 1e-9
            || structureWeight is < 0 or > 0.20)
            throw new ArgumentOutOfRangeException(nameof(window30Weight), "年度短期点位权重无效");
        if (frequencyMode == AnnualShortFrequencyMode.ExponentialDecay && decayHalfLife <= 0)
            throw new ArgumentOutOfRangeException(nameof(decayHalfLife), "指数衰减半衰期必须大于0");

        var window30 = history.TakeLast(AnnualShortHistoryWindow).ToList();
        var window15 = window30.TakeLast(15).ToList();
        var window5 = window30.TakeLast(5).ToList();
        var baseRates = frequencyMode == AnnualShortFrequencyMode.ExponentialDecay
            ? CreateExponentialRates(window30, decayHalfLife)
            : Enumerable.Range(1, 33).ToDictionary(
                ball => ball,
                ball => window30Weight * Rate(CountOccurrences(window30, ball, false), window30.Count)
                      + window15Weight * Rate(CountOccurrences(window15, ball, false), window15.Count)
                      + window5Weight * Rate(CountOccurrences(window5, ball, false), window5.Count));
        var coverageScores = Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball =>
            {
                double zoneRatio = CategoryTransitionRatio(history, Zone(ball), Zone, baseRates, priorStrength: 12);
                double parityRatio = CategoryTransitionRatio(
                    history, ball % 2, value => value % 2, baseRates, priorStrength: 12);
                double routeRatio = CategoryTransitionRatio(
                    history, ball % 3, value => value % 3, baseRates, priorStrength: 12);
                double primeRatio = CategoryTransitionRatio(
                    history, IsPrime(ball) ? 1 : 0, value => IsPrime(value) ? 1 : 0, baseRates, priorStrength: 12);
                double baseline = Math.Max(baseRates[ball], 1.0 / 33.0);
                double repeatRatio = Math.Clamp(
                    ConditionalTransitionRate(history, ball) / baseline, 0.75, 1.25);
                double neighborRatio = Math.Clamp(
                    NeighborTransitionRate(history, ball) / baseline, 0.75, 1.25);
                double structureTotal = 0;
                int structureCount = 0;
                if (structureSignals.HasFlag(AnnualShortStructureSignals.Zone))
                { structureTotal += zoneRatio; structureCount++; }
                if (structureSignals.HasFlag(AnnualShortStructureSignals.Parity))
                { structureTotal += parityRatio; structureCount++; }
                if (structureSignals.HasFlag(AnnualShortStructureSignals.Route))
                { structureTotal += routeRatio; structureCount++; }
                if (structureSignals.HasFlag(AnnualShortStructureSignals.Prime))
                { structureTotal += primeRatio; structureCount++; }
                if (structureSignals.HasFlag(AnnualShortStructureSignals.Repeat))
                { structureTotal += repeatRatio; structureCount++; }
                if (structureSignals.HasFlag(AnnualShortStructureSignals.Neighbor))
                { structureTotal += neighborRatio; structureCount++; }
                double structureRatio = structureCount == 0 ? 1 : structureTotal / structureCount;
                return baseRates[ball] * ((1 - structureWeight) + structureWeight * structureRatio);
            });
        return SelectGreedyRangeCoverageCombination(coverageScores);
    }

    private static Dictionary<int, double> CreateExponentialRates(
        IReadOnlyList<DrawRecord> history,
        double halfLife)
    {
        var weights = Enumerable.Range(0, history.Count)
            .Select(age => Math.Pow(0.5, age / halfLife))
            .ToArray();
        double totalWeight = weights.Sum();
        return Enumerable.Range(1, 33).ToDictionary(
            ball => ball,
            ball => history.Select((record, index) =>
                    record.RedBalls.Contains(ball) ? weights[history.Count - 1 - index] : 0)
                .Sum() / totalWeight);
    }

    internal static List<DrawRecord> GetAnnualShortHistory(
        IReadOnlyList<DrawRecord> history,
        int issue)
    {
        int targetYear = issue / 1000;
        var currentYear = history.Where(record => record.Period / 1000 == targetYear)
            .TakeLast(AnnualShortHistoryWindow)
            .ToList();
        if (currentYear.Count >= AnnualShortHistoryWindow) return currentYear;

        int needed = AnnualShortHistoryWindow - currentYear.Count;
        var previousYear = history.Where(record => record.Period / 1000 == targetYear - 1)
            .TakeLast(needed);
        return previousYear.Concat(currentYear).ToList();
    }
}
