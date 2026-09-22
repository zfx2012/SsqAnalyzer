using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SsqAnalyzer.Services;

namespace SsqAnalyzer
{
    public partial class MainWindow : Window
    {
        private readonly IDataService _dataService;
        private static readonly Brush StatusGreenBrush;
        private static readonly Brush StatusRedBrush;

        static MainWindow()
        {
            StatusGreenBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            StatusGreenBrush.Freeze();
            StatusRedBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            StatusRedBrush.Freeze();
        }

        public MainWindow(IDataService dataService)
        {
            _dataService = dataService;
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            _dataService.DataUpdated += OnDataUpdate;
            UpdateStatusBar();
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _dataService.DataUpdated -= OnDataUpdate;
        }

        private void OnDataUpdate() => Dispatcher.InvokeAsync(UpdateStatusBar);

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    GetMonitorInfo(monitor, ref monitorInfo);
                    mmi.ptMaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
                    mmi.ptMaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
                    mmi.ptMaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
                    mmi.ptMaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;
                    Marshal.StructureToPtr(mmi, lParam, true);
                }
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private void UpdateStatusBar()
        {
            var records = _dataService.GetAllRecords();
            if (records == null || records.Count == 0)
            {
                StatusPeriod.Text = "期号：-";
                StatusUpdate.Text = "无开奖数据";
                StatusSync.Text = "暂无同步数据";
                StatusSync.Foreground = StatusRedBrush;
                SyncDot.Fill = StatusRedBrush;
                return;
            }

            var latest = records[^1];
            StatusPeriod.Text = $"第 {latest.Period} 期 · {latest.DateLabel}";

            if (_dataService.HasLocalFile)
            {
                try
                {
                    var lastWrite = File.GetLastWriteTime(_dataService.LocalDataFilePath);
                    StatusUpdate.Text = $"本地数据 {lastWrite:yyyy-MM-dd HH:mm}";
                }
                catch
                {
                    StatusUpdate.Text = "本地数据";
                }
                StatusSync.Text = "数据已同步";
                StatusSync.Foreground = StatusGreenBrush;
                SyncDot.Fill = StatusGreenBrush;
            }
            else
            {
                StatusUpdate.Text = "内嵌数据";
                StatusSync.Text = "数据未同步";
                StatusSync.Foreground = StatusRedBrush;
                SyncDot.Fill = StatusRedBrush;
            }
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
                NavigateTo(tag);
        }

        public void NavigateTo(string tag)
        {
            ResetNavStyles();
            string activeTag = tag switch
            {
                "compound" => "tickets",
                "history" => "chart",
                _ => tag
            };
            var active = new[] { NavChart, NavTickets, NavKill, NavPosition, NavGroup, NavRecords, NavSettings }
                .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), activeTag, StringComparison.Ordinal));
            if (active is not null) active.Style = (Style)FindResource("NavButtonActive");

            PageChart.Visibility = Visibility.Collapsed;
            PageCompound.Visibility = Visibility.Collapsed;
            PageTickets.Visibility = Visibility.Collapsed;
            PageKill.Visibility = Visibility.Collapsed;
            PagePosition.Visibility = Visibility.Collapsed;
            PageGroup.Visibility = Visibility.Collapsed;
            PageRecords.Visibility = Visibility.Collapsed;
            PageHistory.Visibility = Visibility.Collapsed;
            PageSettings.Visibility = Visibility.Collapsed;

            switch (tag)
            {
                case "chart": PageChart.Visibility = Visibility.Visible; break;
                case "compound": PageCompound.Visibility = Visibility.Visible; PageCompound.LoadData(); break;
                case "tickets": PageTickets.Visibility = Visibility.Visible; PageTickets.CheckAndLoad(); break;
                case "kill": PageKill.Visibility = Visibility.Visible; PageKill.LoadData(); break;
                case "position": PagePosition.Visibility = Visibility.Visible; PagePosition.LoadData(); break;
                case "group": PageGroup.Visibility = Visibility.Visible; PageGroup.LoadData(); break;
                case "records": PageRecords.Visibility = Visibility.Visible; break;
                case "history": PageHistory.Visibility = Visibility.Visible; break;
                case "settings": PageSettings.Visibility = Visibility.Visible; break;
            }
        }

        private void ResetNavStyles()
        {
            var navBtns = new[] { NavChart, NavTickets, NavKill, NavPosition, NavGroup, NavRecords, NavSettings };
            foreach (var btn in navBtns)
                btn.Style = (Style)FindResource("NavButton");
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e)
            => Close();

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.GetPosition(this).Y <= 32)
                DragMove();
        }
    }
}
