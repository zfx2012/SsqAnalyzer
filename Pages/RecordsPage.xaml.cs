using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using Microsoft.Win32;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages
{
    public partial class RecordsPage : UserControl
    {
        private int _periodCount = 30;
        private List<DrawRecord>? _allData;
        private bool _subscribed;
        private int _loadVersion;

        public RecordsPage() : this(App.Services.GetRequiredService<IDataService>()) { }

        public RecordsPage(IDataService data)
        {
            _data = data;
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (_subscribed) return;
            _subscribed = true;
            _loadVersion++;
            _data.DataUpdated += OnDataUpdate;
            LoadData();
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _subscribed = false;
            _loadVersion++;
            _data.DataUpdated -= OnDataUpdate;
        }

        private void OnDataUpdate()
        {
            int version = _loadVersion;
            Dispatcher.InvokeAsync(() =>
            {
                if (_subscribed && version == _loadVersion) LoadData();
            });
        }

        private readonly IDataService _data;

        /// <summary>
        /// 显示用包装 — 为 DrawRecord 添加 UI 专用属性
        /// </summary>
        private class RecordViewModel
        {
            public DrawRecord Record { get; }
            public int RepeatCount { get; } // 与上期重号个数

            public RecordViewModel(DrawRecord record, bool isLatest, DrawRecord? prevRecord = null)
            {
                Record = record;
                IsLatest = isLatest;
                RepeatCount = prevRecord != null
                    ? record.RedBalls.Intersect(prevRecord.RedBalls).Count()
                    : 0;
            }

            public bool IsLatest { get; }

            // 基础期号信息
            public int Period => Record.Period;
            public string ShortPeriod => Record.ShortPeriod;
            public string DateLabel => Record.DateLabel;
            public string WeekDayName => Record.WeekDayName;
            public IReadOnlyList<int> RedBalls => Record.RedBalls;
            public int BlueBall => Record.BlueBall;

            // 和值/跨度/奇偶
            public int RedSum => Record.RedSum;
            public int RedSpan => Record.RedSpan;
            public string OddEvenLabel => $"{Record.OddCount}:{Record.EvenCount}";
            // 分析指标
            public int LinkCount => Record.LinkCount;
            public string ZoneLabel => Record.ZoneLabel;
            public string BigSmallLabel => Record.BigSmallLabel;
            public string PrimeLabel => Record.PrimeLabel;
            public string ZO2Label => Record.ZO2Label;

            // 销售额（元→亿）
            public double SalesYi => Record.SalesAmount / 100_000_000.0;
            // 奖池（元→亿）
            public double PoolYi => Record.PoolAmount / 100_000_000.0;

            // 各奖级注数
            public int FirstPrizeCount => Record.FirstPrizeCount;
            public int SecondPrizeCount => Record.SecondPrizeCount;
            public int ThirdPrizeCount => Record.ThirdPrizeCount;
            public int FourthPrizeCount => Record.FourthPrizeCount;
            public int FifthPrizeCount => Record.FifthPrizeCount;
            public int SixthPrizeCount => Record.SixthPrizeCount;

            // 一二等奖单注奖金（元）
            public long FirstPrizeYuan => Record.FirstPrizeAmount;
            public long SecondPrizeYuan => Record.SecondPrizeAmount;
        }

        private void LoadData()
        {
            _allData = _data.GetAllRecords();
            if (_allData == null || _allData.Count == 0)
            {
                RecordsGrid.ItemsSource = null;
                InfoText.Text = "暂无开奖数据";
                SummaryText.Text = "";
                FooterText.Text = "数据源暂无可用历史数据";
                return;
            }

            _periodCount = 30;
            UpdatePeriodButtons();
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            if (_allData == null || _allData.Count == 0)
            {
                RecordsGrid.ItemsSource = null;
                InfoText.Text = "暂无开奖数据";
                SummaryText.Text = "";
                FooterText.Text = "数据源暂无可用历史数据";
                return;
            }

            int count = System.Math.Min(_periodCount, _allData.Count);
            var recent = _allData.TakeLast(count).ToList();

            var vmList = new List<RecordViewModel>(count);
            for (int i = 0; i < recent.Count; i++)
            {
                var prev = i > 0 ? recent[i - 1] : null;
                vmList.Add(new RecordViewModel(recent[i], i == recent.Count - 1, prev));
            }

            RecordsGrid.ItemsSource = vmList;

            int total = _allData.Count;
            int firstPer = recent[0].Period;
            int lastPer = recent[^1].Period;
            InfoText.Text = $"共 {total} 期";
            SummaryText.Text = $"显示 {firstPer} ~ {lastPer} 期";
            FooterText.Text = $"数据源：{(_data.HasLocalFile ? "本地缓存" : "内嵌数据")}，共 {total} 期历史数据";
        }

        private void UpdatePeriodButtons()
        {
            var btns = new[] { Btn30, Btn50, Btn100, Btn200 };
            foreach (var btn in btns)
            {
                if (btn.Tag is string tag && int.TryParse(tag, out int v))
                    btn.Style = v == _periodCount
                        ? (Style)FindResource("FilterTabActive")
                        : (Style)FindResource("FilterTab");
            }
        }

        private void PeriodBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out int v))
            {
                _periodCount = v;
                UpdatePeriodButtons();
                RefreshGrid();
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_allData == null || _allData.Count == 0) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Excel 文件|*.xlsx|CSV 文件|*.csv|所有文件|*.*",
                FileName = $"双色球开奖记录_{_allData[^1].Period}",
                DefaultExt = ".xlsx"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                string path = dialog.FileName;
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

                if (ext == ".csv")
                {
                    ExportCsv(path);
                }
                else
                {
                    ExportXlsx(path);
                }

                FooterText.Text = $"✅ 已导出 {_allData.Count} 期数据到 {path}";
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportCsv(string path)
        {
            if (_allData == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("期号,日期,周,红球1,红球2,红球3,红球4,红球5,红球6,蓝球," +
                "重号,连号,和值,跨度,奇偶比,大小比,质合比,三区比,012路," +
                "销售额(亿),奖池(亿),一等注数,一等奖金,二等注数,二等奖金," +
                "三等奖,四等奖,五等奖,六等奖");

            for (int i = 0; i < _allData.Count; i++)
            {
                var r = _allData[i];
                int repeat = i > 0 ? r.RedBalls.Intersect(_allData[i - 1].RedBalls).Count() : 0;

                sb.AppendLine(
                    $"{CsvField(r.Period)},{CsvField(r.DateLabel)},{CsvField(r.WeekDayName)}," +
                    $"{CsvField(r.RedBalls[0], "D2")},{CsvField(r.RedBalls[1], "D2")},{CsvField(r.RedBalls[2], "D2")}," +
                    $"{CsvField(r.RedBalls[3], "D2")},{CsvField(r.RedBalls[4], "D2")},{CsvField(r.RedBalls[5], "D2")}," +
                    $"{CsvField(r.BlueBall, "D2")},{CsvField(repeat)},{CsvField(r.LinkCount)}," +
                    $"{CsvField(r.RedSum)},{CsvField(r.RedSpan)}," +
                    $"{CsvField($"{r.OddCount}:{r.EvenCount}")}," +
                    $"{CsvField(r.BigSmallLabel)},{CsvField(r.PrimeLabel)},{CsvField(r.ZoneLabel)},{CsvField(r.ZO2Label)}," +
                    $"{CsvField(r.SalesAmount / 100_000_000.0, "F2")},{CsvField(r.PoolAmount / 100_000_000.0, "F2")}," +
                    $"{CsvField(r.FirstPrizeCount)},{CsvField(r.FirstPrizeAmount)}," +
                    $"{CsvField(r.SecondPrizeCount)},{CsvField(r.SecondPrizeAmount)}," +
                    $"{CsvField(r.ThirdPrizeCount)},{CsvField(r.FourthPrizeCount)}," +
                    $"{CsvField(r.FifthPrizeCount)},{CsvField(r.SixthPrizeCount)}"
                );
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>CSV 字段转义：防止 Excel 公式注入（= + - @ 前缀），安全拼接</summary>
        private static string CsvField(object? value, string? format = null)
        {
            if (value == null) return "";
            var s = format != null ? string.Format($"{{0:{format}}}", value) : value.ToString() ?? "";
            if (s.Length == 0) return "";
            // 以 = + - @ 开头的字段 → 加前导空格防止被 Excel 当作公式
            if (s[0] is '=' or '+' or '-' or '@')
                s = "'" + s;
            // 含逗号或双引号的字段 → 用双引号包裹
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private void ExportXlsx(string path)
        {
            if (_allData == null) return;
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("开奖记录");

            // 表头
            string[] headers = {
                "期号","日期","周","红球1","红球2","红球3","红球4","红球5","红球6","蓝球",
                "重号","连号","和值","跨度","奇偶比","大小比","质合比","三区比","012路",
                "销售额(亿)","奖池(亿)","一等注数","一等奖金","二等注数","二等奖金",
                "三等奖","四等奖","五等奖","六等奖"
            };

            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
                ws.Cell(1, c + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(1, c + 1).Style.Font.Bold = true;
                ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromArgb(0xE8, 0xE8, 0xE8);
            }

            // 数据行
            for (int i = 0; i < _allData.Count; i++)
            {
                var r = _allData[i];
                int row = i + 2;
                int repeat = i > 0 ? r.RedBalls.Intersect(_allData[i - 1].RedBalls).Count() : 0;

                ws.Cell(row, 1).Value = r.Period;
                ws.Cell(row, 2).Value = r.DateLabel;
                ws.Cell(row, 3).Value = r.WeekDayName;
                ws.Cell(row, 4).Value = r.RedBalls[0];
                ws.Cell(row, 5).Value = r.RedBalls[1];
                ws.Cell(row, 6).Value = r.RedBalls[2];
                ws.Cell(row, 7).Value = r.RedBalls[3];
                ws.Cell(row, 8).Value = r.RedBalls[4];
                ws.Cell(row, 9).Value = r.RedBalls[5];
                ws.Cell(row, 10).Value = r.BlueBall;
                ws.Cell(row, 11).Value = repeat;
                ws.Cell(row, 12).Value = r.LinkCount;
                ws.Cell(row, 13).Value = r.RedSum;
                ws.Cell(row, 14).Value = r.RedSpan;
                ws.Cell(row, 15).Value = $"{r.OddCount}:{r.EvenCount}";
                ws.Cell(row, 16).Value = r.BigSmallLabel;
                ws.Cell(row, 17).Value = r.PrimeLabel;
                ws.Cell(row, 18).Value = r.ZoneLabel;
                ws.Cell(row, 19).Value = r.ZO2Label;
                ws.Cell(row, 20).Value = r.SalesAmount / 100_000_000.0;
                ws.Cell(row, 21).Value = r.PoolAmount / 100_000_000.0;
                ws.Cell(row, 22).Value = r.FirstPrizeCount;
                ws.Cell(row, 23).Value = r.FirstPrizeAmount;
                ws.Cell(row, 24).Value = r.SecondPrizeCount;
                ws.Cell(row, 25).Value = r.SecondPrizeAmount;
                ws.Cell(row, 26).Value = r.ThirdPrizeCount;
                ws.Cell(row, 27).Value = r.FourthPrizeCount;
                ws.Cell(row, 28).Value = r.FifthPrizeCount;
                ws.Cell(row, 29).Value = r.SixthPrizeCount;

                // 居中对齐所有数据单元格
                for (int c = 1; c <= 29; c++)
                {
                    ws.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
            }

            // 列宽自适应
            ws.Columns().AdjustToContents();

            // 冻结第一行
            ws.SheetView.FreezeRows(1);

            wb.SaveAs(path);
        }
    }
}
