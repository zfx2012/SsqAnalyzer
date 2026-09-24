using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

/// <summary>Read-only replay of fixed definitions. No fitting, selection, or repository writes.</summary>
internal static class BuiltinQualityAudit
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    internal sealed record Score(int Evaluated, int Triggered, int Failed, int Killed, int WrongBalls, int WrongPeriods)
    {
        public double? Accuracy => Killed == 0 ? null : 1d - (double)WrongBalls / Killed;
        public double TriggerRate => Evaluated == 0 ? 0 : (double)Triggered / Evaluated;
        public double? PreserveRate => Triggered == 0 ? null : 1d - (double)WrongPeriods / Triggered;
    }
    internal sealed record Pair(string A, string B, string Ball, int EitherActive, int BothActive, int SameOutputs,
        int SharedWrongPeriods, double SameAmongEither, double SameAmongBoth);
    internal sealed record RuleAudit(string Id, string Name, string Ball, string Fingerprint, string Description,
        Score All, Score Recent500, Score Recent100, Score Last50Triggers, int? Last50FirstPeriod, int Last50SpanDraws,
        KillEvaluationMetrics Metrics, int UniqueKills500, int UniqueWrong500, int FirstBallSelections,
        string[] Flags, string[] Errors);
    internal sealed record Combined(string Range, int Draws, double AverageRedRemaining, double AverageBlueRemaining,
        int RedTarget, int BlueTarget, int JointTarget, double AverageRetainedReds, int RedFullyRetained,
        int BlueRetained, int BothFullyRetained, int TargetAndRedFull, int TargetAndBlueFull, int TargetAndBothFull,
        double? TargetAverageRetainedReds, double RandomExpectedBothFull, double? TargetRandomExpectedBothFull,
        int RedUnder6, int BlueZero, int FailedDraws);

    public static void Run(string dataPath, string directory)
    {
        Directory.CreateDirectory(directory);
        var history = KillResearchService.SnapshotRecords(File.ReadLines(dataPath).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l =>
        {
            var p = l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return new DrawRecord { Period = int.Parse(p[0]), DrawDate = DateTime.Parse(p[1]), RedBalls = p.Skip(2).Take(6).Select(int.Parse).ToArray(), BlueBall = int.Parse(p[8]) };
        }));
        if (history.Count < 501) throw new ArgumentException("Quality audit requires at least 501 chronological draws.", nameof(dataPath));
        var definitions = BuiltinRules.LoadAll().Select(KillRuleDefinition.Capture).ToArray();
        if (definitions.Length != 100) throw new InvalidOperationException("Expected fixed 100-rule catalog.");
        var rules = definitions.Select(d => d.ToRule()).ToArray();
        var descriptions = BuiltinRules.LoadAll().ToDictionary(r => r.RuleId, r => r.Description);
        int n = history.Count, recentStart = Math.Max(1, n - 500);
        var actualR = history.Select(r => Mask(r.RedBalls)).ToArray(); var actualB = history.Select(r => Mask(new[] { r.BlueBall })).ToArray();
        var builder = new RuleContextBuilder(); var miss = MissMatrixCalculator.Compute(history, history);
        var contexts = Enumerable.Range(0, n).Select(i => builder.BuildWithMiss(history, i, miss)).ToArray();
        var executor = new JintRuleExecutor();
        var masks = new ulong[100][]; var failures = new bool[100][]; var errors = new List<string>[100];
        int independentComparisons = 0;
        for (int r = 0; r < rules.Length; r++)
        {
            masks[r] = new ulong[n]; failures[r] = new bool[n]; errors[r] = new();
            for (int i = 1; i < n; i++)
            {
                try { masks[r][i] = Mask(executor.Execute(rules[r], contexts[i]).KilledBalls); }
                catch (RuleExecutionException ex) { failures[r][i] = true; errors[r].Add($"{history[i].Period}: {ex.Message}"); }
                if (rules[r].RuleId.StartsWith("B-S-", StringComparison.Ordinal) && !failures[r][i])
                {
                    var p = rules[r].Params;
                    var spec = new BuiltinRuleExpansion.Spec(((JsonElement)p["family"]).GetInt32(), ((JsonElement)p["window"]).GetInt32(), rules[r].BallType);
                    int expected = BuiltinRuleExpansion.Predict(spec, history, i);
                    if (masks[r][i] != (expected == 0 ? 0UL : 1UL << (expected - 1))) throw new InvalidOperationException($"Independent mismatch {rules[r].RuleId}/{history[i].Period}");
                    independentComparisons++;
                }
            }
            Console.WriteLine($"Replay {r + 1}/100 {rules[r].RuleId}: errors {errors[r].Count}");
        }
        // Rebuild contexts after changing every future draw's balls; same target metadata is preserved.
        int futureChecks = 0;
        foreach (int index in new[] { Math.Min(150, n - 1), n / 2, n - 2, n - 1 }.Distinct())
        {
            var changed = KillResearchService.SnapshotRecords(history);
            for (int i = index; i < n; i++) { changed[i].RedBalls = new[] { 1, 2, 3, 4, 5, 6 }; changed[i].BlueBall = 16; }
            var alternate = builder.BuildWithMiss(changed, index, MissMatrixCalculator.Compute(changed, changed));
            foreach (int r in Enumerable.Range(0, 100).Where(r => !failures[r][index]))
            {
                if (Mask(executor.Execute(rules[r], alternate).KilledBalls) != masks[r][index]
                    || Mask(executor.Execute(rules[r], contexts[index]).KilledBalls) != masks[r][index])
                    throw new InvalidOperationException($"Future/repeat mismatch {rules[r].RuleId}/{history[index].Period}");
                futureChecks++;
            }
        }
        var audits = new List<RuleAudit>();
        for (int r = 0; r < 100; r++)
        {
            var actual = rules[r].BallType == BallType.Red ? actualR : actualB;
            var triggered = Enumerable.Range(1, n - 1).Where(i => masks[r][i] != 0 && !failures[r][i]).TakeLast(50).ToArray();
            Score Summarize(IEnumerable<int> indices)
            {
                var values = indices.ToArray(); var valid = values.Where(i => !failures[r][i]).ToArray();
                return new(values.Length, valid.Count(i => masks[r][i] != 0), values.Length - valid.Length,
                    valid.Sum(i => Pop(masks[r][i])), valid.Sum(i => Pop(masks[r][i] & actual[i])), valid.Count(i => (masks[r][i] & actual[i]) != 0));
            }
            var all = Summarize(Enumerable.Range(1, n - 1)); var recent = Summarize(Enumerable.Range(recentStart, n - recentStart));
            var last50 = Summarize(triggered); int span = triggered.Length == 0 ? 0 : n - triggered[0];
            int unique = 0, uniqueWrong = 0;
            for (int i = recentStart; i < n; i++)
            {
                ulong others = 0;
                for (int j = 0; j < 100; j++) if (j != r && rules[j].BallType == rules[r].BallType) others |= masks[j][i];
                var only = masks[r][i] & ~others; unique += Pop(only); uniqueWrong += Pop(only & actual[i]);
            }
            var metrics = KillEvaluationMetrics.Compute(Enumerable.Range(1, n - 1).Select(i => new KillEvaluationPeriod(history[i].Period, Pop(masks[r][i]), Pop(masks[r][i] & actual[i]), failures[r][i])), rules[r].BallType, 100);
            var flags = new List<string>(); double baseline = BuiltinRules.RandomKillAccuracy(rules[r].BallType);
            if (errors[r].Count > 0) flags.Add("执行异常");
            if (recent.TriggerRate < .10) flags.Add("近500期触发率低于10%");
            if (span > 500) flags.Add("最近50次触发跨度超过500期");
            if (all.Accuracy < baseline && recent.Accuracy < baseline && recent.Triggered >= 50) flags.Add("全历史及近500期均低于随机单号基准");
            if (last50.Triggered < 50 || last50.Accuracy < KillSettings.For(rules[r].BallType)) flags.Add("当前近50次未达固定门槛");
            if (unique > 0 && (double)(unique - uniqueWrong) / unique < baseline) flags.Add("独有排除部分低于随机基准（描述性）");
            if (flags.Count == 0) flags.Add("保留观察，尚非独立验证通过");
            audits.Add(new(rules[r].RuleId, rules[r].Name, rules[r].BallType.ToString(), definitions[r].Fingerprint, descriptions[rules[r].RuleId],
                all, recent, Summarize(Enumerable.Range(Math.Max(1, n - 100), Math.Min(100, n - 1))), last50,
                triggered.Length == 0 ? null : history[triggered[0]].Period, span, metrics, unique, uniqueWrong,
                Enumerable.Range(recentStart, n - recentStart).Count(i => (masks[r][i] & 1UL) != 0), flags.ToArray(), errors[r].ToArray()));
        }
        var pairs = new List<Pair>();
        for (int a = 0; a < 100; a++) for (int b = a + 1; b < 100; b++)
        {
            if (rules[a].BallType != rules[b].BallType) continue;
            int either = 0, both = 0, same = 0, sharedWrong = 0; var actual = rules[a].BallType == BallType.Red ? actualR : actualB;
            for (int i = recentStart; i < n; i++)
            {
                if (failures[a][i] || failures[b][i]) continue;
                if (masks[a][i] != 0 || masks[b][i] != 0) either++;
                if (masks[a][i] != 0 && masks[b][i] != 0) { both++; if (masks[a][i] == masks[b][i]) same++; }
                if ((masks[a][i] & actual[i]) != 0 && (masks[b][i] & actual[i]) != 0) sharedWrong++;
            }
            pairs.Add(new(rules[a].RuleId, rules[b].RuleId, rules[a].BallType.ToString(), either, both, same, sharedWrong,
                either == 0 ? 0 : (double)same / either, both == 0 ? 0 : (double)same / both));
        }
        Combined Combine(string range, int start)
        {
            int count = n - start, redTarget = 0, blueTarget = 0, joint = 0, redFull = 0, blueFull = 0, full = 0, targetRed = 0, targetBlue = 0, targetFull = 0, redUnder = 0, blueZero = 0, failed = 0;
            double remainingR = 0, remainingB = 0, keptR = 0, targetKeptR = 0, randomFull = 0, targetRandomFull = 0;
            for (int i = start; i < n; i++)
            {
                if (Enumerable.Range(0, 100).Any(r => failures[r][i])) { failed++; continue; }
                ulong red = 0, blue = 0;
                for (int r = 0; r < 100; r++) { if (rules[r].BallType == BallType.Red) red |= masks[r][i]; else blue |= masks[r][i]; }
                int nr = 33 - Pop(red), nb = 16 - Pop(blue), hits = Pop(red & actualR[i]);
                bool rt = nr is >= 6 and <= 15, bt = nb is >= 1 and <= 5, rf = hits == 0, bf = (blue & actualB[i]) == 0;
                double random = Choose(nr, 6) / Choose(33, 6) * nb / 16d;
                remainingR += nr; remainingB += nb; keptR += 6 - hits; randomFull += random;
                if (rt) redTarget++; if (bt) blueTarget++; if (rf) redFull++; if (bf) blueFull++; if (rf && bf) full++;
                if (nr < 6) redUnder++; if (nb == 0) blueZero++;
                if (rt && bt) { joint++; targetKeptR += 6 - hits; targetRandomFull += random; if (rf) targetRed++; if (bf) targetBlue++; if (rf && bf) targetFull++; }
            }
            int valid = count - failed;
            return new(range, count, remainingR / valid, remainingB / valid, redTarget, blueTarget, joint, keptR / valid, redFull, blueFull, full,
                targetRed, targetBlue, targetFull, joint == 0 ? null : targetKeptR / joint, randomFull, joint == 0 ? null : targetRandomFull, redUnder, blueZero, failed);
        }
        var combined = new[] { Combine("历史（跳过前120个开奖点）", 120), Combine("近500个实际开奖期", recentStart), Combine("近100个实际开奖期", Math.Max(1, n - 100)) };
        var result = new { GeneratedAtUtc = DateTime.UtcNow, Source = Path.GetFullPath(dataPath), Count = n, First = history[0].Period, Last = history[^1].Period,
            DataHash = KillRuleDefinition.DataHash(history), RuleSetHash = KillRuleDefinition.RulesHash(definitions), IndependentComparisons = independentComparisons,
            FutureAndRepeatChecks = futureChecks, Rules = audits, Combined = combined, Pairs = pairs.OrderByDescending(p => p.SameAmongEither).ToArray() };
        File.WriteAllText(Path.Combine(directory, "quality-audit.json"), JsonSerializer.Serialize(result, Json));
        File.WriteAllText(Path.Combine(directory, "fixed-definitions.json"), JsonSerializer.Serialize(definitions, new JsonSerializerOptions(Json) { IgnoreReadOnlyProperties = true }));
        File.WriteAllText(Path.Combine(directory, "replay.json"), JsonSerializer.Serialize(new { Periods = history.Select(r => r.Period), RuleIds = rules.Select(r => r.RuleId), Masks = masks, Failures = failures }, Json));
        var md = new StringBuilder("# 100 条内置杀号规则质量排查\n\n");
        md.AppendLine($"数据：{history[0].Period}—{history[^1].Period}，共 {n} 期。规则指纹：`{result.RuleSetHash}`。\n");
        md.AppendLine("固定当前规则逐期重放，不改参数、不启停规则、不写回统计。以下历史结果都属于回顾性排查；近期成绩曾参与规则挑选，不能当作独立验证。近500期指500个实际开奖点，近50次指50次触发，两者不可混用。\n");
        md.AppendLine($"执行异常总数：{audits.Sum(r => r.All.Failed)}。新增50条与独立C#实现逐点比较：{independentComparisons} 次；未来开奖改动与重复执行检查：{futureChecks} 组（每组两次复算）。\n");
        md.AppendLine("## 组合目标检查\n\n目标固定为红球剩余6—15个、蓝球剩余1—5个；不强行补删号码。完整保留是实际6红+1蓝均在候选集内。\n");
        md.AppendLine("|范围|开奖期数|平均剩余红/蓝|同时达到数量目标|平均保留实际红球|红球完整保留|蓝球保留|红蓝完整保留|数量达标且红蓝完整保留|\n|---|---:|---|---:|---:|---:|---:|---:|---:|");
        foreach (var c in combined) md.AppendLine($"|{c.Range}|{c.Draws}|{c.AverageRedRemaining:F2} / {c.AverageBlueRemaining:F2}|{c.JointTarget}|{c.AverageRetainedReds:F2}/6|{c.RedFullyRetained}|{c.BlueRetained}|{c.BothFullyRetained}|{c.TargetAndBothFull}|");
        md.AppendLine("\n## 逐条规则结果\n\n随机单号排除基准：红球27/33=81.82%，蓝球15/16=93.75%。这不是效果承诺。‘低于基准’仅描述样本；独有排除指只有该规则排除的号码。所有标记均为复核优先级，未自动淘汰。\n");
        md.AppendLine("|ID / 规则|近500期触发|全历史正确率|近500期正确率|近50次正确率|50次跨度（开奖期数）|独有排除/错杀（近500期）|标记|\n|---|---:|---:|---:|---:|---:|---:|---|");
        foreach (var r in audits) md.AppendLine($"|{r.Id} {r.Name}|{r.Recent500.Triggered}/{r.Recent500.Evaluated}|{P(r.All.Accuracy)}|{P(r.Recent500.Accuracy)}|{P(r.Last50Triggers.Accuracy)}|{r.Last50SpanDraws}|{r.UniqueKills500}/{r.UniqueWrong500}|{string.Join("；", r.Flags)}|");
        md.AppendLine("\n## 输出重复候选\n\n近500期，同球种至少30期共同触发；两者至少一个触发时，完全相同输出比例≥80%。另列共同触发时相同比例≥85%的条件相关对。分母排除双方都不触发，避免稀疏规则被误判为一致。\n");
        md.AppendLine("|规则A|规则B|任一触发期数|共同触发期数|相同输出/任一触发|相同输出/共同触发|同时错杀期数|\n|---|---|---:|---:|---:|---:|---:|");
        foreach (var p in pairs.Where(p => p.BothActive >= 30 && (p.SameAmongEither >= .8 || p.SameAmongBoth >= .85)).OrderByDescending(p => p.SameAmongEither))
            md.AppendLine($"|{p.A}|{p.B}|{p.EitherActive}|{p.BothActive}|{p.SameAmongEither:P1}|{p.SameAmongBoth:P1}|{p.SharedWrongPeriods}|");
        md.AppendLine("\n## 统计限制\n\nquality-audit.json保留抽样不确定性、随机对照和重复对，replay.json另存逐期输出。历史组合统一跳过前120个开奖点，不表示历史同期等长跨度规则已充分预热。随机对照采用程序既有按期超几何模拟及区块自助法，100条规则作比较数；未覆盖历史调参时全部尝试，因此即便出现探索性差异，也不是独立优势证明。固定较早/较晚窗口均已被规则筛选过程间接使用，不称为留出验证。\n");
        File.WriteAllText(Path.Combine(directory, "quality-audit.md"), md.ToString(), new UTF8Encoding(true));
        Console.WriteLine(JsonSerializer.Serialize(new { result.Count, result.Last, Errors = audits.Sum(r => r.All.Failed), LowFrequency = audits.Count(r => r.Recent500.TriggerRate < .1), WideLast50 = audits.Count(r => r.Last50SpanDraws > 500), BelowBaselineBoth = audits.Count(r => r.Flags.Contains("全历史及近500期均低于随机单号基准")), StrongPairs = pairs.Count(p => p.BothActive >= 30 && p.SameAmongEither >= .8), ConditionalPairs = pairs.Count(p => p.BothActive >= 30 && p.SameAmongBoth >= .85), Combined = combined }, Json));
    }
    private static string P(double? value) => value is null ? "—" : value.Value.ToString("P2");
    private static ulong Mask(IEnumerable<int> balls) { ulong mask = 0; foreach (int b in balls) mask |= 1UL << (b - 1); return mask; }
    private static int Pop(ulong value) => BitOperations.PopCount(value);
    private static double Choose(int n, int k) { if (n < k) return 0; double result = 1; for (int i = 1; i <= k; i++) result *= (double)(n - k + i) / i; return result; }
}
