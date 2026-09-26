using System.Collections.Concurrent;
using System.Diagnostics;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// Jint 沙箱执行器（默认实现）。
/// 安全约束（§7.1）：
///   - 仅注入 ctx proxy（白名单 API），不暴露 File/Process/Http/反射/require/import
///   - 超时限制（默认 5 秒）
///   - 内存限制（默认 16 MB）
///   - 禁用 Conversation（防止 Number/String 等扩展）
///   - 替换 Math.random 为抛异常（确保确定性，便于回测）
/// 异常包装：JS 报错转成 RuleExecutionException，不向用户泄露堆栈。
///
/// 性能优化（瓶颈2）：
///   - 按 rule.RuleId 缓存 Engine，避免每次 Execute 都重新编译 ProxyJsCode + rule.JsCode
///   - ProxyJsCode 拆成 ProxyFunctionsJs（函数定义，只编译一次）+ ProxyCtxJs（ctx 重建，每次执行）
///   - 同规则回测 100 期：100 次 Engine 创建+编译 → 1 次
/// 线程安全：
///   - Jint Engine 非线程安全；IRuleExecutor 是 Singleton，可能被 KillEngine/UI 线程 与
///     BacktestEngine/后台 Task 并发调用。用 _engineLock 串行化 Execute 调用，确保
///     同一时刻只有一个线程在操作 Engine。回测/杀号本身是串行 foreach，全局锁不损失吞吐。
/// </summary>
public sealed class JintRuleExecutor : IRuleExecutor
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private const int DefaultMemoryLimitBytes = 16 * 1024 * 1024;  // 16 MB

    private readonly TimeSpan _executionTimeout;
    private readonly int _memoryLimitBytes;

    /// <summary>按 rule.RuleId 缓存已初始化的 Engine（含 Math.random 禁用 + ProxyFunctionsJs + rule.JsCode）。</summary>
    private readonly ConcurrentDictionary<string, Engine> _engineCache = new();
    private readonly ConcurrentDictionary<string, string> _engineSourceCache = new();
    private readonly object _engineLock = new();

    /// <summary>Engine 调用串行化锁（Jint Engine 非线程安全）。</summary>

    public JintRuleExecutor(TimeSpan? executionTimeout = null, int memoryLimitBytes = DefaultMemoryLimitBytes)
    {
        _executionTimeout = executionTimeout ?? DefaultTimeout;
        _memoryLimitBytes = memoryLimitBytes > 0 ? memoryLimitBytes : DefaultMemoryLimitBytes;
    }

    public KillResult Execute(IKillRule rule, RuleContext ctx)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(rule.JsCode))
            throw new RuleExecutionException(rule.RuleId, $"规则 {rule.RuleId} 的 jsCode 为空");

        var sw = Stopwatch.StartNew();
        try
        {
            JsValue jsResult;
            lock (_engineLock)
            {
                // 取或创建缓存的 Engine（首次调用某规则时编译 ProxyFunctionsJs + rule.JsCode）
                if (!_engineSourceCache.TryGetValue(rule.RuleId, out var cachedSource)
                    || !string.Equals(cachedSource, rule.JsCode, StringComparison.Ordinal))
                {
                    var refreshedEngine = CreateEngine(rule);
                    _engineCache[rule.RuleId] = refreshedEngine;
                    _engineSourceCache[rule.RuleId] = rule.JsCode;
                }

                var engine = _engineCache[rule.RuleId];

                // 更新 .NET ctx（每次 Execute 都变）+ 重建 ctx proxy 对象（引用新的 __ctx_dotnet）
                engine.SetValue("__ctx_dotnet", ctx);
                engine.SetValue("__rule_params_json", System.Text.Json.JsonSerializer.Serialize(rule.Params));
                engine.Execute(ProxyCtxJs);

                // 调用 getKillBalls(ctx)
                jsResult = engine.Evaluate("getKillBalls(ctx)");
            }

            // 解析返回值：必须是 number[]，每个元素为 1-33（红）或 1-16（蓝）
            var killedBalls = ParseKilledBalls(jsResult, rule);

            sw.Stop();
            return new KillResult
            {
                RuleId = rule.RuleId,
                BallType = rule.BallType,
                KilledBalls = killedBalls,
                Reason = BuildReason(rule, killedBalls),
                Elapsed = sw.Elapsed
            };
        }
        catch (RuleExecutionException)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            throw new RuleExecutionException(rule.RuleId,
                $"规则 {rule.RuleId} 执行超时（>{_executionTimeout.TotalSeconds:F0}s）", ex);
        }
        catch (MemoryLimitExceededException ex)
        {
            throw new RuleExecutionException(rule.RuleId,
                $"规则 {rule.RuleId} 执行超出内存限制（>{_memoryLimitBytes / 1024 / 1024}MB）", ex);
        }
        catch (JavaScriptException ex)
        {
            // JS 报错转友好提示，不泄露堆栈给用户
            throw new RuleExecutionException(rule.RuleId,
                $"规则 {rule.RuleId} 执行失败：{ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new RuleExecutionException(rule.RuleId,
                $"规则 {rule.RuleId} 执行失败：{ex.Message}", ex);
        }
        finally
        {
            _engineCache.TryRemove(rule.RuleId, out _);
            _engineSourceCache.TryRemove(rule.RuleId, out _);
            sw.Stop();
        }
    }

    /// <summary>
    /// 创建并初始化 Engine（仅首次调用某规则时执行）：
    ///   1. 配置超时/内存限制
    ///   2. 替换 Math.random 为抛异常（确保规则确定性，便于回测）
    ///   3. 注入 ProxyFunctionsJs（__toRecordView/__toMissArray/__toRecordArray，只编译一次）
    ///   4. 执行 rule.JsCode（定义 getKillBalls，只编译一次）
    ///   5. 验证 getKillBalls 已定义
    /// 后续 Execute 仅更新 __ctx_dotnet + Execute(ProxyCtxJs) + Evaluate("getKillBalls(ctx)")。
    /// </summary>
    private Engine CreateEngine(IKillRule rule)
    {
        var engine = new Engine(options => options
            .TimeoutInterval(_executionTimeout)
            .LimitMemory(_memoryLimitBytes));

        // 替换 Math.random 为抛异常（确保规则确定性，便于回测）
        engine.Execute(
            "Math.random = function() { throw new Error('Math.random is disabled: rules must be deterministic'); };");

        // 注入 proxy 函数定义（只编译一次，不依赖 __ctx_dotnet）
        engine.Execute("Date = function() { throw new Error('Date is disabled: rules must use historical data'); }; Date.now = Date;");
        engine.Execute(ProxyFunctionsJs);

        // 执行规则 JS（必须定义 function getKillBalls(ctx)）
        engine.Execute(rule.JsCode);

        // 验证 getKillBalls 已定义（非 null/undefined）
        var fn = engine.GetValue("getKillBalls");
        if (fn is null || fn.IsUndefined() || fn.IsNull())
            throw new RuleExecutionException(rule.RuleId,
                $"规则 {rule.RuleId} 的 jsCode 未定义顶层函数 getKillBalls(ctx)");

        return engine;
    }

    /// <summary>
    /// ctx proxy JS 代码（拆分自原 ProxyJsCode）：只定义 __toRecordView/__toMissArray/__toRecordArray
    /// 三个工具函数。这些函数不依赖 __ctx_dotnet，可一次性编译并缓存。
    /// DrawRecord 也通过 __toRecordView 转为只读 JS 对象（redBalls 是 JS 数组拷贝，规则修改不影响 .NET 端）。
    /// </summary>
    private const string ProxyFunctionsJs = @"
function __toRecordView(rec) {
    if (!rec) return null;
    var srcRed = rec.RedBalls;
    var jsRedBalls = [];
    for (var i = 0; i < srcRed.length; i++) jsRedBalls.push(srcRed[i]);
    return {
        period: rec.Period,
        drawDate: rec.DrawDate,
        redBalls: jsRedBalls,
        blueBall: rec.BlueBall,
        redSum: rec.RedSum,
        redSpan: rec.RedSpan,
        oddCount: rec.OddCount,
        evenCount: rec.EvenCount,
        zoneLabel: rec.ZoneLabel,
        bigSmallLabel: rec.BigSmallLabel,
        primeLabel: rec.PrimeLabel,
        zo2Label: rec.ZO2Label,
        linkCount: rec.LinkCount
    };
}
function __toMissArray(dotnetArr) {
    var result = [];
    for (var i = 0; i < dotnetArr.length; i++) {
        result.push({ ball: dotnetArr[i].Ball, value: dotnetArr[i].Value });
    }
    return result;
}
function __toRecordArray(dotnetArr) {
    var result = [];
    for (var i = 0; i < dotnetArr.length; i++) result.push(__toRecordView(dotnetArr[i]));
    return result;
}
";

    /// <summary>
    /// ctx 对象构建 JS（拆分自原 ProxyJsCode）：每次 Execute 重跑，因为依赖的 __ctx_dotnet 变了。
    /// 把 .NET RuleContext 包装为驼峰 JS 对象（按 §7.1 白名单）。
    /// </summary>
    private const string ProxyCtxJs = @"
var __ctx = __ctx_dotnet;
var ctx = {
    params: JSON.parse(__rule_params_json),
    currentCycle: __ctx.CurrentCycle,
    currentParity: __ctx.CurrentParity,
    currentShortPeriodSuffix: __ctx.CurrentShortPeriodSuffix,
    latestRecord: __toRecordView(__ctx.LatestRecord),
    history: function (n) { return __toRecordArray(__ctx.History(n)); },
    historyFor: function (scope, n) { return __toRecordArray(__ctx.HistoryFor(scope, n)); },
    latestFor: function (scope) { return __toRecordView(__ctx.LatestFor(scope)); },
    getMiss: function (ball, window) { return __ctx.GetMiss(ball, window); },
    getMissFor: function (scope, ball, window) { return __ctx.GetMissFor(scope, ball, window); },
    getPreviousRedOccurrenceDistance: function (ball) { return __ctx.GetPreviousRedOccurrenceDistance(ball); },
    getPreviousRedOccurrenceDistanceFor: function (scope, ball) { return __ctx.GetPreviousRedOccurrenceDistanceFor(scope, ball); },
    getMissValues: function (window) { return __toMissArray(__ctx.GetMissValues(window)); },
    getMissValuesFor: function (scope, window) { return __toMissArray(__ctx.GetMissValuesFor(scope, window)); },
    getSamePeriodRecords: function (suffix3) { return __toRecordArray(__ctx.GetSamePeriodRecords(suffix3)); },
    getCycleRecords: function (cycleType) { return __toRecordArray(__ctx.GetCycleRecords(cycleType)); },
    getParityRecords: function (parityType) { return __toRecordArray(__ctx.GetParityRecords(parityType)); }
};
";

    private static IReadOnlyList<int> ParseKilledBalls(JsValue jsResult, IKillRule rule)
    {
        if (jsResult is null || jsResult.IsNull() || jsResult.IsUndefined())
            return Array.Empty<int>();

        if (!jsResult.IsArray())
            throw new RuleExecutionException(rule.RuleId,
                $"规则 {rule.RuleId} 的 getKillBalls 返回值不是数组（实际类型：{jsResult.Type}）");

        var arr = jsResult.AsArray();
        var result = new List<int>((int)arr.Length);
        int maxBall = rule.BallType == BallType.Red ? 33 : 16;
        for (uint i = 0; i < arr.Length; i++)
        {
            var elem = arr[i];
            if (!elem.IsNumber())
                throw new RuleExecutionException(rule.RuleId,
                    $"规则 {rule.RuleId} 的 getKillBalls 返回数组包含非数字元素（索引 {i}）");
            var num = elem.AsNumber();
            int intNum = (int)num;
            if (num != intNum)
                throw new RuleExecutionException(rule.RuleId,
                    $"规则 {rule.RuleId} 的 getKillBalls 返回数组包含非整数元素（索引 {i}：{num}）");
            if (intNum < 1 || intNum > maxBall)
                throw new RuleExecutionException(rule.RuleId,
                    $"规则 {rule.RuleId} 返回号码 {intNum} 越界（{rule.BallType} 应为 1-{maxBall}）");
            if (!result.Contains(intNum))
                result.Add(intNum);
        }
        return result;
    }

    private static string BuildReason(IKillRule rule, IReadOnlyList<int> killedBalls)
    {
        if (killedBalls.Count == 0) return $"{rule.Name}：未触发";
        return $"{rule.Name}：杀 [{string.Join(",", killedBalls)}]";
    }
}
