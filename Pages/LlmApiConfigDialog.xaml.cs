using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages;

/// <summary>
/// LLM API 配置子窗口（NL→Code 用）。
/// 模型预设（DeepSeek/GLM/Minimax/Kimi/自定义）→ 自动补地址 → 填 API Key → 测试/获取模型 → 保存。
/// 配置独立于上方「千问 API」（多模态视频用），存于 llmKey/llmBaseUrl/llmModel。
/// </summary>
public partial class LlmApiConfigDialog : Window
{
    private readonly ICredentialStore _store;
    private readonly HttpClient _http;
    /// <summary>累积的模型列表（去重）。ItemsSource 只在 OnLoaded 设一次，后续只 Add，避免"项集合必须为空"错误。</summary>
    private readonly ObservableCollection<string> _allModels = new();
    private CancellationTokenSource? _requestCts;
    private bool _closed;
    private bool _settingModel;

    private void InvalidateRequest()
    {
        if (_settingModel) return;
        _requestCts?.Cancel();
        if (_requestCts is not null && !_closed) SetStatus("配置已更改，请重新获取或测试", ok: true);
    }

    public LlmApiConfigDialog()
        : this(App.Services.GetRequiredService<ICredentialStore>()) { }

    public LlmApiConfigDialog(ICredentialStore store)
        : this(store, new HttpClient { Timeout = TimeSpan.FromSeconds(30) }) { }

    internal LlmApiConfigDialog(ICredentialStore store, HttpClient http)
    {
        _http = http;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        InitializeComponent();
        UrlBox.TextChanged += (_, _) => InvalidateRequest();
        ApiKeyBox.PasswordChanged += (_, _) => InvalidateRequest();
        ModelBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => InvalidateRequest()));
        Closed += (_, _) => { _closed = true; InvalidateRequest(); _http.Dispose(); };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        PresetBox.ItemsSource = LlmProviderPresets.All;

        // 反推预设
        var storedUrl = _store.LoadLlmBaseUrl();
        var guessedId = LlmProviderPresets.GuessPresetIdByBaseUrl(storedUrl) ?? "custom";
        PresetBox.SelectedValue = guessedId;

        // 地址框：预设选了则填预设的默认 URL（除非 storedUrl 非空且不匹配预设，让用户保留）
        UrlBox.Text = string.IsNullOrWhiteSpace(storedUrl)
            ? LlmProviderPresets.FindById(guessedId)?.DefaultBaseUrl ?? ""
            : storedUrl!;

        // API Key
        var key = _store.LoadLlmApiKey();
        if (!string.IsNullOrEmpty(key)) ApiKeyBox.Password = key;

        // 模型名：ItemsSource 只设一次（ObservableCollection），后续只 Add，避免重复获取时报"项集合必须为空"
        ModelBox.ItemsSource = _allModels;
        var model = _store.LoadLlmModel();
        ModelBox.Text = model ?? "";
        if (!string.IsNullOrWhiteSpace(model) && !_allModels.Contains(model))
            _allModels.Add(model);
    }

    // ==================== 交互 ====================

    /// <summary>预设切换：自动填默认 URL；"custom" 不动 URL（让用户自填）。</summary>
    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedValue is not string id) return;
        var preset = LlmProviderPresets.FindById(id);
        if (preset is null) return;
        // 自定义：不动 URL；其他预设：覆盖（但只在 URL 为空时覆盖，避免覆盖用户手动修改）
        if (preset.Id == "custom")
        {
            // 清空 URL 让用户输入（但仅当当前 URL 等于某个已知预设的 URL 时才清，避免清掉自填值）
            var current = UrlBox.Text?.Trim();
            bool isKnown = LlmProviderPresets.All.Any(p =>
                !string.IsNullOrEmpty(p.DefaultBaseUrl) &&
                string.Equals(p.DefaultBaseUrl.TrimEnd('/'), current?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            if (isKnown) UrlBox.Text = "";
        }
        else if (string.IsNullOrWhiteSpace(UrlBox.Text) || IsKnownPresetUrl(UrlBox.Text))
        {
            UrlBox.Text = preset.DefaultBaseUrl;
        }
    }

    private static bool IsKnownPresetUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var u = url.Trim().TrimEnd('/').ToLowerInvariant();
        return LlmProviderPresets.All.Any(p =>
            !string.IsNullOrEmpty(p.DefaultBaseUrl) &&
            p.DefaultBaseUrl.TrimEnd('/').ToLowerInvariant() == u);
    }

    /// <summary>获取模型：GET {baseUrl}/models，解析 data[].id 填入下拉。</summary>
    private async void FetchModels_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || !BtnFetchModels.IsEnabled) return;
        var url = UrlBox.Text?.Trim();
        var key = ApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(url)) { SetStatus("请先填写地址", ok: false); return; }
        if (string.IsNullOrWhiteSpace(key)) { SetStatus("请先填写 API Key", ok: false); return; }

        InvalidateRequest();
        using var cts = new CancellationTokenSource();
        _requestCts = cts;
        SetStatus("正在获取模型列表...", ok: true, loading: true);
        BtnFetchModels.IsEnabled = false;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{url.TrimEnd('/')}/models");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var resp = await _http.SendAsync(req, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!resp.IsSuccessStatusCode)
            {
                SetStatus($"获取失败 (HTTP {(int)resp.StatusCode})：{Truncate(body, 120)}", ok: false);
                return;
            }
            var ids = LlmProviderPresets.ParseModelIds(body);
            if (ids.Count == 0)
            {
                SetStatus("未解析到模型列表（响应格式异常），请手动填写", ok: false);
                return;
            }
            // 累积去重：保留所有已获取模型，可重复获取（只 Add 不重设 ItemsSource）
            int added = 0;
            foreach (var id in ids)
            {
                if (!_allModels.Contains(id))
                {
                    _allModels.Add(id);
                    added++;
                }
            }
            _settingModel = true;
            try { ModelBox.Text = ids[0]; } // 默认选本次获取的第一个
            finally { _settingModel = false; }
            SetStatus($"本次获取 {ids.Count} 个模型（新增 {added}，累计 {_allModels.Count}），默认选中 {ids[0]}", ok: true);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closed && !cts.IsCancellationRequested) SetStatus($"获取失败：{ex.Message}", ok: false);
        }
        finally
        {
            if (ReferenceEquals(_requestCts, cts)) _requestCts = null;
            if (!_closed) BtnFetchModels.IsEnabled = true;
        }
    }

    /// <summary>测试 API：发最小 chat completion（max_tokens=1）验证 Key/URL/Model 协同工作。</summary>
    private async void TestApi_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || !BtnTest.IsEnabled) return;
        var url = UrlBox.Text?.Trim();
        var key = ApiKeyBox.Password;
        var model = ModelBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url)) { SetStatus("请先填写地址", ok: false); return; }
        if (string.IsNullOrWhiteSpace(key)) { SetStatus("请先填写 API Key", ok: false); return; }
        if (string.IsNullOrWhiteSpace(model)) { SetStatus("请先填写或获取模型名", ok: false); return; }

        InvalidateRequest();
        using var cts = new CancellationTokenSource();
        _requestCts = cts;
        SetStatus("正在测试 API...", ok: true, loading: true);
        BtnTest.IsEnabled = false;
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model,
                messages = new[] { new { role = "user", content = "hi" } },
                max_tokens = 1
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{url.TrimEnd('/')}/chat/completions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (resp.IsSuccessStatusCode)
            {
                SetStatus("✅ 测试通过 · API/地址/模型均可正常工作", ok: true);
            }
            else
            {
                var detail = await resp.Content.ReadAsStringAsync(cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                SetStatus($"❌ 测试失败 (HTTP {(int)resp.StatusCode})：{Truncate(detail, 120)}", ok: false);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closed && !cts.IsCancellationRequested) SetStatus($"❌ 测试异常：{ex.Message}", ok: false);
        }
        finally
        {
            if (ReferenceEquals(_requestCts, cts)) _requestCts = null;
            if (!_closed) BtnTest.IsEnabled = true;
        }
    }

    /// <summary>保存配置：写回 llmKey/llmBaseUrl/llmModel，关闭对话框（DialogResult=true）。</summary>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text?.Trim();
        var key = ApiKeyBox.Password;
        var model = ModelBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus("地址不能为空", ok: false);
            return;
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            SetStatus("API Key 不能为空", ok: false);
            return;
        }
        if (string.IsNullOrWhiteSpace(model))
        {
            SetStatus("模型名不能为空", ok: false);
            return;
        }

        try
        {
            _store.SaveLlmApiKey(key, url, model);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            SetStatus($"保存失败：{ex.Message}", ok: false);
        }
    }

    private void SetStatus(string text, bool ok, bool loading = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = loading
            ? (Brush)Application.Current.FindResource("TextTertiary")
            : ok
                ? (Brush)Application.Current.FindResource("Accent")
                : (Brush)Application.Current.FindResource("HotColor");
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
