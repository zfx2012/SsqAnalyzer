# 验证程序

这是独立的控制台验证程序，通过主项目的 `InternalsVisibleTo` 检查内部算法。程序集名保持为 `SsqAnalyzer.Tests`，不使用 xUnit 或 Moq。

从项目根目录运行：

```powershell
dotnet build Verification/SsqAnalyzer.PositionVerification.csproj -o tmp/verification-build
dotnet tmp/verification-build/SsqAnalyzer.Tests.dll
```

成功时输出 `Passed 32/32 verification groups.`；任一断言失败会抛出异常并以非零退出码结束。完整运行包括年度研究，耗时明显长于普通单元检查。

页面专项检查可使用 `dotnet tmp/verification-build/SsqAnalyzer.Tests.dll --page-check`。
该检查包含杀号页三态排序、刷新保持选择和排序、搜索组合筛选、空列表、窗口切换、启用状态持久化，以及详情错误样本的窗口隔离。
`KillSortingVerification.cs` 使用 60 条混合规则，逐一覆盖全部 9 个可排序列的升序、降序、默认顺序，以及真实复选框连续操作后的行容器、选择、横纵滚动和异步仓储通知。启用列在勾选时保持行位置，再次点击表头按最新状态排序；启用状态筛选仍即时生效。
同时在验证程序目录生成 `kill-page.png`、`kill-details.png` 和 `kill-rules-1200.png` / `kill-rules-900.png`，用于检查实际 WPF 布局。

## 离线性能测量

```powershell
dotnet build Verification/SsqAnalyzer.PositionVerification.csproj -c Release -o tmp/performance-build
$env:DOTNET_TieredCompilation = '0'
dotnet tmp/performance-build/SsqAnalyzer.Tests.dll --benchmark tmp/performance-results.json
```

`--benchmark` 运行固定种子的离线样本，不运行默认验证套件、不读取实际票据和凭据。省略输出路径时将完整 JSON 打印到控制台。该环境变量只作用于当前 PowerShell 会话及其子进程，用于减少分层 JIT 对前后测量的影响。

每项先预热，再测量 3 或 5 个样本，报告每次操作的中位耗时、当前线程的累计托管内存分配和结果哈希。组号每个样本执行 10 次，模型拟合每个样本执行 3 次。各样本之间显式 GC；序列化和哈希计算不计入耗时或分配。性能数值不作为测试通过条件，输出不确定时则直接失败。

走势分别测量同数据引用及同内容新列表的刷新和布局，不包含窗口显示和 GPU 绘制。算法性能历史记录见 [性能记录](PERFORMANCE.md)。

专项走势测量包含真实数据变化重建、012 切换、100/300 期首次布局、两个方向的期数切换，以及 15 个场景的 PNG 和像素哈希：

```powershell
dotnet tmp/performance-build/SsqAnalyzer.Tests.dll --trend-benchmark tmp/trend-results
```

每项预热后取 5 次样本的中位数。省略目录时输出到 `tmp/trend-benchmark`。详见 [走势性能记录](TREND_PERFORMANCE.md)。

期数切换的准备渲染和布局在计时区间外完成，300→100 与 100→300 分别测量。首次布局指新建矩阵控件后的渲染及布局，进程和字体已经预热，不代表程序冷启动时间。本轮结果见 [完整渲染记录](TREND_RENDER_PERFORMANCE.md)。

## 数据解析测量

数据文本解析可单独离线测量：

```powershell
dotnet tmp/performance-build/SsqAnalyzer.Tests.dll --data-benchmark tmp/data-results.json
```

该入口先运行兼容性检查，再用内嵌数据比较冻结的旧解析器和当前实现，每项预热后测量 5 组、每组 10 次。记录解析耗时、当前线程托管分配和结果哈希，不读写实际开奖数据文件，不访问网络。详见 [数据解析优化记录](DATA_PARSING_PERFORMANCE.md)。

## 文件导航

| 文件 | 内容 |
|---|---|
| `Program.cs` | 启动默认验证套件或显式选择性能测量 |
| `VerificationSuite.cs` | 统一注册检查，保持既有检查顺序 |
| `CommandLineVerification.cs` | 命令识别、参数默认值、JSON 结构、退出码和异常边界 |
| `AnalysisVerification.cs` | 统计结果与旧实现的兼容性 |
| `DataParsingVerification.cs` / `DataParsingReference.cs` | 数据文本解析兼容性、旧实现基准及离线性能测量 |
| `CancellationVerification.cs` | 最后一次规则执行期间取消、提交前保护、批量部分完成和下载准备前取消 |
| `PositionPredictionVerification.cs` | 点位边界、预测约束、未来数据隔离、年度范围 |
| `PredictorPerformanceVerification.cs` | 周期前缀结果、同分顺序、未来隔离和复用缓冲区的浮点逐位一致性 |
| `PerformanceMeasurements.cs` | 固定数据的预测、回测、组号、矩阵布局和研究测量 |
| `PositionResearchVerification.cs` | 点位研究和年度短窗研究 |
| `WorkbookVerification.cs` | 年度导出的期号范围 |
| `LedgerVerification.cs` | 账本生命周期、前向门槛、哈希和损坏保护 |
| `KillRuleVerification.cs` | 内置杀号规则筛选和旧 ID 迁移 |
| `GroupVerification.cs` | 组号输入状态、确定性和合法性 |
| `TicketVerification.cs` | 票据文件期号解析 |
| `VideoVerification.cs` | 模拟 B站响应、下载校验和进度计算 |
| `TrendVerification.cs` | STA 线程中的矩阵生命周期、配色和交互 |
| `TrendCacheVerification.cs` | 同内容控件复用、缓存失效、完整重建单元格复用及旧状态清除 |
| `TrendPerformanceMeasurements.cs` | 走势专项耗时、分配量和渲染截图测量 |
| `PageLifecycleVerification.cs` | 走势/历史页重载、订阅去重、过期回调取消和渲染器复用 |
| `AsyncPageVerification.cs` | 延迟回测/搜索/代码生成/模型响应、弹窗覆盖、其他页面订阅清理；由页面生命周期检查调用 |
| `VerificationSupport.cs` | 公共断言、固定数据构造和临时目录辅助方法 |
| `TestDoubles.cs` | 模拟数据服务和 HTTP 响应 |

原有检查和辅助方法归入同一个 `partial VerificationSuite`，保持私有可见性。新增模块检查后，在 `VerificationSuite.Run` 中注册；无需将辅助方法改为公开接口。

页面生命周期检查最后运行：它在独立 STA 线程创建一个 WPF Application 并加载实际主题资源，直接实例化走势页和历史页，模拟 Loaded/Unloaded 并处理 Dispatcher 队列。检查结束后关闭 Application，不启动主窗口。该检查确认重复 Loaded 只订阅、读取一次，卸载及再次加载后旧回调不再读取数据，且每页始终复用同一个斜连渲染器；还覆盖走势图筛选缓存失效、空数据恢复、历史页已有查询重新加载。

主窗口目前通过 Visibility 切换页面，隐藏不等于 Unloaded。本轮保持隐藏页面接收数据更新的行为；卸载保护仅在实际收到 Unloaded 时生效。

组号验证覆盖 1、5、20 注的数量、升序号码和重复调用一致性。候选排序与说明集合复用的前后测量见 [组号性能记录](GROUP_PERFORMANCE.md)。

取消检查使用可控规则执行器和内存仓储，不使用延时制造竞态，不调用网络或修改实际规则文件。覆盖最后一期执行时取消、刚满 30 次触发时取消、取消同时规则抛异常，以及批量第一条完成后第二条取消；断言已取消规则不保存统计、不改变启用状态，已完成规则保持保存结果。取消检查是提交前的协作式保护，不回滚已经开始或完成的持久化操作。

视频路径额外在文件读取、下载准备、下载完成、AI 返回和保存分析结果前检查取消；等待下载完成展示的延时也接受取消。下载准备中的取消直接传播，不尝试备用源。下载旧任务的错误、取消提示和最终动画清理仅在它仍是当前任务时更新界面。这些边界的远程网络交互未在离线套件中进行端到端测试。

年度导出检查增加开始前取消、最后一次进度报告时取消，以及缺少目标年度之前历史数据时的错误提示。异步页面检查使用 TaskCompletionSource 和受控后台执行器模拟旧请求延迟成功/失败，不访问真实凭据或远程服务。完整汇总见 [OPTIMIZATION_SUMMARY.md](OPTIMIZATION_SUMMARY.md)。

验证会写入临时文件，规则迁移检查还会暂时写入验证程序输出目录下的规则文件并在结束时恢复，因此推荐使用独立输出目录。它不会启动 WPF 主窗口，也不会调用主程序的启动逻辑。

固定门槛验证 `KillThresholdVerification.cs` 使用实际回测引擎验证红球 81%/82%、蓝球 93%/94% 边界，同时检查列表、报告、未注入设置时的默认判断，以及旧规则门槛字段不会覆盖固定值。

回测正确性专项：`dotnet tmp/verification-build/SsqAnalyzer.Tests.dll --backtest-check`。窗口拆分、异常记录、禁用状态持久化和旧统计迁移详见 [回测正确性修复](BACKTEST_CORRECTNESS.md)。
