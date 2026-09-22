using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using SsqAnalyzer.Controls;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

namespace SsqAnalyzer.Pages;

public partial class GroupPage : UserControl
{
    private readonly IDataService _data;
    private readonly ITicketRepository _tickets;
    private readonly GroupInputStore _snapshots;
    private readonly GroupService _groups;
    private GroupInputBundle? _inputs;
    private GroupSourceKind _selectedSource = GroupSourceKind.Trend;
    private TrendViewKind _selectedTrendView = TrendViewKind.Basic;
    private DiagonalChainRenderer? _trendPreviewRenderer;
    private int _targetIssue;
    private bool _subscribed;

    public GroupPage() : this(
        App.Services.GetRequiredService<IDataService>(),
        App.Services.GetRequiredService<ITicketRepository>(),
        App.Services.GetRequiredService<GroupInputStore>(),
        App.Services.GetRequiredService<GroupService>()) { }

    public GroupPage(IDataService data, ITicketRepository tickets, GroupInputStore snapshots, GroupService groups)
    {
        _data = data;
        _tickets = tickets;
        _snapshots = snapshots;
        _groups = groups;
        InitializeComponent();
        _trendPreviewRenderer = new DiagonalChainRenderer(TrendPreviewMatrix, TrendPreviewOverlay);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed) return;
        if (!_subscribed)
        {
            _data.DataUpdated += OnInputsChanged;
            _tickets.TicketsChanged += OnInputsChanged;
            _tickets.PeriodChanged += OnInputsChanged;
            _tickets.TypeChanged += OnInputsChanged;
            _snapshots.Changed += OnInputsChanged;
            _subscribed = true;
            _loadVersion++;
        }
        LoadData();
        ApplyLayout(new Size(ActualWidth, ActualHeight));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) return;
        _data.DataUpdated -= OnInputsChanged;
        _tickets.TicketsChanged -= OnInputsChanged;
        _tickets.PeriodChanged -= OnInputsChanged;
        _tickets.TypeChanged -= OnInputsChanged;
        _snapshots.Changed -= OnInputsChanged;
        _subscribed = false;
        _loadVersion++;
        _trendPreviewRenderer?.Clear();
    }

    private int _loadVersion;
    private void OnInputsChanged()
    {
        int version = _loadVersion;
        Dispatcher.InvokeAsync(() =>
        {
            if (_subscribed && version == _loadVersion) RefreshInputs();
        });
    }

    public void LoadData()
    {
        if (_targetIssue <= 0)
        {
            int latest = _data.GetLastPeriod();
            _targetIssue = latest > 0 ? latest + 1 : DateTime.Now.Year * 1000 + 1;
            TargetIssueInput.Text = _targetIssue.ToString();
        }
        RefreshInputs();
    }

    private void RefreshInputs()
    {
        try
        {
            _inputs = _groups.GetInputs(_targetIssue);
            RenderInputs();
        }
        catch (Exception ex)
        {
            GenerateStatusText.Text = ex.Message;
            GenerateStatusText.Foreground = BrushFrom("#EF4444");
        }
    }

    private void RenderInputs()
    {
        if (_inputs is null) return;
        RailTitle.Text = $"{_targetIssue} 期数据轨道";
        RailSummary.Text = $"{_inputs.ReadyCount} 项可用 · {4 - _inputs.ReadyCount} 项未参与 · 所有节点均可点击";
        ReadyCountText.Text = $"{_inputs.ReadyCount}/4";

        SetSourceStatus(GroupSourceKind.Trend, TrendRailDot, TrendRailState, TrendCardState, TrendCardSummary, TrendSourceCard);
        SetSourceStatus(GroupSourceKind.TicketStats, TicketRailDot, TicketRailState, TicketCardState, TicketCardSummary, TicketSourceCard);
        SetSourceStatus(GroupSourceKind.Position, PositionRailDot, PositionRailState, PositionCardState, PositionCardSummary, PositionSourceCard);
        SetSourceStatus(GroupSourceKind.KillPool, KillRailDot, KillRailState, KillCardState, KillCardSummary, KillSourceCard);
        SelectCardVisual();
        RenderEvidence();
        RenderComposer();
        ResultsPanel.Children.Clear();
        bool canGenerate = _inputs.ReadyCount >= GroupService.MinimumReadySources;
        GenerateStatusText.Text = canGenerate
            ? "相同输入与规则版本会生成相同结果。"
            : $"至少需要 {GroupService.MinimumReadySources} 项可用数据才能组号，当前仅有 {_inputs.ReadyCount} 项。";
        GenerateStatusText.Foreground = canGenerate
            ? (Brush)FindResource("TextTertiary")
            : BrushFrom("#C5842B");
    }

    private void SetSourceStatus(GroupSourceKind kind, System.Windows.Shapes.Ellipse dot,
        TextBlock railState, TextBlock cardState, TextBlock cardSummary, Button card)
    {
        var status = _inputs!.Status(kind);
        var brush = StateBrush(status.State);
        string label = StateText(status.State);
        dot.Fill = brush;
        railState.Text = status.Summary;
        railState.Foreground = brush;
        cardState.Text = label;
        cardState.Foreground = brush;
        cardSummary.Text = status.Summary;
        card.Background = status.State == GroupSourceState.Missing
            ? BrushFrom("#FFF8E8")
            : (Brush)FindResource("BgElevated");
    }

    private void SelectSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse(tag, out GroupSourceKind kind))
        {
            _selectedSource = kind;
            SelectCardVisual();
            RenderEvidence();
        }
    }

    private void SelectCardVisual()
    {
        foreach (var (button, kind) in new[]
        {
            (TrendSourceCard, GroupSourceKind.Trend),
            (TicketSourceCard, GroupSourceKind.TicketStats),
            (PositionSourceCard, GroupSourceKind.Position),
            (KillSourceCard, GroupSourceKind.KillPool)
        })
        {
            bool selected = kind == _selectedSource;
            button.BorderBrush = selected ? (Brush)FindResource("Accent") : Brushes.Transparent;
            if (selected) button.Background = (Brush)FindResource("AccentSoft");
            else if (_inputs?.Status(kind).State == GroupSourceState.Missing) button.Background = BrushFrom("#FFF8E8");
            else button.Background = (Brush)FindResource("BgElevated");
        }
    }

    private void RenderEvidence()
    {
        if (_inputs is null) return;
        var status = _inputs.Status(_selectedSource);
        EvidenceTitle.Text = status.Name;
        EvidenceSubtitle.Text = status.Summary;
        EvidenceEmptyText.Visibility = Visibility.Collapsed;
        TrendDetailPanel.Visibility = _selectedSource == GroupSourceKind.Trend ? Visibility.Visible : Visibility.Collapsed;
        TicketDetailPanel.Visibility = _selectedSource == GroupSourceKind.TicketStats ? Visibility.Visible : Visibility.Collapsed;
        PositionDetailPanel.Visibility = _selectedSource == GroupSourceKind.Position ? Visibility.Visible : Visibility.Collapsed;
        KillDetailPanel.Visibility = _selectedSource == GroupSourceKind.KillPool ? Visibility.Visible : Visibility.Collapsed;

        switch (_selectedSource)
        {
            case GroupSourceKind.Trend: RenderTrend(); break;
            case GroupSourceKind.TicketStats: RenderTicketStats(); break;
            case GroupSourceKind.Position: RenderPosition(); break;
            case GroupSourceKind.KillPool: RenderKillPool(); break;
        }
    }

    private void RenderTrend()
    {
        TrendViewSelector.Children.Clear();
        if (_inputs?.Trend is not { } trend)
        {
            ShowEmpty("暂无历史开奖数据。打开走势图同步数据后再返回本页。");
            SnapshotText.Text = "走势快照：无";
            return;
        }

        foreach (var view in trend.Views)
        {
            var button = new Button
            {
                Tag = view.View,
                Style = (Style)FindResource(view.View == _selectedTrendView ? "FilterTabActive" : "FilterTab"),
                Margin = new Thickness(0, 0, view == trend.Views[^1] ? 0 : 6, 0),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = view.Name, FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = $"{view.SampleSize} 期 · {view.FilterDescription}", FontSize = 8, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center }
                    }
                }
            };
            AutomationProperties.SetAutomationId(button, $"TrendView{view.View}");
            button.Click += TrendView_Click;
            TrendViewSelector.Children.Add(button);
        }

        var selected = trend.Views.FirstOrDefault(view => view.View == _selectedTrendView) ?? trend.Views[0];
        _selectedTrendView = selected.View;
        var rows = selected.ChartRows.ToList();
        var previewRows = rows.TakeLast(5).ToList();
        TrendPreviewMatrix.SetFullData(rows);
        TrendPreviewMatrix.Render(rows, true, true, Math.Min(5, rows.Count), selected.View == TrendViewKind.Route012);
        if (previewRows.Count >= 4) _trendPreviewRenderer?.Update(previewRows, 1);
        else _trendPreviewRenderer?.Clear();

        TrendPreviewTitle.Text = $"{selected.Name} · 最近 {Math.Min(5, rows.Count)} 期";
        TrendPreviewCaption.Text = $"{selected.FilterDescription} · 显示遗漏、预测冷号和1级三斜连";
        TrendPreviewCaption.Foreground = (Brush)FindResource("TextTertiary");
        TrendColdRedText.Text = $"红球：{FormatNumbers(selected.ColdRedNumbers)}";
        TrendColdBlueText.Text = $"蓝球：{FormatNumbers(selected.ColdBlueNumbers)}";
        TrendDiagonalRedText.Text = $"红球：{FormatNumbers(selected.DiagonalRedCandidates)}";
        TrendDiagonalBlueText.Text = $"蓝球：{FormatNumbers(selected.DiagonalBlueCandidates)}";
        SnapshotText.Text = $"目标期 {_targetIssue} · 截至 {trend.AsOfIssue} · {selected.Name} · 快照 {trend.SnapshotId}";
    }

    private void TrendView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrendViewKind view })
        {
            _selectedTrendView = view;
            RenderTrend();
        }
    }

    private void TrendPreview_Click(object sender, MouseButtonEventArgs e)
        => OpenSelectedTrendPreview();

    private void OpenTrendPreview_Click(object sender, RoutedEventArgs e)
        => OpenSelectedTrendPreview();

    private void OpenSelectedTrendPreview()
    {
        if (_inputs?.Trend?.Views.FirstOrDefault(view => view.View == _selectedTrendView) is not { } view) return;
        try
        {
            ShowTrendPreview(view);
        }
        catch (Exception ex)
        {
            TrendPreviewCaption.Text = $"无法打开预览：{ex.Message}";
            TrendPreviewCaption.Foreground = BrushFrom("#EF4444");
        }
    }

    private void ShowTrendPreview(TrendViewSnapshot view)
    {
        var rows = view.ChartRows.ToList();
        var matrix = new MatrixGrid { MinWidth = 1180 };
        var overlay = new Grid { IsHitTestVisible = false };
        var chart = new Grid();
        chart.Children.Add(matrix);
        chart.Children.Add(overlay);
        matrix.SetFullData(rows);
        matrix.Render(rows, true, true, Math.Min(5, rows.Count), view.View == TrendViewKind.Route012);

        var window = new Window
        {
            Title = $"{view.Name} · 最近 {Math.Min(5, rows.Count)} 期走势",
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 1480,
            Height = 300,
            MinWidth = 900,
            MinHeight = 280,
            Background = (Brush)FindResource("BgContent"),
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = chart
            }
        };
        window.Loaded += (_, _) => new DiagonalChainRenderer(matrix, overlay).Update(rows.TakeLast(5).ToList(), 1);
        window.Show();
    }

    private void RenderTicketStats()
    {
        RedStatsPanel.Children.Clear();
        BlueStatsPanel.Children.Clear();
        if (_inputs?.TicketStats is not { } stats)
        {
            ShowEmpty("本期尚未载入复式票。打开复式票统计页载入对应期数据后再返回本页。");
            SnapshotText.Text = "复式票快照：无";
            return;
        }

        TicketDetailSummary.Text = $"{stats.TicketCount} 张票；完整保留 33 个红球和 16 个蓝球统计数。";
        foreach (var item in stats.RedCounts.Select((count, index) => (Number: index + 1, Count: count)).OrderBy(x => x.Count).ThenBy(x => x.Number))
            RedStatsPanel.Children.Add(MakeStatChip(item.Number, item.Count, false));
        foreach (var item in stats.BlueCounts.Select((count, index) => (Number: index + 1, Count: count)).OrderBy(x => x.Count).ThenBy(x => x.Number))
            BlueStatsPanel.Children.Add(MakeStatChip(item.Number, item.Count, true));
        SnapshotText.Text = $"数据期 {stats.TargetIssue} · 快照 {stats.SnapshotId} · 统计数越小，策略优先级越高";
    }

    private void RenderPosition()
    {
        PositionBallsPanel.Children.Clear();
        if (_inputs?.Position is not { } position)
        {
            ShowEmpty("本期尚未生成点位。打开点位推荐页生成后会自动回传这 6 个红球点位。");
            SnapshotText.Text = "点位快照：无";
            return;
        }
        foreach (int number in position.RedPoints) PositionBallsPanel.Children.Add(MakeBall(number, false, 34));
        PositionDetailText.Text = "组号仅使用以上 6 个红球点位，不读取蓝球。";
        SnapshotText.Text = $"目标期 {position.TargetIssue} · 截至 {position.AsOfIssue} · {position.RuleVersionId} · 快照 {position.SnapshotId}";
    }

    private void RenderKillPool()
    {
        KillRedPanel.Children.Clear();
        KillBluePanel.Children.Clear();
        if (_inputs?.KillPool is not { } pool)
        {
            ShowEmpty("本期尚未执行杀号。本次组号会使用红球 01～33、蓝球 01～16 全集。");
            SnapshotText.Text = "杀号快照：无";
            return;
        }
        foreach (int number in pool.RemainingRedNumbers) KillRedPanel.Children.Add(MakeNumberChip(number, false));
        foreach (int number in pool.RemainingBlueNumbers) KillBluePanel.Children.Add(MakeNumberChip(number, true));
        SnapshotText.Text = $"目标期 {pool.TargetIssue} · 红 {pool.RemainingRedNumbers.Count} / 蓝 {pool.RemainingBlueNumbers.Count} · 快照 {pool.SnapshotId}";
    }

    private void ShowEmpty(string message)
    {
        TrendDetailPanel.Visibility = Visibility.Collapsed;
        TicketDetailPanel.Visibility = Visibility.Collapsed;
        PositionDetailPanel.Visibility = Visibility.Collapsed;
        KillDetailPanel.Visibility = Visibility.Collapsed;
        EvidenceEmptyText.Text = message;
        EvidenceEmptyText.Visibility = Visibility.Visible;
    }

    private void RenderComposer()
    {
        UsedSourcesPanel.Children.Clear();
        if (_inputs is null) return;
        foreach (var status in _inputs.Statuses)
            UsedSourcesPanel.Children.Add(MakeSourceChip(status));
        GenerateButton.IsEnabled = _inputs.ReadyCount >= GroupService.MinimumReadySources;
        GenerateButton.Content = GenerateButton.IsEnabled
            ? $"使用 {_inputs.ReadyCount} 项数据开始组号"
            : $"至少 {GroupService.MinimumReadySources} 项数据才能组号";
        UpdateBudget();
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TicketCountInput.Text.Trim(), out int count) || count is < 1 or > 20)
        {
            GenerateStatusText.Text = "注数必须是 1～20 的整数。";
            GenerateStatusText.Foreground = BrushFrom("#EF4444");
            return;
        }
        try
        {
            var result = _groups.Generate(_targetIssue, count);
            RenderResults(result);
            GenerateStatusText.Text = $"已生成 {result.Tickets.Count} 注 · 输入 {result.InputId} · {result.RuleVersionId}";
            GenerateStatusText.Foreground = StateBrush(GroupSourceState.Ready);
        }
        catch (Exception ex)
        {
            ResultsPanel.Children.Clear();
            GenerateStatusText.Text = ex.Message;
            GenerateStatusText.Foreground = BrushFrom("#EF4444");
        }
    }

    private void RenderResults(GroupGenerationResult result)
    {
        ResultsPanel.Children.Clear();
        foreach (var ticket in result.Tickets)
        {
            var content = new StackPanel();
            var heading = new Grid();
            heading.Children.Add(new TextBlock { Text = $"方案 {ticket.Index:D2}", FontSize = 10, Foreground = (Brush)FindResource("TextTertiary") });
            heading.Children.Add(new TextBlock { Text = $"策略分 {ticket.StrategyScore:0.##}", FontSize = 9, Foreground = (Brush)FindResource("TextTertiary"), HorizontalAlignment = HorizontalAlignment.Right });
            content.Children.Add(heading);
            var balls = new WrapPanel { Margin = new Thickness(0, 7, 0, 5) };
            foreach (int number in ticket.RedBalls) balls.Children.Add(MakeBall(number, false, 25));
            balls.Children.Add(new TextBlock { Text = "+", Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center });
            balls.Children.Add(MakeBall(ticket.BlueBall, true, 25));
            content.Children.Add(balls);
            content.Children.Add(new TextBlock
            {
                Text = ticket.Evidence,
                FontSize = 8,
                Foreground = (Brush)FindResource("TextTertiary"),
                TextWrapping = TextWrapping.Wrap
            });
            ResultsPanel.Children.Add(new Border
            {
                Background = (Brush)FindResource("BgContent"),
                BorderBrush = (Brush)FindResource("BorderSubtle"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 7),
                Child = content
            });
        }

        string used = result.UsedSources.Count == 0 ? "基础规则" : string.Join("、", result.UsedSources);
        string skipped = result.SkippedSources.Count == 0 ? "无" : string.Join("、", result.SkippedSources);
        ResultsPanel.Children.Add(new TextBlock
        {
            Text = $"使用：{used}\n跳过：{skipped}",
            FontSize = 8,
            Foreground = (Brush)FindResource("TextTertiary"),
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void LoadIssue_Click(object sender, RoutedEventArgs e) => LoadTargetIssue();

    private void TargetIssueInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) LoadTargetIssue();
    }

    private void LoadTargetIssue()
    {
        if (!int.TryParse(TargetIssueInput.Text.Trim(), out int issue) || issue <= 0)
        {
            GenerateStatusText.Text = "请输入有效目标期号。";
            GenerateStatusText.Foreground = BrushFrom("#EF4444");
            return;
        }
        _targetIssue = issue;
        RefreshInputs();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshInputs();

    private void Rail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag }) Navigate(tag);
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e) => Navigate(_selectedSource switch
    {
        GroupSourceKind.Trend => "chart",
        GroupSourceKind.TicketStats => "tickets",
        GroupSourceKind.Position => "position",
        GroupSourceKind.KillPool => "kill",
        _ => "group"
    });

    private void Navigate(string tag)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow) mainWindow.NavigateTo(tag);
    }

    private void TicketCountInput_TextChanged(object sender, TextChangedEventArgs e) => UpdateBudget();

    private void UpdateBudget()
    {
        if (BudgetText is null) return;
        BudgetText.Text = int.TryParse(TicketCountInput?.Text, out int count) && count > 0
            ? $"{count * 2}.00 元"
            : "—";
    }

    private Border MakeStatChip(int number, int count, bool blue)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock { Text = number.ToString("D2"), FontFamily = new FontFamily("Consolas"), FontSize = 9, Foreground = blue ? (Brush)FindResource("BlueBall") : (Brush)FindResource("RedBall") });
        stack.Children.Add(new TextBlock { Text = $" · {count}", FontFamily = new FontFamily("Consolas"), FontSize = 9 });
        return new Border
        {
            Background = count <= 1 ? BrushFrom("#FFF1E3") : (Brush)FindResource("BgElevated"),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(0, 0, 4, 4), Child = stack
        };
    }

    private Border MakeNumberChip(int number, bool blue) => new()
    {
        Background = (Brush)FindResource("BgElevated"), CornerRadius = new CornerRadius(3),
        Padding = new Thickness(5, 3, 5, 3), Margin = new Thickness(0, 0, 4, 4),
        Child = new TextBlock
        {
            Text = number.ToString("D2"), FontFamily = new FontFamily("Consolas"), FontSize = 9,
            Foreground = blue ? (Brush)FindResource("BlueBall") : (Brush)FindResource("RedBall")
        }
    };

    private Border MakeSourceChip(GroupSourceStatus status) => new()
    {
        Background = status.IsReady ? BrushFrom("#E4F4EE") : BrushFrom("#FFF1DC"),
        CornerRadius = new CornerRadius(11), Padding = new Thickness(7, 3, 7, 3),
        Margin = new Thickness(0, 0, 4, 4),
        Child = new TextBlock
        {
            Text = status.IsReady ? status.Name : $"{status.Name}未用",
            FontSize = 8, Foreground = status.IsReady ? BrushFrom("#117353") : BrushFrom("#A46619")
        }
    };

    private BallControl MakeBall(int number, bool blue, double size) => new()
    {
        Number = number,
        BallType = blue ? "blue" : "red",
        Size = size,
        Width = size,
        Height = size,
        Margin = new Thickness(2)
    };

    private void GroupPage_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout(e.NewSize);

    private void ApplyLayout(Size size)
    {
        if (WorkbenchGrid is null) return;
        double width = size.Width;
        double scale = Math.Clamp(width / 1350, 1.0, 1.5);
        PageContent.LayoutTransform = new ScaleTransform(scale, scale);

        var columns = WorkbenchGrid.ColumnDefinitions;
        if (width >= 1050)
        {
            double panelHeight = Math.Max(0, size.Height / scale - 190);
            SourcesPanel.MinHeight = EvidencePanel.MinHeight = ComposerPanel.MinHeight = panelHeight;
            columns[0].Width = new GridLength(240);
            columns[1].Width = new GridLength(1, GridUnitType.Star);
            columns[2].Width = new GridLength(360);
            Place(SourcesPanel, 0, 0, 1, new Thickness(0));
            Place(EvidencePanel, 0, 1, 1, new Thickness(10, 0, 10, 0));
            Place(ComposerPanel, 0, 2, 1, new Thickness(0));
        }
        else if (width >= 760)
        {
            SourcesPanel.MinHeight = EvidencePanel.MinHeight = ComposerPanel.MinHeight = 0;
            columns[0].Width = new GridLength(240);
            columns[1].Width = new GridLength(1, GridUnitType.Star);
            columns[2].Width = new GridLength(0);
            Place(SourcesPanel, 0, 0, 1, new Thickness(0));
            Place(EvidencePanel, 0, 1, 1, new Thickness(10, 0, 0, 0));
            Place(ComposerPanel, 1, 0, 2, new Thickness(0, 10, 0, 0));
        }
        else
        {
            SourcesPanel.MinHeight = EvidencePanel.MinHeight = ComposerPanel.MinHeight = 0;
            columns[0].Width = new GridLength(1, GridUnitType.Star);
            columns[1].Width = new GridLength(0);
            columns[2].Width = new GridLength(0);
            Place(SourcesPanel, 0, 0, 1, new Thickness(0));
            Place(EvidencePanel, 1, 0, 1, new Thickness(0, 10, 0, 0));
            Place(ComposerPanel, 2, 0, 1, new Thickness(0, 10, 0, 0));
        }
    }

    private static void Place(FrameworkElement element, int row, int column, int columnSpan, Thickness margin)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
        element.Margin = margin;
    }

    private Brush StateBrush(GroupSourceState state) => state switch
    {
        GroupSourceState.Ready => BrushFrom("#1C8B67"),
        GroupSourceState.Missing => BrushFrom("#C5842B"),
        GroupSourceState.Stale => BrushFrom("#F59E0B"),
        _ => BrushFrom("#EF4444")
    };

    private static string StateText(GroupSourceState state) => state switch
    {
        GroupSourceState.Ready => "可用",
        GroupSourceState.Missing => "未生成",
        GroupSourceState.Stale => "已过期",
        _ => "异常"
    };

    private static string FormatNumbers(IEnumerable<int> numbers)
    {
        var list = numbers.Take(18).Select(number => number.ToString("D2")).ToList();
        return list.Count == 0 ? "无" : string.Join(" ", list);
    }

    private static Brush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
