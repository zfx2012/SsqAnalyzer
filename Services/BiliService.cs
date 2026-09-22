using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SsqAnalyzer.Services;

public class BiliService : IVideoSearchService
{
    private readonly HttpClient _http;
    private readonly ThreadLocal<Random> _rnd = new(() => new());
    private DateTime _nextRequestAt = DateTime.MinValue;
    private readonly object _lock = new();
    private string? _mixinKey;
    private DateTime _mixinKeyFetchedAt = DateTime.MinValue;
    private static readonly TimeSpan MixinKeyLifetime = TimeSpan.FromMinutes(30);
    private readonly ITicketStore _store;

    private static readonly int[] MixinKeyEncTab = {
        46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
        27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13,
        37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4,
        22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52
    };

    public BiliService(ITicketStore store) : this(store, null) { }

    internal BiliService(ITicketStore store, HttpClient? httpClient)
    {
        _store = store;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Add("Referer", "https://www.bilibili.com/");
        _http.DefaultRequestHeaders.Add("Origin", "https://www.bilibili.com");
    }

    private async Task<string> GetMixinKey(CancellationToken ct = default)
    {
        if (_mixinKey != null && DateTime.UtcNow - _mixinKeyFetchedAt < MixinKeyLifetime)
            return _mixinKey;
        var json = await BiliGetAsync("https://api.bilibili.com/x/web-interface/nav", ct);
        JsonNode? nav;
        try { nav = JsonNode.Parse(json); }
        catch { throw new Exception("获取 WBI 密钥失败，可能是风控拦截"); }
        var imgUrl = nav?["data"]?["wbi_img"]?["img_url"]?.ToString() ?? "";
        var subUrl = nav?["data"]?["wbi_img"]?["sub_url"]?.ToString() ?? "";
        var imgKey = System.IO.Path.GetFileNameWithoutExtension(imgUrl.Split('/').Last());
        var subKey = System.IO.Path.GetFileNameWithoutExtension(subUrl.Split('/').Last());
        var raw = imgKey + subKey;
        if (imgKey.Length == 0 || subKey.Length == 0 || raw.Length <= MixinKeyEncTab.Max())
            throw new Exception("获取 WBI 密钥失败，返回的图片密钥格式无效");
        var sb = new StringBuilder(32);
        for (int i = 0; i < 32; i++) sb.Append(raw[MixinKeyEncTab[i]]);
        _mixinKey = sb.ToString();
        _mixinKeyFetchedAt = DateTime.UtcNow;
        return _mixinKey;
    }

    public static string CalcWrid(string sortedQuery, string mixinKey)
    {
        var input = sortedQuery + mixinKey;
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return string.Concat(hash.Select(b => b.ToString("x2")));
    }

    public async Task<string> BiliGetAsync(string url, CancellationToken ct = default)
    {
        int delay;
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var next = now > _nextRequestAt ? now : _nextRequestAt;
            delay = Math.Max(0, (int)(next - now).TotalMilliseconds);
            _nextRequestAt = next.AddMilliseconds(800);
        }
        if (delay > 0) await Task.Delay(delay, ct);

        for (int retry = 0; retry < 3; retry++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            req.Headers.Add("Referer", "https://www.bilibili.com/");
            var biliCookie = _store.LoadBiliCookieString();
            if (!string.IsNullOrWhiteSpace(biliCookie))
                req.Headers.Add("Cookie", biliCookie);
            req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9");
            using var resp = await _http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            var statusCode = (int)resp.StatusCode;
            if (!resp.IsSuccessStatusCode)
            {
                if (statusCode == 408 || statusCode == 429 || statusCode >= 500)
                {
                    await Task.Delay(2000 + _rnd.Value!.Next(1000), ct);
                    continue;
                }
                throw new HttpRequestException($"B站请求失败，HTTP {statusCode}");
            }
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith("<"))
            { await Task.Delay(2000 + _rnd.Value!.Next(1000), ct); continue; }
            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch { await Task.Delay(2000 + _rnd.Value!.Next(1000), ct); continue; }
            var code = root?["code"]?.GetValue<int>() ?? -1;
            if (code == -352 || code == -799 || code == -412)
            { await Task.Delay(3000 + _rnd.Value!.Next(2000), ct); continue; }
            // Anonymous /nav requests return -101 but still provide the public WBI signing keys.
            if (code == -101
                && string.Equals(url, "https://api.bilibili.com/x/web-interface/nav", StringComparison.Ordinal))
                return json;
            if (code != 0)
            {
                var message = root?["message"]?.ToString();
                throw new HttpRequestException(
                    $"B站接口返回错误 ({code}){(string.IsNullOrWhiteSpace(message) ? "" : $": {message}")}");
            }
            return json;
        }
        throw new Exception("请求过于频繁，请稍后再试");
    }

    public async Task<JsonNode?> SearchByUid(long uid, CancellationToken ct = default)
    {
        var mixinKey = await GetMixinKey(ct);
        var wts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sortedParams = new[] { ("mid", uid.ToString()), ("pn", "1"), ("ps", "30"), ("wts", wts) }
            .OrderBy(x => x.Item1).ToList();
        var query = string.Join("&", sortedParams.Select(x => $"{x.Item1}={x.Item2}"));
        var wRid = CalcWrid(query, mixinKey);
        var url = $"https://api.bilibili.com/x/space/wbi/arc/search?{query}&w_rid={wRid}";
        var json = await BiliGetAsync(url, ct);
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { throw new Exception("API 返回异常，可能是风控拦截"); }
        return root;
    }

    public async Task<JsonNode?> SearchByBvid(string bvid, CancellationToken ct = default)
    {
        var json = await BiliGetAsync($"https://api.bilibili.com/x/web-interface/view?bvid={bvid}", ct);
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { throw new Exception("API 返回异常，可能是风控拦截"); }
        return root;
    }

    public static bool IsUrl(string text)
        => text.StartsWith("http://") || text.StartsWith("https://") || text.StartsWith("www.");

    public static long? ExtractUidFromUrl(string url)
    {
        foreach (var part in url.Split('/', '?', '&'))
            if (long.TryParse(part, out var n) && n > 1000) return n;
        return null;
    }

    public static string ExtractBvid(string url)
    {
        foreach (var part in url.Split('/', '?', '&', '#'))
        {
            if (!part.StartsWith("BV", StringComparison.Ordinal) || part.Length < 12)
                continue;

            var candidate = part[..12];
            if (candidate[2..].All(c =>
                    (c >= '0' && c <= '9')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')))
                return candidate;
        }
        return "";
    }
}
