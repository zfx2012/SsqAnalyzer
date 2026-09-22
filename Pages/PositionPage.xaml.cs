using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

namespace SsqAnalyzer.Pages;

public partial class PositionPage : UserControl
{
    private static readonly Brush GoldBrush = BrushFrom("#FFD700");
    private static readonly Brush PaleGoldBrush = BrushFrom("#FFF8E1");
    private static readonly Brush RowHeaderBrush = BrushFrom("#F1F1F1");

    private readonly IDataService _dataService;
    private readonly IPositionPredictor _predictor;
    private readonly IPositionValidationStore _validationStore;
    private readonly GroupInputStore _groupInputs;
    private PositionPrediction? _prediction;
    private bool _suppressIssueChange;
    private string _selectedCopyText = "";
    private int _statusVersion;
    private int _loadVersion;
    private bool _subscribed;
    private CancellationTokenSource? _backtestCts;
    private CancellationTokenSource? _exportCts;

    private int BeginStatus()
    {
        StatusText.ToolTip = null;
        return ++_statusVersion;
    }

    public PositionPage() : this(
        App.Services.GetRequiredService<IDataService>(),
        App.Services.GetRequiredService<IPositionPredictor>(),
        App.Services.GetRequiredService<IPositionValidationStore>(),
        App.Services.GetRequiredService<GroupInputStore>()) { }

    public PositionPage(
        IDataService dataService,
        IPositionPredictor predictor,
        IPositionValidationStore validationStore,
        GroupInputStore groupInputs)
    {
        _dataService = dataService;
        _predictor = predictor;
        _validationStore = validationStore;
        _groupInputs = groupInputs;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_subscribed) return;
        _subscribed = true;
        _loadVersion++;
        _dataService.DataUpdated += OnDataUpdated;
        _validationStore.Changed += OnValidationChanged;
        RuleVersionText.Text = _predictor.RuleVersionId;
        try
        {
            _validationStore.Reconcile();
        }
        catch (Exception)
        {
            // RefreshForwardStatus displays the durable ledger error.
        }
        LoadIssues();
        RefreshForwardStatus();
        Focus();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _subscribed = false;
        _loadVersion++;
        BeginStatus();
        _backtestCts?.Cancel();
        _exportCts?.Cancel();
        _dataService.DataUpdated -= OnDataUpdated;
        _validationStore.Changed -= OnValidationChanged;
    }

    private void OnDataUpdated()
    {
        int version = _loadVersion;
        Dispatcher.InvokeAsync(() =>
        {
            if (!_subscribed || version != _loadVersion) return;
            int? selected = IssueBox.SelectedItem as int?;
            LoadIssues(selected);
            RefreshForwardStatus();
        });
    }

    private void OnValidationChanged()
    {
        int version = _loadVersion;
        Dispatcher.InvokeAsync(() =>
        {
            if (_subscribed && version == _loadVersion) RefreshForwardStatus();
        });
    }

    public void LoadData() => LoadIssues(IssueBox.SelectedItem as int?);

    private void LoadIssues(int? preferredIssue = null)
    {
        BeginStatus();
        _backtestCts?.Cancel();
        var issues = _predictor.GetIssueOptions();
        _suppressIssueChange = true;
        IssueBox.ItemsSource = issues;
        if (issues.Count == 0)
        {
            IssueBox.SelectedItem = null;
            _prediction = null;
            ClearView("暂无开奖数据，无法生成点位");
            _suppressIssueChange = false;
            return;
        }

        int defaultIssue = preferredIssue.HasValue && issues.Contains(preferredIssue.Value)
            ? preferredIssue.Value
            : issues[0];
        IssueBox.SelectedItem = defaultIssue;
        _suppressIssueChange = false;
        GeneratePrediction();
    }

    private void Generate_Click(object sender, RoutedEventArgs e) => GeneratePrediction();

    private void IssueBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressIssueChange && IsLoaded)
            GeneratePrediction();
    }

    private void GeneratePrediction()
    {
        BeginStatus();
        _backtestCts?.Cancel();
        if (IssueBox.SelectedItem is not int issue) return;
        GenerateButton.IsEnabled = false;
        StatusText.Text = "正在计算点位...";
        try
        {
            _prediction = _predictor.Predict(issue, issue > _dataService.GetLastPeriod() ? "live" : "backtest");
            _groupInputs.SavePosition(_prediction);
            RenderPrediction(_prediction);
            RenderScoreDetails(_prediction);
            StatusText.Text = _prediction.IsDrawn
                ? $"已生成 {_prediction.Issue} 期并完成评估"
                : $"已生成 {_prediction.Issue} 期，等待开奖";
            RefreshForwardStatus();
        }
        catch (Exception ex)
        {
            _prediction = null;
            ClearView(ex.Message);
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    private void RenderPrediction(PositionPrediction prediction)
    {
        PredictionGrid.Children.Clear();
        PredictionGrid.RowDefinitions.Clear();
        PredictionGrid.ColumnDefinitions.Clear();
        PredictionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        PredictionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
        PredictionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
        for (int i = 0; i < 6; i++)
            PredictionGrid.RowDefinitions.Add(new RowDefinition { Height = i is 0 or 1 ? GridLength.Auto : new GridLength(44) });

        AddHeader("维度", 0);
        var predictedHeader = AddHeader($"{prediction.Issue} 期（预测）", 1);
        predictedHeader.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ClickCount == 2) ToggleDetails();
        };
        AddHeader($"{prediction.Issue} 期（开奖）", 2);

        var actual = prediction.Actual;
        int? actualBlue = actual?.BlueBall;

        AddRow(1, "点位 ±1",
            BuildNumberPanel(prediction.RedPoints, false, null),
            actual is null ? BuildPendingPanel() : BuildNumberPanel(
                actual.RedBalls,
                false,
                actual.RedBalls
                    .Where(number => prediction.RedPoints.Any(point => PositionPointRange.Contains(point, number, 33)))
                    .ToHashSet()));
        AddRow(2, "独蓝",
            BuildNumberPanel(new[] { prediction.SingleBlue }, true, actualBlue.HasValue ? new HashSet<int> { actualBlue.Value } : null),
            BuildBlueActual(actual, new[] { prediction.SingleBlue }));
        AddRow(3, "两码围蓝",
            BuildNumberPanel(prediction.DoubleBlue, true, actualBlue.HasValue ? new HashSet<int> { actualBlue.Value } : null),
            BuildBlueActual(actual, prediction.DoubleBlue));
        AddRow(4, "三码围蓝",
            BuildNumberPanel(prediction.TripleBlue, true, actualBlue.HasValue ? new HashSet<int> { actualBlue.Value } : null),
            BuildBlueActual(actual, prediction.TripleBlue));
        AddHitRow(prediction);

        CutoffText.Text = $"截止 {prediction.AsOfIssue}";
        SnapshotText.Text = $"snapshot {prediction.SnapshotId} · run {prediction.RunId}";
    }

    private Border AddHeader(string text, int column)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("BgElevated"),
            BorderBrush = (Brush)FindResource("BorderDefault"),
            BorderThickness = new Thickness(column == 0 ? 0 : 1, 0, 0, 1),
            Padding = new Thickness(10, 9, 10, 9),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontFamily = column == 0 ? new FontFamily("Segoe UI") : new FontFamily("Consolas")
            }
        };
        Grid.SetColumn(border, column);
        PredictionGrid.Children.Add(border);
        return border;
    }

    private void AddRow(int row, string label, UIElement predicted, UIElement actual)
    {
        var header = CellBorder(new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Center
        }, RowHeaderBrush, leftBorder: false);
        AddCell(header, row, 0);

        var predictionCell = CellBorder(predicted, (Brush)FindResource("BgSurface"));
        var actualCell = CellBorder(actual, (Brush)FindResource("BgSurface"));
        string copyText = ExtractCopyText(label);
        predictionCell.MouseLeftButtonDown += (_, _) => SelectRow(copyText, predictionCell);
        predictionCell.ContextMenu = BuildRowMenu(copyText);
        AddCell(predictionCell, row, 1);
        AddCell(actualCell, row, 2);
    }

    private void AddHitRow(PositionPrediction prediction)
    {
        int row = 5;
        var header = CellBorder(new TextBlock
        {
            Text = "命中",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Center
        }, RowHeaderBrush, false);
        AddCell(header, row, 0);

        var evaluation = prediction.Evaluation;
        Brush background = evaluation is null ? (Brush)FindResource("BgSurface")
            : evaluation.HitPoints >= 4 ? GoldBrush
            : evaluation.HitPoints >= 3 ? PaleGoldBrush
            : (Brush)FindResource("BgSurface");
        string value = evaluation is null ? "待开奖" : $"{evaluation.HitLabel}   {evaluation.RatingLabel}";
        var hitText = new TextBlock
        {
            Text = value,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = evaluation is null ? "等待开奖结果" : $"命中评级：{evaluation.RatingLabel}"
        };
        AddCell(CellBorder(hitText, background), row, 1);
        var actualText = new TextBlock
        {
            Text = prediction.Actual is null ? "" : prediction.Actual.DateLabel,
            FontSize = 13,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        AddCell(CellBorder(actualText, background), row, 2);
    }

    private UIElement BuildNumberPanel(IEnumerable<int> numbers, bool blue, ISet<int>? hits, Brush? medal = null)
    {
        var panel = BasePanel();
        foreach (var number in numbers)
        {
            bool hit = hits?.Contains(number) == true;
            var background = hit ? medal ?? PaleGoldBrush : Brushes.Transparent;
            var foreground = hit && medal is not null ? Brushes.White
                : blue && hit ? (Brush)FindResource("BlueBall")
                : Brushes.Black;
            panel.Children.Add(new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(2),
                ToolTip = hit ? $"号码 {number:D2} 命中" : null,
                Child = new TextBlock
                {
                    Text = number.ToString("D2"),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 15,
                    FontWeight = hit ? FontWeights.Bold : FontWeights.SemiBold,
                    Foreground = foreground
                }
            });
        }
        if (panel.Children.Count == 0)
            panel.Children.Add(MutedText("—"));
        return panel;
    }

    private UIElement BuildBlueActual(DrawRecord? actual, IEnumerable<int> candidates)
    {
        if (actual is null) return BuildPendingPanel();
        return BuildNumberPanel(new[] { actual.BlueBall }, true,
            candidates.Contains(actual.BlueBall) ? new HashSet<int> { actual.BlueBall } : null);
    }

    private static WrapPanel BasePanel() => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private UIElement BuildPendingPanel() => MutedText("待开奖");

    private TextBlock MutedText(string text) => new()
    {
        Text = text,
        Foreground = Brushes.Black,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 13
    };

    private Border CellBorder(UIElement child, Brush background, bool leftBorder = true) => new()
    {
        Background = background,
        BorderBrush = (Brush)FindResource("BorderSubtle"),
        BorderThickness = new Thickness(leftBorder ? 1 : 0, 0, 0, 1),
        Padding = new Thickness(8, 5, 8, 5),
        Child = child
    };

    private void AddCell(UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        PredictionGrid.Children.Add(element);
    }

    private ContextMenu BuildRowMenu(string copyText)
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "复制此行" };
        copy.Click += (_, _) => Clipboard.SetText(copyText);
        menu.Items.Add(copy);
        return menu;
    }

    private void SelectRow(string copyText, Border cell)
    {
        _selectedCopyText = copyText;
        BeginStatus();
        StatusText.Text = "已选择，可按 Ctrl+C 复制";
        cell.Focus();
    }

    private string ExtractCopyText(string label)
    {
        if (_prediction is null) return "";
        return label switch
        {
            "点位 ±1" => FormatNumbers(_prediction.RedPoints),
            "独蓝" => _prediction.SingleBlue.ToString("D2"),
            "两码围蓝" => FormatNumbers(_prediction.DoubleBlue),
            "三码围蓝" => FormatNumbers(_prediction.TripleBlue),
            _ => ""
        };
    }

    private void RenderScoreDetails(PositionPrediction prediction)
    {
        ScoreGrid.ItemsSource = prediction.RedScores.Select(s => ScoreRow.From("红球", s))
            .Concat(prediction.BlueScores.Select(s => ScoreRow.From("蓝球", s)))
            .ToList();
    }

    private void RefreshForwardStatus()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_validationStore.LastError))
                throw new InvalidDataException(_validationStore.LastError);

            var summary = _validationStore.GetSummary(_predictor.RuleVersionId);
            var shadow = _validationStore.GetSummaries()
                .FirstOrDefault(item => item.RuleVersionId.StartsWith(
                    PositionPredictor.CurrentShadowRuleVersion,
                    StringComparison.Ordinal));
            var comparison = shadow is null ? null
                : _validationStore.CompareVersions(summary.RuleVersionId, shadow.RuleVersionId);
            string shadowStatus = shadow is null ? ""
                : $"\n影子点位 {shadow.EvaluatedCount} 已结算 / {shadow.PendingCount} 待开奖，覆盖命中 {shadow.AveragePointHits:F2}，范围命中 {shadow.AverageRangePointHits:F2}"
                  + $"\n点位对照：{comparison!.PointVerdict}";
            ForwardStatusText.Text = $"前向 {summary.EvaluatedCount} 已结算 / {summary.PendingCount} 待开奖";
            ForwardStatusText.ToolTip = $"独蓝 {summary.SingleBlueHitRate:P2}（95%下界 {summary.SingleBlueWilsonLower95:P2}）\n"
                + $"前/后段 {summary.FirstHalfSingleBlueHitRate:P2} / {summary.SecondHalfSingleBlueHitRate:P2}\n"
                + $"两码 {summary.DoubleBlueHitRate:P2}，三码 {summary.TripleBlueHitRate:P2}\n"
                + $"点位均中 {summary.AveragePointHits:F2}，随机 {summary.RandomAveragePointHits:F2}，提升 {summary.PointLift:+0.00;-0.00;0.00}\n"
                + $"六个点位范围平均命中 {summary.AverageRangePointHits:F2}\n"
                + $"点位提升95%下界 {summary.PointLiftLower95:+0.00;-0.00;0.00}，前/后段 {summary.FirstHalfPointLift:+0.00;-0.00;0.00} / {summary.SecondHalfPointLift:+0.00;-0.00;0.00}\n"
                + $"{summary.Verdict}{shadowStatus}\n"
                + $"账本：{_validationStore.FilePath}";
        }
        catch (Exception ex)
        {
            ForwardStatusText.Text = "前向账本不可用";
            ForwardStatusText.ToolTip = $"{ex.Message}\n账本：{_validationStore.FilePath}";
        }
    }

    private async void Backtest_Click(object sender, RoutedEventArgs e)
    {
        if (_backtestCts is not null) return;
        int version = BeginStatus();
        using var cts = new CancellationTokenSource();
        _backtestCts = cts;
        BacktestButton.IsEnabled = false;
        StatusText.Text = "正在执行 200 期无未来数据滚动回测...";
        try
        {
            var report = await Task.Run(() => _predictor.Backtest(200, cancellationToken: cts.Token), cts.Token);
            if (cts.IsCancellationRequested || version != _statusVersion) return;
            string summary = $"回测 {report.SampleSize} 期：点位提升 {report.PointLift:+0.00;-0.00;0.00}，独蓝 {report.SingleBlueHitRate:P1}；{report.Verdict}";
            StatusText.Text = summary;
            StatusText.ToolTip = $"期号 {report.StartIssue}-{report.EndIssue}\n"
                + $"点位均中 {report.AveragePointHits:F2}，随机期望 {report.RandomAveragePointHits:F2}，提升 {report.PointLift:+0.00;-0.00;0.00}\n"
                + $"点位提升95%下界 {report.PointLiftLower95:+0.00;-0.00;0.00}，前/后段 {report.FirstHalfPointLift:+0.00;-0.00;0.00} / {report.SecondHalfPointLift:+0.00;-0.00;0.00}\n"
                + $"独蓝 {report.SingleBlueHitRate:P2}，前/后段 {report.FirstHalfSingleBlueHitRate:P2} / {report.SecondHalfSingleBlueHitRate:P2}\n"
                + $"两码 {report.DoubleBlueHitRate:P2}，三码 {report.TripleBlueHitRate:P2}";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested && version == _statusVersion)
                StatusText.Text = $"滚动回测失败：{ex.Message}";
        }
        finally
        {
            _backtestCts = null;
            BacktestButton.IsEnabled = true;
        }
    }

    private void Details_Click(object sender, RoutedEventArgs e) => ToggleDetails();

    private void ToggleDetails()
    {
        ScorePanel.Visibility = ScorePanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
        DetailsButton.Content = ScorePanel.Visibility == Visibility.Visible ? "收起明细" : "评分明细";
    }

    private void ExportImage_Click(object sender, RoutedEventArgs e)
    {
        BeginStatus();
        if (_prediction is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "PNG 图片|*.png",
            DefaultExt = ".png",
            FileName = $"点位推荐_{_prediction.Issue}.png"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            PredictionGrid.UpdateLayout();
            const int dpi = 192;
            int width = Math.Max(1, (int)Math.Ceiling(PredictionGrid.ActualWidth * dpi / 96.0));
            int height = Math.Max(1, (int)Math.Ceiling(PredictionGrid.ActualHeight * dpi / 96.0));
            var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(PredictionGrid);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new FileStream(dialog.FileName, FileMode.Create);
            encoder.Save(stream);
            StatusText.Text = $"图片已导出：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"图片导出失败：{ex.Message}";
        }
    }

    private async void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        try { await ExportExcelAsync(); }
        catch (Exception ex)
        {
            BeginStatus();
            StatusText.Text = $"表格导出失败：{ex.Message}";
        }
    }

    private async Task ExportExcelAsync()
    {
        if (!ExportExcelButton.IsEnabled) return;
        int version = BeginStatus();
        var records = _dataService.GetAllRecords()
            .OrderBy(record => record.Period)
            .ToArray();
        if (records.Length < 4)
        {
            StatusText.Text = "历史数据不足，至少需要4期才能导出点位预测";
            return;
        }
        int predictionYear = PositionWorkbookExporter.GetDefaultPredictionYear(records);
        int predictionIssueCount = PositionWorkbookExporter.GetPredictionIssueCount(
            records,
            predictionYear);
        int nextIssue = _predictor.GetIssueOptions().First();
        int missingPredictionCount = PositionWorkbookExporter.GetMissingPredictionCount(
            records,
            predictionYear);
        if (missingPredictionCount > 100)
        {
            var decision = MessageBox.Show(
                $"当前没有可用的 {predictionYear} 年预测缓存，首次导出需要计算 {missingPredictionCount} 期，可能需要几分钟。\n\n计算完成后，后续导出会很快。是否继续？",
                "首次导出提示",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (decision != MessageBoxResult.Yes) return;
        }

        var yearRecords = records.Where(record => record.Period / 1000 == predictionYear).ToArray();
        int lastExportIssue = nextIssue / 1000 == predictionYear
            ? nextIssue
            : yearRecords[^1].Period;
        var dialog = new SaveFileDialog
        {
            Filter = "Excel 文件|*.xlsx",
            DefaultExt = ".xlsx",
            FileName = $"点位推荐_{predictionYear}_{yearRecords[0].Period}-{lastExportIssue}.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        ExportExcelButton.IsEnabled = false;
        using var cts = new CancellationTokenSource();
        _exportCts = cts;
        bool reportingProgress = true;
        try
        {
            IProgress<int> progress = new Progress<int>(completed =>
            {
                if (reportingProgress && !cts.IsCancellationRequested && version == _statusVersion)
                    StatusText.Text = $"正在生成 {predictionYear} 年点位数据：{completed}/{predictionIssueCount}";
            });
            var predictions = await Task.Run(() =>
                PositionWorkbookExporter.GeneratePredictions(records, progress, predictionYear, cts.Token), cts.Token);
            reportingProgress = false;
            cts.Token.ThrowIfCancellationRequested();
            if (version == _statusVersion) StatusText.Text = "正在写入 Excel 表格...";
            await Task.Run(() => PositionWorkbookExporter.Export(dialog.FileName, predictions), cts.Token);
            if (!cts.IsCancellationRequested && version == _statusVersion)
                StatusText.Text = $"已导出 {predictionYear} 年 {predictions.Count} 行（含本年度下一期预测）：{dialog.FileName}";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested && version == _statusVersion)
                StatusText.Text = $"表格导出失败：{ex.Message}";
        }
        finally
        {
            reportingProgress = false;
            _exportCts = null;
            ExportExcelButton.IsEnabled = true;
        }
    }

    private static string FormatNumbers(IEnumerable<int> numbers) =>
        string.Join(' ', numbers.Select(n => n.ToString("D2")));

    private void ClearView(string message)
    {
        PredictionGrid.Children.Clear();
        PredictionGrid.RowDefinitions.Clear();
        PredictionGrid.ColumnDefinitions.Clear();
        PredictionGrid.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = Brushes.Black,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24)
        });
        ScoreGrid.ItemsSource = null;
        StatusText.Text = message;
        CutoffText.Text = "";
        SnapshotText.Text = "";
    }

    private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (!ctrl) return;
        if (e.Key == Key.G)
        {
            GeneratePrediction();
            e.Handled = true;
        }
        else if (e.Key == Key.E && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ExportExcel_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.E)
        {
            ExportImage_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.C && !string.IsNullOrEmpty(_selectedCopyText))
        {
            Clipboard.SetText(_selectedCopyText);
            BeginStatus();
            StatusText.Text = "已复制选中行";
            e.Handled = true;
        }
    }

    private static Brush BrushFrom(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private sealed class ScoreRow
    {
        public string Type { get; init; } = "";
        public string Ball { get; init; } = "";
        public string Omission { get; init; } = "";
        public string HistoryFrequency { get; init; } = "";
        public string Frequency30 { get; init; } = "";
        public string Frequency15 { get; init; } = "";
        public string Frequency5 { get; init; } = "";
        public string LongTermScore { get; init; } = "";
        public string RecentScore { get; init; } = "";
        public string StructureScore { get; init; } = "";
        public string FormulaScore { get; init; } = "";
        public string ExclusionScore { get; init; } = "";
        public string TotalScore { get; init; } = "";
        public string Notes { get; init; } = "";

        public static ScoreRow From(string type, PositionBallScore score) => new()
        {
            Type = type,
            Ball = score.Ball.ToString("D2"),
            Omission = score.Omission.ToString(),
            HistoryFrequency = score.HistoryFrequency.ToString(),
            Frequency30 = score.Frequency30.ToString(),
            Frequency15 = score.Frequency15.ToString(),
            Frequency5 = score.Frequency5.ToString(),
            LongTermScore = score.LongTermFeature.ToString("F3"),
            RecentScore = $"{score.Window30Feature:F3}/{score.Window15Feature:F3}",
            StructureScore = score.RecentStructureFeature.ToString("F3"),
            TotalScore = score.TotalScore.ToString("F4"),
            Notes = score.DirectionLabel
        };

        public static ScoreRow From(string type, PositionBlueScore score) => new()
        {
            Type = type,
            Ball = score.Ball.ToString("D2"),
            Omission = score.Omission.ToString(),
            HistoryFrequency = score.HistoryFrequency.ToString(),
            Frequency30 = score.Frequency30.ToString(),
            Frequency15 = score.Frequency16.ToString(),
            Frequency5 = score.Frequency5.ToString(),
            FormulaScore = score.FormulaScore.ToString("F3"),
            ExclusionScore = score.ExclusionScore.ToString("F3"),
            TotalScore = score.CombinedScore.ToString("F4"),
            Notes = string.IsNullOrEmpty(score.FormulaVotes)
                ? score.ExclusionReason
                : $"{score.FormulaVotes} · {score.ExclusionReason}"
        };
    }
}
