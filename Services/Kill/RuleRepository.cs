using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 规则仓储默认实现（架构设计 §3.7）。
/// - 内置规则：从 <see cref="BuiltinRules"/> 静态加载（进程内单例缓存）
/// - 用户规则：持久化到 data/kill_rules_user.json（原子写 .tmp + File.Move，与 TicketStore 一致）
/// - 回测统计：用户规则持久化到同一文件 stats 字段；内置规则仅更新内存（§7.5）
/// - 内置规则启用状态覆盖：持久化到同一文件 builtinOverrides 字段
/// </summary>
public sealed class RuleRepository : IRuleRepository
{
    private static readonly string DataDir = AppPaths.DataDirectory;
    private static readonly string UserRulesPath = Path.Combine(DataDir, "kill_rules_user.json");

    // 内置规则（与 BuiltinRules 共享同一缓存引用，使 BacktestStats/IsEnabled 等可变字段在进程内一致）
    private readonly IReadOnlyList<KillRule> _builtinRules;
    // 用户规则（可变列表，增删后持久化）
    private readonly List<KillRule> _userRules;
    // 用户规则回测统计（按 ruleId 索引）
    private readonly Dictionary<string, BacktestStatsSnapshot> _userStats;
    // 内置规则回测统计（按 ruleId 索引，进程内挂到 KillRule.BacktestStats；持久化到 builtinOverrides[ruleId].backtestStats）
    private readonly Dictionary<string, BacktestStatsSnapshot> _builtinStats;
    private readonly object _stateLock = new();

    public event Action? RulesChanged;

    public RuleRepository()
    {
        _builtinRules = BuiltinRules.LoadAll();
        (_userRules, _userStats, _builtinStats) = LoadUserFile();
        // 把已持久化的回测统计挂回用户规则实例
        foreach (var rule in _userRules)
        {
            if (_userStats.TryGetValue(rule.RuleId, out var stat))
                rule.BacktestStats = stat;
        }
        // 把已持久化的回测统计挂回内置规则实例（启动时无需重新回测）
        foreach (var rule in _builtinRules)
        {
            if (_builtinStats.TryGetValue(rule.RuleId, out var stat))
                rule.BacktestStats = stat;
        }
        // 一次性旧 ID 迁移（B-GT/GS/GC/GE → B-G-R）；无旧 key 时立即返回
        RunBuiltinIdMigrationIfNeeded();
    }

    /// <summary>检测并执行一次性 ID 迁移：扫 JSON 输入的旧 builtinOverrides key，重命名为新 ID。
    /// 已无旧 ID 时立即返回（只在第一次启动迁移时执行一次）。</summary>
    public void RunBuiltinIdMigrationIfNeeded()
    {
        if (!File.Exists(UserRulesPath)) return;
        bool hasLegacy;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(UserRulesPath, Encoding.UTF8));
            hasLegacy = doc.RootElement.TryGetProperty("builtinOverrides", out var bo) && bo.ValueKind == JsonValueKind.Object
                && bo.EnumerateObject().Any(p => MigrateOldBuiltinRuleId(p.Name) != p.Name);
        }
        catch { return; }
        if (!hasLegacy) return;

        // 重命名 _builtinStats 字典 key
        var renamed = new Dictionary<string, BacktestStatsSnapshot>(_builtinStats.Count);
        foreach (var kv in _builtinStats)
            renamed[MigrateOldBuiltinRuleId(kv.Key)] = kv.Value;
        _builtinStats.Clear();
        foreach (var kv in renamed) _builtinStats[kv.Key] = kv.Value;

        PersistUserFile();
    }
    /// <inheritdoc />
    public IReadOnlyList<IKillRule> GetAll()
    {
        var result = new List<IKillRule>(_builtinRules.Count + _userRules.Count);
        foreach (var r in _builtinRules) result.Add(r);
        foreach (var r in _userRules) result.Add(r);
        return result;
    }

    /// <inheritdoc />
    public IKillRule? Find(string ruleId)
    {
        if (string.IsNullOrEmpty(ruleId)) return null;
        foreach (var r in _builtinRules)
            if (r.RuleId == ruleId) return r;
        foreach (var r in _userRules)
            if (r.RuleId == ruleId) return r;
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<IKillRule> GetEnabled()
    {
        var result = new List<IKillRule>();
        foreach (var r in _builtinRules)
            if (r.IsEnabled) result.Add(r);
        foreach (var r in _userRules)
            if (r.IsEnabled) result.Add(r);
        return result;
    }

    /// <inheritdoc />
    public void Add(IKillRule rule)
    {
        if (rule is null) throw new ArgumentNullException(nameof(rule));
        if (rule.IsBuiltin)
            throw new InvalidOperationException($"无法添加内置规则 {rule.RuleId}：内置规则不可通过 Add 新增");
        if (Find(rule.RuleId) is not null)
            throw new InvalidOperationException($"规则 {rule.RuleId} 已存在，无法重复添加");

        lock (_stateLock)
        {
            _userRules.Add(AsKillRule(rule));
            try
            {
                PersistUserFile();
            }
            catch
            {
                _userRules.RemoveAt(_userRules.Count - 1);
                throw;
            }
        }
        RulesChanged?.Invoke();
    }

    /// <inheritdoc />
    public void Update(IKillRule rule)
    {
        lock (_stateLock)
        {
        if (rule is null) throw new ArgumentNullException(nameof(rule));
        var existing = Find(rule.RuleId);
        if (existing is null)
            throw new InvalidOperationException($"规则 {rule.RuleId} 不存在，无法更新");

        if (existing is KillRule krExisting)
        {
            if (krExisting.IsBuiltin)
            {
                // 内置规则：仅允许改 IsEnabled/ForceEnabled（jsCode/params 等不可改）
                krExisting.IsEnabled = rule.IsEnabled;
                krExisting.ForceEnabled = rule.ForceEnabled;
                PersistUserFile();  // 持久化 builtinOverrides
            }
            else
            {
                // 用户规则：全字段更新（替换实例）
                var idx = _userRules.IndexOf(krExisting);
                if (idx >= 0)
                {
                    var newRule = AsKillRule(rule);
                    newRule.BacktestStats = rule.BacktestStats ?? krExisting.BacktestStats;
                    _userRules[idx] = newRule;
                    if (newRule.BacktestStats is not null)
                        _userStats[newRule.RuleId] = newRule.BacktestStats;
                    PersistUserFile();
                }
            }
        }
        }
        RulesChanged?.Invoke();
    }

    /// <inheritdoc />
    public void Delete(string ruleId)
    {
        lock (_stateLock)
        {
        if (string.IsNullOrEmpty(ruleId))
            throw new ArgumentException("ruleId 不能为空", nameof(ruleId));

        var existing = Find(ruleId);
        if (existing is null) return;
        if (existing.IsBuiltin)
            throw new InvalidOperationException($"无法删除内置规则 {ruleId}：内置规则不可删除");

        _userRules.Remove((KillRule)existing);
        _userStats.Remove(ruleId);
        PersistUserFile();
        }
        RulesChanged?.Invoke();
    }

    /// <inheritdoc />
    public void SaveBacktestStats(string ruleId, BacktestStatsSnapshot stats)
    {
        lock (_stateLock)
        {
        if (string.IsNullOrEmpty(ruleId))
            throw new ArgumentException("ruleId 不能为空", nameof(ruleId));
        if (stats is null) throw new ArgumentNullException(nameof(stats));

        var rule = Find(ruleId);
        if (rule is null) return;

        if (rule is KillRule kr)
            kr.BacktestStats = stats;

        // 用户规则：写 _userStats 段
        // 内置规则：写 _builtinStats 段（持久化到 builtinOverrides[ruleId].backtestStats，下次启动恢复，避免重复回测）
        if (rule.IsBuiltin)
            _builtinStats[ruleId] = stats;
        else
            _userStats[ruleId] = stats;
        PersistUserFile();
        }
    }

    /// <summary>
    /// 准确率门槛判定（架构 §3.5/§3.6 注解）：
    /// 若 stat.Accuracy &lt; 对应球种的固定门槛 且 !rule.ForceEnabled → rule.IsEnabled = false。
    /// 门槛判定使用最近完成的窗口；样本不足或执行失败不自动禁用。
    /// </summary>
    public void ApplyAccuracyGate(KillRule rule)
    {
        if (rule is null) throw new ArgumentNullException(nameof(rule));
        var stats = rule.BacktestStats;
        if (stats is null) return;  // 无回测数据，不判定

        var stat = stats.Effective;
        if (!stat.IsUsable) return;

        if (stat.Accuracy < KillSettings.For(rule.BallType) && !rule.ForceEnabled)
        {
            rule.IsEnabled = false;
        }
    }

    /// <summary>切换启用状态（便捷方法，等价于 Update 仅改 IsEnabled）。</summary>
    public void ToggleEnabled(string ruleId, bool enabled)
    {
        lock (_stateLock)
        {
        if (string.IsNullOrEmpty(ruleId))
            throw new ArgumentException("ruleId 不能为空", nameof(ruleId));
        var rule = Find(ruleId);
        if (rule is null)
            throw new InvalidOperationException($"规则 {ruleId} 不存在");
        if (rule is KillRule kr)
        {
            kr.IsEnabled = enabled;
            PersistUserFile();  // 用户规则与内置规则覆盖均持久化到同一文件
        }
        }
        RulesChanged?.Invoke();
    }

    // ===== 持久化 =====

    private (List<KillRule> rules, Dictionary<string, BacktestStatsSnapshot> userStats, Dictionary<string, BacktestStatsSnapshot> builtinStats) LoadUserFile()
    {
        var rules = new List<KillRule>();
        var stats = new Dictionary<string, BacktestStatsSnapshot>();
        var builtinStats = new Dictionary<string, BacktestStatsSnapshot>();
        if (!File.Exists(UserRulesPath)) return (rules, stats, builtinStats);

        try
        {
            var json = File.ReadAllText(UserRulesPath, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("userRules", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    try { rules.Add(KillRule.FromJson(item.GetRawText())); }
                    catch (Exception ex) { Debug.WriteLine($"[RuleRepository] 用户规则条目解析失败：{ex.Message}"); }
                }
            }
            if (root.TryGetProperty("stats", out var obj) && obj.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in obj.EnumerateObject())
                {
                    var stat = ParseStatsSnapshot(prop.Value);
                    if (stat is not null) stats[prop.Name] = stat;
                }
            }
            if (root.TryGetProperty("builtinOverrides", out var bo) && bo.ValueKind == JsonValueKind.Object)
            {
                ApplyBuiltinOverrides(bo, builtinStats);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RuleRepository] 加载用户规则文件失败：{ex.Message}");
        }
        return (rules, stats, builtinStats);
    }

    /// <summary>把内置规则的启用状态覆盖与回测统计应用到 BuiltinRules 缓存。
    private void ApplyBuiltinOverrides(JsonElement bo, Dictionary<string, BacktestStatsSnapshot> builtinStats)
    {
        foreach (var prop in bo.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            string effectiveId = MigrateOldBuiltinRuleId(prop.Name);
            KillRule? matched = null;
            foreach (var r in _builtinRules)
            {
                if (r.RuleId == effectiveId) { matched = r; break; }
            }
            if (matched is null) continue;
            if (prop.Value.TryGetProperty("isEnabled", out var en))
                matched.IsEnabled = en.GetBoolean();
            if (prop.Value.TryGetProperty("forceEnabled", out var fe))
                matched.ForceEnabled = fe.GetBoolean();
            if (prop.Value.TryGetProperty("backtestStats", out var bs) && bs.ValueKind == JsonValueKind.Object)
            {
                var stat = ParseStatsSnapshot(bs);
                if (stat is not null) builtinStats[effectiveId] = stat;
            }
        }
    }

    /// <summary>把旧版内置规则 ID 重命名为当前连续编号；变体后缀保持不变。</summary>
    private static string MigrateOldBuiltinRuleId(string oldId)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["B-GT-R-005"] = "B-G-R-001",
            ["B-G-R-013"] = "B-G-R-001",
            ["B-G-B-003"] = "B-G-B-001",
            ["B-G-R-014"] = "B-G-R-002",
            ["B-G-R-015"] = "B-G-R-003",
            ["B-G-R-016"] = "B-G-R-004",
            ["B-G-R-017"] = "B-G-R-005",
            ["B-G-R-018"] = "B-G-R-006",
            ["B-G-R-019"] = "B-G-R-007",
            ["B-G-R-020"] = "B-G-R-008",
            ["B-G-B-004"] = "B-G-B-002",
            ["B-G-R-021"] = "B-G-R-009",
            ["B-G-R-022"] = "B-G-R-010",
            ["B-G-R-023"] = "B-G-R-011",
            ["B-G-R-024"] = "B-G-R-012"
        };
        if (map.TryGetValue(oldId, out var currentId)) return currentId;
        foreach (var suffix in new[] { "-HS", "-OE", "-CY" })
        {
            if (!oldId.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var baseId = oldId[..^suffix.Length];
            if (map.TryGetValue(baseId, out currentId)) return currentId + suffix;
        }
        return oldId;
    }

    private static BacktestStatsSnapshot? ParseStatsSnapshot(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        try
        {
            var w30 = ParseWindowStat(el.GetProperty("window30"));
            var w50 = ParseWindowStat(el.GetProperty("window50"));
            var w100 = ParseWindowStat(el.GetProperty("window100"));
            var lastRunAt = el.TryGetProperty("lastRunAt", out var lr) && lr.ValueKind == JsonValueKind.String
                ? lr.GetDateTime() : DateTime.UtcNow;
            bool separate = el.TryGetProperty("formatVersion", out var version) && version.GetInt32() >= 2;
            return new BacktestStatsSnapshot(w30, w50, separate ? w100 : BacktestWindowStat.Empty, lastRunAt)
            {
                WindowAll = separate && el.TryGetProperty("windowAll", out var all) ? ParseWindowStat(all) : BacktestWindowStat.Empty,
                LastWindow = separate && el.TryGetProperty("lastWindow", out var last)
                    && Enum.TryParse<BacktestWindow>(last.GetString(), out var parsed) && Enum.IsDefined(parsed) ? parsed : null,
                LegacyCombinedWindow = separate
                    ? el.TryGetProperty("legacyCombinedWindow", out var legacy) ? ParseWindowStat(legacy) : null
                    : w100
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RuleRepository] 回测统计解析失败：{ex.Message}");
            return null;
        }
    }

    private static BacktestWindowStat ParseWindowStat(JsonElement el)
    {
        var stat = new BacktestWindowStat(
            TriggeredCount: el.GetProperty("triggeredCount").GetInt32(),
            KillBallCount: el.GetProperty("killBallCount").GetInt32(),
            CorrectBallCount: el.GetProperty("correctBallCount").GetInt32(),
            Accuracy: el.GetProperty("accuracy").GetDouble(),
            SampleInsufficient: el.GetProperty("killBallCount").GetInt32() == 0 || (el.TryGetProperty("sampleInsufficient", out var si) && si.GetBoolean())
        )
        {
            ElapsedMs = el.TryGetProperty("elapsedMs", out var elapsed) ? elapsed.GetInt64() : 0,
            RunAt = el.TryGetProperty("runAt", out var run) && run.ValueKind == JsonValueKind.String ? run.GetDateTime() : null,
            EvaluatedCount = el.TryGetProperty("evaluatedCount", out var evaluated) ? evaluated.GetInt32() : 0,
            FailureCount = el.TryGetProperty("failureCount", out var failed) ? failed.GetInt32() : 0,
            LastExecutionError = el.TryGetProperty("lastExecutionError", out var error) ? error.GetString() : null,
            FirstPeriod = el.TryGetProperty("firstPeriod", out var first) ? first.GetInt32() : null,
            LastPeriod = el.TryGetProperty("lastPeriod", out var end) ? end.GetInt32() : null
        };

        // errorSamples 为新增字段，旧 JSON 无此字段时 TryGetProperty 返回 false → 保持默认空列表
        if (el.TryGetProperty("errorSamples", out var esArr) && esArr.ValueKind == JsonValueKind.Array)
        {
            var samples = new List<BacktestErrorSample>();
            foreach (var item in esArr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                samples.Add(new BacktestErrorSample(
                    Period: item.GetProperty("period").GetInt32(),
                    KilledBalls: ReadIntArray(item, "killedBalls"),
                    ActualNextBalls: ReadIntArray(item, "actualNextBalls"),
                    HitBalls: ReadIntArray(item, "hitBalls")));
            }
            return stat with { ErrorSamples = samples };
        }
        return stat;
    }

    private static int[] ReadIntArray(JsonElement parent, string propName)
    {
        if (!parent.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<int>();
        var list = new List<int>();
        foreach (var n in arr.EnumerateArray())
            list.Add(n.GetInt32());
        return list.ToArray();
    }

    private void PersistUserFile()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", 1);

                // userRules：用户规则定义
                writer.WriteStartArray("userRules");
                foreach (var r in _userRules)
                {
                    var json = r.ToJson();
                    using var doc = JsonDocument.Parse(json);
                    doc.RootElement.WriteTo(writer);
                }
                writer.WriteEndArray();

                // stats：用户规则回测统计（内置规则不进此段）
                writer.WriteStartObject("stats");
                foreach (var kv in _userStats)
                {
                    writer.WritePropertyName(kv.Key);
                    WriteStatsSnapshot(writer, kv.Value);
                }
                writer.WriteEndObject();

                // builtinOverrides：内置规则启用状态覆盖 + 回测统计（持久化用于下次启动恢复，避免重复回测）
                writer.WriteStartObject("builtinOverrides");
                foreach (var r in _builtinRules)
                {
                    writer.WriteStartObject(r.RuleId);
                    writer.WriteBoolean("isEnabled", r.IsEnabled);
                    writer.WriteBoolean("forceEnabled", r.ForceEnabled);
                    if (_builtinStats.TryGetValue(r.RuleId, out var bstat))
                    {
                        writer.WritePropertyName("backtestStats");
                        WriteStatsSnapshot(writer, bstat);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();

                writer.WriteEndObject();
            }
            AtomicWrite(UserRulesPath, ms.ToArray());
        }
        catch (Exception ex)
        {
            RestorePersistedState();
            Debug.WriteLine($"[RuleRepository] 持久化用户规则失败：{ex.Message}");
            throw new IOException("规则数据保存失败，请检查安装目录是否可写", ex);
        }
    }

    private void RestorePersistedState()
    {
        try
        {
            var defaults = BuiltinRules.LoadAll().ToDictionary(r => r.RuleId);
            foreach (var rule in _builtinRules)
            {
                if (defaults.TryGetValue(rule.RuleId, out var original))
                {
                    rule.IsEnabled = original.IsEnabled;
                    rule.ForceEnabled = original.ForceEnabled;
                    rule.BacktestStats = original.BacktestStats;
                }
            }
            var loaded = LoadUserFile();
            _userRules.Clear();
            _userRules.AddRange(loaded.rules);
            _userStats.Clear();
            foreach (var item in loaded.userStats)
                _userStats[item.Key] = item.Value;
            _builtinStats.Clear();
            foreach (var item in loaded.builtinStats)
                _builtinStats[item.Key] = item.Value;

            foreach (var rule in _userRules)
                rule.BacktestStats = _userStats.TryGetValue(rule.RuleId, out var stat) ? stat : null;
            foreach (var rule in _builtinRules)
            {
                if (_builtinStats.TryGetValue(rule.RuleId, out var stat))
                    rule.BacktestStats = stat;
                else
                    rule.BacktestStats = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RuleRepository] 鍥炴粴鍐呭瓨鐘舵€佸け璐ワ細{ex.Message}");
        }
    }

    private static void WriteStatsSnapshot(Utf8JsonWriter writer, BacktestStatsSnapshot s)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("window30");
        WriteWindowStat(writer, s.Window30);
        writer.WritePropertyName("window50");
        WriteWindowStat(writer, s.Window50);
        writer.WritePropertyName("window100");
        WriteWindowStat(writer, s.Window100);
        writer.WriteNumber("formatVersion", 2);
        writer.WritePropertyName("windowAll");
        WriteWindowStat(writer, s.WindowAll);
        if (s.LastWindow is { } last) writer.WriteString("lastWindow", last.ToString());
        if (s.LegacyCombinedWindow is { } legacy)
        {
            writer.WritePropertyName("legacyCombinedWindow");
            WriteWindowStat(writer, legacy);
        }
        writer.WriteString("lastRunAt", s.LastRunAt);
        writer.WriteEndObject();
    }

    private static void WriteWindowStat(Utf8JsonWriter writer, BacktestWindowStat w)
    {
        writer.WriteStartObject();
        writer.WriteNumber("triggeredCount", w.TriggeredCount);
        writer.WriteNumber("killBallCount", w.KillBallCount);
        writer.WriteNumber("correctBallCount", w.CorrectBallCount);
        writer.WriteNumber("accuracy", w.Accuracy);
        writer.WriteBoolean("sampleInsufficient", w.SampleInsufficient);
        writer.WriteNumber("elapsedMs", w.ElapsedMs);
        if (w.RunAt is { } run) writer.WriteString("runAt", run);
        writer.WriteNumber("evaluatedCount", w.EvaluatedCount);
        writer.WriteNumber("failureCount", w.FailureCount);
        if (w.LastExecutionError is { } error) writer.WriteString("lastExecutionError", error);
        if (w.FirstPeriod is { } first) writer.WriteNumber("firstPeriod", first);
        if (w.LastPeriod is { } last) writer.WriteNumber("lastPeriod", last);

        // errorSamples：空列表也写出（保持新格式），旧版本读取时多余字段会被忽略
        writer.WriteStartArray("errorSamples");
        foreach (var s in w.ErrorSamples)
        {
            writer.WriteStartObject();
            writer.WriteNumber("period", s.Period);
            WriteIntArray(writer, "killedBalls", s.KilledBalls);
            WriteIntArray(writer, "actualNextBalls", s.ActualNextBalls);
            WriteIntArray(writer, "hitBalls", s.HitBalls);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WriteIntArray(Utf8JsonWriter writer, string propName, IReadOnlyList<int> arr)
    {
        writer.WriteStartArray(propName);
        foreach (var n in arr)
            writer.WriteNumberValue(n);
        writer.WriteEndArray();
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>把 IKillRule 转为可变的 KillRule 实例（若已是 KillRule 则直接返回）。</summary>
    private static KillRule AsKillRule(IKillRule rule)
    {
        if (rule is KillRule kr) return kr;
        return new KillRule
        {
            RuleId = rule.RuleId,
            Name = rule.Name,
            Category = rule.Category,
            SubCategory = rule.SubCategory,
            BallType = rule.BallType,
            IsBuiltin = rule.IsBuiltin,
            IsEnabled = rule.IsEnabled,
            IsVisible = rule.IsVisible,
            MinAccuracy = rule.MinAccuracy,
            ForceEnabled = rule.ForceEnabled,
            JsCode = rule.JsCode,
            Params = rule.Params.ToDictionary(kv => kv.Key, kv => kv.Value),
            Description = rule.Description,
            Tags = rule.Tags.ToList(),
            Source = rule.Source,
            CreatedAt = rule.CreatedAt,
            BacktestStats = rule.BacktestStats
        };
    }
}
