using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SsqAnalyzer.Models;
using SsqAnalyzer.Pages;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    private static DrawRecord SubmissionSource() => new() { Period = 2026110, DrawDate = new DateTime(2026, 9, 22), RedBalls = new[] { 1, 2, 3, 4, 5, 6 }, BlueBall = 1 };
    private static DrawRecord SubmissionActual() => new() { Period = 2026111, DrawDate = new DateTime(2026, 9, 24), RedBalls = new[] { 1, 2, 3, 4, 5, 32 }, BlueBall = 8 };
    private static KillReport SubmissionReport(DateTime time)
    {
        var definitions = new[] { ("red-a", BallType.Red, new[] { 1, 33 }), ("red-b", BallType.Red, new[] { 1, 32 }),
            ("blue", BallType.Blue, new[] { 8 }), ("idle", BallType.Red, Array.Empty<int>()), ("failed", BallType.Red, Array.Empty<int>()) };
        var rules = definitions.Select(d => new KillRule { RuleId = d.Item1, Name = "测试规则 " + d.Item1, BallType = d.Item2,
            Category = RuleCategory.Formula, JsCode = "function getKillBalls(ctx) { return [33]; }" }).ToArray();
        return new KillReport
        {
            TargetPeriod = 2026111, TargetDate = new DateTime(2026, 9, 24), SourceThroughPeriod = 2026110,
            SourceDataHash = KillRuleDefinition.DataHash(new[] { SubmissionSource() }), GeneratedAt = time,
            RuleDefinitions = rules.Select(KillRuleDefinition.Capture).ToArray(), EnabledRules = rules,
            Results = definitions.Select(d => new KillResult { RuleId = d.Item1, BallType = d.Item2, KilledBalls = d.Item3,
                Reason = "提交时的条件", ExecutionError = d.Item1 == "failed" ? "fixture error" : null }).ToArray(),
            KilledRedBalls = new[] { 1, 32, 33 }.Select(n => new KilledBallDetail { Ball = n, BallType = BallType.Red }).ToArray(),
            KilledBlueBalls = new[] { new KilledBallDetail { Ball = 8, BallType = BallType.Blue } },
            RecommendedRedBalls = Enumerable.Range(1, 33).Except(new[] { 1, 32, 33 }).ToArray(),
            RecommendedBlueBalls = Enumerable.Range(1, 16).Except(new[] { 8 }).ToArray()
        };
    }
    internal static void VerifyKillSubmissions()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "submission-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        DateTime now = new(2026, 9, 24, 12, 59, 59, DateTimeKind.Utc);
        var report = SubmissionReport(now.AddMinutes(-1));
        var source = new[] { SubmissionSource() };
        var actual = source.Append(SubmissionActual()).ToArray();
        string path = Path.Combine(dir, "ledger.json");
        var store = new KillSubmissionStore(path, () => now);
        Assert(store.Submit(report, source), "first submission created");
        Assert(!store.Submit(report, source) && store.Read().Count == 1, "repeated submission keeps one record");
        Assert(new KillSubmissionStore(path, () => now).Read().Count == 1, "restart preserves submission");
        ((KillRule)report.EnabledRules[0]).Params["changed"] = 12;
        ((int[])report.Results[0].KilledBalls)[0] = 20;
        Assert(store.Read()[0].Submission.Rules[0].KilledBalls[0] == 1, "snapshot detached from report mutation");
        Assert(!store.Read()[0].Submission.Rules[0].Definition.Parameters.Contains("changed"), "rule definition detached");
        Assert(store.Settle(source).Added == 0 && store.Settle(actual).Added == 0 && store.Read()[0].Review is null, "pending and not-yet-drawn results not scored");
        now = now.AddSeconds(1);
        ExpectFailure<InvalidOperationException>(() => new KillSubmissionStore(Path.Combine(dir, "cutoff.json"), () => now).Submit(SubmissionReport(now), source));
        now = now.AddMinutes(15);
        Assert(store.Settle(actual).Added == 1 && store.Settle(actual).Added == 0, "draw time settles exactly once");
        var entry = store.Read().Single();
        Assert(KillSubmissionStore.WrongBalls(entry, BallType.Red).SequenceEqual(new[] { 1, 32 }) && KillSubmissionStore.WrongBalls(entry, BallType.Blue).SequenceEqual(new[] { 8 }), "union deduplicates shared wrong balls");
        string detail = KillSubmissionStore.ReviewText(entry);
        Assert(detail.Contains("红球 4/6") && detail.Contains("red-a") && detail.Contains("red-b") && detail.Contains("未触发（不计成功）") && detail.Contains("执行异常：fixture error（不计成功）"), "review attributes errors and distinguishes skipped rules");
        Assert(entry.Submission.Rules[0].KilledBalls.SequenceEqual(new[] { 1, 33 }), "review uses frozen outputs, not executable code returning 33");
        string originalBytes = File.ReadAllText(path);
        actual[^1].BlueBall = 9;
        Assert(store.Settle(actual).Issues.Count == 1 && File.ReadAllText(path) == originalBytes, "corrected actual never overwrites review");
        var changedSource = SubmissionSource(); changedSource.BlueBall = 2;
        Assert(store.Settle(new[] { changedSource, SubmissionActual() }).Issues.Count == 1 && File.ReadAllText(path) == originalBytes, "changed source warns without overwrite");
        now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        ExpectFailure<InvalidOperationException>(() => new KillSubmissionStore(Path.Combine(dir, "stale.json"), () => now).Submit(SubmissionReport(now), new[] { changedSource }));
        ExpectFailure<InvalidOperationException>(() => new KillSubmissionStore(Path.Combine(dir, "drawn.json"), () => now).Submit(SubmissionReport(now), actual));
        var badSummary = SubmissionReport(now); ((int[])badSummary.RecommendedRedBalls)[0] = 1;
        ExpectFailure<InvalidOperationException>(() => new KillSubmissionStore(Path.Combine(dir, "summary.json"), () => now).Submit(badSummary, source));
        File.WriteAllText(path, originalBytes.Replace("fixture error", "changed error"));
        string corrupt = File.ReadAllText(path);
        ExpectFailure<InvalidDataException>(() => store.Read());
        ExpectFailure<InvalidDataException>(() => store.Submit(SubmissionReport(now), source));
        Assert(File.ReadAllText(path) == corrupt, "corrupt ledger never reset");
        Assert(KillDrawSchedule.NextPeriod(2026153, new DateTime(2026, 12, 31)) == 2027001, "year rollover target period");

        // Real engine output, including execution failures, can be submitted with no rerun.
        var data = new FakeDataService(); data.SetRecords(SubmissionSource());
        var liveRule = new KillRule { RuleId = "live", Name = "live", BallType = BallType.Red, Category = RuleCategory.Formula,
            JsCode = "function getKillBalls(ctx) { return [ctx.params.ball]; }", Params = new() { ["ball"] = 33 } };
        var failingRule = new KillRule { RuleId = "broken", Name = "broken", BallType = BallType.Blue, Category = RuleCategory.Formula,
            JsCode = "function getKillBalls(ctx) { throw new Error('broken'); }" };
        var engine = new KillEngine(new ListRuleRepository(liveRule, failingRule), new JintRuleExecutor(), new RuleContextBuilder(data));
        var generated = engine.Execute(new[] { liveRule, failingRule });
        Assert(generated.Results[0].KilledBalls.Single() == 33 && generated.Results[1].ExecutionError is not null, "engine executes frozen params and records errors");
        liveRule.Params["ball"] = 1;
        Assert(generated.RuleDefinitions[0].ToRule().Params["ball"].ToString() == "33", "engine captures params at generation");
        Console.WriteLine("PASS submission snapshots, cutoff, duplicate prevention, immutable review and corruption checks");
    }

    private static void VerifyKillSubmissionWindows()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "submission-ui-" + Guid.NewGuid().ToString("N"));
        DateTime now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var store = new KillSubmissionStore(Path.Combine(dir, "ledger.json"), () => now);
        var data = new FakeDataService(); data.SetRecords(SubmissionSource());
        var coordinator = new KillReviewCoordinator(data, store);
        var report = new KillReportWindow(SubmissionReport(now), data, store, coordinator);
        report.Show(); PumpDispatcher();
        var submit = Descendants<Button>(report).Single(b => Equals(b.Content, "提交本期报告"));
        submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => Equals(submit.Content, "该期已提交"));
        Assert(store.Read().Count == 1 && !submit.IsEnabled, "report button saves exactly once");
        PumpDispatcher(); report.UpdateLayout(); SaveKillPreview(report, "submission-report.png", 1100, 860); report.Close();
        coordinator.Start(); coordinator.Start();
        Assert(data.SubscriberCount == 1, "coordinator startup subscribes once");
        now = now.AddHours(2); data.SetRecords(SubmissionSource(), SubmissionActual()); data.NotifyDataUpdated();
        PumpUntil(() => store.Read().Single().Review is not null);
        var history = new KillSubmissionHistoryWindow(data, store, coordinator);
        history.Show();
        PumpUntil(() => Descendants<TextBlock>(history).Any(t => t.Text.Contains("错杀红球：01 32")));
        Assert(Descendants<DataGrid>(history).Single().Items.Count == 1, "history displays persisted submission");
        PumpDispatcher(); history.UpdateLayout(); SaveKillPreview(history, "submission-review.png", 1100, 860);
        history.Width = 760; history.Height = 680; PumpDispatcher(); history.UpdateLayout();
        SaveKillPreview(history, "submission-review-narrow.png", 760, 680);
        var reviewScroll = Descendants<ScrollViewer>(history).First(s => s.Content is StackPanel);
        reviewScroll.ScrollToEnd(); PumpDispatcher(); history.UpdateLayout();
        SaveKillPreview(history, "submission-review-rules.png", 760, 680);
        history.Close(); var refresh = coordinator.RefreshAsync(); PumpUntil(() => refresh.IsCompleted); refresh.GetAwaiter().GetResult(); PumpDispatcher();
        // Closing history unsubscribes from coordinator; refresh remains safe after closure.
        static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T match) yield return match;
                foreach (var descendant in Descendants<T>(child)) yield return descendant;
            }
        }
    }
}
