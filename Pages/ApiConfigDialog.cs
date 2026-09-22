using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SsqAnalyzer.Services;

namespace SsqAnalyzer.Pages;

public class ApiConfigDialog : Window
{
    private readonly TextBox _txtApiKey;
    private readonly TextBox _txtWsId;
    private readonly TextBox _txtApiHost;
    private readonly ComboBox _cmbModel;
    private readonly TextBlock _txtStatus;
    private readonly string _originalKey;
    private static readonly (string label, string tag)[] KnownModels = new[]
    {
        ("qwen3.7-plus (推荐)", "qwen3.7-plus"),
        ("qwen3.6-plus", "qwen3.6-plus"),
        ("qwen3.5-plus", "qwen3.5-plus"),
        ("qwen3-vl-plus", "qwen3-vl-plus"),
    };
    public string ApiKey { get; private set; } = string.Empty;
    public string WorkspaceId { get; private set; } = string.Empty;
    public string ApiHost { get; private set; } = string.Empty;
    public string Model { get; private set; } = "qwen3-vl-plus";

    public ApiConfigDialog(string? currentKey, string? currentModel, string? currentWsId = null, string? currentApiHost = null)
    {
        _originalKey = currentKey ?? "";
        Title = "API 设置 - 千问 DashScope";
        Width = 560;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 480;
        MinHeight = 560;
        WindowStyle = WindowStyle.ToolWindow;
        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));

        var textPrimary = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
        var textSecondary = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
        var primary = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9));
        var danger = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
        var borderColor = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD));

        // 主容器
        var rootPanel = new StackPanel { Margin = new Thickness(20), Orientation = Orientation.Vertical };

        // 标题
        rootPanel.Children.Add(new TextBlock { Text = "⚙ 配置千问 API", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 6) });
        rootPanel.Children.Add(new TextBlock { Text = "请填入阿里云 DashScope API Key、业务空间 ID 和推理 API 域名。配置将被加密存储在本地。", FontSize = 12, Foreground = textSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        // 状态
        _txtStatus = new TextBlock { FontSize = 11, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        SetStatus(currentKey ?? "");
        rootPanel.Children.Add(_txtStatus);

        // API Key
        rootPanel.Children.Add(CreateLabel("🔑 API Key"));
        _txtApiKey = CreateTextBox(DisplayKey(currentKey ?? ""), "在阿里云 DashScope 控制台获取");
        rootPanel.Children.Add(_txtApiKey);

        // 业务空间 ID
        rootPanel.Children.Add(CreateLabel("🏢 业务空间 ID (WorkspaceId)"));
        _txtWsId = CreateTextBox(currentWsId ?? "", "百炼控制台右上角可找到 Workspace ID");
        rootPanel.Children.Add(_txtWsId);

        // API Host
        rootPanel.Children.Add(CreateLabel("🌐 推理 API 域名 (API Host)"));
        _txtApiHost = CreateTextBox(currentApiHost ?? "", "业务空间专属域名，如 llm-xxx.cn-beijing.maas.aliyuncs.com，留空使用默认");
        rootPanel.Children.Add(_txtApiHost);

        // 模型
        rootPanel.Children.Add(CreateLabel("🤖 分析模型"));
        _cmbModel = new ComboBox { FontSize = 13, Padding = new Thickness(8, 6, 8, 6), Height = 36, BorderBrush = borderColor, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var m in KnownModels)
            _cmbModel.Items.Add(new ComboBoxItem { Content = m.label, Tag = m.tag });
        foreach (ComboBoxItem item in _cmbModel.Items)
            if (item.Tag?.ToString() == currentModel) { item.IsSelected = true; break; }
        if (_cmbModel.SelectedIndex < 0) _cmbModel.SelectedIndex = 0;
        rootPanel.Children.Add(_cmbModel);

        rootPanel.Children.Add(new TextBlock { Text = "💡 VL系列: 纯视觉视频分析 | 3.7-plus: 最新模型 | flash: 轻量快速", FontSize = 11, Foreground = textSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        // 帮助链接
        var help = new TextBlock { FontSize = 12, Foreground = textSecondary, Margin = new Thickness(0, 0, 0, 20), TextWrapping = TextWrapping.Wrap };
        var link = new Hyperlink { NavigateUri = new System.Uri("https://dashscope.console.aliyun.com/apiKey"), Foreground = primary };
        link.Inlines.Add("DashScope API Key 管理");
        link.RequestNavigate += OpenUrl;
        help.Inlines.Add("📖 ");
        help.Inlines.Add(link);
        rootPanel.Children.Add(help);

        // 按钮行
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };

        var btnClear = new Button { Content = "🗑 清除配置", Width = 90, Height = 34, Margin = new Thickness(0, 0, 10, 0), Background = danger, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12 };
        btnClear.Click += (_, _) => { ApiKey = ""; WorkspaceId = ""; ApiHost = ""; Model = ""; DialogResult = true; Close(); };
        buttons.Children.Add(btnClear);

        buttons.Children.Add(new FrameworkElement { Width = 60 });

        var btnCancel = new Button { Content = "取消", Width = 80, Height = 34, Margin = new Thickness(0, 0, 10, 0), Background = Brushes.White, BorderBrush = borderColor, Foreground = textPrimary, FontSize = 13 };
        btnCancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(btnCancel);

        var btnSave = new Button { Content = "💾 保存", Width = 80, Height = 34, Background = primary, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 13, FontWeight = FontWeights.SemiBold };
        btnSave.Click += (_, _) =>
        {
            var key = _txtApiKey.Text.Trim();
            // 如果 Key 包含星号（掩码显示状态），使用原始 Key
            if (!string.IsNullOrEmpty(_originalKey) && key.Contains('*'))
                key = _originalKey;
            if (string.IsNullOrWhiteSpace(key)) { MessageBox.Show("请输入有效的 API Key。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); _txtApiKey.Focus(); return; }
            if (!key.StartsWith("sk-"))
            {
                var r = MessageBox.Show("API Key 通常以 'sk-' 开头，确认输入正确？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }
            ApiKey = key;
            WorkspaceId = _txtWsId.Text.Trim();
            ApiHost = _txtApiHost.Text.Trim();
            Model = (_cmbModel.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "qwen3-vl-plus";
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(btnSave);

        rootPanel.Children.Add(buttons);

        // 使用 ScrollViewer 包裹
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rootPanel };
        Content = scroll;
    }

    private static TextBlock CreateLabel(string text)
        => new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)), Margin = new Thickness(0, 12, 0, 6) };

    private static TextBox CreateTextBox(string text, string tooltip)
        => new TextBox { FontSize = 13, Padding = new Thickness(10, 8, 10, 8), Height = 38, BorderBrush = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)), BorderThickness = new Thickness(1.5), Text = text, ToolTip = tooltip };

    private void SetStatus(string currentKey)
    {
        if (!string.IsNullOrEmpty(currentKey))
        {
            _txtStatus.Text = $"✅ 已配置: {MaskKey(currentKey)}";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
        }
        else
        {
            _txtStatus.Text = "⚠ 请填写 API Key";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
        }
    }

    private static string MaskKey(string key) =>
        string.IsNullOrEmpty(key) || key.Length <= 8 ? new string('*', key?.Length ?? 0) : key[..4] + new string('*', key.Length - 8) + key[^4..];

    /// <summary>显示掩码的 API Key（只保留首尾4位）</summary>
    private static string DisplayKey(string key) =>
        string.IsNullOrEmpty(key) || key.Length <= 8 ? "" : key[..4] + new string('*', key.Length - 8) + key[^4..];

    private static void OpenUrl(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = e.Uri.ToString(), UseShellExecute = true });
}
