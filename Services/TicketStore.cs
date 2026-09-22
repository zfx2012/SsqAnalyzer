using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SsqAnalyzer.Services
{
    public enum LotteryType { SSQ, DLT }

    public class Ticket
    {
        public List<int> Reds { get; set; } = new();
        public List<int> Blues { get; set; } = new();
    }

    public class TicketStore : ITicketStore
    {
        public List<Ticket> Tickets { get; } = new();
        public event Action? TicketsChanged;

        private LotteryType _type = LotteryType.SSQ;
        public LotteryType CurrentType
        {
            get => _type;
            set { if (_type != value) { _type = value; Tickets.Clear(); _period = null; TicketsChanged?.Invoke(); PeriodChanged?.Invoke(); TypeChanged?.Invoke(); } }
        }
        public int RedMax => _type == LotteryType.DLT ? 35 : 33;
        public int BlueMax => _type == LotteryType.DLT ? 12 : 16;
        public event Action? TypeChanged;

        // ===== DPAPI 完整性校验 =====
        private static readonly byte[] DPAPI_MAGIC = { 0x53, 0x51, 0x41, 0x01 }; // "SQA\1"

        private byte[] ProtectWithMagic(byte[] plain)
        {
            var data = new byte[DPAPI_MAGIC.Length + plain.Length];
            Buffer.BlockCopy(DPAPI_MAGIC, 0, data, 0, DPAPI_MAGIC.Length);
            Buffer.BlockCopy(plain, 0, data, DPAPI_MAGIC.Length, plain.Length);
            return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        }

        private byte[] UnprotectWithMagic(byte[] encrypted)
        {
            var data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            if (data.Length < DPAPI_MAGIC.Length)
                throw new InvalidOperationException("加密数据损坏：长度不匹配，可能已被篡改或旧版格式");
            for (int i = 0; i < DPAPI_MAGIC.Length; i++)
            {
                if (data[i] != DPAPI_MAGIC[i])
                    throw new InvalidOperationException("加密数据损坏：魔数不匹配，可能已被篡改或旧版格式");
            }
            var result = new byte[data.Length - DPAPI_MAGIC.Length];
            Buffer.BlockCopy(data, DPAPI_MAGIC.Length, result, 0, result.Length);
            return result;
        }

        private int? _period;
        public int? Period
        {
            get => _period;
            set { _period = value; PeriodChanged?.Invoke(); }
        }
        public event Action? PeriodChanged;

        public void AddTicket(Ticket t)
        {
            Tickets.Add(t);
            TicketsChanged?.Invoke();
        }

        public void AddTickets(IEnumerable<Ticket> tickets)
        {
            Tickets.AddRange(tickets);
            TicketsChanged?.Invoke();
        }

        public void RemoveTicketAt(int idx)
        {
            if (idx >= 0 && idx < Tickets.Count)
            {
                Tickets.RemoveAt(idx);
                TicketsChanged?.Invoke();
            }
        }

        public void Clear()
        {
            Tickets.Clear();
            TicketsChanged?.Invoke();
        }

        /// <summary>从 AI 返回文本中提取期数</summary>
        public int? ExtractPeriod(string text)
        {
            foreach (var line in text.Split('\n', '\r'))
            {
                var clean = line.Trim();
                if (clean.StartsWith("期数"))
                {
                    var numStr = new string(clean.Where(char.IsDigit).ToArray());
                    // 双色球期号 2024XXX（7位），大乐透期号 24001（5位）
                    if (numStr.Length >= 5 && int.TryParse(numStr, out var n) && n > 1000)
                        return n;
                }
            }
            return null;
        }

        /// <summary>从 AI 返回文本中提取彩种类型</summary>
        public LotteryType? ExtractType(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (var line in text.Split('\n', '\r'))
            {
                var clean = line.Trim().ToLowerInvariant();
                if (clean.Contains("类型:双色球") || clean.Contains("类型:大乐透"))
                    return clean.Contains("大乐透") ? LotteryType.DLT : LotteryType.SSQ;
            }
            return null;
        }

        /// <summary>从文件名推断彩种类型</summary>
        public LotteryType? DetectTypeFromFileName(string filePath)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            var nameLower = name.ToLowerInvariant();
            // 中文命名：大乐透 / 双色球
            if (name.Contains("大乐透")) return LotteryType.DLT;
            if (name.Contains("双色球")) return LotteryType.SSQ;
            // 英文缩写：DLT_24001 / SSQ_2024001 等
            if (nameLower.StartsWith("dlt_") || nameLower.StartsWith("dlt.")
                || nameLower.Contains("_dlt_") || nameLower.Contains("_dlt.")
                || nameLower.EndsWith("_dlt"))
                return LotteryType.DLT;
            if (nameLower.StartsWith("ssq_") || nameLower.StartsWith("ssq.")
                || nameLower.Contains("_ssq_") || nameLower.Contains("_ssq.")
                || nameLower.EndsWith("_ssq"))
                return LotteryType.SSQ;
            return null;
        }

        /// <summary>从文件名提取期号（按 _ - . 分割，取 5-7 位纯数字段）</summary>
        public int? ExtractPeriodFromFileName(string filePath)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            // 按常见分隔符拆分，避免把多个数字拼成超长字符串
            var parts = name.Split('_', '-', ' ');
            foreach (var part in parts)
            {
                // 期号 5-7 位（排除 8 位日期如 20240627）
                if (part.Length is >= 5 and <= 7 && part.All(char.IsDigit)
                    && int.TryParse(part, out var n) && n > 1000)
                    return n;
            }
            // 回退：用正则提取 5-7 位连续数字，优先选最长的
            var matches = System.Text.RegularExpressions.Regex.Matches(name, @"\d{5,7}");
            foreach (var m in matches.Cast<System.Text.RegularExpressions.Match>()
                         .OrderByDescending(m => m.Length))
            {
                if (int.TryParse(m.Value, out var n) && n > 10000)
                    return n;
            }
            return null;
        }

        /// <summary>检查数据文件是否已存在（宽松匹配：同时检查完整期号和末5位短格式）</summary>
        public bool DataFileExists(LotteryType type, int? period)
        {
            if (!period.HasValue) return false;
            var dir = DataDir;
            var periodStr = period.Value.ToString();
            // 完整格式
            if (File.Exists(Path.Combine(dir, $"{type}_{periodStr}.txt"))) return true;
            // 取末5位作为短格式匹配（兼容 2026071 ↔ 26071）
            var tail = periodStr.Length > 5 ? periodStr[^5..] : periodStr;
            if (File.Exists(Path.Combine(dir, $"{type}_{tail}.txt"))) return true;
            // 短格式反向：26071 也尝试匹配 2026071（最近2年）
            if (periodStr.Length <= 5)
            {
                var curYear = DateTime.Now.Year % 100;
                foreach (var y in new[] { curYear, curYear - 1 })
                {
                    if (File.Exists(Path.Combine(dir, $"{type}_{y}{periodStr}.txt"))) return true;
                }
            }
            return false;
        }

        /// <summary>从 AI 返回的文本解析票行（使用当前 UI 选中彩种的范围）</summary>
        public List<Ticket> ParseTicketsFromText(string text) =>
            ParseTicketsFromText(text, _type);

        /// <summary>从 AI 返回的文本解析票行（使用指定彩种的范围）</summary>
        public List<Ticket> ParseTicketsFromText(string text, LotteryType type)
        {
            int redMax = type == LotteryType.DLT ? 35 : 33;
            int blueMax = type == LotteryType.DLT ? 12 : 16;

            var tickets = new List<Ticket>();
            text = text.Replace("```", "").Replace("红球", "").Replace("蓝球", "")
                       .Replace("红：", "").Replace("蓝：", "")
                       .Replace("：", ":").Replace("，", ",").Replace("；", ";");

            foreach (var line in text.Split('\n', '\r'))
            {
                var clean = line.Trim();
                if (string.IsNullOrWhiteSpace(clean)) continue;

                var semiIdx = clean.LastIndexOf(';');
                if (semiIdx < 0) continue;

                var redPart = clean[..semiIdx].Trim();
                var bluePart = clean[(semiIdx + 1)..].Trim();

                if (!TryExtractNumbersStrict(redPart, redMax, out var redNums, out var redError)
                    || !TryExtractNumbersStrict(bluePart, blueMax, out var blueNums, out var blueError))
                    continue;

                // 双色球：红球≥6 + 蓝球≥1；大乐透：前区≥5 + 后区≥2
                int minReds = type == LotteryType.DLT ? 5 : 6;
                int minBlues = type == LotteryType.DLT ? 2 : 1;
                if (redNums.Count >= minReds && blueNums.Count >= minBlues)
                    tickets.Add(new Ticket { Reds = redNums, Blues = blueNums });
            }
            return tickets;
        }

        /// <summary>检测数据中是否有超出当前彩票范围的号码</summary>
        public (bool hasError, string? msg) ValidateTicketRange(string filePath)
        {
            try
            {
                var text = File.ReadAllText(filePath);
                int maxRed = _type == LotteryType.DLT ? 35 : 33;
                int maxBlue = _type == LotteryType.DLT ? 12 : 16;
                int otherMaxRed = _type == LotteryType.DLT ? 33 : 35;
                int otherMaxBlue = _type == LotteryType.DLT ? 16 : 12;
                var otherName = _type == LotteryType.DLT ? "双色球" : "大乐透";

                bool hasOutOfRange = false;
                foreach (var line in text.Split('\n', '\r'))
                {
                    var clean = line.Trim();
                    if (string.IsNullOrWhiteSpace(clean)) continue;
                    var semiIdx = clean.LastIndexOf(';');
                    if (semiIdx < 0) continue;
                    var redPart = clean[..semiIdx].Trim();
                    var bluePart = clean[(semiIdx + 1)..].Trim();
                    var reds = ExtractNumbers(redPart, 99);
                    var blues = ExtractNumbers(bluePart, 99);
                    foreach (var r in reds) if (r > maxRed) hasOutOfRange = true;
                    foreach (var b in blues) if (b > maxBlue) hasOutOfRange = true;
                }
                if (hasOutOfRange)
                    return (true, $"⚠️ 数据包含超出{_type}范围的号码，可能为{otherName}数据，请切换对应类型再导入");
                return (false, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TicketStore.ValidateTicketRange] 验证号码范围异常: {ex.Message}");
                return (false, null);
            }
        }

        private List<int> ExtractNumbers(string s, int maxVal)
        {
            var nums = new List<int>();
            foreach (var part in s.Split(new[] { ' ', ',', '/', '|', '-', '、', '　' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var numStr = part.Trim().TrimStart('0');
                if (int.TryParse(numStr, out var n) && n >= 1 && n <= maxVal)
                    nums.Add(n);
            }
            return nums.Distinct().OrderBy(n => n).ToList();
        }

        private static bool TryExtractNumbersStrict(string s, int maxVal, out List<int> numbers, out string error)
        {
            numbers = new List<int>();
            error = "";
            foreach (var part in s.Split(new[] { ' ', ',', '/', '|', '-', ':', '、', '　' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = part.Trim();
                if (!int.TryParse(token, out var value) || value < 1 || value > maxVal)
                {
                    error = $"号码 {token} 超出 1-{maxVal} 范围或格式无效";
                    return false;
                }

                if (numbers.Contains(value))
                {
                    error = $"号码 {value:D2} 重复";
                    return false;
                }
                numbers.Add(value);
            }

            numbers.Sort();
            return numbers.Count > 0;
        }

        // ===== 数据本地持久化 =====
        public string DataDir => AppPaths.DataDirectory;

        private string PeriodFile => Path.Combine(DataDir, $"{_type}_{(_period.HasValue ? _period.Value.ToString() : "unknown")}.txt");

        /// <summary>保存当前票行按期号命名</summary>
        public void SaveToDataDir()
        {
            if (!_period.HasValue || Tickets.Count == 0) return;
            Directory.CreateDirectory(DataDir);
            var file = PeriodFile;
            var lines = Tickets.Select(t =>
                $"{string.Join(",", t.Reds.Select(n => n.ToString("D2")))};{string.Join(",", t.Blues.Select(n => n.ToString("D2")))}"
            );
            string tmp = file + ".tmp";
            File.WriteAllLines(tmp, lines, Encoding.UTF8);
            File.Move(tmp, file, overwrite: true);
        }

        /// <summary>直接保存分析结果到本地文件（不进缓存）</summary>
        public string SaveResultAsFile(List<Ticket> tickets, int? period, LotteryType? type = null)
        {
            if (tickets.Count == 0) return "";
            Directory.CreateDirectory(DataDir);
            var resultType = type ?? InferType(tickets);
            var periodStr = period.HasValue ? period.Value.ToString() : $"unknown_{DateTime.Now:yyyyMMdd_HHmmss}";
            var file = Path.Combine(DataDir, $"{resultType}_{periodStr}.txt");
            var lines = tickets.Select(t =>
                $"{string.Join(",", t.Reds.Select(n => n.ToString("D2")))};{string.Join(",", t.Blues.Select(n => n.ToString("D2")))}"
            );
            string tmp = file + ".tmp";
            File.WriteAllLines(tmp, lines, Encoding.UTF8);
            File.Move(tmp, file, overwrite: true);
            return Path.GetFileName(file);
        }

        /// <summary>从本地文件加载票行</summary>
        public bool LoadFromFile(string filePath)
        {
            try
            {
                var text = File.ReadAllText(filePath);
                // 提取期号（支持 SSQ_2024130 / DLT_24001 或纯数字格式）
                var fn = Path.GetFileNameWithoutExtension(filePath);
                var nameParts = fn.Split('_', StringSplitOptions.RemoveEmptyEntries);
                var periodToken = nameParts.Length switch
                {
                    1 => nameParts[0],
                    2 => nameParts[1],
                    _ => null
                };
                int? loadedPeriod = periodToken is { Length: 5 or 7 }
                    && int.TryParse(periodToken, out var p) && p > 1000 ? p : null;
                var loadedTickets = new List<Ticket>();

                foreach (var line in text.Split('\n', '\r'))
                {
                    var clean = line.Trim();
                    if (string.IsNullOrWhiteSpace(clean)) continue;
                    var tickets = ParseTicketsFromText(clean);
                    loadedTickets.AddRange(tickets);
                }
                if (loadedTickets.Count == 0) return false;
                Tickets.Clear();
                Tickets.AddRange(loadedTickets);
                Period = loadedPeriod;
                TicketsChanged?.Invoke();
                return Tickets.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TicketStore.LoadFromFile] 加载文件异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>列出当前类型的本地数据文件</summary>
        public List<string> ListDataFiles()
        {
            var files = new List<string>();
            var directories = new[] { DataDir, Path.Combine(AppPaths.LegacyRoot, "data") }
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in directories)
            {
                files.AddRange(Directory.GetFiles(directory, $"{_type}_*.txt"));
                files.AddRange(Directory.GetFiles(directory, $"{_type}_*.html"));
            }
            files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            files.Sort((a, b) => string.CompareOrdinal(b, a));
            return files;
        }

        /// <summary>自动加载最新期号的数据</summary>
        public int? LoadLatestData()
        {
            var files = ListDataFiles();
            foreach (var f in files)
            {
                if (LoadFromFile(f)) return Period;
            }
            return null;
        }

        // ===== API Key AES 加解密 =====
        /// <summary>从号码范围推断彩票类型</summary>
        internal static LotteryType InferType(List<Ticket> tickets)
        {
            var maxRed = tickets.SelectMany(t => t.Reds).DefaultIfEmpty(1).Max();
            var maxBlue = tickets.SelectMany(t => t.Blues).DefaultIfEmpty(1).Max();
            if (maxRed > 33) return LotteryType.DLT;
            if (maxBlue > 12) return LotteryType.SSQ;
            // 红蓝均在交集范围（红≤33且蓝≤12）：根据典型号码个数推断
            var redCount = tickets.FirstOrDefault()?.Reds.Count ?? 6;
            return redCount >= 6 ? LotteryType.SSQ : LotteryType.DLT;
        }

        private string ApiKeyPath => AppPaths.ApiKeyFile;
        private string LegacyApiKeyPath => AppPaths.LegacyApiKeyFile;

        // ===== B站 Cookie 加解密（独立存储）=====

        private string BiliCookiePath => AppPaths.BiliCookieFile;
        private string LegacyBiliCookiePath => AppPaths.LegacyBiliCookieFile;

        private string? _cachedBiliCookie;
        private string? _cachedSessdata;
        private string? _cachedBiliJct;
        private string? _cachedBiliBuvid3;

        /// <summary>保存B站Cookie</summary>
        public void SaveBiliCookie(string cookie, string? sessdata, string? biliJct, string? buvid3)
        {
            _cachedBiliCookie = cookie;
            _cachedSessdata = sessdata;
            _cachedBiliJct = biliJct;
            _cachedBiliBuvid3 = buvid3;
            var json = System.Text.Json.JsonSerializer.Serialize(new { cookie, sessdata, biliJct, buvid3 });
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var cipherBytes = ProtectWithMagic(plainBytes);
            string tmp = BiliCookiePath + ".tmp";
            File.WriteAllBytes(tmp, cipherBytes);
            File.Move(tmp, BiliCookiePath, overwrite: true);
        }

        /// <summary>获取B站完整Cookie字符串（优先用登录后的真实Cookie）</summary>
        public string? LoadBiliCookieString()
        {
            if (_cachedBiliCookie != null) return _cachedBiliCookie;
            var cookiePath = AppPaths.ExistingFile(BiliCookiePath, LegacyBiliCookiePath);
            if (!File.Exists(cookiePath)) return null;
            try
            {
                var cipherBytes = File.ReadAllBytes(cookiePath);
                
                string json;
                try
                {
                    var plainBytes = UnprotectWithMagic(cipherBytes);
                    json = Encoding.UTF8.GetString(plainBytes);
                }
                catch (CryptographicException)
                {
                    Debug.WriteLine("[TicketStore] BiliCookie DPAPI 解密失败 — 旧 AES 格式文件已被自动迁移");
                    return null;
                }
                catch (InvalidOperationException)
                {
                    Debug.WriteLine("[TicketStore] BiliCookie 数据损坏（魔数不匹配），请重新登录");
                    return null;
                }
                
                var obj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                _cachedBiliCookie = obj.TryGetProperty("cookie", out var c) ? c.GetString() : null;
                _cachedSessdata = obj.TryGetProperty("sessdata", out var s) ? s.GetString() : null;
                _cachedBiliJct = obj.TryGetProperty("biliJct", out var j) ? j.GetString() : null;
                _cachedBiliBuvid3 = obj.TryGetProperty("buvid3", out var b) ? b.GetString() : null;
                return _cachedBiliCookie;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TicketStore.LoadBiliCookieString] 加载Cookie异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>是否已登录B站</summary>
        public bool IsBiliLoggedIn => !string.IsNullOrEmpty(LoadBiliCookieString());
        public string? BiliSessdata => _cachedSessdata;
        public string? BiliBuvid3 => _cachedBiliBuvid3;

        private string? _cachedApiKey;
        private string? _cachedModel;
        private string? _cachedWsId;
        private string? _cachedApiHost;
        private string? _cachedBiliUid;
        private string? _cachedLlmKey;
        private string? _cachedLlmBaseUrl;
        private string? _cachedLlmModel;
        private bool _configLoaded;

        public string? LoadApiKey()
        {
            if (_cachedApiKey != null) return _cachedApiKey;
            LoadConfig();
            return _cachedApiKey;
        }

        /// <summary>获取用户配置的模型名称，未配置时返回 null</summary>
        public string? LoadModel()
        {
            LoadConfig();
            return _cachedModel;
        }

        public string? LoadWsId()
        {
            LoadConfig();
            return _cachedWsId;
        }
        /// <summary>加载 API Host 域名（不含路径），为空时使用默认 DashScope</summary>
        public string? LoadApiHost()
        {
            LoadConfig();
            return _cachedApiHost;
        }
        /// <summary>加载用户配置的 B站 UID</summary>
        public string? LoadBiliUid()
        {
            if (_cachedBiliUid != null) return _cachedBiliUid;
            LoadConfig();
            return _cachedBiliUid;
        }

        public void SaveApiKey(string plainText, string? model = null, string? wsId = null, string? apiHost = null)
        {
            _cachedApiKey = string.IsNullOrEmpty(plainText) ? null : plainText;
            _cachedModel = string.IsNullOrWhiteSpace(model) ? null : model;
            _cachedWsId = string.IsNullOrWhiteSpace(wsId) ? null : wsId;
            _cachedApiHost = string.IsNullOrWhiteSpace(apiHost) ? null : apiHost;
            SaveConfig(plainText, model, wsId, apiHost, _cachedBiliUid, _cachedLlmKey, _cachedLlmBaseUrl, _cachedLlmModel);
        }

        /// <summary>保存 B站 UID 默认值</summary>
        public void SaveBiliUid(string? uid)
        {
            _cachedBiliUid = string.IsNullOrWhiteSpace(uid) ? null : uid.Trim();
            SaveConfig(_cachedApiKey ?? "", _cachedModel, _cachedWsId, _cachedApiHost, _cachedBiliUid, _cachedLlmKey, _cachedLlmBaseUrl, _cachedLlmModel);
        }

        // ===== LLM 配置（NL→Code 用，独立于上方千问/多模态字段） =====

        public string? LoadLlmApiKey()
        {
            LoadConfig();
            return _cachedLlmKey;
        }

        public string? LoadLlmBaseUrl()
        {
            LoadConfig();
            return _cachedLlmBaseUrl;
        }

        public string? LoadLlmModel()
        {
            LoadConfig();
            return _cachedLlmModel;
        }

        public void SaveLlmApiKey(string? apiKey, string? baseUrl, string? model)
        {
            _cachedLlmKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            _cachedLlmBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl!.Trim();
            _cachedLlmModel = string.IsNullOrWhiteSpace(model) ? null : model!.Trim();
            SaveConfig(_cachedApiKey ?? "", _cachedModel, _cachedWsId, _cachedApiHost, _cachedBiliUid,
                _cachedLlmKey, _cachedLlmBaseUrl, _cachedLlmModel);
        }

        private void LoadConfig()
        {
            if (_configLoaded) return;
            _configLoaded = true;
            try
            {
                var configPath = AppPaths.ExistingFile(ApiKeyPath, LegacyApiKeyPath);
                if (!File.Exists(configPath)) { _cachedApiKey = null; _cachedModel = null; _cachedWsId = null; _cachedApiHost = null; _cachedLlmKey = null; _cachedLlmBaseUrl = null; _cachedLlmModel = null; return; }
                var cipherBytes = File.ReadAllBytes(configPath);
                if (cipherBytes.Length == 0) return;

                string json;
                try
                {
                    var plainBytes = UnprotectWithMagic(cipherBytes);
                    json = Encoding.UTF8.GetString(plainBytes);
                }
                catch (CryptographicException)
                {
                    Debug.WriteLine("[TicketStore] DPAPI 解密失败 — 可能是旧 AES 格式文件已被迁移，请重新配置");
                    return;
                }
                catch (InvalidOperationException)
                {
                    Debug.WriteLine("[TicketStore] 配置数据损坏（魔数不匹配），请重新配置");
                    return;
                }

                var obj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                _cachedApiKey = obj.TryGetProperty("key", out var k) ? k.GetString() : null;
                _cachedModel = obj.TryGetProperty("model", out var m) ? m.GetString() : null;
                _cachedWsId = obj.TryGetProperty("wsId", out var w) ? w.GetString() : null;
                _cachedApiHost = obj.TryGetProperty("apiHost", out var h) ? h.GetString() : null;
                _cachedBiliUid = obj.TryGetProperty("biliUid", out var bu) ? bu.GetString() : null;
                _cachedLlmKey = obj.TryGetProperty("llmKey", out var lk) ? lk.GetString() : null;
                _cachedLlmBaseUrl = obj.TryGetProperty("llmBaseUrl", out var lbu) ? lbu.GetString() : null;
                _cachedLlmModel = obj.TryGetProperty("llmModel", out var lm) ? lm.GetString() : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TicketStore.LoadConfig] 异常: {ex.Message}");
                _cachedApiKey = null; _cachedModel = null; _cachedWsId = null; _cachedApiHost = null;
                _cachedLlmKey = null; _cachedLlmBaseUrl = null; _cachedLlmModel = null;
            }
        }

        private void SaveConfig(string key, string? model, string? wsId, string? apiHost, string? biliUid,
            string? llmKey, string? llmBaseUrl, string? llmModel)
        {
            // 文件删除判断：所有字段都为空才删（含 LLM 字段，避免仅 LLM 配置时被误删）
            if (string.IsNullOrEmpty(key) && string.IsNullOrEmpty(biliUid) && string.IsNullOrEmpty(llmKey))
            {
                _cachedApiKey = null; _cachedModel = null; _cachedWsId = null; _cachedApiHost = null; _cachedBiliUid = null;
                _cachedLlmKey = null; _cachedLlmBaseUrl = null; _cachedLlmModel = null;
                try { File.Delete(ApiKeyPath); } catch (Exception ex) { Debug.WriteLine($"[TicketStore] 文件删除失败: {ex.Message}"); }
                return;
            }
            var json = System.Text.Json.JsonSerializer.Serialize(new { key, model, wsId, apiHost, biliUid, llmKey, llmBaseUrl, llmModel });
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var cipherBytes = ProtectWithMagic(plainBytes);
            string tmp = ApiKeyPath + ".tmp";
            File.WriteAllBytes(tmp, cipherBytes);
            File.Move(tmp, ApiKeyPath, overwrite: true);
            _cachedApiKey = key; _cachedModel = model; _cachedWsId = wsId; _cachedApiHost = apiHost; _cachedBiliUid = biliUid;
            _cachedLlmKey = llmKey; _cachedLlmBaseUrl = llmBaseUrl; _cachedLlmModel = llmModel;
            _configLoaded = true;
        }
    }
}
