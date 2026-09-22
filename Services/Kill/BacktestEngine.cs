using SsqAnalyzer.Models;
using System.Diagnostics;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 回测引擎（架构设计 §3.6 + §4.2 时序图 + §7.4 per-ball pooled 算法）。
///
/// per-ball pooled 概率（Q3 决策）：
///   accuracy = Σ对球 / Σ杀球（不是 all-or-nothing）
///   红蓝独立：rule.BallType=Red 只校验 records[i].RedBalls；Blue 只校验 BlueBall。
///
/// 时间护栏（§7.3）：复用 <see cref="IRuleContextBuilder.Build"/>(allRecords, i)，
/// 规则只能看 [0, i)，被预测期 = allRecords[i]，绝不偷看未来。
///
/// 门槛联动（§3.5/§3.6 注解）：回测完成后保存快照到 repository，
/// 并按本次完成的窗口判定准确率门槛：
///   accuracy &lt; 全局门槛（IKillSettings.GetMinAccuracy，红球 0.82、蓝球 0.94）且 !ForceEnabled → IsEnabled = false。
/// 全局门槛未注入（如单测）时回退到对应球种的固定门槛。
///
/// 注：<see cref="IRuleRepository"/> 接口未暴露 ApplyAccuracyGate（仅 <see cref="RuleRepository"/> 具体类有），
/// 此处内联门槛判定逻辑（与 RuleRepository.ApplyAccuracyGate 等价，约 10 行），
/// 以保持可被 mock IRuleRepository 单测，避免耦合到具体类型。
/// </summary>
public sealed class BacktestEngine : IBacktestEngine
{
    private const int MaxErrorSamples = 5;  // 架构 §2.1：错误样本前 5 条

    private readonly IRuleExecutor _executor;
    private readonly IRuleContextBuilder _ctxBuilder;
    private readonly IRuleRepository _repo;
    private readonly IDataService _dataService;
    private readonly IKillSettings? _settings;

    public BacktestEngine(
        IRuleExecutor executor,
        IRuleContextBuilder ctxBuilder,
        IRuleRepository repo,
        IDataService dataService,
        IKillSettings? settings = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _ctxBuilder = ctxBuilder ?? throw new ArgumentNullException(nameof(ctxBuilder));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _settings = settings;
    }

    /// <inheritdoc />
    public BacktestStat Run(IKillRule rule, BacktestWindow window, CancellationToken ct = default)
    {
        if (rule is null) throw new ArgumentNullException(nameof(rule));
        ct.ThrowIfCancellationRequested();

        var allRecords = _dataService.GetAllRecords();
        var stat = RunInternal(rule, allRecords, window, ct);

        // 把单窗口结果合并到 BacktestStatsSnapshot（保留其他窗口的既有值）
        var newWindowStat = new BacktestWindowStat(
            TriggeredCount: stat.TriggeredCount,
            KillBallCount: stat.KillBallCount,
            CorrectBallCount: stat.CorrectBallCount,
            Accuracy: stat.Accuracy,
            SampleInsufficient: stat.SampleInsufficient)
        {
            ErrorSamples = stat.ErrorSamples,
            ElapsedMs = stat.ElapsedMs,
            RunAt = stat.RunAt,
            EvaluatedCount = stat.EvaluatedCount,
            FailureCount = stat.FailureCount,
            LastExecutionError = stat.LastExecutionError,
            FirstPeriod = stat.FirstPeriod,
            LastPeriod = stat.LastPeriod
        };

        var existing = rule.BacktestStats;
        var empty = new BacktestWindowStat(0, 0, 0, 0, SampleInsufficient: true);
        var w30 = window == BacktestWindow.Last30Triggers ? newWindowStat : (existing?.Window30 ?? empty);
        var w50 = window == BacktestWindow.Last50Triggers ? newWindowStat : (existing?.Window50 ?? empty);
        var w100 = window == BacktestWindow.Last100Triggers ? newWindowStat : (existing?.Window100 ?? empty);
        var snapshot = new BacktestStatsSnapshot(w30, w50, w100, stat.RunAt)
        {
            WindowAll = window == BacktestWindow.All ? newWindowStat : existing?.WindowAll ?? empty,
            LastWindow = window,
            LegacyCombinedWindow = existing?.LegacyCombinedWindow
        };

        // 直接回写 rule.BacktestStats（保证内存状态一致，不依赖 repository 实现是否回写）
        ct.ThrowIfCancellationRequested();
        bool previousEnabled = rule.IsEnabled;
        try
        {
            rule.BacktestStats = snapshot;
            ApplyAccuracyGate(rule);
            // Persist the statistics and resulting enabled state in one write.
            _repo.SaveBacktestStats(rule.RuleId, snapshot);
        }
        catch
        {
            rule.BacktestStats = existing;
            if (rule is KillRule mutable) mutable.IsEnabled = previousEnabled;
            throw;
        }

        return stat;
    }

    /// <inheritdoc />
    public async Task RunAllAsync(BacktestWindow window, IProgress<(int done, int total)>? progress = null, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            var rules = _repo.GetAll();
            int total = rules.Count;
            int done = 0;
            foreach (var rule in rules)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    Run(rule, window, ct);
                }
                catch (RuleExecutionException)
                {
                    // 单规则回测失败：跳过，继续下一条（不中断批量）
                }
                done++;
                progress?.Report((done, total));
            }
        }, ct);
    }

    /// <inheritdoc />
    public Task BacktestAll(int targetN = 100, IProgress<(int done, int total)>? progress = null, CancellationToken ct = default)
    {
        var window = targetN switch
        {
            100 => BacktestWindow.Last100Triggers,
            50  => BacktestWindow.Last50Triggers,
            30  => BacktestWindow.Last30Triggers,
            _   => BacktestWindow.All
        };
        return RunAllAsync(window, progress, ct);
    }

    // ==================== 内部实现 ====================

    /// <summary>
    /// per-ball pooled 概率回测核心算法（§7.4）。
    /// 从最新期往前遍历，i = 被预测期索引；规则通过 ctx 只能看 [0, i)。
    /// 凑满 requiredTriggers 次触发即停；样本不足仍返回已统计部分。
    ///
    /// 性能优化（瓶颈3）：循环外一次性预计算全量遗漏矩阵 fullMissMatrix，
    /// 循环内复用 BuildWithMiss 切片，避免每期重算遗漏矩阵（100 期 × N 球 → 1 次 N 球）。
    /// </summary>
    internal BacktestStat RunInternal(IKillRule rule, IReadOnlyList<DrawRecord> allRecords,
        BacktestWindow window, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();

        int requiredTriggers = window switch
        {
            BacktestWindow.Last30Triggers => 30,
            BacktestWindow.Last50Triggers => 50,
            BacktestWindow.Last100Triggers => 100,
            _ => int.MaxValue  // All：遍历全部历史
        };

        int triggered = 0;
        int killSum = 0;
        int correctSum = 0;
        var errorSamples = new List<BacktestErrorSample>();
        int evaluated = 0, failures = 0;
        int? firstPeriod = null, lastPeriod = null;
        string? lastError = null;

        // 循环外预计算全量遗漏矩阵（瓶颈3 优化）：1 次 O(N×49) 而非每期 O(currentIndex×49)
        // RuleContextBuilder.BuildWithMiss 复用切片：fullMissMatrix[0..i] 行 == 原 Compute(allRecords[0..i], allRecords)
        var concreteBuilder = _ctxBuilder as RuleContextBuilder;
        int[,]? fullMissMatrix = null;
        if (concreteBuilder != null && allRecords.Count > 0)
        {
            fullMissMatrix = MissMatrixCalculator.Compute(allRecords, allRecords);
        }

        // i = 被预测期索引；规则看 [0, i)，最新 = allRecords[i-1]
        // 从最新（i=Count-1）往最旧（i=1）遍历，凑满 requiredTriggers 次触发即停
        for (int i = allRecords.Count - 1; i >= 1 && triggered < requiredTriggers; i--)
        {
            ct.ThrowIfCancellationRequested();
            evaluated++;
            firstPeriod = allRecords[i].Period;
            lastPeriod ??= allRecords[i].Period;
            // 优先走 BuildWithMiss（生产环境：DI 注入 RuleContextBuilder 实例，可转换）
            // fallback 走 Build（测试环境：mock IRuleContextBuilder，不可转换）
            RuleContext ctx = (concreteBuilder != null && fullMissMatrix != null)
                ? concreteBuilder.BuildWithMiss(allRecords, i, fullMissMatrix)
                : _ctxBuilder.Build(allRecords, i);

            KillResult result;
            try
            {
                result = _executor.Execute(rule, ctx);
            }
            catch (RuleExecutionException ex)
            {
                failures++;
                lastError = ex.Message;
                continue;
            }

            ct.ThrowIfCancellationRequested();
            if (!result.Triggered) continue;

            triggered++;
            var predicted = allRecords[i];
            IReadOnlyList<int> actualBalls = rule.BallType == BallType.Red
                ? predicted.RedBalls
                : new[] { predicted.BlueBall };

            var hitBalls = new List<int>();
            foreach (var b in result.KilledBalls)
            {
                killSum++;
                if (!actualBalls.Contains(b))
                {
                    correctSum++;  // 被杀且未开出 → 正确
                }
                else
                {
                    hitBalls.Add(b);  // 被杀且开出 → 错杀
                }
            }

            if (hitBalls.Count > 0 && errorSamples.Count < MaxErrorSamples)
            {
                errorSamples.Add(new BacktestErrorSample(
                    Period: predicted.Period,
                    KilledBalls: result.KilledBalls.ToArray(),
                    ActualNextBalls: actualBalls.ToArray(),
                    HitBalls: hitBalls.ToArray()));
            }
        }

        ct.ThrowIfCancellationRequested();
        return new BacktestStat
        {
            RuleId = rule.RuleId,
            Window = window,
            TriggeredCount = triggered,
            KillBallCount = killSum,
            CorrectBallCount = correctSum,
            ErrorSamples = errorSamples,
            ElapsedMs = sw.ElapsedMilliseconds,
            EvaluatedCount = evaluated,
            FailureCount = failures,
            LastExecutionError = lastError,
            FirstPeriod = firstPeriod,
            LastPeriod = lastPeriod
        };
    }

    /// <summary>
    /// 准确率门槛判定（等价于 RuleRepository.ApplyAccuracyGate，§3.5/§3.6 注解）：
    /// 使用最近完成的窗口；样本不足或执行失败时不自动改变启用状态。
    /// accuracy &lt; 全局门槛（_settings?.GetMinAccuracy(rule.BallType) ?? KillSettings.For(rule.BallType)）且 !ForceEnabled → IsEnabled = false。
    /// </summary>
    private void ApplyAccuracyGate(IKillRule rule)
    {
        if (rule is not KillRule kr) return;
        var stats = kr.BacktestStats;
        if (stats is null) return;

        var stat = stats.Effective;
        if (!stat.IsUsable) return;

        double gate = _settings?.GetMinAccuracy(rule.BallType) ?? KillSettings.For(rule.BallType);
        if (stat.Accuracy < gate && !kr.ForceEnabled)
        {
            kr.IsEnabled = false;
        }
    }
}
