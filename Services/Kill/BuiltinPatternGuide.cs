namespace SsqAnalyzer.Services.Kill;

/// <summary>Human-readable geometry for current builtins. Sketches explain definitions, not real predictions.</summary>
internal sealed record BuiltinPatternGuide(string Kind, string Scope, string Shape, string Target,
    string Condition, string[] Columns, string[] RowLabels, string[] Cells)
{
    internal static BuiltinPatternGuide? For(KillRule rule)
    {
        var current = BuiltinRules.LoadAll().FirstOrDefault(r => r.RuleId == rule.RuleId);
        if (!rule.IsBuiltin || current is null
            || KillRuleDefinition.Capture(rule).Fingerprint != KillRuleDefinition.Capture(current).Fingerprint) return null;
        if (rule.RuleId.StartsWith("B-S-", StringComparison.Ordinal) && rule.Params.ContainsKey("geometryVersion"))
        {
            var offsets = System.Text.Json.JsonSerializer.Deserialize<int[]>(System.Text.Json.JsonSerializer.Serialize(rule.Params["offsets"]))!;
            int targetOffset = System.Text.Json.JsonSerializer.Deserialize<int>(System.Text.Json.JsonSerializer.Serialize(rule.Params["target"]));
            int min = offsets.Append(targetOffset).Min(), max = offsets.Append(targetOffset).Max();
            var shownColumns = max - min <= 6 ? Enumerable.Range(min, max - min + 1).ToArray() : offsets.Append(targetOffset).Distinct().Order().ToArray();
            string Column(int n) => n == 0 ? "n" : $"n{n:+0;-0}";
            var diagram = offsets.Select(offset => string.Concat(shownColumns.Select(n => n == offset ? '●' : '·')))
                .Append(string.Concat(shownColumns.Select(n => n == targetOffset ? '×' : '·'))).ToArray();
            return new("轨迹图形", "基本图：每行一个实际开奖期，从上到下由旧到新。",
                "连续各行依次出现 " + string.Join(" → ", offsets.Select(Column)) + "；同时识别左右镜像。其他位置不限制。" + (max-min > 6 ? "示意省略无关列，实际间距以列头数字为准。" : ""),
                "下一行 " + Column(targetOffset) + "；镜像取对应反向位置，越界舍弃，多个匹配合并去重。",
                "无频次、冷热或遗漏过滤；尚未前向验证，旧统计规则成绩不代表此版本。",
                shownColumns.Select(Column).ToArray(),
                Enumerable.Range(0, offsets.Length).Select(i => $"前{offsets.Length - i}行").Append("下一行").ToArray(), diagram);
        }
        if (!rule.RuleId.StartsWith("B-G-", StringComparison.Ordinal)) return null;
        string id = string.Join("-", rule.RuleId.Split('-').Take(4));
        string scope = rule.RuleId.EndsWith("-HS", StringComparison.Ordinal) ? "历史同期图：每行是往年相同期号，不是连续实际开奖。"
            : rule.RuleId.EndsWith("-OE", StringComparison.Ordinal) ? "奇偶图：只保留与目标期号同奇偶的开奖，每行是一个匹配期。"
            : rule.RuleId.EndsWith("-CY", StringComparison.Ordinal) ? "周期图：只保留与目标开奖日同星期的开奖，每行是一个匹配期。"
            : id == "B-G-B-001" ? "历史同期图：每行是往年相同期号；遗漏单位是同期样本行。"
            : "基本图：每行是一个实际开奖期，从上到下由旧到新。";
        var condition = BuiltinRuleRevisions.FindCondition(rule.RuleId);
        string filter = condition is null ? "无额外过滤条件。"
            : condition.Label + (condition.Feature == "count" ? "。这是图形候选数量限制。"
                : "。这是后来加入的历史筛选条件；频次和遗漏按实际开奖序列计算，不按筛选图层行数计算。");
        string shape, target;
        string kind = "图形结构";
        string[] columns = [], labels = [], cells = [];
        switch (id)
        {
            case "B-G-R-001":
                kind = "遗漏条件"; shape = "选择累计遗漏达到阈值的红球；只取遗漏最大者，并列取小号。";
                target = "一个冷号。这不是由多个已开点围成的几何图形。"; break;
            case "B-G-B-001":
                kind = "同期遗漏条件"; shape = "对每个蓝球计算相同期号样本中的连续缺席行数，达到阈值即入选。";
                target = "可能有多个蓝球；默认阈值16指16个同期样本，不是16个连续实际开奖期。"; break;
            case "B-G-R-002":
                shape = "同一号码连续两行开出，下一行再开就形成三重。"; target = "下一行同列号码 n。";
                columns = ["n"]; labels = ["前2行", "上1行", "下一行"]; cells = ["●", "●", "×"]; break;
            case "B-G-R-003":
                shape = "同列连续两行开出，后一行同时开相邻号；左右镜像都识别。"; target = "下一行杀构成L的两个号码，不是只杀缺角。";
                columns = ["n", "n+1"]; labels = ["前2行", "上1行", "下一行"]; cells = ["●·", "●●", "××"]; break;
            case "B-G-R-004":
                shape = "同列连续两行开出，前一行同时开相邻号；左右镜像都识别。"; target = "下一行杀构成倒L的两个号码。";
                columns = ["n", "n+1"]; labels = ["前2行", "上1行", "下一行"]; cells = ["●●", "●·", "××"]; break;
            case "B-G-R-005":
                int gap = condition?.Gap > 0 ? condition.Gap : 1;
                shape = $"同一号码：开出→连续空{gap}行→开出→连续空{gap}行。"; target = "下一行同列号码 n，阻止交替继续。";
                columns = ["n"]; cells = new[] { "●" }.Concat(Enumerable.Repeat("○", gap)).Concat(new[] { "●" }).Concat(Enumerable.Repeat("○", gap)).Append("×").ToArray();
                labels = Enumerable.Range(0, cells.Length - 1).Select(i => $"前{cells.Length - 1 - i}行").Append("下一行").ToArray(); break;
            case "B-G-R-006":
                shape = "同一号码依次开、开、空、开；下一行再开会形成两组重号。"; target = "下一行同列号码 n。";
                columns = ["n"]; labels = ["前4行", "前3行", "前2行", "上1行", "下一行"]; cells = ["●", "●", "○", "●", "×"]; break;
            case "B-G-R-007":
                shape = "前一行开中心号，后一行中心必须未开、左右相邻号同时开出。"; target = "下一行中心号 n；不杀两侧号码。";
                columns = ["n-1", "n", "n+1"]; labels = ["前2行", "上1行", "下一行"]; cells = ["·●·", "●○●", "·×·"]; break;
            case "B-G-R-008":
                shape = "相邻A、B：A开且B空，紧接一行B开且A空；随后直到最新A再开，A和B中间均不得再开。左右镜像都识别。";
                target = "下一行补第四角的B。中间可没有空行，也可有多行；不限制固定间隔。";
                columns = ["A", "B=A±1"]; labels = ["较早行", "紧接一行", "中间0至多行", "最新行", "下一行"]; cells = ["●○", "○●", "○○", "●○", "·×"]; break;
            case "B-G-R-009":
                kind = "红蓝对应条件"; shape = "取当前图层最近一期的蓝球号码，映射为同号码红球。";
                target = "一个1—16之间的红球；不是多点构成的几何形状。"; break;
            case "B-G-B-002":
                kind = "重号条件"; shape = "取上一期蓝球，下一期同号出现即形成连续重号。"; target = "下一期同列蓝球 n。";
                columns = ["n"]; labels = ["上1行", "下一行"]; cells = ["●", "×"]; break;
            case "B-G-R-010":
                shape = "最新一行开出至少三个连续号码；四连及更长连号会产生重叠三连。"; target = "下一行所有三连对应的号码，合并去重。";
                columns = ["n", "n+1", "n+2"]; labels = ["上1行", "下一行"]; cells = ["●●●", "×××"]; break;
            case "B-G-R-011":
                shape = "连续三行沿同一方向每行移动一列；上升、下降方向都识别。"; target = "下一行斜线延长点，越过1—33边界时舍弃，不循环。";
                columns = ["n", "n+1", "n+2", "n+3"]; labels = ["前3行", "前2行", "上1行", "下一行"]; cells = ["●···", "·●··", "··●·", "···×"]; break;
            case "B-G-R-012":
                shape = "一对相邻号在较早行和最新行同时开出，中间至少一行，且这两个号码均未开出。"; target = "下一行这两个相邻号码；不限固定高度。";
                columns = ["n", "n+1"]; labels = ["较早行", "中间1至多行", "最新行", "下一行"]; cells = ["●●", "○○", "●●", "××"]; break;
            default: return null;
        }
        return new(kind, scope, shape, target, filter, columns, labels, cells);
    }
}
