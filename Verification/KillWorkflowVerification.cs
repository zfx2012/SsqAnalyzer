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
            var preferences = new KillPagePreferenceStore(Path.Combine(dir, "preferences.json"));
            var saved = new KillPagePreferences { Window = 100, SortKey = "Name", SortDescending = true,
                Favorites = new() { "red-a" }, HiddenColumns = new() { "Description" }, ColumnWidths = new() { ["Name"] = 245 } };
            preferences.Save(saved); var restored = preferences.Read();
            Assert(restored.Window == 100 && restored.SortDescending && restored.Favorites.Contains("red-a") && restored.ColumnWidths["Name"] == 245 && restored.HiddenColumns.Contains("Description"), "preferences survive restart");
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
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        try
        {
        WithTestDirectory(dir =>
        {
            var data = new FakeDataService(); data.SetRecords(SubmissionSource());
            KillRule Rule(string id, int ball) => new() { RuleId = id, Name = id, BallType = BallType.Red, Category = RuleCategory.Formula,
                JsCode = "function getKillBalls(ctx) { return [ctx.params.ball]; }", Params = new() { ["ball"] = ball } };
            var first = Rule("first", 1); var second = Rule("second", 32); var third = Rule("third", 1);
            var repo = new ListRuleRepository(first, second, third);
            var engine = new KillEngine(repo, new JintRuleExecutor(), new RuleContextBuilder(data));
            var preferences = new KillPagePreferenceStore(Path.Combine(dir, "prefs.json"));
            var backtest = new WorkflowBacktest();
            KillPage Page() => new(data, repo, engine, backtest, new RuleContextBuilder(data), new KillSettings(), new GroupInputStore(), preferences: preferences);
            var page = Page(); page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            page.Width = 1200; page.Height = 850; page.Measure(new Size(1200, 850)); page.Arrange(new Rect(0, 0, 1200, 850)); page.UpdateLayout();
            var grid = (DataGrid)page.FindName("RulesGrid");
            List<RuleRowViewModel> Rows() => grid.Items.Cast<RuleRowViewModel>().ToList();
            void Call(string method, params object[] args) => typeof(KillPage).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);
            T Field<T>(string name) => (T)typeof(KillPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            grid.SelectedItems.Add(Rows()[0]); grid.SelectedItems.Add(Rows()[1]);
            Assert(first.IsEnabled && second.IsEnabled, "batch selection independent from rule enablement");
            Call("ToggleFavorites"); Assert(Rows().Count(r => r.IsFavorite) == 2, "favorite selection scope");
            Call("SetBatchEnabled", false); PumpDispatcher();
            Assert(!first.IsEnabled && !second.IsEnabled && third.IsEnabled && grid.SelectedItems.Count == 2, "batch enable persists only selected rows and preserves selection");
            Call("SetBatchEnabled", true); PumpDispatcher();
            Call("RunKill");
            Assert(Rows().All(r => r.ExecutionLabel.StartsWith("杀 ")), "preview computes visible per-rule outputs without opening a modal report");
            var balls = Field<WrapPanel>("_previewBalls");
            balls.Children.OfType<Button>().Single(b => b.Tag is KilledBallDetail { Ball: 1 }).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(Rows().Select(r => r.RuleId).SequenceEqual(new[] { "first", "third" }), "number click filters to all contributing rules");
            InvokeClick(page, "ResetList_Click");
            var firstRow = Rows().Single(r => r.RuleId == "first"); grid.SelectedItem = firstRow;
            Assert(Field<TextBlock>("_linkedInfo").Text.Contains("first"), "rule selection explains linked output");
            Call("SetBatchEnabled", false); PumpDispatcher();
            Assert(!Field<Button>("_openReport").IsEnabled && Rows().All(r => r.ExecutionLabel == "预览已过期"), "rule changes invalidate preview and disable report submission entry");
            Field<ComboBox>("_batchScope").SelectedIndex = 1;
            ((TextBox)page.FindName("RuleSearch")).Text = "second";
            Call("SetBatchEnabled", false); PumpDispatcher();
            Assert(!second.IsEnabled && third.IsEnabled, "filtered batch scope excludes unrelated rules");
            InvokeClick(page, "ResetList_Click");
            Field<ComboBox>("_batchScope").SelectedIndex = 1; Call("SetBatchEnabled", true); PumpDispatcher();
            Call("RunKill"); page.UpdateLayout(); SaveKillPreview(page, "kill-workflow-preview.png", 1200, 850);
            data.NotifyDataUpdated(); PumpDispatcher(); Assert(!Field<Button>("_openReport").IsEnabled, "draw update invalidates prior results");
            ((TextBox)page.FindName("RuleSearch")).Text = "second";
            var scopedTask = (Task)typeof(KillPage).GetMethod("RunScopedBacktest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
            PumpUntil(() => scopedTask.IsCompleted); scopedTask.GetAwaiter().GetResult();
            Assert(backtest.Calls.SequenceEqual(new[] { "second" }), "scoped backtest runs only captured filtered scope");
            InvokeClick(page, "ResetList_Click"); backtest.Block = true;
            scopedTask = (Task)typeof(KillPage).GetMethod("RunScopedBacktest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
            PumpUntil(() => backtest.Started.IsSet);
            Field<CancellationTokenSource>("_backtestCts").Cancel(); backtest.Release.Set();
            PumpUntil(() => scopedTask.IsCompleted); scopedTask.GetAwaiter().GetResult();
            Assert(backtest.Calls.Count == 1 && Field<TextBlock>("_workflowMessage").Text.Contains("已取消"), "cancellation stops remaining scoped rules");
            var nameColumn = grid.Columns.Single(c => c.SortMemberPath == "Name"); nameColumn.Width = 245;
            Call("RulesGrid_Sorting", grid, new DataGridSortingEventArgs(nameColumn));
            grid.Columns.Single(c => c.SortMemberPath == "Description").Visibility = Visibility.Collapsed;
            ((Button)page.FindName("Btn30")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            var reopened = Page(); reopened.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var reopenedGrid = (DataGrid)reopened.FindName("RulesGrid");
            Assert(reopenedGrid.Items.Cast<RuleRowViewModel>().Count(r => r.IsFavorite) == 2
                && reopenedGrid.Columns.Single(c => c.SortMemberPath == "Name").SortDirection == System.ComponentModel.ListSortDirection.Ascending
                && reopenedGrid.Columns.Single(c => c.SortMemberPath == "Description").Visibility == Visibility.Collapsed, "favorites, sorting and hidden columns restore in new page");
            reopened.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        });
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private sealed class WorkflowBacktest : IBacktestEngine
    {
        internal readonly List<string> Calls = new();
        internal bool Block;
        internal readonly ManualResetEventSlim Started = new(false), Release = new(false);
        public BacktestStat Run(IKillRule rule, BacktestWindow window, CancellationToken ct = default)
        {
            if (Block) { Started.Set(); Release.Wait(ct); }
            ct.ThrowIfCancellationRequested(); Calls.Add(rule.RuleId);
            return new BacktestStat { RuleId = rule.RuleId, Window = window, TriggeredCount = 0, KillBallCount = 0, CorrectBallCount = 0 };
        }
        public Task RunAllAsync(BacktestWindow window, IProgress<(int done, int total)>? progress = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task BacktestAll(int targetN = 100, IProgress<(int done, int total)>? progress = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
