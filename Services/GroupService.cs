using System.Security.Cryptography;
using System.Text;
using SsqAnalyzer.Controls;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

/// <summary>汇总四类组号输入并生成可复现的基础单式组合。</summary>
public sealed class GroupService
{
    public const string RuleVersion = "draft-v1";
    public const int MinimumReadySources = 2;

    private readonly IDataService _data;
    private readonly ITicketRepository _tickets;
    private readonly GroupInputStore _snapshots;

    public GroupService(IDataService data, ITicketRepository tickets, GroupInputStore snapshots)
    {
        _data = data;
        _tickets = tickets;
        _snapshots = snapshots;
    }

    public GroupInputBundle GetInputs(int targetIssue)
    {
        if (targetIssue <= 0) throw new ArgumentOutOfRangeException(nameof(targetIssue));

        var all = _data.GetAllRecords().OrderBy(record => record.Period).ToList();
        var trend = BuildTrend(targetIssue, all);
        var ticketStats = BuildTicketStats(targetIssue);
        var position = _snapshots.GetPosition(targetIssue);
        var killPool = _snapshots.GetKillPool(targetIssue);
        var latestPosition = position ?? _snapshots.GetLatestPosition();
        var latestKill = killPool ?? _snapshots.GetLatestKillPool();

        var statuses = new[]
        {
            trend is null
                ? new GroupSourceStatus(GroupSourceKind.Trend, "走势图", GroupSourceState.Missing, "暂无历史数据")
                : new GroupSourceStatus(GroupSourceKind.Trend, "走势图", GroupSourceState.Ready,
                    $"{trend.Views.Count} 个视图 · 截至 {trend.AsOfIssue}", trend.TargetIssue),
            TicketStatus(targetIssue, ticketStats),
            position is not null
                ? new GroupSourceStatus(GroupSourceKind.Position, "6 个点位", GroupSourceState.Ready,
                    string.Join(" ", position.RedPoints.Select(Number)), position.TargetIssue)
                : latestPosition is not null
                    ? new GroupSourceStatus(GroupSourceKind.Position, "6 个点位", GroupSourceState.Stale,
                        $"仅有 {latestPosition.TargetIssue} 期数据", latestPosition.TargetIssue)
                    : new GroupSourceStatus(GroupSourceKind.Position, "6 个点位", GroupSourceState.Missing, "本期尚未生成"),
            killPool is not null
                ? new GroupSourceStatus(GroupSourceKind.KillPool, "杀号大底", GroupSourceState.Ready,
                    $"红 {killPool.RemainingRedNumbers.Count} · 蓝 {killPool.RemainingBlueNumbers.Count}", killPool.TargetIssue)
                : latestKill is not null
                    ? new GroupSourceStatus(GroupSourceKind.KillPool, "杀号大底", GroupSourceState.Stale,
                        $"仅有 {latestKill.TargetIssue} 期数据", latestKill.TargetIssue)
                    : new GroupSourceStatus(GroupSourceKind.KillPool, "杀号大底", GroupSourceState.Missing, "本期尚未生成")
        };

        return new GroupInputBundle(targetIssue, trend, ticketStats, position, killPool, statuses);
    }

    public GroupGenerationResult Generate(int targetIssue, int ticketCount)
    {
        if (ticketCount is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(ticketCount), "注数必须在 1～20 之间");

        var input = GetInputs(targetIssue);
        if (input.ReadyCount < MinimumReadySources)
            throw new InvalidOperationException($"至少需要 {MinimumReadySources} 项可用数据才能组号，当前仅有 {input.ReadyCount} 项");

        var redScores = new double[34];
        var blueScores = new double[17];
        var used = new List<string>();

        if (input.Trend is not null && input.Status(GroupSourceKind.Trend).IsReady)
        {
            used.Add("走势");
            foreach (var view in input.Trend.Views)
            {
                Add(redScores, view.ColdRedNumbers, 1);
                Add(redScores, view.DiagonalRedCandidates, 2.5);
                Add(blueScores, view.ColdBlueNumbers, 1);
                Add(blueScores, view.DiagonalBlueCandidates, 2.5);
            }
        }

        if (input.TicketStats is not null && input.Status(GroupSourceKind.TicketStats).IsReady)
        {
            used.Add("复式票统计");
            AddInverseCounts(redScores, input.TicketStats.RedCounts);
            AddInverseCounts(blueScores, input.TicketStats.BlueCounts);
        }

        if (input.Position is not null && input.Status(GroupSourceKind.Position).IsReady)
        {
            used.Add("点位");
            Add(redScores, input.Position.RedPoints, 3);
        }

        var redPool = Enumerable.Range(1, 33).ToList();
        var bluePool = Enumerable.Range(1, 16).ToList();
        if (input.KillPool is not null && input.Status(GroupSourceKind.KillPool).IsReady)
        {
            used.Add("杀号大底");
            redPool = input.KillPool.RemainingRedNumbers.Distinct().OrderBy(number => number).ToList();
            bluePool = input.KillPool.RemainingBlueNumbers.Distinct().OrderBy(number => number).ToList();
        }

        if (redPool.Count < 6) throw new InvalidOperationException("杀号后剩余红球不足 6 个，无法生成合法组合");
        if (bluePool.Count == 0) throw new InvalidOperationException("杀号后没有剩余蓝球，无法生成合法组合");

        var rankedReds = redPool
            .OrderByDescending(number => redScores[number])
            .ThenBy(number => StableOrder(targetIssue, number, 17))
            .Take(Math.Min(16, redPool.Count))
            .ToArray();
        var rankedBlues = bluePool
            .OrderByDescending(number => blueScores[number])
            .ThenBy(number => StableOrder(targetIssue, number, 53))
            .ToArray();

        var redCandidates = Combinations(rankedReds, 6)
            .Select(numbers => new
            {
                Numbers = numbers,
                Score = numbers.Sum(number => redScores[number]),
                Tie = StableOrder(targetIssue, numbers.Aggregate(0, (value, number) => value * 37 + number), 97)
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Tie)
            .Take(Math.Max(ticketCount * 4, ticketCount))
            // Scores and ties use ranked order; canonical display order is only
            // needed for the retained candidates, not every six-ball combination.
            .Select(candidate => new
            {
                Numbers = candidate.Numbers.OrderBy(number => number).ToArray(),
                candidate.Score,
                candidate.Tie
            })
            .ToList();

        var coldReds = input.Trend?.Views.SelectMany(view => view.ColdRedNumbers).Distinct().ToArray()
            ?? Array.Empty<int>();
        var diagonalReds = input.Trend?.Views.SelectMany(view => view.DiagonalRedCandidates).Distinct().ToArray()
            ?? Array.Empty<int>();
        var results = redCandidates
            .SelectMany(red => rankedBlues.Select(blue => new
            {
                red.Numbers,
                Blue = blue,
                Score = red.Score + blueScores[blue],
                Tie = StableOrder(targetIssue, red.Tie ^ blue, 131)
            }))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Tie)
            .Take(ticketCount)
            .Select((candidate, index) => new GroupGeneratedTicket(
                index + 1,
                candidate.Numbers,
                candidate.Blue,
                Math.Round(candidate.Score, 2),
                BuildEvidence(candidate.Numbers, candidate.Blue, input, coldReds, diagonalReds)))
            .ToList();

        var skipped = input.Statuses.Where(status => !status.IsReady)
            .Select(status => $"{status.Name}（{StateText(status.State)}）")
            .ToList();
        string inputId = StableId(string.Join("|", new[]
        {
            targetIssue.ToString(), RuleVersion,
            input.Trend?.SnapshotId ?? "-", input.TicketStats?.SnapshotId ?? "-",
            input.Position?.SnapshotId ?? "-", input.KillPool?.SnapshotId ?? "-"
        }));

        return new GroupGenerationResult(targetIssue, RuleVersion, inputId, used, skipped, results);
    }

    private GroupSourceStatus TicketStatus(int targetIssue, TicketStatsSnapshot? snapshot)
    {
        if (_tickets.CurrentType != LotteryType.SSQ)
            return new GroupSourceStatus(GroupSourceKind.TicketStats, "复式票统计", GroupSourceState.Invalid, "当前数据不是双色球", _tickets.Period);
        if (_tickets.Tickets.Count == 0)
            return new GroupSourceStatus(GroupSourceKind.TicketStats, "复式票统计", GroupSourceState.Missing, "本期尚未载入");
        if (_tickets.Period != targetIssue)
            return new GroupSourceStatus(GroupSourceKind.TicketStats, "复式票统计", GroupSourceState.Stale,
                $"当前为 {_tickets.Period?.ToString() ?? "未知"} 期", _tickets.Period);
        return new GroupSourceStatus(GroupSourceKind.TicketStats, "复式票统计", GroupSourceState.Ready,
            $"{snapshot!.TicketCount} 张票 · 红 33 / 蓝 16", targetIssue);
    }

    private TicketStatsSnapshot? BuildTicketStats(int targetIssue)
    {
        if (_tickets.CurrentType != LotteryType.SSQ || _tickets.Tickets.Count == 0) return null;
        var reds = new int[33];
        var blues = new int[16];
        foreach (var ticket in _tickets.Tickets)
        {
            foreach (int number in ticket.Reds.Where(number => number is >= 1 and <= 33)) reds[number - 1]++;
            foreach (int number in ticket.Blues.Where(number => number is >= 1 and <= 16)) blues[number - 1]++;
        }
        string id = StableId($"{_tickets.Period}|{string.Join(',', reds)}|{string.Join(',', blues)}");
        return new TicketStatsSnapshot(_tickets.Period ?? targetIssue, _tickets.Tickets.Count, id, reds, blues);
    }

    private static TrendGroupSnapshot? BuildTrend(int targetIssue, List<DrawRecord> all)
    {
        var available = all.Where(record => record.Period < targetIssue).ToList();
        if (available.Count == 0) return null;
        int asOfIssue = available[^1].Period;
        DayOfWeek cycleDay = all.FirstOrDefault(record => record.Period == targetIssue)?.DrawDate.DayOfWeek
            ?? NextDrawDate(available[^1].DrawDate).DayOfWeek;
        var recent = available.TakeLast(100).ToList();
        var views = new[]
        {
            BuildView(TrendViewKind.Basic, "基本走势", recent, "最近 100 期"),
            BuildView(TrendViewKind.Historical, "历史同期",
                available.Where(record => record.Period % 1000 == targetIssue % 1000).ToList(), $"期号后 3 位 {targetIssue % 1000:D3}"),
            BuildView(TrendViewKind.Route012, "012 路", recent, "最近 100 期 · 012 排列"),
            BuildView(TrendViewKind.Parity, "奇偶图",
                available.Where(record => record.Period % 2 == targetIssue % 2).TakeLast(100).ToList(), targetIssue % 2 == 0 ? "偶数期" : "奇数期"),
            BuildView(TrendViewKind.Cycle, "周期图",
                available.Where(record => record.DrawDate.DayOfWeek == cycleDay).TakeLast(100).ToList(), DayName(cycleDay))
        };
        string id = StableId($"{targetIssue}|{asOfIssue}|" + string.Join("|", views.Select(view =>
            $"{string.Join(',', view.ColdRedNumbers)}:{string.Join(',', view.DiagonalRedCandidates)}")));
        return new TrendGroupSnapshot(targetIssue, asOfIssue, id, available.TakeLast(5).ToList(), views);
    }

    private static TrendViewSnapshot BuildView(TrendViewKind kind, string name, List<DrawRecord> rows, string filter)
    {
        var coldReds = ColdNumbers(rows, 33, 10, record => record.RedBalls);
        var coldBlues = ColdNumbers(rows, 16, 16, record => new[] { record.BlueBall });
        var recentRows = rows.TakeLast(20).Reverse<DrawRecord>().ToList();
        var rowIndexes = Enumerable.Range(0, recentRows.Count).ToList();
        var redOrder = kind == TrendViewKind.Route012 ? MatrixGrid.RedOrderZO2 : MatrixGrid.RedOrderNormal;
        var blueOrder = kind == TrendViewKind.Route012 ? MatrixGrid.BlueOrderZO2 : MatrixGrid.BlueOrderNormal;
        var reds = recentRows.Select(record => record.RedBalls.Select(number => Array.IndexOf(redOrder, number)).ToList()).ToList();
        var blues = recentRows.Select(record => new List<int> { 33 + Array.IndexOf(blueOrder, record.BlueBall) }).ToList();
        var redDiagonal = new HashSet<int>();
        var blueDiagonal = new HashSet<int>();
        const int levelOneGap = 1;
        foreach (var chain in DiagonalChainRenderer.FindChains(reds, rowIndexes, 0, 32, 3, levelOneGap))
            redDiagonal.Add(redOrder[chain.PredCol]);
        foreach (var chain in DiagonalChainRenderer.FindChains(blues, rowIndexes, 33, 48, 3, levelOneGap))
            blueDiagonal.Add(blueOrder[chain.PredCol - 33]);
        return new TrendViewSnapshot(kind, name, coldReds, coldBlues,
            redDiagonal.OrderBy(number => number).ToArray(),
            blueDiagonal.OrderBy(number => number).ToArray(), rows.Count, filter, rows.ToArray());
    }

    private static IReadOnlyList<int> ColdNumbers(
        IReadOnlyList<DrawRecord> rows, int maximum, int threshold,
        Func<DrawRecord, IEnumerable<int>> selector)
    {
        var result = new List<int>();
        for (int number = 1; number <= maximum; number++)
        {
            int last = -1;
            for (int index = rows.Count - 1; index >= 0; index--)
            {
                if (!selector(rows[index]).Contains(number)) continue;
                last = index;
                break;
            }
            int omission = last < 0 ? rows.Count : rows.Count - 1 - last;
            if (omission >= threshold) result.Add(number);
        }
        return result;
    }

    private static IEnumerable<int[]> Combinations(int[] source, int count)
    {
        var buffer = new int[count];
        return Walk(0, 0);

        IEnumerable<int[]> Walk(int sourceIndex, int depth)
        {
            if (depth == count)
            {
                yield return buffer.ToArray();
                yield break;
            }
            for (int index = sourceIndex; index <= source.Length - (count - depth); index++)
            {
                buffer[depth] = source[index];
                foreach (var item in Walk(index + 1, depth + 1)) yield return item;
            }
        }
    }

    private static void Add(double[] scores, IEnumerable<int> numbers, double value)
    {
        foreach (int number in numbers.Where(number => number > 0 && number < scores.Length)) scores[number] += value;
    }

    private static void AddInverseCounts(double[] scores, IReadOnlyList<int> counts)
    {
        int maximum = counts.Count == 0 ? 0 : counts.Max();
        if (maximum == 0) return;
        for (int index = 0; index < counts.Count && index + 1 < scores.Length; index++)
            scores[index + 1] += maximum - counts[index];
    }

    private static string BuildEvidence(IReadOnlyList<int> reds, int blue, GroupInputBundle input,
        IReadOnlyList<int> coldReds, IReadOnlyList<int> diagonalReds)
    {
        var evidence = new List<string>();
        if (input.Trend is not null)
        {
            int cold = coldReds.Count(reds.Contains);
            int diagonal = diagonalReds.Count(reds.Contains);
            evidence.Add($"走势冷号 {cold} · 斜连 {diagonal}");
        }
        if (input.TicketStats is not null && input.Status(GroupSourceKind.TicketStats).IsReady)
        {
            int redTotal = reds.Sum(number => input.TicketStats.RedCounts[number - 1]);
            int blueCount = input.TicketStats.BlueCounts[blue - 1];
            evidence.Add($"复式统计 红合计 {redTotal} · 蓝 {blueCount}");
        }
        if (input.Position is not null)
            evidence.Add($"点位 {reds.Count(input.Position.RedPoints.Contains)}/6");
        if (input.KillPool is not null)
            evidence.Add("位于杀号剩余大底");
        return evidence.Count == 0 ? "仅使用基础合法性规则" : string.Join("；", evidence);
    }

    private static int StableOrder(int issue, int value, int salt)
    {
        unchecked
        {
            uint x = (uint)(issue * 397 ^ value * salt);
            x ^= x >> 16;
            x *= 0x7FEB352D;
            x ^= x >> 15;
            return (int)(x & 0x7FFFFFFF);
        }
    }

    private static DateTime NextDrawDate(DateTime date)
    {
        do date = date.AddDays(1);
        while (date.DayOfWeek is not (DayOfWeek.Tuesday or DayOfWeek.Thursday or DayOfWeek.Sunday));
        return date;
    }

    private static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Sunday => "周日",
        _ => day.ToString()
    };

    private static string StateText(GroupSourceState state) => state switch
    {
        GroupSourceState.Missing => "未生成",
        GroupSourceState.Stale => "已过期",
        GroupSourceState.Invalid => "异常",
        _ => "可用"
    };

    private static string Number(int number) => number.ToString("D2");
    private static string StableId(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..12];
}
