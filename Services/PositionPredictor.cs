using System.IO;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor : IPositionPredictor
{

    public const string CurrentRuleVersion = "position-v5.0-annual-short";
    public const string PreviousPrimaryRuleVersion = "position-v3.2.2";
    public const string LegacyPrimaryRuleVersion = "position-v3.2.1";
    public const string FormulaEnsembleRuleVersion = "position-v3.3.1-shadow";
    public const string RollingHotRuleVersion = "position-v3.4.1-shadow";
    public const string RangeCoverageRuleVersion = "position-v3.6-shadow";
    public const string FormulaAnchoredRuleVersion = "position-v3.7-shadow";
    public const string AdaptiveBlueRuleVersion = "position-v3.8-shadow";
    public const string PointTransitionShadowRuleVersion = "position-v4.1-shadow";
    public const string PointBalancedTransitionShadowRuleVersion = "position-v4.2-shadow";
    public const string PointRegularizedTransitionShadowRuleVersion = "position-v4.3-shadow";
    public const string PointRecentStructureShadowRuleVersion = "position-v4.4-shadow";
    public const string PointShrunkSeasonTransitionShadowRuleVersion = "position-v4.5-shadow";
    public const string AnnualShortNoStructureShadowRuleVersion = "position-v5.1-annual-short-no-structure-shadow";
    public const string AnnualShortRangeEventGlobalShadowRuleVersion = "position-v5.2-annual-short-range-event-global-shadow";
    public const string AnnualShortDynamicHierarchicalShadowRuleVersion =
        "position-v5.3-annual-short-dynamic-hierarchical-global-shadow";
    public const string CurrentShadowRuleVersion = AnnualShortDynamicHierarchicalShadowRuleVersion;
    private const int RollingHotWindow = 30;
    private const int AnnualShortHistoryWindow = 30;

    private readonly IDataService _dataService;
    private readonly IPositionValidationStore? _validationStore;
    private readonly PositionRuleConfig _config;
    private readonly BlueFormulaMode _formulaMode;
    private readonly RedSelectionMode _redSelectionMode;
    private readonly BlueSelectionMode _blueSelectionMode;
    private readonly AnnualShortPointProfile? _annualShortProfile;
    private readonly string _ruleVersionId;

    public PositionPredictor(IDataService dataService)
        : this(dataService, new PositionRuleConfig(), null) { }

    public PositionPredictor(IDataService dataService, IPositionValidationStore validationStore)
        : this(dataService, new PositionRuleConfig(), validationStore) { }

    internal PositionPredictor(IDataService dataService, PositionRuleConfig config)
        : this(dataService, config, null) { }

    internal PositionPredictor(
        IDataService dataService,
        PositionRuleConfig config,
        IPositionValidationStore? validationStore)
        : this(
            dataService,
            config,
            validationStore,
            BlueFormulaMode.Legacy,
            CurrentRuleVersion,
            RedSelectionMode.AnnualShortTermCoverage,
            BlueSelectionMode.CombinedRanking) { }

    private PositionPredictor(
        IDataService dataService,
        PositionRuleConfig config,
        IPositionValidationStore? validationStore,
        BlueFormulaMode formulaMode,
        string baseRuleVersion,
        RedSelectionMode redSelectionMode = RedSelectionMode.Legacy,
        BlueSelectionMode blueSelectionMode = BlueSelectionMode.CombinedRanking,
        AnnualShortPointProfile? annualShortProfile = null)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _validationStore = validationStore;
        _config = config;
        _formulaMode = formulaMode;
        _redSelectionMode = redSelectionMode;
        _blueSelectionMode = blueSelectionMode;
        _annualShortProfile = annualShortProfile;
        ValidateConfig(config);
        _ruleVersionId = $"{baseRuleVersion}+{CreateConfigId(config)}";
    }

    public string RuleVersionId => _ruleVersionId;

    public IReadOnlyList<int> GetIssueOptions()
    {
        var records = OrderedRecords();
        if (records.Count == 0) return Array.Empty<int>();

        var result = records.Skip(1).Select(record => record.Period).ToList();
        result.Add(ComputeNextIssue(records[^1]));
        result.Reverse();
        return result;
    }

    public PositionPrediction Predict(int issue, string runMode = "live")
    {
        var records = OrderedRecords();
        var history = records.Where(record => record.Period < issue).ToList();
        if (history.Count == 0)
            throw new InvalidOperationException($"期号 {issue} 之前没有可用于预测的开奖数据");

        var actual = records.FirstOrDefault(record => record.Period == issue);
        DateTime targetDrawDate = actual?.DrawDate ?? ComputeNextDrawDate(history[^1]);
        var pointHistory = GetAnnualShortHistory(history, issue);
        var redScores = ScoreRedBalls(pointHistory, issue);
        var combination = _redSelectionMode switch
        {
            RedSelectionMode.AnnualShortTermCoverage =>
                _annualShortProfile is { } profile
                    ? profile.SelectionObjective == AnnualShortSelectionObjective.BallCoverage
                        ? SelectAnnualShortTermCoverageCombination(
                            pointHistory,
                            profile.Window30Weight,
                            profile.Window15Weight,
                            profile.Window5Weight,
                            profile.StructureWeight,
                            profile.StructureSignals,
                            profile.FrequencyMode,
                            profile.DecayHalfLife)
                        : SelectAnnualShortRangeEventCombination(
                            pointHistory,
                            profile.Window30Weight,
                            profile.Window15Weight,
                            profile.Window5Weight,
                            profile.SelectionObjective,
                            profile.RangeEventWeight)
                    : SelectAnnualShortTermCoverageCombination(pointHistory),
            RedSelectionMode.RangeCoverage => SelectRangeCoverageCombination(history),
            RedSelectionMode.CategoryTransitionCoverage =>
                SelectCategoryTransitionCoverageCombination(history),
            RedSelectionMode.ZoneRouteTransitionCoverage =>
                SelectCategoryTransitionCoverageCombination(
                    history,
                    strength: 0.10,
                    useZone: true,
                    useParity: false,
                    useRoute: true),
            RedSelectionMode.RegularizedZoneRouteTransitionCoverage =>
                SelectCategoryTransitionCoverageCombination(
                    history,
                    strength: 0.05,
                    useZone: true,
                    useParity: false,
                    useRoute: true,
                    priorStrength: 120),
            RedSelectionMode.RecentStructureCoverage =>
                SelectRecentStructureCoverageCombination(redScores, history),
            RedSelectionMode.ShrunkSeasonTransitionCoverage =>
                SelectShrunkSeasonTransitionCoverageCombination(history, targetDrawDate),
            _ => SelectRedCombination(redScores, history)
        };
        var blueSelection = ScoreBlueBalls(history, issue);
        var nestedBlue = BuildNestedBlueSelection(blueSelection);
        var snapshotId = CreateSnapshotId(history);
        string normalizedRunMode = string.IsNullOrWhiteSpace(runMode) ? "live" : runMode;
        var prediction = new PositionPrediction
        {
            Issue = issue,
            AsOfIssue = history[^1].Period,
            SnapshotId = snapshotId,
            RuleVersionId = RuleVersionId,
            RunId = CreateRunId(issue, snapshotId, RuleVersionId),
            RunMode = normalizedRunMode,
            CombinationScore = combination.Score,
            RedPoints = combination.Balls,
            FormulaBlue = blueSelection.FormulaBlue,
            ExclusionBlue = blueSelection.ExclusionBlue,
            BlueFormulaName = blueSelection.FormulaName,
            BlueFormulaHitRate = blueSelection.FormulaHitRate,
            BlueExclusionHitRate = blueSelection.ExclusionHitRate,
            BluePrimaryPath = blueSelection.PrimaryPath,
            SingleBlue = nestedBlue[0],
            DoubleBlue = nestedBlue.Take(2).OrderBy(ball => ball).ToArray(),
            TripleBlue = nestedBlue.Take(3).OrderBy(ball => ball).ToArray(),
            RedScores = redScores.OrderByDescending(score => score.TotalScore).ThenBy(score => score.Ball).ToArray(),
            BlueScores = blueSelection.Scores.OrderByDescending(score => score.CombinedScore).ThenBy(score => score.Ball).ToArray(),
            Actual = actual
        };

        var result = actual is null
            ? prediction
            : CopyWithEvaluation(prediction, Evaluate(prediction, actual));
        if (ShouldFreeze(result, records))
        {
            try
            {
                _validationStore!.Freeze(result);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The recommendation remains usable; the validation status exposes the ledger error.
            }
        }
        return result;
    }
}
