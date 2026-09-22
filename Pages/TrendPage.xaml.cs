using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages
{
    public partial class TrendPage : UserControl
    {
        private readonly IDataService _ds;
        private List<DrawRecord> _data = new();
        private IAnalysisService _analysisService;
        private bool _initialized = false;
        private bool _mutualResetting = false;
        private int _currentPeriods = 30;
        private DiagonalChainRenderer? _diagRenderer;
        private bool _subscribed;
        private int _loadVersion;

        public TrendPage() : this(App.Services.GetRequiredService<IDataService>(),
            App.Services.GetRequiredService<IAnalysisService>())
        { }

        public TrendPage(IDataService ds, IAnalysisService analysisService)
        {
            _ds = ds;
            _analysisService = analysisService;
            InitializeComponent();
            _currentPeriods = 30;
            _diagRenderer = new DiagonalChainRenderer(MatrixView, DiagOverlayContainer);
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (_subscribed) return;
            _subscribed = true;
            _loadVersion++;
            _ds.DataUpdated += OnDataUpdated;
            LoadData();
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _subscribed = false;
            _loadVersion++;
            _ds.DataUpdated -= OnDataUpdated;
            _diagRenderer?.Clear();
        }

        private void OpenHistory_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.NavigateTo("history");
        }

        private void OnDataUpdated()
        {
            int version = _loadVersion;
            Dispatcher.InvokeAsync(() =>
            {
                if (_subscribed && version == _loadVersion) LoadData();
            });
        }

        public void LoadData()
        {
            try
            {
                _data = _ds.GetAllRecords();
                _lastWdIdx = _lastParityIdx = _lastPeriods = -1;
                _filteredData = new List<DrawRecord>();
                if (_data == null || _data.Count == 0)
                {
                    _initialized = false;
                    _filteredData = new List<DrawRecord>();
                    MatrixView.SetFullData(_data ?? new List<DrawRecord>());
                    MatrixView.Render(new List<DrawRecord>(), true, true, 0);
                    _diagRenderer?.Clear();
                    return;
                }
                _initialized = true;

                // 根据默认期数高亮对应的按钮
                UpdateDefaultFilterTab();

                // 传递完整数据给 MatrixGrid 用于跨窗口遗漏值计算
                MatrixView.SetFullData(_data);

                // 默认显示基本走势
                RefreshMatrix();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrendPage.LoadData] 加载数据异常: {ex.Message}");
            }
        }

        private void UpdateDefaultFilterTab()
        {
            string defTag = _currentPeriods.ToString();
            foreach (var child in PeriodFilterPanel.Children)
            {
                if (child is Border border)
                {
                    foreach (var b in FindVisualChildren<Button>(border))
                    {
                        b.Style = b.Tag?.ToString() == defTag
                            ? (Style)FindResource("FilterTabActive")
                            : (Style)FindResource("FilterTab");
                    }
                }
            }
        }

        private static List<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            var result = new List<T>();
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) result.Add(t);
                result.AddRange(FindVisualChildren<T>(child));
            }
            return result;
        }

        // ==================== 事件处理 ====================

        private void PeriodFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out var periods))
            {
                _currentPeriods = periods;
                // 更新按钮样式
                UpdateFilterTabStyles(btn);
                RefreshMatrix();
            }
        }

        private void UpdateFilterTabStyles(Button activeBtn)
        {
            foreach (var child in PeriodFilterPanel.Children)
            {
                if (child is Border border)
                {
                    foreach (var b in FindVisualChildren<Button>(border))
                        b.Style = (Style)FindResource("FilterTab");
                }
            }
            activeBtn.Style = (Style)FindResource("FilterTabActive");
        }

        private void Chk012_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;
            bool zo2On = Chk012.IsChecked == true;
            // 012 路使用自己的列顺序计算斜连，仅与周期/奇偶过滤互斥。
            SelWeekDay.IsEnabled = !zo2On;
            SelParity.IsEnabled = !zo2On;
            if (zo2On)
            {
                // Apply both resets before rendering the final 012 view.
                _mutualResetting = true;
                try
                {
                    SelWeekDay.SelectedIndex = 0;
                    SelParity.SelectedIndex = 0;
                }
                finally
                {
                    _mutualResetting = false;
                }
            }
            RefreshMatrix();
        }

        private void OptionChanged(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;
            RefreshMatrix();
        }

        private void SelWeekDay_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized) return;
            if (SelWeekDay.SelectedIndex > 0 && !_mutualResetting)
            {
                // 选了周期 → 奇偶自动复位为"全部"（加标志避免 SelParity_Changed 重复渲染）
                _mutualResetting = true;
                SelParity.SelectedIndex = 0;
                _mutualResetting = false;
                RefreshMatrix();
            }
            else if (!_mutualResetting)
            {
                RefreshMatrix();
            }
        }

        private void SelParity_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized) return;
            if (SelParity.SelectedIndex > 0 && !_mutualResetting)
            {
                // 选了奇偶 → 周期自动复位为"全部"（加标志避免 SelWeekDay_Changed 重复渲染）
                _mutualResetting = true;
                SelWeekDay.SelectedIndex = 0;
                _mutualResetting = false;
                RefreshMatrix();
            }
            else if (!_mutualResetting)
            {
                RefreshMatrix();
            }
        }

        // ==================== 基本走势图 ====================

        private List<DrawRecord> _filteredData = new();
        private int _lastWdIdx = -1, _lastParityIdx = -1, _lastPeriods = -1;

        /// <summary>
        /// 从最新往最旧扫描，同时满足周期+奇偶条件，凑够 periods 条后停止
        /// </summary>
        private List<DrawRecord> GetFilteredData(int periods)
        {
            // 周期过滤
            int wdIdx = SelWeekDay.SelectedIndex;
            var dayOfWeek = wdIdx switch
            {
                0 => (DayOfWeek?)null,    // 全部
                1 => DayOfWeek.Tuesday,
                2 => DayOfWeek.Thursday,
                3 => DayOfWeek.Sunday,
                _ => (DayOfWeek?)null
            };

            // 奇偶过滤
            int parityIdx = SelParity.SelectedIndex;
            bool? isOdd = parityIdx switch
            {
                0 => null,       // 全部
                1 => true,       // 奇数期
                2 => false,      // 偶数期
                _ => null
            };

            // 从最新往最旧找，满足条件即收集，凑够 periods 条即止
            var result = new List<DrawRecord>();
            for (int i = _data.Count - 1; i >= 0 && result.Count < periods; i--)
            {
                var rec = _data[i];
                bool match = true;
                if (dayOfWeek != null && rec.DrawDate.DayOfWeek != dayOfWeek.Value)
                    match = false;
                if (isOdd == true && rec.Period % 2 == 0) match = false;   // 奇数期
                if (isOdd == false && rec.Period % 2 == 1) match = false;  // 偶数期
                if (match)
                    result.Add(rec);
            }
            result.Reverse(); // 恢复时间正序
            return result;
        }

        private void RefreshMatrix()
        {
            int periods = _currentPeriods;
            bool showMiss = ChkMiss.IsChecked ?? true;
            bool showCold = ChkCold.IsChecked ?? true;
            bool showZO2 = Chk012.IsChecked ?? false;

            // 仅在过滤参数变化时重建 _filteredData，避免每次触发全量渲染
            int wdIdx = SelWeekDay.SelectedIndex;
            int parityIdx = SelParity.SelectedIndex;
            bool filterChanged = _lastWdIdx != wdIdx || _lastParityIdx != parityIdx || _lastPeriods != periods;

            if (filterChanged || _filteredData.Count == 0)
            {
                _filteredData = GetFilteredData(periods);
                _lastWdIdx = wdIdx;
                _lastParityIdx = parityIdx;
                _lastPeriods = periods;
            }

            MatrixView.Render(_filteredData, showMiss, showCold, periods, showZO2);

            if (ChkLian.IsChecked == true && _diagRenderer != null)
                _diagRenderer.Update(_filteredData, SelLevel.SelectedIndex, ChkStack.IsChecked == true);
            else
                _diagRenderer?.Clear();
        }
    }
}
