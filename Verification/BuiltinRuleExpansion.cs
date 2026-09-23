using System.IO;
using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

internal static class BuiltinRuleExpansion
{
    internal sealed record Spec(int Family, int Window, BallType Ball);
    private static readonly string[] Names = { "高频", "低频已出现", "降温", "升温", "短期重复", "长缺回补", "短期密集", "中段遗漏", "超均值遗漏", "紧邻重开" };
    private static readonly string[] Reasons = {
        "选择窗口内出现次数最多的号码", "在窗口内至少出现一次的号码中选择出现次数最少者",
        "前半窗口比后半窗口出现频率高，按两段长度归一后选择降幅最大者", "后半窗口比前半窗口出现频率高，按两段长度归一后选择增幅最大者",
        "最近3期出现至少2次，选择窗口总频次最多者", "最新一期出现，且此前连续未出现期数至少为窗口的三分之一，选择空缺最长者",
        "最近5期至少出现2次且频率高于整窗均值，选择超出幅度最大者", "当前遗漏位于窗口的四分之一到二分之一之间，选择窗口频次最高者",
        "窗口内至少出现2次，当前遗漏大于窗口期数除以出现次数，选择遗漏与频次乘积最大者", "最新一期出现，且上次出现距最新期不超过窗口四分之一，选择间距最小者" };

    internal static KillRule Create(Spec s, string id) => new()
    {
        RuleId=id, Name=$"{s.Window}期{Names[s.Family]}单号杀{(s.Ball==BallType.Red?"红":"蓝")}", Category=RuleCategory.Pattern,
        SubCategory="统计条件", BallType=s.Ball, IsBuiltin=true, IsEnabled=true, ForceEnabled=false, MinAccuracy=KillSettings.For(s.Ball),
        Params=new() { ["window"]=s.Window, ["family"]=s.Family, ["blue"]=s.Ball==BallType.Blue },
        Description=$"只使用此前{s.Window}个实际开奖期：{Reasons[s.Family]}；同分取小号；条件不满足则不杀号，每次最多1个。近50次成绩参与历史筛选，尚未前向验证。",
        Tags=new(){"统计条件",Names[s.Family],"历史筛选"}, Source="builtin", CreatedAt=new DateTime(2026,9,23,0,0,0,DateTimeKind.Utc), JsCode=Code
    };

    internal const string Code = """
function getKillBalls(ctx) {
  var w=ctx.params.window, f=ctx.params.family, blue=ctx.params.blue;
  var h=ctx.history(w); if(h.length<w) return [];
  var max=blue?16:33, count=[], newer=[], last3=[], last5=[], miss=[], gap=[];
  for(var b=1;b<=max;b++){count[b]=0;newer[b]=0;last3[b]=0;last5[b]=0;miss[b]=w;gap[b]=-1;}
  for(var i=0;i<w;i++){
    var balls=blue?[h[i].blueBall]:h[i].redBalls;
    for(var j=0;j<balls.length;j++){
      var b=balls[j];count[b]++;
      if(i>=Math.floor(w/2))newer[b]++;
      if(i>=w-3)last3[b]++;if(i>=w-5)last5[b]++;
      if(i<w-1)gap[b]=w-1-i;
      miss[b]=w-1-i;
    }
  }
  var best=0, bestScore=-Infinity;
  for(var b=1;b<=max;b++){
    var score=-Infinity, c=count[b], n=newer[b], m=miss[b], g=gap[b];
    if(f===0)score=c;
    if(f===1&&c>0)score=-c;
    var change=n*Math.floor(w/2)-(c-n)*(w-Math.floor(w/2));
    if(f===2&&change<0)score=-change;
    if(f===3&&change>0)score=change;
    if(f===4&&last3[b]>=2)score=c;
    if(f===5&&m===0&&g>=0&&g-1>=Math.floor(w/3))score=g;
    if(f===6&&last5[b]>=2&&last5[b]*w>5*c)score=last5[b]*w-5*c;
    if(f===7&&m>=Math.floor(w/4)&&m<=Math.floor(w/2))score=c;
    if(f===8&&c>=2&&m*c>w)score=m*c;
    if(f===9&&m===0&&g>0&&g<=Math.floor(w/4))score=-g;
    if(score>bestScore){best=b;bestScore=score;}
  }
  return best===0?[]:[best];
}
""";

    // Independent C# implementation used for screening; accepted output is checked against JS at every draw.
    internal static int Predict(Spec s, IReadOnlyList<DrawRecord> records, int index)
    {
        int w=s.Window; if(index<w)return 0;
        int max=s.Ball==BallType.Red?33:16;
        var count=new int[max+1];var newer=new int[max+1];var three=new int[max+1];var five=new int[max+1];
        var last=Enumerable.Repeat(-1,max+1).ToArray();var previous=Enumerable.Repeat(-1,max+1).ToArray();
        for(int i=index-w;i<index;i++)
        {
            IEnumerable<int> balls=s.Ball==BallType.Red?records[i].RedBalls:new[]{records[i].BlueBall};
            foreach(int b in balls){count[b]++;if(i>=index-w+w/2)newer[b]++;if(i>=index-3)three[b]++;if(i>=index-5)five[b]++;if(i<index-1)previous[b]=i;last[b]=i;}
        }
        int best=0;double bestScore=double.NegativeInfinity;
        for(int b=1;b<=max;b++)
        {
            int c=count[b],n=newer[b],m=last[b]<0?w:index-1-last[b],g=previous[b]<0?-1:index-1-previous[b];
            int change=n*(w/2)-(c-n)*(w-w/2);
            double score=s.Family switch {
                0=>c,1 when c>0=>-c,2 when change<0=>-change,3 when change>0=>change,
                4 when three[b]>=2=>c,5 when m==0&&g>=0&&g-1>=w/3=>g,
                6 when five[b]>=2&&five[b]*w>5*c=>five[b]*w-5*c,
                7 when m>=w/4&&m<=w/2=>c,8 when c>=2&&m*c>w=>m*c,
                9 when m==0&&g>0&&g<=w/4=>-g,_=>double.NegativeInfinity};
            if(score>bestScore){best=b;bestScore=score;}
        }
        return best;
    }
    private sealed record Score(int Triggered,int Correct,int? First,int? Last)
    { public double Accuracy=>Triggered==0?0:(double)Correct/Triggered; }
    private sealed record Trial(Spec Spec,Score Recent,Score Older,Score All,int[] Output);
    public static void Run(string input,string directory)
    {
        Directory.CreateDirectory(directory);
        var records=KillResearchService.SnapshotRecords(File.ReadLines(input).Where(l=>!string.IsNullOrWhiteSpace(l)).Select(l=>{
            var p=l.Split(' ',StringSplitOptions.RemoveEmptyEntries);return new DrawRecord{Period=int.Parse(p[0]),DrawDate=DateTime.Parse(p[1]),RedBalls=p.Skip(2).Take(6).Select(int.Parse).ToArray(),BlueBall=int.Parse(p[8])};}));
        var trials=new List<Trial>();int split=1+(records.Count-1)*70/100;
        foreach(var ball in new[]{BallType.Red,BallType.Blue})
        for(int family=0;family<10;family++)
        foreach(int window in new[]{10,12,15,20,25,30,40,50,60,80,100,120})
        {
            var spec=new Spec(family,window,ball);var output=Enumerable.Range(0,records.Count).Select(i=>Predict(spec,records,i)).ToArray();
            trials.Add(new(spec,Summary(output,ball,0,records.Count,50),Summary(output,ball,0,split),Summary(output,ball,0,records.Count),output));
        }
        var executor=new JintRuleExecutor();var builder=new RuleContextBuilder();var miss=MissMatrixCalculator.Compute(records,records);
        var existing=new List<(BallType Ball,int[][] Output)>();
        var originalIds=BuiltinRules.LoadOriginalCatalog().Select(r=>r.RuleId).ToHashSet();
        foreach(var rule in BuiltinRules.LoadAll().Where(r=>originalIds.Contains(r.RuleId)))
        {
            var output=new int[records.Count][];output[0]=Array.Empty<int>();
            for(int i=1;i<records.Count;i++)output[i]=executor.Execute(rule,builder.BuildWithMiss(records,i,miss)).KilledBalls.ToArray();
            existing.Add((rule.BallType,output));
        }
        var chosen=new List<Trial>();
        foreach(var ball in new[]{BallType.Red,BallType.Blue})
        {
            var eligible=trials.Where(t=>t.Spec.Ball==ball&&t.Recent.Triggered==50&&t.Recent.Accuracy>=KillSettings.For(ball)
                &&t.All.Triggered>=200&&t.Recent.First>=records[^500].Period&&t.Recent.Last>=records[^30].Period)
                .OrderByDescending(t=>t.Older.Triggered>=100&&t.Older.Accuracy>=KillSettings.For(ball))
                .ThenByDescending(t=>t.Older.Accuracy).ThenByDescending(t=>t.All.Triggered).ThenBy(t=>t.Spec.Family).ThenBy(t=>t.Spec.Window).ToArray();
            Console.WriteLine($"{ball}: {eligible.Length} passing candidates");
            foreach(var t in eligible)
            {
                if(chosen.Count(c=>c.Spec.Ball==ball)==25)break;
                if(chosen.Count(c=>c.Spec.Ball==ball&&c.Spec.Family==t.Spec.Family)>=3)continue;
                if(existing.Where(e=>e.Ball==ball).Any(e=>Similarity(t.Output,e.Output)>.90))continue;
                chosen.Add(t);existing.Add((ball,t.Output.Select(b=>b==0?Array.Empty<int>():new[]{b}).ToArray()));
            }
        }
        if(chosen.Count!=50)throw new InvalidOperationException($"Only {chosen.Count} distinct candidates qualified; no output catalog written.");
        var rules=new List<KillRule>();int red=0,blue=0;
        foreach(var t in chosen)
        {
            string id=t.Spec.Ball==BallType.Red?$"B-S-R-{++red:D3}":$"B-S-B-{++blue:D3}";
            var rule=Create(t.Spec,id);
            for(int i=1;i<records.Count;i++)
            {
                var actual=executor.Execute(rule,builder.BuildWithMiss(records,i,miss)).KilledBalls;
                if((t.Output[i]==0&&actual.Count!=0)||(t.Output[i]!=0&&(actual.Count!=1||actual[0]!=t.Output[i])))throw new InvalidOperationException($"JS mismatch {id} at {records[i].Period}");
            }
            rules.Add(rule);Console.WriteLine($"{id} {rule.Name}: {t.Recent.Accuracy:P2} / all {t.All.Triggered} triggers");
        }
        File.WriteAllText(Path.Combine(directory,"rules.json"),"[\n"+string.Join(",\n",rules.Select(r=>r.ToJson()))+"\n]");
        File.WriteAllText(Path.Combine(directory,"audit.json"),JsonSerializer.Serialize(new{DataHash=KillRuleDefinition.DataHash(records),Count=records.Count,Last=records[^1].Period,ReferenceEnd=records[split-1].Period,
            Trials=trials.Select(t=>new{t.Spec,t.Recent,t.Older,t.All}),Selected=chosen.Select((t,i)=>new{rules[i].RuleId,rules[i].Name,t.Spec,t.Recent,t.Older,t.All})},new JsonSerializerOptions{WriteIndented=true}));
        Score Summary(int[] output,BallType ball,int start,int end,int take=int.MaxValue)
        {
            var indexes=Enumerable.Range(start,end-start).Where(i=>output[i]>0).TakeLast(take).ToArray();
            return new(indexes.Length,indexes.Count(i=>ball==BallType.Red?!records[i].RedBalls.Contains(output[i]):records[i].BlueBall!=output[i]),indexes.Length==0?null:records[indexes[0]].Period,indexes.Length==0?null:records[indexes[^1]].Period);
        }
        double Similarity(int[] a,int[][] b)
        {
            int union=0,equal=0;for(int i=Math.Max(1,records.Count-500);i<records.Count;i++){if(a[i]==0&&b[i].Length==0)continue;union++;if(b[i].Length==1&&a[i]==b[i][0])equal++;}
            return union==0?1:(double)equal/union;
        }
    }
}
