using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using SsqAnalyzer.Pages;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    internal static void VerifyKillWorkflowData()
    {
        WithTestDirectory(dir =>
        {
            var time = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
            var store = new KillSubmissionStore(Path.Combine(dir, "submissions.json"), () => time);
            var report = SubmissionReport(time); store.Submit(report, new[] { SubmissionSource() });
            var rule = report.RuleDefinitions[0].ToRule();
            Assert(KillActualPerformance.Summarize(rule, store.Read()).Single() is { Submitted: 1, Reviewed: 0, Triggered: 0 }, "pending actual submissions never count as successful triggers");
            time = time.AddHours(2); store.Settle(new[] { SubmissionSource(), SubmissionActual() });
            var actual = KillActualPerformance.Summarize(rule, store.Read()).Single();
            Assert(actual is { Current: true, Reviewed: 1, Triggered: 1, WrongPeriods: 1 } && actual.ErrorPeriods.Single() == 2026111, "actual errors trace exact issue");
            rule.Params["changed"] = 99;
            Assert(KillActualPerformance.Summarize(rule, store.Read()).Single().Current == false, "edited rule does not inherit old version performance");
            var failing = report.RuleDefinitions.Single(r => r.RuleId == "failed").ToRule();
            Assert(KillActualPerformance.Summarize(failing, store.Read()).Single() is { Failed: 1, Triggered: 0 }, "actual failures excluded from triggered denominator");
            Assert(KillSubmissionChecks.Describe(report, new[] { SubmissionSource() }, time).Contains("已过提交截止"), "expired submit precheck");
        });
    }
    private static void VerifyKillWorkflowUi()
    {
        var data = new FakeDataService(); data.SetRecords(SubmissionSource());
        KillRule Rule(string id, int ball) => new() { RuleId = id, Name = id, BallType = BallType.Red, Category = RuleCategory.Formula,
            JsCode = "function getKillBalls(ctx) { return [ctx.params.ball]; }", Params = new() { ["ball"] = ball } };
        var first = Rule("first", 1); var second = Rule("second", 32); var third = Rule("third", 1);
        var repo = new ListRuleRepository(first, second, third);
        var engine = new KillEngine(repo, new JintRuleExecutor(), new RuleContextBuilder(data));
        var page = new KillPage(data, repo, engine, DispatchProxy.Create<IBacktestEngine, OfflineDependencyProxy>(),
            new RuleContextBuilder(data), new KillSettings(), new GroupInputStore());
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        page.Width = 1200; page.Height = 800; page.Measure(new Size(1200, 800)); page.Arrange(new Rect(0, 0, 1200, 800)); page.UpdateLayout();
        var grid = (DataGrid)page.FindName("RulesGrid");
        List<RuleRowViewModel> Rows() => grid.Items.Cast<RuleRowViewModel>().ToList();
        void Execute() => typeof(KillPage).GetMethod("RunKill", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
        T Field<T>(string name) => (T)typeof(KillPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        Assert(grid.SelectionMode == DataGridSelectionMode.Single && grid.Columns.All(c => c.Header?.ToString() != "收藏"), "simple single-selection rule list");
        Execute();
        Assert(Rows().All(r => r.ExecutionLabel.StartsWith("杀 ")), "per-rule preview remains available");
        var balls = Field<WrapPanel>("_previewBalls");
        Assert(balls.Children.Count == 2 && balls.Children.OfType<Button>().Count() == 0, "preview numbers are static without linkage actions");
        grid.SelectedItem = Rows()[0];
        Assert(Rows().Count == 3 && first.IsEnabled && second.IsEnabled, "selection neither filters nor changes enablement");
        typeof(KillPage).GetMethod("EnabledCheckBox_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page,
            new object[] { new CheckBox { DataContext = Rows()[0], IsChecked = false }, new RoutedEventArgs() });
        PumpDispatcher();
        Assert(!Field<Button>("_openReport").IsEnabled && Rows().All(r => r.ExecutionLabel == "预览已过期"), "single-rule edits invalidate preview");
        Execute();
        page.UpdateLayout(); SaveKillPreview(page, "kill-workflow-preview.png", 1200, 800);
        data.NotifyDataUpdated(); PumpDispatcher();
        Assert(!Field<Button>("_openReport").IsEnabled, "draw changes invalidate preview");
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }
}