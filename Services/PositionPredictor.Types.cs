using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{
    [Flags]
    internal enum AnnualShortStructureSignals
    {
        None = 0,
        Zone = 1,
        Parity = 2,
        Route = 4,
        Prime = 8,
        Repeat = 16,
        Neighbor = 32,
        Category = Zone | Parity | Route | Prime,
        Transition = Repeat | Neighbor,
        All = Category | Transition
    }

    internal enum AnnualShortFrequencyMode
    {
        MultiWindow,
        ExponentialDecay
    }

    internal enum AnnualShortSelectionObjective
    {
        BallCoverage,
        RangeEventGreedy,
        RangeEventGlobal,
        RangeEventBlendGlobal,
        RangeEventHierarchicalGlobal,
        RangeEventAdaptiveWindowGlobal,
        RangeEventGlobalBallTieBreak,
        RangeEventRegionCalibratedGlobal,
        RangeEventStateTransitionGlobal,
        RangeEventStructureAnalogGlobal,
        RangeEventOneStandardErrorBallCoverageGlobal,
        RangeEventGlobalMomentumTieBreak,
        RangeEventGlobalJpfCompoundTieBreak,
        RangeEventGlobalJpfMidpointAvoidanceTieBreak,
        RangeEventGlobalStableSignalRankTieBreak,
        StableSignalRankGlobal,
        PrequentialSignalChampionGlobal,
        BaseStableRankConsensusGlobal,
        DiscountedBetaRangeGlobal,
        DynamicHierarchicalBallGlobal,
        DynamicConditionalHierarchicalRangeGlobal,
        DynamicHierarchicalStableBlendGlobal,
        DynamicHierarchicalShapeBallGlobal,
        DynamicHierarchicalEnsembleGlobal,
        DynamicHierarchicalPreviousYearSharedTransferBlendGlobal,
        PositionPolarizationGlobal
    }

    private enum BlueFormulaMode { Legacy, Ensemble, RollingHot }
    private enum RedSelectionMode
    {
        Legacy,
        AnnualShortTermCoverage,
        RangeCoverage,
        CategoryTransitionCoverage,
        ZoneRouteTransitionCoverage,
        RegularizedZoneRouteTransitionCoverage,
        RecentStructureCoverage,
        ShrunkSeasonTransitionCoverage
    }
    private enum BlueSelectionMode { CombinedRanking, FormulaAnchored, ReliabilityGatedFormula }
    private enum BlueFormulaKind { TrendStep, ThreeBlueSum, RedBlueMix, IssueShift, CycleClosest }
    private sealed record FormulaMetric(
        string Name,
        int CurrentBall,
        double RawAccuracy,
        double SmoothedAccuracy,
        double SelectionScore);
    private sealed record AnnualShortPointProfile(
        double Window30Weight,
        double Window15Weight,
        double Window5Weight,
        double StructureWeight,
        AnnualShortStructureSignals StructureSignals = AnnualShortStructureSignals.All,
        AnnualShortFrequencyMode FrequencyMode = AnnualShortFrequencyMode.MultiWindow,
        double DecayHalfLife = 0,
        AnnualShortSelectionObjective SelectionObjective = AnnualShortSelectionObjective.BallCoverage,
        double RangeEventWeight = 1.0);
    private sealed record BlueSelection(
        IReadOnlyList<PositionBlueScore> Scores,
        int FormulaBlue,
        int ExclusionBlue,
        string FormulaName,
        double FormulaHitRate,
        double ExclusionHitRate,
        double FormulaSelectionScore,
        string PrimaryPath,
        int PrimaryBlue);
    private sealed record ExclusionFeature(double Score, string Reason);
    private sealed record PathReliability(double RawAccuracy, double SmoothedAccuracy, double SelectionScore);
    private sealed record CombinationResult(int[] Balls, double Score);
    private sealed record PointCoverageCandidate(int[] Points, HashSet<int> Covered, double Score);
    private sealed record HierarchicalBallRow(
        int Ball,
        double[] Features,
        double Outcome,
        double Weight);

    private sealed record DynamicHierarchicalProbabilityResult(
        IReadOnlyDictionary<int, double> BallProbabilities,
        IReadOnlyDictionary<int, double> RangeProbabilities,
        IReadOnlyList<double> FeatureCoefficients);
}
