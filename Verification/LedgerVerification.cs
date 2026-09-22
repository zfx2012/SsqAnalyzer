using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

internal static partial class VerificationSuite
{
    private static void VerifyForwardLedgerLifecycle()
    {
        WithTestDirectory(testRoot =>
        {
            string ledgerPath = Path.Combine(testRoot, "ledger.json");
            var data = new FakeDataService();
            var store = new PositionValidationStore(data, ledgerPath, subscribeToUpdates: true);
    
            Assert(store.GetSummary("rule-a").FrozenCount == 0, "empty summary");
            Assert(!File.Exists(ledgerPath), "summary does not create ledger");
    
            var first = Prediction(2026085, 2026084, "rule-a", "run-a");
            Assert(store.Freeze(first), "first freeze");
            Assert(File.Exists(ledgerPath), "freeze creates ledger");
            Assert(store.GetIntegrityReport().IsFullySealed, "new record sealed");
            Assert(!store.Freeze(first), "same run rejected");
            Assert(!store.Freeze(Prediction(2026085, 2026084, "rule-a", "run-a2")),
                "same issue and rule rejected");
            Assert(!store.Freeze(Prediction(2026086, 2026085, "rule-a", "backtest", "backtest")),
                "backtest rejected");
            Assert(!store.Freeze(Prediction(2026086, 2026086, "rule-a", "not-forward")),
                "non-forward target rejected");
    
            data.SetRecords(new DrawRecord
            {
                Period = 2026085,
                DrawDate = new DateTime(2026, 7, 26),
                RedBalls = new[] { 1, 5, 6, 10, 12, 16 },
                BlueBall = 5
            });
            data.NotifyDataUpdated();
    
            var settled = store.GetSummary("rule-a");
            Assert(settled.EvaluatedCount == 1 && settled.PendingCount == 0, "automatic settlement");
            Assert(settled.AveragePointHits == 5, "point range evaluation");
            Assert(settled.AverageRangePointHits == 4,
                "visible point-range evaluation");
            Assert(settled.RandomAverageRangePointHits > 0
                && settled.RangePointLift > 0
                && !settled.IsRangePointStatisticallyUsable,
                "visible point-range random baseline and small-sample gate");
            Assert(settled.SingleBlueHitRate == 1, "single blue evaluation");
            Assert(settled.RandomAveragePointHits > 0, "random point baseline");
            Assert(!settled.IsRedStatisticallyUsable && !settled.IsBlueStatisticallyUsable,
                "small sample cannot be usable");
            Assert(store.Reconcile() == 0, "settlement idempotent");
    
            Assert(store.Freeze(Prediction(2026086, 2026085, "rule-b", "run-b")), "version b freeze");
            Assert(store.Freeze(Prediction(2026086, 2026085, "rule-c", "run-c")),
                "same issue under different version");
            Assert(store.Freeze(Prediction(
                    2026086,
                    2026085,
                    "rule-d",
                    "run-d",
                    singleBlue: 7,
                    doubleBlue: new[] { 7, 11 },
                    tripleBlue: new[] { 7, 11, 15 })),
                "changed nested blue candidate freeze");
            Assert(store.GetSummary("rule-b").PendingCount == 1, "version b isolated");
            Assert(store.GetSummary("rule-c").PendingCount == 1, "version c isolated");
            Assert(store.GetSummaries().Count == 4, "all versions listed");
    
            data.SetRecords(
                new DrawRecord
                {
                    Period = 2026085,
                    DrawDate = new DateTime(2026, 7, 26),
                    RedBalls = new[] { 1, 5, 6, 10, 12, 16 },
                    BlueBall = 5
                },
                new DrawRecord
                {
                    Period = 2026086,
                    DrawDate = new DateTime(2026, 7, 28),
                    RedBalls = new[] { 2, 8, 14, 20, 26, 32 },
                    BlueBall = 9
                });
            Assert(store.Reconcile() == 3, "paired version settlement");
            var comparison = store.CompareVersions("rule-b", "rule-c");
            Assert(comparison.PairedEvaluatedCount == 1, "paired comparison count");
            Assert(comparison.DifferentSingleBlueCount == 0, "identical version predictions recognized");
            Assert(comparison.DifferentDoubleBlueCount == 0
                && comparison.DifferentTripleBlueCount == 0,
                "identical nested predictions recognized");
            Assert(comparison.CandidateMeetsAllRelativeGates,
                "unchanged dimensions satisfy non-regression gates");
            Assert(comparison.IsRangePointPairwiseStable
                && comparison.PrimaryAverageRangePointHits == 4
                && comparison.CandidateAverageRangePointHits == 4,
                "identical point ranges satisfy visible-hit gate");
            Assert(!comparison.IsPointOnlyPromotable
                && comparison.PointVerdict.Contains("没有差异", StringComparison.Ordinal),
                "identical points do not satisfy point-only promotion");
            Assert(!comparison.IsCandidatePromotable, "small paired sample cannot promote");
            var changedComparison = store.CompareVersions("rule-b", "rule-d");
            Assert(changedComparison.DifferentSingleBlueCount == 1
                && changedComparison.DifferentDoubleBlueCount == 1
                && changedComparison.DifferentTripleBlueCount == 1,
                "changed nested blue dimensions detected");
            Assert(!changedComparison.IsSingleBluePairwiseStable
                && !changedComparison.IsDoubleBluePairwiseStable
                && !changedComparison.IsTripleBluePairwiseStable
                && !changedComparison.CandidateMeetsAllRelativeGates,
                "changed dimensions cannot pass on one paired sample");
    
            using var document = JsonDocument.Parse(File.ReadAllText(ledgerPath));
            var record = document.RootElement.GetProperty("Records")[0];
            Assert(record.GetProperty("RunId").GetString() == "run-a", "frozen run immutable");
            Assert(record.GetProperty("ActualBlue").GetInt32() == 5, "actual result persisted");
            Assert(record.GetProperty("PredictionHash").GetString()?.Length == 64, "prediction hash persisted");
        });
    }
    private static void VerifyForwardPointAbsoluteGates()
    {
        WithTestDirectory(testRoot =>
        {
            var allLit = BuildForwardGateSummary(
                Path.Combine(testRoot, "all-lit-ledger.json"),
                new[] { 2, 5, 8, 11, 14, 17 });
            Assert(allLit.EvaluatedCount == PositionValidationPolicy.MinimumPairedSampleSize,
                "forward absolute gate sample size");
            Assert(allLit.AveragePointHits == 6 && allLit.AverageRangePointHits == 6,
                "all-lit sample covers and lights six points");
            Assert(allLit.IsBallCoverageStatisticallyUsable
                && allLit.IsRangePointStatisticallyUsable
                && allLit.IsRedStatisticallyUsable,
                "all-lit sample passes both point absolute gates");
    
            var clustered = BuildForwardGateSummary(
                Path.Combine(testRoot, "clustered-ledger.json"),
                new[] { 1, 2, 3, 4, 5, 6 });
            Assert(clustered.AveragePointHits == 6 && clustered.AverageRangePointHits == 2,
                "clustered sample separates red coverage from lit points");
            Assert(clustered.IsBallCoverageStatisticallyUsable
                && !clustered.IsRangePointStatisticallyUsable
                && !clustered.IsRedStatisticallyUsable,
                "clustered sample fails only visible point absolute gate");
        });
    }
    private static PositionForwardSummary BuildForwardGateSummary(string ledgerPath, int[] actualReds)
    {
        var data = new FakeDataService();
        var store = new PositionValidationStore(data, ledgerPath, subscribeToUpdates: false);
        int[] points = { 2, 5, 8, 11, 14, 17 };
        var records = new List<DrawRecord>(PositionValidationPolicy.MinimumPairedSampleSize);
        for (int index = 0; index < PositionValidationPolicy.MinimumPairedSampleSize; index++)
        {
            int issue = 2027001 + index;
            Assert(store.Freeze(Prediction(
                issue,
                issue - 1,
                "absolute-gate-rule",
                $"absolute-gate-run-{index}",
                redPoints: points)), $"forward absolute gate freeze {index}");
            records.Add(new DrawRecord
            {
                Period = issue,
                DrawDate = new DateTime(2027, 1, 1).AddDays(index * 3),
                RedBalls = actualReds.ToArray(),
                BlueBall = 5
            });
        }
        data.SetRecords(records.ToArray());
        Assert(store.Reconcile() == PositionValidationPolicy.MinimumPairedSampleSize,
            "forward absolute gate settlement");
        return store.GetSummary("absolute-gate-rule");
    }
    private static void VerifyLedgerImmutableHash()
    {
        WithTestDirectory(testRoot =>
        {
            string ledgerPath = Path.Combine(testRoot, "ledger.json");
            var store = new PositionValidationStore(new FakeDataService(), ledgerPath, subscribeToUpdates: false);
            Assert(store.Freeze(Prediction(2026085, 2026084, "rule-a", "run-a")), "integrity freeze");
    
            var document = JsonNode.Parse(File.ReadAllText(ledgerPath))!.AsObject();
            var record = document["Records"]!.AsArray()[0]!.AsObject();
            record["FormulaBlue"] = 6;
            File.WriteAllText(ledgerPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    
            var tampered = new PositionValidationStore(new FakeDataService(), ledgerPath, subscribeToUpdates: false);
            bool rejected = false;
            try
            {
                tampered.GetSummary("rule-a");
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Assert(rejected, "tampered immutable prediction rejected");
            Assert(tampered.LastError?.Contains("校验失败", StringComparison.Ordinal) == true,
                "tamper reason retained");
        });
    }
    private static void VerifyCorruptLedgerProtection()
    {
        WithTestDirectory(testRoot =>
        {
            string corruptPath = Path.Combine(testRoot, "corrupt.json");
            const string corruptContent = "{ broken";
            File.WriteAllText(corruptPath, corruptContent);
            var store = new PositionValidationStore(new FakeDataService(), corruptPath, subscribeToUpdates: false);
            bool rejected = false;
            try
            {
                store.Freeze(Prediction(2026087, 2026086, "rule-a", "run-corrupt"));
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Assert(rejected, "corrupt ledger rejected");
            Assert(File.ReadAllText(corruptPath) == corruptContent, "corrupt ledger not overwritten");
        });
    }
}
