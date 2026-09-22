using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 最近一期开奖的只读投影 DTO（不暴露原始 DrawRecord 的可变方法）。
/// JS 沙箱通过 JintRuleExecutor 包装为驼峰字段（redBalls/blueBall/...）。
/// </summary>
public sealed class LatestRecordView
{
    public int Period { get; init; }
    public DateTime DrawDate { get; init; }
    public IReadOnlyList<int> RedBalls { get; init; } = Array.Empty<int>();
    public int BlueBall { get; init; }
    public int RedSum { get; init; }
    public int RedSpan { get; init; }
    public int OddCount { get; init; }
    public int EvenCount { get; init; }
    public string ZoneLabel { get; init; } = "";
    public string BigSmallLabel { get; init; } = "";
    public string PrimeLabel { get; init; } = "";
    public string ZO2Label { get; init; } = "";
    public int LinkCount { get; init; }

    public static LatestRecordView From(DrawRecord r) => new()
    {
        Period = r.Period,
        DrawDate = r.DrawDate,
        RedBalls = r.RedBalls.ToArray(),
        BlueBall = r.BlueBall,
        RedSum = r.RedSum,
        RedSpan = r.RedSpan,
        OddCount = r.OddCount,
        EvenCount = r.EvenCount,
        ZoneLabel = r.ZoneLabel,
        BigSmallLabel = r.BigSmallLabel,
        PrimeLabel = r.PrimeLabel,
        ZO2Label = r.ZO2Label,
        LinkCount = r.LinkCount
    };
}
