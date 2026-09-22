using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SsqAnalyzer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages;

public class DataFilePicker : Window
{
    private readonly ListBox _lstFiles;
    private readonly ITicketStore _store;
    public string? SelectedFile { get; private set; }

    public DataFilePicker() : this(App.Services.GetRequiredService<ITicketStore>()) { }

    public DataFilePicker(ITicketStore store)
    {
        _store = store;
        Title = "选择数据文件";
        Width = 450;
        Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 350;
        MinHeight = 300;
        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock { Text = "📁 本地数据文件", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)), Margin = new Thickness(0, 0, 0, 12) });

        _lstFiles = new ListBox
        {
            FontSize = 13,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),
            MinHeight = 180,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _lstFiles.ItemContainerStyle = new Style(typeof(ListBoxItem));
        _lstFiles.ItemContainerStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 5, 8, 5)));

        var files = _store.ListDataFiles();
        foreach (var f in files)
        {
            var item = new ListBoxItem
            {
                Content = Path.GetFileName(f),
                Tag = f,
                ToolTip = f
            };
            _lstFiles.Items.Add(item);
        }

        if (_lstFiles.Items.Count == 0)
            _lstFiles.Items.Add(new ListBoxItem { Content = "(无数据文件)", IsEnabled = false });

        _lstFiles.MouseDoubleClick += (_, _) => ConfirmSelection();
        panel.Children.Add(_lstFiles);

        // 按钮行
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new Button { Content = "取消", Width = 80, Height = 32, Margin = new Thickness(0, 0, 10, 0), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)), Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)), FontSize = 13 };
        btnCancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(btnCancel);

        var btnOk = new Button { Content = "📥 加载", Width = 80, Height = 32, Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 13, FontWeight = FontWeights.SemiBold };
        btnOk.Click += (_, _) => ConfirmSelection();
        buttons.Children.Add(btnOk);
        panel.Children.Add(buttons);

        Content = panel;
    }

    private void ConfirmSelection()
    {
        if (_lstFiles.SelectedItem is ListBoxItem item && item.Tag is string path)
        {
            SelectedFile = path;
            DialogResult = true;
            Close();
        }
    }
}
