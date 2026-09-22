using System.IO;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

internal static partial class VerificationSuite
{
    private static void VerifyPointRanges()
    {
        Assert(PositionPointRange.Contains(1, 1, 33), "lower center");
        Assert(PositionPointRange.Contains(1, 2, 33), "lower neighbor");
        Assert(!PositionPointRange.Contains(1, 3, 33), "outside lower range");
        Assert(PositionPointRange.Contains(33, 32, 33), "upper neighbor");
        Assert(PositionPointRange.Contains(33, 33, 33), "upper center");
        Assert(!PositionPointRange.Contains(33, 31, 33), "outside upper range");
        double expectedInteriorProbability = 1
            - (30.0 / 33) * (29.0 / 32) * (28.0 / 31)
            * (27.0 / 30) * (26.0 / 29) * (25.0 / 28);
        Assert(Math.Abs(PositionPointRange.RandomHitProbability(6, 33, 6)
            - expectedInteriorProbability) < 1e-12, "point random hit probability");
        Assert(Math.Abs(PositionPointRange.RandomExpectedLitCount(
            new[] { 2, 6, 10, 14, 18, 22 }, 33, 6) - 6 * expectedInteriorProbability) < 1e-12,
            "point random expected lit count");
    }
    private static void VerifyPredictorInvariants()
    {
        var records = BuildRecords(60);
        const int targetIssue = 2025041;
        var fullData = new FakeDataService();
        fullData.SetRecords(records.ToArray());
        var first = new PositionPredictor(fullData).Predict(targetIssue, "backtest");
        var repeat = new PositionPredictor(fullData).Predict(targetIssue, "backtest");
    
        Assert(first.RunId == repeat.RunId, "deterministic run id");
        Assert(first.RedPoints.SequenceEqual(repeat.RedPoints), "deterministic red points");
        Assert(first.SingleBlue == repeat.SingleBlue, "deterministic single blue");
        Assert(first.RedPoints.Count == 6 && first.RedPoints.Distinct().Count() == 6, "six unique points");
        var coveredReds = ExpandCoverage(first.RedPoints);
        Assert(first.RuleVersionId.StartsWith(PositionPredictor.CurrentRuleVersion + "+", StringComparison.Ordinal),
            "current point promotion version");
        Assert(coveredReds.Count == 18, "current point promotion covers eighteen numbers");
        int[] rankedBlues = first.BlueScores.OrderByDescending(score => score.CombinedScore)
            .ThenBy(score => score.Ball)
            .Take(3)
            .Select(score => score.Ball)
            .ToArray();
        Assert(first.SingleBlue == rankedBlues[0], "single blue is top combined score");
        Assert(first.DoubleBlue.Count == 2 && first.DoubleBlue.Contains(first.SingleBlue),
            "single nested in double");
        Assert(first.TripleBlue.Count == 3 && first.DoubleBlue.All(first.TripleBlue.Contains),
            "double nested in triple");
        Assert(first.DoubleBlue.Order().SequenceEqual(rankedBlues.Take(2).Order()),
            "double blue uses top two combined scores");
        Assert(first.TripleBlue.Order().SequenceEqual(rankedBlues.Order()),
            "triple blue uses top three combined scores");
        var alteredFuture = records.Select(record => Clone(record)).ToList();
        foreach (var record in alteredFuture.Where(record => record.Period > targetIssue))
        {
            record.RedBalls = new[] { 2, 8, 14, 20, 26, 32 };
            record.BlueBall = 16;
        }
        var alteredData = new FakeDataService();
        alteredData.SetRecords(alteredFuture.ToArray());
        var withoutFutureLeak = new PositionPredictor(alteredData).Predict(targetIssue, "backtest");
        Assert(first.RunId == withoutFutureLeak.RunId, "future changes do not alter snapshot");
        Assert(first.RedPoints.SequenceEqual(withoutFutureLeak.RedPoints), "future changes do not alter points");
        Assert(first.FormulaBlue == withoutFutureLeak.FormulaBlue, "future changes do not alter formula blue");
        Assert(first.ExclusionBlue == withoutFutureLeak.ExclusionBlue, "future changes do not alter exclusion blue");
        Assert(first.TripleBlue.SequenceEqual(withoutFutureLeak.TripleBlue), "future changes do not alter nested blue");
    
        WithTestDirectory(testRoot =>
        {
            var candidateStore = new PositionValidationStore(
                fullData,
                Path.Combine(testRoot, "candidate-ledger.json"),
                subscribeToUpdates: false);
            var candidate = PositionPredictor.CreateRollingHotShadow(fullData, candidateStore)
                .Predict(targetIssue, "backtest");
            int expectedHot = records.Where(record => record.Period < targetIssue)
                .TakeLast(30)
                .GroupBy(record => record.BlueBall)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .First()
                .Key;
            Assert(candidate.RuleVersionId.StartsWith(
                PositionPredictor.RollingHotRuleVersion + "+",
                StringComparison.Ordinal), "rolling hot candidate version");
            Assert(candidate.FormulaBlue == expectedHot, "rolling hot formula uses 30-period mode internally");
            Assert(candidate.SingleBlue == candidate.BlueScores
                .OrderByDescending(score => score.CombinedScore)
                .ThenBy(score => score.Ball)
                .First().Ball, "rolling hot candidate publishes combined top blue");
    
            var alteredCandidateStore = new PositionValidationStore(
                alteredData,
                Path.Combine(testRoot, "altered-candidate-ledger.json"),
                subscribeToUpdates: false);
            var alteredCandidate = PositionPredictor.CreateRollingHotShadow(alteredData, alteredCandidateStore)
                .Predict(targetIssue, "backtest");
            Assert(candidate.SingleBlue == alteredCandidate.SingleBlue,
                "future changes do not alter rolling hot candidate");
    
            var rangeCandidate = PositionPredictor.CreateRangeCoverageShadow(fullData, candidateStore)
                .Predict(targetIssue, "backtest");
            Assert(rangeCandidate.RuleVersionId.StartsWith(
                PositionPredictor.RangeCoverageRuleVersion + "+",
                StringComparison.Ordinal), "range coverage candidate version");
            Assert(rangeCandidate.RedPoints.Count == 6 && rangeCandidate.RedPoints.Distinct().Count() == 6,
                "range coverage candidate has six unique points");
            int coveredNumbers = rangeCandidate.RedPoints
                .SelectMany(point => Enumerable.Range(
                    Math.Max(1, point - PositionPointRange.Radius),
                    Math.Min(33, point + PositionPointRange.Radius)
                        - Math.Max(1, point - PositionPointRange.Radius) + 1))
                .Distinct()
                .Count();
            Assert(coveredNumbers == 18, "range coverage candidate covers eighteen numbers");
            Assert(rangeCandidate.SingleBlue == candidate.SingleBlue,
                "range coverage candidate inherits rolling hot blue");
    
            var formulaAnchored = PositionPredictor.CreateFormulaAnchoredRangeCoverageShadow(
                    fullData,
                    candidateStore)
                .Predict(targetIssue, "backtest");
            Assert(formulaAnchored.RuleVersionId.StartsWith(
                PositionPredictor.FormulaAnchoredRuleVersion + "+",
                StringComparison.Ordinal), "formula anchored candidate version");
            Assert(formulaAnchored.RedPoints.SequenceEqual(rangeCandidate.RedPoints),
                "formula anchored candidate preserves range coverage points");
            Assert(formulaAnchored.SingleBlue == formulaAnchored.FormulaBlue,
                "formula anchored candidate publishes formula blue as single blue");
            Assert(formulaAnchored.DoubleBlue.Contains(formulaAnchored.SingleBlue)
                && formulaAnchored.TripleBlue.Count == 3
                && formulaAnchored.TripleBlue.Distinct().Count() == 3
                && formulaAnchored.DoubleBlue.All(formulaAnchored.TripleBlue.Contains),
                "formula anchored candidate preserves strict nested blue sets");
    
            var adaptiveBlue = PositionPredictor.CreateAdaptiveBlueRangeCoverageShadow(
                    fullData,
                    candidateStore)
                .Predict(targetIssue, "backtest");
            Assert(adaptiveBlue.RuleVersionId.StartsWith(
                PositionPredictor.AdaptiveBlueRuleVersion + "+",
                StringComparison.Ordinal), "adaptive blue candidate version");
            Assert(adaptiveBlue.RedPoints.SequenceEqual(rangeCandidate.RedPoints),
                "adaptive blue candidate preserves range coverage points");
            Assert(adaptiveBlue.DoubleBlue.Contains(adaptiveBlue.SingleBlue)
                && adaptiveBlue.TripleBlue.Count == 3
                && adaptiveBlue.TripleBlue.Distinct().Count() == 3
                && adaptiveBlue.DoubleBlue.All(adaptiveBlue.TripleBlue.Contains),
                "adaptive blue candidate preserves strict nested blue sets");
    
            var pointTransition = PositionPredictor.CreatePointTransitionShadow(
                    fullData,
                    candidateStore)
                .Predict(targetIssue, "backtest");
            Assert(pointTransition.RuleVersionId.StartsWith(
                PositionPredictor.PointTransitionShadowRuleVersion + "+",
                StringComparison.Ordinal), "point transition shadow version");
            Assert(ExpandCoverage(pointTransition.RedPoints).Count == 18,
                "point transition shadow covers eighteen numbers");
            Assert(pointTransition.SingleBlue == first.SingleBlue
                && pointTransition.DoubleBlue.SequenceEqual(first.DoubleBlue)
                && pointTransition.TripleBlue.SequenceEqual(first.TripleBlue),
                "point transition shadow preserves current blue");
    
            var balancedPointTransition = PositionPredictor.CreatePointBalancedTransitionShadow(
                    fullData,
                    candidateStore)
                .Predict(targetIssue, "backtest");
            Assert(balancedPointTransition.RuleVersionId.StartsWith(
                PositionPredictor.PointBalancedTransitionShadowRuleVersion + "+",
                StringComparison.Ordinal), "balanced point transition shadow version");
            Assert(ExpandCoverage(balancedPointTransition.RedPoints).Count == 18,
                "balanced point transition shadow covers eighteen numbers");
            Assert(balancedPointTransition.SingleBlue == first.SingleBlue
                && balancedPointTransition.DoubleBlue.SequenceEqual(first.DoubleBlue)
                && balancedPointTransition.TripleBlue.SequenceEqual(first.TripleBlue),
                "balanced point transition shadow preserves current blue");
    
            var regularizedPointTransition = PositionPredictor.CreatePointRegularizedTransitionShadow(
                    fullData,
                    candidateStore)
                .Predict(targetIssue, "backtest");
            Assert(regularizedPointTransition.RuleVersionId.StartsWith(
                PositionPredictor.PointRegularizedTransitionShadowRuleVersion + "+",
                StringComparison.Ordinal), "regularized point transition shadow version");
            Assert(ExpandCoverage(regularizedPointTransition.RedPoints).Count == 18,
                "regularized point transition shadow covers eighteen numbers");
            Assert(regularizedPointTransition.SingleBlue == first.SingleBlue
                && regularizedPointTransition.DoubleBlue.SequenceEqual(first.DoubleBlue)
                && regularizedPointTransition.TripleBlue.SequenceEqual(first.TripleBlue),
                "regularized point transition shadow preserves current blue");
    
            var previousPrimary = PositionPredictor.CreatePreviousPrimary(
                    fullData,
                    candidateStore)
                .Predict(targetIssue, "backtest");
            Assert(previousPrimary.RuleVersionId.StartsWith(
                PositionPredictor.PreviousPrimaryRuleVersion + "+",
                StringComparison.Ordinal), "previous primary version");
            Assert(!previousPrimary.RedPoints.SequenceEqual(first.RedPoints),
                "point promotion changes previous primary points");
            Assert(previousPrimary.SingleBlue == first.SingleBlue
                && previousPrimary.DoubleBlue.SequenceEqual(first.DoubleBlue)
                && previousPrimary.TripleBlue.SequenceEqual(first.TripleBlue),
                "point promotion preserves previous primary blue");
            var legacyPrimary = PositionPredictor.CreateLegacyPrimary(
                    fullData,
                    candidateStore)
                .Predict(targetIssue, "backtest");
            Assert(legacyPrimary.RuleVersionId.StartsWith(
                PositionPredictor.LegacyPrimaryRuleVersion + "+",
                StringComparison.Ordinal), "legacy primary research baseline version");
            Assert(legacyPrimary.RedPoints.SequenceEqual(previousPrimary.RedPoints)
                && legacyPrimary.SingleBlue == previousPrimary.SingleBlue
                && legacyPrimary.TripleBlue.SequenceEqual(previousPrimary.TripleBlue),
                "legacy primary research baseline preserves points and blue");
    
            Assert(!rangeCandidate.RuleVersionId.StartsWith(PositionPredictor.CurrentRuleVersion + "+", StringComparison.Ordinal),
                "legacy range coverage remains isolated from annual short-term primary");
    
            var alteredRangeCandidate = PositionPredictor.CreateRangeCoverageShadow(
                    alteredData,
                    alteredCandidateStore)
                .Predict(targetIssue, "backtest");
            Assert(rangeCandidate.RedPoints.SequenceEqual(alteredRangeCandidate.RedPoints),
                "future changes do not alter range coverage points");
            var alteredFormulaAnchored = PositionPredictor.CreateFormulaAnchoredRangeCoverageShadow(
                    alteredData,
                    alteredCandidateStore)
                .Predict(targetIssue, "backtest");
            Assert(formulaAnchored.SingleBlue == alteredFormulaAnchored.SingleBlue
                && formulaAnchored.TripleBlue.SequenceEqual(alteredFormulaAnchored.TripleBlue),
                "future changes do not alter formula anchored blue");
            var alteredAdaptiveBlue = PositionPredictor.CreateAdaptiveBlueRangeCoverageShadow(
                    alteredData,
                    alteredCandidateStore)
                .Predict(targetIssue, "backtest");
            Assert(adaptiveBlue.SingleBlue == alteredAdaptiveBlue.SingleBlue
                && adaptiveBlue.TripleBlue.SequenceEqual(alteredAdaptiveBlue.TripleBlue),
                "future changes do not alter adaptive blue");
            var alteredPointTransition = PositionPredictor.CreatePointTransitionShadow(
                    alteredData,
                    alteredCandidateStore)
                .Predict(targetIssue, "backtest");
            Assert(pointTransition.RedPoints.SequenceEqual(alteredPointTransition.RedPoints),
                "future changes do not alter point transition shadow");
            var alteredBalancedPointTransition = PositionPredictor.CreatePointBalancedTransitionShadow(
                    alteredData,
                    alteredCandidateStore)
                .Predict(targetIssue, "backtest");
            Assert(balancedPointTransition.RedPoints.SequenceEqual(
                    alteredBalancedPointTransition.RedPoints),
                "future changes do not alter balanced point transition shadow");
            var alteredRegularizedPointTransition = PositionPredictor
                .CreatePointRegularizedTransitionShadow(alteredData, alteredCandidateStore)
                .Predict(targetIssue, "backtest");
            Assert(regularizedPointTransition.RedPoints.SequenceEqual(
                    alteredRegularizedPointTransition.RedPoints),
                "future changes do not alter regularized point transition shadow");
            var recentStructurePointShadow = PositionPredictor
                .CreatePointRecentStructureShadow(fullData, candidateStore)
                .Predict(targetIssue, "backtest");
            var alteredRecentStructurePointShadow = PositionPredictor
                .CreatePointRecentStructureShadow(alteredData, alteredCandidateStore)
                .Predict(targetIssue, "backtest");
            Assert(recentStructurePointShadow.RedPoints.SequenceEqual(
                    alteredRecentStructurePointShadow.RedPoints),
                "future changes do not alter recent structure point shadow");
            var shrunkSeasonPointShadow = PositionPredictor
                .CreatePointShrunkSeasonTransitionShadow(fullData, candidateStore)
                .Predict(targetIssue, "backtest");
            var alteredShrunkSeasonPointShadow = PositionPredictor
                .CreatePointShrunkSeasonTransitionShadow(alteredData, alteredCandidateStore)
                .Predict(targetIssue, "backtest");
            Assert(shrunkSeasonPointShadow.RedPoints.SequenceEqual(
                    alteredShrunkSeasonPointShadow.RedPoints),
                "future changes do not alter shrunk season point shadow");
            Assert(ExpandCoverage(shrunkSeasonPointShadow.RedPoints).Count == 18,
                "shrunk season point shadow covers eighteen numbers");
            var primaryPredictor = new PositionPredictor(fullData);
            var previousPrimaryPredictor = PositionPredictor.CreatePreviousPrimary(fullData, candidateStore);
            var paired = PositionPredictor.CreateRangeCoverageShadow(fullData, candidateStore)
                .CompareBacktest(previousPrimaryPredictor, 30);
            Assert(paired.SampleSize == 30 && paired.OffsetFromLatest == 0,
                "paired backtest sample and default offset");
            Assert(paired.DifferentPointPredictionCount > 0,
                "paired backtest detects point differences");
            var promotedPaired = primaryPredictor.CompareBacktest(previousPrimaryPredictor, 30);
            Assert(promotedPaired.DifferentPointPredictionCount > 0
                && promotedPaired.DifferentSingleBlueCount == 0
                && promotedPaired.DifferentDoubleBlueCount == 0
                && promotedPaired.DifferentTripleBlueCount == 0,
                "point promotion changes points without changing blue predictions");
            var pointShadowPaired = PositionPredictor.CreatePointTransitionShadow(fullData, candidateStore)
                .CompareBacktest(primaryPredictor, 30);
            Assert(pointShadowPaired.DifferentSingleBlueCount == 0
                && pointShadowPaired.DifferentDoubleBlueCount == 0
                && pointShadowPaired.DifferentTripleBlueCount == 0,
                "point transition shadow changes no blue predictions");
            var balancedPointShadowPaired = PositionPredictor
                .CreatePointBalancedTransitionShadow(fullData, candidateStore)
                .CompareBacktest(primaryPredictor, 30);
            Assert(balancedPointShadowPaired.DifferentSingleBlueCount == 0
                && balancedPointShadowPaired.DifferentDoubleBlueCount == 0
                && balancedPointShadowPaired.DifferentTripleBlueCount == 0,
                "balanced point transition shadow changes no blue predictions");
            var regularizedPointShadowPaired = PositionPredictor
                .CreatePointRegularizedTransitionShadow(fullData, candidateStore)
                .CompareBacktest(primaryPredictor, 30);
            Assert(regularizedPointShadowPaired.DifferentSingleBlueCount == 0
                && regularizedPointShadowPaired.DifferentDoubleBlueCount == 0
                && regularizedPointShadowPaired.DifferentTripleBlueCount == 0,
                "regularized point transition shadow changes no blue predictions");
            var recentStructurePointShadowPaired = PositionPredictor
                .CreatePointRecentStructureShadow(fullData, candidateStore)
                .CompareBacktest(primaryPredictor, 30);
            Assert(recentStructurePointShadowPaired.DifferentSingleBlueCount == 0
                && recentStructurePointShadowPaired.DifferentDoubleBlueCount == 0
                && recentStructurePointShadowPaired.DifferentTripleBlueCount == 0,
                "recent structure point shadow changes no blue predictions");
            var shrunkSeasonPointShadowPaired = PositionPredictor
                .CreatePointShrunkSeasonTransitionShadow(fullData, candidateStore)
                .CompareBacktest(primaryPredictor, 30);
            Assert(shrunkSeasonPointShadowPaired.DifferentSingleBlueCount == 0
                && shrunkSeasonPointShadowPaired.DifferentDoubleBlueCount == 0
                && shrunkSeasonPointShadowPaired.DifferentTripleBlueCount == 0,
                "shrunk season point shadow changes no blue predictions");
            var identicalPaired = primaryPredictor.CompareBacktest(primaryPredictor, 30);
            Assert(identicalPaired.DifferentDoubleBlueCount == 0
                && identicalPaired.DifferentTripleBlueCount == 0
                && identicalPaired.DoubleBlueAdvantage == 0
                && identicalPaired.TripleBlueAdvantage == 0,
                "paired backtest recognizes identical nested blue predictions");
            var offsetPaired = PositionPredictor.CreateRangeCoverageShadow(fullData, candidateStore)
                .CompareBacktest(primaryPredictor, 30, 5);
            Assert(offsetPaired.OffsetFromLatest == 5 && offsetPaired.EndIssue < paired.EndIssue,
                "paired backtest honors historical offset");
        });
    }
    private static void VerifyAnnualShortTermPredictorScope()
    {
        var olderYear = BuildRecords(40);
        for (int index = 0; index < olderYear.Count; index++)
        {
            olderYear[index].Period = 2024001 + index;
            olderYear[index].DrawDate = new DateTime(2024, 1, 1).AddDays(index * 3);
        }
        var previousYear = BuildRecords(50);
        var currentYear = BuildRecords(40);
        for (int index = 0; index < currentYear.Count; index++)
        {
            currentYear[index].Period = 2026001 + index;
            currentYear[index].DrawDate = new DateTime(2026, 1, 1).AddDays(index * 3);
        }
        var records = olderYear.Concat(previousYear).Concat(currentYear)
            .OrderBy(record => record.Period)
            .ToArray();
    
        var data = new FakeDataService();
        data.SetRecords(records);
        var predictor = new PositionPredictor(data);
        var mature = predictor.Predict(2026036, "backtest");
    
        var altered = records.Select(Clone).ToArray();
        foreach (var record in altered.Where(record => record.Period < 2026006))
            record.RedBalls = new[] { 2, 3, 5, 7, 11, 13 };
        var alteredData = new FakeDataService();
        alteredData.SetRecords(altered);
        var matureAltered = new PositionPredictor(alteredData).Predict(2026036, "backtest");
        Assert(mature.RedPoints.SequenceEqual(matureAltered.RedPoints),
            "annual predictor ignores prior year after thirty current-year draws");
        var early = predictor.Predict(2026011, "backtest");
        var expectedBorrowedHistory = previousYear.TakeLast(20).Concat(currentYear.Take(10)).ToArray();
        foreach (var score in early.RedScores)
        {
            int expectedFrequency = expectedBorrowedHistory.Count(record => record.RedBalls.Contains(score.Ball));
            Assert(score.HistoryFrequency == expectedFrequency,
                $"early annual predictor borrows only required 2025 tail for ball {score.Ball}");
        }
    
        var shortPreviousYear = previousYear.TakeLast(12).ToArray();
        var limitedRecords = olderYear.Concat(shortPreviousYear).Concat(currentYear.Take(11))
            .OrderBy(record => record.Period)
            .ToArray();
        var limitedData = new FakeDataService();
        limitedData.SetRecords(limitedRecords);
        var limitedEarly = new PositionPredictor(limitedData).Predict(2026011, "backtest");
        var expectedLimitedHistory = shortPreviousYear.Concat(currentYear.Take(10)).ToArray();
        foreach (var score in limitedEarly.RedScores)
        {
            int expectedFrequency = expectedLimitedHistory.Count(record => record.RedBalls.Contains(score.Ball));
            Assert(score.HistoryFrequency == expectedFrequency,
                $"annual predictor never fills a remaining gap from 2024 for ball {score.Ball}");
        }
    }
}
