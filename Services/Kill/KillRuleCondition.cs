using System.Text.Json.Nodes;

namespace SsqAnalyzer.Services.Kill;

/// <summary>A fixed history-only eligibility condition applied to the original pattern's candidates.</summary>
internal sealed record KillRuleCondition(string Feature, int Window, int Minimum, int Maximum, int Gap = 0)
{
    internal string Label => (Gap > 0 ? $"交替间隔改为 {Gap} 期，且" : "") + (Feature switch
    {
        "miss" => $"候选号码当前遗漏在 {Minimum}—{Maximum} 期之间",
        "frequency" => $"候选号码在过去 {Window} 个实际开奖期出现 {Minimum}—{Maximum} 次",
        "count" => $"原图形同时产生 {Minimum}—{Maximum} 个候选时才触发",
        "alternationGap" => $"同一红球开出、连续空 {Minimum} 期、再开出、再连续空 {Minimum} 期才触发",
        _ => throw new InvalidOperationException("未知规则条件")
    });
    internal KillRule Apply(KillRule original)
    {
        if (Gap > 0)
            return (this with { Gap = 0 }).Apply(new KillRuleCondition("alternationGap", 0, Gap, Gap).Apply(original));
        if (original.BallType != BallType.Red || Minimum < 0 || Maximum < Minimum
            || Feature is not ("miss" or "frequency" or "count" or "alternationGap") || (Feature == "frequency" && Window is not (10 or 20 or 50 or 100))
            || (Feature == "alternationGap" && (original.RuleId != "B-G-R-005" || Minimum is < 2 or > 4 || Minimum != Maximum)))
            throw new InvalidOperationException("规则条件无效。");
        var json = JsonNode.Parse(original.ToJson())!.AsObject();
        if (Feature == "alternationGap")
        {
            int step = Minimum + 1;
            json["jsCode"] = $$"""
function getKillBalls(ctx) {
  var h = ctx.history({{step * 2}});
  if (h.length < {{step * 2}}) return [];
  var result = [];
  for (var j = 0; j < h[0].redBalls.length; j++) {
    var b = h[0].redBalls[j], valid = true;
    for (var i = 1; i < h.length; i++) {
      var present = h[i].redBalls.indexOf(b) >= 0;
      if (present !== (i === {{step}})) { valid = false; break; }
    }
    if (valid) result.push(b);
  }
  return result.sort(function(a,b) { return a-b; });
}
""";
            json["description"] = "基本图：" + Label + "。替换原隔一期交替条件；近50次成绩用于历史调参，待前向验证。";
            return KillRule.FromJson(json.ToJsonString());
        }
        json["jsCode"] = original.JsCode + "\nvar originalPatternKill = getKillBalls;\ngetKillBalls = function(ctx) {\n"
            + "var candidates = originalPatternKill(ctx); if (!candidates || !candidates.length) return [];\n"
            + (Feature == "count" ? $"return candidates.length >= {Minimum} && candidates.length <= {Maximum} ? candidates : [];\n"
                : Feature == "miss" ? $"return candidates.filter(function(b) {{ var n = ctx.getMiss(b, 0); return n >= {Minimum} && n <= {Maximum}; }});\n"
                : $"var h = ctx.history({Window}); if (h.length < {Window}) return [];\n"
                    + "var counts = {}; for(var i=0;i<h.length;i++) for(var j=0;j<h[i].redBalls.length;j++) { var b=h[i].redBalls[j]; counts[b]=(counts[b]||0)+1; }\n"
                    + $"return candidates.filter(function(b) {{ var n=counts[b]||0; return n >= {Minimum} && n <= {Maximum}; }});\n")
            + "};";
        json["description"] = original.Description + "；新增条件：" + Label + "。条件基于实际开奖历史；近50次成绩用于历史调参，待前向验证。";
        return KillRule.FromJson(json.ToJsonString());
    }
}
