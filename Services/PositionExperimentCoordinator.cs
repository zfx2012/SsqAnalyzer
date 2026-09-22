using System.Diagnostics;

namespace SsqAnalyzer.Services;

public sealed class PositionExperimentCoordinator
{
    private readonly object _gate = new();
    private readonly IDataService _dataService;
    private readonly IPositionValidationStore _validationStore;
    private readonly IPositionPredictor _primaryPredictor;
    private bool _running;

    public PositionExperimentCoordinator(
        IDataService dataService,
        IPositionValidationStore validationStore,
        IPositionPredictor primaryPredictor)
    {
        _dataService = dataService;
        _validationStore = validationStore;
        _primaryPredictor = primaryPredictor;
        _dataService.DataUpdated += OnDataUpdated;
    }

    public string? LastError { get; private set; }
    public DateTime? LastAttemptUtc { get; private set; }

    public int EnsureCurrentPredictions()
    {
        lock (_gate)
        {
            if (_running) return 0;
            _running = true;
            try
            {
                LastAttemptUtc = DateTime.UtcNow;
                _validationStore.SealMissingHashes();
                int before = _validationStore.GetSummaries().Sum(summary => summary.FrozenCount);
                FreezeNext(_primaryPredictor);
                FreezeNext(PositionPredictor.CreateAnnualShortDynamicHierarchicalShadow(
                    _dataService,
                    _validationStore));
                int after = _validationStore.GetSummaries().Sum(summary => summary.FrozenCount);
                LastError = _validationStore.LastError;
                return Math.Max(0, after - before);
            }
            catch (Exception ex)
            {
                LastError = _validationStore.LastError ?? ex.Message;
                Debug.WriteLine($"[PositionExperimentCoordinator] 自动冻结失败: {ex.Message}");
                return 0;
            }
            finally
            {
                _running = false;
            }
        }
    }

    private static void FreezeNext(IPositionPredictor predictor)
    {
        int issue = predictor.GetIssueOptions().FirstOrDefault();
        if (issue != 0) predictor.Predict(issue, "live");
    }

    private void OnDataUpdated() => EnsureCurrentPredictions();
}
