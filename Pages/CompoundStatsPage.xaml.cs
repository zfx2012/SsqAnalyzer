using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Linq;
using System.IO;
using SsqAnalyzer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages
{
    public partial class CompoundStatsPage : UserControl, INotifyPropertyChanged
    {

        public class VideoInfo : INotifyPropertyChanged
        {
            public string Bvid { get; set; } = "";
            public string Title { get; set; } = "";
            public string Author { get; set; } = "";
            public string Duration { get; set; } = "";
            public int Play { get; set; }
            public List<VideoFormat> Formats { get; set; } = new();
            public int Index { get; set; }

            public string DisplayText =>
                $"#{Index} {Title}  |  {Author}  |  {Duration}  |  {Play:N0}播  |  {string.Join(" ", Formats.Select(f => $"[{f.Label}]"))}";

            public bool IsDownloadable => !string.IsNullOrWhiteSpace(Bvid);
            public bool HasError => !IsDownloadable;
            public string TitleWithExt => IsDownloadable ? $"{Title}.mp4" : Title;

            public event PropertyChangedEventHandler? PropertyChanged;
            public void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        }

        public class VideoFormat
        {
            public int Quality { get; set; }
            public string Label { get; set; } = "";
            public string Codec { get; set; } = "";
        }

#pragma warning disable CS0067 // INotifyPropertyChanged 接口要求实现，当前未使用
        public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
        public ObservableCollection<VideoInfo> SearchResults { get; } = new();
        private readonly ITicketStore _store;
        private readonly IVideoSearchService _bili;
        private readonly DownloadService _downloader;
        private readonly AiAnalysisService _ai;
        private Storyboard? _hourglassStoryboard;
        private Storyboard? _downloadHourglassStoryboard;
        private DispatcherTimer? _analysisProgressTimer;
        private DispatcherTimer? _downloadProgressTimer;
        private bool _isAnalyzing;
        private CancellationTokenSource? _downloadCts;
        private CancellationTokenSource? _searchCts;
        private CancellationTokenSource? _analysisCts;

        // ===== 沙漏翻转动画 =====
        private void StartHourglassAnimation()
        {
            StopHourglassAnimation();

            var sb = new Storyboard();
            var anim = new ObjectAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(1.2),
                RepeatBehavior = RepeatBehavior.Forever
            };

            // ⌛ (倒置) → ⏳ (直立) → 循环
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame
            {
                KeyTime = TimeSpan.FromSeconds(0),
                Value = "⌛ AI分析中请稍等…"
            });
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame
            {
                KeyTime = TimeSpan.FromSeconds(0.6),
                Value = "⏳ AI分析中请稍等…"
            });

            Storyboard.SetTarget(anim, AnalyzeStatus);
            Storyboard.SetTargetProperty(anim, new PropertyPath(TextBlock.TextProperty));
            sb.Children.Add(anim);

            _hourglassStoryboard = sb;
            sb.Begin(AnalyzeStatus, true);
        }

        private void StopHourglassAnimation()
        {
            if (_hourglassStoryboard == null) return;
            _hourglassStoryboard.Stop(AnalyzeStatus);
            _hourglassStoryboard = null;
            // 恢复为直立沙漏
            if (AnalyzeStatus.Text.Contains('⌛'))
                AnalyzeStatus.Text = AnalyzeStatus.Text.Replace('⌛', '⏳');
        }

        private void StartDownloadHourglassAnimation()
        {
            StopDownloadHourglassAnimation();
            DownloadSpinner.Visibility = Visibility.Visible;
            var animation = new ObjectAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(1.2),
                RepeatBehavior = RepeatBehavior.Forever
            };
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame
            {
                KeyTime = TimeSpan.FromSeconds(0),
                Value = "⌛"
            });
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame
            {
                KeyTime = TimeSpan.FromSeconds(0.6),
                Value = "⏳"
            });
            Storyboard.SetTarget(animation, DownloadSpinner);
            Storyboard.SetTargetProperty(animation, new PropertyPath(TextBlock.TextProperty));
            _downloadHourglassStoryboard = new Storyboard();
            _downloadHourglassStoryboard.Children.Add(animation);
            _downloadHourglassStoryboard.Begin(DownloadSpinner, true);
        }

        private void StopDownloadHourglassAnimation()
        {
            if (_downloadHourglassStoryboard != null)
            {
                _downloadHourglassStoryboard.Stop(DownloadSpinner);
                _downloadHourglassStoryboard = null;
            }
            DownloadSpinner.Visibility = Visibility.Collapsed;
        }

        private void StartEstimatedAnalysisProgress(string fileName)
        {
            StopEstimatedAnalysisProgress();
            var started = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                if (!ReferenceEquals(_analysisProgressTimer, timer)) return;
                var seconds = started.Elapsed.TotalSeconds;
                AnalyzeProgress.Value = EstimatedAnalysisProgress(seconds);
                AnalyzeTip.Text = $"正在分析: {fileName} · 已等待 {(int)seconds} 秒 ";
            };
            _analysisProgressTimer = timer;
            timer.Start();
        }

        private void StopEstimatedAnalysisProgress()
        {
            _analysisProgressTimer?.Stop();
            _analysisProgressTimer = null;
        }

        private static double EstimatedAnalysisProgress(double seconds) => seconds switch
        {
            <= 0 => 0,
            < 15 => seconds * 2,
            < 60 => 30 + (seconds - 15) * 35 / 45,
            < 120 => 65 + (seconds - 60) * 23 / 60,
            _ => Math.Min(95, 88 + (seconds - 120) * 7 / 180)
        };

        private void StartEstimatedDownloadProgress()
        {
            StopEstimatedDownloadProgress();
            var started = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                if (!ReferenceEquals(_downloadProgressTimer, timer)) return;
                var shown = EstimatedDownloadProgress(started.Elapsed.TotalSeconds);
                DownloadProgress.Value = shown;
                DownloadStatus.Text = $"下载中 · 约 {shown:0}%";
            };
            _downloadProgressTimer = timer;
            timer.Start();
        }

        private void StopEstimatedDownloadProgress()
        {
            _downloadProgressTimer?.Stop();
            _downloadProgressTimer = null;
        }

        private static double EstimatedDownloadProgress(double seconds) => seconds switch
        {
            <= 0 => 0,
            < 5 => seconds * 3,
            < 20 => 15 + (seconds - 5) * 3,
            < 60 => 60 + (seconds - 20) * 28 / 40,
            _ => Math.Min(95, 88 + (seconds - 60) * 7 / 120)
        };

        public CompoundStatsPage() : this(
            App.Services.GetRequiredService<ITicketStore>(),
            App.Services.GetRequiredService<IVideoSearchService>(),
            App.Services.GetRequiredService<DownloadService>(),
            App.Services.GetRequiredService<AiAnalysisService>()) { }

        public CompoundStatsPage(ITicketStore store, IVideoSearchService bili, DownloadService downloader, AiAnalysisService ai)
        {
            _store = store;
            _bili = bili;
            _downloader = downloader;
            _ai = ai;
            InitializeComponent();
            DataContext = this;
            Loaded += (_, _) =>
            {
                RefreshLocalDir(VideoDir);
                // 如果已保存 B站 UID，作为默认值显示在搜索框
                var savedUid = ((ICredentialStore)_store).LoadBiliUid();
                if (!string.IsNullOrWhiteSpace(savedUid) && string.IsNullOrWhiteSpace(SearchBox.Text))
                {
                    SearchBox.Text = savedUid;
                    SearchBox.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
                }
            };
            Unloaded += (_, _) =>
            {
                _downloadCts?.Cancel();
                _searchCts?.Cancel();
                _analysisCts?.Cancel();
            };
        }

        private void OpenTicketStats_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.NavigateTo("tickets");
        }

        // ===== 设置持久化 =====
        private string CurrentApiKey
        {
            get
            {
                var key = _store.LoadApiKey();
                return key ?? "";
            }
        }

        private static string VideoDir => AppPaths.VideoDirectory;

        private void DeleteLocalVideo_Click(object sender, RoutedEventArgs e)
        {
            var selected = LocalList.SelectedItems.Cast<ListBoxItem>()
                .Select(item => item.Tag?.ToString())
                .Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (selected.Count == 0) return;

            foreach (var path in selected)
            {
                try { System.IO.File.Delete(path!); } catch (Exception ex) { Debug.WriteLine($"[CompoundStatsPage] 文件删除失败: {ex.Message}"); }
            }
            RefreshLocalDir(VideoDir);
            AnalyzeStatus.Text = "已删除选中视频";
        }

        // ===== 搜索视频 =====
        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var savedUid = ((ICredentialStore)_store).LoadBiliUid();
            if (!string.IsNullOrEmpty(savedUid) && SearchBox.Text == savedUid)
            {
                SearchBox.Text = "";
                SearchBox.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var savedUid = ((ICredentialStore)_store).LoadBiliUid();
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                if (!string.IsNullOrEmpty(savedUid))
                {
                    SearchBox.Text = savedUid;
                    SearchBox.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
                }
            }
        }

        private async void SearchVideo_Click(object sender, RoutedEventArgs e)
        {
            var input = SearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(input)) return;

            SearchResults.Clear();
            _searchCts?.Cancel();
            using var searchCts = new CancellationTokenSource();
            _searchCts = searchCts;

            try
            {
                if (BiliService.IsUrl(input)) await SearchByUrl(input, searchCts.Token);
                else if (long.TryParse(input, out long uid) && uid > 1000) await SearchByUid(uid, searchCts.Token);
                else SearchResults.Add(new VideoInfo { Title = "仅支持 B站UID或视频链接搜索" });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (searchCts.IsCancellationRequested || !ReferenceEquals(_searchCts, searchCts)) return;
                SearchResults.Clear();
                SearchResults.Add(new VideoInfo { Title = $"搜索失败: {ex.Message}" });
            }
            finally
            {
                if (ReferenceEquals(_searchCts, searchCts)) _searchCts = null;
            }
        }

        // ===== B站 UID 搜索（WBI 签名） =====
        private async Task SearchByUid(long uid, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SearchResults.Clear();
            SearchResults.Add(new VideoInfo { Title = $"获取 UID {uid} 视频…" });

            try
            {
                var root = await _bili.SearchByUid(uid, ct);
                ct.ThrowIfCancellationRequested();
                var code = root?["code"]?.GetValue<int>() ?? -1;

                if (code == 0)
                {
                    var vlist = root?["data"]?["list"]?["vlist"]?.AsArray();
                    if (vlist != null && vlist.Count > 0)
                    {
                        SearchResults.Clear();
                        foreach (var item in vlist) if (item != null) AddVlistItem(item);
                        RefreshSearchIndices();
                        return;
                    }
                }
                SearchResults.Clear();
                SearchResults.Add(new VideoInfo { Title = code == 0 ? $"UID {uid} 没有公开视频" : $"请求失败 (code={code}): {root?["message"]}" });
                if (code == -352 && !_store.IsBiliLoggedIn)
                {
                    var loginHint = "UID搜索被风控，请使用视频链接搜索 或 点击「登录B站」获取登录态";
                    SearchResults.Add(new VideoInfo { Title = loginHint });
                    await PromptLogin(ct);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ct.ThrowIfCancellationRequested();
                SearchResults.Clear();
                SearchResults.Add(new VideoInfo { Title = $"获取失败: {ex.Message}" });
            }
        }

        private void AddVlistItem(JsonNode item)
        {
            SearchResults.Add(new VideoInfo
            {
                Bvid = item["bvid"]?.ToString() ?? "",
                Title = item["title"]?.ToString() ?? "",
                Author = item["author"]?.ToString() ?? "",
                Duration = item["length"]?.ToString() ?? "",
                Play = int.TryParse(item["play"]?.ToString(), out var p) ? p : 0,
                Formats = VideoQualityOptions()
            });
        }

        private void RefreshSearchIndices()
        {
            for (int i = 0; i < SearchResults.Count; i++)
            {
                SearchResults[i].Index = i + 1;
                SearchResults[i].Notify();
            }
        }

        // ===== URL 链接搜索 =====
        private async Task SearchByUrl(string url, CancellationToken ct = default)
        {
            if (url.Contains("bilibili.com") || url.Contains("b23.tv"))
            {
                if (url.Contains("space.bilibili.com"))
                {
                    var uid = BiliService.ExtractUidFromUrl(url);
                    if (uid.HasValue) { await SearchByUid(uid.Value, ct); return; }
                }
                var bvid = BiliService.ExtractBvid(url);
                if (!string.IsNullOrEmpty(bvid)) { await SearchByBvid(bvid, ct); return; }
            }

            SearchResults.Clear();
            SearchResults.Add(new VideoInfo { Title = "无法识别链接，请输入 B站 UID 或视频链接" });
        }

        private async Task SearchByBvid(string bvid, CancellationToken ct = default)
        {
            var root = await _bili.SearchByBvid(bvid, ct);
            ct.ThrowIfCancellationRequested();
            var code = root?["code"]?.GetValue<int>() ?? -1;
            if (code != 0)
            {
                SearchResults.Clear();
                SearchResults.Add(new VideoInfo { Title = $"请求失败 (code={code}): {root?["message"]}" });
                return;
            }

            var data = root?["data"];
            if (data is null)
            {
                SearchResults.Clear();
                SearchResults.Add(new VideoInfo { Title = "请求成功但没有返回视频信息" });
                return;
            }
            var formats = VideoQualityOptions();
            SearchResults.Add(new VideoInfo
            {
                Bvid = bvid,
                Title = HtmlDecode(data?["title"]?.ToString() ?? ""),
                Author = data?["owner"]?["name"]?.ToString() ?? "",
                Duration = FormatDuration(data?["duration"]?.ToString() ?? "0"),
                Play = int.TryParse(data?["stat"]?["view"]?.ToString(), out var p) ? p : 0,
                Formats = formats
            });
            RefreshSearchIndices();
        }

        // ===== B站登录 =====
        private async Task PromptLogin(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_store.IsBiliLoggedIn) return;
            var dlg = new BiliLoginDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                ct.ThrowIfCancellationRequested();
                var keyword = SearchBox.Text.Trim();
                if (long.TryParse(keyword, out long uid) && uid > 1000)
                    await SearchByUid(uid, ct);
            }
        }

        // ===== 下载视频（yt-dlp） =====
        private void PreviewList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PreviewList.SelectedItem is not VideoInfo v || !v.IsDownloadable)
            {
                DetailPanel.Visibility = Visibility.Collapsed;
                QualityPanel.Visibility = Visibility.Collapsed;
                NoSelection.Visibility = Visibility.Visible;
                DownloadBtn.IsEnabled = false;
                DownloadStatus.Text = "";
                return;
            }

            NoSelection.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            QualityPanel.Visibility = Visibility.Visible;
            DetailTitle.Text = v.Title;
            DetailAuthor.Text = $"UP主: {v.Author}";
            DetailDuration.Text = $"时长: {v.Duration}";
            DetailPlay.Text = $"播放: {v.Play:N0}";
            Rd720P.IsChecked = true;
            DownloadBtn.IsEnabled = true;
            DownloadStatus.Text = "";
        }

        private async void DownloadVideo_Click(object sender, RoutedEventArgs e)
        {
            if (PreviewList.SelectedItem is not VideoInfo v || !v.IsDownloadable)
            {
                DownloadStatus.Text = "⚠ 请先在左侧选择视频";
                return;
            }

            System.IO.Directory.CreateDirectory(VideoDir);
            _downloadCts?.Cancel();
            using var downloadCts = new CancellationTokenSource();
            _downloadCts = downloadCts;
            DownloadBtn.IsEnabled = false;
            DownloadProgress.Visibility = Visibility.Visible;
            DownloadProgress.Value = 0;
            DownloadStatus.Text = "准备下载…";
            StartDownloadHourglassAnimation();
            StartEstimatedDownloadProgress();

            try
            {
                await _downloader.EnsureYtDlp(downloadCts.Token);
                downloadCts.Token.ThrowIfCancellationRequested();
                var url = $"https://www.bilibili.com/video/{v.Bvid}";
                var output = System.IO.Path.Combine(VideoDir, $"{DownloadService.SanitizeFileName(v.Title)}.mp4");
                var quality = Rd720P.IsChecked == true ? "720" : "480";
                await _downloader.RunDownload(url, output, quality, downloadCts.Token);
                downloadCts.Token.ThrowIfCancellationRequested();
                StopEstimatedDownloadProgress();
                DownloadProgress.Value = 100;
                DownloadStatus.Text = "✅ 下载完成";
                await Task.Delay(400, downloadCts.Token);
                RefreshLocalDir(VideoDir);
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(_downloadCts, downloadCts)) DownloadStatus.Text = "已取消下载";
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_downloadCts, downloadCts)) DownloadStatus.Text = $"❌ 下载失败: {ex.Message}";
            }
            finally
            {
                if (ReferenceEquals(_downloadCts, downloadCts))
                {
                    StopEstimatedDownloadProgress();
                    StopDownloadHourglassAnimation();
                    _downloadCts = null;
                    DownloadBtn.IsEnabled = true;
                    DownloadProgress.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void RefreshLocalDir(string dir)
        {
            var fileInfos = new List<(string display, string path)>();
            if (!System.IO.Directory.Exists(dir)) { fileInfos.Add(("视频目录为空", "")); }
            else
            {
                var exts = new[] { ".mp4", ".flv", ".mkv", ".avi", ".mov", ".wmv", ".webm" };
                var files = System.IO.Directory.GetFiles(dir, "*.*")
                    .Where(f => exts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
                    .Select(f => new System.IO.FileInfo(f))
                    .OrderByDescending(f => _store.ExtractPeriodFromFileName(f.Name) ?? int.MinValue)
                    .ThenByDescending(f => f.LastWriteTime)
                    .ToList();
                if (files.Count == 0) { fileInfos.Add(("视频目录为空", "")); }
                else foreach (var f in files)
                        fileInfos.Add(($"{System.IO.Path.GetFileNameWithoutExtension(f.Name)}  ({f.Length / 1024.0 / 1024.0:F1}MB)", f.FullName));
            }

            void Populate(ListBox lb)
            {
                lb.Items.Clear();
                foreach (var (display, path) in fileInfos)
                {
                    lb.Items.Add(new ListBoxItem { Content = display, Tag = path });
                }
            }
            Populate(LocalList);
        }

        private async void AnalyzeVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_isAnalyzing)
            {
                AnalyzeStatus.Text = "⏳ 正在分析中，请等待完成…";
                return;
            }

            if (LocalList.SelectedItem is not ListBoxItem item || string.IsNullOrEmpty(item.Tag?.ToString()))
            {
                AnalyzeStatus.Text = "⚠ 请先在左侧选择一个视频";
                return;
            }
            var fp = item.Tag.ToString()!;

            // 遍历本地数据文件，检查该视频是否已分析过
            var fileType = _store.DetectTypeFromFileName(fp);
            var filePeriod = _store.ExtractPeriodFromFileName(fp);
            var alreadyAnalyzed = fileType.HasValue && filePeriod.HasValue
                && _store.DataFileExists(fileType.Value, filePeriod.Value);
            if (alreadyAnalyzed)
            {
                AnalyzeStatus.Text = "该视频已分析过，请前往「复式票统计」查看";
                return;
            }

            _analysisCts?.Cancel();
            var analysisCts = new CancellationTokenSource();
            _analysisCts = analysisCts;
            try
            {
                await AnalyzeVideos(fp, analysisCts.Token);
            }
            finally
            {
                if (ReferenceEquals(_analysisCts, analysisCts))
                    _analysisCts = null;
                analysisCts.Dispose();
            }
        }

        private async Task AnalyzeVideos(string filePath, CancellationToken ct)
        {
            _isAnalyzing = true;
            AnalyzeBtn.IsEnabled = false;
            AnalyzeProgress.Visibility = Visibility.Visible;
            AnalyzeProgress.Value = 0;
            AnalyzeStatus.Text = "⏳ AI分析中请稍等…";

            string? finalText = null;
            try
            {
                StartHourglassAnimation();

                var name = System.IO.Path.GetFileName(filePath);
                AnalyzeTip.Text = $"正在分析: {name}";
                StartEstimatedAnalysisProgress(name);
                var (tickets, period, detectedType) = await AnalyzeSingleVideo(filePath, ct);
                ct.ThrowIfCancellationRequested();

                var typeName = (detectedType ?? LotteryType.SSQ) == LotteryType.DLT ? "大乐透" : "双色球";
                StopEstimatedAnalysisProgress();
                AnalyzeProgress.Value = 100;
                if (tickets.Count > 0)
                {
                    var savedFile = _store.SaveResultAsFile(tickets, period, detectedType);
                    finalText = $"✅ 成功解析 {tickets.Count} 张{typeName}复式票";
                    AnalyzeTip.Text = string.IsNullOrEmpty(savedFile) ? $"已分析 1 个文件" : $"已保存: {savedFile}";
                }
                else
                {
                    finalText = $"⚠ 未识别到{typeName}号码，请检查视频画面是否清晰";
                    AnalyzeTip.Text = "";
                }
                RefreshLocalDir(VideoDir);
            }
            catch (OperationCanceledException)
            {
                finalText = "已取消视频分析";
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (msg.Contains("FreeTierOnly") || msg.Contains("403"))
                    msg = "⚠️ 免费额度已用完，请在设置 → API设置中更换模型";
                else if (msg.Contains("curl") || msg.Contains("退出码"))
                    msg = "⚠️ 网络请求失败，请检查网络后重试";
                finalText = msg.Contains("免费额度") ? $"❌ {msg}" : $"❌ 分析失败: {msg}";
            }
            finally
            {
                StopEstimatedAnalysisProgress();
                StopHourglassAnimation();
                if (finalText != null)
                    AnalyzeStatus.Text = finalText;
                AnalyzeProgress.Visibility = Visibility.Collapsed;
                _isAnalyzing = false;
                AnalyzeBtn.IsEnabled = true;
            }
        }

        // ===== 千问大模型 API =====
        private async Task<(List<Ticket> tickets, int? period, LotteryType? type)> AnalyzeSingleVideo(string filePath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var key = CurrentApiKey;
            if (string.IsNullOrEmpty(key))
                throw new Exception("请在「设置」页面中配置 API Key");

            var fileInfo = new System.IO.FileInfo(filePath);
            if (fileInfo.Length > 10 * 1024 * 1024)
            {
                var sizeMB = fileInfo.Length / (1024.0 * 1024.0);
                throw new Exception($"视频超过10MB ({sizeMB:F1}MB)，请先压缩");
            }

            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath, ct);
            ct.ThrowIfCancellationRequested();
            var base64 = Convert.ToBase64String(fileBytes);
            var mime = AiAnalysisService.GetMimeType(filePath);

            var prompt = "你是彩票复式票号码识别助手。识别视频画面中的双色球（红球01-33，蓝球01-16）或大乐透（前区01-35，后区01-12）复式票号码。\n"
                + "先判断彩票类型，第一行输出：类型:双色球 或 类型:大乐透\n"
                + "第二行输出期数，格式：期数:2024001（完整的年份+期号）\n"
                + "再每注一行，格式：红球号码;蓝球号码（双色球）或 前区号码;后区号码（大乐透）\n"
                + "双色球示例：01,03,15,22,28,31;09\n"
                + "大乐透示例：05,12,18,25,33;02,09\n"
                + "只识别复式票，忽略胆拖票，红球或蓝球位置有遮挡识的，整个票直接不要了\n"
                + "按金额大小从大到小排序，只需要金额排前33张的复式票。\n"
                + "只输出类型、期数和号码，不要解释。";

            var host = _store.LoadApiHost();
            var useNativeDashScope = !string.IsNullOrWhiteSpace(host);

            // 检查模型是否已配置，否则 API 会因 model=null 直接拒绝
            var model = _store.LoadModel();
            if (string.IsNullOrWhiteSpace(model))
                throw new Exception("请在「设置」页面中配置分析模型（推荐 qwen3-vl-plus）");

            // === 调试：打印配置值 ===
            Debug.WriteLine($"[AnalyzeVideo] API Key 长度: {key?.Length ?? 0}, 模型: {model}, Host: {host}, 原生模式: {useNativeDashScope}");
            Debug.WriteLine($"[AnalyzeVideo] 视频: {Path.GetFileName(filePath)}, 大小: {fileInfo.Length / 1024.0:F1}KB");
            Debug.WriteLine($"[AnalyzeVideo] 端点: {(useNativeDashScope ? $"https://{host}/api/v1/services/aigc/multimodal-generation/generation" : "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions")}");

            string bodyJson;
            if (useNativeDashScope)
            {
                var root = new JsonObject
                {
                    ["model"] = model,
                    ["input"] = new JsonObject
                    {
                        ["messages"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["role"] = "user",
                                ["content"] = new JsonArray
                                {
                                    new JsonObject { ["video"] = $"data:{mime};base64,{base64}" },
                                    new JsonObject { ["text"] = prompt }
                                }
                            }
                        }
                    },
                    ["parameters"] = new JsonObject
                    {
                        ["temperature"] = 0.1,
                        ["enable_thinking"] = false,
                        ["result_format"] = "message"
                    }
                };
                bodyJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            }
            else
            {
                var root = new JsonObject
                {
                    ["model"] = model,
                    ["messages"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["role"] = "user",
                            ["content"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["type"] = "video_url",
                                    ["video_url"] = new JsonObject
                                    {
                                        ["url"] = $"data:{mime};base64,{base64}",
                                        ["fps"] = 2
                                    }
                                },
                                new JsonObject { ["type"] = "text", ["text"] = prompt }
                            }
                        }
                    },
                    ["temperature"] = 0.1
                };
                root["enable_thinking"] = false;
                bodyJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            }

            var endpoint = useNativeDashScope
                ? $"https://{host}/api/v1/services/aigc/multimodal-generation/generation"
                : "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";

            var rawResponse = await _ai.CurlPostAsync(key ?? "", bodyJson, endpoint, ct);
            ct.ThrowIfCancellationRequested();
            // === 调试：响应信息 ===
            Debug.WriteLine($"[AnalyzeVideo] 响应原始长度: {rawResponse?.Length ?? 0}");
            Debug.WriteLine($"[AnalyzeVideo] 响应前200字符: {(rawResponse?.Length > 200 ? rawResponse[..200] + "..." : rawResponse)}");

            // 检查 API 是否返回了错误（DashScope 有时返回 200 + 错误 JSON）
            var errMsg = CheckDashScopeError(rawResponse ?? "");
            if (errMsg != null) throw new Exception(errMsg);

            // 从 JSON 响应中提取 AI 返回的纯文本内容（而非原始 JSON 字符串）
            var text = ExtractAiContent(rawResponse ?? "");
            Debug.WriteLine($"[AnalyzeVideo] 提取文本: {(text?.Length > 200 ? text[..200] + "..." : text)}");
            var detectedType = _store.ExtractType(text!)
                            ?? _store.DetectTypeFromFileName(filePath);
            // 优先从 AI 响应提取期号，AI 未返回时从文件名提取回退
            var period = _store.ExtractPeriod(text!)
                      ?? _store.ExtractPeriodFromFileName(filePath);
            // 使用 AI 识别出的类型决定号码范围，无法识别时默认双色球
            var tickets = _store.ParseTicketsFromText(text!, detectedType ?? LotteryType.SSQ);
            return (tickets, period, detectedType);
        }

        /// <summary>检查 API 响应中的错误码，有错误返回友好提示文本，无错误返回 null</summary>
        private static string? CheckDashScopeError(string rawResponse)
        {
            if (string.IsNullOrEmpty(rawResponse)) return null;
            try
            {
                var doc = JsonNode.Parse(rawResponse);
                if (doc == null) return null;
                var code = doc["code"]?.GetValue<string>();
                var message = doc["message"]?.GetValue<string>();
                if (string.IsNullOrEmpty(code)) return null;

                switch (code)
                {
                    case "InvalidApiKey":
                        return "API Key 无效。如已配置「推理 API 域名」，请将其留空，使用默认的 DashScope 公共接口即可。";
                    case "FreeTierOnly":
                        return "免费额度已用完，请在设置更换模型。";
                    case "BackendError":
                    case "InternalError":
                        return $"AI 服务内部错误({code})，请稍后重试。";
                    default:
                        return $"API 返回错误 ({code}): {message}";
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// 从 AI API 的 JSON 响应中提取纯文本内容。
        /// 兼容模式：content 为字符串；原生 DashScope (result_format=message)：content 为 [{text: "..."}, ...]
        /// </summary>
        private static string ExtractAiContent(string response)
        {
            if (string.IsNullOrEmpty(response)) return response;

            try
            {
                var doc = JsonNode.Parse(response);
                if (doc == null) return response;

                // 兼容模式：choices[0].message.content（字符串）
                var choices = doc["choices"] as JsonArray;
                if (choices != null && choices.Count > 0)
                {
                    var text = GetContentText(choices[0]?["message"]?["content"]);
                    if (text != null) return text;
                }

                // 原生 DashScope：output
                var output = doc["output"];
                if (output != null)
                {
                    // output.text
                    var text = output["text"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(text)) return text;

                    // output.choices[0].message.content（字符串 或 数组）
                    var outChoices = output["choices"] as JsonArray;
                    if (outChoices != null && outChoices.Count > 0)
                    {
                        var outText = GetContentText(outChoices[0]?["message"]?["content"]);
                        if (outText != null) return outText;
                    }
                }

                return response;
            }
            catch { return response; }
        }

        /// <summary>从 content 节点提取文本：支持字符串和 [{text: "..."}] 数组</summary>
        private static string? GetContentText(JsonNode? content)
        {
            if (content == null) return null;
            // 字符串
            if (content is JsonValue) { var s = content.GetValue<string>(); return string.IsNullOrEmpty(s) ? null : s; }
            // 数组 [{text: "..."}, ...]
            if (content is JsonArray arr)
            {
                var sb = new StringBuilder();
                foreach (var item in arr)
                {
                    var t = item?["text"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(t)) sb.Append(t);
                }
                var r = sb.ToString();
                return r.Length > 0 ? r : null;
            }
            return null;
        }

        private static string FormatDuration(string seconds)
        {
            if (int.TryParse(seconds, out int s)) { int m = s / 60; int h = m / 60; return h > 0 ? $"{h}:{m % 60:D2}:{s % 60:D2}" : $"{m}:{s % 60:D2}"; }
            return seconds;
        }

        private static string HtmlDecode(string text) => System.Net.WebUtility.HtmlDecode(text);

        private static List<VideoFormat> VideoQualityOptions() => new()
        {
            new() { Quality = 32, Label = "480P", Codec = "h264" },
            new() { Quality = 64, Label = "720P", Codec = "h264" },
        };

        public void LoadData() { }
    }
}
