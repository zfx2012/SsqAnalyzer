using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SsqAnalyzer.Pages;

/// <summary>B站 UID 设置弹窗</summary>
public class BiliUidDialog : Window
{
    private readonly TextBox _txtUid;

    public string BiliUid { get; private set; } = "";

    public BiliUidDialog(string currentUid)
    {
        Title = "B站 UID 设置";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));

        var textPrimary = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
        var textSecondary = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
        var primary = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9));
        var borderColor = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD));

        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = "📺 B站 UID 设置",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = textPrimary,
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(new TextBlock
        {
            Text = "B站用户的UID，在B站个人空间页面 URL 中可找到（如 space.bilibili.com/12345，UID 就是 12345）。\n也支持填入 B站视频链接作为默认搜索内容。\n留空则清空默认值。",
            FontSize = 12,
            Foreground = textSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        root.Children.Add(new TextBlock
        {
            Text = "B站 UID",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = textPrimary,
            Margin = new Thickness(0, 0, 0, 6)
        });
        _txtUid = new TextBox
        {
            Text = currentUid,
            FontSize = 13,
            Padding = new Thickness(10, 8, 10, 8),
            Height = 38,
            BorderBrush = borderColor,
            BorderThickness = new Thickness(1.5)
        };
        root.Children.Add(_txtUid);

        // 按钮行
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var btnCancel = new Button
        {
            Content = "取消",
            Width = 80,
            Height = 34,
            Margin = new Thickness(0, 0, 10, 0),
            Background = Brushes.White,
            BorderBrush = borderColor,
            Foreground = textPrimary,
            FontSize = 13
        };
        btnCancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(btnCancel);

        var btnConfirm = new Button
        {
            Content = "确认",
            Width = 80,
            Height = 34,
            Background = primary,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            IsDefault = true
        };
        btnConfirm.Click += (_, _) =>
        {
            var uid = _txtUid.Text.Trim();
            if (uid.Length > 0 &&
                !long.TryParse(uid, out _) &&
                !Uri.TryCreate(uid, UriKind.Absolute, out _))
            {
                MessageBox.Show("请输入 B站 UID（纯数字）或视频链接 URL", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            BiliUid = uid;
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(btnConfirm);

        root.Children.Add(buttons);
        Content = root;
    }
}
