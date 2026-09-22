using System.IO;
using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

internal static partial class VerificationSuite
{
    private static void VerifyPointOnlyResearch()
    {
        var records = BuildRecords(80);
        var first = PositionPointResearch.Run(records, windowSize: 30, windowCount: 2);
        var repeat = PositionPointResearch.Run(records, windowSize: 30, windowCount: 2);
    
        Assert(JsonSerializer.Serialize(first) == JsonSerializer.Serialize(repeat),
            "point research is deterministic");
        Assert(first.DataAsOfIssue == records[^1].Period, "point research data boundary");
        Assert(first.SampleSize == 60, "point research sample size");
        Assert(first.Models.Count >= 10, "point research compares multiple candidates");
        Assert(first.Models.Select(model => model.Model).Distinct().Count() == first.Models.Count,
            "point research model ids are unique");
        Assert(first.Models.All(model => model.Aggregate.SampleSize == 60),
            "point research aggregate uses point-only target set");
        Assert(first.Models.All(model => model.Windows.Count == 2
            && model.Windows.All(window => window.SampleSize == 30)),
            "point research creates non-overlapping windows");
        Assert(first.Models.All(model => model.Aggregate.AverageHits is >= 0 and <= 6),
            "point research hit count bounds");
        Assert(first.Models.All(model => model.Aggregate.AverageRangeHits is >= 0 and <= 6),
            "point research range hit count bounds");
        Assert(first.Models.Any(model => model.Model == "stable-zone-route-transition10"),
            "point research includes current balanced transition candidate");
        Assert(first.Models.Any(model => model.Model == "stable-zone-route-transition05-prior120"),
            "point research includes current regularized transition candidate");
        Assert(first.Models.Any(model => model.Model == "online-v45-gated-400"),
            "point research includes v4.5 historical gate candidate");
        Assert(first.Models.Any(model => model.Model == "stable-range-event-greedy"),
            "point research audits visible point-column hit objective");
    
        var alteredFuture = records.Select(Clone).ToList();
        foreach (var record in alteredFuture.Skip(60))
            record.RedBalls = new[] { 2, 8, 14, 20, 26, 32 };
        var altered = PositionPointResearch.Run(alteredFuture, windowSize: 30, windowCount: 2);
        foreach (var model in first.Models)
        {
            var originalPastWindow = model.Windows.Single(window => window.OffsetFromLatest == 30);
            var alteredPastWindow = altered.Models.Single(item => item.Model == model.Model)
                .Windows.Single(window => window.OffsetFromLatest == 30);
            Assert(JsonSerializer.Serialize(originalPastWindow) == JsonSerializer.Serialize(alteredPastWindow),
                $"point research future isolation for {model.Model}");
        }
    }
    private static void VerifyAnnualShortTermPointResearch()
    {
        var previousYear = BuildRecords(40);
        var currentYear = BuildRecords(40);
        for (int index = 0; index < currentYear.Count; index++)
        {
            currentYear[index].Period = 2026001 + index;
            currentYear[index].DrawDate = new DateTime(2026, 1, 1).AddDays(index * 3);
        }
        var records = previousYear.Concat(currentYear).OrderBy(record => record.Period).ToArray();
        var minimumContextProbabilities = PositionPredictor
            .GetDynamicHierarchicalBallProbabilities(records.Take(5).ToArray());
        Assert(minimumContextProbabilities.Count == 33
            && minimumContextProbabilities.Values.All(double.IsFinite)
            && minimumContextProbabilities.Values.All(probability => probability is > 0 and < 1),
            "dynamic hierarchical model handles exactly five history rows");
        var first = AnnualShortPointResearch.Run(records);
        var repeat = AnnualShortPointResearch.Run(records);
    
        Assert(JsonSerializer.Serialize(first) == JsonSerializer.Serialize(repeat),
            "annual short-term research is deterministic");
        Assert(first.PredictionYear == 2026 && first.SampleSize == 40,
            "annual short-term research evaluates current year only");
        Assert(first.MinimumHistoricalSampleSize == 100
            && first.MinimumForwardPairedSampleSize == PositionValidationStore.MinimumPromotionPairedSampleSize
            && first.RequiresSealedForwardValidation,
            "annual short-term research separates historical and sealed forward gates");
        Assert(first.MultipleTesting.CandidateCount == first.Models.Count - 1
            && first.MultipleTesting.DrawCount == first.SampleSize
            && first.MultipleTesting.BootstrapReplications == 2000
            && first.MultipleTesting.BlockLength >= 2
            && first.MultipleTesting.BallCoverage.RealityCheckPValue is > 0 and <= 1
            && first.MultipleTesting.BallCoverage.SpaPValue is > 0 and <= 1
            && first.MultipleTesting.RangeLighting.RealityCheckPValue is > 0 and <= 1
            && first.MultipleTesting.RangeLighting.SpaPValue is > 0 and <= 1,
            "annual short-term research applies deterministic candidate-family tests");
        Assert(first.Models.Count >= 9 && first.Models.Select(model => model.Model).Distinct().Count() == first.Models.Count,
            "annual short-term research compares distinct profiles");
        Assert(first.Models.All(model => model.AverageHits is >= 0 and <= 6
            && model.AverageRangeHits is >= 0 and <= 6),
            "annual short-term research hit bounds");
        Assert(first.Models.All(model => model.ExactRepeatRate is >= 0 and <= 1
            && model.AverageRetainedPoints is >= 0 and <= 6),
            "annual short-term research repetition bounds");
        Assert(first.Models.All(model => model.PointColumnHitRates.Count == 6
            && model.PointColumnRandomRates.Count == 6
            && model.PointColumnLifts.Count == 6
            && model.PointColumnAdvantagesToCurrentShadow.Count == 6
            && model.PointColumnHitRates.All(rate => rate is >= 0 and <= 1)
            && model.PointColumnRandomRates.All(rate => rate is >= 0 and <= 1)),
            "annual short-term research point-column diagnostics");
        Assert(first.Models.All(model => model.RecentSampleSize == 10
            && model.RecentAverageHits is >= 0 and <= 6
            && model.RecentAverageRangeHits is >= 0 and <= 6
            && model.RecentPointColumnHitRates.Count == 6
            && model.RecentPointColumnHitRates.All(rate => rate is >= 0 and <= 1)),
            "annual short-term research reports the latest ten draws separately");
        Assert(first.Models.All(model => model.PointCenterDiagnostics.Sum(item => item.SelectedCount)
                == first.SampleSize * 6
            && Math.Abs(model.PointCenterDiagnostics.Sum(item => item.LitCount)
                - model.AverageRangeHits * first.SampleSize) < 1e-9),
            "annual short-term research point-center diagnostics reconcile totals");
        Assert(first.Models.All(model => model.PointColumnCenterDiagnostics.Count > 0
            && Enumerable.Range(1, 6).All(column =>
                model.PointColumnCenterDiagnostics.Where(item => item.Column == column)
                    .Sum(item => item.SelectedCount) == first.SampleSize)),
            "annual short-term research column-center diagnostics reconcile totals");
        Assert(first.Models.All(model => model.Segments.Count == 5
            && model.Segments.Sum(segment => segment.SampleSize) == first.SampleSize),
            "annual short-term research uses fixed complete time segments");
        var latestDraw = currentYear[^1];
        var latestShadow = first.Models.Single(model => model.Model == first.CurrentShadowModel);
        Assert(first.Models.All(model => model.MeetsPointColumnShadowGate
                == model.PointColumnAdvantagesToCurrentShadow.All(advantage => advantage >= -1e-12)
            && Math.Abs(model.WeakestPointColumnAdvantageToCurrentShadow
                - model.PointColumnAdvantagesToCurrentShadow.Min()) < 1e-12),
            "annual short-term research applies per-column non-inferiority against current shadow");
        Assert(latestShadow.MeetsPointColumnShadowGate
            && latestShadow.PointColumnAdvantagesToCurrentShadow.All(advantage => Math.Abs(advantage) < 1e-12),
            "annual short-term research anchors per-column shadow advantages");
        Assert(first.Models.All(model => model.LatestIssue == latestDraw.Period
            && model.LatestPoints.Count == 6
            && model.LatestHits == latestDraw.RedBalls.Count(ball =>
                model.LatestPoints.Any(point => PositionPointRange.Contains(point, ball, 33)))
            && model.LatestRangeHits == model.LatestPoints.Count(point =>
                latestDraw.RedBalls.Any(ball => PositionPointRange.Contains(point, ball, 33)))
            && model.LatestAdvantageToCurrentShadow == model.LatestHits - latestShadow.LatestHits
            && model.LatestRangeAdvantageToCurrentShadow
                == model.LatestRangeHits - latestShadow.LatestRangeHits
            && model.LatestDifferentFromCurrentShadow
                == !model.LatestPoints.SequenceEqual(latestShadow.LatestPoints)),
            "annual short-term research latest observation reconciles model and shadow results");
        Assert(first.ScoreDiagnostics.Count == 9
            && first.ScoreDiagnostics.All(report => report.DrawCount == first.SampleSize
                && report.EventObservationCount == first.SampleSize * 31
                && report.BrierScore is >= 0 and <= 1
                && report.RandomBrierScore is >= 0 and <= 1
                && report.MeanWithinIssueAuc is >= 0 and <= 1
                && report.FirstHalfWithinIssueAuc is >= 0 and <= 1
                && report.SecondHalfWithinIssueAuc is >= 0 and <= 1
                && report.Segments.Count == 5
                && report.Segments.Sum(segment => segment.SampleSize) == first.SampleSize),
            "annual short-term research audits range-event score quality");
        Assert(first.ScoreDiagnostics.Any(report => report.Model == "dynamic-hierarchical-range"),
            "annual short-term research audits dynamic hierarchical probabilities");
        Assert(first.ScoreDiagnostics.Any(report =>
                report.Model == "dynamic-conditional-hierarchical-range")
            && first.Models.Any(report =>
                report.Model == "annual-short-dynamic-conditional-hierarchical-range-global"),
            "annual short-term research includes fixed-draw likelihood candidate");
        Assert(first.Models.Any(report =>
                report.Model == "annual-short-dynamic-stable-blend05-global"),
            "annual short-term research includes low-weight stable blend candidate");
        Assert(first.ScoreDiagnostics.Any(report => report.Model == "dynamic-hierarchical-shape-range")
            && first.Models.Any(report =>
                report.Model == "annual-short-dynamic-hierarchical-shape-ball-global"),
            "annual short-term research includes fixed structural completion candidate");
        Assert(first.ScoreDiagnostics.Any(report => report.Model == "dynamic-hierarchical-ensemble-range")
            && first.Models.Any(report =>
                report.Model == "annual-short-dynamic-hierarchical-ensemble-global"),
            "annual short-term research includes equal-weight probability ensemble");
        Assert(first.Models.Any(report => report.Model
                == "annual-short-dynamic-hierarchical-previous-year-shared-transfer-blend05-global"),
            "annual short-term research retains preregistered previous-year transfer blend");
        Assert(first.PreregisteredEvaluation is
            {
                Model: "annual-short-dynamic-hierarchical-previous-year-shared-transfer-blend05-global",
                LockedAfterIssue: 2026096,
                FirstEligibleIssue: 2026097,
                SampleSize: 0,
                AverageHits: null,
                AverageRangeHits: null,
                AdvantageToCurrentShadow: null,
                RangeAdvantageToCurrentShadow: null
            }
            && first.PreregisteredEvaluation.PointColumnAdvantagesToCurrentShadow.Count == 0,
            "annual short-term research isolates preregistered out-of-sample results");
        Assert(first.ScoreDiagnostics.Any(report => report.Model == "position-polarization-range")
            && first.Models.Any(report =>
                report.Model == "annual-short-position-polarization-global"),
            "annual short-term research includes shrunk order-statistic polarization");
        string[] researchSignals =
        {
            "jpf-midpoint",
            "jpf-midpoint-avoidance",
            "jpf-compound-transition",
            "jpf-tail-state",
            "stable-momentum-midpoint-rank",
            "discounted-beta-range"
        };
        Assert(first.SignalDiagnostics.Count == 12
            && first.SignalDiagnostics.All(report => report.DrawCount == first.SampleSize
                && report.EventObservationCount == first.SampleSize * 31
                && report.MeanWithinIssueAuc is >= 0 and <= 1
                && report.FirstHalfWithinIssueAuc is >= 0 and <= 1
                && report.SecondHalfWithinIssueAuc is >= 0 and <= 1
                && report.Segments.Count == 5
                && report.Segments.Sum(segment => segment.SampleSize) == first.SampleSize),
            "annual short-term research audits dynamic range signals");
        Assert(researchSignals.All(signal => first.SignalDiagnostics.Any(report => report.Signal == signal)),
            "annual short-term research includes isolated signal audits");
        Assert(first.FeatureCoefficientDiagnostics.Count == 9
            && first.FeatureCoefficientDiagnostics.Select(report => report.Feature).Distinct().Count() == 9
            && first.FeatureCoefficientDiagnostics.All(report => report.DrawCount == first.SampleSize
                && double.IsFinite(report.MeanCoefficient)
                && double.IsFinite(report.FirstHalfMeanCoefficient)
                && double.IsFinite(report.SecondHalfMeanCoefficient)
                && report.PositiveShare is >= 0 and <= 1
                && report.SignConsistency is >= 0.5 and <= 1
                && report.Segments.Count == 5
                && report.Segments.Sum(segment => segment.SampleSize) == first.SampleSize
                && report.Segments.All(segment => double.IsFinite(segment.MeanCoefficient)
                    && segment.PositiveShare is >= 0 and <= 1)),
            "annual short-term research audits dynamic feature coefficient stability");
        Assert(first.ResidualDriftDiagnostics.Count == 3
            && first.ResidualDriftDiagnostics.Select(report => report.Metric).Distinct().Count() == 3
            && first.ResidualDriftDiagnostics.All(report => report.DrawCount == first.SampleSize
                && report.Observations.Count == first.SampleSize
                && report.DetectionCount is >= 0 and <= 40
                && report.Latest == report.Observations[^1]
                && report.Observations.Select(observation => observation.Issue)
                    .SequenceEqual(currentYear.Select(record => record.Period))
                && report.Observations.All(observation => double.IsFinite(observation.BicAdvantage)
                    && double.IsFinite(observation.FullMean)
                    && (!observation.IsChangeDetected
                        ? observation.EstimatedChangeIssue is null && observation.RecentMean is null
                        : observation.EstimatedChangeIssue < observation.Issue
                            && double.IsFinite(observation.RecentMean!.Value)))),
            "annual short-term research audits prequential residual drift with BIC");
        Assert(first.PointColumnProbabilityDiagnostics.Count == 6
            && first.PointColumnProbabilityDiagnostics.Select(report => report.Column)
                .SequenceEqual(Enumerable.Range(1, 6))
            && first.PointColumnProbabilityDiagnostics.All(report =>
                report.DrawCount == first.SampleSize
                && report.HitRate is >= 0 and <= 1
                && report.MeanPredictedProbability is > 0 and < 1
                && double.IsFinite(report.CalibrationBias)
                && report.BrierScore is >= 0 and <= 1
                && report.RandomBrierScore is >= 0 and <= 1
                && report.Auc is >= 0 and <= 1
                && report.FirstHalfBrierScore is >= 0 and <= 1
                && report.SecondHalfBrierScore is >= 0 and <= 1
                && report.RecentHitRate is >= 0 and <= 1
                && report.RecentBrierScore is >= 0 and <= 1
                && report.MeanSelectionPercentile is >= 0 and <= 1
                && report.MeanPoint is >= 2 and <= 32
                && report.RecoverableMissRate is >= 0 and <= 1
                && double.IsFinite(report.MeanRecoverableMissScoreMargin)
                && report.Segments.Count == 5
                && report.Segments.Sum(segment => segment.SampleSize) == first.SampleSize
                && report.Segments.All(segment => segment.HitRate is >= 0 and <= 1
                    && segment.MeanPredictedProbability is > 0 and < 1
                    && segment.BrierScore is >= 0 and <= 1
                    && segment.MeanSelectionPercentile is >= 0 and <= 1)),
            "annual short-term research audits selected-point probability reliability by column");
    
        var zeroRates = Enumerable.Range(2, 31).ToDictionary(point => point, _ => 0.0);
        DrawRecord[] signalHistory =
        {
            new() { Period = 2025001, RedBalls = new[] { 1, 7, 13, 19, 25, 31 } },
            new() { Period = 2025002, RedBalls = new[] { 2, 8, 14, 20, 26, 32 } },
            new() { Period = 2025003, RedBalls = new[] { 3, 9, 15, 21, 27, 33 } },
            new() { Period = 2025004, RedBalls = new[] { 5, 7, 14, 17, 25, 27 } }
        };
        var jpfValues = AnnualShortPointResearch.CreateRangeSignals(
            signalHistory,
            zeroRates,
            zeroRates,
            zeroRates);
        Assert(jpfValues["jpf-midpoint"].Values.All(value => value is >= 0 and <= 15)
            && jpfValues["jpf-midpoint"][16] > 0,
            "JPF midpoint signal counts latest-draw integer midpoint support");
        Assert(jpfValues["jpf-midpoint-avoidance"].Values.All(value => value is >= -15 and <= 0)
            && jpfValues["jpf-midpoint-avoidance"].All(pair =>
                pair.Value == -jpfValues["jpf-midpoint"][pair.Key]),
            "JPF midpoint avoidance is the fixed inverse of midpoint support");
        Assert(jpfValues["jpf-compound-transition"].Values.All(value => value is >= 0 and <= 3)
            && jpfValues["jpf-compound-transition"][16] == 3,
            "JPF compound signal combines repeat, interval and outside-neighbor support");
        Assert(jpfValues["jpf-tail-state"].Values.All(value => value is >= 0 and <= 7)
            && jpfValues["jpf-tail-state"][16] == 6,
            "JPF tail signal counts latest tail matches and same-tail support");
        Assert(jpfValues["stable-momentum-midpoint-rank"].Values.All(value => value is >= 0 and <= 1),
            "stable signal rank ensemble stays normalized");
        Assert(jpfValues["discounted-beta-range"].Values.All(value => value is > 0 and < 1),
            "discounted beta range signal stays probabilistic");
        Assert(first.Models.Any(model => model.Model == first.BaselineModel),
            "annual short-term research includes production baseline");
        Assert(first.Models.Single(model => model.Model == first.CurrentShadowModel)
            is
        {
            AdvantageToCurrentShadow: 0,
            RangeAdvantageToCurrentShadow: 0,
            DifferentFromCurrentShadowCount: 0
        },
            "annual short-term research anchors pairwise metrics to current shadow");
        Assert(first.Models.Single(model => model.Model == first.CurrentShadowModel)
            .Segments.All(segment => segment is
            { AdvantageToCurrentShadow: 0, RangeAdvantageToCurrentShadow: 0 }),
            "annual short-term research anchors every segment to current shadow");
        Assert(first.Models.All(model => !model.MeetsMinimumSampleSize
            && !model.IsEligibleForForwardValidation
            && model.ResearchVerdict.Contains("历史样本不足100期", StringComparison.Ordinal)),
            "annual short-term research blocks forward eligibility below historical gate");
        Assert(first.Models.All(model => model.MeetsRetainedPointGate
            == (model.AverageRetainedPoints <= first.Models.Single(shadow =>
                shadow.Model == first.CurrentShadowModel).AverageRetainedPoints + 1e-12)),
            "annual short-term research applies retained-point gate against current shadow");
        Assert(first.Models.Single(model => model.Model == first.CurrentShadowModel).MeetsRetainedPointGate,
            "annual short-term research current shadow passes retained-point gate");
        Assert(first.Models.Any(model => model.Model == "annual-short-range-event-hierarchical-global"),
            "annual short-term research includes preregistered hierarchical range candidate");
        Assert(first.Models.Any(model => model.Model == "annual-short-range-event-adaptive-window-global"),
            "annual short-term research includes preregistered adaptive-window range candidate");
        Assert(first.Models.Any(model => model.Model == "annual-short-range-event-global-ball-tiebreak"),
            "annual short-term research includes range-event tie-break candidate");
        Assert(first.Models.Any(model => model.Model == "annual-short-range-event-region-calibrated-global"),
            "annual short-term research includes preregistered rolling region calibration candidate");
        Assert(first.Models.Any(model => model.Model == "annual-short-range-event-state-transition-global"),
            "annual short-term research includes preregistered range-state transition candidate");
        Assert(first.Models.Any(model => model.Model == "annual-short-range-event-structure-analog-global"),
            "annual short-term research includes preregistered structure-analog candidate");
        Assert(first.Models.Any(model => model.Model == "annual-short-range-event-one-se-ball-coverage-global"),
            "annual short-term research includes preregistered one-standard-error candidate");
        Assert(first.Models.Any(model => model.Model == "annual-short-range-event-global-momentum-tiebreak"),
            "annual short-term research includes preregistered momentum tie-break candidate");
        Assert(first.Models.Any(model =>
                model.Model == "annual-short-range-event-global-jpf-compound-tiebreak"),
            "annual short-term research includes isolated JPF compound tie-break candidate");
        Assert(first.Models.Any(model =>
                model.Model == "annual-short-range-event-global-jpf-midpoint-avoidance-tiebreak"),
            "annual short-term research includes fixed JPF midpoint avoidance tie-break candidate");
        Assert(first.Models.Any(model =>
                model.Model == "annual-short-range-event-global-stable-signal-rank-tiebreak"),
            "annual short-term research includes stable signal rank tie-break candidate");
        Assert(first.Models.Any(model => model.Model == "annual-short-stable-signal-rank-global"),
            "annual short-term research includes stable signal rank global ablation");
        Assert(first.Models.Any(model => model.Model == "annual-short-prequential-signal-champion-global"),
            "annual short-term research includes prequential signal champion candidate");
        Assert(first.Models.Any(model => model.Model == "annual-short-base-stable-rank-consensus-global"),
            "annual short-term research includes base-stable rank consensus candidate");
        Assert(first.Models.Any(model => model.Model == "annual-short-discounted-beta-range-global"),
            "annual short-term research includes discounted beta range candidate");
        Assert(first.CurrentShadowModel == "annual-short-dynamic-hierarchical-ball-global"
            && first.Models.Any(model => model.Model == first.CurrentShadowModel),
            "annual short-term research anchors the dynamic hierarchical shadow");
    
        var dynamicHistory = PositionPredictor.GetAnnualShortHistory(
            records.Where(record => record.Period < 2026040).ToArray(),
            2026040);
        var dynamicProbabilities = PositionPredictor.GetDynamicHierarchicalBallProbabilities(dynamicHistory);
        Assert(dynamicProbabilities.Count == 33
            && dynamicProbabilities.Keys.SequenceEqual(Enumerable.Range(1, 33))
            && dynamicProbabilities.Values.All(value => double.IsFinite(value) && value is >= 0.01 and <= 0.60),
            "dynamic hierarchical ball probabilities are complete and bounded");
        var transferBlendProbabilities = PositionPredictor
            .GetAnnualShortPreviousYearSharedTransferBlendBallProbabilities(
                records.Where(record => record.Period < 2026040).ToArray(),
                2026040);
        Assert(transferBlendProbabilities.Count == 33
            && transferBlendProbabilities.Keys.SequenceEqual(Enumerable.Range(1, 33))
            && transferBlendProbabilities.Values.All(value =>
                double.IsFinite(value) && value is >= 0.01 and <= 0.60),
            "previous-year transfer blend probabilities are complete and bounded");
        var conditionalProbabilities =
            PositionPredictor.GetDynamicConditionalHierarchicalBallProbabilities(dynamicHistory);
        Assert(conditionalProbabilities.Count == 33
            && conditionalProbabilities.Values.All(value => double.IsFinite(value) && value is > 0 and < 1)
            && Math.Abs(conditionalProbabilities.Values.Sum() - 6) < 1e-10,
            "fixed-draw likelihood probabilities are complete and sum to six");
        var polarizationScores = PositionPredictor.GetAnnualShortPositionPolarizationRangeScores(dynamicHistory);
        Assert(polarizationScores.Count == 31
            && polarizationScores.Values.All(value => double.IsFinite(value)),
            "order-statistic polarization range scores are finite and complete");
        var uniformProbabilities = Enumerable.Range(1, 33).ToDictionary(ball => ball, _ => 6.0 / 33);
        var conditionedRangeScores = PositionPredictor.GetConditionedRangeEventScores(uniformProbabilities);
        double uniformRangeProbability = 1 - DataService.Combination(30, 6)
            / (double)DataService.Combination(33, 6);
        Assert(conditionedRangeScores.Count == 31
            && conditionedRangeScores.Values.All(value =>
                Math.Abs(value - uniformRangeProbability) < 1e-12),
            "fixed-draw conditioning matches exact uniform range probability");
    
        var alteredFuture = records.Select(Clone).ToArray();
        foreach (var record in alteredFuture.Where(record => record.Period >= 2026017))
            record.RedBalls = new[] { 2, 8, 14, 20, 26, 32 };
        var alteredResearch = AnnualShortPointResearch.Run(alteredFuture);
        foreach (var model in first.Models)
        {
            var alteredModel = alteredResearch.Models.Single(item => item.Model == model.Model);
            Assert(JsonSerializer.Serialize(model.Segments.Take(2))
                == JsonSerializer.Serialize(alteredModel.Segments.Take(2)),
                $"annual short-term research future isolation for {model.Model}");
        }
        foreach (var scoreReport in first.ScoreDiagnostics)
        {
            var alteredScoreReport = alteredResearch.ScoreDiagnostics.Single(item => item.Model == scoreReport.Model);
            Assert(JsonSerializer.Serialize(scoreReport.Segments.Take(2))
                == JsonSerializer.Serialize(alteredScoreReport.Segments.Take(2)),
                $"annual short-term score future isolation for {scoreReport.Model}");
        }
        foreach (var signalReport in first.SignalDiagnostics)
        {
            var alteredSignalReport = alteredResearch.SignalDiagnostics.Single(
                item => item.Signal == signalReport.Signal);
            Assert(JsonSerializer.Serialize(signalReport.Segments.Take(2))
                == JsonSerializer.Serialize(alteredSignalReport.Segments.Take(2)),
                $"annual short-term signal future isolation for {signalReport.Signal}");
        }
        foreach (var coefficientReport in first.FeatureCoefficientDiagnostics)
        {
            var alteredCoefficientReport = alteredResearch.FeatureCoefficientDiagnostics.Single(
                item => item.Feature == coefficientReport.Feature);
            Assert(JsonSerializer.Serialize(coefficientReport.Segments.Take(2))
                == JsonSerializer.Serialize(alteredCoefficientReport.Segments.Take(2)),
                $"annual short-term coefficient future isolation for {coefficientReport.Feature}");
        }
        foreach (var driftReport in first.ResidualDriftDiagnostics)
        {
            var alteredDriftReport = alteredResearch.ResidualDriftDiagnostics.Single(
                item => item.Metric == driftReport.Metric);
            Assert(JsonSerializer.Serialize(driftReport.Observations.Take(16))
                == JsonSerializer.Serialize(alteredDriftReport.Observations.Take(16)),
                $"annual short-term residual drift future isolation for {driftReport.Metric}");
        }
        foreach (var columnReport in first.PointColumnProbabilityDiagnostics)
        {
            var alteredColumnReport = alteredResearch.PointColumnProbabilityDiagnostics.Single(
                item => item.Column == columnReport.Column);
            Assert(JsonSerializer.Serialize(columnReport.Segments.Take(2))
                == JsonSerializer.Serialize(alteredColumnReport.Segments.Take(2)),
                $"annual short-term column probability future isolation for column {columnReport.Column}");
        }
    
        WithTestDirectory(testRoot =>
        {
            var data = new FakeDataService();
            data.SetRecords(records);
            var shadow = PositionPredictor.CreateAnnualShortNoStructureShadow(
                    data,
                    new PositionValidationStore(data, Path.Combine(testRoot, "shadow-ledger.json"), subscribeToUpdates: false))
                .Predict(2026036, "backtest");
            Assert(shadow.RuleVersionId.StartsWith(
                PositionPredictor.AnnualShortNoStructureShadowRuleVersion + "+",
                StringComparison.Ordinal), "annual short shadow version");
            Assert(shadow.RedPoints.Count == 6 && ExpandCoverage(shadow.RedPoints).Count == 18,
                "annual short shadow retains six non-overlapping point ranges");
    
            var rangeEventShadow = PositionPredictor.CreateAnnualShortRangeEventGlobalShadow(
                    data,
                    new PositionValidationStore(data, Path.Combine(testRoot, "range-shadow-ledger.json"), subscribeToUpdates: false))
                .Predict(2026036, "backtest");
            Assert(rangeEventShadow.RuleVersionId.StartsWith(
                PositionPredictor.AnnualShortRangeEventGlobalShadowRuleVersion + "+",
                StringComparison.Ordinal), "annual range-event shadow version");
            Assert(rangeEventShadow.RedPoints.Count == 6 && ExpandCoverage(rangeEventShadow.RedPoints).Count == 18,
                "annual range-event shadow retains eighteen covered balls");
    
            var dynamicShadow = PositionPredictor.CreateAnnualShortDynamicHierarchicalShadow(
                    data,
                    new PositionValidationStore(data, Path.Combine(testRoot, "dynamic-shadow-ledger.json"), subscribeToUpdates: false))
                .Predict(2026036, "backtest");
            Assert(dynamicShadow.RuleVersionId.StartsWith(
                PositionPredictor.AnnualShortDynamicHierarchicalShadowRuleVersion + "+",
                StringComparison.Ordinal), "annual dynamic hierarchical shadow version");
            Assert(dynamicShadow.RedPoints.Count == 6 && ExpandCoverage(dynamicShadow.RedPoints).Count == 18,
                "annual dynamic hierarchical shadow retains eighteen covered balls");
            Assert(PositionPredictor.CurrentShadowRuleVersion
                == PositionPredictor.AnnualShortDynamicHierarchicalShadowRuleVersion,
                "current shadow points to dynamic hierarchical version");
        });
    }
}
