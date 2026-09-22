# 🚨 SsqAnalyzer 事故响应报告 v2

> **报告人**: Rex (SRE Engineer — 雷克斯)
> **生成时间**: 2026-06-28
> **项目**: SsqAnalyzer — C# WPF .NET 10 双色球/大乐透彩票分析工具
> **基准**: 代码审查报告 (22项发现) + 当前代码文件快照
> **状态**: 3个 SEV-1/2/3 已修复(v1报告), 以下为**当前未修复**的剩余风险

---

## 1️⃣ 事故分诊与 SEV 评级

### 按 SEV 等级排序（未修复）

| # | 事故 | SEV | 影响范围 | 概率 | 检测难度 | 分类 |
|---|------|-----|----------|------|----------|------|
| **I-1** | API Key 通过 curl.exe 临时文件 + 托管堆明文缓存 | **SEV-1** 🔴 | 所有使用 AI 分析的用户（DashScope API Key 凭据泄露 → 财务损失 + 隐私泄露） | 中 | 难 | 安全-凭证泄露 |
| **I-2** | HTML 导出无转义编码 → XSS 漏洞 | **SEV-1** 🔴 | 复式票统计导出功能（恶意数据文件导致脚本注入，可窃取本地 API Key） | 低 | 中 | 安全-XSS |
| **I-3** | DPAPI 文件完整性缺失（无 optionalEntropy + 无版本号） | **SEV-1** 🔴 | 所有加密存储凭据（api_key / bili_cookie 文件可被回滚攻击） | 低 | 难 | 安全-存储完整性 |
| **I-4** | TicketStore.CurrentType 全局可变状态无锁 → 竞态条件 | **SEV-2** 🟠 | AI 分析 + 数据文件加载并发时数据损坏（号码错误 / 空列表崩溃） | 中 | 中 | 正确性-并发 |
| **I-5** | B站 Cookie 无限轮询无超时上限 | **SEV-2** 🟠 | B站登录功能（线程无限轮询 B站 API 1.5s/次，CPU/网络资源耗尽） | 高 | 易 | 正确性-可用性 |
| **I-6** | ExtractType 全角冒号不兼容 → AI 解析失败 | **SEV-2** 🟠 | AI 视频分析功能（Qwen 输出含全角冒号时类型无法识别，号码范围用错导致数据损坏） | 中 | 中 | 正确性-数据损坏 |
| **I-7** | curl.exe 进程泄漏 + 错误截断 200 字符 → 调试困难 | **SEV-3** 🟡 | AI 视频分析功能（长时间运行后资源耗尽；API 错误无法定位） | 中 | 难 | 资源管理-进程泄漏 |
| **I-8** | File.ReadAllText 全量读入 → 大文件 OOM | **SEV-3** 🟡 | 本地数据文件加载（几千注大票文件内存溢出） | 低 | 易 | 性能-OOM |
| **I-9** | DPAPI 解密失败静默返回 null → 用户无感知 | **SEV-3** 🟡 | 所有依赖 API Key / B站 Cookie 的功能（凭据静默丢失，用户以为功能坏了） | 中 | 难 | 可用性-静默失败 |
| **I-10** | ExtractPeriodFromFileName 首个匹配歧义 | **SEV-3** 🟡 | 本地文件加载（文件名含年份数字时取错期号） | 低 | 中 | 正确性-数据不一致 |
| **I-11** | InferType 对 DLT 复式票误判为 SSQ | **SEV-3** 🟡 | 分析结果保存 / 类型推断（复式票数据用错号码范围） | 中 | 中 | 正确性-数据损坏 |
| **I-12** | TitleWithExt 硬编码 .mp4 → 扩展名不匹配 | **SEV-4** 🟢 | 视频选择显示的扩展名与实际文件不符 | 低 | 易 | UX-显示错误 |
| **I-13** | LoadData() 空方法 + MainWindow 调用 | **SEV-4** 🟢 | 全部页面导航（无副作用，但影响代码可维护性和新人理解） | 高 | 易 | 可维护性-死代码 |

### 综合风险评分

```
高风险区域 (SEV-1): ████████████████████  3 项
              ↓ 需立即行动
中高风险 (SEV-2):   ██████████████          3 项
              ↓ 需 1 周内修复
中等风险 (SEV-3):   ████████████           5 项
              ↓ 需本月修复
低风险 (SEV-4):     ████                   2 项
```

---

## 2️⃣ 5-Why 根因分析

### 选择的事故：**I-1 — API Key 通过 curl.exe + 托管堆明文缓存**

**选择理由**：这是一个复合型安全漏洞，一旦被利用（本机进程枚举 + 内存转储），攻击者可获取 DashScope API Key 并在外部滥用，直接造成财务损失。它涉及三种攻击向量：命令行（已部分缓解）→ 托管堆（未缓解）→ 底层进程（未彻底缓解）。

```
Why 1: 为什么 API Key 可能被本机其他进程获取？
       → 透过两条路径：
         (a) 进程枚举 → 查看 curl.exe 命令行参数（虽然已改用头文件，但 endpoint URL 仍在命令行）
         (b) 进程内存转储 → AiAnalysisService.CurlPostAsync 收到 key 参数后传入 curl.exe，
             且 _cachedApiKey 以 string 类型长期驻留在 TicketStore 的托管堆中，GC 前无法擦除

Why 2: 为什么 API Key 长期驻留在托管堆中？
       → TicketStore._cachedApiKey 是 string? 私有字段。只要 TicketStore 实例存活（Singleton），
         _cachedApiKey 就持有 key 的引用。string 在 .NET 中是不可变类型，即使设为 null，
         原始字符串内容仍存在于 GC 堆中，直到被回收覆盖。
         → 而 TicketStore 注册为 Singleton：应用生命周期 = 缓存生命周期。
       → 所以在 AiAnalysisService.CurlPostAsync 中也直接收到 key 字符串参数，
         调用栈上同样存在明文拷贝。

Why 3: 为什么 AiAnalysisService 使用 curl.exe 而不是 HttpClient？
       → 这是一个架构决策（见 ADR-004）。开发者选择了进程调用而非内建 HTTP 客户端。
         当时的考虑：curl 已在 Windows 10+ 预装，无需额外依赖。
         然而代价是：(1) API Key 经过进程边界传递；(2) 无法利用 .NET 的安全内存管理；
         (3) 即使改用头文件，curl.exe 进程信息仍对同级进程可见。
       → 更深层原因：项目早期没有统一的 HTTP 抽象层，DataService 和 BiliService 各自管理
         自己的 HttpClient 实例，而 AiAnalysisService 选择了不同的技术路线。

Why 4: 为什么没有使用 SecureString / ZeroMemory 模式？
       → 开发者使用了简单的 string 类型存储敏感信息。
         根本原因：项目没有「敏感信息处理规范」——没有文档规定 API Key、Cookie 等
         敏感数据在内存中应如何存储、何时应擦除。
       → 代码审查已指出此问题（#4 高严重度），但尚未修复。
       → 从 TicketStore 的代码模式看，_cachedApiKey 的缓存逻辑是为了避免频繁文件 I/O，
         设计上优先考虑了性能，但牺牲了安全性。

Why 5: 系统层面的根本原因是什么？
       → **缺少安全编码规范+安全审计流程。**
         具体来说：
         (1) 项目没有安全敏感信息的统一处理策略（内存中怎么存、进程间怎么传）
         (2) 没有代码审查的安全检查清单
         (3) DI 容器和服务架构分散了关注点：
            - TicketStore 负责存储/缓存（但缓存了它不该长期持有的敏感凭据）
            - AiAnalysisService 负责 API 调用（但选择了不安全的传输方式）
            - 两者之间没有安全边界或协议约定
         (4) 近期修复（头文件方案）提升了命令行安全性，但没有触及根本的凭据生命周期管理问题
         (5) 缺乏自动化安全测试（如凭据泄漏检测、内存扫描）
```

**根本原因总结**：
> 项目缺少统一的敏感信息处理规范和安全审计流程。API Key 在内存中作为普通 string 长期缓存（TicketStore Singleton 生命周期），且通过 curl.exe 进程边界传输（即使使用头文件缓解，endpoint URL 仍在命令行暴露）。深层系统原因是安全编码规范缺失、安全审查清单不完整、以及分层架构没有定义安全边界。

---

### 备选：**I-4 — TicketStore.CurrentType 竞态条件**

```
Why 1: 为什么在 AI 分析过程中切换彩票类型会导致数据损坏？
       → CurrentType 的 setter 在 _type 改变时执行 Tickets.Clear()。
         若 AI 分析（异步）正在通过 _type 进行号码解析，中间状态会收到 Clear()，
         导致解析中的页面访问到空列表或错误类型。

Why 2: 为什么 CurrentType setter 没有线程安全保护？
       → setter 是一个裸属性访问器，使用了简单的 if 比较然后赋值+清空。
         整个 setter 没有 lock 或任何同步原语。
         Tickets 列表本身是 List<Ticket>，也非线程安全。

Why 3: 为什么 TicketStore 设计为全局可变单例？
       → TicketStore 注册为 Singleton。最初设计是为了让所有页面共享同一份票行数据。
         CurrentType 是一个全局切换开关，被 UI（所有页面）和分析逻辑共享。
         这违反了「显式参数优先」原则——上下文依赖了隐式全局状态。

Why 4: 为什么没有在架构层面禁止全局可变状态？
       → 架构阶段（ADR-003）已经意识到 code-behind 模式的问题并选择了渐进式迁移，
         但尚未触及全局状态治理(TicketStore.CurrentType)。
         代码审查(#6)已经指出此问题，但尚未进入修复队列。
         当前项目的 DI 容器设计（Singleton TicketStore）本身鼓励了全局状态的共享。

Why 5: 根本原因？
       → **模块间通信没有采用显式上下文传递。**
         CurrentType 作为一个横切关注点，应该封装在 AnalysisContext 值对象中通过
         方法参数显式传递，而非作为全局属性读写。这本质上是一种隐式依赖反模式。
         → 架构师在 ADR-002 承认了 TicketStore 违反 SRP，但竞态风险未被单独评估。
```

---

## 3️⃣ 行动项

### 🔴 P0 — 立即行动（SEV-1 事故，48 小时内）

| # | 行动项 | 对应事故 | 责任方 | 说明 |
|---|--------|---------|--------|------|
| A1 | **AiAnalysisService 从 curl.exe 迁移到 HttpClient** | I-1 | SRE + Dev | 重写 `CurlPostOnce` → `HttpClient.PostAsync`。`Authorization` 头通过 `HttpRequestMessage.Headers.Authorization` 设置，完全消除命令行暴露。错误保留完整消息（不再截断到 200 字符） |
| A2 | **API Key 缓存改用 SecureString / 字节数组 + 显式擦除** | I-1 | Dev | 在 `TicketStore` 中将 `_cachedApiKey` 从 `string?` 改为 `byte[]`（或 `SecureString`）。使用完毕后调用 `Array.Clear()` / `SecureString.Dispose()`。从 `AiAnalysisService.CurlPostAsync`（未来 HttpClient 版本）中不再传递 key 字符串参数，改用委托/接口注入 |
| A3 | **HTML 导出添加 HtmlEncode** | I-2 | Dev | 在 `TicketsPage.xaml.cs` ExportHtml_Click 中为所有拼接字符串调用 `System.Net.WebUtility.HtmlEncode()`，包括 `_store.Period` 和所有号码拼接 |
| A4 | **DPAPI 增加 optionalEntropy + 版本号校验** | I-3 | Dev | `ProtectedData.Protect(plainBytes, additionalEntropy, ...)` 使用应用级别的固定 entropy。在加密数据末尾附加版本号字节，未来迁移时可识别格式 |

### 🟠 P1 — 本周行动（SEV-2 事故）

| # | 行动项 | 对应事故 | 责任方 | 说明 |
|---|--------|---------|--------|------|
| A5 | **CurrentType setter 添加锁保护** | I-4 | Dev | `lock (_typeLock)` 保护 _type 的读写 + Tickets.Clear()。添加 `readonly object _typeLock = new()` |
| A6 | **BiliLoginDialog.PollLogin() 添加超时上限（120秒）** | I-5 | Dev | 添加 `DateTime.UtcNow + TimeSpan.FromSeconds(120)` 截止时间，超时后自动停止轮询并提示用户 |
| A7 | **ExtractType 改用正则匹配支持全角/半角冒号** | I-6 | Dev | `@"类型\s*[:：]\s*(双色球|大乐透)"` 正则替换当前 `clean.Contains("类型:双色球")` 硬匹配 |
| A8 | **创建安全编码规范文档** | 全部 | SRE + 架构师 | 编写安全编码规范并加入 PR 审查 checklist：内存中凭据处理、命令行参数、XSS 预防、加密存储 |

### 🟡 P2 — 本月行动（SEV-3 + SEV-4）

| # | 行动项 | 对应事故 | 责任方 | 说明 |
|---|--------|---------|--------|------|
| A9 | **CurlPostOnce 统一 Kill + Dispose 到 finally 块** | I-7 | Dev | 将 `proc.Kill() + proc.Dispose()` 统一放到 finally 块，确保所有异常路径都释放进程句柄 |
| A10 | **ValidateTicketRange 改用 File.ReadLines 延迟读取** | I-8 | Dev | `foreach (var line in File.ReadLines(filePath))` 替换 `File.ReadAllText` + `Split` |
| A11 | **DPAPI 解密失败添加 NeedsReconfiguration 通知标志** | I-9 | Dev | `catch (CryptographicException)` 中设置 `NeedsReconfiguration = true`，SettingsPage 加载时检测并弹窗提示用户重新配置 |
| A12 | **ExtractPeriodFromFileName 优先匹配已知前缀后的数字段** | I-10 | Dev | 按 `_` 分隔后取最后一个数字段，而非遍历所有分段的首个匹配 |
| A13 | **InferType 添加文件名优先检测** | I-11 | Dev | 有文件名时优先使用 `DetectTypeFromFileName` 而非号码范围推断。保存文件路径参数 |
| A14 | **TitleWithExt 读取实际文件扩展名** | I-12 | Dev | `Path.GetExtension(path)` 动态获取扩展名，移除硬编码 `.mp4` |

### 🟢 P3 — 下个迭代

| # | 行动项 | 对应事故 | 责任方 | 说明 |
|---|--------|---------|--------|------|
| A15 | LoadData() 添加注释说明或移除 MainWindow 调用 | I-13 | Dev | 添加 XML 注释说明「由 XAML Loaded 事件驱动，此方法为符合导航接口保持空实现」 |
| A16 | 添加 CI 安全扫描（secrets 检测 + 凭据泄漏检查） | 全部 | SRE | GitHub Actions 集成 `dotnet format` + Secret Scanner，防止 API Key 硬编码 |

### 行动项优先级排布

```
48h 内:
├── A1  curl.exe → HttpClient 迁移（最高优先级）    ← SRE + Dev
├── A2  内存凭据改用 SecureString                   ← Dev
├── A3  HTML 导出 HtmlEncode                        ← Dev
└── A4  DPAPI optionalEntropy                       ← Dev

本周:
├── A5  CurrentType 加锁                            ← Dev
├── A6  BiliCookie 轮询超时                          ← Dev
├── A7  ExtractType 正则全角/半角                    ← Dev
└── A8  安全编码规范文档                             ← SRE + 架构师

本月:
├── A9  进程统一 Kill + Dispose                      ← Dev
├── A10 File.ReadLines 延迟读取                       ← Dev
├── A11 DPAPI 失败通知                               ← Dev
├── A12 期号解析歧义修正                             ← Dev
├── A13 InferType 文件名优先                          ← Dev
└── A14 TitleWithExt 动态扩展名                      ← Dev

下个迭代:
├── A15 LoadData 注释/清理                          ← Dev
└── A16 CI 安全扫描                                  ← SRE
```

---

## 4️⃣ 状态沟通模板

### 📢 事故通报模板

```
🚨 [INCIDENT] SsqAnalyzer — [事故名称]

SEV: [SEV-1/SEV-2/SEV-3]
状态: [待处理 / 处理中 / 已修复]
报告人: Rex (SRE)

📋 影响范围
- 功能：[受影响的功能模块]
- 用户：[受影响用户范围]
- 数据：[数据影响/是否丢失]

🔍 根因（5-Why 摘要）
[1-2 句话的根因总结]

🛠 修复方案
[修复方式描述]

⏱ 时间线
[2026-06-28] SEV 评定 — [操作说明]
[预计] 修复完成 — [预计日期]

📌 行动项
- [ ] [行动项 1] — [负责人] — [截止日期]
- [ ] [行动项 2] — [负责人] — [截止日期]
- [ ] [行动项 3] — [负责人] — [截止日期]

📎 参考
- 代码审查: [链接到 CODE_REVIEW_REPORT.md #issue-number]
- 架构评估: [参考 ARCHITECTURE_ASSESSMENT.md ADR-NNN]
- 相关代码: [文件路径:行号]

💡 注意
[升级条件、回滚方案、用户通知策略等]
```

### 示例：针对 I-1 的事故通报

```
🚨 [INCIDENT] SsqAnalyzer — API Key 凭据泄露风险

SEV: SEV-1
状态: 待处理（48h 内修复）
报告人: Rex (SRE)

📋 影响范围
- 功能：AI 视频分析（DashScope API）
- 用户：所有配置了 API Key 的用户（~100% 的分析用户）
- 数据：DashScope API Key 明文暴露于本机进程枚举 + 托管堆内存转储
  → 攻击者可滥用 Key 调用 DashScope API 产生费用

🔍 根因（5-Why 摘要）
项目缺少统一的敏感信息处理规范。API Key 通过三条路径暴露：
(1) 托管堆中长期缓存（TicketStore.Singleton → string 不可擦除）
(2) curl.exe 进程命令行（endpoint URL）
(3) 临时头文件（虽已清理但存在时间窗口）

🛠 修复方案
A1: curl.exe → HttpClient 迁移（消除进程边界暴露）
A2: SecureString 替换 string 缓存（+ 使用后显式擦除）

⏱ 时间线
[2026-06-28] SEV-1 评定 — 代码审查 #1/#4 确认未修复
[2026-06-30] 预计修复完成

📌 行动项
- [ ] A1: curl.exe → HttpClient 迁移 — SRE + Dev — 2026-06-30
- [ ] A2: SecureString 内存缓存 — Dev — 2026-06-30
- [ ] A8: 安全编码规范文档 — SRE + 架构师 — 2026-07-04

📎 参考
- 代码审查: CODE_REVIEW_REPORT.md #1 (SEV: 严重), #4 (SEV: 高)
- 架构评估: ARCHITECTURE_ASSESSMENT.md ADR-004
- 相关代码: Services/AiAnalysisService.cs:38-114
             Services/TicketStore.cs:450-468

💡 注意
- 修复前不建议在生产环境运行 AI 分析功能
- 强烈建议用户在本机修复完成前**不要在安装了 SsqAnalyzer 的机器上运行其他不受信进程**
- 修复完成后请立即**重置 DashScope API Key** 以确保旧 Key 失效
```

---

## 5️⃣ 整体风险评估

### 当前项目安全姿态

```
安全姿态评分: 4/10 ⚠️

已修复 (v1 报告):   NRE 启动崩溃 / 进度条 UX / 号码范围 Bug ✅
未修复安全风险:     API Key 暴露(SEV-1) / XSS(SEV-1) / DPAPI完整性(SEV-1) ❌
未修复正确性风险:   竞态(SEV-2) / AI解析(SEV-2) / 无限轮询(SEV-2) ❌
```

### 关键指标

| 指标 | 数值 | 目标 |
|------|------|------|
| SEV-1 未修复数 | **3** | 0 |
| SEV-2 未修复数 | **3** | 0 |
| SEV-3 未修复数 | **5** | ≤ 2 |
| 累计修复行动项 | **16 项** | — |
| 需要用户配合的行动 | **1**（重置 API Key） | — |

---

## 6️⃣ 站会摘要

**Rex (SRE) · 2026-06-28 站会**

1. **v1 三个事故已关闭**（NRE 崩溃、进度条 UX、号码范围 Bug）🟢

2. **新增 3 个 SEV-1 未修复风险**🔴：最严重的是 API Key 通过 curl.exe + 托管堆缓存暴露（I-1），已在 5-Why 分析中确认根本原因——项目缺少安全编码规范。建议优先修复合并尽快部署。

3. **三个 SEV-2 需要本周处理**🟠：竞态条件（CurrentType）、无限轮询（B站 Cookie）、AI 解析兼容性（全角冒号），预计 2-3 天可修复。

4. **行动中**🔄：已分配 Dockerfile 中的 A1（curl→HttpClient，SRE+Dev 双人）、A2（SecureString，Dev）、A3（HtmlEncode，Dev）、A4（DPAPI entropy，Dev）。A8（安全编码规范，SRE+架构师）作为基础设施工作并行推进。

5. **外部依赖**⛔：curl→HttpClient 迁移需要回归测试验证 AI 视频分析是否正常，建议在迁移前保存一份当前版本的测试视频；HtmlEncode 修改后需验证导出 HTML 的正确渲染。

---

*报告结束 — 如需进一步调查、调整 SEV 等级或行动项优先级，请通过主理人联系 Rex。*
