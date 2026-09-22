using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

/// <summary>
/// Walk-forward comparison for annual short-term point profiles. Every profile delegates
/// point selection to the production selector, so it cannot read data outside its target year
/// and the immediately preceding year tail used for the 30-draw warm-up.
/// </summary>
internal static class AnnualShortPointResearch
{
    private const int MinimumHistoricalSampleSize = 100;
    private const string BaselineModel = "annual-short-40r30-35r15-25r5-structure08";
    private const string CurrentShadowModel = "annual-short-dynamic-hierarchical-ball-global";
    private const string PreregisteredModel =
        "annual-short-dynamic-hierarchical-previous-year-shared-transfer-blend05-global";
    private const int PreregisteredAfterIssue = 2026096;

    private static readonly RangeScoreProfile[] ScoreProfiles =
    {
        new("range-event-r30", 1, 0, 0),
        new("range-event-r15", 0, 1, 0),
        new("range-event-r5", 0, 0, 1),
        new("range-event-40r30-35r15-25r5", 0.40, 0.35, 0.25),
        new("dynamic-hierarchical-range", 0, 0, 0, DynamicHierarchical: true),
        new("dynamic-conditional-hierarchical-range", 0, 0, 0, ConditionalLikelihood: true),
        new("dynamic-hierarchical-shape-range", 0, 0, 0,
            DynamicHierarchical: true,
            ShapeFeatures: true),
        new("dynamic-hierarchical-ensemble-range", 0, 0, 0,
            DynamicHierarchical: true,
            Ensemble: true),
        new("position-polarization-range", 0.40, 0.35, 0.25,
            PositionPolarization: true)
    };

    private static readonly string[] SignalNames =
    {
        "momentum-r5-minus-r30",
        "momentum-r15-minus-r30",
        "acceleration-r5-r15-r30",
        "omission-high",
        "recent-lit",
        "state-streak-continuation",
        "jpf-midpoint",
        "jpf-midpoint-avoidance",
        "jpf-compound-transition",
        "jpf-tail-state",
        "stable-momentum-midpoint-rank",
        "discounted-beta-range"
    };

    private static readonly string[] DynamicFeatureNames =
    {
        "frequency-half-life-3",
        "frequency-half-life-7",
        "frequency-half-life-15",
        "omission",
        "latest-repeat",
        "latest-neighbor",
        "latest-zone-load",
        "latest-route-load",
        "latest-parity-load"
    };

    private static readonly Profile[] Profiles =
    {
        new(BaselineModel, 0.40, 0.35, 0.25, 0.08),
        new("annual-short-55r30-30r15-15r5-structure08", 0.55, 0.30, 0.15, 0.08),
        new("annual-short-30r30-45r15-25r5-structure08", 0.30, 0.45, 0.25, 0.08),
        new("annual-short-25r30-30r15-45r5-structure08", 0.25, 0.30, 0.45, 0.08),
        new("annual-short-balanced-structure08", 1.0 / 3, 1.0 / 3, 1.0 / 3, 0.08),
        new("annual-short-40r30-35r15-25r5-structure00", 0.40, 0.35, 0.25, 0.00),
        new("annual-short-40r30-35r15-25r5-structure04", 0.40, 0.35, 0.25, 0.04),
        new("annual-short-40r30-35r15-25r5-structure12", 0.40, 0.35, 0.25, 0.12),
        new("annual-short-40r30-35r15-25r5-structure16", 0.40, 0.35, 0.25, 0.16),
        new("annual-short-category04", 0.40, 0.35, 0.25, 0.04,
            PositionPredictor.AnnualShortStructureSignals.Category),
        new("annual-short-category08", 0.40, 0.35, 0.25, 0.08,
            PositionPredictor.AnnualShortStructureSignals.Category),
        new("annual-short-category12", 0.40, 0.35, 0.25, 0.12,
            PositionPredictor.AnnualShortStructureSignals.Category),
        new("annual-short-zone-route08", 0.40, 0.35, 0.25, 0.08,
            PositionPredictor.AnnualShortStructureSignals.Zone
                | PositionPredictor.AnnualShortStructureSignals.Route),
        new("annual-short-transition04", 0.40, 0.35, 0.25, 0.04,
            PositionPredictor.AnnualShortStructureSignals.Transition),
        new("annual-short-transition08", 0.40, 0.35, 0.25, 0.08,
            PositionPredictor.AnnualShortStructureSignals.Transition),
        new("annual-short-transition12", 0.40, 0.35, 0.25, 0.12,
            PositionPredictor.AnnualShortStructureSignals.Transition),
        new("annual-short-zone-route-transition08", 0.40, 0.35, 0.25, 0.08,
            PositionPredictor.AnnualShortStructureSignals.Zone
                | PositionPredictor.AnnualShortStructureSignals.Route
                | PositionPredictor.AnnualShortStructureSignals.Transition),
        new("annual-short-decay03-structure00", 0.40, 0.35, 0.25, 0.00,
            FrequencyMode: PositionPredictor.AnnualShortFrequencyMode.ExponentialDecay,
            DecayHalfLife: 3),
        new("annual-short-decay05-structure00", 0.40, 0.35, 0.25, 0.00,
            FrequencyMode: PositionPredictor.AnnualShortFrequencyMode.ExponentialDecay,
            DecayHalfLife: 5),
        new("annual-short-decay08-structure00", 0.40, 0.35, 0.25, 0.00,
            FrequencyMode: PositionPredictor.AnnualShortFrequencyMode.ExponentialDecay,
            DecayHalfLife: 8),
        new("annual-short-decay10-structure00", 0.40, 0.35, 0.25, 0.00,
            FrequencyMode: PositionPredictor.AnnualShortFrequencyMode.ExponentialDecay,
            DecayHalfLife: 10),
        new("annual-short-decay15-structure00", 0.40, 0.35, 0.25, 0.00,
            FrequencyMode: PositionPredictor.AnnualShortFrequencyMode.ExponentialDecay,
            DecayHalfLife: 15),
        new("annual-short-decay20-structure00", 0.40, 0.35, 0.25, 0.00,
            FrequencyMode: PositionPredictor.AnnualShortFrequencyMode.ExponentialDecay,
            DecayHalfLife: 20),
        new("annual-short-decay30-structure00", 0.40, 0.35, 0.25, 0.00,
            FrequencyMode: PositionPredictor.AnnualShortFrequencyMode.ExponentialDecay,
            DecayHalfLife: 30),
        new("annual-short-range-event-greedy", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventGreedy),
        new("annual-short-range-event-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventGlobal),
        new("annual-short-range-event-global-ball-tiebreak", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventGlobalBallTieBreak),
        new("annual-short-range-event-hierarchical-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventHierarchicalGlobal),
        new("annual-short-range-event-adaptive-window-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventAdaptiveWindowGlobal),
        new("annual-short-range-event-region-calibrated-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventRegionCalibratedGlobal),
        new("annual-short-range-event-state-transition-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventStateTransitionGlobal),
        new("annual-short-range-event-structure-analog-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventStructureAnalogGlobal),
        new("annual-short-range-event-one-se-ball-coverage-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventOneStandardErrorBallCoverageGlobal),
        new("annual-short-range-event-global-momentum-tiebreak", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventGlobalMomentumTieBreak),
        new("annual-short-range-event-global-jpf-compound-tiebreak", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventGlobalJpfCompoundTieBreak),
        new("annual-short-range-event-global-jpf-midpoint-avoidance-tiebreak", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventGlobalJpfMidpointAvoidanceTieBreak),
        new("annual-short-range-event-global-stable-signal-rank-tiebreak", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventGlobalStableSignalRankTieBreak),
        new("annual-short-stable-signal-rank-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.StableSignalRankGlobal),
        new("annual-short-prequential-signal-champion-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.PrequentialSignalChampionGlobal),
        new("annual-short-base-stable-rank-consensus-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.BaseStableRankConsensusGlobal),
        new("annual-short-discounted-beta-range-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.DiscountedBetaRangeGlobal),
        new(CurrentShadowModel, 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.DynamicHierarchicalBallGlobal),
        new("annual-short-dynamic-conditional-hierarchical-range-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective:
                PositionPredictor.AnnualShortSelectionObjective.DynamicConditionalHierarchicalRangeGlobal),
        new("annual-short-dynamic-stable-blend05-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective:
                PositionPredictor.AnnualShortSelectionObjective.DynamicHierarchicalStableBlendGlobal),
        new("annual-short-dynamic-hierarchical-shape-ball-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective:
                PositionPredictor.AnnualShortSelectionObjective.DynamicHierarchicalShapeBallGlobal),
        new("annual-short-dynamic-hierarchical-ensemble-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective:
                PositionPredictor.AnnualShortSelectionObjective.DynamicHierarchicalEnsembleGlobal),
        new(PreregisteredModel,
            0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective
                .DynamicHierarchicalPreviousYearSharedTransferBlendGlobal),
        new("annual-short-position-polarization-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective:
                PositionPredictor.AnnualShortSelectionObjective.PositionPolarizationGlobal),
        new("annual-short-range-event-global-55r30-30r15-15r5", 0.55, 0.30, 0.15, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventGlobal),
        new("annual-short-range-event-global-30r30-45r15-25r5", 0.30, 0.45, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventGlobal),
        new("annual-short-range-event-global-25r30-30r15-45r5", 0.25, 0.30, 0.45, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventGlobal),
        new("annual-short-range-event-global-balanced", 1.0 / 3, 1.0 / 3, 1.0 / 3, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventGlobal),
        new("annual-short-range-blend00-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventBlendGlobal,
            RangeEventWeight: 0.00),
        new("annual-short-range-blend25-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventBlendGlobal,
            RangeEventWeight: 0.25),
        new("annual-short-range-blend50-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventBlendGlobal,
            RangeEventWeight: 0.50),
        new("annual-short-range-blend75-global", 0.40, 0.35, 0.25, 0.00,
            SelectionObjective: PositionPredictor.AnnualShortSelectionObjective.RangeEventBlendGlobal,
            RangeEventWeight: 0.75)
    };

    public static AnnualShortPointResearchReport Run(IReadOnlyList<DrawRecord> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var records = source.OrderBy(record => record.Period).ToArray();
        if (records.Length < 4)
            throw new InvalidOperationException("At least four draws are required for annual short-term research.");

        int targetYear = records[^1].Period / 1000;
        var targets = records.Where(record => record.Period / 1000 == targetYear).ToArray();
        if (targets.Length < 30)
            throw new InvalidOperationException("At least thirty current-year draws are required for annual short-term research.");

        var observations = Profiles.ToDictionary(profile => profile.Name, _ => new List<Observation>(targets.Length));
        var scoreObservations = ScoreProfiles.ToDictionary(
            profile => profile.Name,
            _ => new List<RangeScoreObservation>(targets.Length));
        var signalObservations = SignalNames.ToDictionary(
            name => name,
            _ => new List<RangeScoreObservation>(targets.Length));
        var coefficientObservations = DynamicFeatureNames.ToDictionary(
            name => name,
            _ => new List<FeatureCoefficientObservation>(targets.Length));
        var columnProbabilityObservations = new List<PointColumnProbabilityObservation>(targets.Length);
        foreach (var target in targets)
        {
            var history = records.Where(record => record.Period < target.Period).ToArray();
            var scoresByProfile = new Dictionary<string, IReadOnlyDictionary<int, double>>();
            foreach (var scoreProfile in ScoreProfiles)
            {
                var scores = scoreProfile.ConditionalLikelihood
                    ? PositionPredictor.GetAnnualShortDynamicConditionalHierarchicalRangeEventScores(
                        history,
                        target.Period)
                    : scoreProfile.DynamicHierarchical
                    ? PositionPredictor.GetAnnualShortDynamicRangeEventScores(
                        history,
                        target.Period,
                        scoreProfile.ShapeFeatures,
                        scoreProfile.Ensemble)
                    : scoreProfile.PositionPolarization
                    ? PositionPredictor.GetAnnualShortPositionPolarizationRangeScores(
                        PositionPredictor.GetAnnualShortHistory(history, target.Period),
                        scoreProfile.Window30Weight,
                        scoreProfile.Window15Weight,
                        scoreProfile.Window5Weight)
                    : PositionPredictor.GetAnnualShortRangeEventScores(
                        history,
                        target.Period,
                        scoreProfile.Window30Weight,
                        scoreProfile.Window15Weight,
                        scoreProfile.Window5Weight);
                scoresByProfile[scoreProfile.Name] = scores;
                scoreObservations[scoreProfile.Name].Add(new RangeScoreObservation(
                    target.Period,
                    Enumerable.Range(2, 31).Select(point => scores[point]).ToArray(),
                    Enumerable.Range(2, 31).Select(point => target.RedBalls.Any(ball =>
                        PositionPointRange.Contains(point, ball, 33)) ? 1 : 0).ToArray()));
            }
            var shortHistory = PositionPredictor.GetAnnualShortHistory(history, target.Period);
            var coefficients = PositionPredictor.GetDynamicHierarchicalFeatureCoefficients(shortHistory);
            for (int index = 0; index < DynamicFeatureNames.Length; index++)
                coefficientObservations[DynamicFeatureNames[index]].Add(
                    new FeatureCoefficientObservation(target.Period, coefficients[index]));
            var signals = CreateRangeSignals(
                shortHistory,
                scoresByProfile["range-event-r30"],
                scoresByProfile["range-event-r15"],
                scoresByProfile["range-event-r5"]);
            foreach (string signalName in SignalNames)
            {
                signalObservations[signalName].Add(new RangeScoreObservation(
                    target.Period,
                    Enumerable.Range(2, 31).Select(point => signals[signalName][point]).ToArray(),
                    Enumerable.Range(2, 31).Select(point => target.RedBalls.Any(ball =>
                        PositionPointRange.Contains(point, ball, 33)) ? 1 : 0).ToArray()));
            }
            var pointsByModel = Profiles.ToDictionary(
                profile => profile.Name,
                profile => PositionPredictor.SelectAnnualShortTermPoints(
                    history,
                    target.Period,
                    profile.Window30Weight,
                    profile.Window15Weight,
                    profile.Window5Weight,
                    profile.StructureWeight,
                    profile.StructureSignals,
                    profile.FrequencyMode,
                    profile.DecayHalfLife,
                    profile.SelectionObjective,
                    profile.RangeEventWeight));
            var baselinePoints = pointsByModel[BaselineModel];
            var shadowPoints = pointsByModel[CurrentShadowModel];
            var shadowRangeScores = scoresByProfile["dynamic-hierarchical-range"];
            var shadowBallProbabilities = PositionPredictor
                .GetDynamicHierarchicalBallProbabilities(shortHistory);
            var shadowSelectionScores = Enumerable.Range(2, 31).ToDictionary(
                point => point,
                point => Enumerable.Range(point - PositionPointRange.Radius,
                        PositionPointRange.Radius * 2 + 1)
                    .Sum(ball => shadowBallProbabilities[ball]));
            columnProbabilityObservations.Add(new PointColumnProbabilityObservation(
                target.Period,
                shadowPoints,
                shadowPoints.Select(point => shadowRangeScores[point]).ToArray(),
                shadowPoints.Select(point => target.RedBalls.Any(ball =>
                    PositionPointRange.Contains(point, ball, 33)) ? 1 : 0).ToArray(),
                shadowPoints.Select(point =>
                {
                    int lower = shadowSelectionScores.Values.Count(score =>
                        score < shadowSelectionScores[point] - 1e-12);
                    int equal = shadowSelectionScores.Values.Count(score =>
                        Math.Abs(score - shadowSelectionScores[point]) <= 1e-12);
                    return (lower + (equal - 1) / 2.0) / (shadowSelectionScores.Count - 1);
                }).ToArray(),
                Enumerable.Range(0, shadowPoints.Count).Select(column =>
                {
                    int lower = column == 0 ? 2 : shadowPoints[column - 1] + 3;
                    int upper = column == shadowPoints.Count - 1 ? 32 : shadowPoints[column + 1] - 3;
                    return Enumerable.Range(lower, upper - lower + 1).Any(point =>
                        target.RedBalls.Any(ball => PositionPointRange.Contains(point, ball, 33)));
                }).ToArray(),
                Enumerable.Range(0, shadowPoints.Count).Select(column =>
                {
                    int lower = column == 0 ? 2 : shadowPoints[column - 1] + 3;
                    int upper = column == shadowPoints.Count - 1 ? 32 : shadowPoints[column + 1] - 3;
                    var hitScores = Enumerable.Range(lower, upper - lower + 1)
                        .Where(point => target.RedBalls.Any(ball =>
                            PositionPointRange.Contains(point, ball, 33)))
                        .Select(point => shadowSelectionScores[point])
                        .ToArray();
                    return hitScores.Length == 0
                        ? 0
                        : shadowSelectionScores[shadowPoints[column]] - hitScores.Max();
                }).ToArray()));
            int baselineHits = CountHits(baselinePoints, target);
            int baselineRangeHits = CountRangeHits(baselinePoints, target);
            foreach (var profile in Profiles)
            {
                var points = pointsByModel[profile.Name];
                observations[profile.Name].Add(new Observation(
                    target.Period,
                    CountHits(points, target),
                    CountRangeHits(points, target),
                    RandomExpectedHits(points),
                    PositionPointRange.RandomExpectedLitCount(points, 33, 6),
                    points.Select(point => target.RedBalls.Any(ball =>
                        PositionPointRange.Contains(point, ball, 33)) ? 1 : 0).ToArray(),
                    points.Select(point => PositionPointRange.RandomHitProbability(point, 33, 6)).ToArray(),
                    CountHits(points, target) - baselineHits,
                    CountRangeHits(points, target) - baselineRangeHits,
                    !points.SequenceEqual(baselinePoints),
                    points.ToArray()));
            }
        }

        var reports = Profiles.Select(profile => BuildReport(
                profile.Name,
                observations[profile.Name],
                observations[CurrentShadowModel]))
            .OrderByDescending(report => report.AverageHits)
            .ThenBy(report => report.Model)
            .ToArray();
        var scoreReports = ScoreProfiles.Select(profile => BuildRangeScoreReport(
            profile.Name,
            scoreObservations[profile.Name])).ToArray();
        var signalReports = SignalNames.Select(name => BuildRangeSignalReport(
            name,
            signalObservations[name])).ToArray();
        var coefficientReports = DynamicFeatureNames.Select(name =>
            BuildFeatureCoefficientReport(name, coefficientObservations[name])).ToArray();
        var residualDriftReports = BuildResidualDriftReports(
            scoreObservations["dynamic-hierarchical-range"]);
        var columnProbabilityReports = BuildPointColumnProbabilityReports(
            columnProbabilityObservations);
        var multipleTesting = BuildMultipleTestingReport(observations, CurrentShadowModel);
        return new AnnualShortPointResearchReport(
            records[^1].Period,
            targetYear,
            targets.Length,
            BaselineModel,
            CurrentShadowModel,
            MinimumHistoricalSampleSize,
            PositionValidationStore.MinimumPromotionPairedSampleSize,
            true,
            multipleTesting,
            scoreReports,
            signalReports,
            coefficientReports,
            residualDriftReports,
            columnProbabilityReports,
            reports)
        {
            PreregisteredEvaluation = BuildPreregisteredEvaluation(observations)
        };
    }

    private static AnnualShortPreregisteredEvaluation BuildPreregisteredEvaluation(
        IReadOnlyDictionary<string, List<Observation>> observations)
    {
        var candidate = observations[PreregisteredModel]
            .Where(value => value.Issue > PreregisteredAfterIssue)
            .ToArray();
        var shadow = observations[CurrentShadowModel]
            .Where(value => value.Issue > PreregisteredAfterIssue)
            .ToArray();
        if (candidate.Length != shadow.Length)
            throw new InvalidOperationException("Preregistered and shadow sample sizes differ.");
        if (candidate.Length == 0)
            return new AnnualShortPreregisteredEvaluation(
                PreregisteredModel,
                PreregisteredAfterIssue,
                PreregisteredAfterIssue + 1,
                0,
                null,
                null,
                null,
                null,
                Array.Empty<double>());

        return new AnnualShortPreregisteredEvaluation(
            PreregisteredModel,
            PreregisteredAfterIssue,
            PreregisteredAfterIssue + 1,
            candidate.Length,
            candidate.Average(value => value.Hits),
            candidate.Average(value => value.RangeHits),
            candidate.Zip(shadow, (left, right) => left.Hits - right.Hits).Average(),
            candidate.Zip(shadow, (left, right) => left.RangeHits - right.RangeHits).Average(),
            Enumerable.Range(0, 6).Select(column => candidate.Zip(
                shadow,
                (left, right) => left.PointColumnHits[column] - right.PointColumnHits[column])
                .Average()).ToArray());
    }

    private static IReadOnlyList<AnnualShortPointColumnProbabilityReport>
        BuildPointColumnProbabilityReports(
            IReadOnlyList<PointColumnProbabilityObservation> observations) =>
        Enumerable.Range(0, 6).Select(column =>
        {
            var values = observations.Select(observation => new PointColumnProbabilityValue(
                observation.Issue,
                observation.Points[column],
                observation.Probabilities[column],
                observation.Outcomes[column],
                observation.SelectionPercentiles[column],
                observation.FeasibleHitExists[column],
                observation.SelectedMinusBestHitScores[column])).ToArray();
            int half = values.Length / 2;
            var firstHalf = values.Take(half).ToArray();
            var secondHalf = values.Skip(half).ToArray();
            var recent = values.TakeLast(Math.Min(10, values.Length)).ToArray();
            double randomProbability = PositionPointRange.RandomHitProbability(2, 33, 6);
            return new AnnualShortPointColumnProbabilityReport(
                column + 1,
                values.Length,
                values.Average(value => value.Outcome),
                values.Average(value => value.Probability),
                values.Average(value => value.Probability - value.Outcome),
                Brier(values),
                values.Average(value => Math.Pow(randomProbability - value.Outcome, 2)),
                BinaryAuc(values),
                firstHalf.Length == 0 ? 0 : Brier(firstHalf),
                secondHalf.Length == 0 ? 0 : Brier(secondHalf),
                recent.Average(value => value.Outcome),
                Brier(recent),
                values.Average(value => value.SelectionPercentile),
                values.Average(value => value.Point),
                values.Count(value => value.Outcome == 0 && value.FeasibleHitExists)
                    / (double)Math.Max(1, values.Count(value => value.Outcome == 0)),
                values.Where(value => value.Outcome == 0 && value.FeasibleHitExists)
                    .Select(value => value.SelectedMinusBestHitScore)
                    .DefaultIfEmpty(0)
                    .Average(),
                BuildPointColumnProbabilitySegments(values, 5));
        }).ToArray();

    private static IReadOnlyList<AnnualShortPointColumnProbabilitySegmentReport>
        BuildPointColumnProbabilitySegments(
            IReadOnlyList<PointColumnProbabilityValue> values,
            int segmentCount)
    {
        var segments = new List<AnnualShortPointColumnProbabilitySegmentReport>(segmentCount);
        for (int segment = 0; segment < segmentCount; segment++)
        {
            int start = segment * values.Count / segmentCount;
            int end = (segment + 1) * values.Count / segmentCount;
            if (end <= start) continue;
            var current = values.Skip(start).Take(end - start).ToArray();
            segments.Add(new AnnualShortPointColumnProbabilitySegmentReport(
                segment + 1,
                current[0].Issue,
                current[^1].Issue,
                current.Length,
                current.Average(value => value.Outcome),
                current.Average(value => value.Probability),
                Brier(current),
                current.Average(value => value.SelectionPercentile)));
        }
        return segments;
    }

    private static double Brier(IReadOnlyList<PointColumnProbabilityValue> values) =>
        values.Average(value => Math.Pow(value.Probability - value.Outcome, 2));

    private static double BinaryAuc(IReadOnlyList<PointColumnProbabilityValue> values)
    {
        double wins = 0;
        int pairs = 0;
        foreach (var positive in values.Where(value => value.Outcome == 1))
        foreach (var negative in values.Where(value => value.Outcome == 0))
        {
            pairs++;
            if (positive.Probability > negative.Probability) wins++;
            else if (Math.Abs(positive.Probability - negative.Probability) <= 1e-12) wins += 0.5;
        }
        return pairs == 0 ? 0.5 : wins / pairs;
    }

    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<int, double>> CreateRangeSignals(
        IReadOnlyList<DrawRecord> history,
        IReadOnlyDictionary<int, double> rate30,
        IReadOnlyDictionary<int, double> rate15,
        IReadOnlyDictionary<int, double> rate5)
    {
        bool IsLit(DrawRecord record, int point) => record.RedBalls.Any(ball =>
            PositionPointRange.Contains(point, ball, 33));

        var signals = SignalNames.ToDictionary(
            name => name,
            _ => new Dictionary<int, double>());
        var jpfMidpointScores = PositionPredictor.GetJpfMidpointSupportScores(history);
        var jpfCompoundScores = PositionPredictor.GetJpfCompoundTransitionScores(history);
        var stableRankScores = PositionPredictor.GetAnnualShortStableSignalRankScores(history);
        var discountedBetaScores = PositionPredictor.GetDiscountedBetaRangeScores(history);
        foreach (int point in Enumerable.Range(2, 31))
        {
            signals["momentum-r5-minus-r30"][point] = rate5[point] - rate30[point];
            signals["momentum-r15-minus-r30"][point] = rate15[point] - rate30[point];
            signals["acceleration-r5-r15-r30"][point] =
                (rate5[point] - rate15[point]) - (rate15[point] - rate30[point]);

            int omission = 0;
            for (int index = history.Count - 1; index >= 0; index--)
            {
                if (IsLit(history[index], point)) break;
                omission++;
            }
            bool recentLit = history.Count > 0 && IsLit(history[^1], point);
            int streak = 0;
            for (int index = history.Count - 1; index >= 0; index--)
            {
                if (IsLit(history[index], point) != recentLit) break;
                streak++;
            }
            signals["omission-high"][point] = omission;
            signals["recent-lit"][point] = recentLit ? 1 : 0;
            signals["state-streak-continuation"][point] = recentLit ? streak : -streak;
            signals["jpf-midpoint"][point] = jpfMidpointScores[point];
            signals["jpf-midpoint-avoidance"][point] = -jpfMidpointScores[point];
            signals["jpf-compound-transition"][point] = jpfCompoundScores[point];
            signals["jpf-tail-state"][point] = CountLatestTailStateSupport(history, point);
            signals["stable-momentum-midpoint-rank"][point] = stableRankScores[point];
            signals["discounted-beta-range"][point] = discountedBetaScores[point];
        }
        return signals.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<int, double>)pair.Value);
    }

    private static int CountLatestTailStateSupport(IReadOnlyList<DrawRecord> history, int point)
    {
        if (history.Count == 0) return 0;

        var rangeTails = Enumerable.Range(point - PositionPointRange.Radius,
                PositionPointRange.Radius * 2 + 1)
            .Select(ball => ball % 10)
            .ToHashSet();
        var tailGroups = history[^1].RedBalls
            .GroupBy(ball => ball % 10)
            .ToArray();
        int support = tailGroups
            .Where(group => rangeTails.Contains(group.Key))
            .Sum(group => group.Count());
        if (tailGroups.Any(group => rangeTails.Contains(group.Key) && group.Count() >= 2))
            support++;
        return support;
    }

    private static AnnualShortRangeScoreReport BuildRangeScoreReport(
        string model,
        IReadOnlyList<RangeScoreObservation> observations)
    {
        var pairs = observations.SelectMany(observation => observation.Scores.Select(
            (score, index) => new { Score = score, Outcome = observation.Outcomes[index] })).ToArray();
        double randomProbability = PositionPointRange.RandomHitProbability(2, 33, 6);
        int half = observations.Count / 2;
        return new AnnualShortRangeScoreReport(
            model,
            observations.Count,
            pairs.Length,
            pairs.Average(pair => Math.Pow(pair.Score - pair.Outcome, 2)),
            pairs.Average(pair => Math.Pow(randomProbability - pair.Outcome, 2)),
            observations.Average(CalculateWithinIssueAuc),
            half == 0 ? 0 : observations.Take(half).Average(CalculateWithinIssueAuc),
            half == 0 ? 0 : observations.Skip(half).Average(CalculateWithinIssueAuc),
            pairs.Average(pair => pair.Score - pair.Outcome),
            BuildRangeScoreSegments(observations, 5));
    }

    private static AnnualShortRangeSignalReport BuildRangeSignalReport(
        string signal,
        IReadOnlyList<RangeScoreObservation> observations)
    {
        int half = observations.Count / 2;
        return new AnnualShortRangeSignalReport(
            signal,
            observations.Count,
            observations.Sum(observation => observation.Scores.Count),
            observations.Average(CalculateWithinIssueAuc),
            half == 0 ? 0 : observations.Take(half).Average(CalculateWithinIssueAuc),
            half == 0 ? 0 : observations.Skip(half).Average(CalculateWithinIssueAuc),
            BuildRangeSignalSegments(observations, 5));
    }

    private static AnnualShortFeatureCoefficientReport BuildFeatureCoefficientReport(
        string feature,
        IReadOnlyList<FeatureCoefficientObservation> observations)
    {
        int half = observations.Count / 2;
        double positiveShare = observations.Count(value => value.Coefficient > 0)
            / (double)observations.Count;
        double negativeShare = observations.Count(value => value.Coefficient < 0)
            / (double)observations.Count;
        double zeroShare = 1 - positiveShare - negativeShare;
        return new AnnualShortFeatureCoefficientReport(
            feature,
            observations.Count,
            observations.Average(value => value.Coefficient),
            half == 0 ? 0 : observations.Take(half).Average(value => value.Coefficient),
            half == 0 ? 0 : observations.Skip(half).Average(value => value.Coefficient),
            positiveShare,
            Math.Max(positiveShare, negativeShare) + zeroShare,
            BuildFeatureCoefficientSegments(observations, 5));
    }

    private static IReadOnlyList<AnnualShortResidualDriftReport> BuildResidualDriftReports(
        IReadOnlyList<RangeScoreObservation> observations)
    {
        return
        [
            BuildResidualDriftReport(
                "calibration-residual",
                observations,
                observation => observation.Scores.Zip(
                    observation.Outcomes,
                    (score, outcome) => score - outcome).Average()),
            BuildResidualDriftReport(
                "brier-loss",
                observations,
                observation => observation.Scores.Zip(
                    observation.Outcomes,
                    (score, outcome) => Math.Pow(score - outcome, 2)).Average()),
            BuildResidualDriftReport(
                "within-issue-auc",
                observations,
                CalculateWithinIssueAuc)
        ];
    }

    private static AnnualShortResidualDriftReport BuildResidualDriftReport(
        string metric,
        IReadOnlyList<RangeScoreObservation> observations,
        Func<RangeScoreObservation, double> valueSelector)
    {
        var values = new List<double>(observations.Count);
        var diagnostics = new List<AnnualShortResidualChangeObservation>(observations.Count);
        foreach (var observation in observations)
        {
            values.Add(valueSelector(observation));
            diagnostics.Add(DetectSingleMeanChange(observation.Issue, observations, values));
        }
        return new AnnualShortResidualDriftReport(metric, observations.Count, diagnostics);
    }

    private static AnnualShortResidualChangeObservation DetectSingleMeanChange(
        int issue,
        IReadOnlyList<RangeScoreObservation> observations,
        IReadOnlyList<double> values)
    {
        const int minimumSegmentSize = 2;
        int count = values.Count;
        if (count < minimumSegmentSize * 2)
            return new AnnualShortResidualChangeObservation(issue, false, null, 0, values.Average(), null);

        double totalMean = values.Average();
        double noChangeSse = values.Sum(value => Math.Pow(value - totalMean, 2));
        double bestChangeSse = double.PositiveInfinity;
        int bestSplit = minimumSegmentSize;
        double bestRecentMean = 0;
        for (int split = minimumSegmentSize; split <= count - minimumSegmentSize; split++)
        {
            double earlyMean = values.Take(split).Average();
            double recentMean = values.Skip(split).Average();
            double sse = values.Take(split).Sum(value => Math.Pow(value - earlyMean, 2))
                + values.Skip(split).Sum(value => Math.Pow(value - recentMean, 2));
            if (sse < bestChangeSse - 1e-15)
            {
                bestChangeSse = sse;
                bestSplit = split;
                bestRecentMean = recentMean;
            }
        }

        double noChangeBic = GaussianBic(noChangeSse, count, parameterCount: 2);
        double changeBic = GaussianBic(bestChangeSse, count, parameterCount: 4);
        bool detected = changeBic < noChangeBic;
        return new AnnualShortResidualChangeObservation(
            issue,
            detected,
            detected ? observations[bestSplit].Issue : null,
            noChangeBic - changeBic,
            totalMean,
            detected ? bestRecentMean : null);
    }

    private static double GaussianBic(double sse, int sampleSize, int parameterCount) =>
        sampleSize * Math.Log(Math.Max(sse / sampleSize, 1e-15))
        + parameterCount * Math.Log(sampleSize);

    private static IReadOnlyList<AnnualShortFeatureCoefficientSegmentReport>
        BuildFeatureCoefficientSegments(
            IReadOnlyList<FeatureCoefficientObservation> observations,
            int segmentCount)
    {
        var segments = new List<AnnualShortFeatureCoefficientSegmentReport>(segmentCount);
        for (int segment = 0; segment < segmentCount; segment++)
        {
            int start = segment * observations.Count / segmentCount;
            int end = (segment + 1) * observations.Count / segmentCount;
            if (end <= start) continue;
            var values = observations.Skip(start).Take(end - start).ToArray();
            segments.Add(new AnnualShortFeatureCoefficientSegmentReport(
                segment + 1,
                values[0].Issue,
                values[^1].Issue,
                values.Length,
                values.Average(value => value.Coefficient),
                values.Count(value => value.Coefficient > 0) / (double)values.Length));
        }
        return segments;
    }

    private static IReadOnlyList<AnnualShortRangeSignalSegmentReport> BuildRangeSignalSegments(
        IReadOnlyList<RangeScoreObservation> observations,
        int segmentCount)
    {
        var segments = new List<AnnualShortRangeSignalSegmentReport>(segmentCount);
        for (int segment = 0; segment < segmentCount; segment++)
        {
            int start = segment * observations.Count / segmentCount;
            int end = (segment + 1) * observations.Count / segmentCount;
            if (end <= start) continue;
            var values = observations.Skip(start).Take(end - start).ToArray();
            segments.Add(new AnnualShortRangeSignalSegmentReport(
                segment + 1,
                values[0].Issue,
                values[^1].Issue,
                values.Length,
                values.Average(CalculateWithinIssueAuc)));
        }
        return segments;
    }

    private static IReadOnlyList<AnnualShortRangeScoreSegmentReport> BuildRangeScoreSegments(
        IReadOnlyList<RangeScoreObservation> observations,
        int segmentCount)
    {
        var segments = new List<AnnualShortRangeScoreSegmentReport>(segmentCount);
        for (int segment = 0; segment < segmentCount; segment++)
        {
            int start = segment * observations.Count / segmentCount;
            int end = (segment + 1) * observations.Count / segmentCount;
            if (end <= start) continue;
            var values = observations.Skip(start).Take(end - start).ToArray();
            var pairs = values.SelectMany(observation => observation.Scores.Select(
                (score, index) => new { Score = score, Outcome = observation.Outcomes[index] })).ToArray();
            segments.Add(new AnnualShortRangeScoreSegmentReport(
                segment + 1,
                values[0].Issue,
                values[^1].Issue,
                values.Length,
                pairs.Average(pair => Math.Pow(pair.Score - pair.Outcome, 2)),
                values.Average(CalculateWithinIssueAuc),
                pairs.Average(pair => pair.Score - pair.Outcome)));
        }
        return segments;
    }

    private static double CalculateWithinIssueAuc(RangeScoreObservation observation)
    {
        double wins = 0;
        int pairs = 0;
        for (int positive = 0; positive < observation.Outcomes.Count; positive++)
        {
            if (observation.Outcomes[positive] == 0) continue;
            for (int negative = 0; negative < observation.Outcomes.Count; negative++)
            {
                if (observation.Outcomes[negative] != 0) continue;
                pairs++;
                if (observation.Scores[positive] > observation.Scores[negative]) wins++;
                else if (Math.Abs(observation.Scores[positive] - observation.Scores[negative]) <= 1e-12)
                    wins += 0.5;
            }
        }
        return pairs == 0 ? 0.5 : wins / pairs;
    }

    private static AnnualShortPointModelReport BuildReport(
        string model,
        IReadOnlyList<Observation> observations,
        IReadOnlyList<Observation> currentShadowObservations)
    {
        if (observations.Count != currentShadowObservations.Count)
            throw new InvalidOperationException("Annual short-term comparison sample sizes differ.");
        var hitValues = observations.Select(value => (double)value.Hits).ToArray();
        var liftValues = observations.Select(value => value.Hits - value.RandomExpectedHits).ToArray();
        var rangeLiftValues = observations.Select(value =>
            value.RangeHits - value.RandomExpectedRangeHits).ToArray();
        var advantages = observations.Select(value => (double)value.AdvantageToBaseline).ToArray();
        var rangeAdvantages = observations.Select(value => (double)value.RangeAdvantageToBaseline).ToArray();
        var shadowAdvantages = observations.Zip(
            currentShadowObservations,
            (candidate, shadow) => (double)(candidate.Hits - shadow.Hits)).ToArray();
        var shadowRangeAdvantages = observations.Zip(
            currentShadowObservations,
            (candidate, shadow) => (double)(candidate.RangeHits - shadow.RangeHits)).ToArray();
        int differentFromCurrentShadowCount = observations.Zip(
            currentShadowObservations,
            (candidate, shadow) => !candidate.Points.SequenceEqual(shadow.Points))
            .Count(different => different);
        int pointColumnCount = observations[0].Points.Count;
        var pointColumnHitRates = Enumerable.Range(0, pointColumnCount)
            .Select(index => observations.Average(value => value.PointColumnHits[index]))
            .ToArray();
        var currentShadowPointColumnHitRates = Enumerable.Range(0, pointColumnCount)
            .Select(index => currentShadowObservations.Average(value => value.PointColumnHits[index]))
            .ToArray();
        var pointColumnRandomRates = Enumerable.Range(0, pointColumnCount)
            .Select(index => observations.Average(value => value.PointColumnRandomRates[index]))
            .ToArray();
        var pointColumnLifts = pointColumnHitRates.Zip(
            pointColumnRandomRates,
            (actual, expected) => actual - expected).ToArray();
        var pointColumnAdvantagesToCurrentShadow = pointColumnHitRates.Zip(
            currentShadowPointColumnHitRates,
            (candidate, shadow) => candidate - shadow).ToArray();
        int adjacentCount = Math.Max(0, observations.Count - 1);
        double exactRepeatRate = adjacentCount == 0 ? 0
            : observations.Zip(observations.Skip(1), (left, right) => left.Points.SequenceEqual(right.Points))
                .Count(repeated => repeated) / (double)adjacentCount;
        double currentShadowExactRepeatRate = adjacentCount == 0 ? 0
            : currentShadowObservations.Zip(
                    currentShadowObservations.Skip(1),
                    (left, right) => left.Points.SequenceEqual(right.Points))
                .Count(repeated => repeated) / (double)adjacentCount;
        double averageRetainedPoints = adjacentCount == 0 ? 0
            : observations.Zip(observations.Skip(1), (left, right) => left.Points.Intersect(right.Points).Count())
                .Average();
        double currentShadowAverageRetainedPoints = adjacentCount == 0 ? 0
            : currentShadowObservations.Zip(
                    currentShadowObservations.Skip(1),
                    (left, right) => left.Points.Intersect(right.Points).Count())
                .Average();
        var selectedPointCounts = new int[34];
        var litPointCounts = new int[34];
        foreach (var observation in observations)
        {
            for (int index = 0; index < observation.Points.Count; index++)
            {
                int point = observation.Points[index];
                selectedPointCounts[point]++;
                litPointCounts[point] += observation.PointColumnHits[index];
            }
        }
        var pointCenterDiagnostics = Enumerable.Range(1, 33)
            .Where(point => selectedPointCounts[point] > 0)
            .Select(point => new AnnualShortPointCenterReport(
                point,
                selectedPointCounts[point],
                litPointCounts[point],
                litPointCounts[point] / (double)selectedPointCounts[point],
                PositionPointRange.RandomHitProbability(point, 33, 6)))
            .ToArray();
        var pointColumnCenterDiagnostics = Enumerable.Range(0, pointColumnCount)
            .SelectMany(column => Enumerable.Range(1, 33)
                .Select(point => new
                {
                    Column = column,
                    Point = point,
                    Observations = observations.Where(value => value.Points[column] == point).ToArray()
                })
                .Where(value => value.Observations.Length > 0)
                .Select(value => new AnnualShortPointColumnCenterReport(
                    value.Column + 1,
                    value.Point,
                    value.Observations.Length,
                    value.Observations.Sum(observation => observation.PointColumnHits[value.Column]),
                    value.Observations.Average(observation =>
                        observation.PointColumnRandomRates[value.Column]))))
            .ToArray();
        int half = observations.Count / 2;
        double liftLower95 = MeanLower95(liftValues);
        double firstHalfLift = half == 0 ? 0 : liftValues.Take(half).Average();
        double secondHalfLift = half == 0 ? 0 : liftValues.Skip(half).Average();
        double rangeLiftLower95 = MeanLower95(rangeLiftValues);
        double firstHalfRangeLift = half == 0 ? 0 : rangeLiftValues.Take(half).Average();
        double secondHalfRangeLift = half == 0 ? 0 : rangeLiftValues.Skip(half).Average();
        double shadowAdvantageLower95 = MeanLower95(shadowAdvantages);
        double firstHalfShadowAdvantage = half == 0 ? 0 : shadowAdvantages.Take(half).Average();
        double secondHalfShadowAdvantage = half == 0 ? 0 : shadowAdvantages.Skip(half).Average();
        double shadowRangeAdvantageLower95 = MeanLower95(shadowRangeAdvantages);
        double firstHalfShadowRangeAdvantage = half == 0
            ? 0 : shadowRangeAdvantages.Take(half).Average();
        double secondHalfShadowRangeAdvantage = half == 0
            ? 0 : shadowRangeAdvantages.Skip(half).Average();
        bool meetsMinimumSampleSize = observations.Count >= MinimumHistoricalSampleSize;
        bool meetsAbsoluteBallCoverageGate = liftLower95 > 0
            && firstHalfLift > 0
            && secondHalfLift > 0;
        bool meetsAbsoluteRangeGate = rangeLiftLower95 > 0
            && firstHalfRangeLift > 0
            && secondHalfRangeLift > 0;
        bool meetsAbsoluteCoverageGate = meetsAbsoluteBallCoverageGate
            && meetsAbsoluteRangeGate;
        bool meetsCurrentShadowHitGate = shadowAdvantageLower95 > 0
            && firstHalfShadowAdvantage > 0
            && secondHalfShadowAdvantage > 0;
        bool meetsCurrentShadowRangeGate = shadowRangeAdvantageLower95 > 0
            && firstHalfShadowRangeAdvantage > 0
            && secondHalfShadowRangeAdvantage > 0;
        bool meetsPointColumnGate = pointColumnLifts.All(lift => lift >= 0);
        bool meetsPointColumnShadowGate = pointColumnAdvantagesToCurrentShadow
            .All(advantage => advantage >= -1e-12);
        bool meetsRepeatRateGate = exactRepeatRate <= currentShadowExactRepeatRate + 1e-12;
        bool meetsRetainedPointGate = averageRetainedPoints
            <= currentShadowAverageRetainedPoints + 1e-12;
        bool isEligibleForForwardValidation = meetsMinimumSampleSize
            && meetsAbsoluteCoverageGate
            && meetsCurrentShadowHitGate
            && meetsCurrentShadowRangeGate
            && meetsPointColumnGate
            && meetsPointColumnShadowGate
            && meetsRepeatRateGate
            && meetsRetainedPointGate;
        var segments = BuildSegments(observations, currentShadowObservations, 5);
        var latest = observations[^1];
        var latestShadow = currentShadowObservations[^1];
        int recentSampleSize = Math.Min(10, observations.Count);
        var recent = observations.TakeLast(recentSampleSize).ToArray();
        var recentShadow = currentShadowObservations.TakeLast(recentSampleSize).ToArray();
        return new AnnualShortPointModelReport(
            model,
            observations.Average(value => value.Hits),
            observations.Average(value => value.RangeHits),
            observations.Average(value => value.RandomExpectedHits),
            liftLower95,
            firstHalfLift,
            secondHalfLift,
            advantages.Average(),
            MeanLower95(advantages),
            half == 0 ? 0 : advantages.Take(half).Average(),
            half == 0 ? 0 : advantages.Skip(half).Average(),
            rangeAdvantages.Average(),
            MeanLower95(rangeAdvantages),
            half == 0 ? 0 : rangeAdvantages.Take(half).Average(),
            half == 0 ? 0 : rangeAdvantages.Skip(half).Average(),
            observations.Count(value => value.DifferentFromBaseline),
            exactRepeatRate,
            averageRetainedPoints)
        {
            Segments = segments,
            AdvantageToCurrentShadow = shadowAdvantages.Average(),
            AdvantageToCurrentShadowLower95 = shadowAdvantageLower95,
            FirstHalfAdvantageToCurrentShadow = firstHalfShadowAdvantage,
            SecondHalfAdvantageToCurrentShadow = secondHalfShadowAdvantage,
            RangeAdvantageToCurrentShadow = shadowRangeAdvantages.Average(),
            DifferentFromCurrentShadowCount = differentFromCurrentShadowCount,
            PointColumnHitRates = pointColumnHitRates,
            PointColumnRandomRates = pointColumnRandomRates,
            PointColumnLifts = pointColumnLifts,
            WeakestPointColumnLift = pointColumnLifts.Min(),
            PointColumnAdvantagesToCurrentShadow = pointColumnAdvantagesToCurrentShadow,
            WeakestPointColumnAdvantageToCurrentShadow = pointColumnAdvantagesToCurrentShadow.Min(),
            PointCenterDiagnostics = pointCenterDiagnostics,
            PointColumnCenterDiagnostics = pointColumnCenterDiagnostics,
            RandomExpectedRangeHits = observations.Average(value => value.RandomExpectedRangeHits),
            RangeLiftLower95 = rangeLiftLower95,
            FirstHalfRangeLift = firstHalfRangeLift,
            SecondHalfRangeLift = secondHalfRangeLift,
            RangeAdvantageToCurrentShadowLower95 = shadowRangeAdvantageLower95,
            FirstHalfRangeAdvantageToCurrentShadow = firstHalfShadowRangeAdvantage,
            SecondHalfRangeAdvantageToCurrentShadow = secondHalfShadowRangeAdvantage,
            MeetsMinimumSampleSize = meetsMinimumSampleSize,
            MeetsAbsoluteBallCoverageGate = meetsAbsoluteBallCoverageGate,
            MeetsAbsoluteRangeGate = meetsAbsoluteRangeGate,
            MeetsAbsoluteCoverageGate = meetsAbsoluteCoverageGate,
            MeetsCurrentShadowHitGate = meetsCurrentShadowHitGate,
            MeetsCurrentShadowRangeGate = meetsCurrentShadowRangeGate,
            MeetsPointColumnGate = meetsPointColumnGate,
            MeetsPointColumnShadowGate = meetsPointColumnShadowGate,
            MeetsRepeatRateGate = meetsRepeatRateGate,
            MeetsRetainedPointGate = meetsRetainedPointGate,
            IsEligibleForForwardValidation = isEligibleForForwardValidation,
            WorstSegmentAdvantageToCurrentShadow = segments.Min(segment =>
                segment.AdvantageToCurrentShadow),
            WorstSegmentRangeAdvantageToCurrentShadow = segments.Min(segment =>
                segment.RangeAdvantageToCurrentShadow),
            LatestIssue = latest.Issue,
            LatestPoints = latest.Points,
            LatestHits = latest.Hits,
            LatestRangeHits = latest.RangeHits,
            LatestAdvantageToCurrentShadow = latest.Hits - latestShadow.Hits,
            LatestRangeAdvantageToCurrentShadow = latest.RangeHits - latestShadow.RangeHits,
            LatestDifferentFromCurrentShadow = !latest.Points.SequenceEqual(latestShadow.Points),
            RecentSampleSize = recentSampleSize,
            RecentAverageHits = recent.Average(value => value.Hits),
            RecentAverageRangeHits = recent.Average(value => value.RangeHits),
            RecentAdvantageToCurrentShadow = recent.Zip(
                recentShadow,
                (candidate, shadow) => candidate.Hits - shadow.Hits).Average(),
            RecentRangeAdvantageToCurrentShadow = recent.Zip(
                recentShadow,
                (candidate, shadow) => candidate.RangeHits - shadow.RangeHits).Average(),
            RecentPointColumnHitRates = Enumerable.Range(0, pointColumnCount)
                .Select(index => recent.Average(value => value.PointColumnHits[index]))
                .ToArray(),
            ResearchVerdict = BuildResearchVerdict(
                meetsMinimumSampleSize,
                meetsAbsoluteBallCoverageGate,
                meetsAbsoluteRangeGate,
                meetsCurrentShadowHitGate,
                meetsCurrentShadowRangeGate,
                meetsPointColumnGate,
                meetsPointColumnShadowGate,
                meetsRepeatRateGate,
                meetsRetainedPointGate)
        };
    }

    private static AnnualShortMultipleTestingReport BuildMultipleTestingReport(
        IReadOnlyDictionary<string, List<Observation>> observations,
        string benchmarkModel)
    {
        var benchmark = observations[benchmarkModel];
        var candidates = observations.Keys
            .Where(model => !string.Equals(model, benchmarkModel, StringComparison.Ordinal))
            .OrderBy(model => model, StringComparer.Ordinal)
            .ToArray();
        var hitDifferences = candidates.ToDictionary(
            model => model,
            model => observations[model].Zip(
                benchmark,
                (candidate, baseline) => (double)(candidate.Hits - baseline.Hits)).ToArray());
        var rangeDifferences = candidates.ToDictionary(
            model => model,
            model => observations[model].Zip(
                benchmark,
                (candidate, baseline) => (double)(candidate.RangeHits - baseline.RangeHits)).ToArray());
        const int bootstrapReplications = 2000;
        int blockLength = Math.Max(2, (int)Math.Round(Math.Sqrt(benchmark.Count)));
        return new AnnualShortMultipleTestingReport(
            candidates.Length,
            benchmark.Count,
            bootstrapReplications,
            blockLength,
            BuildFamilyTest(hitDifferences, bootstrapReplications, blockLength, 20260803),
            BuildFamilyTest(rangeDifferences, bootstrapReplications, blockLength, 20260804));
    }

    private static AnnualShortFamilyTestReport BuildFamilyTest(
        IReadOnlyDictionary<string, double[]> differences,
        int bootstrapReplications,
        int blockLength,
        int seed)
    {
        int sampleSize = differences.First().Value.Length;
        var means = differences.ToDictionary(pair => pair.Key, pair => pair.Value.Average());
        var standardDeviations = differences.ToDictionary(
            pair => pair.Key,
            pair => SampleStandardDeviation(pair.Value));
        string bestModel = means.OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .First().Key;
        double bestMean = means[bestModel];
        double spaCutoff = -Math.Sqrt(2 * Math.Log(Math.Max(Math.Log(sampleSize), 1.000001)) / sampleSize);
        var spaModels = differences.Keys
            .Where(model => standardDeviations[model] <= 1e-12
                ? means[model] >= 0
                : means[model] / standardDeviations[model] >= spaCutoff)
            .ToArray();
        if (spaModels.Length == 0) spaModels = [bestModel];

        double observedSpa = spaModels.Max(model => StudentizedMean(
            means[model],
            standardDeviations[model],
            sampleSize));
        var realityMaxima = new double[bootstrapReplications];
        var spaMaxima = new double[bootstrapReplications];
        var indices = new int[sampleSize];
        var random = new Random(seed);
        for (int replication = 0; replication < bootstrapReplications; replication++)
        {
            int offset = 0;
            while (offset < sampleSize)
            {
                int start = random.Next(sampleSize);
                for (int step = 0; step < blockLength && offset < sampleSize; step++)
                    indices[offset++] = (start + step) % sampleSize;
            }

            double realityMaximum = double.NegativeInfinity;
            double spaMaximum = double.NegativeInfinity;
            foreach (var pair in differences)
            {
                double centeredMean = indices.Average(index => pair.Value[index] - means[pair.Key]);
                realityMaximum = Math.Max(realityMaximum, centeredMean);
                if (spaModels.Contains(pair.Key))
                    spaMaximum = Math.Max(spaMaximum, StudentizedMean(
                        centeredMean,
                        standardDeviations[pair.Key],
                        sampleSize));
            }
            realityMaxima[replication] = realityMaximum;
            spaMaxima[replication] = spaMaximum;
        }

        Array.Sort(realityMaxima);
        double realityPValue = (1 + realityMaxima.Count(value => value >= bestMean - 1e-12))
            / (bootstrapReplications + 1.0);
        double spaPValue = (1 + spaMaxima.Count(value => value >= observedSpa - 1e-12))
            / (bootstrapReplications + 1.0);
        return new AnnualShortFamilyTestReport(
            bestModel,
            bestMean,
            realityPValue,
            bestMean - Quantile(realityMaxima, 0.95),
            observedSpa,
            spaPValue,
            spaModels.Length);
    }

    private static double SampleStandardDeviation(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        double mean = values.Average();
        return Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1));
    }

    private static double StudentizedMean(double mean, double standardDeviation, int sampleSize) =>
        Math.Sqrt(sampleSize) * mean / Math.Max(standardDeviation, 1e-9);

    private static double Quantile(IReadOnlyList<double> sortedValues, double probability)
    {
        double position = probability * (sortedValues.Count - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper) return sortedValues[lower];
        double fraction = position - lower;
        return sortedValues[lower] * (1 - fraction) + sortedValues[upper] * fraction;
    }

    private static string BuildResearchVerdict(
        bool meetsMinimumSampleSize,
        bool meetsAbsoluteBallCoverageGate,
        bool meetsAbsoluteRangeGate,
        bool meetsCurrentShadowHitGate,
        bool meetsCurrentShadowRangeGate,
        bool meetsPointColumnGate,
        bool meetsPointColumnShadowGate,
        bool meetsRepeatRateGate,
        bool meetsRetainedPointGate)
    {
        var failures = new List<string>();
        if (!meetsMinimumSampleSize) failures.Add($"历史样本不足{MinimumHistoricalSampleSize}期");
        if (!meetsAbsoluteBallCoverageGate) failures.Add("红球覆盖未稳定超过随机基线");
        if (!meetsAbsoluteRangeGate) failures.Add("点位点亮未稳定超过随机基线");
        if (!meetsCurrentShadowHitGate) failures.Add("红球覆盖未稳定超过当前影子");
        if (!meetsCurrentShadowRangeGate) failures.Add("点位点亮未稳定超过当前影子");
        if (!meetsPointColumnGate) failures.Add("point-column non-inferiority gate failed");
        if (!meetsPointColumnShadowGate) failures.Add("point-column shadow non-inferiority gate failed");
        if (!meetsRepeatRateGate) failures.Add("repeat-rate gate failed");
        if (!meetsRetainedPointGate) failures.Add("retained-point gate failed");
        return failures.Count == 0
            ? "历史门槛通过，仅可进入开奖前密封的真实前向验证"
            : string.Join("；", failures);
    }

    private static IReadOnlyList<AnnualShortPointSegmentReport> BuildSegments(
        IReadOnlyList<Observation> observations,
        IReadOnlyList<Observation> currentShadowObservations,
        int segmentCount)
    {
        if (observations.Count != currentShadowObservations.Count)
            throw new InvalidOperationException("Annual short-term segment sample sizes differ.");
        var segments = new List<AnnualShortPointSegmentReport>(segmentCount);
        for (int segment = 0; segment < segmentCount; segment++)
        {
            int start = segment * observations.Count / segmentCount;
            int end = (segment + 1) * observations.Count / segmentCount;
            if (end <= start) continue;
            var values = observations.Skip(start).Take(end - start).ToArray();
            var shadowValues = currentShadowObservations.Skip(start).Take(end - start).ToArray();
            segments.Add(new AnnualShortPointSegmentReport(
                segment + 1,
                values[0].Issue,
                values[^1].Issue,
                values.Length,
                values.Average(value => value.Hits),
                values.Average(value => value.RangeHits),
                values.Average(value => value.AdvantageToBaseline),
                values.Average(value => value.RangeAdvantageToBaseline),
                values.Zip(shadowValues, (candidate, shadow) => candidate.Hits - shadow.Hits).Average(),
                values.Zip(shadowValues, (candidate, shadow) =>
                    candidate.RangeHits - shadow.RangeHits).Average()));
        }
        return segments;
    }

    private static int CountHits(IReadOnlyList<int> points, DrawRecord target) =>
        target.RedBalls.Count(ball => points.Any(point => PositionPointRange.Contains(point, ball, 33)));

    private static int CountRangeHits(IReadOnlyList<int> points, DrawRecord target) =>
        points.Count(point => target.RedBalls.Any(ball => PositionPointRange.Contains(point, ball, 33)));

    private static double RandomExpectedHits(IReadOnlyList<int> points)
    {
        int covered = points.SelectMany(point => Enumerable.Range(
                Math.Max(1, point - PositionPointRange.Radius),
                Math.Min(33, point + PositionPointRange.Radius)
                    - Math.Max(1, point - PositionPointRange.Radius) + 1))
            .Distinct()
            .Count();
        return 6.0 * covered / 33.0;
    }

    private static double MeanLower95(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double mean = values.Average();
        if (values.Count == 1) return mean;
        double variance = values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1);
        const double z = 1.959963984540054;
        return mean - z * Math.Sqrt(variance / values.Count);
    }

    private sealed record Profile(
        string Name,
        double Window30Weight,
        double Window15Weight,
        double Window5Weight,
        double StructureWeight,
        PositionPredictor.AnnualShortStructureSignals StructureSignals =
            PositionPredictor.AnnualShortStructureSignals.All,
        PositionPredictor.AnnualShortFrequencyMode FrequencyMode =
            PositionPredictor.AnnualShortFrequencyMode.MultiWindow,
        double DecayHalfLife = 0,
        PositionPredictor.AnnualShortSelectionObjective SelectionObjective =
            PositionPredictor.AnnualShortSelectionObjective.BallCoverage,
        double RangeEventWeight = 1.0);

    private sealed record RangeScoreProfile(
        string Name,
        double Window30Weight,
        double Window15Weight,
        double Window5Weight,
        bool DynamicHierarchical = false,
        bool ShapeFeatures = false,
        bool Ensemble = false,
        bool ConditionalLikelihood = false,
        bool PositionPolarization = false);

    private sealed record RangeScoreObservation(
        int Issue,
        IReadOnlyList<double> Scores,
        IReadOnlyList<int> Outcomes);

    private sealed record FeatureCoefficientObservation(int Issue, double Coefficient);

    private sealed record PointColumnProbabilityObservation(
        int Issue,
        IReadOnlyList<int> Points,
        IReadOnlyList<double> Probabilities,
        IReadOnlyList<int> Outcomes,
        IReadOnlyList<double> SelectionPercentiles,
        IReadOnlyList<bool> FeasibleHitExists,
        IReadOnlyList<double> SelectedMinusBestHitScores);

    private sealed record PointColumnProbabilityValue(
        int Issue,
        int Point,
        double Probability,
        int Outcome,
        double SelectionPercentile,
        bool FeasibleHitExists,
        double SelectedMinusBestHitScore);

    private sealed record Observation(
        int Issue,
        int Hits,
        int RangeHits,
        double RandomExpectedHits,
        double RandomExpectedRangeHits,
        IReadOnlyList<int> PointColumnHits,
        IReadOnlyList<double> PointColumnRandomRates,
        int AdvantageToBaseline,
        int RangeAdvantageToBaseline,
        bool DifferentFromBaseline,
        IReadOnlyList<int> Points);
}

internal sealed record AnnualShortPointResearchReport(
    int DataAsOfIssue,
    int PredictionYear,
    int SampleSize,
    string BaselineModel,
    string CurrentShadowModel,
    int MinimumHistoricalSampleSize,
    int MinimumForwardPairedSampleSize,
    bool RequiresSealedForwardValidation,
    AnnualShortMultipleTestingReport MultipleTesting,
    IReadOnlyList<AnnualShortRangeScoreReport> ScoreDiagnostics,
    IReadOnlyList<AnnualShortRangeSignalReport> SignalDiagnostics,
    IReadOnlyList<AnnualShortFeatureCoefficientReport> FeatureCoefficientDiagnostics,
    IReadOnlyList<AnnualShortResidualDriftReport> ResidualDriftDiagnostics,
    IReadOnlyList<AnnualShortPointColumnProbabilityReport> PointColumnProbabilityDiagnostics,
    IReadOnlyList<AnnualShortPointModelReport> Models)
{
    public AnnualShortPreregisteredEvaluation PreregisteredEvaluation { get; init; } = null!;
}

internal sealed record AnnualShortPreregisteredEvaluation(
    string Model,
    int LockedAfterIssue,
    int FirstEligibleIssue,
    int SampleSize,
    double? AverageHits,
    double? AverageRangeHits,
    double? AdvantageToCurrentShadow,
    double? RangeAdvantageToCurrentShadow,
    IReadOnlyList<double> PointColumnAdvantagesToCurrentShadow);

internal sealed record AnnualShortMultipleTestingReport(
    int CandidateCount,
    int DrawCount,
    int BootstrapReplications,
    int BlockLength,
    AnnualShortFamilyTestReport BallCoverage,
    AnnualShortFamilyTestReport RangeLighting);

internal sealed record AnnualShortFamilyTestReport(
    string BestModel,
    double BestMeanAdvantage,
    double RealityCheckPValue,
    double RealityCheckSelectionAdjustedLower95,
    double SpaStatistic,
    double SpaPValue,
    int SpaRetainedCandidateCount)
{
    public bool RealityCheckPasses05 => RealityCheckPValue < 0.05
        && RealityCheckSelectionAdjustedLower95 > 0;
    public bool SpaPasses05 => SpaPValue < 0.05;
}

internal sealed record AnnualShortRangeScoreReport(
    string Model,
    int DrawCount,
    int EventObservationCount,
    double BrierScore,
    double RandomBrierScore,
    double MeanWithinIssueAuc,
    double FirstHalfWithinIssueAuc,
    double SecondHalfWithinIssueAuc,
    double CalibrationBias,
    IReadOnlyList<AnnualShortRangeScoreSegmentReport> Segments)
{
    public double BrierSkill => RandomBrierScore == 0 ? 0 : 1 - BrierScore / RandomBrierScore;
}

internal sealed record AnnualShortRangeScoreSegmentReport(
    int Segment,
    int StartIssue,
    int EndIssue,
    int SampleSize,
    double BrierScore,
    double MeanWithinIssueAuc,
    double CalibrationBias);

internal sealed record AnnualShortRangeSignalReport(
    string Signal,
    int DrawCount,
    int EventObservationCount,
    double MeanWithinIssueAuc,
    double FirstHalfWithinIssueAuc,
    double SecondHalfWithinIssueAuc,
    IReadOnlyList<AnnualShortRangeSignalSegmentReport> Segments);

internal sealed record AnnualShortRangeSignalSegmentReport(
    int Segment,
    int StartIssue,
    int EndIssue,
    int SampleSize,
    double MeanWithinIssueAuc);

internal sealed record AnnualShortFeatureCoefficientReport(
    string Feature,
    int DrawCount,
    double MeanCoefficient,
    double FirstHalfMeanCoefficient,
    double SecondHalfMeanCoefficient,
    double PositiveShare,
    double SignConsistency,
    IReadOnlyList<AnnualShortFeatureCoefficientSegmentReport> Segments);

internal sealed record AnnualShortFeatureCoefficientSegmentReport(
    int Segment,
    int StartIssue,
    int EndIssue,
    int SampleSize,
    double MeanCoefficient,
    double PositiveShare);

internal sealed record AnnualShortResidualDriftReport(
    string Metric,
    int DrawCount,
    IReadOnlyList<AnnualShortResidualChangeObservation> Observations)
{
    public AnnualShortResidualChangeObservation Latest => Observations[^1];
    public int DetectionCount => Observations.Count(observation => observation.IsChangeDetected);
}

internal sealed record AnnualShortResidualChangeObservation(
    int Issue,
    bool IsChangeDetected,
    int? EstimatedChangeIssue,
    double BicAdvantage,
    double FullMean,
    double? RecentMean);

internal sealed record AnnualShortPointColumnProbabilityReport(
    int Column,
    int DrawCount,
    double HitRate,
    double MeanPredictedProbability,
    double CalibrationBias,
    double BrierScore,
    double RandomBrierScore,
    double Auc,
    double FirstHalfBrierScore,
    double SecondHalfBrierScore,
    double RecentHitRate,
    double RecentBrierScore,
    double MeanSelectionPercentile,
    double MeanPoint,
    double RecoverableMissRate,
    double MeanRecoverableMissScoreMargin,
    IReadOnlyList<AnnualShortPointColumnProbabilitySegmentReport> Segments)
{
    public double BrierSkill => RandomBrierScore == 0 ? 0 : 1 - BrierScore / RandomBrierScore;
}

internal sealed record AnnualShortPointColumnProbabilitySegmentReport(
    int Segment,
    int StartIssue,
    int EndIssue,
    int SampleSize,
    double HitRate,
    double MeanPredictedProbability,
    double BrierScore,
    double MeanSelectionPercentile);

internal sealed record AnnualShortPointModelReport(
    string Model,
    double AverageHits,
    double AverageRangeHits,
    double RandomExpectedHits,
    double LiftLower95,
    double FirstHalfLift,
    double SecondHalfLift,
    double AdvantageToBaseline,
    double AdvantageToBaselineLower95,
    double FirstHalfAdvantageToBaseline,
    double SecondHalfAdvantageToBaseline,
    double RangeAdvantageToBaseline,
    double RangeAdvantageToBaselineLower95,
    double FirstHalfRangeAdvantageToBaseline,
    double SecondHalfRangeAdvantageToBaseline,
    int DifferentFromBaselineCount,
    double ExactRepeatRate,
    double AverageRetainedPoints)
{
    public double Lift => AverageHits - RandomExpectedHits;
    public double AdvantageToCurrentShadow { get; init; }
    public double AdvantageToCurrentShadowLower95 { get; init; }
    public double FirstHalfAdvantageToCurrentShadow { get; init; }
    public double SecondHalfAdvantageToCurrentShadow { get; init; }
    public double RangeAdvantageToCurrentShadow { get; init; }
    public int DifferentFromCurrentShadowCount { get; init; }
    public IReadOnlyList<double> PointColumnHitRates { get; init; } = Array.Empty<double>();
    public IReadOnlyList<double> PointColumnRandomRates { get; init; } = Array.Empty<double>();
    public IReadOnlyList<double> PointColumnLifts { get; init; } = Array.Empty<double>();
    public double WeakestPointColumnLift { get; init; }
    public IReadOnlyList<double> PointColumnAdvantagesToCurrentShadow { get; init; } = Array.Empty<double>();
    public double WeakestPointColumnAdvantageToCurrentShadow { get; init; }
    public IReadOnlyList<AnnualShortPointCenterReport> PointCenterDiagnostics { get; init; } =
        Array.Empty<AnnualShortPointCenterReport>();
    public IReadOnlyList<AnnualShortPointColumnCenterReport> PointColumnCenterDiagnostics { get; init; } =
        Array.Empty<AnnualShortPointColumnCenterReport>();
    public double RandomExpectedRangeHits { get; init; }
    public double RangeLift => AverageRangeHits - RandomExpectedRangeHits;
    public double RangeLiftLower95 { get; init; }
    public double FirstHalfRangeLift { get; init; }
    public double SecondHalfRangeLift { get; init; }
    public double RangeAdvantageToCurrentShadowLower95 { get; init; }
    public double FirstHalfRangeAdvantageToCurrentShadow { get; init; }
    public double SecondHalfRangeAdvantageToCurrentShadow { get; init; }
    public bool MeetsMinimumSampleSize { get; init; }
    public bool MeetsAbsoluteBallCoverageGate { get; init; }
    public bool MeetsAbsoluteRangeGate { get; init; }
    public bool MeetsAbsoluteCoverageGate { get; init; }
    public bool MeetsCurrentShadowHitGate { get; init; }
    public bool MeetsCurrentShadowRangeGate { get; init; }
    public bool MeetsPointColumnGate { get; init; }
    public bool MeetsPointColumnShadowGate { get; init; }
    public bool MeetsRepeatRateGate { get; init; }
    public bool MeetsRetainedPointGate { get; init; }
    public bool IsEligibleForForwardValidation { get; init; }
    public double WorstSegmentAdvantageToCurrentShadow { get; init; }
    public double WorstSegmentRangeAdvantageToCurrentShadow { get; init; }
    public int LatestIssue { get; init; }
    public IReadOnlyList<int> LatestPoints { get; init; } = Array.Empty<int>();
    public int LatestHits { get; init; }
    public int LatestRangeHits { get; init; }
    public int LatestAdvantageToCurrentShadow { get; init; }
    public int LatestRangeAdvantageToCurrentShadow { get; init; }
    public bool LatestDifferentFromCurrentShadow { get; init; }
    public int RecentSampleSize { get; init; }
    public double RecentAverageHits { get; init; }
    public double RecentAverageRangeHits { get; init; }
    public double RecentAdvantageToCurrentShadow { get; init; }
    public double RecentRangeAdvantageToCurrentShadow { get; init; }
    public IReadOnlyList<double> RecentPointColumnHitRates { get; init; } = Array.Empty<double>();
    public string ResearchVerdict { get; init; } = string.Empty;
    public IReadOnlyList<AnnualShortPointSegmentReport> Segments { get; init; } =
        Array.Empty<AnnualShortPointSegmentReport>();
}

internal sealed record AnnualShortPointSegmentReport(
    int Segment,
    int StartIssue,
    int EndIssue,
    int SampleSize,
    double AverageHits,
    double AverageRangeHits,
    double AdvantageToBaseline,
    double RangeAdvantageToBaseline,
    double AdvantageToCurrentShadow,
    double RangeAdvantageToCurrentShadow);

internal sealed record AnnualShortPointCenterReport(
    int Point,
    int SelectedCount,
    int LitCount,
    double HitRate,
    double RandomHitRate)
{
    public double Lift => HitRate - RandomHitRate;
}

internal sealed record AnnualShortPointColumnCenterReport(
    int Column,
    int Point,
    int SelectedCount,
    int LitCount,
    double RandomHitRate)
{
    public double HitRate => LitCount / (double)SelectedCount;
    public double Lift => HitRate - RandomHitRate;
}
