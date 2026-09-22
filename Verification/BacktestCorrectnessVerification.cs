using System.IO;
using System.Reflection;
using System.Text.Json;
using SsqAnalyzer;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    internal static void VerifyBacktestCorrectness()
    {
        var data = new FakeDataService();
        var records = Enumerable.Range(0, 121).Select(i => new DrawRecord
        {
            Period = 2026001 + i, DrawDate = new DateTime(2026, 1, 1).AddDays(i),
            RedBalls = new[] { 1, 2, 3, 4, 5, 6 }, BlueBall = 1
        }).ToArray();
        data.SetRecords(records);
        var builder = new RuleContextBuilder(data);
        KillRule Rule(string id) => new() { RuleId = id, Name = id, BallType = BallType.Red, Category = RuleCategory.Formula, JsCode = "" };
        var rule = Rule("split-windows");
        var repo = new CommitObservingRepository(rule);
        BacktestEngine Engine(IRuleExecutor executor) => new(executor, builder, repo, data);
        var accurate = new DiagnosticExecutor(_ => new[] { 33 });
        var inaccurate = new DiagnosticExecutor(_ => new[] { 1 });
        Engine(accurate).Run(rule, BacktestWindow.Last50Triggers);
        var fifty = rule.BacktestStats!.Window50;
        Engine(inaccurate).Run(rule, BacktestWindow.Last100Triggers);
        var hundred = rule.BacktestStats!.Window100;
        Assert(!rule.IsEnabled && repo.EnabledAtSave == false, "gate uses just-completed window and saves disabled state with stats");
        Assert(rule.BacktestStats.Effective == hundred, "old favourable 50-trigger window cannot override recent 100-trigger result");
        var engine = new KillEngine(repo, inaccurate, builder);
        Assert(engine.Execute(new[] { rule }).KilledRedBalls.Single().Confidence == ConfidenceLevel.Low, "default report uses latest window");
        Assert(engine.Execute(new[] { rule }, BacktestWindow.Last50Triggers).KilledRedBalls.Single().Confidence == ConfidenceLevel.Medium,
            "explicit report window uses exactly the requested statistics");
        Assert(engine.Execute(new[] { rule }, BacktestWindow.All).KilledRedBalls.Single().Confidence == ConfidenceLevel.Low,
            "unmeasured all-history does not borrow 100 or 50 statistics");
        Engine(accurate).Run(rule, BacktestWindow.All);
        Assert(rule.BacktestStats!.WindowAll.TriggeredCount == 120 && rule.BacktestStats.Window100 == hundred
            && rule.BacktestStats.Window50 == fifty, "all-history has independent storage");
        Engine(accurate).Run(rule, BacktestWindow.Last100Triggers);
        Assert(rule.BacktestStats!.WindowAll.TriggeredCount == 120, "100-trigger rerun preserves all-history");

        foreach (int count in new[] { 0, 1, 29, 30 })
        {
            data.SetRecords(records.Take(count + 1).ToArray());
            var result = Engine(accurate).Run(rule, BacktestWindow.All);
            Assert(result.SampleInsufficient == (count < 30), $"all-history minimum samples: {count}");
            Assert(result.EvaluatedCount == count && result.TriggeredCount == count, "coverage includes attempted periods");
        }
        data.SetRecords(records);
        var noTrigger = Engine(new DiagnosticExecutor(_ => Array.Empty<int>())).Run(rule, BacktestWindow.All);
        Assert(noTrigger.SampleInsufficient && noTrigger.EvaluatedCount == 120 && noTrigger.FailureCount == 0, "no-trigger differs from failure");
        var failed = Engine(new DiagnosticExecutor(_ => throw new RuleExecutionException(rule.RuleId, "fixture failure"))).Run(rule, BacktestWindow.All);
        Assert(failed.FailureCount == 120 && failed.TriggeredCount == 0 && failed.LastExecutionError == "fixture failure",
            "all failures are recorded rather than silently treated as non-triggers");
        Assert(failed.FirstPeriod == 2026002 && failed.LastPeriod == 2026121, "coverage records predicted issue range");
        var partial = Engine(new DiagnosticExecutor(ctx => ctx.CurrentIndex == 120
            ? throw new RuleExecutionException(rule.RuleId, "partial failure") : new[] { 33 })).Run(rule, BacktestWindow.Last30Triggers);
        Assert(partial.TriggeredCount == 30 && partial.EvaluatedCount == 31 && partial.FailureCount == 1
            && !rule.BacktestStats!.Effective.IsUsable, "partial failures invalidate gate despite sufficient triggers");
        Assert(engine.Execute(new[] { rule }).KilledRedBalls.Single().Confidence == ConfidenceLevel.Low, "failed latest run cannot use old successful run");
        var previous = rule.BacktestStats;
        rule.IsEnabled = true;
        repo.FailSave = true;
        bool saveFailed = false;
        try { Engine(inaccurate).Run(rule, BacktestWindow.Last30Triggers); }
        catch (IOException) { saveFailed = true; }
        Assert(saveFailed && ReferenceEquals(previous, rule.BacktestStats) && rule.IsEnabled, "failed commit restores in-memory statistics and enablement");
        repo.FailSave = false;
        rule.ForceEnabled = true;
        Engine(inaccurate).Run(rule, BacktestWindow.Last30Triggers);
        Assert(rule.IsEnabled && repo.EnabledAtSave == true, "force-enabled override remains explicit");

        // Real repository round trip in the verification executable's isolated directory.
        string path = Path.Combine(AppPaths.DataDirectory, "kill_rules_user.json");
        byte[]? backup = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var builtinBackup = BuiltinRules.LoadAll().Select(r => (Rule: r, r.IsEnabled, r.ForceEnabled, r.BacktestStats)).ToArray();
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(path, "{\"userRules\":[],\"stats\":{},\"builtinOverrides\":{}}");
            var diskRepo = new RuleRepository();
            var builtin = diskRepo.GetAll().OfType<KillRule>().First(r => r.IsBuiltin);
            builtin.IsEnabled = true;
            builtin.ForceEnabled = false;
            new BacktestEngine(inaccurate, builder, diskRepo, data).Run(builtin, BacktestWindow.Last100Triggers);
            Assert(!new RuleRepository().Find(builtin.RuleId)!.IsEnabled, "builtin automatic disable also survives restart");
            var persisted = Rule("persisted-backtest-fixture");
            diskRepo.Add(persisted);
            new BacktestEngine(inaccurate, builder, diskRepo, data).Run(persisted, BacktestWindow.Last100Triggers);
            var restored = new RuleRepository().Find(persisted.RuleId)!;
            Assert(!restored.IsEnabled && restored.BacktestStats!.LastWindow == BacktestWindow.Last100Triggers,
                "restart preserves automatic disable and latest window");
            Assert(restored.BacktestStats!.Window100.FirstPeriod == 2026022 && restored.BacktestStats.Window100.LastPeriod == 2026121
                && restored.BacktestStats.Window100.RunAt is not null, "per-window coverage and time survive persistence");
            new BacktestEngine(new DiagnosticExecutor(_ => throw new RuleExecutionException(persisted.RuleId, "saved error")), builder, diskRepo, data)
                .Run(persisted, BacktestWindow.All);
            restored = new RuleRepository().Find(persisted.RuleId)!;
            Assert(restored.BacktestStats!.WindowAll.FailureCount == 120 && restored.BacktestStats.WindowAll.LastExecutionError == "saved error"
                && restored.BacktestStats.Window100.TriggeredCount == 100, "failure metadata and independent windows survive restart");

            using var old = JsonDocument.Parse("""
            {"window30":{"triggeredCount":0,"killBallCount":0,"correctBallCount":0,"accuracy":0,"sampleInsufficient":true},
             "window50":{"triggeredCount":0,"killBallCount":0,"correctBallCount":0,"accuracy":0,"sampleInsufficient":true},
             "window100":{"triggeredCount":120,"killBallCount":120,"correctBallCount":120,"accuracy":1,"sampleInsufficient":false},
             "lastRunAt":"2026-01-01T00:00:00Z"}
            """);
            var migrated = (BacktestStatsSnapshot)typeof(RuleRepository).GetMethod("ParseStatsSnapshot", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { old.RootElement })!;
            Assert(migrated.LegacyCombinedWindow!.TriggeredCount == 120 && !migrated.WindowAll.IsUsable && !migrated.Window100.IsUsable,
                "ambiguous legacy 100/all preserved but not silently used as verified result");
            diskRepo.SaveBacktestStats(persisted.RuleId, migrated);
            restored = new RuleRepository().Find(persisted.RuleId)!;
            Assert(restored.BacktestStats!.LegacyCombinedWindow!.TriggeredCount == 120, "legacy data preserved on new-format round trip");
        }
        finally
        {
            if (backup is null) File.Delete(path); else File.WriteAllBytes(path, backup);
            foreach (var old in builtinBackup)
            {
                old.Rule.IsEnabled = old.IsEnabled; old.Rule.ForceEnabled = old.ForceEnabled; old.Rule.BacktestStats = old.BacktestStats;
            }
        }
    }

    private sealed class DiagnosticExecutor(Func<RuleContext, int[]> run) : IRuleExecutor
    {
        public KillResult Execute(IKillRule rule, RuleContext context)
        {
            Assert(context.HistoryRecords.Count == context.CurrentIndex, "history excludes predicted draw");
            return new KillResult { RuleId = rule.RuleId, BallType = rule.BallType, KilledBalls = run(context) };
        }
    }

    private sealed class CommitObservingRepository(KillRule rule) : IRuleRepository
    {
        public bool FailSave { get; set; }
        public bool? EnabledAtSave { get; private set; }
        public event Action? RulesChanged { add { } remove { } }
        public IReadOnlyList<IKillRule> GetAll() => new[] { rule };
        public IReadOnlyList<IKillRule> GetEnabled() => rule.IsEnabled ? GetAll() : Array.Empty<IKillRule>();
        public IKillRule? Find(string id) => id == rule.RuleId ? rule : null;
        public void SaveBacktestStats(string id, BacktestStatsSnapshot stats)
        {
            if (FailSave) throw new IOException("fixture save failure");
            EnabledAtSave = rule.IsEnabled;
        }
        public void Add(IKillRule value) => throw new NotSupportedException();
        public void Update(IKillRule value) => throw new NotSupportedException();
        public void Delete(string id) => throw new NotSupportedException();
    }
}
