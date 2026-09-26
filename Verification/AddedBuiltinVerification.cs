using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    internal static int[] GeometryReference(IKillRule rule, IReadOnlyList<DrawRecord> data, int index)
    {
        var path = JsonSerializer.Deserialize<int[]>(JsonSerializer.Serialize(rule.Params["offsets"]))!;
        int target = JsonSerializer.Deserialize<int>(JsonSerializer.Serialize(rule.Params["target"]));
        bool blue = rule.BallType == BallType.Blue;
        if (index < path.Length) return [];
        IEnumerable<int> Balls(DrawRecord r) => blue ? new[] { r.BlueBall } : r.RedBalls;
        var rows = data.Skip(index - path.Length).Take(path.Length).Select(r => Balls(r).ToHashSet()).ToArray();
        var result = new SortedSet<int>();
        foreach (int first in rows[0]) foreach (int sign in new[] { 1, -1 })
        {
            int anchor = first - sign * path[0], candidate = anchor + sign * target;
            if (candidate >= 1 && candidate <= (blue ? 16 : 33)
                && rows.Select((row, i) => row.Contains(anchor + sign * path[i])).All(v => v)) result.Add(candidate);
        }
        return result.ToArray();
    }

    private static void VerifyAddedBuiltinRules(KillRule[] added)
    {
        Assert(added.Length == 50 && added.Count(r => r.BallType == BallType.Red) == 25 && added.Count(r => r.BallType == BallType.Blue) == 25, "50 geometry rules retain identities and ball types");
        Assert(added.All(r => r.IsBuiltin && !r.ForceEnabled && r.Params.ContainsKey("geometryVersion") && !r.Params.ContainsKey("family")), "statistical conditions replaced by geometry");
        var executor = new JintRuleExecutor(); var builder = new RuleContextBuilder();
        var random = new Random(926);
        var sample = Enumerable.Range(0, 160).Select(i => new DrawRecord { Period = 2026001+i, DrawDate = new DateTime(2026,1,1).AddDays(i),
            RedBalls = Enumerable.Range(1,33).OrderBy(_ => random.Next()).Take(6).Order().ToArray(), BlueBall = random.Next(1,17) }).ToArray();
        foreach (var rule in added)
        {
            var restored = KillRule.FromJson(rule.ToJson());
            Assert(KillRuleDefinition.Capture(restored).Fingerprint == KillRuleDefinition.Capture(rule).Fingerprint, "array parameters survive rule persistence");
            var snapshot = KillRuleDefinition.Capture(rule).ToRule();
            Assert(executor.Execute(snapshot, builder.Build(sample, 159)).KilledBalls.SequenceEqual(executor.Execute(rule, builder.Build(sample, 159)).KilledBalls), "submitted geometry definition replays identically");
            var path = JsonSerializer.Deserialize<int[]>(JsonSerializer.Serialize(rule.Params["offsets"]))!;
            int target = Convert.ToInt32(rule.Params["target"]);
            foreach (int sign in new[] { 1, -1 })
            {
                int origin = 1 - path.Append(target).Min(v => sign * v);
                var records = path.Select((offset,i) => new DrawRecord { Period=2026001+i, DrawDate=new DateTime(2026,1,1).AddDays(i),
                    RedBalls=new[]{origin+sign*offset,20,22,24,26,28}.Order().ToArray(), BlueBall=origin+sign*offset }).ToArray();
                Assert(executor.Execute(rule,builder.Build(records,records.Length)).KilledBalls.Contains(origin+sign*target), "positive and mirrored geometry fixture");
                Assert(executor.Execute(rule,builder.Build(records,records.Length-1)).KilledBalls.Count == 0, "insufficient rows never trigger");
                records[0].RedBalls=new[]{17,19,21,23,25,27}; records[0].BlueBall=records[0].BlueBall==16?15:16;
                Assert(!executor.Execute(rule,builder.Build(records,records.Length)).KilledBalls.Contains(origin+sign*target), "broken required point removes fixture target");
            }
            // Explicit upper/lower boundary cases must never wrap onto the opposite edge.
            foreach (int anchor in new[] { 1, rule.BallType == BallType.Blue ? 16 : 33 })
            {
                var edge = path.Select((offset, i) => new DrawRecord { Period = 2026001+i, DrawDate = new DateTime(2026,1,1).AddDays(i),
                    RedBalls = new[] { 1, 2, 3, 31, 32, 33 }, BlueBall = Math.Clamp(anchor + offset, 1, 16) }).ToArray();
                Assert(executor.Execute(rule, builder.Build(edge, edge.Length)).KilledBalls.SequenceEqual(GeometryReference(rule, edge, edge.Length)), "boundary output matches non-wrapping reference");
            }
            for (int index=0;index<sample.Length;index++)
                Assert(executor.Execute(rule,builder.Build(sample,index)).KilledBalls.SequenceEqual(GeometryReference(rule,sample,index)), "independent geometry comparison including edges and empty history");
            var expected=executor.Execute(rule,builder.Build(sample,159)).KilledBalls.ToArray();
            sample[159].BlueBall=sample[159].BlueBall==1?16:1; sample[159].RedBalls=new[]{1,2,3,4,5,6};
            Assert(executor.Execute(rule,builder.Build(sample,159)).KilledBalls.SequenceEqual(expected), "target draw isolated");
            var guide=BuiltinPatternGuide.For(rule)!;
            Assert(guide.Cells.Length==guide.RowLabels.Length && guide.Cells.All(row=>row.Length==guide.Columns.Length), "geometry guides match rows and columns");
        }
    }
}
