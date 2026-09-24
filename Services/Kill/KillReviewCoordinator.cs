namespace SsqAnalyzer.Services.Kill;

/// <summary>Review saved submissions on startup and when local draw data changes.</summary>
public sealed class KillReviewCoordinator(IDataService data, KillSubmissionStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;
    public string? LastError { get; private set; }
    public IReadOnlyDictionary<int, string> Issues { get; private set; } = new Dictionary<int, string>();
    public event Action? Updated;
    public void Start()
    {
        if (_started) return;
        _started = true;
        data.DataUpdated += OnDataUpdated;
        OnDataUpdated();
    }
    private void OnDataUpdated() => _ = RefreshAsync();
    public async Task RefreshAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var history = KillResearchService.SnapshotRecords(data.GetAllRecords());
            var result = await Task.Run(() => store.Settle(history));
            Issues = result.Issues; LastError = null;
        }
        catch (Exception ex) { LastError = $"复盘未完成：{ex.Message}"; }
        finally { _gate.Release(); }
        Updated?.Invoke();
    }
}
