using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SsqAnalyzer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages;

public class BiliLoginDialog : Window
{
    private readonly Image _qrcodeImage;
    private readonly TextBlock _statusText;
    private readonly Button _btnCancel;
    private string? _qrcodeKey;
    private bool _loggedIn;
    private bool _closed;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loginStarted;
    private bool _loginFinished;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public BiliLoginDialog()
    {
        Title = "登录B站";
        Width = 300;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
        Topmost = true;

        var panel = new StackPanel { Margin = new Thickness(16), VerticalAlignment = VerticalAlignment.Center };

        panel.Children.Add(new TextBlock
        {
            Text = "请使用B站App扫描二维码登录",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        });

        _qrcodeImage = new Image
        {
            Width = 200,
            Height = 200,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(_qrcodeImage);

        _statusText = new TextBlock
        {
            Text = "正在获取二维码…",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(_statusText);

        _btnCancel = new Button
        {
            Content = "取消",
            Width = 80,
            Height = 32,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _btnCancel.Click += (_, _) => { _closed = true; DialogResult = false; Close(); };
        panel.Children.Add(_btnCancel);

        Content = panel;
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _closed = true;
            if (!_loginFinished) _lifetime.Cancel();
            if (!_loginStarted) { _loginFinished = true; _lifetime.Dispose(); }
        };
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_closed || _loginStarted) return;
        _loginStarted = true;
        try { await StartLogin(); }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex) { if (!_closed) _statusText.Text = $"登录异常: {ex.Message}"; }
        finally { _loginFinished = true; _lifetime.Dispose(); }
    }

    private async Task StartLogin()
    {
        try
        {
            // 步骤1: 获取二维码
            var genResp = await _http.GetStringAsync("https://passport.bilibili.com/x/passport-login/web/qrcode/generate", _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            JsonNode? genRoot;
            try { genRoot = JsonNode.Parse(genResp); }
            catch { _statusText.Text = "二维码服务器返回异常"; return; }
            if (genRoot?["code"]?.GetValue<int>() != 0)
            {
                _statusText.Text = "获取二维码失败";
                return;
            }
            var qrUrl = genRoot["data"]?["url"]?.ToString() ?? "";
            _qrcodeKey = genRoot["data"]?["qrcode_key"]?.ToString() ?? "";

            // 显示二维码（用QR API生成图片，直接显示url文本或使用第三方服务）
            // 最简单方式：用 qrserver.com API 生成二维码图片
            var qrImageUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=200x200&data={Uri.EscapeDataString(qrUrl)}";
            var imgBytes = await _http.GetByteArrayAsync(qrImageUrl, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            using var ms = new System.IO.MemoryStream(imgBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = ms;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            _qrcodeImage.Source = bitmap;
            _statusText.Text = "请用B站App扫码";

            // 步骤2: 轮询扫码状态
            await PollLogin();
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex)
        {
            if (!_closed) _statusText.Text = $"连接失败: {ex.Message}";
        }
    }

    private async Task PollLogin()
    {
        var startTime = DateTime.Now;
        const int timeoutSeconds = 120;
        while (!_closed && !_loggedIn)
        {
            if ((DateTime.Now - startTime).TotalSeconds > timeoutSeconds)
            {
                _statusText.Text = $"⏰ 扫码超时（{timeoutSeconds}秒），请重试";
                return;
            }
            await Task.Delay(1500, _lifetime.Token);
            try
            {
                var pollResp = await _http.GetStringAsync(
                    $"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key={_qrcodeKey}", _lifetime.Token);
                _lifetime.Token.ThrowIfCancellationRequested();
                JsonNode? root;
                try { root = JsonNode.Parse(pollResp); }
                catch { continue; }
                var code = root?["code"]?.GetValue<int>() ?? -1;
                var data = root?["data"];

                switch (code)
                {
                    case 0 when data != null:
                        // 登录成功，提取 Cookie
                        var url = data["url"]?.ToString() ?? "";
                        var sessdata = ExtractCookieValue(url, "SESSDATA");
                        var biliJct = ExtractCookieValue(url, "bili_jct");
                        var buvid3 = ExtractCookieValue(url, "buvid3") ?? "auto";
                        var fullCookie = $"SESSDATA={sessdata}; bili_jct={biliJct}; buvid3={buvid3}";
                        App.Services.GetRequiredService<ITicketStore>().SaveBiliCookie(fullCookie, sessdata, biliJct, buvid3);
                        _loggedIn = true;
                        _statusText.Text = "✅ 登录成功";
                        await Task.Delay(500, _lifetime.Token);
                        DialogResult = true;
                        Close();
                        return;
                    case 86038:
                        _statusText.Text = "二维码已过期，点击取消重新搜索";
                        return;
                    case 86101:
                        _statusText.Text = "等待扫码…";
                        break;
                    case 86090:
                        _statusText.Text = "已扫码，请在手机上确认";
                        break;
                    default:
                        _statusText.Text = $"状态异常 (code={code})";
                        break;
                }
            }
            catch (OperationCanceledException) when (_closed) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BiliLoginDialog.PollLogin] 轮询异常: {ex.Message}");
            }
        }
    }

    private static string ExtractCookieValue(string url, string key)
    {
        var search = key + "=";
        var idx = url.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        idx += search.Length;
        var end = url.IndexOf('&', idx);
        if (end < 0) end = url.Length;
        return Uri.UnescapeDataString(url[idx..end]);
    }
}
