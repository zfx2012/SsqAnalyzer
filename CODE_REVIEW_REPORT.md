# 代码审查报告：SsqAnalyzer WPF .NET 10 桌面应用

**审查范围**: 全部 18 个源码文件（Pages, Services, Controls, Models, Converters, App）
**审查日期**: 2026-06-28
**审查师**: Cody · 代码审查师

---

## 严重问题

### 🔴 #1 - API Key 通过命令行参数传递给 curl.exe [安全-凭证泄露]

| 字段 | 值 |
|------|------|
| 文件 | `Services/AiAnalysisService.cs` |
| 行号 | 43 |
| 类别 | **安全** — 凭证泄露 |
| 严重度 | **🔴 严重** |

**问题描述**: API Key 直接拼接到 curl.exe 的命令行参数中：
```csharp
Arguments = $"-sS ... -H \"Authorization: Bearer {key}\" ..."
```
在 Windows 上，命令行参数可通过 WMI (`SELECT CommandLine FROM Win32_Process`)、Process Explorer、Event Tracing 等机制被**同一机器上的所有用户/进程**读取。这会暴露 DashScope API Key。

**修复建议**: 改用 `HttpClient` 直接发送 POST 请求（而非 spawn curl.exe），将 Authorization header 添加到 `HttpRequestMessage.Headers` 中，而非命令行参数。

```csharp
using var client = new HttpClient();
var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
var response = await client.SendAsync(request, ct);
return await response.Content.ReadAsStringAsync(ct);
```

---

### 🔴 #2 - B站 Cookie 通过 DPAPI 加密存储但缺少完整性校验 [安全-存储]

| 字段 | 值 |
|------|------|
| 文件 | `Services/TicketStore.cs` |
| 行号 | 352-353, 465 |
| 类别 | **安全** — 加密存储 |
| 严重度 | **🔴 严重** |

**问题描述**: `ProtectedData.Protect` 未使用 `optionalEntropy` 参数（传 `null`），且未附加 HMAC 校验。DPAPI 输出的是标准格式密文，攻击者拿到 `api_key` 或 `bili_cookie` 文件后无法解密（需要同一 Windows 用户），但**文件可被替换/回滚**：如果攻击者备份了旧版本的加密文件，可以覆盖当前文件实现"回滚攻击"。

**修复建议**: 
1. 添加应用级别的熵值：`ProtectedData.Protect(plainBytes, additionalEntropy, ...)` 其中 `additionalEntropy` 取自固定应用密钥。
2. 考虑在加密数据末尾附加版本号，便于未来迁移。

---

### 🔴 #3 - HTML 导出无编码转义 → XSS 风险 [安全-XSS]

| 字段 | 值 |
|------|------|
| 文件 | `Pages/TicketsPage.xaml.cs` |
| 行号 | 321-378 |
| 类别 | **安全** — XSS |
| 严重度 | **🔴 严重** |

**问题描述**: `ExportHtml_Click` 直接在 HTML 字符串拼接中嵌入用户数据：
- `_store.Period` (nullable int)拼接为 `$"第 {_store.Period} 期"`
- 文件名 `$"复式票统计_{expType}_{_store.Period}.html"` 
- 所有号码通过 `sb.Append($"...{n:D2}...")` 拼接
虽然彩票号码是整数不易注入，但 `_store.Period` 来源于文件解析（`ExtractPeriodFromFileName`），理论上可以被恶意构造。若用户打开恶意制作的 `.txt` 数据文件并导出 HTML，可能触发脚本执行。

**修复建议**: 对所有动态拼接到 HTML 中的字符串值使用 `System.Net.WebUtility.HtmlEncode()`：

```csharp
sb.AppendLine($"<h2>🔥 复式票统计 {HtmlEncode(typeStr)}</h2>");
sb.AppendLine($"<p class=\"period\">{HtmlEncode(periodStr)}</p>");
```

---

## 高严重度问题

### 🟠 #4 - API Key 内存中明文缓存 [安全-内存泄露]

| 字段 | 值 |
|------|------|
| 文件 | `Services/TicketStore.cs` |
| 行号 | 396-468 |
| 类别 | **安全** — 内存敏感数据 |
| 严重度 | **🟠 高** |

**问题描述**: `_cachedApiKey` 是 `string?` 类型，以不可变字符串形式存储在托管堆中。.NET 字符串在 GC 回收前**无法被擦除**（string 是不可变的，且 `= null` 仅释放引用）。攻击者若在进程崩溃时获取内存转储 (dump)，可从中提取 API Key。同样问题适用于 `_cachedBiliCookie`。

**修复建议**: 
1. 使用 `System.Security.SecureString` 或 `System.Text.Encoding.UTF8.GetBytes()` + `ZeroMemory` 模式存储。
2. 最小化 key 在内存中的生存时间，使用后立即用 `Array.Clear()` 擦除字节数组：
```csharp
// 使用完 key 后
Array.Clear(keyBytes, 0, keyBytes.Length);
_cachedApiKey = null;
```

---

### 🟠 #5 - 基础号码常量硬编码，不支持 DLT 动态布局 [正确性]

| 字段 | 值 |
|------|------|
| 文件 | `Controls/MatrixGrid.cs` |
| 行号 | 17-18, 68-73 |
| 类别 | **正确性** — 硬编码常量 |
| 严重度 | **🟠 高** |

**问题描述**: 
```csharp
private const int RedCount = 33;
private const int BlueCount = 16;
```
`MatrixGrid` 使用硬编码 SSQ 的 33 红 + 16 蓝布局。当程序切换到 DLT 模式（35 红 + 12 蓝）时，`RedOrderNormal` 和 `BlueOrderNormal` 数组仍是 SSQ 的固定大小。虽然当前项目只在 SSQ 走势图中使用 MatrixGrid，但这是一颗定时炸弹——任何人后续扩展时都会掉进这个坑。

同样 `TrendPage` 的 `Chk012` 和对角线链预测也只针对 SSQ 33 红做了优化。

**修复建议**: 将红/蓝球数量作为构造参数或属性传入 MatrixGrid，根据配置动态生成排列数组。

---

### 🟠 #6 - TicketStore._type 全局可变状态导致竞态 [正确性-并发]

| 字段 | 值 |
|------|------|
| 文件 | `Services/TicketStore.cs` |
| 行号 | 24-28 |
| 类别 | **正确性** — 竞态条件 |
| 严重度 | **🟠 高** |

**问题描述**: `CurrentType` setter 是裸属性访问器，无锁保护：
```csharp
set { if (_type != value) { _type = value; Tickets.Clear(); _period = null; ... } }
```
在异步操作（如 `CompoundStatsPage.AnalyzeVideos` 耗时分析中）进行到一半时，若用户切换到另一个页面并切换彩种，会导致 `Tickets.Clear()` 被并发调用，正在分析中的页面引用到空列表或错误类型。`Tickets` 列表本身是 `List<Ticket>`，非线程安全。

**修复建议**: 
```csharp
private readonly object _typeLock = new();
public LotteryType CurrentType
{
    get { lock (_typeLock) return _type; }
    set 
    { 
        lock (_typeLock)
        {
            if (_type == value) return;
            _type = value; 
            Tickets.Clear(); 
            _period = null; 
        }
        TicketsChanged?.Invoke();
        PeriodChanged?.Invoke();
    }
}
```

---

### 🟠 #7 - AI API 请求 curl 进程泄露风险 [安全-正确性]

| 字段 | 值 |
|------|------|
| 文件 | `Services/AiAnalysisService.cs` |
| 行号 | 85-94 |
| 类别 | **正确性** — 资源管理 |
| 严重度 | **🟠 高** |

**问题描述**: `catch (OperationCanceledException)` 和 `catch { }` 两个块包含完全相同的 `proc.Kill()` 代码。此外，在超时分支（行 68）直接 `throw` 后未等待进程完全退出。若 `proc.Kill()` 抛出异常（无权限杀死子进程），外层 catch 块会再次尝试 Kill，但**不会释放 proc 的句柄**（无 `proc.Dispose()` 或 `using`）。

**修复建议**: 统一 Kill + Dispose 到 finally 块：
```csharp
finally
{
    if (proc is { HasExited: false })
    {
        try { proc.Kill(entireProcessTree: true); proc.WaitForExit(2000); } catch { }
    }
    proc?.Dispose();
}
```

---

### 🟠 #8 - ValidateTicketRange 先读全文件再逐行解析，大文件 OOM [性能]

| 字段 | 值 |
|------|------|
| 文件 | `Services/TicketStore.cs` |
| 行号 | 185-216 |
| 类别 | **性能** — 内存 |
| 严重度 | **🟠 高** |

**问题描述**: `File.ReadAllText(filePath)` 将整个文件读入单个字符串（可能数 MB），然后又用 `Split('\n', '\r')` 生成另一个大数组。对于包含数千条注单的文件，内存占用翻倍。

**修复建议**: 使用 `File.ReadLines()` 延迟逐行读取：
```csharp
foreach (var line in File.ReadLines(filePath))
{
    // 逐行处理...
}
```

---

## 中严重度问题

### 🟡 #9 - 重复的 UI 构建代码 [可维护性]

| 字段 | 值 |
|------|------|
| 文件 | `Pages/TicketsPage.xaml.cs` |
| 行号 | 143-265 和 403-461 |
| 类别 | **可维护性** — 代码重复 |
| 严重度 | **🟡 中** |

**问题描述**: `BuildHeader` + `BuildTicketRows` + `BuildStatsRow` 的完整表格构建逻辑在 `RebuildTable`（常规显示）和 `ExportImage_Click`（导出图片）中几乎**完全复制粘贴**了一遍。共约 200 行重复代码。任何对表格布局的修改（如添加列、调整格式）都需要在两个地方同步修改，极易遗漏。

**修复建议**: 提取公共的 `BuildTableContent(Grid grid, int startRow, int endIdx, bool includeActions)` 方法，在两个场景中复用。

---

### 🟡 #10 - DPAPI 解密失败后静默返回 null，用户无感知 [可用性]

| 字段 | 值 |
|------|------|
| 文件 | `Services/TicketStore.cs` |
| 行号 | 371-375, 436-439 |
| 类别 | **正确性** — 错误处理 |
| 严重度 | **🟡 中** |

**问题描述**: 当 DPAPI 解密抛出 `CryptographicException` 时，代码仅通过 `Debug.WriteLine` 输出一条日志，然后默默地返回 `null`。普通用户运行程序后会发现 API Key "丢失了"但不知道为什么。旧 AES 格式文件被"静默迁移"（注释如此），但用户没有被提示重新输入 Key。

**修复建议**: 添加一个通知机制（如设置一个 `public bool NeedsReconfiguration` 标志），在 `SettingsPage` 加载时检测并弹出提示：
```csharp
catch (CryptographicException)
{
    Debug.WriteLine("[TicketStore] DPAPI 解密失败");
    NeedsReconfiguration = true;  // 设置页面检查此标志
    return null;
}
```

---

### 🟡 #11 - B站 Cookie 轮询无退出上限 [安全-可用性]

| 字段 | 值 |
|------|------|
| 文件 | `Pages/BiliLoginDialog.cs` |
| 行号 | 131-180 |
| 类别 | **正确性** — 无限循环 |
| 严重度 | **🟡 中** |

**问题描述**: `PollLogin()` 的 `while (!_closed && !_loggedIn)` 循环没有超时上限。如果用户不手动取消，线程会无限轮询 B站 API（每 1.5 秒一次）。此外，`_http` 是 `static` 的静态字段，但 `BiliLoginDialog` 可能创建多个实例（当前代码未限制），会导致 HttpClient 实例泄露。

**修复建议**: 添加最大轮询时间（如 120 秒）：
```csharp
var startTime = DateTime.UtcNow;
while (!_closed && !_loggedIn && (DateTime.UtcNow - startTime).TotalSeconds < 120)
{
    // ... 轮询逻辑
}
if (!_loggedIn) 
    _statusText.Text = "登录超时，请重试";
```

---

### 🟡 #12 - ExtractType 对全角冒号和空格不鲁棒 [正确性]

| 字段 | 值 |
|------|------|
| 文件 | `Services/TicketStore.cs` |
| 行号 | 93-94 |
| 类别 | **正确性** — AI 响应解析 |
| 严重度 | **🟡 中** |

**问题描述**: 
```csharp
if (clean.Contains("类型:双色球") || clean.Contains("类型:大乐透"))
```
该匹配仅支持半角冒号 `:`。AI 模型（Qwen）的回复可能包含全角冒号 `：`、半角 `:`、或无冒号空格格式 `类型: 双色球`。一旦 AI 使用了不同格式，类型检测会失败，导致 `ParseTicketsFromText` 使用错误的号码范围（默认 SSQ 但数据是 DLT）。

**修复建议**: 使用更宽松的正则匹配：
```csharp
private static readonly Regex TypePattern = new(@"类型\s*[:：]\s*(双色球|大乐透)", RegexOptions.Compiled);
var match = TypePattern.Match(clean);
if (match.Success) return match.Groups[1].Value == "大乐透" ? LotteryType.DLT : LotteryType.SSQ;
```

---

### 🟡 #13 - FullRender 创建大量 UIElement 导致 UI 线程压力 [性能]

| 字段 | 值 |
|------|------|
| 文件 | `Controls/MatrixGrid.cs` |
| 行号 | 204-412 |
| 类别 | **性能** — UI 渲染 |
| 严重度 | **🟡 中** |

**问题描述**: `FullRender()` 为每个数据单元格创建 `Border` + `TextBlock`（或 `Ellipse` + `TextBlock`）。对于 50 期数据：表头(1+33+1+16)+数据行(50×(1+1+33+1+16))+3行预选行×(33+16) ≈ 2700+ 个 UIElement。每次切换期数或过滤条件都重建所有元素。虽然使用了 `BeginInit/EndInit`，但大量子元素仍会让布局测量/排列阶段变慢。

**修复建议**: 
1. 使用 `VirtualizingStackPanel` 或仅渲染可见行（只创建屏幕可视区域的行，滚动时回收/重用）。
2. 对于海量单元格，考虑使用 `DrawingVisual` 或 `WriteableBitmap` 直接渲染到像素。

---

### 🟡 #14 - HttpClient 静态/实例混合模式 [可维护性]

| 字段 | 值 |
|------|------|
| 文件 | `Services/BiliService.cs:13`, `Services/DataService.cs:27`, `Pages/BiliLoginDialog.cs:22` |
| 行号 | 多处 |
| 类别 | **可维护性** — 资源管理 |
| 严重度 | **🟡 中** |

**问题描述**: 项目中 `HttpClient` 的创建模式不一致：
- `BiliService._http`: 实例字段（10s 超时），实例非 `IDisposable`
- `DataService._http`: 实例字段（8s 超时），同上
- `DownloadService._dlHttp`: 实例字段（30min 超时）
- `BiliLoginDialog._http`: **静态字段**（10s 超时）
- `AnalysisService` 未使用 HttpClient，而是用 curl.exe

根据 .NET 官方建议，HttpClient 应作为**单例**或由 `IHttpClientFactory` 管理。当前模式混合使用，且 `BiliLoginDialog._http` 是静态的但不被 IHttpClientFactory 管理，可能导致 socket 耗尽。

**修复建议**: 通过 DI 注入 `IHttpClientFactory`，统一管理 HttpClient 生命周期：
```csharp
// 在 App.xaml.cs 中注册
services.AddHttpClient<BiliService>(client => { client.Timeout = TimeSpan.FromSeconds(10); });
services.AddHttpClient<DataService>(client => { client.Timeout = TimeSpan.FromSeconds(8); });
```

---

### 🟡 #15 - 视频文件名扩展名与 TitleWithExt 不匹配 [正确性]

| 字段 | 值 |
|------|------|
| 文件 | `Pages/CompoundStatsPage.xaml.cs` |
| 行号 | 35-36 |
| 类别 | **正确性** |
| 严重度 | **🟡 中** |

**问题描述**: 
```csharp
public string TitleWithExt => HasError ? Title : $"{Title}.mp4";
```
始终追加 `.mp4` 扩展名，但 yt-dlp 可能下载为 `.flv`、`.mkv`（取决于视频编码和格式选择）。下载完成后通过 `DownloadVideo_Click` 的 `output` 参数指定了 `.mp4`，所以目前是 mp4。但如果用户手动复制了其他格式的视频到 video 目录，选择该文件时显示的名称与实际文件不匹配。

**修复建议**: 此属性应检查实际文件扩展名：从文件路径中获取，而不是硬编码 `.mp4`。

---

## 低严重度问题

### 🟢 #16 - 多次出现的魔法数字 [可维护性]

| 字段 | 值 |
|------|------|
| 文件 | `Controls/MatrixGrid.cs`, `Services/AiAnalysisService.cs:11`, `Pages/CompoundStatsPage.xaml.cs:133` |
| 行号 | 多处 |
| 类别 | **可维护性** |
| 严重度 | **🟢 低** |

**问题描述**:
- `MatrixGrid.cs:20` - `ColWeekDay = 1` 无注释说明为什么是 1
- `AiAnalysisService.cs:11` - `const int maxRetries = 2` 硬编码重试次数
- `CompoundStatsPage.xaml.cs:133` - `const int MaxTickets = 50` 无业务说明
- `MatrixGrid.cs:372` - 冷号阈值 `>= 10` 红球、`>= 16` 蓝球无命名常量
- `DataService.cs:27` - `TimeSpan.FromSeconds(8)` 超时硬编码
- `BiliService.cs:67` - `800 - elapsed` 请求间隔 800ms 硬编码

**修复建议**: 提取为具名常量/配置项，为关键阈值添加注释说明业务理由。

---

### 🟢 #17 - CompoundStatsPage.LoadData() 空方法 [可维护性]

| 字段 | 值 |
|------|------|
| 文件 | `Pages/CompoundStatsPage.xaml.cs` |
| 行号 | 659 |
| 类别 | **可维护性** |
| 严重度 | **🟢 低** |

**问题描述**: `LoadData()` 方法体为空，但在 `MainWindow.xaml.cs:167` 中被调用。
```csharp
public void LoadData() { }
```
这个桩方法让维护者困惑：到底是故意留空（因为已经通过 `Loaded` 事件初始化了）还是忘记实现了？

**修复建议**: 添加注释说明，或移除 MainWindow 中的调用：
```csharp
/// <summary>
/// 数据初始化由 XAML Loaded 事件自动完成，此方法为符合导航接口保持空实现
/// </summary>
public void LoadData() { /* 由 Loaded 事件驱动 */ }
```

---

### 🟢 #18 - UiColors.StatsCellBg 逻辑与 TicketsPage.StatsCellBg 不一致 [正确性]

| 字段 | 值 |
|------|------|
| 文件 | `UiColors.cs:47-57` vs `Pages/TicketsPage.xaml.cs:257-265` |
| 行号 | 对比两处 |
| 类别 | **可维护性** — 重复逻辑 |
| 严重度 | **🟢 低** |

**问题描述**: `UiColors.StatsCellBg` 和 `TicketsPage.StatsCellBg` 都实现了类似的热度颜色算法，但计算方式不同：
- UiColors: `ratio = freq / avg`, `r = 250 - (int)(ratio * 200)`
- TicketsPage: `ratio = (freq - 1) / Math.Max(avg - 1, 1)`, `r = 255 - (int)(ratio * 135)`

计算结果不同，但都被称为"热度颜色"。维护者无法确定哪个是"正确"的。

**修复建议**: 移除 `TicketsPage.StatsCellBg`，统一使用 `UiColors.StatsCellBg`。

---

### 🟢 #19 - 期号解析可能的歧义 [正确性]

| 字段 | 值 |
|------|------|
| 文件 | `Services/TicketStore.cs` |
| 行号 | 120-137 |
| 类别 | **正确性** |
| 严重度 | **🟢 低** |

**问题描述**: `ExtractPeriodFromFileName` 使用 `>= 5 and <= 7` 来匹配期号，但文件名中可能包含多位数字（如年份 `2026`、日期 `0628`）。当前逻辑靠排除 8 位以上数字来避免匹配日期，但 5-7 位数字在文件名中仍可能有多个匹配——代码只会返回第一个匹配到的。

例如文件名 `SSQ_2026072_备份.txt` 会被拆为 `["SSQ", "2026072", "备份"]`，正确匹配 `2026072`。但若文件名是 `2026_072.txt`，第一个匹配到的会是 `2026`（年份）而非 `072`（期号）。

**修复建议**: 优先匹配已知前缀后的数字段（如 `_` 分隔的最后一个数字段），而非遍历所有分段。

---

### 🟢 #20 - crash.log 显示 XAML 加载时 NullReferenceException [稳定性]

| 字段 | 值 |
|------|------|
| 文件 | `bin/Debug/net10.0-windows/crash.log` |
| 行号 | 1-16 |
| 类别 | **稳定性** |
| 严重度 | **🟢 低** |

**问题描述**: crash.log 记录了启动时的 `NullReferenceException`：
```
System.NullReferenceException: Object reference not set to an instance of an object.
   at System.DefaultBinder.BindToMethod(...)
   at MS.Internal.Xaml.Runtime.DynamicMethodRuntime.CreateInstanceWithCtor(Type type, Object[] args)
```
这表明某个 XAML 页面的构造函数在 DI 服务解析时失败了（可能是某个页面被 XAML 创建时不带参数的默认构造函数调用 `App.Services.GetRequiredService<>()` 时 DI 容器尚未完全初始化）。

**修复建议**: 确保 `App.OnStartup` 中 `Services` 的初始化在 XAML 加载之前完成。检查 `MainWindow` XAML 中声明的所有子页面的默认构造函数路径。

---

### 🟢 #21 - 未使用的 using 指令 [可维护性]

| 字段 | 值 |
|------|------|
| 文件 | 多处 |
| 行号 | 各处 |
| 类别 | **可维护性** |
| 严重度 | **🟢 低** |

**问题描述**: 以下 using 未使用：
- `Pages/CompoundStatsPage.xaml.cs:13` — `using System.Linq;`
- `Pages/TicketsPage.xaml.cs` — `using System.Windows.Media.Imaging;`（仅在 ExportImage 中使用局部方法，未在全局引用）

**修复建议**: 运行 `dotnet format` 清理未使用的引用。

---

### 🟢 #22 - InferType 方法从 Ticket 推断彩种逻辑不严谨 [正确性]

| 字段 | 值 |
|------|------|
| 文件 | `Services/TicketStore.cs` |
| 行号 | 318-327 |
| 类别 | **正确性** |
| 严重度 | **🟢 低** |

**问题描述**: 
```csharp
internal static LotteryType InferType(List<Ticket> tickets)
{
    var maxRed = tickets.SelectMany(t => t.Reds).DefaultIfEmpty(1).Max();
    var maxBlue = tickets.SelectMany(t => t.Blues).DefaultIfEmpty(1).Max();
    if (maxRed > 33) return LotteryType.DLT;
    if (maxBlue > 12) return LotteryType.SSQ;
    var redCount = tickets.FirstOrDefault()?.Reds.Count ?? 6;
    return redCount >= 6 ? LotteryType.SSQ : LotteryType.DLT;
}
```
当红球最大 <= 33 且蓝球最大 <= 12 的交集范围时，用 `redCount >= 6` 决定——双色球红球 6 个，DLT 前区 5 个。但 DLT 也可能有 6 个以上的前区号码（复式票）。这个启发式在 DLT 复式票场景下会误判为 SSQ。

**修复建议**: 文件名检测优先于范围推断，或添加更多启发信息（如号码个数分布统计）。

---

## 做得好的地方

1. **DPAPI 迁移**: 从 AES 自实现加密迁移到 Windows DPAPI (`ProtectedData`)，加密安全性大幅提升。旧格式迁移代码优雅处理了兼容性。

2. **矩阵渲染增量更新**: `MatrixGrid.Render()` 通过 `BeginInit/EndInit` 批量更新布局，并在数据相同且配置未变时只走 `UpdateDisplay()` 快速路径（仅更新缓存列表中的单元格），避免了全量重建的性能开销。

3. **Brush 冻结**: `UiColors` 中所有 Brush 都调用了 `Freeze()`，`MatrixGrid` 的自定义 Brush 也通过 `FreezeBrush` 冻结，避免了 WPF 在渲染线程的自动克隆开销。

4. **重试 + 取消模式**: `AiAnalysisService.CurlPostAsync` 实现了指数退避重试（1s, 2s）和 `CancellationToken` 链路（用户取消 + 超时合并），模式清晰。

5. **B站 WBI 签名**: `BiliService` 实现了正确的 w_rid 签名算法（mixin key 加密），符合 B站 API 安全要求。

6. **SHA256 校验**: `DownloadService.EnsureYtDlp()` 在下载 yt-dlp.exe 后校验 SHA256 哈希，防止中间人攻击替换二进制文件。

7. **DI 容器**: 使用 `Microsoft.Extensions.DependencyInjection` 管理服务生命周期，接口/实现分离（`ITicketStore`/`TicketStore`），为测试奠定了基础。

8. **全局异常处理**: `App.xaml.cs` 注册了三个级别的异常处理（UI、AppDomain、Task），确保未捕获异常不会导致静默崩溃。

9. **API Key 掩码展示**: `ApiConfigDialog.DisplayKey()` 只显示首尾 4 位，中间用 `*` 掩盖，符合安全 UI 最佳实践。

---

## 结论

**审查结果**: Request Changes

**核心问题**: 
- **安全风险**: API Key 通过 curl 命令行参数传递（#1）是最严重的问题，攻击者可通过本机进程枚举获取 Key。HTML 导出无编码（#3）在特定场景下可导致 XSS。
- **正确性**: MatrixGrid 硬编码 SSQ 常量（#5）、TicketStore 全局可变状态（#6）、AI 响应解析的 Unicode 兼容性（#12）都需要修复。
- **可维护性**: TicketsPage 中约 200 行重复 UI 代码（#9）是长期维护的最大债务。

**优先级建议**:
1. **立即修复**: #1（curl 传 Key）、#3（XSS）、#7（进程泄漏）
2. **本周修复**: #5（硬编码常量）、#6（竞态）、#12（AI 解析）、#10（DPAPI 静默失败）
3. **下个迭代**: #9（重复代码重构）、#14（HttpClient 管理）、#4（内存明文 Key）
4. **持续改进**: 其余低优先级建议

---

*审查报告结束*
