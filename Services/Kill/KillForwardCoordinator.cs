namespace SsqAnalyzer.Services.Kill;

/// <summary>Settle saved predictions after local draw data changes; never generates predictions after the fact.</summary>
public sealed class KillForwardCoordinator(IDataService data, KillForwardStore store)
{
    private readonly SemaphoreSlim _gate = new(1);
    public string? LastError { get; private set; }
    public void Start() { data.DataUpdated += Refresh; Refresh(); }
    private async void Refresh()
    {
        await _gate.WaitAsync();
        try
        {
            var snapshot = data.GetAllRecords();
            await Task.Run(() => store.Settle(snapshot));
            LastError = null;
        }
        catch (Exception ex) { LastError = ex.Message; }
        finally { _gate.Release(); }
    }
}
