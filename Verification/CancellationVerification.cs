using SsqAnalyzer.Models;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    private static void VerifyCancellationBeforeCommit()
    {
        foreach (var (records, cancelAt, fail) in new[] { (2, 1, false), (31, 30, false), (2, 1, true) })
        {
            using var cts = new CancellationTokenSource();
            var rule = NewRule("cancelled");
            var repo = new CancellationRepository(rule);
            var executor = new CancellingExecutor(cts, cancelAt, fail);
            var engine = new BacktestEngine(executor, new RuleContextBuilder(), repo, Data(records));
            bool cancelled = false;
            try { engine.Run(rule, BacktestWindow.Last30Triggers, cts.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && repo.Saves == 0 && rule.BacktestStats is null && rule.IsEnabled,
                "cancellation during final execution does not save statistics or change rule enablement");
        }

        using var batchCts = new CancellationTokenSource();
        var first = NewRule("completed");
        var second = NewRule("cancelled");
        var batchRepo = new CancellationRepository(first, second);
        var batch = new BacktestEngine(new CancellingExecutor(batchCts, 2, false),
            new RuleContextBuilder(), batchRepo, Data(2));
        bool batchCancelled = false;
        try { batch.RunAllAsync(BacktestWindow.All, ct: batchCts.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { batchCancelled = true; }
        Assert(batchCancelled && batchRepo.Saves == 1 && first.BacktestStats is not null && second.BacktestStats is null,
            "batch preserves completed rules and never commits the cancelled rule");

        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();
        bool downloadCancelled = false;
        try { new DownloadService().EnsureYtDlp(alreadyCancelled.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { downloadCancelled = true; }
        Assert(downloadCancelled, "cancelled downloader preparation stops before file or network access");

        static KillRule NewRule(string id) => new()
        {
            RuleId = id, Name = id, Category = default, BallType = BallType.Red, JsCode = ""
        };
        static FakeDataService Data(int count)
        {
            var data = new FakeDataService();
            data.SetRecords(Enumerable.Range(0, count).Select(i => new DrawRecord
            {
                Period = 2026001 + i, DrawDate = new DateTime(2026, 1, 1).AddDays(i),
                RedBalls = new[] { 1, 2, 3, 4, 5, 6 }, BlueBall = 1
            }).ToArray());
            return data;
        }
    }

    private sealed class CancellingExecutor(CancellationTokenSource cts, int cancelAt, bool fail) : IRuleExecutor
    {
        private int _calls;
        public KillResult Execute(IKillRule rule, RuleContext context)
        {
            if (++_calls == cancelAt)
            {
                cts.Cancel();
                if (fail) throw new RuleExecutionException(rule.RuleId, "cancelled fixture");
            }
            return new KillResult { RuleId = rule.RuleId, BallType = rule.BallType, KilledBalls = new[] { 1 } };
        }
    }

    private sealed class CancellationRepository(params IKillRule[] rules) : IRuleRepository
    {
        public int Saves { get; private set; }
        public event Action? RulesChanged { add { } remove { } }
        public IReadOnlyList<IKillRule> GetAll() => rules;
        public IReadOnlyList<IKillRule> GetEnabled() => rules.Where(r => r.IsEnabled).ToArray();
        public IKillRule? Find(string id) => rules.FirstOrDefault(r => r.RuleId == id);
        public void SaveBacktestStats(string id, BacktestStatsSnapshot stats) => Saves++;
        public void Add(IKillRule rule) => throw new NotSupportedException();
        public void Update(IKillRule rule) => throw new NotSupportedException();
        public void Delete(string id) => throw new NotSupportedException();
    }
}
