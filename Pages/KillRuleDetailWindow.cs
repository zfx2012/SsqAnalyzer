using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SsqAnalyzer.Services.Kill;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages;

/// <summary>
/// 杀号规则详情窗口（代码生成 UI，与 BiliLoginDialog/BiliUidDialog 风格一致）。
/// 接收一条 <see cref="KillRule"/>，分三区展示：基础信息 / 回测详情 / 错误样本。
/// 门槛按球种固定为红球 82%、蓝球 94%。
/// 主题资源键复用 DarkTheme.xaml（BgContent/BgSurface/BgElevated/TextPrimary/...）。
/// </summary>
public class KillRuleDetailWindow : Window
{
    private readonly IKillSettings? _settings;

    public KillRuleDetailWindow(KillRule rule, IKillSettings? settings = null, int periodCount = 50)
    {
        _settings = settings ?? App.Services.GetService<IKillSettings>();
        Title = $"规则详情 - {rule.Name}";
        Width = 780;
        Height = 720;
        MinWidth = 660;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = (Brush)Application.Current.FindResource("BgContent");

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent
        };

        var root = new StackPanel { Margin = new Thickness(16) };

        // 顶部标题
        root.Children.Add(new TextBlock
        {
            Text = $"📋 {rule.Name}",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = rule.RuleId,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = (Brush)Application.Current.FindResource("TextTertiary"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        // A. 基础信息区
        root.Children.Add(BuildSectionTitle("基础信息"));
        root.Children.Add(BuildBasicInfoGrid(rule));

        // B. 回测详情区
        root.Children.Add(BuildSectionTitle("回测详情"));
        root.Children.Add(new TextBlock
        {
            Text = $"当前查看：{(periodCount == 0 ? "全部历史" : $"近 {periodCount} 次触发")}\n"
                + (rule.BacktestStats is null ? "尚未回测" : $"最近回测：{rule.BacktestStats.LastRunAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}")
                + "\n各窗口独立保存；全部窗口至少 30 次触发。失败结果不参与达标判定。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("TextTertiary"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(BuildBacktestTable(rule.BacktestStats));
        foreach (var (name, stat) in WindowRows(rule.BacktestStats))
        {
            root.Children.Add(new TextBlock
            {
                Text = $"{name}：" + (stat?.RunAt is { } run ? run.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "未回测或旧统计时间未知")
                    + (stat?.FirstPeriod is { } first ? $" · 覆盖 {first}—{stat.LastPeriod}" : "")
                    + (stat?.RunAt is not null ? $"\n检查 {stat.EvaluatedCount} 期 · 触发 {stat.TriggeredCount} 次 · 执行失败 {stat.FailureCount} 次" : "")
                    + (stat?.LastExecutionError is { } error ? $"\n最近执行错误：{error}" : ""),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"), FontSize = 11
            });
        }
        if (rule.BacktestStats?.LegacyCombinedWindow is { } legacy)
            root.Children.Add(new TextBlock
            {
                Text = $"旧版 100 次 / 全部混合统计：触发 {legacy.TriggeredCount} 次，准确率 {legacy.Accuracy:P1}。窗口无法确认，仅保留查看，请重新回测。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)Application.Current.FindResource("TextTertiary")
            });

        // C. 错误样本区
        root.Children.Add(BuildSectionTitle("当前窗口错误样本（最多 5 条）"));
        root.Children.Add(BuildErrorSamplesSection(rule.BacktestStats, periodCount));

        var code = new TextBox
        {
            Text = rule.JsCode, IsReadOnly = true, AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas"), FontSize = 12, Height = 180,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(new Expander { Header = "规则代码（可选择并复制）", Content = code, Margin = new Thickness(0, 12, 0, 8) });

        scroll.Content = root;
        var layout = new DockPanel { Background = Background };
        var close = new Button { Content = "关闭", IsCancel = true, MinWidth = 88, Margin = new Thickness(16, 8, 16, 12), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        layout.Children.Add(close);
        layout.Children.Add(scroll);
        Content = layout;
    }

    // ==================== 区块构建 ====================

    private static TextBlock BuildSectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
        Margin = new Thickness(0, 16, 0, 6)
    };

    /// <summary>基础信息 Grid：2 列（标签 + 值），JsCode 预览单独一行高 120。</summary>
    private Grid BuildBasicInfoGrid(KillRule rule)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rows = new (string Label, string Value)[]
        {
            ("规则ID", rule.RuleId),
            ("规则名", rule.Name),
            ("球种", rule.BallType == BallType.Red ? "红球" : "蓝球"),
            ("类别", rule.Category == RuleCategory.Pattern ? "图形" : "公式"),
            ("来源", rule.IsBuiltin ? "内置" : "自定义"),
            ("描述", string.IsNullOrEmpty(rule.Description) ? "—" : rule.Description),
            ("启用状态", rule.IsEnabled ? "启用" : "禁用"),
            ("强制启用", rule.ForceEnabled ? "是" : "否"),
            ("固定门槛", $"{(_settings?.GetMinAccuracy(rule.BallType) ?? KillSettings.For(rule.BallType)):P0}")
        };

        foreach (var (label, value) in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            int r = grid.RowDefinitions.Count - 1;

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = (Brush)Application.Current.FindResource("TextTertiary"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 16, 4)
            };
            Grid.SetRow(lbl, r);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var val = new TextBlock
            {
                Text = value,
                FontSize = 12,
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4)
            };
            Grid.SetRow(val, r);
            Grid.SetColumn(val, 1);
            grid.Children.Add(val);
        }

        return grid;

    }

    /// <summary>回测详情表：近30/50/100次触发与全部历史四行。</summary>
    private static (string Name, BacktestWindowStat? Stat)[] WindowRows(BacktestStatsSnapshot? stats) => new[]
    {
        ("近30次触发", stats?.Window30), ("近50次触发", stats?.Window50),
        ("近100次触发", stats?.Window100), ("全部历史", stats?.WindowAll)
    };

    private static Grid BuildBacktestTable(BacktestStatsSnapshot? stats)
    {
        var grid = new Grid();
        // 列：窗口 / 触发次数 / 杀球数 / 对 / 错 / 准确率 / 状态 / 耗时
        var colWidths = new GridLength[]
        {
            new(104),
            new(64),
            new(60),
            new(46),
            new(46),
            new(64),
            new(70),
            new(70)
        };
        foreach (var w in colWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });

        // 表头行
        grid.RowDefinitions.Add(new RowDefinition());
        var headers = new[] { "窗口", "触发次数", "杀球数", "对", "错", "准确率", "状态", "耗时" };
        for (int c = 0; c < headers.Length; c++)
            grid.Children.Add(BuildCell(headers[c], c, 0, isHeader: true));

        // 数据行
        var windowRows = WindowRows(stats);

        foreach (var (name, w) in windowRows)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            int r = grid.RowDefinitions.Count - 1;

            // 整行空（stats 为 null 或该窗口全空）→ 整行显示 "—"
            bool isEmpty = w is null
                || (w.RunAt is null && w.FailureCount == 0 && w.TriggeredCount == 0 && w.KillBallCount == 0 && w.SampleInsufficient);

            string triggered, kill, correct, wrong, acc, status, elapsed;
            if (isEmpty)
            {
                triggered = kill = correct = wrong = acc = status = elapsed = "—";
            }
            else
            {
                triggered = w!.TriggeredCount.ToString();
                kill = w.KillBallCount.ToString();
                correct = w.CorrectBallCount.ToString();
                wrong = (w.KillBallCount - w.CorrectBallCount).ToString();
                acc = w.KillBallCount == 0 ? "—" : $"{w.Accuracy:P1}";
                status = w.FailureCount > 0 ? $"失败 {w.FailureCount}"
                    : w.SampleInsufficient ? (w.TriggeredCount == 0 ? "无触发" : "样本不足") : "充足";
                elapsed = w.ElapsedMs <= 0 ? "—" : (w.ElapsedMs < 1000 ? $"{w.ElapsedMs} ms" : $"{w.ElapsedMs / 1000.0:F2} s");
            }

            grid.Children.Add(BuildCell(name, 0, r, isRowName: true));
            grid.Children.Add(BuildCell(triggered, 1, r));
            grid.Children.Add(BuildCell(kill, 2, r));
            grid.Children.Add(BuildCell(correct, 3, r));
            grid.Children.Add(BuildCell(wrong, 4, r));
            grid.Children.Add(BuildCell(acc, 5, r, isMono: true));
            grid.Children.Add(BuildCell(status, 6, r));
            grid.Children.Add(BuildCell(elapsed, 7, r, isMono: true));
        }

        return grid;
    }

    /// <summary>构建单个表格单元格（Border + TextBlock）。</summary>
    private static Border BuildCell(string text, int col, int row,
        bool isHeader = false, bool isRowName = false, bool isMono = false)
    {
        var cell = new Border
        {
            BorderBrush = (Brush)Application.Current.FindResource("BorderSubtle"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(6, 4, 6, 4)
        };
        if (isHeader)
            cell.Background = (Brush)Application.Current.FindResource("BgElevated");

        var tb = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = isHeader
                ? (Brush)Application.Current.FindResource("TextSecondary")
                : (isRowName
                    ? (Brush)Application.Current.FindResource("TextPrimary")
                    : (Brush)Application.Current.FindResource("TextSecondary")),
            HorizontalAlignment = (isHeader || !isRowName) ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (isHeader || isRowName)
            tb.FontWeight = FontWeights.SemiBold;
        if (isMono)
            tb.FontFamily = new FontFamily("Consolas");

        cell.Child = tb;
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, col);
        return cell;
    }

    /// <summary>
    /// 错误样本区：从 BacktestStats 取当前明确选择的窗口
    /// 的 ErrorSamples 逐条展示。杀错的球用 HotColor 红色标注。所有窗口均无样本时显示占位。
    /// </summary>
    private static UIElement BuildErrorSamplesSection(BacktestStatsSnapshot? stats, int periodCount)
    {
        // 不跨窗口借用错误样本。
        BacktestWindowStat? chosen = null;
        if (stats is not null)
        {
            chosen = periodCount switch
            {
                30 => stats.Window30,
                50 => stats.Window50,
                100 => stats.Window100,
                _ => stats.WindowAll
            };
        }

        if (chosen is null || chosen.ErrorSamples.Count == 0)
        {
            return new TextBlock
            {
                Text = stats is null ? "尚未回测，暂无错误样本。"
                    : chosen?.FailureCount > 0 ? "当前窗口存在执行失败；没有记录错杀样本不代表规则安全。"
                    : chosen is null || chosen.TriggeredCount == 0 ? "当前窗口无有效样本。"
                    : "当前窗口未记录错误样本。",
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)Application.Current.FindResource("TextTertiary"),
                Margin = new Thickness(0, 4, 0, 16)
            };
        }

        var hotColor = (Brush)Application.Current.FindResource("HotColor");
        var textSecondary = (Brush)Application.Current.FindResource("TextSecondary");
        var textTertiary = (Brush)Application.Current.FindResource("TextTertiary");

        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 16) };

        foreach (var s in chosen.ErrorSamples)
        {
            var card = new Border
            {
                Background = (Brush)Application.Current.FindResource("BgElevated"),
                BorderBrush = (Brush)Application.Current.FindResource("BorderSubtle"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var cardPanel = new StackPanel();

            // 期号
            var periodLine = new TextBlock { FontSize = 12, Margin = new Thickness(0, 0, 0, 2) };
            var periodRun = new Run($"期号 {s.Period}")
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("TextPrimary")
            };
            periodLine.Inlines.Add(periodRun);
            cardPanel.Children.Add(periodLine);

            // 杀了 / 实际开出
            cardPanel.Children.Add(BuildSampleLine("杀了", FormatBalls(s.KilledBalls), textSecondary));
            cardPanel.Children.Add(BuildSampleLine("实际开出", FormatBalls(s.ActualNextBalls), textSecondary));

            // 杀错的（红色）
            if (s.HitBalls.Count > 0)
            {
                cardPanel.Children.Add(BuildSampleLine("杀错的", FormatBalls(s.HitBalls), hotColor));
            }

            card.Child = cardPanel;
            panel.Children.Add(card);
        }

        return panel;
    }

    private static TextBlock BuildSampleLine(string label, string value, Brush valueBrush)
    {
        var tb = new TextBlock { FontSize = 12, Margin = new Thickness(0, 1, 0, 1) };
        tb.Inlines.Add(new Run($"{label}：")
        {
            Foreground = (Brush)Application.Current.FindResource("TextTertiary")
        });
        tb.Inlines.Add(new Run(value)
        {
            Foreground = valueBrush,
            FontFamily = new FontFamily("Consolas")
        });
        return tb;
    }

    private static string FormatBalls(IReadOnlyList<int> balls)
        => balls.Count == 0 ? "—" : string.Join(",", balls.Select(b => b.ToString("D2")));
}
