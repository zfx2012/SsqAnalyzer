using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 杀号执行引擎（架构设计 §3.5 + §4.1 时序图）。
/// 流程：
///   1. 取所有 IsEnabled=true 的规则（Execute() 无参版本）或调用方指定子集；
///   2. 用 <see cref="IRuleContextBuilder.BuildLatest"/> 构建"最新一期"上下文（时间护栏：规则看不到被预测期）；
///   3. 遍历规则 → <see cref="IRuleExecutor.Execute"/> 得 <see cref="KillResult"/>；
///   4. 汇总：每个被杀号码记录"被哪些规则杀"（<see cref="BallRuleTrace"/>），同一号码多规则杀 → 合并理由（不重复杀）；
///   5. 置信度打标（门槛对齐全局 MinAccuracy，红球 82%、蓝球 94%）：达标规则数 ≥2→High / =1→Medium / =0→Low，写入 <see cref="KilledBallDetail.Confidence"/>；
///   6. 推荐集 = 1-33 扣被杀红球 / 1-16 扣被杀蓝球；
///   7. 生成 <see cref="KillReport"/>（含 TargetPeriod/被杀明细/推荐集）。
/// </summary>
public sealed class KillEngine : IKillEngine
{
    private const int RedBallCount = 33;
    private const int BlueBallCount = 16;

    private readonly IRuleRepository _repo;
    private readonly IRuleExecutor _executor;
    private readonly IRuleContextBuilder _ctxBuilder;
    private readonly IKillSettings? _settings;

    public KillEngine(
        IRuleRepository repo,
        IRuleExecutor executor,
        IRuleContextBuilder ctxBuilder,
        IKillSettings? settings = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _ctxBuilder = ctxBuilder ?? throw new ArgumentNullException(nameof(ctxBuilder));
        _settings = settings;
    }

    /// <inheritdoc />
    public KillReport Execute()
    {
        var enabled = _repo.GetEnabled();
        return Execute(enabled);
    }

    /// <inheritdoc />
    public KillReport Execute(IEnumerable<IKillRule> rules) => Execute(rules, null);

    public KillReport Execute(IEnumerable<IKillRule> rules, BacktestWindow? evaluationWindow)
    {
        if (rules is null) throw new ArgumentNullException(nameof(rules));

        var ruleList = rules.ToList();
        var ctx = _ctxBuilder.BuildLatest();
        var targetPeriod = ComputeTargetPeriod(ctx);

        // 逐规则执行（单规则失败不影响其他规则）
        var results = new List<KillResult>(ruleList.Count);
        foreach (var rule in ruleList)
        {
            try
            {
                var result = _executor.Execute(rule, ctx);
                results.Add(result);
            }
            catch (RuleExecutionException)
            {
                // 单规则执行失败：记录空结果（未触发），其他规则继续
                results.Add(new KillResult
                {
                    RuleId = rule.RuleId,
                    BallType = rule.BallType,
                    KilledBalls = Array.Empty<int>(),
                    Reason = $"规则执行失败，已跳过",
                    Elapsed = TimeSpan.Zero
                });
            }
        }

        // 汇总：按球号聚合溯源链（同一号码多规则杀 → 合并理由，不重复杀）
        var redTracesByBall = new Dictionary<int, List<(IKillRule Rule, KillResult Result, double EffectiveAccuracy)>>();
        var blueTracesByBall = new Dictionary<int, List<(IKillRule Rule, KillResult Result, double EffectiveAccuracy)>>();

        for (int i = 0; i < ruleList.Count; i++)
        {
            var rule = ruleList[i];
            var result = results[i];
            if (!result.Triggered) continue;

            double effAcc = GetEffectiveAccuracy(rule, evaluationWindow);
            var bucket = rule.BallType == BallType.Red ? redTracesByBall : blueTracesByBall;
            foreach (var ball in result.KilledBalls)
            {
                if (!bucket.TryGetValue(ball, out var list))
                {
                    list = new List<(IKillRule, KillResult, double)>();
                    bucket[ball] = list;
                }
                list.Add((rule, result, effAcc));
            }
        }

        var killedReds = BuildKilledDetails(redTracesByBall, BallType.Red);
        var killedBlues = BuildKilledDetails(blueTracesByBall, BallType.Blue);
        var recommendedReds = ComputeSurvived(RedBallCount, killedReds);
        var recommendedBlues = ComputeSurvived(BlueBallCount, killedBlues);

        return new KillReport
        {
            TargetPeriod = targetPeriod,
            EvaluationWindow = evaluationWindow,
            GeneratedAt = DateTime.UtcNow,
            EnabledRules = ruleList,
            Results = results,
            KilledRedBalls = killedReds,
            KilledBlueBalls = killedBlues,
            RecommendedRedBalls = recommendedReds,
            RecommendedBlueBalls = recommendedBlues
        };
    }

    /// <summary>
    /// 计算被预测期号：BuildLatest 的 ctx.CurrentIndex = allRecords.Count，
    /// 被预测期 = 最近一期 Period + 1（无历史则返回 0）。
    /// </summary>
    private static int ComputeTargetPeriod(RuleContext ctx)
    {
        if (ctx.LatestRecord is null) return 0;
        return ctx.LatestRecord.Period + 1;
    }

    /// <summary>
    /// 取规则的有效回测准确率（架构 §3.5 门槛判定优先级）：
    /// 使用指定窗口（默认最近完成的窗口），无有效统计时返回 0，不借用其他窗口。
    /// </summary>
    private static double GetEffectiveAccuracy(IKillRule rule, BacktestWindow? window)
    {
        var stats = rule.BacktestStats;
        if (stats is null) return 0;
        var stat = window is { } selected ? stats.ForWindow(selected) : stats.Effective;
        return stat.IsUsable && (stat.RuleFingerprint is null || stat.RuleFingerprint == KillRuleDefinition.Capture(rule).Fingerprint) ? stat.Accuracy : 0;
    }

    /// <summary>
    /// 把按球号聚合的溯源链转为 <see cref="KilledBallDetail"/> 列表，并打置信度标。
    /// 置信度规则（门槛对齐全局 MinAccuracy，红球 82%、蓝球 94%）：
    ///   达标规则数（有效准确率 ≥ 门槛）≥ 2 且平均 ≥ 门槛 → High
    ///   达标规则数 = 1 → Medium
    ///   达标规则数 = 0 → Low
    /// </summary>
    private List<KilledBallDetail> BuildKilledDetails(
        Dictionary<int, List<(IKillRule Rule, KillResult Result, double EffectiveAccuracy)>> tracesByBall,
        BallType ballType)
    {
        double gate = _settings?.GetMinAccuracy(ballType) ?? KillSettings.For(ballType);
        var result = new List<KilledBallDetail>(tracesByBall.Count);
        foreach (var kv in tracesByBall)
        {
            int ball = kv.Key;
            var entries = kv.Value;

            var traces = new List<BallRuleTrace>(entries.Count);
            int passingCount = 0;
            double passingSum = 0;
            foreach (var (rule, result2, effAcc) in entries)
            {
                traces.Add(new BallRuleTrace(
                    RuleId: rule.RuleId,
                    RuleName: rule.Name,
                    Category: rule.Category,
                    Reason: result2.Reason,
                    BacktestAccuracy: effAcc));
                if (effAcc >= gate)
                {
                    passingCount++;
                    passingSum += effAcc;
                }
            }

            ConfidenceLevel confidence;
            if (passingCount >= 2 && (passingSum / passingCount) >= gate)
                confidence = ConfidenceLevel.High;
            else if (passingCount >= 1)
                confidence = ConfidenceLevel.Medium;
            else
                confidence = ConfidenceLevel.Low;

            // 按球号升序展示溯源（便于报告阅读）
            traces.Sort((a, b) => string.CompareOrdinal(a.RuleId, b.RuleId));

            result.Add(new KilledBallDetail
            {
                Ball = ball,
                BallType = ballType,
                Confidence = confidence,
                PassingRuleCount = passingCount,
                Traces = traces
            });
        }
        result.Sort((a, b) => a.Ball.CompareTo(b.Ball));
        return result;
    }

    /// <summary>计算存活球号 = 1..max 减去被杀球号。</summary>
    private static IReadOnlyList<int> ComputeSurvived(int maxBall, IReadOnlyList<KilledBallDetail> killed)
    {
        var killedSet = new HashSet<int>(killed.Select(d => d.Ball));
        var survived = new List<int>(maxBall - killedSet.Count);
        for (int b = 1; b <= maxBall; b++)
            if (!killedSet.Contains(b))
                survived.Add(b);
        return survived;
    }
}
