using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace SsqAnalyzer.Services;

public class AiAnalysisService
{
    private readonly HttpClient _http;

    /// <summary>默认构造函数（生产环境使用，连接池单例）</summary>
    public AiAnalysisService() : this(null) { }

    /// <summary>测试用构造函数 — 可注入 HttpClient</summary>
    public AiAnalysisService(HttpClient? httpClient)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    /// <summary>HTTP POST（含重试 + 取消支持）</summary>
    public async Task<string> CurlPostAsync(string key, string bodyJson, string endpoint, CancellationToken ct = default)
    {
        const int maxRetries = 2;
        Exception? lastEx = null;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                int delayMs = 1000 * (1 << (attempt - 1)); // 1s, 2s
                await Task.Delay(delayMs, ct);
            }
            ct.ThrowIfCancellationRequested();
            try
            {
                return await PostOnce(key, bodyJson, endpoint, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastEx = ex;
                // Only retry transient transport/server failures. Invalid credentials or requests will not improve.
                if (ex is ApiRequestException api && !IsTransientStatus(api.StatusCode))
                    throw;
                Debug.WriteLine($"[AiAnalysis] 第 {attempt + 1}/3 次请求失败: {ex.Message}");
            }
        }
        throw lastEx ?? new InvalidOperationException("API 请求失败");
    }

    private static bool IsTransientStatus(int statusCode) =>
        statusCode is 408 or 429 or 500 or 502 or 503 or 504;

    private async Task<string> PostOnce(string key, string bodyJson, string endpoint, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        Debug.WriteLine($"[AiAnalysis] 开始请求: {endpoint}");
        Debug.WriteLine($"[AiAnalysis] 请求体大小: {bodyJson.Length} bytes");
        // Do not write any portion of the credential to logs; debug output can be collected from a user machine.
        Debug.WriteLine($"[AiAnalysis] API Key 已配置: {!string.IsNullOrEmpty(key)}");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        sw.Stop();

        if (response.IsSuccessStatusCode)
        {
            Debug.WriteLine($"[AiAnalysis] ✅ 成功耗时: {sw.ElapsedMilliseconds}ms, 状态码: {(int)response.StatusCode}, 响应长度: {body.Length}");
            return body;
        }

        var safeBody = body ?? "";
        var detail = safeBody.Length > 200 ? safeBody[..200] + "…" : safeBody;
        Debug.WriteLine($"[AiAnalysis] ❌ 失败耗时: {sw.ElapsedMilliseconds}ms, 状态码: {(int)response.StatusCode}");
        throw new ApiRequestException((int)response.StatusCode,
            $"API请求失败 (HTTP {(int)response.StatusCode}): {detail}");
    }

    private sealed class ApiRequestException : Exception
    {
        public int StatusCode { get; }

        public ApiRequestException(int statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }
    }

    public static string GetMimeType(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp4" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".flv" => "video/x-flv",
            _ => "video/mp4"
        };
    }
}
