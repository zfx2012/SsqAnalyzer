using System.IO;
using System.Text;
using System.Text.Json;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services.Kill;

public sealed record FrozenRulePrediction(string RuleId, BallType BallType, int[] KilledBalls);
public sealed record FrozenKillPrediction(string Id, int TargetPeriod, DateTime TargetDate, DateTime FrozenAtUtc,
    int SourceThroughPeriod, string SourceHash, string RuleSetHash, KillRuleDefinition[] Rules,
    FrozenRulePrediction[] Predictions, string PreviousHash, string Hash);
public sealed record KillSettlement(string PredictionId, DateTime SettledAtUtc, int Period, DateTime DrawDate,
    int[] Reds, int Blue, string PreviousHash, string Hash);
public sealed class KillForwardLedger
{
    public int Version { get; init; } = 1;
    public List<FrozenKillPrediction> Predictions { get; init; } = new();
    public List<KillSettlement> Settlements { get; init; } = new();
}

/// <summary>Local append-only-by-API audit log. Hash chains detect accidental edits, not malicious rewriting.</summary>
public sealed class KillForwardStore
{
    private readonly string _path;
    private readonly Func<DateTime> _utcNow;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, IgnoreReadOnlyProperties = true };
    private static readonly TimeZoneInfo China = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
    public KillForwardStore() : this(Path.Combine(AppPaths.DataDirectory, "kill_forward_validation.json")) { }
    internal KillForwardStore(string path, Func<DateTime>? utcNow = null) { _path = path; _utcNow = utcNow ?? (() => DateTime.UtcNow); }

    public FrozenKillPrediction Freeze(IEnumerable<IKillRule> selectedRules, IReadOnlyList<DrawRecord> history,
        int targetPeriod, DateTime targetDate, IRuleExecutor executor, CancellationToken ct = default)
    {
        var records = KillResearchService.SnapshotRecords(history);
        if (records.Count == 0) throw new InvalidOperationException("没有历史数据，无法冻结。");
        DateTime now = _utcNow(), today = TimeZoneInfo.ConvertTimeFromUtc(now, China).Date;
        targetDate = targetDate.Date;
        // Conservative cutoff: only later calendar days are accepted, never today's draw.
        if (targetDate <= today || targetDate > today.AddDays(10) || records[^1].DrawDate.Date < today.AddDays(-7)
            || records[^1].DrawDate.Date > today || targetDate <= records[^1].DrawDate.Date)
            throw new InvalidOperationException("仅允许提前至少一个自然日冻结未来 10 日内的开奖；请先更新最近一周的历史数据。");
        int expected = targetDate.Year > records[^1].DrawDate.Year ? targetDate.Year * 1000 + 1 : records[^1].Period + 1;
        if (targetPeriod != expected || targetPeriod / 1000 != targetDate.Year || records.Any(r => r.Period >= targetPeriod))
            throw new InvalidOperationException("只能冻结历史之后紧接的一期，跨年期号必须从 001 开始。");
        var definitions = selectedRules.Select(KillRuleDefinition.Capture).ToArray();
        if (definitions.Length == 0 || definitions.Select(r => r.RuleId).Distinct().Count() != definitions.Length)
            throw new InvalidOperationException("请选择至少一条规则，且规则 ID 不得重复。");
        var context = new RuleContextBuilder().BuildForTarget(records, targetPeriod, targetDate);
        var predictions = new List<FrozenRulePrediction>();
        foreach (var definition in definitions)
        {
            ct.ThrowIfCancellationRequested();
            var result = executor.Execute(definition.ToRule(), context);
            predictions.Add(new(definition.RuleId, definition.BallType, result.KilledBalls.Distinct().Order().ToArray()));
        }
        ct.ThrowIfCancellationRequested();
        using var gate = Acquire();
        var ledger = Load();
        if (ledger.Predictions.Any(p => p.TargetPeriod == targetPeriod))
            throw new InvalidOperationException("该期已有冻结记录，不能替换规则、重选结果或覆盖。请查看已有记录。");
        if (TimeZoneInfo.ConvertTimeFromUtc(_utcNow(), China).Date >= targetDate) throw new InvalidOperationException("已过冻结截止时间。");
        var entry = new FrozenKillPrediction(Guid.NewGuid().ToString("N"), targetPeriod, targetDate, now, records[^1].Period,
            KillRuleDefinition.DataHash(records), KillRuleDefinition.RulesHash(definitions), definitions, predictions.ToArray(),
            ledger.Predictions.LastOrDefault()?.Hash ?? "", "");
        entry = entry with { Hash = PredictionHash(entry) };
        ledger.Predictions.Add(entry);
        ct.ThrowIfCancellationRequested();
        Save(ledger);
        return entry;
    }

    public int Settle(IReadOnlyList<DrawRecord> history, CancellationToken ct = default)
    {
        var records = KillResearchService.SnapshotRecords(history);
        using var gate = Acquire();
        var ledger = Load();
        int added = 0;
        foreach (var prediction in ledger.Predictions)
        {
            ct.ThrowIfCancellationRequested();
            var actual = records.SingleOrDefault(r => r.Period == prediction.TargetPeriod);
            if (actual is null) continue;
            if (actual.DrawDate.Date != prediction.TargetDate.Date || KillRuleDefinition.DataHash(records.Where(r => r.Period <= prediction.SourceThroughPeriod)) != prediction.SourceHash)
                throw new InvalidOperationException($"第 {prediction.TargetPeriod} 期的开奖日期或冻结时历史发生变化，需要人工核对，未覆盖原记录。");
            var old = ledger.Settlements.SingleOrDefault(s => s.PredictionId == prediction.Id);
            if (old is not null)
            {
                if (!old.Reds.SequenceEqual(actual.RedBalls) || old.Blue != actual.BlueBall)
                    throw new InvalidOperationException($"第 {actual.Period} 期结算后号码发生变化，已停止自动结算，请核对数据。");
                continue;
            }
            if (TimeZoneInfo.ConvertTimeFromUtc(_utcNow(), China).Date < actual.DrawDate.Date)
                throw new InvalidOperationException("历史包含尚未到开奖日期的数据，不能提前结算。");
            var settlement = new KillSettlement(prediction.Id, _utcNow(), actual.Period, actual.DrawDate.Date,
                actual.RedBalls.ToArray(), actual.BlueBall, ledger.Settlements.LastOrDefault()?.Hash ?? "", "");
            ledger.Settlements.Add(settlement with { Hash = SettlementHash(settlement) });
            added++;
        }
        ct.ThrowIfCancellationRequested();
        if (added > 0) Save(ledger);
        return added;
    }

    public string ToText(CancellationToken ct = default)
    {
        KillForwardLedger ledger;
        using (Acquire()) ledger = Load();
        var sb = new StringBuilder("开奖前冻结验证记录\n每期仅允许一次冻结；结算不重新运行规则。按规则版本分别汇总，不混合不同策略。\n");
        sb.AppendLine("仅在开奖日前一日或更早冻结；本机时间和本地数据不是第三方时间戳。哈希用于发现文件损坏，不保证抵御人为重写。\n");
        sb.AppendLine($"已冻结 {ledger.Predictions.Count} 期，已结算 {ledger.Settlements.Count} 期，待开奖 {ledger.Predictions.Count - ledger.Settlements.Count} 期。");
        foreach (var prediction in ledger.Predictions.OrderByDescending(p => p.TargetPeriod))
        {
            var result = ledger.Settlements.SingleOrDefault(s => s.PredictionId == prediction.Id);
            sb.AppendLine($"{prediction.TargetPeriod} / {prediction.TargetDate:yyyy-MM-dd} / 冻结 {prediction.FrozenAtUtc:yyyy-MM-dd HH:mm:ss} UTC / {(result is null ? "待结算" : "已结算")} / 规则版本 {prediction.RuleSetHash[..12]}");
            foreach (var ball in new[] { BallType.Red, BallType.Blue })
                sb.AppendLine($"  {(ball == BallType.Red ? "红球" : "蓝球")}冻结杀号：{string.Join(", ", prediction.Predictions.Where(p => p.BallType == ball).SelectMany(p => p.KilledBalls).Distinct().Order())}");
            if (result is not null) sb.AppendLine($"  开奖：{string.Join(", ", result.Reds)} + {result.Blue}");
        }
        foreach (var group in ledger.Predictions.GroupBy(p => p.RuleSetHash))
        {
            ct.ThrowIfCancellationRequested();
            var settled = group.Select(p => (Prediction: p, Result: ledger.Settlements.SingleOrDefault(s => s.PredictionId == p.Id)))
                .Where(p => p.Result is not null).ToArray();
            if (settled.Length == 0) continue;
            sb.AppendLine($"\n规则版本 {group.Key[..12]}，结算 {settled.Length} 期");
            int comparisons = (group.First().Rules.Length + 2) * Math.Max(1, ledger.Predictions.Select(p => p.RuleSetHash).Distinct().Count());
            foreach (var ball in new[] { BallType.Red, BallType.Blue })
            {
                var periods = settled.Select(p => Observation(p.Prediction.Predictions.Where(r => r.BallType == ball).SelectMany(r => r.KilledBalls), p.Result!, ball));
                sb.AppendLine($"合并{(ball == BallType.Red ? "红球" : "蓝球")}：{KillMetricText.Format(KillEvaluationMetrics.Compute(periods, ball, comparisons, ct))}");
            }
            foreach (var definition in group.First().Rules)
            {
                var periods = settled.Select(p => Observation(p.Prediction.Predictions.Single(r => r.RuleId == definition.RuleId).KilledBalls, p.Result!, definition.BallType));
                sb.AppendLine($"{definition.Name}：{KillMetricText.Format(KillEvaluationMetrics.Compute(periods, definition.BallType, comparisons, ct))}");
            }
        }
        sb.AppendLine("\n" + KillMetricText.MethodNotes);
        return sb.ToString();
    }

    private static KillEvaluationPeriod Observation(IEnumerable<int> killed, KillSettlement actual, BallType ball)
    {
        var set = killed.ToHashSet(); var balls = ball == BallType.Red ? actual.Reds : new[] { actual.Blue };
        return new(actual.Period, set.Count, set.Count(balls.Contains));
    }
    private FileStream Acquire()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        try { return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("验证记录正在由另一窗口或进程操作，请稍后重试。", ex); }
    }
    private KillForwardLedger Load()
    {
        if (!File.Exists(_path)) return new();
        var ledger = JsonSerializer.Deserialize<KillForwardLedger>(File.ReadAllText(_path), JsonOptions)
            ?? throw new InvalidDataException("验证记录为空或损坏，禁止覆盖。");
        if (ledger.Version != 1 || ledger.Predictions.Select(p => p.Id).Distinct().Count() != ledger.Predictions.Count
            || ledger.Predictions.Select(p => p.TargetPeriod).Distinct().Count() != ledger.Predictions.Count
            || ledger.Settlements.Select(p => p.PredictionId).Distinct().Count() != ledger.Settlements.Count)
            throw new InvalidDataException("验证记录版本或唯一性检查失败，禁止覆盖。");
        string previous = "";
        foreach (var p in ledger.Predictions)
        {
            if (p.PreviousHash != previous || p.Hash != PredictionHash(p) || p.RuleSetHash != KillRuleDefinition.RulesHash(p.Rules))
                throw new InvalidDataException("冻结记录完整性校验失败，禁止覆盖。");
            previous = p.Hash;
        }
        previous = "";
        foreach (var s in ledger.Settlements)
        {
            if (s.PreviousHash != previous || s.Hash != SettlementHash(s) || !ledger.Predictions.Any(p => p.Id == s.PredictionId && p.TargetPeriod == s.Period))
                throw new InvalidDataException("结算记录完整性校验失败，禁止覆盖。");
            previous = s.Hash;
        }
        return ledger;
    }
    private static string PredictionHash(FrozenKillPrediction value) => KillRuleDefinition.Hash(JsonSerializer.Serialize(value with { Hash = "" }, JsonOptions));
    private static string SettlementHash(KillSettlement value) => KillRuleDefinition.Hash(JsonSerializer.Serialize(value with { Hash = "" }, JsonOptions));
    private void Save(KillForwardLedger ledger)
    {
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(ledger, JsonOptions)); File.Move(temporary, _path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
