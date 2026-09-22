namespace SsqAnalyzer.Models;

public enum GroupSourceKind { Trend, TicketStats, Position, KillPool }

public enum GroupSourceState { Ready, Missing, Stale, Invalid }

public enum TrendViewKind { Basic, Historical, Route012, Parity, Cycle }

public sealed record TrendViewSnapshot(
    TrendViewKind View,
    string Name,
    IReadOnlyList<int> ColdRedNumbers,
    IReadOnlyList<int> ColdBlueNumbers,
    IReadOnlyList<int> DiagonalRedCandidates,
    IReadOnlyList<int> DiagonalBlueCandidates,
    int SampleSize,
    string FilterDescription,
    IReadOnlyList<DrawRecord> ChartRows);

public sealed record TrendGroupSnapshot(
    int TargetIssue,
    int AsOfIssue,
    string SnapshotId,
    IReadOnlyList<DrawRecord> PreviewDraws,
    IReadOnlyList<TrendViewSnapshot> Views);

public sealed record TicketStatsSnapshot(
    int TargetIssue,
    int TicketCount,
    string SnapshotId,
    IReadOnlyList<int> RedCounts,
    IReadOnlyList<int> BlueCounts);

public sealed record PositionGroupSnapshot(
    int TargetIssue,
    int AsOfIssue,
    string SnapshotId,
    string RuleVersionId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<int> RedPoints);

public sealed record KillPoolSnapshot(
    int TargetIssue,
    string SnapshotId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<int> RemainingRedNumbers,
    IReadOnlyList<int> RemainingBlueNumbers);

public sealed record GroupSourceStatus(
    GroupSourceKind Kind,
    string Name,
    GroupSourceState State,
    string Summary,
    int? SourceIssue = null)
{
    public bool IsReady => State == GroupSourceState.Ready;
}

public sealed record GroupInputBundle(
    int TargetIssue,
    TrendGroupSnapshot? Trend,
    TicketStatsSnapshot? TicketStats,
    PositionGroupSnapshot? Position,
    KillPoolSnapshot? KillPool,
    IReadOnlyList<GroupSourceStatus> Statuses)
{
    public int ReadyCount => Statuses.Count(status => status.IsReady);
    public GroupSourceStatus Status(GroupSourceKind kind) => Statuses.Single(status => status.Kind == kind);
}

public sealed record GroupGeneratedTicket(
    int Index,
    IReadOnlyList<int> RedBalls,
    int BlueBall,
    double StrategyScore,
    string Evidence);

public sealed record GroupGenerationResult(
    int TargetIssue,
    string RuleVersionId,
    string InputId,
    IReadOnlyList<string> UsedSources,
    IReadOnlyList<string> SkippedSources,
    IReadOnlyList<GroupGeneratedTicket> Tickets);
