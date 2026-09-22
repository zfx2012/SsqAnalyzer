# SsqAnalyzer 文档质量评估报告

**评估人：** 多库（Docu）· 技术文档师  
**评估日期：** 2026-06-28  
**项目：** SsqAnalyzer — C# WPF .NET 10 双色球/大乐透彩票分析工具  
**代码库路径：** `SsqAnalyzer/`  

---

## 目录

1. [总体评分](#1-总体评分)
2. [代码内文档质量（XML 注释）](#2-代码内文档质量xml-注释)
3. [项目文档](#3-项目文档)
4. [API 文档](#4-api-文档)
5. [Runbook / 操作手册](#5-runbook--操作手册)
6. [改进建议（优先级排序）](#6-改进建议优先级排序)
7. [附录：模板与工具推荐](#7-附录模板与工具推荐)

---

## 1. 总体评分

| 评估维度 | 权重 | 得分 | 状态 |
|----------|------|------|------|
| **XML 注释覆盖率** | 25% | 3/10 | 🔴 接口零注释，Service 类覆盖率低 |
| **XML 注释质量** | 15% | 4/10 | 🟡 仅描述了"是什么"，缺少"为什么"和参数说明 |
| **项目文档** | 20% | 4/10 | 🟡 有架构/代码审查/事故报告，但无 README/CHANGELOG/CONTRIBUTING |
| **API 文档** | 20% | 2/10 | 🔴 接口无文档，配置无说明，API 无使用指南 |
| **Runbook / 操作手册** | 20% | 1/10 | 🔴 无开发环境搭建、部署、测试、故障排查文档 |
| **总分** | **100%** | **2.8/10** | 🔴 **需要系统性改进** |

---

## 2. 代码内文档质量（XML 注释）

### 2.1 XML 注释覆盖率（逐文件统计）

按文件评估 XML 注释覆盖情况（仅统计**开发源代码**，排除 `obj/` 和 `bin/`）：

| 文件 | 行数 | XML 注释数 | 覆盖率评估 | 说明 |
|------|------|-----------|-----------|------|
| **Services/IDataService.cs** | 19 | **0** | ❌ 0% | 接口无任何注释，包括方法和事件 |
| **Services/ITicketStore.cs** | 45 | **0** | ❌ 0% | 接口无任何注释，45 行接口完全裸奔 |
| **Services/IAnalysisService.cs** | 21 | **0** | ❌ 0% | 接口无任何注释 |
| **Services/AiAnalysisService.cs** | 111 | **2** | ❌ ~5% | 只有 `CurlPostAsync` 有一行摘要 |
| **Services/BiliService.cs** | 146 | **0** | ❌ 0% | 整个 B 站服务类无 XML 注释 |
| **Services/DownloadService.cs** | 119 | **0** | ❌ 0% | 整个下载服务类无 XML 注释 |
| **Services/TicketStore.cs** | ~470 | **~15** | 🟡 ~15% | 部分公共方法有注释，但类本身无注释 |
| **Services/DataService.cs** | 272 | **~16** | 🟡 ~30% | 覆盖率最好的 Service，但缺少 `<param>`/`<returns>` |
| **Services/AnalysisService.cs** | 303 | **~17** | 🟡 ~35% | 所有方法有摘要，但无参数描述 |
| **UiColors.cs** | 58 | **1** | 🟡 ~20% | 类有注释，但字段只有 `//` 行内注释 |
| **BallControl.cs** | 72 | **1** | ❌ ~5% | 类有注释，附加属性/方法无注释 |
| **Converters.cs** | 55 | **3** | 🟡 ~30% | 每个转换器类有摘要 |
| **AnalysisModels.cs** | 99 | **9** | 🟡 ~60% | 覆盖率最高的文件，每个 model 类都有注释 |
| **App.xaml.cs** | 70 | **0** | ❌ 0% | 无 XML 注释 |
| **MainWindow.xaml.cs** | 219 | **0** | ❌ 0% | 无 XML 注释 |
| **Pages/*.xaml.cs** (9 个文件) | ~2500+ | **~0** | ❌ ~0% | 所有 Page 均无 XML 注释 |

**关键发现：**

1. **三个关键接口零注释** — `IDataService`、`ITicketStore`、`IAnalysisService` 是项目核心抽象，但完全没有任何 XML 注释。它们定义了所有页面与服务之间的契约，却没有任何文档说明每个方法的用途、参数含义、返回值、异常情况。

2. **三个 Service 类无注释** — `AiAnalysisService`、`BiliService`、`DownloadService` 没有类级别注释，调用者无法快速了解这些类的作用。

3. **所有 Page 文件零注释** — 9 个 Page 文件（~2500+ 行 code-behind）没有任何 XML 注释。作为 WPF 应用的主要逻辑载体，代码可读性严重依赖方法命名。

4. **无 `<param>` / `<returns>` / `<exception>` 标签** — 项目中所有 XML 注释都是 `<summary>一句话描述</summary>` 的简化形式。没有参数说明、返回值说明、异常说明。

### 2.2 注释质量评估：是"为什么"还是"是什么"

**"是什么"（存在，但不够）：**

```xml
/// <summary>获取杀号推荐</summary>
/// <summary>红球组合频率 TOP 20</summary>
/// <summary>计算和值数据</summary>
```

这些注释只告诉调用者**方法做什么**，不告诉**为什么这样做**或**调用者需要注意什么**。

**缺少的"为什么"示例：**

```csharp
// 当前代码中最好的"为什么"注释在 DataService.cs:210：
// 早期数据(2003-2004)可能不包含销售/奖池字段
```

但类似这样解释设计决策、历史原因、边界条件的注释太少。以下位置明显缺少"为什么"注释：

| 位置 | 缺少的上下文 |
|------|-------------|
| `AiAnalysisService` 用 curl.exe 而非 HttpClient | 为什么选择进程调用方式？ |
| `BiliService` 的 `800 - elapsed` 请求间隔 | 为什么是 800ms？B 站 API 限频策略？ |
| `DataService.DataUrl` 硬编码 | 为什么选这个数据源？格式是什么？ |
| `MatrixGrid` 硬编码 33 红 + 16 蓝 | 为什么不能扩展 DLT？ |

### 2.3 复杂逻辑的注释情况

**注释良好的地方：**

- `DataService.cs:210` — "早期数据(2003-2004)可能不包含销售/奖池字段" ✅ 好的"为什么"注释
- `TicketStore.cs:78` — "双色球期号 2024XXX（7 位），大乐透期号 24001（5 位）" ✅ 解释了业务规则
- `AiAnalysisService.cs:30` — "不重试的条件：403 权限错误、API Key 未配置" ✅ 好的决策说明

**注释缺失的复杂逻辑：**

- `ParseLines` 方法（DataService.cs:185-243）— 格式解析逻辑复杂，无字段位置说明
- `CurlPostOnce` 方法（AiAnalysisService.cs:38-95）— 三合一超时/取消/重试，无流程图说明
- `WBI 签名` 算法（BiliService.cs:36-59）— 复杂的密钥抽取逻辑，无 MD 图说明
- `WndProc` WM_GETMINMAXINFO 处理（MainWindow.xaml.cs:57-76）— 多显示器兼容，无注释

---

## 3. 项目文档

### 3.1 README / CONTRIBUTING / CHANGELOG

| 文档 | 存在 | 说明 |
|------|------|------|
| **README.md** | ❌ **缺失** | 无任何项目介绍、快速开始、功能列表 |
| **CONTRIBUTING.md** | ❌ **缺失** | 无贡献指南、编码规范、PR 流程 |
| **CHANGELOG.md** | ❌ **缺失** | 无版本记录 |
| **LICENSE** | ❌ **缺失** | 无许可证文件 |

### 3.2 存在的外部文档（3 个 .md 文件）

项目有 3 个 Markdown 文件，它们是工程保障团队其他成员产出的评估报告，不是项目本身的文档：

| 文件 | 来源 | 质量评分 | 评价 |
|------|------|----------|------|
| **ARCHITECTURE_ASSESSMENT.md** | 架构师 | ⭐⭐⭐⭐⭐ 9/10 | 结构完整，含 ADR、评分、路线图、ASCII 组件图 |
| **CODE_REVIEW_REPORT.md** | 代码审查师 | ⭐⭐⭐⭐⭐ 8/10 | 22 项发现按严重度分级，修复建议详细 |
| **INCIDENT_RESPONSE.md** | SRE | ⭐⭐⭐⭐⭐ 8/10 | 含 SEV 评级、时间线、5-Why 分析、行动项 |

但这些文档是**静态快照**，未被项目自身引用或维护。新人加入项目时不会知道这些文档的存在。

### 3.3 项目结构文档

**缺失。** 目录结构没有文档说明：

```
SsqAnalyzer/
├── Controls/       # ❓ 为什么分离？放了哪些控件？
├── Converters/     # ❓ WPF 值转换器有哪些？
├── Models/         # ❓ 数据模型还是 ViewModel？
├── Pages/          # ❓ 9 个页面各自干什么？
├── Services/       # ❓ 服务层层次结构？
├── Themes/         # ❓ 主题系统？目前已定义 DarkTheme
├── App.xaml[.cs]   # ❓ DI 配置入口
├── MainWindow.xaml # ❓ 导航系统？
├── UiColors.cs     # ❓ 全局颜色管理
```

### 3.4 架构决策记录（ADR）

**有（在 ARCHITECTURE_ASSESSMENT.md 中）。** 但存在以下问题：

- ✅ 包含 5 个 ADR（混合 DI、TicketStore 职责、无 MVVM、curl.exe、硬编码 URL）
- ✅ 每个 ADR 有背景、选项分析、决策理由、后果
- ❌ **ADR 在架构评估文档内，不在独立的 `/docs/adr/` 目录**，难以增量维护
- ❌ **没有 ADR 索引**或状态跟踪表，新成员不知道哪些决策还在有效

---

## 4. API 文档

### 4.1 Service 接口/类的文档

**严重缺失。** 三个核心接口 `IDataService`、`ITicketStore`、`IAnalysisService` 完全没有任何文档。

以 `ITicketStore` 为例（45 行，~15 个方法），这是项目最复杂的服务接口，承载了票行管理、文件 I/O、加解密、Cookie、解析等职责，却没有任何方法说明：

```csharp
public interface ITicketStore
{
    // ❓ 这个方法是同步还是异步？会触发事件吗？
    // ❓ 参数 null 会怎样？
    // ❓ 返回值 null 代表什么？
    List<Ticket> ParseTicketsFromText(string text);

    // ❓ LoadApiKey 是从哪里加载的？文件？注册表？
    // ❓ 解密失败返回 null 还是抛异常？
    string? LoadApiKey();
    
    // ❓ SaveApiKey 保存到哪里？会覆盖现有配置吗？
    void SaveApiKey(string plainText, string? model = null, string? wsId = null, string? apiHost = null);
}
```

三个没有接口的 Service（AiAnalysisService、BiliService、DownloadService）更是不存在任何 API 契约文档。

### 4.2 配置项说明

| 配置项 | 存在文档 | 说明 |
|--------|---------|------|
| DashScope API Key | ❌ **无** | 无说明如何获取、如何配置、费用如何 |
| AI 模型选择 | 🟡 **部分** | ApiConfigDialog 的 ComboBox 中有模型列表和备注，但无独立文档 |
| Workspace ID | ❌ **无** | 无说明用途 |
| API Host | ❌ **无** | 无说明默认值和可选值 |
| 数据源 URL | ❌ **无** | 硬编码于 DataService.cs，无文档说明格式、更新频率 |
| 下载质量选择 | ❌ **无** | "720" vs 默认画质无文档 |
| B 站 UID/BVID | ❌ **无** | 输入格式、如何从 URL 提取无文档 |

### 4.3 API Key 设置 / 模型选择

代码中 `ApiConfigDialog` 提供了完整的 UI 配置，但没有任何使用文档：

- 用户需要自行申请 DashScope（阿里云千问）API Key
- 可用模型列表硬编码在代码中，无外部说明
- 不同模型的价格、能力差异、推荐场景无文档
- 无限流、速率限制、重试策略等无说明

---

## 5. Runbook / 操作手册

### 5.1 开发环境搭建

| 项目 | 状态 | 说明 |
|------|------|------|
| **.NET SDK 版本要求** | ❌ 无文档 | 目标框架 `net10.0-windows`，需要 .NET 10 SDK（Preview） |
| **IDE 要求** | ❌ 无文档 | 需要 Visual Studio 2022+ 或 VS Code + C# DevKit |
| **依赖项** | ❌ 无文档 | ClosedXML、CommunityToolkit.Mvvm、DI 包；无说明哪些是必需的 |
| **外部工具依赖** | ❌ 无文档 | `curl.exe` 要求（AiAnalysisService）和 `yt-dlp.exe`（DownloadService 运行时自动下载） |
| **环境变量/证书** | ❌ 无文档 | DPAPI 加密需要 Windows 环境 |
| **首次运行步骤** | ❌ 无文档 | clone → build → run 三步走无说明 |

### 5.2 构建/发布步骤

| 项目 | 状态 | 说明 |
|------|------|------|
| 构建命令 | ❌ 无文档 | `dotnet build -c Release` 无说明 |
| 发布命令 | ❌ 无文档 | `dotnet publish` 无配置文件和文档 |
| 发布产物 | ❌ 无文档 | 哪些文件需要分发，哪些不需要 |
| CI/CD | ❌ 无配置 | 无 GitHub Actions、无构建脚本 |

### 5.3 故障排查指南

| 场景 | 文档 | 说明 |
|------|------|------|
| 启动崩溃 | 🟡 部分 | INCIDENT_RESPONSE.md 记录了 NRE 启动崩溃的修复 |
| API 请求失败 | ❌ 无 | AiAnalysisService 错误仅 `Debug.WriteLine`，用户端只看到笼统错误 |
| DPAPI 解密失败 | ❌ 无 | 静默返回 null，用户无法诊断 |
| yt-dlp 下载失败 | ❌ 无 | 网络问题/校验失败无排查步骤 |
| crash.log 解读 | ❌ 无 | 有 crash.log 生成机制，但无说明如何阅读、如何报告 |
| B 站风控 | ❌ 无 | -352/-799/-412 错误码无说明 |

### 5.4 测试运行说明

| 项目 | 状态 | 说明 |
|------|------|------|
| 测试项目 | ❌ **不存在** | csproj 中有 `<InternalsVisibleTo Include="SsqAnalyzer.Tests" />`，但实际项目不存在 |
| 测试框架 | ❌ **未选型** | 未使用 NUnit/xUnit/MSTest |
| CI | ❌ **无** | 无自动化测试运行 |

---

## 6. 改进建议（优先级排序）

### 🔴 P0 — 1-2 周内（高影响，低工作量）

#### 1. 创建 README.md

**理由：** 项目完全没有任何介绍页面。新开发者在代码库面前完全迷失。

**建议内容：**
```markdown
# SsqAnalyzer — 双色球/大乐透走势分析工具

## 功能概览
- 📊 走势图（红球/蓝球频次、冷热分析）
- 📥 复式票数据管理与 AI 分析
- 🔥 复式票统计与导出
- ❌ 杀号推荐（高/中置信度）
- 🎯 组号推荐（均衡型/热号型/冷号型）
- 📋 开奖记录查询
- 📅 历史同期图
- ⚙️ API 配置（DashScope 千问模型）

## 快速开始
### 前置要求
- .NET 10 SDK（[下载](https://dotnet.microsoft.com/download/dotnet/10.0)）
- Windows OS (依赖 WPF 和 DPAPI)
- 可选：DashScope API Key（用于 AI 视频分析功能）

### 运行
```bash
git clone <repo-url>
cd SsqAnalyzer
dotnet run
```

## 项目结构
Controls/    — 自定义 WPF 控件（BallControl, MatrixGrid）
Converters/  — WPF 值转换器
Models/      — 数据模型
Pages/       — 9 个功能页面
Services/    — 服务层（数据/分析/B站/下载/AI）
Themes/      — 主题资源字典
```

#### 2. 为三个核心接口补充 XML 注释

**理由：** 接口是系统契约，没有注释导致任何调用者都需要阅读实现代码来理解行为。

**工作量：** 约 1-2 小时

**建议模板（以 ITicketStore 为例）：**
```csharp
/// <summary>
/// 票行数据存储与管理服务。
/// 负责票行的 CRUD、本地文件持久化、API Key/B站 Cookie 的加密存储、
/// 开奖数据解析和号码范围校验。
/// </summary>
public interface ITicketStore
{
    /// <summary>获取或设置当前彩票类型（SSQ/DLT）。切换时将清空当前票行列表。</summary>
    LotteryType CurrentType { get; set; }
    
    /// <summary>
    /// 从 AI 返回文本中解析票行。
    /// </summary>
    /// <param name="text">AI 返回的原始文本，格式为 "红球号码;蓝球号码"，每行一注。</param>
    /// <param name="type">指定的彩票类型，决定号码范围（SSQ: 红1-33/蓝1-16, DLT: 红1-35/蓝1-12）。</param>
    /// <returns>解析成功的票行列表。解析失败的格式会被静默跳过。</returns>
    List<Ticket> ParseTicketsFromText(string text, LotteryType type);
    
    /// <summary>
    /// 从加密文件加载 DashScope API Key。
    /// </summary>
    /// <returns>解密后的 API Key，若文件不存在或解密失败返回 null。</returns>
    /// <remarks>加密方式：Windows DPAPI (CurrentUser)。文件路径：{BaseDirectory}/data/api_key。</remarks>
    string? LoadApiKey();
}
```

#### 3. 缺失的文件创建

- **`.gitignore`** — 至少忽略 `bin/`、`obj/`、`.vs/`、`crash.log`
- **`.editorconfig`** — 统一缩进、编码风格；推荐使用 `dotnet new editorconfig`

---

### 🟠 P1 — 2-4 周内（中等影响，中等工作量）

#### 4. 为 AiAnalysisService / BiliService / DownloadService 补充完整 XML 注释

**理由：** 这三个 Service 不使用接口，类注释和方法注释是调用者了解其功能的唯一途径。

**重点注释位置：**
- `AiAnalysisService.CurlPostAsync` — 说明使用 curl.exe 的原因（有 ARCHITECTURE_ASSESSMENT.md ADR-004 已记录，应在代码中引用）
- `BiliService.BiliGetAsync` — 说明限频策略（800ms）、重试逻辑、风控处理
- `DownloadService.RunDownload` — 说明 yt-dlp 参数、输出格式、进度回调机制

#### 5. 创建 CHANGELOG.md

**理由：** 项目已有多次迭代（从静态 ServiceLocator 到 DI 容器，三个事故修复），需要版本记录。

**建议初始内容：**
```markdown
# Changelog

## [Unreleased]
### Added
- DI 容器集成（Microsoft.Extensions.DependencyInjection）
- API Key DPAPI 加密存储
- B站 WBI 签名认证
- yt-dlp SHA256 校验下载

### Fixed
- NullReferenceException 启动崩溃（移除 StartupUri）
- 进度条动画状态的 UX 修复
- 号码范围 Bug（AI 识别彩票类型传入解析器）
```

#### 6. 创建 `docs/` 目录整理现有文档

**理由：** 现有 3 个高质量 .md 文件缺乏发现路径。

**建议结构：**
```
docs/
├── adr/
│   ├── README.md          # ADR 索引和状态表
│   ├── 001-hybrid-di.md
│   ├── 002-ticketstore-srp.md
│   ├── 003-no-mvvm.md
│   └── ...
├── architecture.md         # 从 ARCHITECTURE_ASSESSMENT.md 提取
├── code-review/
│   └── 2026-06-28.md       # 从 CODE_REVIEW_REPORT.md 提取
└── incidents/
    └── 2026-06-27-nre-crash.md  # 从 INCIDENT_RESPONSE.md 提取
```

---

### 🟡 P2 — 1-2 个月内（中等影响，较高工作量）

#### 7. 为所有 Page 类的公共方法添加 XML 注释

**理由：** Page 文件是 UI 逻辑的主体（~2500 行），但没有注释。更严重的是所有 Page 同时有无参构造函数（通过 `App.Services.GetRequiredService`）和参数化构造函数——这是项目中最大的设计债务，至少需要注释说明。

**优先处理：**
- `CompoundStatsPage`（最复杂，~600+ 行）
- `TicketsPage`（导出逻辑，~400+ 行）
- `SettingsPage`（API/数据配置）

#### 8. 创建 CONTRIBUTING.md

**理由：** 帮助新开发者理解编码规范、命名约定、依赖注入规则。

**建议内容：**
```markdown
# Contributing

## 编码规范
- 所有接口方法必须包含 XML 注释（summary + param + returns）
- 避免使用 `App.Services.GetRequiredService<T>()`，优先通过构造函数注入
- 禁止将 API Key 通过命令行参数传递（安全问题）
- 使用 `Debug.WriteLine()` 而非 `Console.WriteLine()` 记录调试信息

## 代码审查清单
- [ ] XAML StartupUri 与 DI 不兼容，必须手动创建窗口
- [ ] ParseTicketsFromText 调用时务必传入明确彩票类型
- [ ] 新服务必须创建接口并注册到 DI 容器
```

#### 9. 在 XML 注释中使用 `<see>` / `<seealso>` / `<inheritdoc>` 标签

**理由：** 项目中有多个同名/相似功能的方法（如 `ParseTicketsFromText` 的两个重载），应使用 `<inheritdoc>` 减少重复。同时用 `<see>` 关联相关 ADR。

```csharp
/// <inheritdoc cref="ParseTicketsFromText(string, LotteryType)"/>
/// <remarks>使用当前 <see cref="CurrentType"/> 作为彩票类型。</remarks>
public List<Ticket> ParseTicketsFromText(string text) =>
    ParseTicketsFromText(text, _type);
```

---

### 🟢 P3 — 3-6 个月内（较高工作量）

#### 10. 迁移到 DocFX 或 Sandcastle 生成 API 文档网站

**理由：** 手动维护 XML 注释只能保证编辑器内体验。DocFX 可以将 XML 注释 + 额外 Markdown 文件生成为静态网站。

**推荐方案：**

| 工具 | 优点 | 缺点 |
|------|------|------|
| **DocFX** | 支持 .NET 项目、Markdown 文件嵌入、实时预览 | 需要 CI 集成部署 |
| **Sandcastle Help File Builder** | 成熟的 .NET 文档工具，输出 CHM/网站 | 维护不活跃 |
| **VS 自带的 `///` 生成** | 零配置 | 只能内联查看，无法生成站点 |

**建议选择 DocFX 的原因：**
1. 原生支持 .NET 项目的 XML 注释提取
2. 支持在文档中嵌入 Markdown 文件（可用于创建教程、How-to）
3. 支持自定义模板（匹配项目主题色）
4. 可以集成到 GitHub Actions 自动发布到 GitHub Pages

#### 11. 创建 Runbook

**建议分册：**

| 手册 | 内容 |
|------|------|
| **开发环境搭建** | .NET 10 SDK 安装、IDE 设置、首次运行步骤 |
| **构建与发布** | `dotnet build/publish` 命令参数、发布产物说明 |
| **故障排查指南** | crash.log 解读、常见错误码（-352、-412）、DPAPI 解密失败诊断 |
| **外部依赖管理** | curl.exe、yt-dlp.exe 版本要求、SHA256 校验说明 |

#### 12. 创建 ApiConfig 配置说明文档

**理由：** API Key 是 AI 功能的门槛。当前用户需要通过代码阅读来了解如何配置。

**建议内容：**
```markdown
# API 配置说明

## 支持的 AI 提供商
- **阿里云 DashScope（千问大模型）**
  - 用于：视频 AI 分析 → 自动提取票行

## 获取 API Key
1. 登录 [阿里云百炼平台](https://bailian.console.aliyun.com/)
2. 开通 DashScope 服务
3. 创建 API Key

## 模型选择
| 模型 | 推荐场景 | 说明 |
|------|----------|------|
| qwen3.7-plus | 推荐，通用分析 | 2h 超时，2GB 内存限制 |
| qwen3.6-flash | 快速轻量分析 | 响应快，适合简单票行提取 |
| qwen3-vl-plus | 视频理解 | 支持图片/视频输入的 VL 模型 |

## 注意事项
- API Key 通过 Windows DPAPI 加密存储在本地
- 请求超时 3 分钟，内置 2 次指数退避重试
- 不重试条件：403 权限错误、API Key 未配置
```

---

## 7. 附录：模板与工具推荐

### 7.1 XML 注释模板

```csharp
/// <summary>
/// （必填）简要描述方法/类的功能，使用主动语态。
/// </summary>
/// <param name="paramName">参数的用途、单位（如适用）、有效范围。</param>
/// <returns>返回值的含义。null 代表什么场景？</returns>
/// <exception cref="TException">什么条件下抛出此异常。</exception>
/// <remarks>补充说明：性能注意事项、线程安全、副作用。</remarks>
/// <example>
/// 可选：演示代码
/// <code>
/// var result = service.Method("input");
/// </code>
/// </example>
```

### 7.2 文档工具对比

| 工具 | 类型 | 安装难度 | 输出格式 | 推荐场景 |
|------|------|----------|----------|----------|
| **DocFX** | 文档生成器 | 中等（NuGet 包） | 静态网站 | 需要 API 文档网站的团队项目 |
| **Sandcastle** | 文档生成器 | 较高（VS 扩展） | CHM/网站 | 传统 .NET 企业项目 |
| **Swashbuckle** | API 文档 | 低（NuGet 包） | Swagger UI | REST API 项目（本项目不适用） |
| **MkDocs + Material** | Markdown 站点 | 低（Python） | 静态网站 | 手册/指南类文档（非 API 文档） |

### 7.3 DocFX 快速接入步骤

```bash
# 1. 安装 DocFX 全局工具
dotnet tool install -g docfx

# 2. 初始化文档项目
docfx init -o docs/

# 3. 生成 docfx.json 配置文件
# 编辑 src 路径指向 SsqAnalyzer.csproj

# 4. 构建文档网站
docfx build docs/docfx.json

# 5. 预览
docfx serve docs/_site
```

### 7.4 优先级汇总

```
本周可做 (P0):
├── 创建 README.md
├── 为 IDataService / ITicketStore / IAnalysisService 补充 XML 注释
└── 创建 .gitignore + .editorconfig

本月可做 (P1):
├── 为 AiAnalysisService / BiliService / DownloadService 补充 XML 注释
├── 创建 CHANGELOG.md
└── 创建 docs/ 目录，整理现有工程文档

本季度可做 (P2):
├── 为所有 Page 类补充 XML 注释
├── 创建 CONTRIBUTING.md
└── 使用 <inheritdoc> / <see> 等高级 XML 标签

下季度可做 (P3):
├── 引入 DocFX 生成 API 文档站点
├── 创建 Runbook（环境搭建 + 部署 + 故障排查）
└── 创建 API 配置说明文档
```

---

## 总结

**SsqAnalyzer 项目文档成熟度：2.8/10（需系统性改进）**

**最大优势：** 工程保障团队已产出了高质量的外部文档（架构评估、代码审查、事故响应），但这些都是独立存在的"报告"，尚未融入到项目的日常文档体系中。

**最大缺口：** 三个核心接口零注释、无 README 项目介绍、无 Runbook 操作手册——这些是开发者每天面对的基础设施。

**核心建议：**
1. **本周**创建 README.md + 为接口补充 XML 注释（2 小时内可完成，效果显著）
2. **本月**将现存的高质量工程文档转化为项目自身可引用的 `docs/` 目录
3. **本季度**引入 DocFX 等工具，让文档成为 CI/CD 的一部分而非事后补充

---

*报告结束 — 如需进一步讨论改进计划或协助创建模板，请通过主理人联系多库（Docu）。*
