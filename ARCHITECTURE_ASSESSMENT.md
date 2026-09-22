# 🏗️ SsqAnalyzer 架构评估报告

**评估人：** 阿奇（Archi）· 系统架构师  
**评估日期：** 2026-06-27  
**代码库版本：** 基于工作目录 2026-06-17-00-21-59  
**上次评级：** 4/10 → **本次评级：4.5/10**

---

## 目录

1. [架构质量评分](#1-架构质量评分)
2. [组件依赖图](#2-组件依赖图)
3. [关键架构决策记录（ADR）](#3-关键架构决策记录adr)
4. [架构风险热力图](#4-架构风险热力图)
5. [演化路线图](#5-演化路线图)

---

## 1. 架构质量评分

### 评分明细

| 维度 | 权重 | 得分 | 说明 |
|------|------|------|------|
| **DI & 服务解析** | 20% | 5/10 | 已从纯 ServiceLocator 改为混合模式，但 `App.Services` 静态定位器仍是隐式 ServiceLocator |
| **服务内聚 & SRP** | 15% | 4/10 | TicketStore 承载 6 项不同职责（票管理、文件I/O、加解密、Cookie、类型检测、期数提取） |
| **页面职责分离** | 15% | 4/10 | 所有页面仍为重度 code-behind，无 ViewModel 层；CompoundStatsPage 职责仍过重 |
| **接口完整性** | 10% | 5/10 | 核心数据/分析服务有接口，但 BiliService/DownloadService/AiAnalysisService 无接口 |
| **可测试性** | 15% | 3/10 | 无单元测试、无 mock 能力、静态 `App.Services` 污染全局、许多方法为 private/static |
| **错误处理** | 10% | 5/10 | 全局异常处理完善，但大量 catch 仅 Debug.WriteLine，静默吞异常 |
| **配置管理** | 5% | 3/10 | DataUrl 硬编码于 DataService 中；无 appsettings.json 或配置系统 |
| **可维护性 & 可扩展性** | 10% | 4/10 | 无 MVVM、无导航框架、Monolithic 单体结构、TicketStore 承担过多职责 |
| **总分** | **100%** | **4.5/10** | 较上次 4/10 略有改善，消除了静态 God 类，但基础设施层面问题依旧 |

### 关键改善点（自上次审查）

| 项目 | 上次状态 | 当前状态 |
|------|----------|----------|
| ServiceLocator 反模式 | ❌ 全局使用 | ⚠️ 残留 `App.Services` 静态属性，但提供了参数化构造函数 |
| 静态 God 类 | ❌ DataService/TicketStore 静态 | ✅ 已实例化并注入 DI 容器 |
| 页面职责过重 | ❌ CompoundStatsPage 全能 | ✅ 已拆分为 BiliService/DownloadService/AiAnalysisService |
| DI 容器 | ❌ 无 DI | ✅ 使用 `Microsoft.Extensions.DependencyInjection` |
| 全局异常处理 | ❌ 无 | ✅ 三重防护（UI/AppDomain/Task） |

---

## 2. 组件依赖图

### 2.1 服务层依赖（Interface / Class）

```
┌─────────────────────────────────────────────────────────────────────┐
│                        DI Container                                 │
│  (Microsoft.Extensions.DependencyInjection)                        │
└─────────────────────────────────────────────────────────────────────┘
         │                    │                 │            │
         ▼                    ▼                 ▼            ▼
┌──────────────┐    ┌────────────────┐  ┌──────────────┐  ┌──────────────┐
│  IDataService◄────│ AnalysisService│  │ BiliService  │  │AiAnalysisSvc │
│  (Singleton) │    │ (Transient)    │  │ (Singleton)  │  │ (Singleton)  │
│  ▲           │    │ ▲              │  │ ▲            │  │              │
│  │           │    │ │ depends on   │  │ │ depends on  │  │              │
│  │ DataService│    │ IDataService   │  │ │ ITicketStore│  │              │
│  └──────┬────┘    └────────────────┘  │ └──────┬─────┘  └──────┬───────┘
│         │                             │        │               │
│         │ depends on                  │        │ depends on    │
│         ▼                             ▼        ▼               ▼
│  ┌────────────────────────────────────────────────────────────────┐
│  │                       ITicketStore ◄──── TicketStore           │
│  │                        (Singleton)                            │
│  │  ┌────────────────────────────────────────────────────────┐    │
│  │  │ 职责混搭：Ticket CRUD │ 文件I/O │ DPAPI加解密          │    │
│  │  │ B站Cookie │ 类型/期数检测 │ 号码范围校验               │    │
│  │  └────────────────────────────────────────────────────────┘    │
│  └────────────────────────────────────────────────────────────────┘
│                                                                     
│  ┌──────────────────────────────────┐                              
│  │           DownloadService        │ (Singleton, 无依赖)          
│  │  yt-dlp 下载 + SHA256 校验       │                              
│  └──────────────────────────────────┘                              
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 页面层依赖

```
┌──────────────────────────────────────────────────────────────────────┐
│                           MainWindow                                │
│  ┌──────────────────────────────────────────────────────────────────┐│
│  │ Nav_Click → 切换 Page.Visibility + 调用 LoadData()               ││
│  └──────────────────────────────────────────────────────────────────┘│
│                                                                      │
│  Page[0]: AnalysisPage ─────→ IDataService, IAnalysisService        │
│  Page[1]: TrendPage ────────→ IDataService, IAnalysisService        │
│  Page[2]: StatisticsPage ───→ IDataService, IAnalysisService        │
│  Page[3]: KillPage ─────────→ IDataService, IAnalysisService        │
│  Page[4]: GroupPage ────────→ IDataService, IAnalysisService        │ ← 4/7 页面重复注入
│  Page[5]: HistoryPage ──────→ IDataService                           │
│  Page[6]: RecordsPage ──────→ IDataService                           │
│  Page[7]: TicketsPage ──────→ ITicketStore                           │
│  Page[8]: CompoundStatsPage → ITicketStore, BiliService,            │
│                                DownloadService, AiAnalysisService   │ ← 最重依赖页面
│  Page[9]: SettingsPage ─────→ IDataService, ITicketStore            │
│                                                                      │
│  Dialog: DataFilePicker ────→ ITicketStore                          │
│  Dialog: ApiConfigDialog ───→ (纯数据参数, 无 DI)                    │
│  Dialog: BiliLoginDialog ───→ (未详细分析, 可能有隐式依赖)           │
└──────────────────────────────────────────────────────────────────────┘
```

### 2.3 隐式依赖（非 DI 注入）

| 依赖项 | 位置 | 问题 |
|--------|------|------|
| `App.Services.GetRequiredService<T>()` | 所有 Page 的 parameterless 构造函数 | 静态 Service Locator 残留 |
| `FindResource("...")` Theme 资源 | 所有 Page 的 code-behind | 硬编码依赖 XAML 资源字典 |
| `curl.exe` 进程调用 | `AiAnalysisService.CurlPostAsync()` | 依赖外部 exe 可用性 |
| `yt-dlp.exe` 进程调用 | `DownloadService.RunDownload()` | 运行时自动下载，版本依赖 |
| `https://data.17500.cn/ssq_asc.txt` | `DataService` 硬编码 URL | 无配置、无 fallback、仅 SSQ |
| `System.IO.File` 路径硬编码 | TicketStore, DataService | 基目录拼接，无抽象 |
| `System.Security.Cryptography.ProtectedData` | TicketStore 安全存储 | DPAPI 依赖 Windows 平台 |

### 2.4 依赖合理性评估

| 依赖 | 类型 | 合理性 | 说明 |
|------|------|--------|------|
| Page → IDataService | 显式 DI | ✅ 合理 | 数据源依赖，通过接口解耦 |
| Page → IAnalysisService | 显式 DI | ✅ 合理 | 分析逻辑抽象，便于替换 |
| Page → ITicketStore | 显式 DI | ⚠️ 可接受 | 但 ITicketStore 接口过胖（~30方法），违反 ISP |
| AnalysisService → IDataService | 显式 DI | ✅ 合理 | 分析层依赖数据层 |
| BiliService → ITicketStore | 显式 DI | ⚠️ 可接受 | 但只用了 Cookie 相关方法，暴露了整个 ITicketStore |
| CompoundStatsPage → 4个服务 | 显式 DI | ⚠️ 警告 | 单个页面依赖过多，页面试图职责过大 |
| MainWindow → IDataService | 显式 DI | ✅ 合理 | 仅用于状态栏信息 |
| App.Services (静态) | 隐式 | ❌ 反模式 | 所有页面实际通过此解析依赖 |

---

## 3. 关键架构决策记录（ADR）

### ADR-001: 采用混合 DI / Service Locator 模式

**状态：** Accepted（过渡态）  
**日期：** 2026-06-27

#### 背景
项目从纯静态 ServiceLocator 模式迁移至 DI 容器。所有 Page 同时保留了 parameterless 构造函数（调用 `App.Services.GetRequiredService<T>()`）和参数化构造函数。由于 WPF XAML 实例化机制默认调用 parameterless 构造函数，最终运行时解析路径仍经过静态 `App.Services`。

#### 选项分析
| 选项 | 复杂度 | 可测试性 | 迁移成本 |
|------|--------|----------|----------|
| A. 混合模式（当前） | Low | Low | 零 |
| B. 完全 DI（使用 ViewModel + 宿主注入） | Med | High | 高（需重构 XAML / MVVM） |
| C. 完全去除 DI，回退纯 ServiceLocator | Low | Very Low | 零 |

#### 决策
**选择 A（混合模式）**，作为完整迁移到 MVVM 前的中间过渡态。

#### 理由
1. WPF UserControl 的 XAML 实例化机制不直接支持 DI 容器注入
2. 完整的 MVVM 重构需要较大投入，当前阶段不可行
3. 混合模式保留了显式的构造依赖声明（参数化构造函数），便于逐步迁移

#### 后果
- **变容易了：** 参数化构造函数为未来迁移到 MVVM 留下了入口点
- **变困难了：** `App.Services` 静态属性仍是一个可在任何地方访问的全局状态
- **需要重新审视：** 当引入 MVVM 框架后，应彻底移除静态 `App.Services`

---

### ADR-002: TicketStore 承担多职责（违反 SRP）

**状态：** Accepted（已知技术债）  
**日期：** 2026-06-27

#### 背景
`TicketStore` 类（约 470 行）同时承担以下职责：
1. 票行数据管理（CRUD + 事件通知）
2. 文件 I/O 持久化（SaveToDataDir / LoadFromFile / ListDataFiles）
3. API Key DPAPI 加解密存储
4. Bilibili Cookie DPAPI 加解密存储
5. 开奖数据解析（ParseTicketsFromText / ExtractPeriod / ExtractType）
6. 号码范围校验（ValidateTicketRange / DataFileExists）

#### 选项分析
| 选项 | 复杂度 | 内聚性 | 修改影响面 |
|------|--------|--------|------------|
| A. 保持现状（当前） | Low | Low | 高——任何一项修改都影响整个类 |
| B. 拆分为 3 个独立服务 | Med | High | 低——职责隔离 |
| C. 保持 TicketStore + 委托到私有内部类 | Med | Med | 中 |

#### 决策
**选择 A（保持现状）**，但在 Q3 路线图中规划拆分。

#### 理由
1. 项目当前处于功能快速迭代期，拆分会导致大量页面引用变更
2. 接口 `ITicketStore` 已定义了完整的边界，拆分时接口变化可控
3. TicketStore 的加解密和 Bilibili 部分有独立的前缀/注释，逻辑上已部分隔离

#### 后果
- **变容易了：** 当前开发效率高，修改一个文件即可完成多项功能
- **变困难了：** 任何单一功能的修改都可能引入回归，难以进行单元测试
- **需要重新审视：** Q3 末功能稳定后应进行拆分

---

### ADR-003: 无 MVVM，代码后置承载全部逻辑

**状态：** Accepted（已知技术债）  
**日期：** 2026-06-27

#### 背景
所有 9 个页面均使用 code-behind 模式，UI 渲染、事件处理、业务计算全部混在 UserControl 的 `.xaml.cs` 文件中。以 `AnalysisPage` 为例（约 380 行），同时承担：
- 服务依赖管理（IDataService / IAnalysisService）
- UI 控件创建（BallControl / StackPanel / Border 实例化）
- 业务计算（频率统计、颜色映射）
- 事件处理（按钮点击、期数切换）

#### 选项分析
| 选项 | 复杂度 | 可维护性 | UI 测试能力 |
|------|--------|----------|-------------|
| A. 保持 code-behind（当前） | Low | Low | 无 |
| B. 引入完整 MVVM（CommunityToolkit.Mvvm） | High | High | 高 |
| C. 渐进式提取 ViewModel + 数据绑定 | Med | Med | 部分 |

#### 决策
**选择 C（渐进式提取）**，作为中期目标。

#### 理由
1. 完整 MVVM 迁移需要重写所有 Page，投入极大
2. 渐进式迁移可以从高频变更页面（CompoundStatsPage, AnalysisPage）开始
3. 当前项目中已部分使用 `INotifyPropertyChanged`（CompoundStatsPage 的 VideoInfo 类），说明已有 MVVM 的基础模式认知

#### 后果
- **变容易了：** 目前开发速度不受 MVVM 框架约束
- **变困难了：** 任何 UI 变更都需要手动操作控件树，代码重复度高（TicketsPage 的 ExportImage 和 ExportHtml 中有大量重复的 Grid 构建逻辑）
- **需要重新审视：** 接入新功能时优先采用 ViewModel 模式

---

### ADR-004: AiAnalysisService 使用 curl.exe 进程调用

**状态：** Accepted  
**日期：** 2026-06-27

#### 背景
`AiAnalysisService` 通过 `System.Diagnostics.Process` 启动 `curl.exe` 进程来发送 HTTP POST 请求到阿里云 DashScope API。项目没有使用 `HttpClient` 或其他 .NET HTTP 客户端库直接调用 API。

#### 选项分析
| 选项 | 复杂度 | 可靠性 | 跨平台 | 调试能力 |
|------|--------|--------|--------|----------|
| A. curl.exe 进程调用（当前） | Low | Low | 仅 Windows | Low（stderr 截断到 200 字符） |
| B. HttpClient 直接调用 | Low | High | 全部 | High |
| C. OpenAI SDK / 阿里云 SDK | Med | High | 全部 | High |

#### 决策
**选择 C（逐步迁移到 HttpClient 调用）**，Q3 中期执行。

#### 理由
1. `curl.exe` 进程调用引入了外部工具依赖——不是所有 Windows 环境都预装 curl.exe
2. 进程调用导致错误信息被截断（`detail.Length > 200 ? detail[..200] + "…"`），极难调试
3. 项目中已使用 `System.Net.Http.HttpClient`（DataService, BiliService, DownloadService），技术基础已具备
4. HttpClient 方式可以更好地支持 CancellationToken、超时控制、日志记录

#### 后果
- **变容易了：** 可添加完整的请求日志、超时重试策略
- **变困难了：** 需要重写 `CurlPostAsync` 方法，但影响范围仅限于 CompoundStatsPage 的调用
- **需要重新审视：** 迁移后可添加集成测试

---

### ADR-005: DataService 硬编码数据源 URL

**状态：** Accepted  
**日期：** 2026-06-27

#### 背景
`DataService` 从硬编码的 URL `https://data.17500.cn/ssq_asc.txt` 获取双色球历史数据。该服务仅支持 SSQ 类型，不支持大乐透（DLT）。无配置系统、无回退数据源、无网络代理支持。

#### 选项分析
| 选项 | 复杂度 | 灵活性 | 可靠性 |
|------|--------|--------|--------|
| A. 硬编码 URL（当前） | Low | None | Low（单点故障） |
| B. 配置化（appsettings.json + IOptions） | Med | High | Med |
| C. 配置化 + 多数据源回退 | High | High | High |

#### 决策
**选择 B（配置化）**，Q4 执行。

#### 理由
1. 硬编码 `ssq_asc.txt` 完全不支持大乐透（DLT），但 TicketStore / AnalysisService 已是类型感知的
2. 没有配置系统意味着修改数据源需要重新编译
3. 项目团队规模小，Med 复杂度的方案可接受

#### 后果
- **变容易了：** 未来可添加 DLT 数据源及其他第三方数据提供商
- **变困难了：** 需要引入 Microsoft.Extensions.Configuration 包
- **需要重新审视：** 与 TicketStore 拆分同步进行，避免重复修改

---

## 4. 架构风险热力图

### 🔴 风险 #1：静态 `App.Services` 全局可达（最高优先级）

| 维度 | 评估 |
|------|------|
| **风险描述** | `App.Services` 是一个 `public static IServiceProvider`，在整个应用程序生命周期内全局可达。任何代码（Page、Control、Dialog、Service）都可以通过它解析任意服务，形成隐式的 Service Locator。这破坏了 DI 的显式依赖声明原则。 |
| **影响** | **高** — 导致依赖关系不透明、难以单元测试、服务生命周期管理混乱、无意中延长了 Transient 服务的生命周期 |
| **可能性** | **高** — 每次有新代码需要获取服务时，开发者很自然会使用 `App.Services.GetRequiredService<T>()`，而不是通过构造函数注入。 |
| **缓解策略** | 1. 短期：添加代码规范 + Code Analyzer 规则禁止使用 `App.Services`（除 XAML 实例化桥接外）<br/>2. 中期：为高频页面创建 ViewModel 层，将依赖声明转移到 ViewModel 构造函数<br/>3. 长期：引入 MVVM 框架（如 CommunityToolkit.Mvvm）后彻底移除 |

### 🟠 风险 #2：TicketStore 职责过载 — 修改辐射面过大

| 维度 | 评估 |
|------|------|
| **风险描述** | TicketStore 同时管理票数据、文件存储、加密凭据、B站 Cookie、号码检测，任意一项内部实现的变更（如 DPAPI → 其他加密方案）都会影响所有页面。 |
| **影响** | **高** — 影响 4 个页面（TicketsPage, CompoundStatsPage, SettingsPage, DataFilePicker），任何修改都有回归风险 |
| **可能性** | **中** — 已经发生过一次（DPAPI 密码学迁移），类似变更还会发生 |
| **缓解策略** | 1. 短期：定义清晰的内部职责边界，用 `#region` / 部分类（partial class）物理隔离<br/>2. 中期：提取为独立服务：`ICredentialStore`（凭据）、`IFileStore`（文件）、`ITicketParser`（解析）<br/>3. Q3 路线图中列出拆分任务 |

### 🟡 风险 #3：缺失可测试性基础设施

| 维度 | 评估 |
|------|------|
| **风险描述** | 整个项目无任何单元测试。关键服务（BiliService 的 WBI 签名、AnalysisService 的号码分析、AiAnalysisService 的 API 交互）均无自动化验证手段。所有方法要么是 private/static，要么依赖文件系统或网络。 |
| **影响** | **中高** — 每次修改依赖手工测试，回归遗漏风险高；新人难以理解代码行为 |
| **可能性** | **高** — 随着功能增加，手工测试覆盖比例持续下降 |
| **缓解策略** | 1. 短期：为 AnalysisService 的核心算法（频率统计、杀号、组号）添加纯逻辑单元测试——这些方法不依赖文件I/O，可直接测试<br/>2. 中期：为 BiliService、DownloadService、AiAnalysisService 创建接口，使得它们可 mock<br/>3. 长期：建立 CI 流程自动运行测试 |

---

## 5. 演化路线图

### 📅 Q3 2026（7月 - 9月）—— 基础设施加固

#### Phase 1：TicketStore 职责拆分（7月）
| 任务 | 预估工作量 | 优先级 |
|------|-----------|--------|
| 提取 `ICredentialStore`（API Key + B站 Cookie 加解密） | 2天 | P0 |
| 提取 `ITicketParser`（ExtractPeriod / ExtractType / ParseTicketsFromText） | 1天 | P0 |
| TicketStore 保留核心票 CRUD + 文件 I/O | - | - |
| 更新 DI 注册和所有引用 | 0.5天 | P0 |
| 回归测试验证 4 个受影响页面 | 0.5天 | P0 |

#### Phase 2：接口引入 + 可测试性提升（8月）
| 任务 | 预估工作量 | 优先级 |
|------|-----------|--------|
| 为 BiliService 创建 `IBiliService` 接口 | 0.5天 | P1 |
| 为 DownloadService 创建 `IDownloadService` 接口 | 0.5天 | P1 |
| 为 AiAnalysisService 创建 `IAiAnalysisService` 接口 | 0.5天 | P1 |
| 为 AnalysisService 的纯逻辑方法写单元测试（GetRedFrequency, GetKillRecommendations 等） | 2天 | P1 |
| 添加 NUnit/xUnit 测试项目 | 0.5天 | P1 |

#### Phase 3：AiAnalysisService HTTP 迁移（8月-9月）
| 任务 | 预估工作量 | 优先级 |
|------|-----------|--------|
| 将 `CurlPostAsync` 重写为基于 HttpClient 的实现 | 1天 | P1 |
| 添加请求/响应日志 | 0.5天 | P1 |
| 改进错误处理（保留完整错误消息） | 0.5天 | P1 |
| 移除 curl.exe 依赖 | - | - |

### 📅 Q4 2026（10月 - 12月）—— 架构现代化

#### Phase 4：渐进式 MVVM 迁移（10月-11月）
| 任务 | 预估工作量 | 优先级 |
|------|-----------|--------|
| 引入 CommunityToolkit.Mvvm NuGet 包 | 0.5天 | P2 |
| 为 CompoundStatsPage 创建 CompoundStatsViewModel（最复杂的页面） | 3天 | P2 |
| 为 AnalysisPage 创建 AnalysisViewModel | 2天 | P2 |
| 使用 `DataContext` 绑定替换 code-behind 渲染 | 每个页面1-2天 | P2 |
| 移除所有页面中的 `App.Services` 引用 | 1天 | P2 |

#### Phase 5：配置系统 + 数据源抽象（11月）
| 任务 | 预估工作量 | 优先级 |
|------|-----------|--------|
| 添加 appsettings.json | 0.5天 | P2 |
| 使用 `IOptions<T>` 管理 DataService 的 URL/超时配置 | 1天 | P2 |
| 添加大乐透数据源支持（DataUrl 配置化） | 1天 | P2 |
| 添加多数据源回退机制 | 1天 | P3 |

#### Phase 6：测试基础设施完善（12月）
| 任务 | 预估工作量 | 优先级 |
|------|-----------|--------|
| 为 IAnalysisService 的复合业务方法写单元测试 | 2天 | P2 |
| 为 BiliService 的 URL 解析写单元测试 | 0.5天 | P2 |
| 设置 GitHub Actions / 其他 CI 自动运行测试 | 1天 | P2 |
| 添加代码覆盖率门禁 | 0.5天 | P3 |

### 架构改进雷达图

```
                    DI 纯度 (5/10)
                       ▲
                      / \
      可维护性 ◄───── /   \ ──────► 服务内聚
      (4/10)        /     \         (4/10)
                    │  4.5 │
      可测试性 ◄───── \   / ──────► 接口完整性
      (3/10)        \   /         (5/10)
                     \ /
                      ▼
               错误处理 (5/10)
```

### 总结

**当前架构状态：** 从 4/10 改善到 4.5/10。最大进步是消除了静态 God 类并使用 DI 容器管理服务生命周期。最大隐患是 `App.Services` 静态 Service Locator 残留和 TicketStore 的超重职责。

**优先建议：**
1. **本周可做：** 添加代码规范禁止在新的代码中使用 `App.Services`（除必要桥接外）
2. **本月可做：** 拆分 TicketStore → 提取 CredentialStore 和 TicketParser
3. **本季度可做：** 引入接口为 BiliService/DownloadService/AiAnalysisService 并添加纯逻辑单元测试
4. **下季度可做：** 渐进式 MVVM 迁移 + 配置系统化
