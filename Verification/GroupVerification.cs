using System.Collections;
using System.Reflection;
using System.Text.Json;
using SsqAnalyzer.Controls;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    private static void VerifyGroupRecommendationModule()
    {
        var data = new FakeDataService();
        var records = BuildRecords(120);
        data.SetRecords(records.ToArray());
        int targetIssue = records[^1].Period + 1;
    
        var ticketStore = new TicketStore { Period = targetIssue };
        ticketStore.AddTickets(new[]
        {
            new Ticket { Reds = new() { 1, 3, 8, 14, 22, 31, 33 }, Blues = new() { 3, 9 } },
            new Ticket { Reds = new() { 2, 6, 11, 15, 23, 29, 32 }, Blues = new() { 5, 12 } },
            new Ticket { Reds = new() { 3, 7, 14, 18, 24, 27, 31 }, Blues = new() { 3, 16 } }
        });
    
        var inputStore = new GroupInputStore();
        inputStore.SavePosition(Prediction(
            targetIssue,
            records[^1].Period,
            "group-position-v1",
            "group-position-run",
            "live",
            redPoints: new[] { 3, 8, 15, 22, 27, 31 }));
        inputStore.SaveKill(new KillReport
        {
            TargetPeriod = targetIssue,
            GeneratedAt = DateTime.UtcNow,
            EnabledRules = Array.Empty<IKillRule>(),
            Results = Array.Empty<KillResult>(),
            KilledRedBalls = Array.Empty<KilledBallDetail>(),
            KilledBlueBalls = Array.Empty<KilledBallDetail>(),
            RecommendedRedBalls = Enumerable.Range(1, 28).ToArray(),
            RecommendedBlueBalls = Enumerable.Range(1, 14).ToArray()
        });
    
        var service = new GroupService(data, ticketStore, inputStore);
        var inputs = service.GetInputs(targetIssue);
        Assert(inputs.ReadyCount == 4, "all four group sources ready");
        Assert(inputs.Trend?.Views.Count == 5, "five trend views");
        Assert(inputs.Trend?.Views.All(view => view.ChartRows.Count == view.SampleSize) == true,
            "each trend view carries its own chart rows");
        Assert(inputs.Trend?.Views.Single(view => view.View == TrendViewKind.Historical).ChartRows
            .All(record => record.Period % 1000 == targetIssue % 1000) == true,
            "historical preview uses matching issue suffix rows");
        Assert(inputs.Trend?.Views.Single(view => view.View == TrendViewKind.Parity).ChartRows
            .All(record => record.Period % 2 == targetIssue % 2) == true,
            "parity preview uses matching parity rows");
        var routeView = inputs.Trend!.Views.Single(view => view.View == TrendViewKind.Route012);
        var routeRows = routeView.ChartRows.TakeLast(20).Reverse().ToList();
        var routeRedColumns = routeRows
            .Select(record => record.RedBalls.Select(number => Array.IndexOf(MatrixGrid.RedOrderZO2, number)).ToList())
            .ToList();
        var routeChains = (IEnumerable)typeof(DiagonalChainRenderer)
            .GetMethod("FindChains", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { routeRedColumns, Enumerable.Range(0, routeRows.Count).ToList(), 0, 32, 3, 1 })!;
        var expectedRouteRed = routeChains.Cast<object>()
            .Select(chain => MatrixGrid.RedOrderZO2[(int)chain.GetType().GetProperty("PredCol")!.GetValue(chain)!])
            .Distinct().Order().ToArray();
        Assert(routeView.DiagonalRedCandidates.SequenceEqual(expectedRouteRed),
            "012 group diagonal candidates map visual columns back to ball numbers");
        Assert(inputs.TicketStats?.RedCounts.Count == 33 && inputs.TicketStats.BlueCounts.Count == 16,
            "complete ticket red and blue count arrays");
        Assert(inputs.Position?.RedPoints.SequenceEqual(new[] { 3, 8, 15, 22, 27, 31 }) == true,
            "group input uses exactly six red points");
        Assert(inputs.KillPool?.RemainingRedNumbers.Count == 28
            && inputs.KillPool.RemainingBlueNumbers.Count == 14,
            "group input uses surviving red and blue pools");
    
        var first = service.Generate(targetIssue, 5);
        var repeat = service.Generate(targetIssue, 5);
        Assert(JsonSerializer.Serialize(first) == JsonSerializer.Serialize(repeat),
            "group generation is deterministic");
        Assert(first.UsedSources.Count == 4 && first.Tickets.Count == 5,
            "group generation consumes every ready source");
        Assert(first.Tickets.Select(ticket => $"{string.Join(',', ticket.RedBalls)}+{ticket.BlueBall}").Distinct().Count() == 5,
            "group tickets are distinct");
        Assert(first.Tickets.All(ticket => ticket.RedBalls.Count == 6
            && ticket.RedBalls.Distinct().Count() == 6
            && ticket.RedBalls.All(number => number is >= 1 and <= 33)
            && ticket.BlueBall is >= 1 and <= 16),
            "group tickets satisfy SSQ legality");

        foreach (int count in new[] { 1, 5, 20 })
        {
            var generated = service.Generate(targetIssue, count);
            Assert(generated.Tickets.Count == count, "requested group size is preserved");
            Assert(generated.Tickets.All(ticket => ticket.RedBalls.SequenceEqual(ticket.RedBalls.Order())),
                "retained red candidates use ascending display order");
            Assert(JsonSerializer.Serialize(generated) == JsonSerializer.Serialize(service.Generate(targetIssue, count)),
                "scores, tie ordering and evidence remain deterministic");
        }
    
        var oneSourceService = new GroupService(data, new TicketStore(), new GroupInputStore());
        Assert(oneSourceService.GetInputs(targetIssue).ReadyCount == 1, "one-source fixture is valid");
        bool rejected = false;
        try
        {
            oneSourceService.Generate(targetIssue, 3);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("至少需要 2 项"))
        {
            rejected = true;
        }
        Assert(rejected, "fewer than two ready sources cannot generate tickets");
    }
}
