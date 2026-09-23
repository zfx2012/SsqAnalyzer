# SsqAnalyzer 🎱

以双色球为主的彩票数据分析工具 — WPF 桌面应用。复式票导入、解析和统计同时支持大乐透；开奖数据、点位和组号模块以双色球为准。

## 功能

- **📊 开奖记录** — 抓取双色球历史开奖数据，支持期数筛选和本地缓存
- **📈 走势图** — 红球/蓝球走势矩阵图，支持 012 路、周期过滤、奇偶过滤
- **📋 复式票统计** — 导入/手动录入复式票号码，自动统计出现频率、跨度、和值等
- **🎬 视频分析** — 搜索 B站 视频 → 下载 → AI 识别视频中的复式票号码（DashScope Qwen 模型）
- **🎯 杀号推荐** — 基于历史数据的杀号/组号推荐算法
- **🔬 杀号评估** — 整期错杀与完整保留率、随机对照、时间分段验证、多规则并集回测，以及开奖前冻结和开奖后自动结算；见 [评估口径与操作说明](Verification/KILL_EVALUATION.md)
- **内置规则条件修订** — 保留全部 50 条，修改其中 31 条的触发条件；截至 2026109 期，近 50 次触发均达到固定门槛。此成绩参与过历史调参，不是独立验证或未来保证；见 [逐条条件与前后成绩](Verification/KILL_CONDITION_TUNING.md)。
- **📍 点位推荐** — 正式/影子算法、历史回测、预测冻结和前向验证账本
- **🧩 组号推荐** — 汇总走势、复式票统计、点位和杀号大底，至少两项可用时生成 1～20 注基础单式
- **📤 导出** — HTML 表格导出 / PNG 图片导出 / 点位 Excel 导出

## 技术栈

| 层级 | 技术 |
|------|------|
| 框架 | WPF (.NET 10, Windows-only) |
| DI | Microsoft.Extensions.DependencyInjection |
| 加密 | DPAPI (ProtectedData) |
| AI API | DashScope / 阿里云百炼 (HttpClient) |
| 视频下载 | yt-dlp |
| B站API | 直连 + WBI 签名 |
| 验证 | 独立控制台验证程序，31 组检查；另有离线性能测量入口 |

## 快速开始

### 前置条件

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [yt-dlp](https://github.com/yt-dlp/yt-dlp)（B站视频下载需要，自动安装）

### 构建 & 运行

```bash
# 克隆后进入项目根目录
cd SsqAnalyzer

# 构建主程序和验证程序（显式指定解决方案，避免根目录临时 csproj 干扰）
dotnet build SsqAnalyzer.slnx

# 运行
dotnet run --project SsqAnalyzer.csproj

# 运行验证（任一检查失败会以非零退出码结束）
dotnet run --project Verification/SsqAnalyzer.PositionVerification.csproj
```

### 首次使用配置

1. 启动应用 → 进入 **设置** 页面
2. 配置 DashScope API Key（从 [阿里云百炼](https://help.aliyun.com/zh/model-studio/) 获取）
3. （可选）配置 B站 Cookie 以登录态搜索视频

## 项目结构

```
SsqAnalyzer/
├── App.xaml(.cs)          # 应用入口 + DI 容器配置 + 全局异常处理
├── PositionCommandLineHandler.cs # 点位命令行参数、执行和 JSON 输出
├── Pages/
│   ├── CompoundStatsPage   # 复式票数据（B站搜索/下载/AI分析）
│   ├── TicketsPage         # 复式票统计（导入/导出/统计）
│   ├── TrendPage           # 走势图
│   ├── RecordsPage         # 开奖记录
│   ├── SettingsPage        # 设置（API Key / Cookie / 模型选择）
│   ├── KillPage            # 杀号推荐
│   ├── PositionPage        # 点位推荐与验证
│   ├── GroupPage           # 组号推荐
│   └── HistoryPage         # 历史同期
├── Services/
│   ├── TicketStore         # 核心数据服务（加密存储/文件IO/类型检测）
│   ├── DataService         # 开奖数据抓取与缓存
│   ├── AnalysisService     # 统计分析与算法
│   ├── BiliService         # B站 API 调用（WBI 签名/搜索/用户信息）
│   ├── DownloadService     # yt-dlp 视频下载 + SHA256 校验
│   ├── AiAnalysisService   # AI API 调用（HttpClient + 重试 + 取消）
│   ├── PositionPredictor*.cs # 同一个 partial 类，按职责拆分；见下方导航
│   ├── PositionValidationStore # 密封预测和开奖后前向验证
│   ├── PositionExperimentCoordinator # 正式/影子预测自动冻结
│   ├── GroupService / GroupInputStore # 组号与上游输入快照
│   └── Kill/              # 杀号规则、JavaScript 执行器和回测
├── Controls/
│   ├── MatrixGrid          # 走势矩阵自定义渲染控件
│   ├── BallControl         # 号码球控件
│   └── TrendChart          # 走势图曲线
├── Models/
│   ├── DrawRecord          # 开奖记录数据模型
│   └── AnalysisModels      # 分析结果模型
├── Themes/
│   └── DarkTheme.xaml      # 深色主题统一颜色体系
└── Verification/          # 控制台验证项目，与主程序源码隔离
```

## 关键架构决策

| 决策 | 说明 |
|------|------|
| DI 容器 | Microsoft.Extensions.DependencyInjection，页面使用构造函数注入；XAML 无参构造通过 App.Services 解析 |
| 界面组织 | XAML + 页面后台代码 + 服务层；不是完整 MVVM 分层 |
| 加密方案 | 全局 DPAPI (ProtectedData) 替代硬编码 AES 密钥 |
| AI 调用 | HttpClient（替代 curl.exe），API Key 在内存 Headers 中传递 |
| 视频下载 | yt-dlp 子进程（支持 B站/YouTube 等平台） |
| 验证入口 | Program.cs 启动，VerificationSuite.cs 统一注册，各 *Verification.cs 按模块实现 |

## 代码导航

`PositionPredictor` 仍是同一个类，拆分只调整文件组织，版本常量、方法签名和计算逻辑保持原样：

| 文件（均位于 Services/） | 职责 |
|---|---|
| `PositionPredictor.cs` | 版本常量、构造函数、期号选项与预测入口 |
| `PositionPredictor.Versions.cs` | 历史版本与影子版本的工厂方法 |
| `PositionPredictor.Scoring.cs` | 红蓝球评分入口 |
| `PositionPredictor.AnnualShort.cs` | 年度短窗点位策略 |
| `PositionPredictor.HierarchicalModel.cs` | 动态分层模型拟合与概率计算 |
| `PositionPredictor.RangeEvents.cs` | 范围事件评分、校准和全局选择 |
| `PositionPredictor.RedSelection.cs` / `.BlueSelection.cs` | 红球组合与蓝球公式、选择逻辑 |
| `PositionPredictor.Backtesting.cs` | 滚动回测、配对比较与统计指标 |
| `PositionPredictor.Features.cs` / `.Patterns.cs` | 特征计算与历史模式统计 |
| `PositionPredictor.Snapshots.cs` / `.Types.cs` | 快照标识、冻结条件、配置校验与内部类型 |

应用启动仍由 `App.xaml.cs` 负责。`PositionCommandLineHandler.TryHandle` 识别原有命令；无参数或未知命令返回 `false`，继续原有窗口启动流程。命令处理器通过回调输出 JSON 和请求退出，便于独立验证参数默认值、错误输出和退出码。

验证程序的文件划分见 [Verification/README.md](Verification/README.md)。
性能测量方法及本轮结果见 [Verification/PERFORMANCE.md](Verification/PERFORMANCE.md)。

走势图刷新优化及截图兼容性记录见 [Verification/TREND_PERFORMANCE.md](Verification/TREND_PERFORMANCE.md)。

完整重建时的单元格复用及 100/300 期测量见 [Verification/TREND_RENDER_PERFORMANCE.md](Verification/TREND_RENDER_PERFORMANCE.md)。

本轮全部优化、后台任务生命周期修复和验证范围见 [Verification/OPTIMIZATION_SUMMARY.md](Verification/OPTIMIZATION_SUMMARY.md)。

## 文档

- [点位算法状态](POSITION_ALGORITHM.md) — 算法研究、版本与前向验证记录
- [组号推荐设计](GROUP_RECOMMENDATION_DESIGN.md) — 目标设计；其中部分“当前实现”描述已落后于代码
- [架构评估](ARCHITECTURE_ASSESSMENT.md)、[代码审查](CODE_REVIEW_REPORT.md)、[文档评估](DOCUMENTATION_ASSESSMENT.md) — 历史报告，不能替代当前构建与验证结果
- [事故响应记录](INCIDENT_RESPONSE.md)、[后续事故响应记录](INCIDENT_RESPONSE_V2.md)

## 历史版本记录（测试数量等为当时记录）

### v1.1 (2026-06-28)
- curl→HttpClient 替换，消除 API Key 三路泄露风险
- ITicketStore 接口拆分为 ITicketRepository/ICredentialStore/ITicketParser
- 对角线预测代码提取为共享 DiagonalChainRenderer
- 矩阵遗漏值 O(N²→N) 性能优化
- 测试覆盖率从 84 → 137（+63）
- 编译警告 34→0
- HtmlEncode + CSV 转义（XSS/公式注入防护）

## 构建说明

```bash
# Debug 构建
dotnet build SsqAnalyzer.csproj -c Debug

# Release 构建
dotnet build SsqAnalyzer.csproj -c Release

# 主程序运行时可构建到独立目录，避免覆盖被占用的 exe
dotnet build Verification/SsqAnalyzer.PositionVerification.csproj -o tmp/verification-build
dotnet tmp/verification-build/SsqAnalyzer.Tests.dll
```

`Verification/`、`tmp/` 和 `output/` 不参与主项目的默认文件扫描。`bin/`、`obj/` 是 SDK 构建产物；根目录的 `*_wpftmp.csproj` 是 WPF 临时项目，不是开发入口。

验证程序包含临时文件和规则仓库写入检查，建议使用上述独立输出目录。该命令直接运行验证入口，不启动主程序，也不会触发主程序启动时的预测冻结。

## 依赖说明

| 外部依赖 | 用途 | 安装方式 |
|----------|------|---------|
| .NET 10 SDK | 运行时和 SDK | [官网下载](https://dotnet.microsoft.com/download/dotnet/10.0) |
| .NET HttpClient | AI API 调用 | .NET 内置，无需额外安装 |
| yt-dlp | B站视频下载 | 自动安装到程序目录下的 `yt-dlp.exe` |

## 数据文件位置

所有数据存储在应用程序目录下的子文件夹中：

- `data/SSQ_{期号}.txt` — 双色球复式票数据
- `data/DLT_{期号}.txt` — 大乐透复式票数据
- `video/` — 下载的视频文件
- `ssq_data.txt` — 开奖历史数据缓存
- `api_key` — DPAPI 加密的 API 配置
- `bili_cookie` — DPAPI 加密的 B站 Cookie
- `kill-settings.json` — 杀号门槛配置
- `position_forward_validation.json` — 点位预测与前向验证账本
