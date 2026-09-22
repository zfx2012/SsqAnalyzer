using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SsqAnalyzer.Services;

namespace SsqAnalyzer;

/// <summary>Handles position commands without owning the WPF application lifecycle.</summary>
internal sealed class PositionCommandLineHandler
{
    private readonly IServiceProvider Services;
    private readonly Action<int> _shutdown;
    private readonly Action<object> _writeJson;

    public PositionCommandLineHandler(IServiceProvider services, Action<int> shutdown, Action<object>? writeJson = null)
    {
        Services = services;
        _shutdown = shutdown;
        _writeJson = writeJson ?? WriteConsoleJson;
    }

    /// <summary>Returns false for no command or an unknown command, preserving normal GUI startup.</summary>
    public bool TryHandle(string[] args)
    {
        if (args.FirstOrDefault()?.Equals("--position-update-and-freeze", StringComparison.OrdinalIgnoreCase) == true)
        {
            var dataService = Services.GetRequiredService<IDataService>();
            var store = Services.GetRequiredService<IPositionValidationStore>();
            try
            {
                int updated = Task.Run(dataService.TryUpdateAsync).GetAwaiter().GetResult();
                int settled = store.Reconcile();
                var coordinator = Services.GetRequiredService<PositionExperimentCoordinator>();
                int sealedCount = coordinator.EnsureCurrentPredictions();
                _writeJson(new
                {
                    Updated = updated,
                    LatestIssue = dataService.GetLastPeriod(),
                    Settled = settled,
                    Sealed = sealedCount,
                    store.FilePath,
                    Error = dataService.LastErrorMessage ?? coordinator.LastError,
                    Integrity = store.GetIntegrityReport()
                });
                _shutdown(dataService.LastErrorMessage is null && coordinator.LastError is null ? 0 : 2);
            }
            catch (Exception ex)
            {
                _writeJson(new { Error = store.LastError ?? dataService.LastErrorMessage ?? ex.Message, store.FilePath });
                _shutdown(2);
            }
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-compare-versions", StringComparison.OrdinalIgnoreCase) == true)
        {
            IPositionValidationStore store = args.Length > 3
                ? new PositionValidationStore(
                    Services.GetRequiredService<IDataService>(),
                    Path.GetFullPath(args[3]),
                    subscribeToUpdates: false)
                : Services.GetRequiredService<IPositionValidationStore>();
            try
            {
                var summaries = store.GetSummaries();
                string? primary = args.Length > 1 ? args[1] : summaries
                    .Select(summary => summary.RuleVersionId)
                    .FirstOrDefault(version => version.StartsWith(
                        PositionPredictor.CurrentRuleVersion + "+",
                        StringComparison.Ordinal));
                string? candidate = args.Length > 2 ? args[2] : summaries
                    .Select(summary => summary.RuleVersionId)
                    .FirstOrDefault(version => version.StartsWith(
                        PositionPredictor.CurrentShadowRuleVersion + "+",
                        StringComparison.Ordinal));
                if (primary is null || candidate is null)
                    throw new InvalidOperationException("缺少可比较的主版本或影子版本前向记录");
                _writeJson(new
                {
                    store.FilePath,
                    Integrity = store.GetIntegrityReport(),
                    Comparison = store.CompareVersions(primary, candidate)
                });
                _shutdown(0);
            }
            catch (Exception ex)
            {
                _writeJson(new { Error = store.LastError ?? ex.Message, store.FilePath });
                _shutdown(2);
            }
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-reconcile", StringComparison.OrdinalIgnoreCase) == true)
        {
            var store = Services.GetRequiredService<IPositionValidationStore>();
            try
            {
                int settled = store.Reconcile();
                _writeJson(new
                {
                    Settled = settled,
                    store.FilePath,
                    Integrity = store.GetIntegrityReport(),
                    Summaries = store.GetSummaries(),
                    Comparison = GetDefaultPositionComparison(store)
                });
                _shutdown(0);
            }
            catch (Exception ex)
            {
                _writeJson(new { Error = store.LastError ?? ex.Message, store.FilePath });
                _shutdown(2);
            }
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-audit-ledger", StringComparison.OrdinalIgnoreCase) == true)
        {
            string auditPath = args.Length > 1 ? Path.GetFullPath(args[1]) : AppPaths.PositionValidationFile;
            var store = new PositionValidationStore(
                Services.GetRequiredService<IDataService>(),
                auditPath,
                subscribeToUpdates: false);
            try
            {
                _writeJson(new
                {
                    Valid = true,
                    store.FilePath,
                    Integrity = store.GetIntegrityReport(),
                    Summaries = store.GetSummaries()
                });
                _shutdown(0);
            }
            catch (Exception ex)
            {
                _writeJson(new
                {
                    Valid = false,
                    store.FilePath,
                    Error = store.LastError ?? ex.Message
                });
                _shutdown(2);
            }
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-seal-ledger", StringComparison.OrdinalIgnoreCase) == true)
        {
            var store = Services.GetRequiredService<IPositionValidationStore>();
            try
            {
                int sealedCount = store.SealMissingHashes();
                _writeJson(new
                {
                    Sealed = sealedCount,
                    store.FilePath,
                    Integrity = store.GetIntegrityReport()
                });
                _shutdown(0);
            }
            catch (Exception ex)
            {
                _writeJson(new { Error = store.LastError ?? ex.Message, store.FilePath });
                _shutdown(2);
            }
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-freeze-all", StringComparison.OrdinalIgnoreCase) == true)
        {
            var coordinator = Services.GetRequiredService<PositionExperimentCoordinator>();
            int added = coordinator.EnsureCurrentPredictions();
            var store = Services.GetRequiredService<IPositionValidationStore>();
            _writeJson(new
            {
                Added = added,
                coordinator.LastAttemptUtc,
                Error = coordinator.LastError,
                store.FilePath,
                Integrity = store.GetIntegrityReport(),
                Summaries = store.GetSummaries()
            });
            _shutdown(coordinator.LastError is null ? 0 : 2);
            return true;
        }

        bool freezePrimary = args.FirstOrDefault()?.Equals(
            "--position-freeze-next",
            StringComparison.OrdinalIgnoreCase) == true;
        bool freezeShadow = args.FirstOrDefault()?.Equals(
            "--position-freeze-shadow",
            StringComparison.OrdinalIgnoreCase) == true;
        if (freezePrimary || freezeShadow)
        {
            var store = Services.GetRequiredService<IPositionValidationStore>();
            IPositionPredictor predictor = freezeShadow
                ? PositionPredictor.CreateAnnualShortDynamicHierarchicalShadow(
                    Services.GetRequiredService<IDataService>(),
                    store)
                : Services.GetRequiredService<IPositionPredictor>();
            try
            {
                int issue = predictor.GetIssueOptions().FirstOrDefault();
                if (issue == 0) throw new InvalidOperationException("暂无开奖数据，无法生成下一期预测");
                var prediction = predictor.Predict(issue, "live");
                var summary = store.GetSummary(predictor.RuleVersionId);
                bool frozen = summary.LastIssue == prediction.Issue;
                _writeJson(new
                {
                    Frozen = frozen,
                    prediction.Issue,
                    prediction.AsOfIssue,
                    prediction.SnapshotId,
                    prediction.RuleVersionId,
                    prediction.RunId,
                    prediction.RedPoints,
                    prediction.SingleBlue,
                    prediction.DoubleBlue,
                    prediction.TripleBlue,
                    store.FilePath,
                    store.LastError,
                    Integrity = store.GetIntegrityReport(),
                    Summary = summary,
                    AllSummaries = store.GetSummaries()
                });
                _shutdown(0);
            }
            catch (Exception ex)
            {
                _writeJson(new { Error = store.LastError ?? ex.Message, store.FilePath });
                _shutdown(2);
            }
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-export-all", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (args.Length < 2)
                throw new ArgumentException("--position-export-all 需要指定输出xlsx路径");
            string outputPath = Path.GetFullPath(args[1]);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                int? predictionYear = args.Length > 2
                    && int.TryParse(args[2], out int parsedYear)
                        ? parsedYear
                        : null;
                var predictions = PositionWorkbookExporter.GeneratePredictions(
                    Services.GetRequiredService<IDataService>().GetAllRecords(),
                    predictionYear: predictionYear);
                PositionWorkbookExporter.Export(outputPath, predictions);
                stopwatch.Stop();
                _writeJson(new
                {
                    outputPath,
                    PredictionYear = predictions[0].Issue / 1000,
                    Rows = predictions.Count,
                    FirstIssue = predictions[0].Issue,
                    LastIssue = predictions[^1].Issue,
                    DrawnRows = predictions.Count(prediction => prediction.Actual is not null),
                    ElapsedSeconds = stopwatch.Elapsed.TotalSeconds
                });
                _shutdown(0);
            }
            catch (Exception ex)
            {
                _writeJson(new { Error = ex.ToString(), outputPath });
                _shutdown(2);
            }
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-forward-status", StringComparison.OrdinalIgnoreCase) == true)
        {
            var predictor = Services.GetRequiredService<IPositionPredictor>();
            var store = Services.GetRequiredService<IPositionValidationStore>();
            try
            {
                _writeJson(new
                {
                    store.FilePath,
                    store.LastError,
                    Integrity = store.GetIntegrityReport(),
                    Summary = store.GetSummary(predictor.RuleVersionId),
                    AllSummaries = store.GetSummaries(),
                    Comparison = GetDefaultPositionComparison(store)
                });
                _shutdown(0);
            }
            catch (Exception ex)
            {
                _writeJson(new
                {
                    store.FilePath,
                    Error = store.LastError ?? ex.Message,
                    RuleVersionId = predictor.RuleVersionId
                });
                _shutdown(2);
            }
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-backtest", StringComparison.OrdinalIgnoreCase) == true)
        {
            int sampleSize = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 200;
            var report = Services.GetRequiredService<IPositionPredictor>().Backtest(sampleSize);
            _writeJson(report);
            _shutdown(0);
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-backtest-point-promotion", StringComparison.OrdinalIgnoreCase) == true)
        {
            int sampleSize = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 3200;
            int offset = args.Length > 2 && int.TryParse(args[2], out int parsedOffset) ? parsedOffset : 0;
            var current = new PositionPredictor(
                Services.GetRequiredService<IDataService>(),
                Services.GetRequiredService<IPositionValidationStore>());
            var previous = PositionPredictor.CreatePreviousPrimary(
                Services.GetRequiredService<IDataService>(),
                Services.GetRequiredService<IPositionValidationStore>());
            var comparison = current.CompareBacktest(previous, sampleSize, offset);
            bool bluePredictionsPreserved = comparison.DifferentSingleBlueCount == 0
                && comparison.DifferentDoubleBlueCount == 0
                && comparison.DifferentTripleBlueCount == 0;
            _writeJson(new
            {
                current.RuleVersionId,
                PreviousRuleVersionId = previous.RuleVersionId,
                PointPromotionPassed = comparison.IsRedPairwiseSuperior && bluePredictionsPreserved,
                BluePredictionsPreserved = bluePredictionsPreserved,
                Comparison = comparison
            });
            _shutdown(0);
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-point-research", StringComparison.OrdinalIgnoreCase) == true)
        {
            int windowSize = args.Length > 1 && int.TryParse(args[1], out int parsedWindow) ? parsedWindow : 400;
            int windowCount = args.Length > 2 && int.TryParse(args[2], out int parsedCount) ? parsedCount : 8;
            var records = Services.GetRequiredService<IDataService>().GetAllRecords();
            _writeJson(PositionPointResearch.Run(records, windowSize, windowCount));
            _shutdown(0);
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-annual-short-research", StringComparison.OrdinalIgnoreCase) == true)
        {
            var records = Services.GetRequiredService<IDataService>().GetAllRecords();
            _writeJson(AnnualShortPointResearch.Run(records));
            _shutdown(0);
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-backtest-compare", StringComparison.OrdinalIgnoreCase) == true)
        {
            int sampleSize = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 200;
            int offset = args.Length > 2 && int.TryParse(args[2], out int parsedOffset) ? parsedOffset : 0;
            var primary = Services.GetRequiredService<IPositionPredictor>();
            var candidate = PositionPredictor.CreatePointShrunkSeasonTransitionShadow(
                Services.GetRequiredService<IDataService>(),
                Services.GetRequiredService<IPositionValidationStore>());
            _writeJson(candidate.CompareBacktest(primary, sampleSize, offset));
            _shutdown(0);
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-backtest-shadow", StringComparison.OrdinalIgnoreCase) == true)
        {
            int sampleSize = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 200;
            int offset = args.Length > 2 && int.TryParse(args[2], out int parsedOffset) ? parsedOffset : 0;
            var predictor = PositionPredictor.CreateAnnualShortDynamicHierarchicalShadow(
                Services.GetRequiredService<IDataService>(),
                Services.GetRequiredService<IPositionValidationStore>());
            _writeJson(new
            {
                predictor.RuleVersionId,
                Report = predictor.Backtest(sampleSize, offset)
            });
            _shutdown(0);
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-backtest-recent-structure", StringComparison.OrdinalIgnoreCase) == true)
        {
            int sampleSize = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 200;
            int offset = args.Length > 2 && int.TryParse(args[2], out int parsedOffset) ? parsedOffset : 0;
            var primary = Services.GetRequiredService<IPositionPredictor>();
            var candidate = PositionPredictor.CreatePointRecentStructureShadow(
                Services.GetRequiredService<IDataService>(),
                Services.GetRequiredService<IPositionValidationStore>());
            _writeJson(new
            {
                candidate.RuleVersionId,
                Report = candidate.Backtest(sampleSize, offset),
                Comparison = candidate.CompareBacktest(primary, sampleSize, offset)
            });
            _shutdown(0);
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-backtest-formula-anchor", StringComparison.OrdinalIgnoreCase) == true)
        {
            int sampleSize = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 200;
            int offset = args.Length > 2 && int.TryParse(args[2], out int parsedOffset) ? parsedOffset : 0;
            var baseline = PositionPredictor.CreateRangeCoverageShadow(
                Services.GetRequiredService<IDataService>(),
                Services.GetRequiredService<IPositionValidationStore>());
            var candidate = PositionPredictor.CreateFormulaAnchoredRangeCoverageShadow(
                Services.GetRequiredService<IDataService>(),
                Services.GetRequiredService<IPositionValidationStore>());
            _writeJson(new
            {
                candidate.RuleVersionId,
                Report = offset == 0 ? candidate.Backtest(sampleSize) : null,
                ComparisonToV36 = candidate.CompareBacktest(baseline, sampleSize, offset)
            });
            _shutdown(0);
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-backtest-adaptive-blue", StringComparison.OrdinalIgnoreCase) == true)
        {
            int sampleSize = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 200;
            int offset = args.Length > 2 && int.TryParse(args[2], out int parsedOffset) ? parsedOffset : 0;
            var baseline = PositionPredictor.CreateRangeCoverageShadow(
                Services.GetRequiredService<IDataService>(),
                Services.GetRequiredService<IPositionValidationStore>());
            var candidate = PositionPredictor.CreateAdaptiveBlueRangeCoverageShadow(
                Services.GetRequiredService<IDataService>(),
                Services.GetRequiredService<IPositionValidationStore>());
            _writeJson(new
            {
                candidate.RuleVersionId,
                Report = offset == 0 ? candidate.Backtest(sampleSize) : null,
                ComparisonToV36 = candidate.CompareBacktest(baseline, sampleSize, offset)
            });
            _shutdown(0);
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-backtest-rolling-hot", StringComparison.OrdinalIgnoreCase) == true)
        {
            int sampleSize = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 200;
            var predictor = PositionPredictor.CreateRollingHotShadow(
                Services.GetRequiredService<IDataService>(),
                Services.GetRequiredService<IPositionValidationStore>());
            _writeJson(new
            {
                predictor.RuleVersionId,
                Report = predictor.Backtest(sampleSize)
            });
            _shutdown(0);
            return true;
        }

        if (args.FirstOrDefault()?.Equals("--position-backtest-ensemble", StringComparison.OrdinalIgnoreCase) == true)
        {
            int sampleSize = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 200;
            var predictor = PositionPredictor.CreateFormulaEnsembleShadow(
                Services.GetRequiredService<IDataService>(),
                Services.GetRequiredService<IPositionValidationStore>());
            _writeJson(new
            {
                predictor.RuleVersionId,
                Report = predictor.Backtest(sampleSize)
            });
            _shutdown(0);
            return true;
        }

        return false;
    }

    private static void WriteConsoleJson(object value)
    {
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        output.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static SsqAnalyzer.Models.PositionExperimentComparison? GetDefaultPositionComparison(
        IPositionValidationStore store)
    {
        var versions = store.GetSummaries().Select(summary => summary.RuleVersionId).ToList();
        string? primary = versions.FirstOrDefault(version => version.StartsWith(
            PositionPredictor.CurrentRuleVersion + "+",
            StringComparison.Ordinal));
        string? candidate = versions.FirstOrDefault(version => version.StartsWith(
            PositionPredictor.CurrentShadowRuleVersion + "+",
            StringComparison.Ordinal));
        return primary is null || candidate is null
            ? null
            : store.CompareVersions(primary, candidate);
    }

}
