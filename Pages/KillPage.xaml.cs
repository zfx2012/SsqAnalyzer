using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages
{
    /// <summary>
    /// 杀号页（重构版）。
    /// 全宽规则列表表格（DataGrid） + 顶部按钮组。
    /// 报告弹窗支持提交保存和开奖后复盘，
    /// 支持列表筛选、三态排序、规则详情与回测。
    /// </summary>
    public partial class KillPage : UserControl
    {
        private readonly IDataService _ds;
        private readonly IRuleRepository _ruleRepo;
        private readonly IKillEngine _killEngine;
        private readonly IBacktestEngine _backtestEngine;
        private readonly IRuleContextBuilder _ctxBuilder;
        private readonly IKillSettings _killSettings;
        private readonly GroupInputStore _groupInputs;

        private List<DrawRecord>? _data;
        private int _periodCount = 50;  // 期数按钮：影响回测窗口选择（不接入杀号，杀号用全量历史）
        private KillReport? _lastReport;
        private CancellationTokenSource? _backtestCts;
        private string? _sortKey;
        private ListSortDirection _sortDirection;
        private int _sortClickCount;
        private Dictionary<string, bool>? _enabledSortSnapshot;
        private bool _loaded;  // 初始化期间不触发列表筛选刷新

        public KillPage()
            : this(App.Services.GetRequiredService<IDataService>(),
                   App.Services.GetRequiredService<IRuleRepository>(),
                   App.Services.GetRequiredService<IKillEngine>(),
                   App.Services.GetRequiredService<IBacktestEngine>(),
                   App.Services.GetRequiredService<IRuleContextBuilder>(),
                   App.Services.GetRequiredService<IKillSettings>(),
                   App.Services.GetRequiredService<GroupInputStore>(),
                   App.Services.GetRequiredService<KillSubmissionStore>(),
                   App.Services.GetRequiredService<KillReviewCoordinator>()) { }

        public KillPage(
            IDataService ds,
            IRuleRepository ruleRepo,
            IKillEngine killEngine,
            IBacktestEngine backtestEngine,
            IRuleContextBuilder ctxBuilder,
            IKillSettings killSettings,
            GroupInputStore groupInputs, KillSubmissionStore? submissions = null, KillReviewCoordinator? reviews = null)
        {
            _ds = ds ?? throw new ArgumentNullException(nameof(ds));
            _ruleRepo = ruleRepo ?? throw new ArgumentNullException(nameof(ruleRepo));
            _killEngine = killEngine ?? throw new ArgumentNullException(nameof(killEngine));
            _backtestEngine = backtestEngine ?? throw new ArgumentNullException(nameof(backtestEngine));
            _ctxBuilder = ctxBuilder ?? throw new ArgumentNullException(nameof(ctxBuilder));
            _killSettings = killSettings ?? throw new ArgumentNullException(nameof(killSettings));
            _groupInputs = groupInputs ?? throw new ArgumentNullException(nameof(groupInputs));
            InitializeComponent();
            InitializeWorkflow(submissions, reviews);
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private bool _subscribed;
        private int _loadVersion;
        private bool _rulesRefreshPending;
        private int _renderVersion;

        private void PostWhileLoaded(Action action)
        {
            int version = _loadVersion;
            Dispatcher.InvokeAsync(() =>
            {
                if (_subscribed && version == _loadVersion) action();
            });
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (_subscribed) return;
            _subscribed = true;
            _loadVersion++;
            _ruleRepo.RulesChanged += OnRulesChanged;
            _ds.DataUpdated += OnDataUpdated;
            if (_reviews is not null) _reviews.Updated += OnReviewUpdated;
            ReadActualEntries();
            UpdateThresholdLabel();
            LoadData();
            _loaded = true;
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _subscribed = false;
            _loaded = false;
            _loadVersion++;
            _backtestCts?.Cancel();
            _rulesRefreshPending = false;
            _ruleRepo.RulesChanged -= OnRulesChanged;
            _ds.DataUpdated -= OnDataUpdated;
            if (_reviews is not null) _reviews.Updated -= OnReviewUpdated;
        }

        private void OnDataUpdated()
        {
            PostWhileLoaded(() =>
            {
                _data = _ds.GetAllRecords();
                _currentDataHash = KillRuleDefinition.DataHash(_data);
                PeriodInfo.Text = $"共 {_data.Count} 期历史";
                InvalidatePreview("开奖数据已变化，请重新执行杀号。");
                RenderRuleList();
            });
        }

        /// <summary>规则仓储变化（AddRuleWindow 勾选启用/入库/删除等）→ 自动刷新主表格。</summary>
        private void OnRulesChanged()
        {
            if (_rulesRefreshPending) return;
            _rulesRefreshPending = true;
            PostWhileLoaded(() =>
            {
                _rulesRefreshPending = false;
                RenderRuleList();
            });
        }

        private void UpdateThresholdLabel()
        {
            if (ThresholdValue is null) return;
            ThresholdValue.Text = $"固定门槛：红球 {_killSettings.GetMinAccuracy(BallType.Red):P0} · 蓝球 {_killSettings.GetMinAccuracy(BallType.Blue):P0}";
        }

        public void LoadData()
        {
            _data = _ds.GetAllRecords();
            _currentDataHash = KillRuleDefinition.DataHash(_data);
            UpdatePeriodButtons();
            PeriodInfo.Text = $"共 {_data.Count} 期历史";
            RenderRuleList();
        }

        // ==================== 期数按钮 ====================

        private void UpdatePeriodButtons()
        {
            foreach (var btn in new[] { Btn30, Btn50, Btn100, BtnAll })
            {
                if (btn.Tag is string tag)
                {
                    int v = tag == "0" ? 0 : int.Parse(tag);
                    btn.Style = v == _periodCount
                        ? (Style)FindResource("FilterTabActive")
                        : (Style)FindResource("FilterTab");
                }
            }
        }

        private void PeriodBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                _periodCount = tag == "0" ? 0 : int.Parse(tag);
                UpdatePeriodButtons();
                // 期数仅影响回测窗口选择提示；杀号始终用全量历史（架构 §4.1）。
                PeriodInfo.Text = $"共 {_data?.Count ?? 0} 期历史 | 回测窗口参考：{(_periodCount > 0 ? _periodCount + " 次触发" : "全部触发")}";
                RenderRuleList();
            }
        }

        // ==================== 规则表格渲染 ====================

        /// <summary>渲染全宽规则 DataGrid：每条规则一行。仅显示 IsVisible=true 的规则（自定义规则可在添加规则窗口中隐藏）。</summary>
        private void RenderRuleList()
        {
            CheckPreviewRules();
            string? selectedId = (RulesGrid.SelectedItem as RuleRowViewModel)?.RuleId;
            var scroll = FindScrollViewer(RulesGrid);
            double offset = scroll?.VerticalOffset ?? 0;
            int renderVersion = ++_renderVersion;
            var allRules = _ruleRepo.GetAll();
            var visibleRules = allRules.Where(r => r.IsVisible).ToList();
            int enabledCount = visibleRules.Count(r => r.IsEnabled);
            int hiddenCount = allRules.Count - visibleRules.Count;
            RuleListSummary.Text = $"共 {visibleRules.Count} 条 · 启用 {enabledCount} 条"
                                  + (hiddenCount > 0 ? $" · 隐藏 {hiddenCount} 条" : "");
            RuleFooter.Text = visibleRules.Count == 0
                ? "未加载到任何规则（请检查 builtin-rules.json 资源）"
                : "报告按当前窗口评价；全部窗口至少 30 次触发。有执行异常的结果不参与达标判断。";

            var rows = new List<RuleRowViewModel>(visibleRules.Count);
            int idx = 1;
            foreach (var rule in visibleRules)
            {
                rows.Add(BuildRow(rule, idx++));
            }
            UpdateOverview(rows);
            string search = RuleSearch.Text.Trim();
            rows = rows.Where(row =>
                (search.Length == 0 || $"{row.RuleId} {row.Name} {row.Description}".Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (BallFilter.SelectedIndex <= 0 || row.BallTypeLabel == (BallFilter.SelectedIndex == 1 ? "红球" : "蓝球")) &&
                (StateFilter.SelectedIndex <= 0 || row.IsEnabled == (StateFilter.SelectedIndex == 1)) &&
                (HideFailedCheckBox.IsChecked != true || row.GateSortValue != 4)).ToList();
            RuleListSummary.Text += $" · 显示 {rows.Count} 条";
            EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Text = visibleRules.Count == 0 ? "暂无可见规则，请添加规则或检查规则库。" : "没有匹配的规则，请调整搜索或筛选条件。";
            var sorted = SortRows(rows).ToList();
            for (int i = 0; i < sorted.Count; i++) sorted[i].Index = i + 1;
            // Checkbox saves also publish RulesChanged. Keep both refreshes in-place when
            // membership/order is unchanged so WPF does not reset containers, focus or scroll.
            if (RulesGrid.ItemsSource is List<RuleRowViewModel> current
                && current.Select(row => row.RuleId).SequenceEqual(sorted.Select(row => row.RuleId)))
            {
                for (int i = 0; i < current.Count; i++) current[i].UpdateFrom(sorted[i]);
                foreach (var column in RulesGrid.Columns)
                    column.SortDirection = column.SortMemberPath == _sortKey ? _sortDirection : null;
                return;
            }
            RulesGrid.ItemsSource = sorted;
            RulesGrid.SelectedItem = sorted.FirstOrDefault(row => row.RuleId == selectedId);
            // DataGrid resets header arrows when ItemsSource changes.
            foreach (var column in RulesGrid.Columns)
                column.SortDirection = column.SortMemberPath == _sortKey ? _sortDirection : null;
            if (scroll is not null)
                Dispatcher.InvokeAsync(() =>
                {
                    if (_subscribed && renderVersion == _renderVersion) scroll.ScrollToVerticalOffset(offset);
                }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer viewer) return viewer;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
            return null;
        }

        /// <summary>构建单条规则行视图模型。</summary>
        private RuleRowViewModel BuildRow(IKillRule rule, int index)
        {
            var (accText, accBrush, sampleInsufficient) = FormatAccuracy(rule);
            var windowStat = rule.BacktestStats is null ? null : SelectWindow(rule.BacktestStats);
            bool failed = windowStat?.FailureCount > 0;
            bool gatePassed = IsGatePassed(rule);
            bool forceEnabled = rule.ForceEnabled && !gatePassed;
            // 行灰显条件：不达标 + 未强制启用 + 已回测 + 样本充足（避免对未判定行误灰显）
            bool dimRow = !gatePassed && !forceEnabled
                          && rule.BacktestStats is not null && !sampleInsufficient;

            string gateLabel;
            Brush gateBrush;
            if (failed || rule.BacktestStats is null || sampleInsufficient)
            {
                gateLabel = failed ? "执行异常" : "待判定";
                gateBrush = (Brush)FindResource("TextTertiary");
            }
            else if (forceEnabled)
            {
                gateLabel = "强制启用";
                gateBrush = (Brush)FindResource("WarmColor");
            }
            else if (gatePassed)
            {
                gateLabel = "✓达标";
                gateBrush = (Brush)FindResource("Accent");
            }
            else
            {
                gateLabel = "✗不达标";
                gateBrush = (Brush)FindResource("HotColor");
            }

            double? accuracyValue = rule.BacktestStats is null || sampleInsufficient
                ? null
                : SelectWindow(rule.BacktestStats).Accuracy;
            int gateSortValue = failed ? 5 : rule.BacktestStats is null ? 2 : sampleInsufficient ? 3
                : forceEnabled ? 1 : gatePassed ? 0 : 4;

            var row = new RuleRowViewModel
            {
                Index = index,
                IsEnabled = rule.IsEnabled,
                RuleId = rule.RuleId,
                Name = rule.Name,
                BallTypeLabel = rule.BallType == BallType.Red ? "红球" : "蓝球",
                CategoryLabel = rule.Category == RuleCategory.Pattern ? "图形" : "公式",
                SourceLabel = rule.IsBuiltin ? "内置" : "自定义",
                Description = rule.Description,
                AccuracyLabel = accText,
                AccuracyValue = accuracyValue,
                AccuracyBrush = accBrush,
                StatsHint = windowStat is null ? "尚未回测" : $"触发 {windowStat.TriggeredCount} 次 · 执行失败 {windowStat.FailureCount} 次\n"
                    + (windowStat.RunAt is { } run ? $"回测时间 {run.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "旧统计：回测时间未知")
                    + (windowStat.FirstPeriod is { } first ? $"\n覆盖 {first}—{windowStat.LastPeriod}" : "")
                    + (windowStat.LastExecutionError is { } error ? $"\n{error}" : "")
                    + "\n" + KillMetricText.Format(windowStat.Metrics),
                MinAccuracyLabel = $"{_killSettings.GetMinAccuracy(rule.BallType):P0}",
                GateLabel = gateLabel,
                GateSortValue = gateSortValue,
                GateBrush = gateBrush,
                IsDimmed = dimRow
            };
            PopulateWorkflowRow(row, rule, accText);
            return row;
        }

        /// <summary>同列点击依次切换升序、降序和默认顺序。</summary>
        private void RulesGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            string? key = e.Column.SortMemberPath;
            if (string.IsNullOrEmpty(key)) return;
            e.Handled = true;
            // Capture the enabled sort key on explicit header clicks; checkbox edits
            // keep their row position until the user asks to sort again.
            _enabledSortSnapshot = key == "IsEnabled"
                ? _ruleRepo.GetAll().ToDictionary(rule => rule.RuleId, rule => rule.IsEnabled)
                : null;

            if (StringComparer.Ordinal.Equals(_sortKey, key))
                _sortClickCount++;
            else
            {
                _sortKey = key;
                _sortClickCount = 1;
            }

            if (_sortClickCount >= 3)
            {
                _sortKey = null;
                _sortClickCount = 0;
                _enabledSortSnapshot = null;
                RenderRuleList();
                return;
            }

            _sortDirection = _sortClickCount == 2
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            RenderRuleList();
        }

        private IEnumerable<RuleRowViewModel> SortRows(IEnumerable<RuleRowViewModel> rows)
        {
            if (_sortKey is null) return rows;
            return _sortKey switch
            {
                "AccuracyValue" => SortAccuracy(rows, _sortDirection),
                "IsEnabled" => _sortDirection == ListSortDirection.Ascending
                    ? rows.OrderBy(EnabledSortValue).ThenBy(row => row.RuleId)
                    : rows.OrderByDescending(EnabledSortValue).ThenBy(row => row.RuleId),
                "GateSortValue" => _sortDirection == ListSortDirection.Ascending
                    ? rows.OrderBy(row => row.GateSortValue).ThenBy(row => row.RuleId)
                    : rows.OrderByDescending(row => row.GateSortValue).ThenBy(row => row.RuleId),
                "ActualWrongCount" => _sortDirection == ListSortDirection.Ascending ? rows.OrderBy(r => r.ActualWrongCount).ThenBy(r => r.RuleId) : rows.OrderByDescending(r => r.ActualWrongCount).ThenBy(r => r.RuleId),
                _ => _sortDirection == ListSortDirection.Ascending
                    ? rows.OrderBy(row => GetSortText(row, _sortKey), StringComparer.CurrentCulture).ThenBy(row => row.RuleId)
                    : rows.OrderByDescending(row => GetSortText(row, _sortKey), StringComparer.CurrentCulture).ThenBy(row => row.RuleId)
            };
        }

        private bool EnabledSortValue(RuleRowViewModel row) =>
            _enabledSortSnapshot?.GetValueOrDefault(row.RuleId, row.IsEnabled) ?? row.IsEnabled;

        private void RuleSearch_TextChanged(object sender, TextChangedEventArgs e) { if (_loaded) RenderRuleList(); }
        private void HideFailed_Changed(object sender, RoutedEventArgs e) { if (_loaded) RenderRuleList(); }
        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_loaded) RenderRuleList(); }
        private void ResetList_Click(object sender, RoutedEventArgs e)
        {
            _loaded = false;
            RuleSearch.Clear();
            BallFilter.SelectedIndex = StateFilter.SelectedIndex = 0;
            HideFailedCheckBox.IsChecked = false;
            _sortKey = null;
            _sortClickCount = 0;
            _enabledSortSnapshot = null;
            _loaded = _subscribed;
            RenderRuleList();
        }

        private static IEnumerable<RuleRowViewModel> SortAccuracy(
            IEnumerable<RuleRowViewModel> rows, ListSortDirection direction)
        {
            var available = rows.Where(row => row.AccuracyValue.HasValue);
            var unavailable = rows.Where(row => !row.AccuracyValue.HasValue);
            available = direction == ListSortDirection.Ascending
                ? available.OrderBy(row => row.AccuracyValue!.Value).ThenBy(row => row.RuleId)
                : available.OrderByDescending(row => row.AccuracyValue!.Value).ThenBy(row => row.RuleId);
            return available.Concat(unavailable.OrderBy(row => row.RuleId, StringComparer.Ordinal));
        }

        private static string GetSortText(RuleRowViewModel row, string key) => key switch
        {
            "Name" => row.Name,
            "Description" => row.Description,
            "BallTypeLabel" => row.BallTypeLabel,
            "CategoryLabel" => row.CategoryLabel,
            "SourceLabel" => row.SourceLabel,
            "ExecutionLabel" => row.ExecutionLabel,
            "ActualLabel" => row.ActualLabel,
            _ => row.RuleId
        };

        private (string text, Brush brush, bool insufficient) FormatAccuracy(IKillRule rule)
        {
            var stats = rule.BacktestStats;
            if (stats is null)
                return ("未回测", (Brush)Application.Current.FindResource("TextTertiary"), false);

            var w = SelectWindow(stats);
            if (w.RuleFingerprint is { } fingerprint && fingerprint != KillRuleDefinition.Capture(rule).Fingerprint)
                return ("规则已变更", (Brush)FindResource("TextTertiary"), true);
            if (_currentDataHash is not null && w.DataFingerprint is { } dataFingerprint && dataFingerprint != _currentDataHash)
                return ("历史已更新", (Brush)FindResource("TextTertiary"), true);
            if ((_periodCount == 0 || _periodCount == 100) && stats.LegacyCombinedWindow is not null
                && w.RunAt is null && w.KillBallCount == 0)
                return ("需重新回测", (Brush)FindResource("TextTertiary"), true);
            if (w.FailureCount > 0) return ("执行异常", (Brush)FindResource("HotColor"), true);
            if (w.SampleInsufficient)
            {
                // TriggeredCount == 0 → 该窗口从未回测（或从未触发）；> 0 → 回测了但触发次数不够
                if (w.TriggeredCount == 0)
                    return ("无有效样本", (Brush)Application.Current.FindResource("TextTertiary"), true);
                return ("样本不足", (Brush)Application.Current.FindResource("TextTertiary"), true);
            }

            return ($"{w.Accuracy:P1}", (Brush)Application.Current.FindResource("Accent"), false);
        }

        /// <summary>门槛判定：按 _periodCount 选中的窗口是否达标；样本不足视为未判定 → 返回 true 不阻塞启用。</summary>
        private bool IsGatePassed(IKillRule rule)
        {
            var stats = rule.BacktestStats;
            if (stats is null) return true;  // 未回测，不强制禁用
            var w = SelectWindow(stats);
            if (!w.IsUsable || (w.RuleFingerprint is { } fingerprint && fingerprint != KillRuleDefinition.Capture(rule).Fingerprint)) return true;
            return w.Accuracy >= _killSettings.GetMinAccuracy(rule.BallType);
        }

        private BacktestWindow SelectedWindow => _periodCount switch
        {
            30 => BacktestWindow.Last30Triggers,
            50 => BacktestWindow.Last50Triggers,
            100 => BacktestWindow.Last100Triggers,
            _ => BacktestWindow.All
        };

        private BacktestWindowStat SelectWindow(BacktestStatsSnapshot stats) => stats.ForWindow(SelectedWindow);

        // ==================== 行内交互 ====================

        /// <summary>启用状态 CheckBox 点击：持久化到 IRuleRepository 并刷新表格（同步灰显状态）。</summary>
        private void EnabledCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is RuleRowViewModel row)
            {
                var rule = _ruleRepo.Find(row.RuleId);
                if (rule is KillRule kr)
                {
                    bool oldValue = kr.IsEnabled;
                    try
                    {
                        kr.IsEnabled = cb.IsChecked == true;
                        _ruleRepo.Update(kr);
                    }
                    catch (Exception ex)
                    {
                        kr.IsEnabled = oldValue;
                        MessageBox.Show($"启用状态保存失败：{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally { RenderRuleList(); }
                }
            }
        }

        /// <summary>单行「回测」按钮点击：透传到 RunBacktest(ruleId)。</summary>
        private void BacktestBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RuleRowViewModel row)
            {
                RunBacktest(row.RuleId);
            }
        }

        /// <summary>
        /// 双击规则行：从双击位置向上找 DataGridRow → 取 RuleRowViewModel →
        /// _ruleRepo.Find 拿到完整 KillRule → 弹出 KillRuleDetailWindow 详情窗口。
        /// </summary>
        private void RulesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 从双击位置向上找 DataGridRow，取其 DataContext
            var dep = e.OriginalSource as DependencyObject;
            while (dep is not null and not DataGridRow)
            {
                if (dep is System.Windows.Controls.Primitives.ButtonBase) return;
                dep = dep is Visual ? VisualTreeHelper.GetParent(dep) : LogicalTreeHelper.GetParent(dep);
            }

            if (dep is DataGridRow row && row.DataContext is RuleRowViewModel vm)
            {
                ShowDetails(vm);
            }
        }

        private void DetailsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: RuleRowViewModel row }) ShowDetails(row);
        }

        private void RulesGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.OriginalSource is not System.Windows.Controls.Primitives.ButtonBase
                && RulesGrid.SelectedItem is RuleRowViewModel row)
            {
                e.Handled = true;
                ShowDetails(row);
            }
        }

        private void ShowDetails(RuleRowViewModel row)
        {
            if (_ruleRepo.Find(row.RuleId) is KillRule rule)
                new KillRuleDetailWindow(rule, _killSettings, _periodCount, _actualEntries, OpenSubmissionHistory) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        // ==================== 执行杀号 ====================

        private void RunKill_Click(object sender, RoutedEventArgs e) => RunKill();

        private void SubmissionHistory_Click(object sender, RoutedEventArgs e) =>
            OpenSubmissionHistory(null);

        /// <summary>执行启用规则并打开可提交的报告。</summary>
        private void RunKill()
        {
            if (_data is null || _data.Count == 0)
            {
                MessageBox.Show("历史数据为空，无法执行杀号。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var enabledRules = _ruleRepo.GetEnabled();
            if (enabledRules.Count == 0)
            {
                MessageBox.Show("未启用任何规则，请先在表格中勾选启用规则。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _lastReport = _killEngine.Execute(enabledRules, SelectedWindow);
                _groupInputs.SaveKill(_lastReport);
                ShowExecutionPreview();
                RenderRuleList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"杀号执行失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>添加规则：打开非模态 AddRuleWindow（入库后通过 IRuleRepository.RulesChanged 自动刷新主表格）。</summary>
        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddRuleWindow { Owner = Window.GetWindow(this) };
            win.Show();
        }

        // ==================== 回测 ====================

        /// <summary>是否正在回测（防重入，禁用按钮）。</summary>
        private bool _isBacktesting;

        /// <summary>
        /// 单规则回测：异步执行（瓶颈1 修复），避免 12 规则 × 100 期 = 1200 次 Jint 执行阻塞 UI 线程。
        /// 流程：禁用按钮 → 弹出模态进度窗口（不确定模式转圈）→ 后台 Task.Run 跑 _backtestEngine.Run → 关闭窗口 → 恢复按钮。
        /// </summary>
        private async void RunBacktest(string ruleId)
        {
            if (_isBacktesting) return;  // 防重入
            var rule = _ruleRepo.Find(ruleId);
            if (rule is null) return;

            BacktestWindow window = _periodCount switch
            {
                30 => BacktestWindow.Last30Triggers,
                50 => BacktestWindow.Last50Triggers,
                100 => BacktestWindow.Last100Triggers,
                0 => BacktestWindow.All,
                _ => BacktestWindow.All
            };

            SetBacktestingState(true);
            // 弹出模态进度窗口（不确定模式：转圈）
            var dlg = new BacktestProgressDialog(Window.GetWindow(this)!)
            {
                Title = $"回测：{rule.Name}"
            };
            dlg.SetTitle($"回测：{rule.Name}");
            dlg.SetIndeterminate(rule.Name);
            dlg.Show();
            using var backtestCts = new CancellationTokenSource();
            _backtestCts = backtestCts;

            try
            {
                // 后台线程跑回测（Jint 执行不阻塞 UI）
                await Task.Run(() => _backtestEngine.Run(rule, window, backtestCts.Token), backtestCts.Token);
                backtestCts.Token.ThrowIfCancellationRequested();
                // 引擎内部会写回 rule.BacktestStats 并持久化
                RenderRuleList();  // 已在 UI 线程（await 之后回到捕获的 SynchronizationContext）
            }
            catch (OperationCanceledException) when (backtestCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show($"规则 {rule.Name} 回测失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 关闭进度窗口（设置 100% 让 OnClosing 放行）
                dlg.UpdateProgress(1, 1);
                dlg.Close();
                SetBacktestingState(false);
                if (ReferenceEquals(_backtestCts, backtestCts))
                    _backtestCts = null;
            }
        }

        /// <summary>
        /// 批量回测：异步执行（瓶颈1 修复），所有规则串行跑在后台线程。
        /// 流程：禁用所有按钮 → 弹出模态进度窗口（确定模式进度条）→ 后台 Task.Run 跑 RunAllAsync → 关闭窗口 → 恢复按钮。
        /// </summary>
        private async void BatchBacktest_Click(object sender, RoutedEventArgs e)
        {
            if (_isBacktesting) return;  // 防重入
            BacktestWindow window = _periodCount switch
            {
                30 => BacktestWindow.Last30Triggers,
                50 => BacktestWindow.Last50Triggers,
                100 => BacktestWindow.Last100Triggers,
                0 => BacktestWindow.All,
                _ => BacktestWindow.All
            };

            SetBacktestingState(true);

            // 弹出模态进度窗口（确定模式：进度条 + done/total 文案）
            var dlg = new BacktestProgressDialog(Window.GetWindow(this)!)
            {
                Title = "批量回测"
            };
            dlg.SetTitle("批量回测");
            dlg.UpdateProgress(0, 1);  // 初始占位（0/1 → 0%）
            dlg.Show();

            // Progress<T> 在 UI 线程捕获 SynchronizationContext，回调在 UI 线程执行（直接更新窗口内进度条）
            using var batchBacktestCts = new CancellationTokenSource();
            _backtestCts = batchBacktestCts;
            var progress = new Progress<(int done, int total)>(p =>
            {
                if (!batchBacktestCts.IsCancellationRequested && ReferenceEquals(_backtestCts, batchBacktestCts))
                    dlg.UpdateProgress(p.done, p.total);
            });

            try
            {
                // 后台线程跑批量回测（RunAllAsync 内部串行 foreach，不阻塞 UI）
                await Task.Run(() => _backtestEngine.RunAllAsync(window, progress, batchBacktestCts.Token), batchBacktestCts.Token);
                batchBacktestCts.Token.ThrowIfCancellationRequested();
                RenderRuleList();  // 刷新整个 DataGrid（门槛状态可能变化）
            }
            catch (OperationCanceledException) when (batchBacktestCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show($"批量回测失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 关闭进度窗口
                dlg.UpdateProgress(1, 1);
                dlg.Close();
                SetBacktestingState(false);
                if (ReferenceEquals(_backtestCts, batchBacktestCts))
                    _backtestCts = null;
            }
        }

        /// <summary>
        /// 切换回测进行中的 UI 状态：禁用/恢复顶部按钮 + DataGrid 单行回测按钮。
        /// 进度文案由模态窗口 BacktestProgressDialog 负责，PeriodInfo 保持显示期数信息。
        /// </summary>
        private void SetBacktestingState(bool running)
        {
            _isBacktesting = running;
            BtnBatchBacktest.IsEnabled = !running;
            BtnRunKill.IsEnabled = !running;
            BtnAddRule.IsEnabled = !running;
            // DataGrid 整体禁用：覆盖单行回测按钮（无法逐个访问模板内的按钮）
            RulesGrid.IsEnabled = !running;
            // 期数按钮在回测期间也不应操作（避免改窗口导致回测结果与显示不一致）
            foreach (var btn in new[] { Btn30, Btn50, Btn100, BtnAll })
                btn.IsEnabled = !running;
        }
    }

    /// <summary>规则行支持原位更新，避免勾选时重建列表和丢失交互位置。</summary>
    public sealed class RuleRowViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        internal void UpdateFrom(RuleRowViewModel row)
        {
            Index = row.Index;
            IsEnabled = row.IsEnabled;
            Name = row.Name;
            BallTypeLabel = row.BallTypeLabel;
            CategoryLabel = row.CategoryLabel;
            SourceLabel = row.SourceLabel;
            Description = row.Description;
            AccuracyLabel = row.AccuracyLabel;
            AccuracyValue = row.AccuracyValue;
            AccuracyBrush = row.AccuracyBrush;
            StatsHint = row.StatsHint;
            MinAccuracyLabel = row.MinAccuracyLabel;
            GateLabel = row.GateLabel;
            GateSortValue = row.GateSortValue;
            GateBrush = row.GateBrush;
            IsDimmed = row.IsDimmed;
            ExecutionLabel = row.ExecutionLabel; ExecutionHint = row.ExecutionHint;
            ActualLabel = row.ActualLabel; ActualHint = row.ActualHint; ActualWrongCount = row.ActualWrongCount;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        public int Index { get; set; }
        public bool IsEnabled { get; set; }
        public string RuleId { get; set; } = "";
        public string Name { get; set; } = "";
        public string BallTypeLabel { get; set; } = "";
        public string CategoryLabel { get; set; } = "";
        public string SourceLabel { get; set; } = "";
        public string Description { get; set; } = "";
        public string AccuracyLabel { get; set; } = "";
        public double? AccuracyValue { get; set; }
        public Brush AccuracyBrush { get; set; } = Brushes.Transparent;
        public string StatsHint { get; set; } = "";
        public string MinAccuracyLabel { get; set; } = "";
        public string GateLabel { get; set; } = "";
        public int GateSortValue { get; set; }
        public Brush GateBrush { get; set; } = Brushes.Transparent;
        public bool IsDimmed { get; set; }
        public string ExecutionLabel { get; set; } = "未执行";
        public string ExecutionHint { get; set; } = "";
        public string ActualLabel { get; set; } = "暂无记录";
        public string ActualHint { get; set; } = "";
        public int ActualWrongCount { get; set; }
    }
}
