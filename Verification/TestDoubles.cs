using System.Net.Http;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

sealed class AnonymousBiliHandler : HttpMessageHandler
{
    public bool SawCookie { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SawCookie |= request.Headers.Contains("Cookie");
        string body = request.RequestUri!.AbsolutePath.EndsWith("/nav", StringComparison.Ordinal)
            ? """{"code":-101,"message":"账号未登录","data":{"wbi_img":{"img_url":"https://i0.hdslb.com/0123456789abcdef0123456789abcdef.png","sub_url":"https://i0.hdslb.com/fedcba9876543210fedcba9876543210.png"}}}"""
            : """{"code":0,"message":"0","data":{"list":{"vlist":[]}}}""";
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        });
    }
}

sealed class FakeDataService : IDataService
{
    private List<DrawRecord> _records = new();
    public event Action? DataUpdated;
    public int SubscriberCount => DataUpdated?.GetInvocationList().Length ?? 0;
    public int ReadCount { get; private set; }
    public bool HasLocalFile => false;
    public string LocalDataFilePath => "";
    public string? LastErrorMessage => null;
    public List<DrawRecord> GetAllRecords()
    {
        ReadCount++;
        return _records.ToList();
    }
    public int GetLastPeriod() => _records.Count == 0 ? -1 : _records[^1].Period;
    public int GetRecordCount() => _records.Count;
    public Task<int> TryUpdateAsync() => Task.FromResult(0);
    public void ClearAllData() => _records.Clear();
    public void ResetCache() { }
    public void NotifyDataUpdated() => DataUpdated?.Invoke();
    public void SetRecords(params DrawRecord[] records) =>
        _records = records.OrderBy(record => record.Period).ToList();
}
