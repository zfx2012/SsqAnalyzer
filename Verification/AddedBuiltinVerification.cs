using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    private static void VerifyAddedBuiltinRules(KillRule[] added)
    {
        Assert(added.Length==50 && added.Count(r=>r.BallType==BallType.Red)==25 && added.Count(r=>r.BallType==BallType.Blue)==25,"50 additions include 25 red and 25 blue rules");
        Assert(added.All(r=>r.IsBuiltin&&!r.ForceEnabled&&r.Description.Contains("历史筛选")),"new rules disclose retrospective selection without forcing eligibility");
        Assert(added.Select(r=>$"{r.BallType}|{r.Params["family"]}|{r.Params["window"]}").Distinct().Count()==50,"new parameter configurations are unique");
        var records=Enumerable.Range(0,141).Select(i=>new DrawRecord{Period=2026001+i,DrawDate=new DateTime(2026,1,1).AddDays(i),
            RedBalls=Enumerable.Range(0,6).Select(j=>1+(i*7+j*5)%33).Order().ToArray(),BlueBall=1+(i*3)%16}).ToArray();
        var builder=new RuleContextBuilder();var executor=new JintRuleExecutor();
        foreach(var rule in added)
        {
            int window=Convert.ToInt32(rule.Params["window"]);
            Assert(executor.Execute(rule,builder.Build(records,window-1)).KilledBalls.Count==0,"new rule requires complete historical window");
            var before=executor.Execute(rule,builder.Build(records,140)).KilledBalls.ToArray();
            Assert(before.Length<=1&&before.All(b=>b>=1&&b<=(rule.BallType==BallType.Red?33:16)),"new rule kills at most one valid ball");
            records[140].RedBalls=records[140].RedBalls[0]==1?new[]{2,3,4,5,6,7}:new[]{1,2,3,4,5,6};
            records[140].BlueBall=records[140].BlueBall==16?15:16;
            Assert(executor.Execute(rule,builder.Build(records,140)).KilledBalls.SequenceEqual(before),"target draw cannot change new rule output");
        }
        var flat=Enumerable.Range(0,16).Select(i=>new DrawRecord{Period=2026001+i,DrawDate=new DateTime(2026,1,1).AddDays(i),RedBalls=new[]{1,2,3,4,5,6},BlueBall=4}).ToArray();
        foreach(int family in new[]{2,3})
        {
            var rule=BuiltinRuleExpansion.Create(new(family,15,BallType.Red),"flat-fixture");
            Assert(executor.Execute(rule,builder.Build(flat,15)).KilledBalls.Count==0,"odd windows normalize half-window frequencies: flat data is neither rising nor fading");
        }
        var hot=BuiltinRuleExpansion.Create(new(0,10,BallType.Blue),"hot-fixture");
        Assert(executor.Execute(hot,builder.Build(flat,15)).KilledBalls.SequenceEqual(new[]{4}),"blue hot rule uses blue history");
        var cold=BuiltinRuleExpansion.Create(new(1,10,BallType.Red),"cold-fixture");
        Assert(executor.Execute(cold,builder.Build(flat,15)).KilledBalls.SequenceEqual(new[]{1}),"cold rule excludes never-seen balls and breaks equal scores by smallest number");
    }
}
