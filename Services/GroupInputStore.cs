using System.Security.Cryptography;
using System.Text;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

namespace SsqAnalyzer.Services;

/// <summary>保存用户在点位页和杀号页明确生成的本期结果，供组号页按期读取。</summary>
public sealed class GroupInputStore
{
    private readonly Dictionary<int, PositionGroupSnapshot> _positions = new();
    private readonly Dictionary<int, KillPoolSnapshot> _killPools = new();

    public event Action? Changed;

    public void SavePosition(PositionPrediction prediction)
    {
        if (prediction.RedPoints.Count != 6
            || prediction.RedPoints.Distinct().Count() != 6
            || prediction.RedPoints.Any(number => number is < 1 or > 33))
            return;

        _positions[prediction.Issue] = new PositionGroupSnapshot(
            prediction.Issue,
            prediction.AsOfIssue,
            prediction.SnapshotId,
            prediction.RuleVersionId,
            DateTimeOffset.Now,
            prediction.RedPoints.OrderBy(number => number).ToArray());
        Changed?.Invoke();
    }

    public void SaveKill(KillReport report)
    {
        var reds = report.RecommendedRedBalls.Distinct().OrderBy(number => number).ToArray();
        var blues = report.RecommendedBlueBalls.Distinct().OrderBy(number => number).ToArray();
        if (reds.Any(number => number is < 1 or > 33)
            || blues.Any(number => number is < 1 or > 16))
            return;

        string payload = $"{report.TargetPeriod}|{string.Join(',', reds)}|{string.Join(',', blues)}";
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..12];
        _killPools[report.TargetPeriod] = new KillPoolSnapshot(
            report.TargetPeriod, id, report.GeneratedAt, reds, blues);
        Changed?.Invoke();
    }

    public PositionGroupSnapshot? GetPosition(int issue) => _positions.GetValueOrDefault(issue);
    public KillPoolSnapshot? GetKillPool(int issue) => _killPools.GetValueOrDefault(issue);
    public PositionGroupSnapshot? GetLatestPosition() => _positions.Values.OrderByDescending(x => x.TargetIssue).FirstOrDefault();
    public KillPoolSnapshot? GetLatestKillPool() => _killPools.Values.OrderByDescending(x => x.TargetIssue).FirstOrDefault();
}
