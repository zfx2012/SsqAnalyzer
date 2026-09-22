using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{

    private static PositionEvaluation Evaluate(PositionPrediction prediction, DrawRecord actual)
    {
        int hitPoints = actual.RedBalls.Count(actualBall =>
            prediction.RedPoints.Any(point => PositionPointRange.Contains(point, actualBall, 33)));
        int rating = hitPoints >= 5 ? 5 : hitPoints >= 4 ? 4 : hitPoints >= 3 ? 3 : hitPoints >= 2 ? 2 : 1;
        return new PositionEvaluation(hitPoints, rating);
    }

    private static PositionPrediction CopyWithEvaluation(PositionPrediction source, PositionEvaluation evaluation) => new()
    {
        Issue = source.Issue,
        AsOfIssue = source.AsOfIssue,
        SnapshotId = source.SnapshotId,
        RuleVersionId = source.RuleVersionId,
        RunId = source.RunId,
        RunMode = source.RunMode,
        CombinationScore = source.CombinationScore,
        RedPoints = source.RedPoints,
        FormulaBlue = source.FormulaBlue,
        ExclusionBlue = source.ExclusionBlue,
        BlueFormulaName = source.BlueFormulaName,
        BlueFormulaHitRate = source.BlueFormulaHitRate,
        BlueExclusionHitRate = source.BlueExclusionHitRate,
        BluePrimaryPath = source.BluePrimaryPath,
        SingleBlue = source.SingleBlue,
        DoubleBlue = source.DoubleBlue,
        TripleBlue = source.TripleBlue,
        RedScores = source.RedScores,
        BlueScores = source.BlueScores,
        Actual = source.Actual,
        Evaluation = evaluation
    };

    private List<DrawRecord> OrderedRecords() => _dataService.GetAllRecords()
        .OrderBy(record => record.Period)
        .ToList();

    private bool ShouldFreeze(PositionPrediction prediction, IReadOnlyList<DrawRecord> records)
    {
        if (_validationStore is null
            || prediction.IsDrawn
            || !string.Equals(prediction.RunMode, "live", StringComparison.OrdinalIgnoreCase)) return false;

        var latest = records[^1];
        if (prediction.Issue != ComputeNextIssue(latest)) return false;

        DateTime chinaNow = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"));
        DateTime freezeDeadline = ComputeNextDrawDate(latest).AddHours(21).AddMinutes(15);
        return chinaNow < freezeDeadline;
    }

    private static int ComputeNextIssue(DrawRecord latest)
    {
        var nextDate = ComputeNextDrawDate(latest);
        return nextDate.Year > latest.DrawDate.Year ? nextDate.Year * 1000 + 1 : latest.Period + 1;
    }

    private static DateTime ComputeNextDrawDate(DrawRecord latest)
    {
        int offset = latest.DrawDate.DayOfWeek switch
        {
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Sunday => 2,
            _ => 2
        };
        return latest.DrawDate.Date.AddDays(offset);
    }

    private static string CreateSnapshotId(IReadOnlyList<DrawRecord> history)
    {
        var text = string.Join('|', history.Select(record =>
            $"{record.Period}:{string.Join(',', record.RedBalls)}:{record.BlueBall}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
    }

    private static string CreateRunId(int issue, string snapshotId, string ruleVersion)
    {
        var text = $"{issue}|{snapshotId}|{ruleVersion}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    private static string CreateConfigId(PositionRuleConfig config)
    {
        var json = JsonSerializer.Serialize(config);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..8];
    }

    private static void ValidateConfig(PositionRuleConfig config)
    {
        if (config.Window30 < config.Window15 || config.Window15 < config.Window5 || config.Window5 < 1)
            throw new ArgumentException("红球窗口必须满足 30期 >= 15期 >= 近期窗口 > 0");
        if (config.BlueHotWindow < 1 || config.FormulaBacktestWindow < 30)
            throw new ArgumentException("蓝球窗口配置无效");
        if (config.CandidatePoolSize is < 6 or > 33)
            throw new ArgumentOutOfRangeException(nameof(config), "红球候选池必须在 6 到 33 之间");
        ValidateWeightSum(config.RedLongTermWeight, config.RedWindow30Weight,
            config.RedWindow15Weight, config.RedRecentStructureWeight);
        ValidateWeightSum(config.CombinationBaseWeight, config.CombinationPatternWeight, config.CombinationCoverageWeight);
        ValidateWeightSum(config.BlueFormulaWeight, config.BlueExclusionWeight);
    }

    private static void ValidateWeightSum(params double[] weights)
    {
        if (weights.Any(weight => weight < 0) || Math.Abs(weights.Sum() - 1.0) > 1e-9)
            throw new ArgumentException("策略权重必须为非负数且总和为 1");
    }
}
