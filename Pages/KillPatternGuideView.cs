using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SsqAnalyzer.Services.Kill;

namespace SsqAnalyzer.Pages;

internal static class KillPatternGuideView
{
    internal static FrameworkElement Build(BuiltinPatternGuide guide)
    {
        var panel = new StackPanel();
        TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"), Margin = new Thickness(0, 4, 0, 4) };
        panel.Children.Add(Text($"{guide.Kind} · {guide.Scope}"));
        panel.Children.Add(Text("识别条件：" + guide.Shape));
        if (guide.Columns.Length > 0)
        {
            panel.Children.Add(Text("结构示意 · 不代表当前期已触发"));
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            foreach (var _ in guide.Columns) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            for (int row = 0; row <= guide.Cells.Length; row++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(29) });
            void Cell(int row, int col, string text)
            {
                var label = Text(text); label.Margin = new Thickness(4, 2, 4, 2);
                label.HorizontalAlignment = col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                if (text == "×") { label.Foreground = Brushes.Orange; label.FontWeight = FontWeights.Bold; }
                Grid.SetRow(label, row); Grid.SetColumn(label, col); grid.Children.Add(label);
            }
            for (int col = 0; col < guide.Columns.Length; col++) Cell(0, col + 1, guide.Columns[col]);
            for (int row = 0; row < guide.Cells.Length; row++)
            {
                Cell(row + 1, 0, guide.RowLabels[row]);
                for (int col = 0; col < guide.Columns.Length; col++) Cell(row + 1, col + 1, guide.Cells[row][col].ToString());
            }
            panel.Children.Add(grid);
            panel.Children.Add(Text("● 必须开出   ○ 必须未开   · 不限制   × 下一行杀号候选"));
        }
        panel.Children.Add(Text("杀号位置：" + guide.Target));
        panel.Children.Add(Text("附加条件：" + guide.Condition));
        panel.Children.Add(Text("图示说明形态与位置；正式结果还需通过附加条件，规律形态本身不保证下一期不开出。"));
        return new Border { Background = (Brush)Application.Current.FindResource("BgSurface"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = panel };
    }
}
