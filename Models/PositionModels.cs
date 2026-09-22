using System.Text.Json.Serialization;

namespace SsqAnalyzer.Models;

public static class PositionPointRange
{
    public const int Radius = 1;

    public static bool Contains(int point, int actual, int maxBall) =>
        point is >= 1 && point <= maxBall
        && actual is >= 1 && actual <= maxBall
        && Math.Abs(point - actual) <= Radius;

    public static double RandomHitProbability(
        int point,
        int maxBall,
        int drawCount)
    {
        if (point is < 1 || point > maxBall) throw new ArgumentOutOfRangeException(nameof(point));
        if (drawCount is < 0 || drawCount > maxBall)
            throw new ArgumentOutOfRangeException(nameof(drawCount));

        int minimum = Math.Max(1, point - Radius);
        int maximum = Math.Min(maxBall, point + Radius);
        int width = maximum - minimum + 1;
        if (drawCount == 0) return 0;
        if (drawCount > maxBall - width) return 1;

        double missProbability = 1;
        for (int index = 0; index < drawCount; index++)
            missProbability *= (maxBall - width - index) / (double)(maxBall - index);
        return 1 - missProbability;
    }

    public static double RandomExpectedLitCount(
        IEnumerable<int> points,
        int maxBall,
        int drawCount) => points.Sum(point => RandomHitProbability(point, maxBall, drawCount));
}

public static class PositionValidationPolicy
{
    public const int MinimumPairedSampleSize = 100;
}

public sealed record PositionRuleConfig(
    int Window30 = 30,
    int Window15 = 15,
    int Window5 = 5,
    int BlueHotWindow = 16,
    int CandidatePoolSize = 15,
    int FormulaBacktestWindow = 300,
    double RedLongTermWeight = 0.18,
    double RedWindow30Weight = 0.25,
    double RedWindow15Weight = 0.27,
    double RedRecentStructureWeight = 0.30,
    double CombinationBaseWeight = 0.50,
    double CombinationPatternWeight = 0.25,
    double CombinationCoverageWeight = 0.25,
    double BlueFormulaWeight = 0.55,
    double BlueExclusionWeight = 0.45);

public sealed record PositionBallScore(
    int Ball,
    int Omission,
    int HistoryFrequency,
    int Frequency30,
    int Frequency15,
    int Frequency5,
    double LongTermFeature,
    double Window30Feature,
    double Window15Feature,
    double RecentStructureFeature,
    double TotalScore,
    string DirectionLabel);

public sealed record PositionBlueScore(
    int Ball,
    int Omission,
    int HistoryFrequency,
    int Frequency30,
    int Frequency16,
    int Frequency5,
    double FormulaScore,
    double ExclusionScore,
    double CombinedScore,
    string FormulaVotes,
    string ExclusionReason);

public sealed record PositionEvaluation(
    int HitPoints,
    int Rating)
{
    public string HitLabel => HitPoints.ToString();
    public string RatingLabel => new string('★', Math.Clamp(Rating, 1, 5));
}

public sealed class PositionPrediction
{
    public int Issue { get; init; }
    public int AsOfIssue { get; init; }
    public string SnapshotId { get; init; } = "";
    public string RuleVersionId { get; init; } = "";
    public string RunId { get; init; } = "";
    public string RunMode { get; init; } = "live";
    public double CombinationScore { get; init; }

    public IReadOnlyList<int> RedPoints { get; init; } = Array.Empty<int>();
    public int FormulaBlue { get; init; }
    public int ExclusionBlue { get; init; }
    public string BlueFormulaName { get; init; } = "";
    public double BlueFormulaHitRate { get; init; }
    public double BlueExclusionHitRate { get; init; }
    public string BluePrimaryPath { get; init; } = "";
    public int SingleBlue { get; init; }
    public IReadOnlyList<int> DoubleBlue { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> TripleBlue { get; init; } = Array.Empty<int>();

    public IReadOnlyList<PositionBallScore> RedScores { get; init; } = Array.Empty<PositionBallScore>();
    public IReadOnlyList<PositionBlueScore> BlueScores { get; init; } = Array.Empty<PositionBlueScore>();
    public DrawRecord? Actual { get; init; }
    public PositionEvaluation? Evaluation { get; init; }
    public bool IsDrawn => Actual is not null;
}

public sealed record PositionBacktestReport(
    int SampleSize,
    int StartIssue,
    int EndIssue,
    double AveragePointHits,
    double RandomAveragePointHits,
    double FormulaBlueHitRate,
    double ExclusionBlueHitRate,
    double SingleBlueHitRate,
    double DoubleBlueHitRate,
    double TripleBlueHitRate,
    double FormulaBlueWilsonLower95,
    double ExclusionBlueWilsonLower95,
    double SingleBlueWilsonLower95,
    double FirstHalfSingleBlueHitRate,
    double SecondHalfSingleBlueHitRate,
    double PointLiftLower95,
    double FirstHalfPointLift,
    double SecondHalfPointLift,
    bool IsRedStatisticallyUsable,
    bool IsBlueStatisticallyUsable,
    bool IsStatisticallyUsable)
{
    public int OffsetFromLatest { get; init; }
    public IReadOnlyList<double> PointColumnHitRates { get; init; } = Array.Empty<double>();

    public double PointLift => AveragePointHits - RandomAveragePointHits;
    public string Verdict => IsStatisticallyUsable
        ? "红球和蓝球均达到统计可用门槛"
        : $"红球{(IsRedStatisticallyUsable ? "已达到" : "未达到")}、蓝球{(IsBlueStatisticallyUsable ? "已达到" : "未达到")}统计可用门槛";
}

public sealed record PositionBacktestComparison(
    int SampleSize,
    int OffsetFromLatest,
    int StartIssue,
    int EndIssue,
    string PrimaryRuleVersionId,
    string CandidateRuleVersionId,
    int DifferentPointPredictionCount,
    int DifferentSingleBlueCount,
    double PrimaryAveragePointHits,
    double CandidateAveragePointHits,
    double CandidatePointAdvantage,
    double CandidatePointAdvantageLower95,
    double FirstHalfPointAdvantage,
    double SecondHalfPointAdvantage,
    double PrimarySingleBlueHitRate,
    double CandidateSingleBlueHitRate,
    int CandidateSingleBlueWins,
    int PrimarySingleBlueWins,
    int BothSingleBlueHits,
    int DiscordantBlueOutcomeCount,
    double CandidateBlueWinRateAmongDiscordant,
    double CandidateBlueWinWilsonLower95,
    bool IsRedPairwiseSuperior,
    bool IsBluePairwiseSuperior,
    bool IsPairwiseSuperior)
{
    public int DifferentDoubleBlueCount { get; init; }
    public int DifferentTripleBlueCount { get; init; }
    public double PrimaryDoubleBlueHitRate { get; init; }
    public double CandidateDoubleBlueHitRate { get; init; }
    public double DoubleBlueAdvantage { get; init; }
    public double DoubleBlueAdvantageLower95 { get; init; }
    public double FirstHalfDoubleBlueAdvantage { get; init; }
    public double SecondHalfDoubleBlueAdvantage { get; init; }
    public double PrimaryTripleBlueHitRate { get; init; }
    public double CandidateTripleBlueHitRate { get; init; }
    public double TripleBlueAdvantage { get; init; }
    public double TripleBlueAdvantageLower95 { get; init; }
    public double FirstHalfTripleBlueAdvantage { get; init; }
    public double SecondHalfTripleBlueAdvantage { get; init; }
    public bool IsDoubleBluePairwiseSuperior { get; init; }
    public bool IsTripleBluePairwiseSuperior { get; init; }
    public bool IsNestedBluePairwiseSuperior =>
        IsDoubleBluePairwiseSuperior && IsTripleBluePairwiseSuperior;

    public string Verdict => IsPairwiseSuperior
        ? "候选红球和独蓝均通过历史配对优势门槛"
        : $"候选红球{(IsRedPairwiseSuperior ? "已通过" : "未通过")}、独蓝{(IsBluePairwiseSuperior ? "已通过" : "未通过")}历史配对优势门槛";
}

public sealed class PositionForwardRecord
{
    public int Issue { get; init; }
    public int AsOfIssue { get; init; }
    public string SnapshotId { get; init; } = "";
    public string RuleVersionId { get; init; } = "";
    public string RunId { get; init; } = "";
    public DateTime FrozenAtUtc { get; init; }
    public IReadOnlyList<int> RedPoints { get; init; } = Array.Empty<int>();
    [JsonPropertyName("DragonHead"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? LegacyDragonHead { get; init; }
    [JsonPropertyName("PhoenixTail"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? LegacyPhoenixTail { get; init; }
    [JsonPropertyName("GoldAnchors"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? LegacyGoldAnchors { get; init; }
    public int FormulaBlue { get; init; }
    public int ExclusionBlue { get; init; }
    public int SingleBlue { get; init; }
    public IReadOnlyList<int> DoubleBlue { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> TripleBlue { get; init; } = Array.Empty<int>();
    public string PredictionHash { get; set; } = "";

    public DateTime? EvaluatedAtUtc { get; set; }
    public IReadOnlyList<int> ActualReds { get; set; } = Array.Empty<int>();
    public int? ActualBlue { get; set; }
    public int PointHits { get; set; }
    public bool FormulaBlueHit { get; set; }
    public bool ExclusionBlueHit { get; set; }
    public bool SingleBlueHit { get; set; }
    public bool DoubleBlueHit { get; set; }
    public bool TripleBlueHit { get; set; }
    public bool IsEvaluated => ActualBlue.HasValue;
}

public sealed record PositionLedgerIntegrityReport(
    int TotalRecords,
    int SealedRecords,
    int UnsealedRecords,
    bool IsFullySealed);

public sealed record PositionExperimentComparison(
    string PrimaryRuleVersionId,
    string CandidateRuleVersionId,
    int PairedFrozenCount,
    int PairedEvaluatedCount,
    int DifferentPointPredictionCount,
    int DifferentFormulaBlueCount,
    int DifferentExclusionBlueCount,
    int DifferentSingleBlueCount,
    int DifferentNestedBlueCount,
    int CandidateSingleBlueWins,
    int PrimarySingleBlueWins,
    int BothSingleBlueHits,
    int DiscordantBlueOutcomeCount,
    double PrimaryAveragePointHits,
    double CandidateAveragePointHits,
    double CandidatePointAdvantage,
    double CandidatePointAdvantageLower95,
    double PrimarySingleBlueHitRate,
    double CandidateSingleBlueHitRate,
    double CandidateBlueWinRateAmongDiscordant,
    double CandidateBlueWinWilsonLower95,
    bool CandidateMeetsAbsoluteGate,
    bool IsCandidatePromotable)
{
    public int DifferentDoubleBlueCount { get; init; }
    public int DifferentTripleBlueCount { get; init; }
    public double PointAdvantageFirstHalf { get; init; }
    public double PointAdvantageSecondHalf { get; init; }
    public double SingleBlueAdvantageFirstHalf { get; init; }
    public double SingleBlueAdvantageSecondHalf { get; init; }
    public double PrimaryDoubleBlueHitRate { get; init; }
    public double CandidateDoubleBlueHitRate { get; init; }
    public double DoubleBlueAdvantage { get; init; }
    public double DoubleBlueAdvantageLower95 { get; init; }
    public double DoubleBlueAdvantageFirstHalf { get; init; }
    public double DoubleBlueAdvantageSecondHalf { get; init; }
    public double PrimaryTripleBlueHitRate { get; init; }
    public double CandidateTripleBlueHitRate { get; init; }
    public double TripleBlueAdvantage { get; init; }
    public double TripleBlueAdvantageLower95 { get; init; }
    public double TripleBlueAdvantageFirstHalf { get; init; }
    public double TripleBlueAdvantageSecondHalf { get; init; }
    public bool IsPointPairwiseStable { get; init; }
    public bool IsRangePointPairwiseStable { get; init; }
    public bool IsSingleBluePairwiseStable { get; init; }
    public bool IsDoubleBluePairwiseStable { get; init; }
    public bool IsTripleBluePairwiseStable { get; init; }
    public bool CandidateMeetsAllRelativeGates { get; init; }
    public bool CandidateMeetsPointAbsoluteGate { get; init; }
    public bool CandidateMeetsBallCoverageAbsoluteGate { get; init; }
    public bool CandidateMeetsRangePointAbsoluteGate { get; init; }
    public double PrimaryAverageRangePointHits { get; init; }
    public double CandidateAverageRangePointHits { get; init; }
    public double CandidateRangePointAdvantage { get; init; }
    public double CandidateRangePointAdvantageLower95 { get; init; }
    public double RangePointAdvantageFirstHalf { get; init; }
    public double RangePointAdvantageSecondHalf { get; init; }
    public bool IsPointOnlyPromotable => PairedEvaluatedCount >= PositionValidationPolicy.MinimumPairedSampleSize
        && DifferentPointPredictionCount > 0
        && CandidateMeetsPointAbsoluteGate
        && IsPointPairwiseStable
        && IsRangePointPairwiseStable;

    public string PointVerdict => PairedEvaluatedCount == 0
        ? "等待点位配对开奖样本"
        : DifferentPointPredictionCount == 0
            ? "候选点位与正式版没有差异"
            : PairedEvaluatedCount < PositionValidationPolicy.MinimumPairedSampleSize
                ? $"点位前向样本 {PairedEvaluatedCount}/{PositionValidationPolicy.MinimumPairedSampleSize}"
                : !CandidateMeetsBallCoverageAbsoluteGate
                    ? "候选18码红球覆盖尚未稳定超过随机基线"
                    : !CandidateMeetsRangePointAbsoluteGate
                        ? "候选点位点亮尚未稳定超过随机基线"
                    : !IsPointPairwiseStable
                        ? "候选点位尚未稳定优于正式版"
                        : !IsRangePointPairwiseStable
                            ? "候选点位范围命中尚未稳定优于正式版"
                            : "候选点位已通过单独晋升门槛";

    public string Verdict => PairedEvaluatedCount == 0
        ? DifferentSingleBlueCount == 0
            ? "等待配对开奖样本；当前冻结独蓝没有分歧"
            : "等待配对开奖样本"
        : IsCandidatePromotable
            ? "候选版本达到全部晋升门槛"
            : !CandidateMeetsAbsoluteGate
                ? "候选版本尚未通过绝对随机基线门槛"
                : !CandidateMeetsAllRelativeGates
                    ? "候选版本尚未在全部变化维度证明稳定前向优势"
                : DiscordantBlueOutcomeCount < 30
                    ? "独蓝有效分歧胜负样本不足"
                    : CandidateBlueWinWilsonLower95 <= 0.5
                        ? "候选独蓝尚未证明优于主版本"
                        : DifferentPointPredictionCount > 0 && CandidatePointAdvantageLower95 <= 0
                            ? "候选点位尚未证明优于主版本"
                            : "配对样本尚未达到晋升门槛";
}

public sealed record PositionForwardSummary(
    string RuleVersionId,
    int FrozenCount,
    int EvaluatedCount,
    int PendingCount,
    int? FirstIssue,
    int? LastIssue,
    double AveragePointHits,
    double RandomAveragePointHits,
    double FormulaBlueHitRate,
    double ExclusionBlueHitRate,
    double SingleBlueHitRate,
    double DoubleBlueHitRate,
    double TripleBlueHitRate,
    double SingleBlueWilsonLower95,
    double FirstHalfSingleBlueHitRate,
    double SecondHalfSingleBlueHitRate,
    double PointLiftLower95,
    double FirstHalfPointLift,
    double SecondHalfPointLift,
    bool IsRedStatisticallyUsable,
    bool IsBlueStatisticallyUsable,
    bool IsStatisticallyUsable)
{
    public double AverageRangePointHits { get; init; }
    public double RandomAverageRangePointHits { get; init; }
    public double RangePointLift => AverageRangePointHits - RandomAverageRangePointHits;
    public double RangePointLiftLower95 { get; init; }
    public double FirstHalfRangePointLift { get; init; }
    public double SecondHalfRangePointLift { get; init; }
    public bool IsBallCoverageStatisticallyUsable { get; init; }
    public bool IsRangePointStatisticallyUsable { get; init; }
    public double PointLift => AveragePointHits - RandomAveragePointHits;
    public string Verdict => EvaluatedCount == 0 ? "等待前向开奖样本"
        : IsStatisticallyUsable ? "前向红球和蓝球均达到统计可用门槛"
        : $"前向红球{(IsRedStatisticallyUsable ? "已达到" : "未达到")}、蓝球{(IsBlueStatisticallyUsable ? "已达到" : "未达到")}统计可用门槛";
}
