using System.IO;
using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    internal static void VerifyKillEvaluation()
    {
        var m = KillEvaluationMetrics.Compute(new[] { new KillEvaluationPeriod(1, 2, 1), new(2, 2, 1), new(3, 0, 0), new(4, 2, 0), new(5, 0, 0, true) }, BallType.Red);
        Assert(m.Triggered == 3 && m.Failed == 1 && m.WrongPeriods == 2 && m.LongestWrongStreak == 2, "draw risk counts");
        Assert(m.TriggerRate == .75 && m.AverageKilled == 2 && Math.Abs(m.PreserveRate!.Value - 1d / 3) < 1e-12, "risk denominators exclude failed draws");
        Assert(m.Lower95 is null && m.RandomTailProbability is null, "small sample never supplies confident evidence");
        Assert(Math.Abs(KillEvaluationMetrics.HitCdf(16, 1, 1)[0] - 15d / 16) < 1e-12, "blue hypergeometric baseline");
        Assert(KillEvaluationMetrics.HitCdf(33, 6, 33).Take(6).All(p => p == 0), "killing all reds always kills six winning balls");
        var longSeries = Enumerable.Range(1, 80).Select(i => new KillEvaluationPeriod(i, 1, i % 5 == 0 ? 1 : 0)).ToArray();
        var simulated = KillEvaluationMetrics.Compute(longSeries, BallType.Red, 8);
        Assert(simulated == KillEvaluationMetrics.Compute(longSeries, BallType.Red, 8), "reproducible comparison and bootstrap");
        Assert(simulated.Lower95 <= simulated.Accuracy && simulated.Upper95 >= simulated.Accuracy && simulated.AdjustedProbability >= simulated.RandomTailProbability, "uncertainty and multiplicity correction");
        Assert(JsonSerializer.Deserialize<KillEvaluationMetrics>(JsonSerializer.Serialize(simulated)) == simulated, "metrics persistence roundtrip");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        ExpectFailure<OperationCanceledException>(() => KillEvaluationMetrics.Compute(Array.Empty<KillEvaluationPeriod>(), BallType.Red, ct: cancelled.Token));

        var records = Enumerable.Range(0, 121).Select(i => new DrawRecord { Period = 2026001 + i, DrawDate = new DateTime(2026, 1, 1).AddDays(i), RedBalls = new[] { 1, 2, 3, 4, 5, 6 }, BlueBall = 1 }).ToArray();
        var rule = new KillRule { RuleId = "evaluation", Name = "evaluation", Category = RuleCategory.Formula, BallType = BallType.Red, JsCode = "function getKillBalls(ctx) { return [ctx.params.ball]; }", Params = new() { ["ball"] = 33 } };
        var executor = new JintRuleExecutor(); var context = new RuleContextBuilder().Build(records, 1);
        Assert(executor.Execute(rule, context).KilledBalls.Single() == 33, "rule params injected");
        string version = KillRuleDefinition.Capture(rule).Fingerprint;
        rule.Params["ball"] = 32;
        Assert(executor.Execute(rule, context).KilledBalls.Single() == 32 && version != KillRuleDefinition.Capture(rule).Fingerprint, "parameter changes invalidate version and cached values");
        var clock = new KillRule { RuleId = "clock", Name = "clock", Category = RuleCategory.Formula, JsCode = "function getKillBalls(ctx) { return [Date.now() % 33 + 1]; }", BallType = BallType.Red };
        ExpectFailure<RuleExecutionException>(() => executor.Execute(clock, context));
        var second = (KillRuleDefinition.Capture(rule) with { RuleId = "second" }).ToRule();
        var research = new KillResearchService(executor);
        var report = research.Evaluate(new[] { rule, second }, records);
        new KillResearchService(new DiagnosticExecutor(ctx =>
        {
            Assert(ctx.HistoryRecords.Count == ctx.CurrentIndex && ctx.HistoryRecords.All(r => r.Period < records[ctx.CurrentIndex].Period), "every evaluation step excludes target and future records");
            return new[] { 33 };
        })).Evaluate(new[] { rule }, records);
        var combined = report.Rows.Single(r => r.Combined && r.BallType == BallType.Red);
        Assert(combined.Development.Triggered == 84 && combined.Validation.Triggered == 36 && combined.Validation.Killed == 36, "chronological split and union deduplication");
        Assert(report.DevelopmentEnd < report.ValidationStart && combined.Validation.PreserveRate == 1, "separate ranges and complete preservation");
        var changed = KillResearchService.SnapshotRecords(records);
        foreach (var record in changed.Where(r => r.Period >= report.ValidationStart)) record.RedBalls = new[] { 1, 2, 3, 4, 5, 32 };
        var changedReport = research.Evaluate(new[] { rule, second }, changed);
        Assert(changedReport.Rows[0].Development == report.Rows[0].Development && changedReport.Rows[0].Validation.PreserveRate == 0, "future outcomes cannot change development evaluation");
        second = (KillRuleDefinition.Capture(second) with { Code = "function getKillBalls(ctx) { throw new Error('fixture'); }" }).ToRule();
        Assert(research.Evaluate(new[] { rule, second }, records).Rows[0].Validation.Failed == 36, "union refuses partial success when component fails");
        ExpectFailure<InvalidOperationException>(() => research.Evaluate(new[] { rule }, records.Take(50).ToArray()));

        string directory = Path.Combine(AppContext.BaseDirectory, "evaluation-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ledger.json");
        DateTime now = new(2026, 9, 23, 2, 0, 0, DateTimeKind.Utc);
        var store = new KillForwardStore(path, () => now);
        var history = new[] { new DrawRecord { Period = 2026110, DrawDate = new DateTime(2026, 9, 22), RedBalls = new[] { 1, 2, 3, 4, 5, 6 }, BlueBall = 1 } };
        ExpectFailure<InvalidOperationException>(() => store.Freeze(new[] { rule }, history, 2026111, now.Date, executor));
        var frozen = store.Freeze(new[] { rule }, history, 2026111, new DateTime(2026, 9, 24), executor);
        Assert(frozen.Predictions.Single().KilledBalls.Single() == 32 && store.ToText().Contains("待开奖 1 期"), "saved prediction includes actual frozen outputs");
        ExpectFailure<InvalidOperationException>(() => store.Freeze(new[] { rule }, history, 2026111, new DateTime(2026, 9, 24), executor));
        Assert(store.Settle(history) == 0, "pending results are not scored");
        now = now.AddDays(2);
        var actual = history.Concat(new[] { new DrawRecord { Period = 2026111, DrawDate = new DateTime(2026, 9, 24), RedBalls = new[] { 1, 2, 3, 4, 5, 32 }, BlueBall = 1 } }).ToArray();
        rule.Params["ball"] = 31;
        Assert(store.Settle(actual) == 1 && store.Settle(actual) == 0, "settlement idempotent and independent of edited rules");
        Assert(store.ToText().Contains("整期错杀率 100.0%"), "settlement uses frozen losing kill");
        string committed = File.ReadAllText(path);
        actual[1].BlueBall = 2;
        ExpectFailure<InvalidOperationException>(() => store.Settle(actual));
        Assert(File.ReadAllText(path) == committed, "changed result cannot overwrite settlement");
        var corrupt = JsonSerializer.Deserialize<KillForwardLedger>(committed)!;
        corrupt.Predictions[0].Predictions[0].KilledBalls[0] = 30;
        File.WriteAllText(path, JsonSerializer.Serialize(corrupt));
        ExpectFailure<InvalidDataException>(() => store.ToText());
        Console.WriteLine("PASS kill evaluation metrics, parameters, holdout, union and frozen settlement");
    }

    private static void ExpectFailure<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void VerifyKillEvaluationWindow()
    {
        var data = new FakeDataService();
        data.SetRecords(Enumerable.Range(0, 121).Select(i => new DrawRecord { Period = 2026001 + i, DrawDate = new DateTime(2026, 1, 1).AddDays(i), RedBalls = new[] { 1, 2, 3, 4, 5, 6 }, BlueBall = 1 }).ToArray());
        var rule = new KillRule { RuleId = "ui-evaluation", Name = "界面验证", Category = RuleCategory.Formula, BallType = BallType.Red, JsCode = "function getKillBalls(ctx) { return [33]; }", IsEnabled = true };
        var window = new SsqAnalyzer.Pages.KillEvaluationWindow(data, new ListRuleRepository(rule), new JintRuleExecutor(),
            new KillForwardStore(Path.Combine(AppContext.BaseDirectory, "ui-evaluation", "ledger.json")));
        window.Show(); PumpDispatcher();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var task = (Task<string>)typeof(SsqAnalyzer.Pages.KillEvaluationWindow).GetMethod("Analyze", flags)!.Invoke(window, new object[] { CancellationToken.None })!;
        PumpUntil(() => task.IsCompleted);
        Assert(task.GetAwaiter().GetResult().Contains("【合并】"), "evaluation window completes background research");
        var output = (System.Windows.Controls.TextBox)typeof(SsqAnalyzer.Pages.KillEvaluationWindow).GetField("_output", flags)!.GetValue(window)!;
        output.Text = task.Result;
        window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1040, 780, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "kill-evaluation.png"))) encoder.Save(file);
        window.Close(); PumpDispatcher();
    }
}
