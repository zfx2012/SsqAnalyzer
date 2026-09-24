using System.IO;
using System.Text;
using System.Text.Json;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services.Kill;

public sealed record SubmittedKillRule(KillRuleDefinition Definition, int[] KilledBalls, string Reason, string? ExecutionError);
public sealed record KillSubmission(int TargetPeriod, DateTime TargetDate, DateTime GeneratedAtUtc, DateTime SubmittedAtUtc,
    int SourceThroughPeriod, string SourceHash, SubmittedKillRule[] Rules, string OriginalReport, string Hash);
public sealed record KillSubmissionReview(DateTime ReviewedAtUtc, DateTime DrawDate, int[] Reds, int Blue, string SubmissionHash, string Hash);
public sealed record KillSubmissionEntry(KillSubmission Submission, KillSubmissionReview? Review);
public sealed class KillSubmissionLedger
{
    public int Version { get; init; } = 1;
    public List<KillSubmissionEntry> Entries { get; init; } = new();
}
public sealed record KillReviewSyncResult(int Added, IReadOnlyDictionary<int, string> Issues);

/// <summary>Saves the displayed predictions, never re-executes rules. Local hashes detect accidental edits.</summary>
public sealed class KillSubmissionStore
{
    private readonly string _path;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IgnoreReadOnlyProperties = true };
    public KillSubmissionStore() : this(Path.Combine(AppPaths.DataDirectory, "kill_report_submissions.json")) { }
    internal KillSubmissionStore(string path, Func<DateTime>? utcNow = null)
    { _path = path; _utcNow = utcNow ?? (() => DateTime.UtcNow); }

    public IReadOnlyList<KillSubmissionEntry> Read()
    { lock (_gate) { using var lease = Acquire(); return Load().Entries.OrderByDescending(e => e.Submission.TargetPeriod).ToArray(); } }

    public bool Submit(KillReport report, IReadOnlyList<DrawRecord> history)
    {
        lock (_gate)
        {
            using var lease = Acquire();
            var ledger = Load();
            if (ledger.Entries.Any(e => e.Submission.TargetPeriod == report.TargetPeriod)) return false;
            var records = KillResearchService.SnapshotRecords(history);
            var now = _utcNow();
            if (records.Count == 0 || report.TargetDate is not { } targetDate
                || report.SourceThroughPeriod != records[^1].Period
                || report.SourceDataHash != KillRuleDefinition.DataHash(records)
                || report.TargetPeriod != KillDrawSchedule.NextPeriod(records[^1].Period, records[^1].DrawDate)
                || targetDate != KillDrawSchedule.NextDate(records[^1].DrawDate))
                throw new InvalidOperationException("报告与当前开奖数据不一致，请更新数据并重新生成报告。");
            if (KillDrawSchedule.ChinaTime(now) >= targetDate.AddHours(21)
                || report.GeneratedAt.Kind != DateTimeKind.Utc || report.GeneratedAt > now
                || KillDrawSchedule.ChinaTime(report.GeneratedAt) < records[^1].DrawDate.Date.AddHours(21).AddMinutes(15))
                throw new InvalidOperationException("已过本程序提交截止时间（预计开奖日北京时间 21:00），或报告时间无效。请更新开奖数据并重新生成报告。");
            if (report.RuleDefinitions.Count == 0 || report.RuleDefinitions.Count != report.Results.Count
                || report.RuleDefinitions.Select(d => d.RuleId).Distinct().Count() != report.RuleDefinitions.Count
                || report.Results.Select(r => r.RuleId).Distinct().Count() != report.Results.Count)
                throw new InvalidOperationException("报告规则快照不完整，无法提交。");
            var frozen = report.RuleDefinitions.Select(d =>
            {
                var result = report.Results.SingleOrDefault(r => r.RuleId == d.RuleId);
                if (result is null || result.BallType != d.BallType
                    || result.KilledBalls.Any(n => n < 1 || n > (d.BallType == BallType.Red ? 33 : 16))
                    || result.KilledBalls.Distinct().Count() != result.KilledBalls.Count
                    || (result.ExecutionError is not null && result.KilledBalls.Count != 0))
                    throw new InvalidOperationException("报告规则结果无效，无法提交。");
                return new SubmittedKillRule(d, result.KilledBalls.ToArray(), result.Reason, result.ExecutionError);
            }).ToArray();
            foreach (var type in new[] { BallType.Red, BallType.Blue })
            {
                var killed = frozen.Where(r => r.Definition.BallType == type).SelectMany(r => r.KilledBalls).Distinct().Order().ToArray();
                var displayed = (type == BallType.Red ? report.KilledRedBalls : report.KilledBlueBalls).Select(d => d.Ball).Order();
                var survivors = type == BallType.Red ? report.RecommendedRedBalls : report.RecommendedBlueBalls;
                if (!killed.SequenceEqual(displayed) || !Enumerable.Range(1, type == BallType.Red ? 33 : 16).Except(killed).SequenceEqual(survivors.Order()))
                    throw new InvalidOperationException("报告汇总与规则结果不一致，无法提交。");
            }
            var submission = new KillSubmission(report.TargetPeriod, targetDate, report.GeneratedAt, now,
                report.SourceThroughPeriod, report.SourceDataHash!, frozen, report.ToMarkdown(), "");
            submission = submission with { Hash = SubmissionHash(submission) };
            ledger.Entries.Add(new(submission, null));
            Save(ledger);
            return true;
        }
    }

    public KillReviewSyncResult Settle(IReadOnlyList<DrawRecord> history)
    {
        var records = KillResearchService.SnapshotRecords(history);
        lock (_gate)
        {
            using var lease = Acquire();
            var ledger = Load();
            var issues = new Dictionary<int, string>();
            int added = 0;
            for (int i = 0; i < ledger.Entries.Count; i++)
            {
                var entry = ledger.Entries[i]; var s = entry.Submission;
                var actual = records.SingleOrDefault(r => r.Period == s.TargetPeriod);
                if (KillRuleDefinition.DataHash(records.Where(r => r.Period <= s.SourceThroughPeriod)) != s.SourceHash)
                { issues[s.TargetPeriod] = "提交时的历史数据发生变化或缺失，请核对；保留原记录。"; continue; }
                if (actual is null)
                { if (entry.Review is not null) issues[s.TargetPeriod] = "已复盘的开奖数据目前缺失，保留原记录。"; continue; }
                if (entry.Review is { } old)
                {
                    if (old.DrawDate != actual.DrawDate.Date || old.Blue != actual.BlueBall || !old.Reds.SequenceEqual(actual.RedBalls))
                        issues[s.TargetPeriod] = "开奖结果与原复盘不同，请核对；未覆盖原复盘。";
                    continue;
                }
                var drawTime = actual.DrawDate.Date.AddHours(21).AddMinutes(15);
                if (KillDrawSchedule.ChinaTime(_utcNow()) < drawTime) continue;
                if (KillDrawSchedule.ChinaTime(s.SubmittedAtUtc) >= drawTime)
                { issues[s.TargetPeriod] = "提交时间晚于实际开奖时间，无法作为开奖前记录复盘。"; continue; }
                var review = new KillSubmissionReview(_utcNow(), actual.DrawDate.Date, actual.RedBalls.ToArray(), actual.BlueBall, s.Hash, "");
                review = review with { Hash = ReviewHash(review) };
                ledger.Entries[i] = entry with { Review = review }; added++;
            }
            if (added > 0) Save(ledger);
            return new(added, issues);
        }
    }

    public static int[] WrongBalls(KillSubmissionEntry entry, BallType type) => entry.Review is not { } review ? Array.Empty<int>()
        : entry.Submission.Rules.Where(r => r.Definition.BallType == type).SelectMany(r => r.KilledBalls)
            .Intersect(type == BallType.Red ? review.Reds : new[] { review.Blue }).Order().ToArray();
    public static string Status(KillSubmissionEntry entry) => entry.Review is null ? "待开奖复盘"
        : WrongBalls(entry, BallType.Red).Length + WrongBalls(entry, BallType.Blue).Length > 0 ? "有错杀"
        : entry.Submission.Rules.Any(r => r.ExecutionError is not null) ? "无错杀 / 有执行异常"
        : entry.Submission.Rules.All(r => r.KilledBalls.Length == 0) ? "未触发" : "无错杀";
    public static string ReviewText(KillSubmissionEntry entry)
    {
        var s = entry.Submission;
        var text = new StringBuilder($"第 {s.TargetPeriod} 期复盘 / 错误报告\n提交时间：{KillDrawSchedule.ChinaTime(s.SubmittedAtUtc):yyyy-MM-dd HH:mm:ss}（北京时间）\n状态：{Status(entry)}\n");
        if (entry.Review is not { } actual) return text.AppendLine("尚未获取该期有效开奖结果，更新开奖数据后自动复盘。").ToString();
        text.AppendLine($"开奖日期：{actual.DrawDate:yyyy-MM-dd}；复盘时间：{KillDrawSchedule.ChinaTime(actual.ReviewedAtUtc):yyyy-MM-dd HH:mm:ss}");
        if (s.TargetDate != actual.DrawDate) text.AppendLine($"预计日期 {s.TargetDate:yyyy-MM-dd} 与实际不同，本次按期号匹配实际开奖。");
        text.AppendLine($"开奖号码：红 {Numbers(actual.Reds)} / 蓝 {actual.Blue:D2}");
        var wrongRed = WrongBalls(entry, BallType.Red); var wrongBlue = WrongBalls(entry, BallType.Blue);
        text.AppendLine($"错杀红球：{Numbers(wrongRed)}；错杀蓝球：{Numbers(wrongBlue)}");
        text.AppendLine($"推荐集保留实际红球 {6 - wrongRed.Length}/6；实际蓝球{(wrongBlue.Length == 0 ? "保留" : "被排除")}。");
        text.AppendLine($"规则共 {s.Rules.Length} 条：触发 {s.Rules.Count(r => r.ExecutionError is null && r.KilledBalls.Length > 0)} 条，未触发 {s.Rules.Count(r => r.ExecutionError is null && r.KilledBalls.Length == 0)} 条，执行异常 {s.Rules.Count(r => r.ExecutionError is not null)} 条。");
        foreach (var type in new[] { BallType.Red, BallType.Blue })
        {
            var killed = s.Rules.Where(r => r.Definition.BallType == type).SelectMany(r => r.KilledBalls).Distinct().Order().ToArray();
            var wrong = type == BallType.Red ? wrongRed : wrongBlue;
            text.AppendLine($"{(type == BallType.Red ? "红" : "蓝")}球提交杀号 {Numbers(killed)}；共 {killed.Length} 个，正确排除 {killed.Length - wrong.Length} 个，错杀 {wrong.Length} 个（按号码去重）。");
        }
        text.AppendLine("\n逐条规则复盘（错杀优先）：");
        var rows = s.Rules.Select(r => new { Rule = r, Wrong = r.KilledBalls.Intersect(r.Definition.BallType == BallType.Red ? actual.Reds : new[] { actual.Blue }).Order().ToArray() });
        foreach (var row in rows.OrderByDescending(r => r.Wrong.Length).ThenBy(r => r.Rule.Definition.RuleId))
        {
            var r = row.Rule;
            var outcome = r.ExecutionError is not null ? $"执行异常：{r.ExecutionError}（不计成功）" : r.KilledBalls.Length == 0 ? "未触发（不计成功）"
                : row.Wrong.Length == 0 ? $"本期无错杀；正确排除 {r.KilledBalls.Length} 个" : $"错杀 {Numbers(row.Wrong)}；正确排除 {r.KilledBalls.Length - row.Wrong.Length} 个";
            text.AppendLine($"{r.Definition.Name} [{r.Definition.RuleId}] / {(r.Definition.BallType == BallType.Red ? "红" : "蓝")}：{outcome}");
            text.AppendLine($"  提交杀号：{Numbers(r.KilledBalls)}；当时依据：{r.Reason}");
        }
        text.AppendLine("\n错误总结：错杀号码已定位到上列具体规则；同一号码被多条规则排除，整体只计一次。单期结果不能证明错误原因或规则长期有效，不自动修改规则。");
        return text.ToString();
    }
    private static string Numbers(IEnumerable<int> numbers) { var values = numbers.Select(n => n.ToString("D2")).ToArray(); return values.Length == 0 ? "无" : string.Join(" ", values); }
    private FileStream Acquire()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        try { return new(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("提交记录正在使用，请稍后重试。", ex); }
    }
    private KillSubmissionLedger Load()
    {
        if (!File.Exists(_path)) return new();
        var ledger = JsonSerializer.Deserialize<KillSubmissionLedger>(File.ReadAllText(_path), Json)
            ?? throw new InvalidDataException("提交记录已损坏，禁止覆盖。");
        if (ledger.Version != 1 || ledger.Entries.Select(e => e.Submission.TargetPeriod).Distinct().Count() != ledger.Entries.Count
            || ledger.Entries.Any(e => e.Submission.Hash != SubmissionHash(e.Submission)
                || (e.Review is { } r && (r.SubmissionHash != e.Submission.Hash || r.Hash != ReviewHash(r)))))
            throw new InvalidDataException("提交记录完整性校验失败，禁止覆盖。请从备份恢复。");
        return ledger;
    }
    private static string SubmissionHash(KillSubmission s) => KillRuleDefinition.Hash(JsonSerializer.Serialize(s with { Hash = "" }, Json));
    private static string ReviewHash(KillSubmissionReview r) => KillRuleDefinition.Hash(JsonSerializer.Serialize(r with { Hash = "" }, Json));
    private void Save(KillSubmissionLedger ledger)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(ledger, Json)); File.Move(temporary, _path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
