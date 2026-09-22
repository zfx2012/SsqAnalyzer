# 🚨 SsqAnalyzer 事故响应报告

> **报告人**: Rex (SRE Engineer)
> **生成时间**: 2026-06-27
> **项目**: SsqAnalyzer — C# WPF .NET 10 双色球/大乐透彩票分析工具

---

## 1️⃣ 事故分诊与 SEV 评级

### 📊 总评分

| 事故 | SEV 等级 | 影响范围 | 当前状态 |
|------|----------|----------|----------|
| **① NullReferenceException 启动崩溃** | **SEV-1 🔴** | 应用完全无法启动，所有用户受影响 | ✅ **已修复** |
| **② 进度条一闪而过 + 状态滞留"AI分析中"** | **SEV-3 🟡** | 视频分析功能 UX 降级，功能可用但无视觉反馈 | ✅ **已修复** |
| **③ 号码范围 Bug** | **SEV-2 🟠** | AI 识别大乐透数据时按 SSQ 号码范围(33/16)解析，导致号码丢失/数据损坏 | ✅ **已修复** |

**综合评估**: 堆叠 SEV-1（启动崩溃）。最严重的 NullReferenceException 已在 `App.xaml.cs` 中修复。当前代码库三个已知 Bug 均已修复，但从架构上仍存在系统性风险（详见第 5 节预防措施）。

### SEV 快速查阅

| 等级 | 标准 | 当前匹配 |
|------|------|---------|
| SEV-1 | 服务宕机，所有用户受影响 | ✅ 启动时 NRE 崩溃 |
| SEV-2 | 主要功能降级 | ✅ 号码范围解析错误导致数据损坏 |
| SEV-3 | 次要功能问题 | ✅ 进度条动画 UX |
| SEV-4 | 低影响问题 | — |

---

## 2️⃣ 事件时间线

### 2026-06-27 关键事件

| 时间 | 事件 | 详情 |
|------|------|------|
| **17:47:26** | 🚨 **SEV-1 触发** | `crash.log` 记录 `NullReferenceException` — 应用在 `Application.DoStartup()` 阶段崩溃 |
| **17:47:26** | 📝 **日志记录** | `App.LogCrash()` 将异常写入 `bin/Debug/net10.0-windows/crash.log` |
| **稍后** | 🔍 **根因定位** | XAML `StartupUri` 指向 `MainWindow`，但 WPF XAML 加载器调用无参构造函数，而 `MainWindow` 只有参数化构造函数 `MainWindow(IDataService)` |
| **修复 1** | ✅ **移除 StartupUri** | 从 `App.xaml` 中删除 `StartupUri` 属性 |
| **修复 1** | ✅ **手动创建 MainWindow** | 在 `OnStartup()` 中改为 `new MainWindow(Services.GetRequiredService<IDataService>())`，并通过 DI 容器注入 |
| **修复 2** | ✅ **进度条修复** | `Storyboard.Begin(AnalyzeStatus, true)` 使沙漏动画可控 — 解决进度条一闪而过/状态滞留 |
| **修复 3** | ✅ **号码范围修复** | 为 `ParseTicketsFromText` 新增 `(string, LotteryType)` 重载，AI 分析时使用 AI 识别的类型而非 UI 选中的 `_type` 来决定号码范围 |

### 修复前后对比

```
[修复前] App.xaml  →  StartupUri="MainWindow"  →  XAML加载无参构造函数  →  NRE ❌
[修复后] App.xaml  →  (无 StartupUri)            →  OnStartup手动 new MainWindow(service)  →  OK ✅
```

---

## 3️⃣ 根因分析 — 5 Why

### 事故 #1: NullReferenceException 启动崩溃

```
Why 1: 为什么应用启动时在 DoStartup() 阶段崩溃？
       → 抛出 NullReferenceException，堆栈指向 XAML 加载器尝试创建 MainWindow 实例。

Why 2: 为什么 XAML 加载器创建 MainWindow 实例时失败？
       → WPF 的 XAML 加载器 (BamlLoader) 调用无参构造函数，但 MainWindow 没有提供无参构造函数。

Why 3: 为什么 MainWindow 没有无参构造函数？
       → MainWindow 的唯一公开构造函数是 MainWindow(IDataService dataService)，需要 DI 注入。

Why 4: 为什么 App.xaml 的 StartupUri 仍然指向 MainWindow？
       → StartupUri 是声明式写法，WPF 运行时自动解析 XAML 并创建窗口实例，
         但 StartupUri 机制不理解也不参与 DI 容器，它只调用默认的无参构造。

Why 5: 根本的系统性原因是什么？
       → WPF 的 StartupUri 机制与 DI 容器架构不兼容。
         StartupUri 是一种"声明式"启动方式，而应用已经采用了"手动构建"的 DI 模式
         (ServiceCollection + ServiceProvider)。两者混合使用时，
         StartupUri 绕过了 DI 容器直接创建 MainWindow，导致 DI 依赖无法被满足。
```

**根本原因总结**:
> XAML `StartupUri` 声明式启动与 `Microsoft.Extensions.DependencyInjection` 手动容器构建策略冲突。WPF 的 XAML 加载器在解析 `StartupUri` 时，直接通过反射调用无参构造函数，而 `MainWindow` 已被设计为仅接受 DI 注入的参数化构造函数，二者不匹配导致 `NullReferenceException`。

### 事故 #2: 进度条一闪而过 + 状态滞留

**根因**: `Storyboard` 动画生命周期管理缺陷。`StopHourglassAnimation()` 中调用了 `Storyboard.Stop()` 但未正确处理动画状态恢复，导致进度条在动画停止后未正确隐藏，而状态文本停留在"AI分析中"。

### 事故 #3: 号码范围 Bug

**根因**: `ParseTicketsFromText(string text)` 单参数重载使用 `_type`（UI 上用户当前选中的彩票类型）而非视频实际对应的类型。用户在 UI 选中"双色球"时分析大乐透视频，AI 返回大乐透号码（如 32、35），但解析器按双色球范围（红球 1-33、蓝球 1-16）过滤，导致号码被丢弃。

**更深的 Why**:
- `TicketStore` 的 `CurrentType` 是全局可变状态，被 UI 和分析逻辑共享
- 单参数重载 `ParseTicketsFromText(text)` 隐式依赖这个可变状态，但调用方未意识到这种隐式依赖
- 从 `AnalyzeSingleVideo` 调用时，`detectedType` 已被 AI 识别，但未传入解析方法

---

## 4️⃣ 行动项

### ✅ 已采取的行动

| # | 行动项 | 责任人 | 状态 | 修复文件 |
|---|--------|--------|------|----------|
| A1 | 移除 App.xaml 中 `StartupUri` 属性 | SRE | ✅ **完成** | `App.xaml` |
| A2 | `OnStartup()` 中改为手动 `new MainWindow(services)` | SRE | ✅ **完成** | `App.xaml.cs` |
| A3 | `Storyboard.Begin(AnalyzeStatus, true)` 可控生命周期 | Dev | ✅ **完成** | `CompoundStatsPage.xaml.cs` |
| A4 | 为 `ParseTicketsFromText` 新增 `(string, LotteryType)` 重载 | Dev | ✅ **完成** | `TicketStore.cs` |
| A5 | `AnalyzeSingleVideo` 调用时传入 `detectedType` | Dev | ✅ **完成** | `CompoundStatsPage.xaml.cs` |

### 📋 待采取的行动

| # | 行动项 | 优先级 | 说明 |
|---|--------|--------|------|
| B1 | **增加启动自检机制** | **高** | 启动时验证 DI 容器中所有注册服务是否能成功 Resolve，发现失败时给出明确错误消息而非静默 NRE |
| B2 | **统一启动入口** | **中** | 考虑引入 `AppStartupException` 统一处理类，确保启动过程中任何异常都能被 `DispatcherUnhandledException` 捕获，而不是靠 `crash.log` 事后追溯 |
| B3 | **添加 CI 构建验证** | **中** | 在 GitHub Actions 或本地脚本中增加 `dotnet build` 和基本启动测试，防止 StartupUri 回归 |
| B4 | **增加 Storyboard 单元测试** | **低** | 为 UI 动画控制器添加 mockable 抽象层，验证开始/停止/恢复状态流转正确 |
| B5 | **全局状态审计** | **中** | 审查 `TicketStore.CurrentType` 等全局可变状态，识别所有隐式依赖路径，建立"显式参数优先"编码规范 |
| B6 | **错误消息用户体验优化** | **低** | 启动失败时在 crash.log 之外增加 UI 提示（已有 MessageBox，但 NRE 本身绕过了 UI 初始化阶段） |

---

## 5️⃣ 预防措施

### 🔧 短期（1-2 周内）

1. **DI 容器启动自检**
   - 在 `OnStartup` 中 `BuildServiceProvider` 后，遍历所有 Singleton 注册并调用 `GetRequiredService` 验证
   - 对 Transient 注册执行快速实例化验证
   - 示例实现：
   ```csharp
   void ValidateContainer(IServiceProvider sp)
   {
       // 验证所有 Singleton 可 resolve
       sp.GetRequiredService<IDataService>();
       sp.GetRequiredService<ITicketStore>();
       sp.GetRequiredService<BiliService>();
       sp.GetRequiredService<DownloadService>();
       sp.GetRequiredService<AiAnalysisService>();
       // MainWindow 的构造函数验证
       ActivatorUtilities.CreateInstance<MainWindow>(sp);
   }
   ```

2. **代码 Review 检查清单更新**
   - 新增条目：「XAML `StartupUri` 与 DI 不兼容 — 必须手动创建窗口」
   - 新增条目：「`ParseTicketsFromText` 调用时务必传入明确彩票类型」

### 🏗️ 中期（1-4 周内）

3. **异常处理分层**
   - 区分「启动异常」（应终止应用 + 明确报错）和「运行时异常」（应记录 + 优雅恢复）
   - 启动异常使用自定义 `StartupException`，不依赖 `NullReferenceException` 这种非语义异常

4. **UI 动画抽取抽象层**
   - 将 `Storyboard` 操作封装为 `IAnimationService` 接口，便于测试和状态管理
   - 当前 `CompoundStatsPage` 直接操作 WPF `Storyboard`，难以单元测试

### 🏛️ 长期（1-3 个月内）

5. **架构评审 — 全局状态治理**
   - `TicketStore.CurrentType` 是全局可变单例状态，多处代码依赖它但语义不明确
   - 建议将操作上下文（当前彩票类型、当前期号）封装为 `AnalysisContext` 值对象，通过方法参数显式传递

6. **添加集成测试**
   - 至少覆盖完整的启动流程（App → MainWindow → 页面初始化）
   - 覆盖 AI 分析→解析票行的全链路（可 mock API 返回固定 JSON）

7. **自动化部署检查清单**
   - 构建前：`dotnet build` 无错误
   - 构建后：启动应用并验证 MainWindow 成功显示（可借助 WinAppDriver 或基本 Process 检测）
   - 发布前：检查 crash.log 是否为 0 字节

---

## 6️⃣ 站会摘要

**Rex (SRE) · 2026-06-28 站会**

1. **SEV-1 事故已关闭** 🟢：`NullReferenceException` 启动崩溃已在昨日的修复中处理 — 移除 `StartupUri` 改为 DI 手动创建 `MainWindow`。crash.log 已有记录，无新增复现报告。

2. **两个 SEV-2/3 问题同步修复** ✅：进度条动画滞留和号码范围解析 Bug 已在同一轮修复中完成。`AnalyzeSingleVideo` 现在使用 AI 识别的彩票类型进行号码解析，不再依赖 UI 选中状态。

3. **根因总结**：三个 Bug 的根源指向同一类问题 — **声明式配置与手动 DI 容器不兼容**（StartupUri）和**隐式全局状态依赖**（UI 选中类型影响 AI 解析结果）。

4. **行动推进中** 🔄：将起草「DI 容器启动自检」代码，防止类似启动时静默 NRE。CI 构建验证脚本待排期。

5. **阻塞项** ⛔：无。当前代码主分支已全部修复且可正常启动。

---

*报告结束 — 如需进一步调查或新增行动项，请通过主理人联系 Rex。*
