using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages
{
    public partial class HistoryPage : UserControl
    {
        private List<DrawRecord> _allData = new();
        private List<DrawRecord> _filteredData = new();
        private int _lastSuffix = -1; // 缓存当前查询的后3位
        private bool _subscribed;
        private int _loadVersion;

        public HistoryPage() : this(App.Services.GetRequiredService<IDataService>()) { }

        public HistoryPage(IDataService data)
        {
            _data = data;
            InitializeComponent();
            _diagRenderer = new DiagonalChainRenderer(MatrixView, DiagOverlayContainer);
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (_subscribed) return;
            _subscribed = true;
            _loadVersion++;
            _data.DataUpdated += OnDataUpdated;
            LoadData();
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _subscribed = false;
            _loadVersion++;
            _data.DataUpdated -= OnDataUpdated;
            _diagRenderer?.Clear();
        }

        private readonly IDataService _data;
        private DiagonalChainRenderer? _diagRenderer;

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
            _allData = _data.GetAllRecords();
            if (_lastSuffix >= 0)
                DoQuery();
            else
                HistoryInfo.Text = "输入期号后3位，如 068";
        }

        private void OpenTrend_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.NavigateTo("chart");
        }

        // ==================== 查询逻辑 ====================

        private void QueryHistory_Click(object sender, RoutedEventArgs e)
        {
            DoQuery();
        }

        private void HistoryPeriodInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                DoQuery();
        }

        private void DoQuery()
        {
            string input = HistoryPeriodInput.Text.Trim();
            if (string.IsNullOrEmpty(input))
            {
                HistoryInfo.Text = "请输入期号后3位";
                return;
            }

            string suffix = input.Length >= 3 ? input[^3..] : input.PadLeft(3, '0');
            if (!int.TryParse(suffix, out int suffixNum))
            {
                HistoryInfo.Text = "请输入有效数字";
                return;
            }

            _lastSuffix = suffixNum;

            var matched = _allData
                .Where(d => d.Period % 1000 == suffixNum)
                .OrderBy(d => d.Period)
                .ToList();

            if (matched.Count == 0)
            {
                HistoryInfo.Text = $"未找到期号后3位为 {suffix} 的数据";
                _filteredData = new List<DrawRecord>();
                RefreshMatrix();
                return;
            }

            _filteredData = matched;
            HistoryInfo.Text = $"找到 {matched.Count} 期同期数据（{matched[0].Period} ~ {matched[^1].Period}）";
            RefreshMatrix();
        }

        // ==================== 矩阵刷新 ====================

        private void OptionChanged(object sender, RoutedEventArgs e)
        {
            if (_filteredData.Count == 0) return;
            RefreshMatrix();
        }

        private void RefreshMatrix()
        {
            bool showMiss = ChkMiss.IsChecked ?? true;
            bool showCold = ChkCold.IsChecked ?? true;

            MatrixView.Render(_filteredData, showMiss, showCold, _filteredData.Count);

            if (ChkLian.IsChecked == true && _diagRenderer != null)
                _diagRenderer.Update(_filteredData, SelLevel.SelectedIndex, ChkStack.IsChecked == true);
            else
                _diagRenderer?.Clear();
        }

        // ==================== 对角线链预测（委派给 DiagonalChainRenderer）====================
    }
}
