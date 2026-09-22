using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace SsqAnalyzer.Services;

public class DownloadService
{
    private readonly HttpClient _dlHttp = new() { Timeout = TimeSpan.FromMinutes(30) };
    private const string HashFileName = "SHA2-256SUMS";

    private static string YtDlpPath => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");

    public async Task EnsureYtDlp(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (System.IO.File.Exists(YtDlpPath)) return;

        string[] urlBases = {
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/",
            "https://mirror.ghproxy.com/https://github.com/yt-dlp/yt-dlp/releases/latest/download/"
        };

        foreach (var baseUrl in urlBases)
        {
            try
            {
                var exeBytes = await _dlHttp.GetByteArrayAsync(baseUrl + "yt-dlp.exe", ct);
                var hashBytes = await _dlHttp.GetByteArrayAsync(baseUrl + HashFileName, ct);
                var hashContent = System.Text.Encoding.UTF8.GetString(hashBytes).Trim();
                var expectedHash = ExtractExpectedHash(hashContent)
                    ?? throw new Exception("SHA256 汇总文件中缺少 yt-dlp.exe");

                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var actualHash = Convert.ToHexString(sha256.ComputeHash(exeBytes));

                if (actualHash != expectedHash)
                {
                    Debug.WriteLine($"[DownloadService] yt-dlp SHA256 不匹配: 期望 {expectedHash}, 实际 {actualHash}");
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                await System.IO.File.WriteAllBytesAsync(YtDlpPath, exeBytes);
                Debug.WriteLine($"[DownloadService] yt-dlp SHA256 校验通过: {expectedHash}");
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DownloadService.EnsureYtDlp] 下载/校验失败: {ex.Message}");
            }
        }

        throw new Exception("yt-dlp.exe 下载或校验失败。请手动下载放到程序目录下。\nhttps://github.com/yt-dlp/yt-dlp/releases");
    }

    public async Task RunDownload(string url, string output, string? quality = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var format = quality == "720"
            ? "bv[height<=720][height>480]/bv[height<=720]"
            : "bv[height<=480]/bv";
        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        // ArgumentList keeps URLs and file paths data, even when they contain quotes or spaces.
        psi.ArgumentList.Add(url);
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(output);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(format);
        psi.ArgumentList.Add("--no-progress");
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add("--user-agent");
        psi.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        psi.ArgumentList.Add("--add-header");
        psi.ArgumentList.Add("Referer:https://www.bilibili.com/");
        psi.ArgumentList.Add("--add-header");
        psi.ArgumentList.Add("Origin:https://www.bilibili.com");
        psi.ArgumentList.Add("--extractor-retries");
        psi.ArgumentList.Add("5");
        psi.ArgumentList.Add("--retries");
        psi.ArgumentList.Add("5");
        psi.ArgumentList.Add("--socket-timeout");
        psi.ArgumentList.Add("30");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 yt-dlp 进程");
        var errorOutput = new StringBuilder();

        proc.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (errorOutput)
                    errorOutput.AppendLine(args.Data);
            }
        };

        proc.EnableRaisingEvents = true;
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            await proc.WaitForExitAsync();
            throw;
        }
        // WaitForExit() flushes the asynchronous redirected output events.
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            string lastErr;
            lock (errorOutput) lastErr = errorOutput.ToString().Trim();
            throw new Exception($"yt-dlp 退出码 {proc.ExitCode}\n{lastErr}");
        }
    }

    private static string? ExtractExpectedHash(string content)
    {
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[^1].TrimStart('*').Equals("yt-dlp.exe", StringComparison.OrdinalIgnoreCase))
                continue;
            var hash = parts[0].ToUpperInvariant();
            if (hash.Length == 64 && hash.All(Uri.IsHexDigit)) return hash;
        }
        return null;
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().Trim();
    }
}
