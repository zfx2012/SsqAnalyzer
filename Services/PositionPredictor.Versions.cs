using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{

    internal static PositionPredictor CreateFormulaEnsembleShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.Ensemble,
            FormulaEnsembleRuleVersion);

    internal static PositionPredictor CreateRollingHotShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.RollingHot,
            RollingHotRuleVersion);

    internal static PositionPredictor CreateRangeCoverageShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.RollingHot,
            RangeCoverageRuleVersion,
            RedSelectionMode.RangeCoverage);

    internal static PositionPredictor CreateFormulaAnchoredRangeCoverageShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.RollingHot,
            FormulaAnchoredRuleVersion,
            RedSelectionMode.RangeCoverage,
            BlueSelectionMode.FormulaAnchored);

    internal static PositionPredictor CreateAdaptiveBlueRangeCoverageShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.RollingHot,
            AdaptiveBlueRuleVersion,
            RedSelectionMode.RangeCoverage,
            BlueSelectionMode.ReliabilityGatedFormula);

    internal static PositionPredictor CreatePointTransitionShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.Legacy,
            PointTransitionShadowRuleVersion,
            RedSelectionMode.CategoryTransitionCoverage,
            BlueSelectionMode.CombinedRanking);

    internal static PositionPredictor CreatePointBalancedTransitionShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.Legacy,
            PointBalancedTransitionShadowRuleVersion,
            RedSelectionMode.ZoneRouteTransitionCoverage,
            BlueSelectionMode.CombinedRanking);

    internal static PositionPredictor CreatePointRegularizedTransitionShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.Legacy,
            PointRegularizedTransitionShadowRuleVersion,
            RedSelectionMode.RegularizedZoneRouteTransitionCoverage,
            BlueSelectionMode.CombinedRanking);

    internal static PositionPredictor CreatePointRecentStructureShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(
                RedLongTermWeight: 0.10,
                RedWindow30Weight: 0.28,
                RedWindow15Weight: 0.32,
                RedRecentStructureWeight: 0.30),
            validationStore,
            BlueFormulaMode.Legacy,
            PointRecentStructureShadowRuleVersion,
            RedSelectionMode.RecentStructureCoverage,
            BlueSelectionMode.CombinedRanking);

    internal static PositionPredictor CreatePointShrunkSeasonTransitionShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.Legacy,
            PointShrunkSeasonTransitionShadowRuleVersion,
            RedSelectionMode.ShrunkSeasonTransitionCoverage,
            BlueSelectionMode.CombinedRanking);

    internal static PositionPredictor CreateAnnualShortNoStructureShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.Legacy,
            AnnualShortNoStructureShadowRuleVersion,
            RedSelectionMode.AnnualShortTermCoverage,
            BlueSelectionMode.CombinedRanking,
            annualShortProfile: new AnnualShortPointProfile(0.40, 0.35, 0.25, 0.00));

    internal static PositionPredictor CreateAnnualShortRangeEventGlobalShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.Legacy,
            AnnualShortRangeEventGlobalShadowRuleVersion,
            RedSelectionMode.AnnualShortTermCoverage,
            BlueSelectionMode.CombinedRanking,
            annualShortProfile: new AnnualShortPointProfile(
                0.40,
                0.35,
                0.25,
                0.00,
                SelectionObjective: AnnualShortSelectionObjective.RangeEventGlobal));

    internal static PositionPredictor CreateAnnualShortDynamicHierarchicalShadow(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.Legacy,
            AnnualShortDynamicHierarchicalShadowRuleVersion,
            RedSelectionMode.AnnualShortTermCoverage,
            BlueSelectionMode.CombinedRanking,
            annualShortProfile: new AnnualShortPointProfile(
                0.40,
                0.35,
                0.25,
                0.00,
                SelectionObjective: AnnualShortSelectionObjective.DynamicHierarchicalBallGlobal));

    internal static PositionPredictor CreatePreviousPrimary(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.Legacy,
            PreviousPrimaryRuleVersion,
            RedSelectionMode.Legacy,
            BlueSelectionMode.CombinedRanking);

    internal static PositionPredictor CreateLegacyPrimary(
        IDataService dataService,
        IPositionValidationStore validationStore) => new(
            dataService,
            new PositionRuleConfig(),
            validationStore,
            BlueFormulaMode.Legacy,
            LegacyPrimaryRuleVersion,
            RedSelectionMode.Legacy,
            BlueSelectionMode.CombinedRanking);
}
