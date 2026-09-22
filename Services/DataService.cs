using System.Globalization;
using System.Diagnostics;
using System.Net.Http;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services
{
    /// <summary>
    /// 数据服务：内嵌基数据 + 本地文件持久化 + 网络增量更新
    /// 启动时优先加载本地文件，无本地文件则从内嵌数据加载。
    /// 更新数据时下载到本地文件并缓存。
    /// </summary>
    public class DataService : IDataService
    {
        /// <summary>
        /// 当数据更新时触发，供各页面订阅刷新
        /// </summary>
        public event Action? DataUpdated;

        public void NotifyDataUpdated() => DataUpdated?.Invoke();

        public string? LastErrorMessage { get; private set; }

        private readonly ThreadLocal<Random> _rnd = new(() => new());
        private readonly SemaphoreSlim _updateLock = new(1, 1);
        private Lazy<List<DrawRecord>?> _cachedRecords = null!;
        private readonly HttpClient _http;
        private string DataUrl { get; }

        // 本地持久化文件路径：与 exe 同目录
        private static readonly string LocalDataDir = AppPaths.Root;
        private static readonly string LocalDataFile = AppPaths.DataFile;
        private static readonly string LegacyDataFile = AppPaths.LegacyDataFile;

        /// <summary>默认构造函数（生产环境使用）</summary>
        public DataService() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(8) }, "https://data.17500.cn/ssq_asc.txt") { }

        /// <summary>测试用构造函数 — 可注入 HttpClient 和数据 URL</summary>
        public DataService(HttpClient httpClient, string dataUrl)
        {
            _http = httpClient;
            DataUrl = dataUrl;
            _cachedRecords = new Lazy<List<DrawRecord>?>(() => LoadRecords());
        }

        /// <summary>本地缓存文件是否存在</summary>
        public bool HasLocalFile => System.IO.File.Exists(LocalDataFile) || System.IO.File.Exists(LegacyDataFile);
        /// <summary>本地缓存文件路径</summary>
        public string LocalDataFilePath => AppPaths.ExistingFile(LocalDataFile, LegacyDataFile);

        /// <summary>
        /// 获取所有历史开奖数据（优先本地文件，其次内嵌数据）
        /// </summary>
        public List<DrawRecord> GetAllRecords()
        {
            return _cachedRecords.Value ?? new List<DrawRecord>();
        }

        /// <summary>
        /// 当前数据期数
        /// </summary>
        public int GetRecordCount() => GetAllRecords().Count;

        /// <summary>
        /// 清空本地文件和内存缓存（下次启动从内嵌数据重建）
        /// </summary>
        public void ClearAllData()
        {
            try
            {
                if (System.IO.File.Exists(LocalDataFile))
                    System.IO.File.Delete(LocalDataFile);
                if (System.IO.File.Exists(LegacyDataFile))
                    System.IO.File.Delete(LegacyDataFile);
                _cachedRecords = new Lazy<List<DrawRecord>?>(LoadRecords);
                LastErrorMessage = null;
            }
            catch { /* 忽略删除失败 */ }
        }

        /// <summary>
        /// 异步更新数据：下载 → 对比 → 保存到本地 → 加载
        /// 返回新增期数，0 无更新，负数表示错误
        /// </summary>
        public async Task<int> TryUpdateAsync()
        {
            if (!await _updateLock.WaitAsync(0)) return -8; // 已有更新进行中，直接返回
            try
            {
                // 1. 获取远程数据
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await _http.GetAsync(DataUrl, cts.Token);
                if (!response.IsSuccessStatusCode)
                    return -2;

                var bytes = await response.Content.ReadAsByteArrayAsync();
                var allText = System.Text.Encoding.UTF8.GetString(bytes);
                var allLines = allText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                if (allLines.Length == 0) return -3;

                // 2. 对比远程最后一期 vs 本地最后一期
                string remoteLast = allLines[^1].Trim();
                string remotePeriod = remoteLast.Split(' ')[0];
                if (!int.TryParse(remotePeriod, out int remoteP))
                    return -5;

                var parsedRecords = ParseLines(allLines);
                if (parsedRecords.Count == 0)
                {
                    LastErrorMessage = "远程数据未包含有效开奖记录";
                    return -5;
                }

                int localLastPeriod = GetLastPeriod();
                if (localLastPeriod < 0) return -4;

                if (remoteP <= localLastPeriod)
                {
                    // 无新数据时仍确保本地文件存在（可能从未保存过）
                    if (!await SaveLinesToFileAsync(allLines)) return -11;
                    return 0;
                }

                // 3. 保存完整远程数据到本地文件
                if (!await SaveLinesToFileAsync(allLines)) return -11;

                // 4. 解析并缓存
                int newCount = 0;
                foreach (var rec in parsedRecords)
                {
                    if (rec.Period > localLastPeriod)
                        newCount++;
                }

                _cachedRecords = new Lazy<List<DrawRecord>?>(() => parsedRecords);
                DataUpdated?.Invoke();
                return newCount;
            }
            catch (TaskCanceledException ex) { LastErrorMessage = ex.Message; return -6; }
            catch (HttpRequestException ex) { LastErrorMessage = ex.Message; return -7; }
            catch (Exception ex) { LastErrorMessage = ex.Message; return -10; }
            finally { _updateLock.Release(); }
        }

        /// <summary>
        /// 将原始文本行保存到本地文件
        /// </summary>
        private async Task<bool> SaveLinesToFileAsync(string[] lines)
        {
            try
            {
                System.IO.Directory.CreateDirectory(LocalDataDir);
                string tmp = LocalDataFile + ".tmp";
                await System.IO.File.WriteAllLinesAsync(tmp, lines);
                System.IO.File.Move(tmp, LocalDataFile, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"本地数据保存失败：{ex.Message}";
                Debug.WriteLine($"[DataService.SaveLinesToFileAsync] 保存失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取本地最后期号
        /// </summary>
        public int GetLastPeriod()
        {
            try
            {
                var records = GetAllRecords();
                if (records.Count == 0) return -1;
                return records[^1].Period;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DataService.GetLastPeriod] 获取最新期号异常: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 清空内存缓存（下次调用 GetAllRecords 时重新加载）
        /// </summary>
        public void ResetCache()
        {
            _cachedRecords = new Lazy<List<DrawRecord>?>(LoadRecords);
        }

        private List<DrawRecord> LoadRecords()
        {
            var dataFile = AppPaths.ExistingFile(LocalDataFile, LegacyDataFile);
            if (System.IO.File.Exists(dataFile))
            {
                try
                {
                    var lines = System.IO.File.ReadAllLines(dataFile);
                    var records = ParseLines(lines);
                    if (records.Count == 0)
                    {
                        LastErrorMessage = $"本地开奖数据文件无法解析: {dataFile}";
                    }
                    return records;
                }
                catch { /* 文件损坏忽略，走内嵌数据 */ }
            }

            if (System.IO.File.Exists(dataFile))
                return new List<DrawRecord>();

            return ParseLines(SsqRawData.Lines);
        }

        // ==================== 解析逻辑 ====================

        internal static List<DrawRecord> ParseLines(string[] lines)
        {
            var records = new List<DrawRecord>(lines.Length);
            Span<Range> fields = stackalloc Range[28];
            foreach (var line in lines)
            {
                var text = line.AsSpan().Trim();
                int fieldCount = 0;
                // Keep the original space-only separator and ignore unused trailing fields.
                foreach (var range in text.Split(' '))
                {
                    if (range.Start.Value == range.End.Value) continue;
                    fields[fieldCount++] = range;
                    if (fieldCount == fields.Length) break;
                }
                if (fieldCount < 9) continue;

                if (!int.TryParse(text[fields[0]], out int period)) continue;
                if (!DateTime.TryParseExact(text[fields[1]], "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                var reds = new int[6];
                bool redOk = true;
                for (int i = 0; i < 6; i++)
                {
                    if (!int.TryParse(text[fields[2 + i]], out reds[i]))
                    { redOk = false; break; }
                }
                if (!redOk) continue;
                Array.Sort(reds);

                if (!int.TryParse(text[fields[8]], out int blue)) continue;

                // 早期数据(2003-2004)可能不包含销售/奖池字段
                long p15 = 0, p16 = 0, p18 = 0, p20 = 0;
                int p17 = 0, p19 = 0, p21 = 0, p23 = 0, p25 = 0, p27 = 0;
                if (fieldCount > 15) long.TryParse(text[fields[15]], out p15);
                if (fieldCount > 16) long.TryParse(text[fields[16]], out p16);
                if (fieldCount > 17) int.TryParse(text[fields[17]], out p17);
                if (fieldCount > 18) long.TryParse(text[fields[18]], out p18);
                if (fieldCount > 19) int.TryParse(text[fields[19]], out p19);
                if (fieldCount > 20) long.TryParse(text[fields[20]], out p20);
                if (fieldCount > 21) int.TryParse(text[fields[21]], out p21);
                if (fieldCount > 23) int.TryParse(text[fields[23]], out p23);
                if (fieldCount > 25) int.TryParse(text[fields[25]], out p25);
                if (fieldCount > 27) int.TryParse(text[fields[27]], out p27);

                records.Add(new DrawRecord
                {
                    Period = period,
                    DrawDate = date,
                    RedBalls = reds,
                    BlueBall = blue,
                    SalesAmount = p15,
                    PoolAmount = p16,
                    FirstPrizeCount = p17,
                    FirstPrizeAmount = p18,
                    SecondPrizeCount = p19,
                    SecondPrizeAmount = p20,
                    ThirdPrizeCount = p21,
                    FourthPrizeCount = p23,
                    FifthPrizeCount = p25,
                    SixthPrizeCount = p27
                });
            }
            return records;
        }

        // ==================== 兼容旧接口 ====================

        public List<DrawRecord> GenerateSampleData(int count = 60)
        {
            var all = GetAllRecords();
            return all.TakeLast(Math.Min(count, all.Count)).ToList();
        }

        public int[] GenerateRedBalls()
        {
            var set = new HashSet<int>();
            while (set.Count < 6)
                set.Add(_rnd.Value!.Next(1, 34));
            return set.OrderBy(x => x).ToArray();
        }

        public static long Combination(int n, int k)
        {
            if (k > n || k < 0) return 0;
            if (k == 0 || k == n) return 1;
            k = Math.Min(k, n - k);
            long result = 1;
            for (int i = 0; i < k; i++)
                result = result * (n - i) / (i + 1);
            return result;
        }
    }
}
