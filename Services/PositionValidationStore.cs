using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed class PositionValidationStore : IPositionValidationStore
{
    public const int ForwardValidationStartIssue = 2026085;
    public const int MinimumPromotionPairedSampleSize = PositionValidationPolicy.MinimumPairedSampleSize;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly IDataService _dataService;
    private Ledger? _ledger;
    private bool _loaded;

    public PositionValidationStore(IDataService dataService)
        : this(dataService, AppPaths.PositionValidationFile, subscribeToUpdates: true) { }

    internal PositionValidationStore(IDataService dataService, string filePath, bool subscribeToUpdates)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        if (subscribeToUpdates) _dataService.DataUpdated += OnDataUpdated;
    }

    public string FilePath { get; }
    public string? LastError { get; private set; }
    public event Action? Changed;

    public bool Freeze(PositionPrediction prediction)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        if (prediction.IsDrawn
            || prediction.Issue < ForwardValidationStartIssue
            || prediction.Issue <= prediction.AsOfIssue
            || !string.Equals(prediction.RunMode, "live", StringComparison.OrdinalIgnoreCase)) return false;

        bool changed;
        lock (_gate)
        {
            var ledger = RequireLedger();
            if (ledger.Records.Any(record =>
                    record.RunId == prediction.RunId
                    || (record.Issue == prediction.Issue
                        && record.RuleVersionId == prediction.RuleVersionId))) return false;
            var frozen = new PositionForwardRecord
            {
                Issue = prediction.Issue,
                AsOfIssue = prediction.AsOfIssue,
                SnapshotId = prediction.SnapshotId,
                RuleVersionId = prediction.RuleVersionId,
                RunId = prediction.RunId,
                FrozenAtUtc = DateTime.UtcNow,
                RedPoints = prediction.RedPoints.ToArray(),
                FormulaBlue = prediction.FormulaBlue,
                ExclusionBlue = prediction.ExclusionBlue,
                SingleBlue = prediction.SingleBlue,
                DoubleBlue = prediction.DoubleBlue.ToArray(),
                TripleBlue = prediction.TripleBlue.ToArray()
            };
            frozen.PredictionHash = ComputePredictionHash(frozen);
            ledger.Records.Add(frozen);
            SaveLocked(ledger);
            changed = true;
        }
        if (changed) Changed?.Invoke();
        return changed;
    }

    public int Reconcile()
    {
        int changedCount;
        lock (_gate)
        {
            var ledger = RequireLedger();
            var actualByIssue = _dataService.GetAllRecords().ToDictionary(record => record.Period);
            changedCount = 0;
            foreach (var frozen in ledger.Records)
            {
                if (!actualByIssue.TryGetValue(frozen.Issue, out var actual)) continue;
                if (frozen.ActualBlue == actual.BlueBall
                    && frozen.ActualReds.SequenceEqual(actual.RedBalls)) continue;
                Evaluate(frozen, actual);
                changedCount++;
            }
            if (changedCount > 0) SaveLocked(ledger);
        }
        if (changedCount > 0) Changed?.Invoke();
        return changedCount;
    }

    public PositionForwardSummary GetSummary(string ruleVersionId)
    {
        if (string.IsNullOrWhiteSpace(ruleVersionId))
            throw new ArgumentException("规则版本不能为空", nameof(ruleVersionId));

        lock (_gate)
        {
            var records = RequireLedger().Records
                .Where(record => record.RuleVersionId == ruleVersionId)
                .OrderBy(record => record.Issue)
                .ThenBy(record => record.FrozenAtUtc)
                .ToList();
            var evaluated = records.Where(record => record.IsEvaluated).ToList();
            int count = evaluated.Count;
            int singleHits = evaluated.Count(record => record.SingleBlueHit);
            var pointLifts = evaluated.Select(record =>
                record.PointHits - RandomExpectedPointHits(record.RedPoints)).ToList();
            var rangePointLifts = evaluated.Select(record =>
                RangePointHits(record) - RandomExpectedRangePointHits(record.RedPoints)).ToList();
            int firstHalfCount = count / 2;
            int secondHalfCount = count - firstHalfCount;
            double firstHalfRate = firstHalfCount == 0 ? 0
                : evaluated.Take(firstHalfCount).Count(record => record.SingleBlueHit) / (double)firstHalfCount;
            double secondHalfRate = secondHalfCount == 0 ? 0
                : evaluated.Skip(firstHalfCount).Count(record => record.SingleBlueHit) / (double)secondHalfCount;
            double randomAveragePointHits = count == 0 ? 0
                : evaluated.Average(record => RandomExpectedPointHits(record.RedPoints));
            double pointLiftLower = MeanLower95(pointLifts);
            double firstHalfPointLift = AverageOrZero(pointLifts.Take(firstHalfCount));
            double secondHalfPointLift = AverageOrZero(pointLifts.Skip(firstHalfCount));
            double rangePointLiftLower = MeanLower95(rangePointLifts);
            double firstHalfRangePointLift = AverageOrZero(rangePointLifts.Take(firstHalfCount));
            double secondHalfRangePointLift = AverageOrZero(rangePointLifts.Skip(firstHalfCount));
            double singleLower = WilsonLower95(singleHits, count);
            bool ballCoverageUsable = count >= MinimumPromotionPairedSampleSize
                && pointLiftLower > 0
                && firstHalfPointLift > 0
                && secondHalfPointLift > 0;
            bool rangePointUsable = count >= MinimumPromotionPairedSampleSize
                && rangePointLiftLower > 0
                && firstHalfRangePointLift > 0
                && secondHalfRangePointLift > 0;
            bool redUsable = ballCoverageUsable && rangePointUsable;
            bool blueUsable = count >= MinimumPromotionPairedSampleSize
                && singleLower > 1.0 / 16.0
                && firstHalfRate > 1.0 / 16.0
                && secondHalfRate > 1.0 / 16.0;
            return new PositionForwardSummary(
                ruleVersionId,
                records.Count,
                count,
                records.Count - count,
                records.Count == 0 ? null : records[0].Issue,
                records.Count == 0 ? null : records[^1].Issue,
                count == 0 ? 0 : evaluated.Average(record => record.PointHits),
                randomAveragePointHits,
                HitRate(evaluated, record => record.FormulaBlueHit),
                HitRate(evaluated, record => record.ExclusionBlueHit),
                HitRate(evaluated, record => record.SingleBlueHit),
                HitRate(evaluated, record => record.DoubleBlueHit),
                HitRate(evaluated, record => record.TripleBlueHit),
                singleLower,
                firstHalfRate,
                secondHalfRate,
                pointLiftLower,
                firstHalfPointLift,
                secondHalfPointLift,
                redUsable,
                blueUsable,
                redUsable && blueUsable)
            {
                AverageRangePointHits = count == 0 ? 0 : evaluated.Average(RangePointHits),
                RandomAverageRangePointHits = count == 0 ? 0
                    : evaluated.Average(record => RandomExpectedRangePointHits(record.RedPoints)),
                RangePointLiftLower95 = rangePointLiftLower,
                FirstHalfRangePointLift = firstHalfRangePointLift,
                SecondHalfRangePointLift = secondHalfRangePointLift,
                IsBallCoverageStatisticallyUsable = ballCoverageUsable,
                IsRangePointStatisticallyUsable = rangePointUsable
            };
        }
    }

    public IReadOnlyList<PositionForwardSummary> GetSummaries()
    {
        string[] ruleVersions;
        lock (_gate)
        {
            ruleVersions = RequireLedger().Records
                .Select(record => record.RuleVersionId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(version => version, StringComparer.Ordinal)
                .ToArray();
        }
        return ruleVersions.Select(GetSummary).ToArray();
    }

    public PositionLedgerIntegrityReport GetIntegrityReport()
    {
        lock (_gate)
        {
            var records = RequireLedger().Records;
            int sealedCount = records.Count(record => !string.IsNullOrWhiteSpace(record.PredictionHash));
            return new PositionLedgerIntegrityReport(
                records.Count,
                sealedCount,
                records.Count - sealedCount,
                sealedCount == records.Count);
        }
    }

    public int SealMissingHashes()
    {
        int sealedCount;
        lock (_gate)
        {
            var ledger = RequireLedger();
            sealedCount = 0;
            foreach (var record in ledger.Records.Where(record => string.IsNullOrWhiteSpace(record.PredictionHash)))
            {
                record.PredictionHash = ComputePredictionHash(record);
                sealedCount++;
            }
            if (sealedCount > 0) SaveLocked(ledger);
        }
        if (sealedCount > 0) Changed?.Invoke();
        return sealedCount;
    }

    public PositionExperimentComparison CompareVersions(
        string primaryRuleVersionId,
        string candidateRuleVersionId)
    {
        if (string.IsNullOrWhiteSpace(primaryRuleVersionId))
            throw new ArgumentException("主规则版本不能为空", nameof(primaryRuleVersionId));
        if (string.IsNullOrWhiteSpace(candidateRuleVersionId))
            throw new ArgumentException("候选规则版本不能为空", nameof(candidateRuleVersionId));
        if (string.Equals(primaryRuleVersionId, candidateRuleVersionId, StringComparison.Ordinal))
            throw new ArgumentException("主规则版本和候选规则版本不能相同");

        lock (_gate)
        {
            var records = RequireLedger().Records;
            var primaryByIssue = records
                .Where(record => record.RuleVersionId == primaryRuleVersionId)
                .ToDictionary(record => record.Issue);
            var candidateByIssue = records
                .Where(record => record.RuleVersionId == candidateRuleVersionId)
                .ToDictionary(record => record.Issue);
            var pairs = primaryByIssue.Keys.Intersect(candidateByIssue.Keys)
                .OrderBy(issue => issue)
                .Select(issue => (Primary: primaryByIssue[issue], Candidate: candidateByIssue[issue]))
                .ToList();
            var evaluated = pairs.Where(pair => pair.Primary.IsEvaluated && pair.Candidate.IsEvaluated).ToList();

            int differentPoints = pairs.Count(pair => !pair.Primary.RedPoints.SequenceEqual(pair.Candidate.RedPoints));
            int differentFormula = pairs.Count(pair => pair.Primary.FormulaBlue != pair.Candidate.FormulaBlue);
            int differentExclusion = pairs.Count(pair => pair.Primary.ExclusionBlue != pair.Candidate.ExclusionBlue);
            int differentSingle = pairs.Count(pair => pair.Primary.SingleBlue != pair.Candidate.SingleBlue);
            int differentDouble = pairs.Count(pair =>
                !pair.Primary.DoubleBlue.SequenceEqual(pair.Candidate.DoubleBlue));
            int differentTriple = pairs.Count(pair =>
                !pair.Primary.TripleBlue.SequenceEqual(pair.Candidate.TripleBlue));
            int differentNested = pairs.Count(pair =>
                !pair.Primary.DoubleBlue.SequenceEqual(pair.Candidate.DoubleBlue)
                || !pair.Primary.TripleBlue.SequenceEqual(pair.Candidate.TripleBlue));
            int candidateWins = evaluated.Count(pair => pair.Candidate.SingleBlueHit && !pair.Primary.SingleBlueHit);
            int primaryWins = evaluated.Count(pair => pair.Primary.SingleBlueHit && !pair.Candidate.SingleBlueHit);
            int bothHits = evaluated.Count(pair => pair.Primary.SingleBlueHit && pair.Candidate.SingleBlueHit);
            int discordant = candidateWins + primaryWins;
            var pointAdvantages = evaluated
                .Select(pair => (double)(pair.Candidate.PointHits - pair.Primary.PointHits))
                .ToList();
            var rangePointAdvantages = evaluated
                .Select(pair => (double)(RangePointHits(pair.Candidate) - RangePointHits(pair.Primary)))
                .ToList();
            var singleAdvantages = evaluated
                .Select(pair => (pair.Candidate.SingleBlueHit ? 1.0 : 0.0)
                              - (pair.Primary.SingleBlueHit ? 1.0 : 0.0))
                .ToList();
            var doubleAdvantages = evaluated
                .Select(pair => (pair.Candidate.DoubleBlueHit ? 1.0 : 0.0)
                              - (pair.Primary.DoubleBlueHit ? 1.0 : 0.0))
                .ToList();
            var tripleAdvantages = evaluated
                .Select(pair => (pair.Candidate.TripleBlueHit ? 1.0 : 0.0)
                              - (pair.Primary.TripleBlueHit ? 1.0 : 0.0))
                .ToList();
            double candidatePointAdvantage = AverageOrZero(pointAdvantages);
            double pointAdvantageLower = MeanLower95(pointAdvantages);
            double candidateBlueWinRate = discordant == 0 ? 0 : candidateWins / (double)discordant;
            double candidateBlueWinLower = WilsonLower95(candidateWins, discordant);
            var candidateSummary = GetSummary(candidateRuleVersionId);
            int firstHalfCount = evaluated.Count / 2;
            var pointGate = StableMeanGate(differentPoints, pointAdvantages, firstHalfCount);
            var rangePointGate = StableMeanGate(
                differentPoints,
                rangePointAdvantages,
                firstHalfCount);
            var doubleGate = StableMeanGate(differentDouble, doubleAdvantages, firstHalfCount);
            var tripleGate = StableMeanGate(differentTriple, tripleAdvantages, firstHalfCount);
            double singleFirstHalf = AverageOrZero(singleAdvantages.Take(firstHalfCount));
            double singleSecondHalf = AverageOrZero(singleAdvantages.Skip(firstHalfCount));
            bool singleGate = differentSingle == 0
                || (evaluated.Count >= MinimumPromotionPairedSampleSize
                    && discordant >= 30
                    && candidateBlueWinLower > 0.5
                    && singleFirstHalf > 0
                    && singleSecondHalf > 0);
            bool allRelativeGates = pointGate.Passed
                && rangePointGate.Passed
                && singleGate
                && doubleGate.Passed
                && tripleGate.Passed;
            bool promotable = evaluated.Count >= MinimumPromotionPairedSampleSize
                && candidateSummary.IsStatisticallyUsable
                && allRelativeGates;

            return new PositionExperimentComparison(
                primaryRuleVersionId,
                candidateRuleVersionId,
                pairs.Count,
                evaluated.Count,
                differentPoints,
                differentFormula,
                differentExclusion,
                differentSingle,
                differentNested,
                candidateWins,
                primaryWins,
                bothHits,
                discordant,
                evaluated.Count == 0 ? 0 : evaluated.Average(pair => pair.Primary.PointHits),
                evaluated.Count == 0 ? 0 : evaluated.Average(pair => pair.Candidate.PointHits),
                candidatePointAdvantage,
                pointAdvantageLower,
                HitRate(evaluated.Select(pair => pair.Primary).ToList(), record => record.SingleBlueHit),
                HitRate(evaluated.Select(pair => pair.Candidate).ToList(), record => record.SingleBlueHit),
                candidateBlueWinRate,
                candidateBlueWinLower,
                candidateSummary.IsStatisticallyUsable,
                promotable)
            {
                DifferentDoubleBlueCount = differentDouble,
                DifferentTripleBlueCount = differentTriple,
                PointAdvantageFirstHalf = pointGate.FirstHalf,
                PointAdvantageSecondHalf = pointGate.SecondHalf,
                SingleBlueAdvantageFirstHalf = singleFirstHalf,
                SingleBlueAdvantageSecondHalf = singleSecondHalf,
                PrimaryDoubleBlueHitRate = HitRate(evaluated.Select(pair => pair.Primary).ToList(), record => record.DoubleBlueHit),
                CandidateDoubleBlueHitRate = HitRate(evaluated.Select(pair => pair.Candidate).ToList(), record => record.DoubleBlueHit),
                DoubleBlueAdvantage = AverageOrZero(doubleAdvantages),
                DoubleBlueAdvantageLower95 = doubleGate.Lower95,
                DoubleBlueAdvantageFirstHalf = doubleGate.FirstHalf,
                DoubleBlueAdvantageSecondHalf = doubleGate.SecondHalf,
                PrimaryTripleBlueHitRate = HitRate(evaluated.Select(pair => pair.Primary).ToList(), record => record.TripleBlueHit),
                CandidateTripleBlueHitRate = HitRate(evaluated.Select(pair => pair.Candidate).ToList(), record => record.TripleBlueHit),
                TripleBlueAdvantage = AverageOrZero(tripleAdvantages),
                TripleBlueAdvantageLower95 = tripleGate.Lower95,
                TripleBlueAdvantageFirstHalf = tripleGate.FirstHalf,
                TripleBlueAdvantageSecondHalf = tripleGate.SecondHalf,
                IsPointPairwiseStable = pointGate.Passed,
                IsRangePointPairwiseStable = rangePointGate.Passed,
                IsSingleBluePairwiseStable = singleGate,
                IsDoubleBluePairwiseStable = doubleGate.Passed,
                IsTripleBluePairwiseStable = tripleGate.Passed,
                CandidateMeetsAllRelativeGates = allRelativeGates,
                CandidateMeetsPointAbsoluteGate = candidateSummary.IsRedStatisticallyUsable,
                CandidateMeetsBallCoverageAbsoluteGate =
                    candidateSummary.IsBallCoverageStatisticallyUsable,
                CandidateMeetsRangePointAbsoluteGate =
                    candidateSummary.IsRangePointStatisticallyUsable,
                PrimaryAverageRangePointHits = AverageOrZero(
                    evaluated.Select(pair => (double)RangePointHits(pair.Primary))),
                CandidateAverageRangePointHits = AverageOrZero(
                    evaluated.Select(pair => (double)RangePointHits(pair.Candidate))),
                CandidateRangePointAdvantage = AverageOrZero(rangePointAdvantages),
                CandidateRangePointAdvantageLower95 = rangePointGate.Lower95,
                RangePointAdvantageFirstHalf = rangePointGate.FirstHalf,
                RangePointAdvantageSecondHalf = rangePointGate.SecondHalf
            };
        }
    }

    private void OnDataUpdated()
    {
        try
        {
            Reconcile();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.WriteLine($"[PositionValidationStore] 前向结算失败: {ex.Message}");
        }
    }

    private Ledger RequireLedger()
    {
        EnsureLoaded();
        if (_ledger is null)
            throw new InvalidDataException(LastError ?? "前向验证账本无法加载");
        return _ledger;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(FilePath))
        {
            _ledger = new Ledger();
            LastError = null;
            return;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var ledger = JsonSerializer.Deserialize<Ledger>(json, JsonOptions);
            if (ledger is null || ledger.SchemaVersion != 1)
                throw new InvalidDataException("前向验证账本版本无效");
            ledger.Records ??= new List<PositionForwardRecord>();
            foreach (var record in ledger.Records.Where(record => !string.IsNullOrWhiteSpace(record.PredictionHash)))
            {
                string expected = ComputePredictionHash(record);
                if (!HashEquals(record.PredictionHash, expected))
                    throw new InvalidDataException(
                        $"不可变预测校验失败: {record.Issue}/{record.RuleVersionId}/{record.RunId}");
            }
            _ledger = ledger;
            LastError = null;
        }
        catch (Exception ex)
        {
            _ledger = null;
            LastError = $"前向验证账本损坏，已拒绝覆盖: {ex.Message}";
        }
    }

    private void SaveLocked(Ledger ledger)
    {
        AppPaths.EnsureRoot();
        string temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(ledger, JsonOptions));
            File.Move(temporaryPath, FilePath, overwrite: true);
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = $"前向验证账本写入失败: {ex.Message}";
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) { Debug.WriteLine($"[PositionValidationStore] 临时文件清理失败: {ex.Message}"); }
            }
        }
    }

    private static void Evaluate(PositionForwardRecord frozen, DrawRecord actual)
    {
        frozen.ActualReds = actual.RedBalls.ToArray();
        frozen.ActualBlue = actual.BlueBall;
        frozen.EvaluatedAtUtc = DateTime.UtcNow;
        frozen.PointHits = actual.RedBalls.Count(actualBall =>
            frozen.RedPoints.Any(point => PositionPointRange.Contains(point, actualBall, 33)));
        frozen.FormulaBlueHit = frozen.FormulaBlue == actual.BlueBall;
        frozen.ExclusionBlueHit = frozen.ExclusionBlue == actual.BlueBall;
        frozen.SingleBlueHit = frozen.SingleBlue == actual.BlueBall;
        frozen.DoubleBlueHit = frozen.DoubleBlue.Contains(actual.BlueBall);
        frozen.TripleBlueHit = frozen.TripleBlue.Contains(actual.BlueBall);
    }

    private static double HitRate(IReadOnlyCollection<PositionForwardRecord> records, Func<PositionForwardRecord, bool> selector) =>
        records.Count == 0 ? 0 : records.Count(selector) / (double)records.Count;

    private static int RangePointHits(PositionForwardRecord record) =>
        record.RedPoints.Count(point => record.ActualReds.Any(actualBall =>
            PositionPointRange.Contains(point, actualBall, 33)));

    private static double RandomExpectedRangePointHits(IReadOnlyList<int> points) =>
        PositionPointRange.RandomExpectedLitCount(points, 33, 6);

    private static StableGateResult StableMeanGate(
        int differentPredictionCount,
        IReadOnlyList<double> advantages,
        int firstHalfCount)
    {
        double lower95 = MeanLower95(advantages);
        double firstHalf = AverageOrZero(advantages.Take(firstHalfCount));
        double secondHalf = AverageOrZero(advantages.Skip(firstHalfCount));
        bool passed = differentPredictionCount == 0
            || (advantages.Count >= MinimumPromotionPairedSampleSize
                && lower95 > 0 && firstHalf > 0 && secondHalf > 0);
        return new StableGateResult(lower95, firstHalf, secondHalf, passed);
    }

    private static double WilsonLower95(int hits, int total)
    {
        if (total == 0) return 0;
        const double z = 1.959963984540054;
        double p = hits / (double)total;
        double denominator = 1 + z * z / total;
        double center = p + z * z / (2 * total);
        double margin = z * Math.Sqrt((p * (1 - p) + z * z / (4 * total)) / total);
        return (center - margin) / denominator;
    }

    private static double RandomExpectedPointHits(IReadOnlyList<int> points)
    {
        int coveredNumbers = points
            .SelectMany(point => Enumerable.Range(
                Math.Max(1, point - PositionPointRange.Radius),
                Math.Min(33, point + PositionPointRange.Radius)
                    - Math.Max(1, point - PositionPointRange.Radius) + 1))
            .Distinct()
            .Count();
        return 6.0 * coveredNumbers / 33.0;
    }

    private static double MeanLower95(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double mean = values.Average();
        if (values.Count == 1) return mean;
        double variance = values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1);
        const double z = 1.959963984540054;
        return mean - z * Math.Sqrt(variance / values.Count);
    }

    private static double AverageOrZero(IEnumerable<double> values)
    {
        var materialized = values as IReadOnlyCollection<double> ?? values.ToArray();
        return materialized.Count == 0 ? 0 : materialized.Average();
    }

    private sealed record StableGateResult(
        double Lower95,
        double FirstHalf,
        double SecondHalf,
        bool Passed);

    private static string ComputePredictionHash(PositionForwardRecord record)
    {
        object value = record.LegacyDragonHead is not null
            || record.LegacyPhoenixTail is not null
            || record.LegacyGoldAnchors is not null
            ? new
            {
                record.Issue,
                record.AsOfIssue,
                record.SnapshotId,
                record.RuleVersionId,
                record.RunId,
                FrozenAtUtcTicks = record.FrozenAtUtc.ToUniversalTime().Ticks,
                RedPoints = record.RedPoints.ToArray(),
                DragonHead = record.LegacyDragonHead?.ToArray() ?? Array.Empty<int>(),
                PhoenixTail = record.LegacyPhoenixTail?.ToArray() ?? Array.Empty<int>(),
                GoldAnchors = record.LegacyGoldAnchors?.ToArray() ?? Array.Empty<int>(),
                record.FormulaBlue,
                record.ExclusionBlue,
                record.SingleBlue,
                DoubleBlue = record.DoubleBlue.ToArray(),
                TripleBlue = record.TripleBlue.ToArray()
            }
            : new
            {
                record.Issue,
                record.AsOfIssue,
                record.SnapshotId,
                record.RuleVersionId,
                record.RunId,
                FrozenAtUtcTicks = record.FrozenAtUtc.ToUniversalTime().Ticks,
                RedPoints = record.RedPoints.ToArray(),
                record.FormulaBlue,
                record.ExclusionBlue,
                record.SingleBlue,
                DoubleBlue = record.DoubleBlue.ToArray(),
                TripleBlue = record.TripleBlue.ToArray()
            };
        string canonical = JsonSerializer.Serialize(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool HashEquals(string actual, string expected)
    {
        try
        {
            byte[] actualBytes = Convert.FromHexString(actual);
            byte[] expectedBytes = Convert.FromHexString(expected);
            return actualBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class Ledger
    {
        public int SchemaVersion { get; init; } = 1;
        public List<PositionForwardRecord> Records { get; set; } = new();
    }
}
