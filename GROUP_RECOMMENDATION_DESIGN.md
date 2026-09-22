# SsqAnalyzer 组号推荐模块详细设计

> 版本：v1.2  
> 日期：2026-07-28  
> 适用项目：SsqAnalyzer（WPF / .NET 10）  
> 适用彩种：双色球（SSQ）  
> 文档状态：待实现

## 1. 设计结论

组号推荐是现有“历史走势、复式票统计、杀号、点位推荐”之后的组合决策层。它不重新发明上游算法，也不把任何统计信号解释成真实中奖概率；它负责把现有输出冻结为一次可复现的输入快照，在合法性约束、用户选择和预算限制下生成单式、复式或胆拖方案，并给出逐项可解释的策略评分。

本设计针对当前项目作出以下关键调整：

1. 技术实现采用 WPF、C#、现有依赖注入和 JSON 文件持久化，不引入 PySide6、SQLite 或第二套应用框架。
2. 直接复用 `IDataService`、`IPositionPredictor`、`IKillEngine`、`ITicketRepository` 的领域输出，不从其他页面控件读取数据。
3. 走势、复式票统计和 6 个点位是可解释的规则事实；杀号数据存在时直接限定红蓝候选大底，不存在时回退双色球全集。
4. 现有随机抽取式 `GetGroupRecommendations` 将被确定性、可复现的组号服务替代；相同输入快照、规则版本、请求和随机种子必须产生相同结果。
5. 第一阶段只建设“生成、解释、复制、导出”闭环。“确认购票、派奖清算、盈亏统计”不并入现有 `TicketStore`，因为该服务当前保存的是从视频或文件导入的复式票样本，不是用户实际购票账本。
6. V1 仅支持双色球。项目中的大乐透复式票导入能力不能被误认为组号引擎已经支持大乐透。
7. `GroupPage` 首屏是按期号监控的上游数据仪表盘。四项无论有没有数据都可点击；开始组号时只冻结当前可用项，不要求 4/4。

## 2. 当前实现与目标差距

### 2.1 当前实现

当前 `GroupPage`：

- 只注入 `IDataService` 和 `IAnalysisService`；
- 支持按 50、100、200、全部历史期数刷新；
- 调用 `IAnalysisService.GetGroupRecommendations(periods)` 生成三组单式；
- 按固定频次阈值划分冷热号，再通过 `Random.Shared` 随机抽取；
- 不消费点位预测、杀号报告或当前复式票统计；
- 没有输入快照、规则版本、运行标识、冲突解释和成本计算；
- 同一份数据重复刷新可能得到不同号码，无法回放或验证。

### 2.2 目标实现

目标实现应具备：

- 上游数据状态检查和快照冻结；
- 按目标期展示走势图、复式票统计、点位、杀号四个就绪站点；
- 缺失站点一键导航、携带目标期，并支持完成后返回组号；
- 单式、复式、胆拖三种格式；
- 预算、注数、倍数和成本计算；
- 合法性硬约束、用户锁定约束、可配置评分规则；
- 点位与杀号冲突的双向溯源；
- 确定性候选生成和多样化 Top K；
- 取消、超时、评估数量上限；
- 方案评分明细、数据缺失警告和免责提示；
- 可选的运行记录 JSON，用于复现与后续回测。

## 3. 模块边界

### 3.1 模块负责

- 选择目标期号和历史窗口；
- 汇总并冻结上游输出；
- 校验用户规格与预算；
- 构建红球、蓝球候选池；
- 生成并评分基础单式组合；
- 从基础组合构造单式组合包、复式票或胆拖票；
- 计算基础注数和成本；
- 输出规则命中、扣分、冲突以及未就绪时的阻断原因；
- 监控四个上游模块对当前目标期的就绪状态；
- 展示上游摘要，并从缺失项导航到对应功能页；
- 保存可复现的生成运行记录；
- 复制或导出推荐结果。

### 3.2 模块不负责

- 修改历史开奖数据；
- 修改点位算法或杀号规则；
- 从 `PositionPage`、`KillPage`、`TicketsPage` 的可视控件抓取状态；
- 宣称“提高中奖概率”或输出伪概率；
- 自动认定用户已经购票；
- 自动获取浮动奖金、执行开奖清算或生成盈亏账本；
- 在 V1 中支持大乐透；
- 在线学习并自动修改正式规则权重。

## 4. 上游数据仪表盘

### 4.1 仪表盘的单一任务

仪表盘回答两个问题：

1. 当前目标期的四类上游数据分别有什么；
2. 点击“开始组号”时，哪些数据会实际参与本次规则计算。

四项数据不是强制门槛。任意组合都可以开始组号；某项无数据时跳过对应信号，并在结果中明确显示“本次未使用”。即使四项都没有数据，页面也允许按票面合法性和当前组号规则生成基础方案，但必须显示醒目的低信息提示。

四个站点有真实的业务顺序：

```text
走势图 ───── 复式票统计 ───── 点位推荐 ───── 杀号大底 ───── 开始组号
 多视图信号       号码-统计数         6个红点位        存活红+蓝        汇总可用项
```

轨道表达数据汇聚关系，不是强制步骤。四张卡无论有没有数据都能点击：有数据时进入查看或重做，无数据时进入对应页面完成操作。

### 4.2 数据来源与就绪判定

| 数据来源 | 现有接口/模型 | 本模块用途 | 缺失时行为 |
|---|---|---|---|
| 走势图 | `IDataService`、遗漏矩阵、斜连分析 | 五类视图的冷号和预测斜连候选 | 无数据时跳过走势规则；仍可点击 |
| 复式票统计 | `ITicketRepository` / `Ticket` | 红球 01–33、蓝球 01–16 与各自统计数；统计数越小，规则信号越强 | 无数据时跳过反向统计规则；仍可点击 |
| 点位预测 | `IPositionPredictor.Predict` / `PositionPrediction.RedPoints` | 只取 6 个红球点位，不使用蓝球输出 | 无数据时跳过点位规则；仍可点击 |
| 杀号引擎 | `IKillEngine.Execute` / `KillReport` | 杀号后剩余的红球大底和蓝球大底 | 无数据时使用双色球全集作为候选池；仍可点击 |

组号服务只能依赖接口和领域模型。页面之间继续通过 `MainWindow` 导航，但页面导航不是数据依赖。

### 4.3 站点状态

```csharp
public enum UpstreamReadinessState
{
    Ready,
    Missing,
    Stale,
    IssueMismatch,
    Failed,
    Unavailable
}

public enum UpstreamModule
{
    Trend,
    TicketStats,
    Position,
    Kill
}

public sealed record UpstreamModuleStatus(
    UpstreamModule Module,
    int TargetIssue,
    int RequiredAsOfIssue,
    UpstreamReadinessState State,
    string Title,
    string Summary,
    string ActionLabel,
    DateTimeOffset? GeneratedAt,
    string? ArtifactId,
    string? VersionId,
    IReadOnlyList<string> Details);
```

状态显示规则：

| 状态 | 用户看到的文案 | 卡片操作 | 是否参与本次组号 |
|---|---|---|---|
| `Ready` | 有本期数据，并展示摘要 | 查看 / 重新生成 | 是 |
| `Missing` | 本期暂无数据 | 去生成 | 否，自动跳过 |
| `Stale` | 数据基于旧截止期或旧规则 | 查看 / 更新 | 默认不参与 |
| `IssueMismatch` | 当前数据属于另一期 | 查看 / 切换到本期 | 否 |
| `Failed` | 上次生成失败及具体原因 | 查看错误 / 重试 | 否 |
| `Unavailable` | 前置条件不足 | 前往对应页面 | 否 |

`CanGenerate` 不再由 4/4 状态决定，只要票型、注数和预算等请求本身合法即可。每次开始组号时冻结所有 Ready 数据，生成 `UsedSources` 和 `SkippedSources`；运行过程中不能因为其他页面随后产生新数据而改变本次结果。

### 4.4 每个站点展示什么

#### 走势图站点

- 卡片显示一个粗略版走势：最近 5 期红蓝球小行、数据截止期、五个子视图的数据状态；
- 摘要示例：`5 个视图 · 冷号 12 个 · 斜连候选 7 个`；
- 点击后打开完整走势图，并保留目标期和历史窗口；
- 组号使用的不是粗略画面，而是 `TrendSignalSnapshot`。

走势图必须给组号以下五类视图数据：

| 视图 | 数据范围 | 输出 |
|---|---|---|
| 基本走势图 | 目标期之前最近 N 期 | 红蓝冷号、红蓝预测斜连 |
| 历史同期图 | 历史上期号后三位与目标期相同、且早于目标期的记录 | 红蓝冷号、红蓝预测斜连 |
| 012 路图 | 最近 N 期，按 0/1/2 路列序分析 | 红蓝冷号、红蓝预测斜连 |
| 奇偶图 | 与目标期号奇偶一致的最近 N 条记录 | 红蓝冷号、红蓝预测斜连 |
| 周期图 | 与目标期开奖星期一致的最近 N 条记录 | 红蓝冷号、红蓝预测斜连 |

历史目标期直接使用该期 `DrawDate.DayOfWeek`；未来目标期由 `IIssueResolver` 根据双色球周二/周四/周日开奖节奏给出预计星期，不能由页面当前下拉框状态猜测。每个视图单独记录样本数和可用状态，某个子视图样本不足时只跳过该子视图，不使整个走势图站点失效。

当前冷号规则沿用界面已有定义：红球末行遗漏值 `>= 10`，蓝球末行遗漏值 `>= 16`。阈值以后进入规则配置，不散落在页面代码里。

当前 `DiagonalChainRenderer` 只负责画线，无法提供组号数据。应把 `FindChains` 提取为不依赖 WPF 的 `IDiagonalChainAnalyzer`，返回预测号码及证据，再由 renderer 消费同一结果。每条斜连证据至少包含视图类型、球类型、预测号码、层级、间隔、步长和链长。

012 路视图的斜连按 012 列序计算，不能直接复用基本走势图的结果冒充 012 结果。五个视图命中同一号码时保留来源集合，由后续组号规则决定如何合并，不能简单重复加五次分。

#### 复式票统计站点

- 状态标题：`复式票统计 · 已就绪`；
- 摘要：`2026079 期 · 36 张样本票`；
- 预览：低统计红球、低统计蓝球及其统计数；
- Ready 判定：彩种为 SSQ、文件期号等于目标期、票数大于 0，并已产生统计快照；
- 点击后：打开复式票统计，优先加载该目标期文件，不得自动换成“最新文件”。

组号读取完整的号码-统计数映射，不只读取“低于平均值”的号码列表：

```csharp
public sealed record TicketNumberStat(int Ball, int Count, double ReverseRank);

public sealed record TicketStatsArtifact(
    int TargetIssue,
    int TicketCount,
    IReadOnlyList<TicketNumberStat> RedStats,
    IReadOnlyList<TicketNumberStat> BlueStats,
    double RedAverageAllBalls,
    double BlueAverageAllBalls,
    string SourceFileHash);
```

用户确定的判断方向为：**统计数越小，组号规则越看好该号码**。先生成稳定的反向排名数据：对不同统计数做升序 dense rank，同一统计数得到相同 `ReverseRank`，球号只用于展示排序。若有多个不同统计数，`ReverseRank = 1 - rankIndex / (distinctCount - 1)`；若全部统计数相同，统一取中性值 `0.5`。具体加多少分、选几个号码留到最终规则阶段确定。

统计为 0 的号码属于有效数据，不得像当前 UI 均值计算那样直接从序列中删除。组号快照中的红球均值按 33 个号码、蓝球均值按 16 个号码计算，并保留完整统计数组；界面若继续使用“非零号码均值”，必须明确标注，不能与组号均值混为同一字段。

#### 点位站点

- 状态标题：`点位推荐 · 已就绪`；
- 摘要：`6 个点位 · 03 08 15 22 27 31`；
- 预览：仅展示并输出 6 个红球点位；
- Ready 判定：`Issue == TargetIssue`、`AsOfIssue == RequiredAsOfIssue`，且规则版本与当前正式版本一致；
- 点击后：打开点位页并选中目标期。

组号模块暂不读取 `SingleBlue`、`DoubleBlue` 或 `TripleBlue`。点位正式前向验证账本是不可变研究证据，仪表盘不得把它改造成可覆盖的 UI 缓存。点位页面完成生成后，应另存只包含 6 个 `RedPoints` 和版本信息的 `PositionArtifact`；账本冻结逻辑保持原样。

#### 杀号站点

- 状态标题：`杀号大底 · 已就绪`；
- 摘要：`红球大底 26 个 · 蓝球大底 12 个`；
- 预览：完整存活红球和存活蓝球；
- Ready 判定：`TargetPeriod == TargetIssue`、历史截止期一致，且规则集哈希仍为当前启用规则集；
- 点击后：打开杀号页并定位目标期，再执行或查看报告。

若杀号数据 Ready，`RecommendedRedBalls` 和 `RecommendedBlueBalls` 直接成为本次候选大底，组号不得重新加入已杀号码。若杀号数据缺失，则候选大底回退为红球 01–33、蓝球 01–16，并在结果中注明“未使用杀号大底”。若存活红球少于 6 个或存活蓝球为 0，视为无效产物，不参与本次组号，回退全集并显示错误。

当前 `KillPage` 只把最后一次报告放在内存并用 MessageBox 展示，无法支持跨页面和重启后的按期监控。需要新增持久化的 `KillArtifact`，不能仅检查 `_lastReport != null`。

### 4.5 上游产物目录

```text
data/artifacts/
  trend/
    2026079.json
  ticket-stats/
    2026079.json
  position/
    2026079_{ruleVersionId}.json
  kill/
    2026079_{ruleSetHash}.json
```

每份产物包含：

- `SchemaVersion`；
- `ArtifactId`；
- `Module`；
- `TargetIssue`；
- `AsOfIssue`；
- `GeneratedAtUtc`；
- 输入快照哈希；
- 模块规则版本或规则集哈希；
- 供组号使用的冻结结果；
- 展示摘要。

文件使用临时文件 + 原子替换。目标期相同但规则版本不同的产物可以并存；仪表盘只把当前正式版本标为 Ready，旧版本显示 Stale。

### 4.6 点击导航与返回

当前 `MainWindow.Nav_Click` 是私有事件处理器，子页面不能安全复用。建议抽取统一方法：

```csharp
public sealed record PageNavigationContext(
    string TargetPage,
    int? TargetIssue = null,
    int? HistoryWindow = null,
    string? ReturnPage = null);

public interface INavigationAwarePage
{
    void ApplyNavigationContext(PageNavigationContext context);
}
```

`MainWindow` 内部新增 `NavigateTo(PageNavigationContext context)`，侧栏点击和仪表盘点击都调用它。`GroupPage` 通过 `NavigateRequested` 路由事件提出导航请求，不直接寻找或操作其他页面控件。

页面行为：

- 走势图：应用历史窗口，并滚动到最新截止期；
- 复式票统计：切换 SSQ，尝试加载指定期号；找不到文件时停留在导入入口；
- 点位：选中指定期号，但由用户明确点击“生成点位”；
- 杀号：显示目标期和所需截止期；执行后保存报告产物；
- 上游页面顶部显示 `← 返回 2026079 期组号仪表盘`；
- 返回后 `GroupPage` 重新查询状态，不依赖旧页面实例里的临时字段。

期号上下文必须贯穿整个往返流程。禁止点击缺失卡后跳到上游页，却让上游页自动选择另一期。

### 4.7 上游接口必须补齐的能力

单靠页面导航不足以实现按期号监控，以下接口能力需要同步补齐。

#### 统一期号解析

新增 `IIssueResolver`，集中提供：

```csharp
public interface IIssueResolver
{
    IReadOnlyList<int> GetTargetIssueOptions();
    int GetRequiredAsOfIssue(int targetIssue);
    bool IsHistoricalIssue(int targetIssue);
}
```

点位、杀号、组号共同使用该服务。禁止各模块分别用 `latest + 1` 推算下一期；现有 `KillEngine.ComputeTargetPeriod` 需要迁移到统一解析逻辑，尤其要覆盖年度切换。

#### 复式票统计按期加载

`ITicketRepository` 增加按彩种和期号加载，而不是只提供 `LoadLatestData()`：

```csharp
bool TryLoadIssueData(LotteryType type, int issue, out string? error);
TicketStatsArtifact BuildStatsArtifact(int issue);
```

若指定期文件不存在，页面保持 SSQ 和目标期上下文，显示导入/生成入口，不能回退加载另一最新期并将其显示成当前期。

#### 点位产物发布

`PositionPage` 对指定期成功生成 `PositionPrediction` 后，把必要字段复制为 `PositionArtifact` 并写入产物仓储。该操作不更改、覆盖或删除 `IPositionValidationStore` 中的冻结记录。

#### 杀号按目标期执行

当前 `IKillEngine.Execute()` 只构建最新上下文。增加显式请求：

```csharp
public sealed record KillExecutionRequest(
    int TargetIssue,
    int RequiredAsOfIssue,
    IReadOnlyList<string>? RuleIds = null);

KillReport Execute(KillExecutionRequest request);
```

`IRuleContextBuilder` 相应增加按截止期构建上下文的能力。历史回放时只能读取 `TargetIssue` 之前的数据；运行成功后保存 `KillArtifact`。旧无参 `Execute()` 可保留为“最新期快捷执行”，但仪表盘不得调用它。

## 5. 总体架构

```text
IDataService ───────────────┐
IPositionPredictor ─────────┤
IKillEngine ────────────────┼─> ICombinationInputProvider
ITicketRepository ──────────┘           │
                                        ▼
                             CombinationInputSnapshot
                                        │
                    ┌───────────────────┴───────────────────┐
                    ▼                                       ▼
          ICombinationRuleSetProvider              请求/预算校验
                    │                                       │
                    └───────────────────┬───────────────────┘
                                        ▼
                              ICombinationService
                     候选池 -> 剪枝 -> 评分 -> 多样化
                                        │
                                        ▼
                              CombinationRunResult
                           ┌────────────┴────────────┐
                           ▼                         ▼
                      GroupPage              ICombinationRunStore
```

仪表盘状态不由 `GroupPage` 自己拼凑，增加独立的只读聚合服务：

```text
ITrendInputAdapter ───────────┐
ITicketStatsInputAdapter ─────┤
IPositionInputAdapter ────────┼─> ICombinationReadinessService
IKillInputAdapter ────────────┘              │
                                             ├─> UpstreamDashboardSnapshot
                                             └─> BuildInputSnapshot（采集所有 Ready 项）
```

四个 adapter 同时负责两件事：返回当前状态；在 Ready 时提供冻结输入。这样状态检查和真正组号读取使用同一套判定，不会出现“卡片显示已就绪，但生成时拿不到数据”。

建议新增目录：

```text
Models/
  CombinationModels.cs
Services/Combination/
  ICombinationReadinessService.cs
  CombinationReadinessService.cs
  IUpstreamInputAdapter.cs
  TrendInputAdapter.cs
  TicketStatsInputAdapter.cs
  PositionInputAdapter.cs
  KillInputAdapter.cs
  IAnalysisArtifactStore.cs
  JsonAnalysisArtifactStore.cs
  ICombinationInputProvider.cs
  CombinationInputProvider.cs
  ICombinationService.cs
  CombinationService.cs
  ICombinationRuleSetProvider.cs
  JsonCombinationRuleSetProvider.cs
  ICombinationRunStore.cs
  JsonCombinationRunStore.cs
  CombinationCostCalculator.cs
  CombinationFeatureCalculator.cs
  CombinationPortfolioSelector.cs
Resources/
  combination-rules.json
Verification/
  CombinationVerification.cs
```

不建议把新逻辑继续堆入 `AnalysisService`。该服务已经同时承担多种统计职责，且当前组号方法是待替换的占位实现。

## 6. 输入快照

### 6.1 目标期号

目标期号必须由点位预测器的 `GetIssueOptions()` 或用户明确选择产生，不能简单执行 `latestPeriod + 1`，以免跨年度期号错误。

默认选择：

1. 优先选择 `GetIssueOptions()` 中尚未开奖且最接近当前数据末期的期号；
2. 若只选择历史期，则以 `backtest` 模式构建快照并显示实际开奖号；
3. 点位预测、杀号报告、复式票样本的期号必须与目标期一致，否则标记 `IssueMismatch`，不静默混用。

### 6.2 数据状态

输入快照构建沿用 4.3 节的 `UpstreamReadinessState`，不再维护第二套含义相近的状态枚举。仪表盘与快照构建必须基于同一次 `UpstreamDashboardSnapshot`，避免两次查询之间上游数据发生变化。

| 输入 | Ready 判定 | Stale / Mismatch 判定 |
|---|---|---|
| 历史走势 | 至少有足够窗口且末期与快照声明一致 | 数据已更新但快照仍引用旧末期 |
| 复式票统计 | SSQ 统计产物属于目标期且样本数大于 0 | 彩种、期号或源文件哈希不一致 |
| 点位预测 | `Issue == TargetIssue` 且 `AsOfIssue` 等于历史末期 | 目标期、历史截止期或正式规则版本不一致 |
| 杀号报告 | `TargetPeriod == TargetIssue` 且规则集哈希一致 | 目标期、历史截止期或启用规则集不一致 |

`Build` 只采集 Ready 项。Missing、Stale、IssueMismatch、Failed 和 Unavailable 项写入 `SkippedSources` 与诊断信息，但不阻止生成。杀号缺失或产物无效时，候选池回退到双色球全集。

### 6.3 快照模型

```csharp
public sealed record CombinationInputSnapshot(
    string SnapshotId,
    int TargetIssue,
    int AsOfIssue,
    int HistoryWindow,
    DateTimeOffset CreatedAt,
    TrendSignalSnapshot? Trend,
    TicketStatsArtifact? TicketStats,
    PositionArtifact? Position,
    KillPoolArtifact? KillPool,
    IReadOnlyList<UpstreamModule> UsedSources,
    IReadOnlyList<UpstreamModule> SkippedSources,
    IReadOnlyList<InputDiagnostic> Diagnostics);

public enum TrendViewKind
{
    Basic,
    HistoricalSameIssue,
    Route012,
    TargetIssueParity,
    DrawCycle
}

public sealed record DiagonalCandidate(
    int Ball,
    BallType BallType,
    int Level,
    int Gap,
    int Step,
    int ChainLength);

public sealed record TrendViewSignal(
    TrendViewKind View,
    int SampleCount,
    bool IsUsable,
    string? SkipReason,
    IReadOnlyList<int> ColdReds,
    IReadOnlyList<int> ColdBlues,
    IReadOnlyList<DiagonalCandidate> DiagonalCandidates);

public sealed record TrendSignalSnapshot(
    int TargetIssue,
    int AsOfIssue,
    IReadOnlyList<TrendPreviewRow> PreviewRows,
    IReadOnlyList<TrendViewSignal> Views,
    string ConfigVersionId);

public sealed record TrendPreviewRow(
    int Issue,
    IReadOnlyList<int> Reds,
    int Blue);

public sealed record PositionArtifact(
    int TargetIssue,
    int AsOfIssue,
    string RuleVersionId,
    string RunId,
    IReadOnlyList<int> RedPoints);

public sealed record KillPoolArtifact(
    int TargetIssue,
    int AsOfIssue,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<int> SurvivingReds,
    IReadOnlyList<int> SurvivingBlues,
    string RuleSetHash);
```

四项输入有意允许为空；nullable 在这里表达“本次未使用该模块”，不是 schema 兼容妥协。`UsedSources` 必须与实际非空字段严格一致。

`SnapshotId` 使用规范化 JSON 的 SHA-256。规范化内容只包含本次实际使用的走势信号、复式票完整统计栏、6 个点位、杀号红蓝大底、规则版本和请求，不包含 Missing 模块的旧数据，也不包含 `CreatedAt` 等非业务字段。

## 7. 生成请求与输出模型

### 7.1 请求模型

```csharp
public enum CombinationFormat
{
    Single,
    Compound,
    Dantuo
}

public sealed record CombinationGenerateRequest
{
    public required int TargetIssue { get; init; }
    public int HistoryWindow { get; init; } = 100;
    public CombinationFormat Format { get; init; } = CombinationFormat.Single;
    public int PlanCount { get; init; } = 5;
    public int RedCount { get; init; } = 6;
    public int BlueCount { get; init; } = 1;
    public int DanCount { get; init; }
    public int TuoCount { get; init; }
    public int Multiplier { get; init; } = 1;
    public int? BudgetFen { get; init; }
    public IReadOnlySet<int> LockedReds { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> ExcludedReds { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> LockedBlues { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> ExcludedBlues { get; init; } = new HashSet<int>();
    public int? RandomSeed { get; init; }
    public int MaxEvaluations { get; init; } = 100_000;
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromSeconds(2);
}
```

### 7.2 运行与方案模型

```csharp
public sealed record CombinationRunResult(
    string RunId,
    string SnapshotId,
    string RuleVersionId,
    int RandomSeed,
    CombinationGenerateRequest Request,
    IReadOnlyList<CombinationPlan> Plans,
    IReadOnlyList<InputDiagnostic> Diagnostics,
    GenerationMetrics Metrics);

public sealed record CombinationPlan
{
    public required string PlanId { get; init; }
    public required CombinationFormat Format { get; init; }
    public IReadOnlyList<SingleBet> SingleBets { get; init; } = Array.Empty<SingleBet>();
    public IReadOnlyList<int> CompoundReds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> CompoundBlues { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> DanReds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> TuoReds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> Blues { get; init; } = Array.Empty<int>();
    public int BaseBetCount { get; init; }
    public int UnitPriceFen { get; init; } = 200;
    public int Multiplier { get; init; } = 1;
    public int CostFen { get; init; }
    public double? StrategyScore { get; init; }
    public IReadOnlyList<ScoreBreakdown> ScoreBreakdown { get; init; } = Array.Empty<ScoreBreakdown>();
    public IReadOnlyList<PlanConflict> Conflicts { get; init; } = Array.Empty<PlanConflict>();
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}

public sealed record SingleBet(IReadOnlyList<int> Reds, int Blue);
```

字段名使用 nullable `StrategyScore`。只有正式规则包含可归一化评分项时界面才显示“策略分”；规则仍为空时显示“仅基础规则”，不得伪造 0 分或 100 分。任何情况下都不得显示为“中奖概率”“预测概率”或无定义的“置信度”。

## 8. 规则体系

### 8.1 硬约束

| 编号 | 规则 | 说明 |
|---|---|---|
| H001 | 双色球范围 | 红球 1–33，蓝球 1–16 |
| H002 | 数量合法 | 基础注必须为 6 红 + 1 蓝 |
| H003 | 红球不重复 | 单注红球互异并升序 |
| H004 | 胆拖互斥 | 胆码和拖码不得重复 |
| H005 | 胆码数量 | 1–5 个；`DanCount + TuoCount >= 6` |
| H006 | 用户排除 | 被用户显式排除的号码不得出现 |
| H007 | 用户锁定 | 锁定号码必须出现，且不能同时被排除 |
| H008 | 预算上限 | 方案成本不得超过请求预算 |
| H009 | 运行上限 | 达到取消、超时或评估数上限立即停止继续枚举 |
| H010 | 杀号大底 | 杀号产物可用时，所有候选必须来自存活红蓝大底 |

若用户锁定了杀号大底之外的号码，请求直接提示冲突并要求用户先取消锁定或明确停用本次杀号数据，不能静默把已杀号码加回大底。

### 8.2 四类输入如何进入规则

组号引擎先把上游数据转换成号码级事实，再交给规则评估。这里先固定数据方向，不提前固定最终规则和权重。

| 数据 | 固定预处理 | 留给最终规则决定 |
|---|---|---|
| 走势图 | 汇总五个视图中每个号码的冷号来源、斜连来源和证据链 | 命中几个视图才加分、冷号与斜连各自权重、是否要求组合覆盖 |
| 复式票统计 | 为 01–33、01–16 保存原始统计数和反向排名；数越小排名越高 | 取低统计前几名、分档方式、分值和组合配额 |
| 点位 | 对红球标记 `IsPositionPoint`，数据恰好为 6 个点位 | 一注包含几个点位、是否设置最低/最高数量、分值 |
| 杀号 | 有数据时直接建立红蓝候选大底；无数据时使用双色球全集 | 大底本身不打分，是否允许用户显式停用该数据 |

同一号码在多个走势图中出现时，事实模型保留来源集合和证据数量。规则必须显式说明是按“不同视图数”还是“斜连链条数”计分，防止同一底层记录被重复放大。

### 8.3 数据交集与冲突

冲突处理遵循：

1. 合法性约束永远最高；
2. 杀号大底可用时先限定候选池；
3. 走势冷号、走势斜连、复式票低统计号和点位只在候选大底内部生效；
4. 点位号码不在杀号大底时，记录为“点位被大底排除”，不重新加入；
5. 用户显式排除永远不被任何上游信号重新加入；
6. 用户显式锁定与杀号大底冲突时阻止生成并显示具体号码；
7. 方案解释同时展示使用来源和被过滤来源，不能只展示最终号码。

```csharp
public sealed record PlanConflict(
    int Ball,
    string ConflictType,
    string PositiveSource,
    string NegativeSource,
    string Resolution,
    IReadOnlyList<string> Evidence);
```

组号只消费杀号后的红蓝大底，不再二次使用 `ConfidenceLevel`、被杀规则数量或回测准确率评分，避免把杀号规则重复计算两次。

### 8.4 规则配置

具体组号规则在项目后期完善。当前阶段先固定可扩展接口和配置版本，避免把暂定权重写死在业务代码中。

```csharp
public interface ICombinationRule
{
    string RuleId { get; }
    RuleEvaluation Evaluate(
        CombinationCandidate candidate,
        CombinationInputSnapshot snapshot,
        CombinationRuleContext context);
}

public sealed record RuleEvaluation(
    string RuleId,
    bool Applied,
    double RawScore,
    double WeightedScore,
    IReadOnlyList<string> Reasons);
```

规则配置保存为嵌入资源 `Resources/combination-rules.json`，启动时可复制到 `data/combination/combination-rules.json` 作为用户覆盖版本。当前只确定数据方向：

```json
{
  "schemaVersion": 1,
  "ruleVersionId": "combination-draft-v1",
  "sources": {
    "trend": { "enabled": true, "rules": [] },
    "ticketStats": {
      "enabled": true,
      "direction": "lower-count-is-better",
      "rules": []
    },
    "position": { "enabled": true, "expectedPointCount": 6, "rules": [] },
    "killPool": { "enabled": true, "mode": "candidate-pool" }
  },
  "portfolio": {
    "maxSharedReds": 4,
    "duplicatePenalty": 12
  }
}
```

规则为空时仍可执行基础合法组号，但结果必须标记“当前仅应用票面规则，具体组号规则尚未配置”。配置读取失败时回退到嵌入默认值并显示诊断，不允许静默使用半解析配置。

## 9. 生成算法

### 9.1 确定性要求

若用户没有指定种子，使用以下内容派生：

```text
seed = FirstInt32(SHA256(snapshotId + ruleVersionId + canonicalRequest))
```

所有排序必须有稳定的次级键：总分降序后按红球字典序、蓝球升序排序。禁止在正式生成路径使用 `Random.Shared`。

### 9.2 候选池与号码事实

先确定候选大底：

```text
redPool  = KillPool?.SurvivingReds  ?? [01..33]
bluePool = KillPool?.SurvivingBlues ?? [01..16]
```

再为大底内每个号码构建事实，不在这一阶段写死加减分：

- 五个走势视图中是否为冷号、出现在哪些视图；
- 五个走势视图中有哪些斜连证据；
- 复式票原始统计数和反向排名；
- 是否属于 6 个红球点位；
- 是否被用户锁定或排除。

不固定“红球 Top 16、蓝球 Top 6”。最终规则可以配置预选池大小，也可以直接在完整杀号大底上搜索；具体值随最终规则版本保存。

### 9.3 基础组合枚举

生成器根据规则是否已经完善选择路径：

- 已配置正式规则：使用确定性 beam search 或回溯 + 剪枝，按部分组合的规则上界保留候选；
- 规则仍为空：使用固定种子的确定性抽样生成合法且多样的基础方案，不执行无法解释的伪评分；
- 任一路径都受 `MaxEvaluations`、`MaxDuration` 和取消令牌约束。

正式规则路径：

1. 按号码升序选择 6 个红球；
2. 在部分组合阶段检查剩余槽位、用户锁定可达性和已配置规则的可达上界；
3. 完整红球组合后计算四类数据的规则结果；
4. 与蓝球候选组合成基础注；
5. 计算规则明细和总分；
6. 用有界最小堆保留候选 Top N，不保存全部对象；
7. 每批检查取消令牌、截止时间和评估计数。

双色球全集共有 `C(33,6) × 16` 个基础组合，不能全量物化。杀号大底较大或缺失时必须依赖有界搜索/确定性抽样；不能为了性能偷偷丢弃低排名号码而不在规则和运行记录中说明。

### 9.4 多样化选择

单纯取分数最高的 Top K 容易得到只差一个号码的方案。最终方案使用贪心组合包选择：

```text
adjustedScore = strategyScore
              - duplicatePenalty × maxSimilarity(selectedPlans, candidate)
```

上述公式只在正式规则产生 `StrategyScore` 时使用。规则为空时按固定种子依次选取满足多样性约束的合法组合，不生成虚假分数。红球重合默认不超过 4 个；若约束过紧导致数量不足，可逐级放宽并在 `Diagnostics` 中说明，不能悄悄复制相似方案凑数。

### 9.5 不同票型

#### 单式

- 从多样化基础注中选择 `PlanCount` 注；
- 每注 6 红 + 1 蓝；
- 成本为 `PlanCount × 200 × Multiplier` 分。

#### 复式

- 从高分基础注和号码级分数中构造指定数量的红、蓝集合；
- 评分取展开后基础注的加权平均，并单列覆盖度；
- 若展开注数超过配置上限，不实际展开全部对象，使用组合数学和流式抽样评估；
- 基础注数为 `C(RedCount, 6) × BlueCount`。

#### 胆拖

- 胆码按最终规则的号码事实和组合贡献选择；
- 6 个点位只是可用事实之一，不自动成为胆码；
- 拖码补足结构覆盖和方案多样性；
- 基础注数为 `C(TuoCount, 6 - DanCount) × BlueCount`；
- 胆码与拖码互斥，胆码数必须为 1–5。

成本统一使用整数分：

```text
costFen = baseBetCount × 200 × multiplier
```

## 10. 服务接口与依赖注入

```csharp
public sealed record UpstreamDashboardSnapshot(
    int TargetIssue,
    int RequiredAsOfIssue,
    IReadOnlyList<UpstreamModuleStatus> Modules,
    IReadOnlyList<UpstreamModule> ReadyModules,
    IReadOnlyList<UpstreamModule> SkippedModules);

public interface ICombinationReadinessService
{
    Task<UpstreamDashboardSnapshot> GetAsync(
        int targetIssue,
        int historyWindow,
        CancellationToken cancellationToken = default);

    CombinationInputSnapshot BuildSnapshot(
        UpstreamDashboardSnapshot dashboard,
        CancellationToken cancellationToken = default);
}

public interface IAnalysisArtifactStore
{
    event Action<UpstreamModule, int>? ArtifactChanged;
    Task<T?> GetAsync<T>(UpstreamModule module, int issue, string versionId,
        CancellationToken cancellationToken = default);
    Task SaveAsync<T>(UpstreamModule module, int issue, string versionId, T artifact,
        CancellationToken cancellationToken = default);
}

public interface ICombinationInputProvider
{
    CombinationInputSnapshot Build(
        CombinationGenerateRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICombinationService
{
    Task<CombinationRunResult> GenerateAsync(
        CombinationInputSnapshot snapshot,
        CombinationGenerateRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICombinationRuleSetProvider
{
    CombinationRuleSet GetCurrent();
}

public interface ICombinationRunStore
{
    Task SaveAsync(CombinationRunResult result, CancellationToken cancellationToken = default);
    Task<CombinationRunResult?> GetAsync(string runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CombinationRunHeader>> ListAsync(int? issue = null,
        CancellationToken cancellationToken = default);
}
```

`App.xaml.cs` 注册建议：

```csharp
services.AddSingleton<ICombinationRuleSetProvider, JsonCombinationRuleSetProvider>();
services.AddSingleton<IAnalysisArtifactStore, JsonAnalysisArtifactStore>();
services.AddSingleton<ICombinationReadinessService, CombinationReadinessService>();
services.AddSingleton<ICombinationInputProvider, CombinationInputProvider>();
services.AddSingleton<ICombinationService, CombinationService>();
services.AddSingleton<ICombinationRunStore, JsonCombinationRunStore>();
```

`GroupPage` 以 `ICombinationReadinessService` 为首要依赖，并订阅 `IAnalysisArtifactStore.ArtifactChanged`。现有 `IAnalysisService.GetGroupRecommendations` 标记过时，过渡一个版本后删除。

## 11. 持久化与版本

沿用项目的便携式 JSON/文本文件方案，不新增 SQLite：

```text
data/combination/
  combination-rules.json
  runs/
    2026079/
      {runId}.json
```

运行记录至少保存：

- schema 版本；
- 目标期和历史截止期；
- 输入 `SnapshotId`；
- 点位、杀号、组号规则版本或哈希；
- 完整请求和实际种子；
- 输出方案、评分明细、冲突、诊断和耗时；
- 应用版本。

保存使用项目现有的临时文件 + 原子替换模式。读取损坏文件时返回明确错误，不覆盖原文件。

V1 不保存完整历史开奖记录副本，只保存足以验证快照的一致性哈希和上游冻结输出。若未来要求完全离线重放，再升级 schema 保存压缩后的历史输入。

## 12. WPF 页面设计

### 12.1 页面布局

在现有 `GroupPage` 和导航位置上原地升级，不新增顶层页面。首屏同时展示期号轨道、粗略走势和当前可用信号；组号设置始终可操作：

```text
┌────────────────────────────────────────────────────────────────────────────┐
│ 组号工作台                                      目标期 [2026079 ▼] [刷新] │
│ 先完成本期分析资料，再冻结输入并生成组合                                  │
├────────────────────────────────────────────────────────────────────────────┤
│ 2026079 期数据轨道                                      3 项可用 · 1 项无数据 │
│                                                                            │
│   ●━━━━━━━━━━━━●━━━━━━━━━━━━●━━━━━━━━━━━━○                                │
│   走势           复式票统计       点位推荐        杀号报告                 │
│                                                                            │
│ ┌──────────────────────────┐ ┌────────────┐ ┌────────────┐ ┌───────────┐│
│ │ 粗略走势 · 最近 5 期    │ │复式票统计  │ │6 个点位    │ │杀号大底   ││
│ │ 074 03 08 15 22 27 31+09│ │36 张样本票 │ │03 08 15    │ │暂无数据   ││
│ │ 075 05 11 18 24 29 32+07│ │低统计红14 │ │22 27 31    │ │           ││
│ │ …                        │ │低统计蓝 6 │ │            │ │去执行  →  ││
│ │ 5视图 冷号12 斜连7  →   │ │查看统计 → │ │查看点位 →  │ │           ││
│ └──────────────────────────┘ └────────────┘ └────────────┘ └───────────┘│
│                                                                            │
│ 本次将使用：走势、复式票统计、点位；未使用：杀号大底                      │
├────────────────────────────────────────────────────────────────────────────┤
│ 组号设置                                                                  │
│ 票型 [单式▼]  注数 [5]  历史窗口 [100]  预算 [20.00]  倍数 [1]             │
│ [高级约束]                                         [使用 3 项数据开始组号]│
├────────────────────────────────────────────────────────────────────────────┤
│ 结果区域：显示实际使用的数据、跳过的数据、规则版本和推荐方案               │
└────────────────────────────────────────────────────────────────────────────┘
```

当四项都没有数据时，按钮仍可用，但文案和提示改为：

```text
当前没有上游数据，将只应用票面合法性和已配置组号规则   [按基础规则组号]
```

生成后，仪表盘保留在顶部但压缩为一行，给结果区释放空间：

```text
2026079 · 使用 3 项 · 跳过杀号 · 快照 9A71C2 · 规则 draft-v1   [展开]
```

结果卡继续展示号码、成本、策略分、解释和冲突，但不能把上游仪表盘挤出页面，用户随时能确认方案使用了哪一期、哪一版数据。

### 12.2 交互

- 进入页面或切换目标期时只执行状态查询，不自动运行点位、杀号等重计算；
- 四个站点始终可点击；Ready 状态进入“查看/重做”，非 Ready 状态进入“完成本期数据”；整个卡片和明确按钮具有相同行为；
- 点击站点时携带目标期、历史窗口和返回地址；
- 从上游页面返回、上游产物变更、历史数据更新、票文件变化时自动刷新仪表盘；
- 仪表盘刷新期间保留旧内容并显示轻量进度，不让四张卡闪成空白；
- “开始组号”始终按请求合法性启用，不受上游数据数量限制；按钮文案动态显示“使用 N 项数据开始组号”；
- 点击“开始组号”冻结当前所有 Ready 项，缺失项只写入跳过清单；
- 切换票型后只显示对应规格；
- 输入预算后实时显示最大可生成注数，但最终成本仍由服务端模型校验；
- “高级约束”提供锁定/排除红蓝球，不在主界面堆满 49 个按钮；
- 点击“开始组号”启动异步任务，按钮变为“取消”，页面保持可响应；
- 生成期间显示已评估数量和耗时，不伪造百分比；
- 方案默认展示摘要，展开后显示完整评分明细、冲突证据和数据版本；
- 右键或按钮支持复制当前方案；
- 导出复用现有导出风格，首期优先文本/CSV，图片导出后续接入；
- 历史期回放时并排显示实际开奖号，但不得参与该期生成输入。

键盘与可访问性：

- Tab 顺序按“期号 → 四个站点 → 组号设置 → 开始组号 → 结果”排列；
- 卡片使用真实 Button 或可访问的 Button 模板，不能只给 Border 绑定鼠标事件；
- Enter/Space 可进入站点，Esc 可取消生成；
- 状态同时使用图标、文字和颜色；
- 焦点框清晰可见，进度和错误通过自动化属性播报。

### 12.3 视觉规范

视觉主题采用“轻量分析台”：浅灰底、白色数据面、清晰的轨道线和少量双色球红蓝提示。记忆点只放在“期号分析轨道”这一处，结果卡和设置区保持克制。

字体角色：

- 页面标题：`Segoe UI Variable Display`，22px / SemiBold；
- 正文和按钮：`Microsoft YaHei UI`，12–14px；
- 期号、号码、版本和哈希：`Cascadia Mono`，11–14px；缺失时回退 `Consolas`。

继续复用 `BgContent`、`BgSurface`、`TextPrimary`、`TextTertiary`、`PrimaryButton` 等全局资源，并补充紧凑语义令牌：

| 令牌 | 色值 | 用途 |
|---|---|---|
| `DashboardRailBrush` | `#B8C2CC` | 未完成轨道和卡片分隔 |
| `StatusReadyBrush` | `#1C8B67` | 已就绪节点、勾选和完成轨道 |
| `StatusPendingBrush` | `#C5842B` | 待生成、已过期和需更新 |
| `StatusErrorBrush` | `#C34F52` | 失败或期号冲突 |
| `DashboardBlueBrush` | `#376FA6` | 期号选择、链接和主要操作 |
| `DashboardSoftBrush` | `#EEF3F6` | 轨道区域的安静底色 |

已有 `RedBall`、`BlueBall` 只用于号码球，不把整张卡染红或染蓝。卡片状态主要通过左上状态标签、轨道节点和 2px 顶边表达，避免大面积交通灯色块。

- `ConflictBrush`：方案中存在相反证据；
- `ScorePositiveBrush` / `ScoreNegativeBrush`：评分加减项；
- `LockedBallBorderBrush`：用户锁定；
- `KilledBallBorderBrush`：杀号证据。

布局使用 16px 页面边距、12px 区块间距、10–12px 卡片内边距、4px 圆角。轨道卡等宽，窗口变窄时改为两列并让轨道折行为 2×2；最窄宽度下改为纵向步骤，不允许横向裁掉操作按钮。

## 13. 错误、并发与性能

### 13.1 错误分类

| 错误 | 行为 |
|---|---|
| 无历史数据或窗口不足 | 跳过走势图信号，仍可基础组号，并提供前往开奖记录/同步数据入口 |
| 点位、杀号生成失败 | 保持对应站点为 Failed，本次跳过该数据并显示错误来源 |
| 复式票期号不匹配 | 标记 IssueMismatch，显示目标期和样本期并提供“加载本期” |
| 锁定与排除冲突 | 阻止生成并定位到具体号码 |
| 预算不足 | 阻止生成，显示该规格最低成本 |
| 超时/评估上限 | 返回已完成的候选并标记 `Partial`；不足最低方案数则返回失败 |
| 用户取消 | 不覆盖上一轮结果，不保存未完成运行 |
| 规则配置损坏 | 使用嵌入默认值并显示警告 |

### 13.2 并发

- `GroupPage` 同一时间只允许一个生成任务；
- 新请求先取消旧请求并等待旧任务退出；
- UI 更新通过 Dispatcher，算法层不依赖 Dispatcher；
- 仪表盘只读取上游产物，不在状态刷新时偷偷执行点位或杀号；
- 上游页面保存产物后通过 `ArtifactChanged` 通知，`GroupPage` 合并短时间内的重复刷新；
- 快照生成后，上游数据变化不影响本次运行；下一次生成重新构建快照。

### 13.3 性能目标

- 默认最多 100,000 次候选评估，在普通桌面设备上目标 2 秒内完成；
- 页面刷新不执行网络请求；
- 算法循环中避免创建大量临时集合和字符串；
- 评分解释只为最终候选完整物化，中间候选使用紧凑结构；
- 运行记录写盘不阻塞 UI 线程。

## 14. 验证方案

项目当前有独立的 `Verification` 控制台验证程序，V1 先延续该方式，为纯逻辑增加以下验证组：

1. 双色球合法性边界；
2. 单式、复式、胆拖注数与成本公式；
3. 锁定/排除冲突；
4. 相同快照、配置、请求和种子输出完全一致；
5. 不同种子只影响允许随机打破平局的部分；
6. 历史回放不读取目标期及未来数据；
7. 点位、杀号、复式票期号不一致时跳过错期数据，仍可使用其他 Ready 项组号；
8. 点位不在杀号大底时被正确过滤并留有解释；
9. 四站点 0/4–4/4 的所有组合都可生成，且 `UsedSources` / `SkippedSources` 正确；
10. Top K 多样性和逐级放宽诊断；
11. 取消、超时和评估上限；
12. JSON 运行记录往返一致、损坏文件保护；
13. 规则配置版本和默认回退；
14. 对至少 200 个历史目标期执行无未来数据回放。

完成纯逻辑验证后再做 WPF 冒烟检查：

- 票型切换字段显隐；
- 生成/取消按钮状态；
- 四个站点 Ready/Missing/Stale/Mismatch/Failed 的视觉和文案；
- 点击站点携带正确期号，返回后状态自动刷新；
- 0/4–4/4 均可启动，按钮准确显示本次使用的数据项数量；
- 方案复制文本；
- 长解释、空结果和小窗口下的布局；
- 主题资源和高 DPI 显示。

## 15. 分阶段实施

### 阶段 A：期号仪表盘与上游闭环

- 新增统一产物模型、`IAnalysisArtifactStore` 和四个 input adapter；
- 新增 `ICombinationReadinessService`，实现按期号状态汇总和可用来源采集；
- 把 `GroupPage` 首屏改为期号分析轨道；
- 抽取 `MainWindow.NavigateTo` 和导航上下文；
- 走势图、复式票统计、点位、杀号在成功分析后保存目标期产物；
- 上游页面增加“返回本期组号仪表盘”；
- 补充状态、期号错配和导航 Verification 用例。

完成标准：任意目标期的四项状态可正确识别；四张卡始终可点击；完成操作并返回后站点更新；0/4–4/4 均可生成且使用来源清单准确。

### 阶段 B：替换随机单式（算法最小闭环）

- 新增输入快照、规则配置和组号服务；
- 从当前所有 Ready 产物构建不可变输入，缺失项写入跳过清单；
- 实现确定性单式 Top K、评分明细和冲突解释；
- 改造 `GroupPage` 为异步生成；
- 将旧 `GetGroupRecommendations` 标记过时；
- 补充算法 Verification 用例。

完成标准：同一输入重复生成结果一致，页面不再使用 `Random.Shared`，每组方案都能解释分数来源。

### 阶段 C：票型、预算与运行记录

- 增加复式和胆拖；
- 增加预算、倍数和成本校验；
- 保存/读取生成运行；
- 增加复制、CSV 导出和历史回放。

完成标准：三种票型合法、成本准确、运行可按 `runId` 重放。

### 阶段 D：规则管理与评估

- 增加可视化规则权重设置；
- 对历史目标期批量回放；
- 比较规则版本，但不自动提升候选版本为正式版本；
- 为复式票反向热度因子提供独立开关和样本外评估。

完成标准：规则版本可审计，历史评估严格避免未来数据泄漏。

### 独立后续项目：实际购票与清算

若确实需要“确认购票、派奖、盈亏”，应新增独立的 `PurchasedTicket` 聚合、仓储和清算服务。不要复用当前 `TicketStore.Tickets`，原因是：

- 当前 `Ticket` 只有红蓝球集合，没有票型、倍数、成本和不可变快照；
- 当前仓储会随彩种切换和文件加载清空；
- 当前数据代表外部复式票样本，不代表用户实际购买；
- 清算还需要开奖结果修订、奖级规则版本、浮动奖金来源和幂等键。

只有在该独立领域模型完成后，组号方案才可通过显式操作转换为已购票记录。

## 16. 代码迁移清单

| 现有文件 | 计划修改 |
|---|---|
| `Models/AnalysisModels.cs` | `GroupRecommendation` 仅作兼容；新模型移至 `CombinationModels.cs` |
| `Services/IAnalysisService.cs` | `GetGroupRecommendations` 标记过时并最终删除 |
| `Services/AnalysisService.cs` | 删除随机组号职责，保留通用统计或后续拆分 |
| `Services/ITicketRepository.cs` | 增加 SSQ 指定期加载和统计产物构建能力 |
| `Services/Kill/IKillEngine.cs` | 增加显式目标期执行请求，保留旧快捷重载作兼容 |
| `Services/Kill/IRuleContextBuilder.cs` | 支持按目标期之前的数据构建规则上下文 |
| `Services/IIssueResolver.cs` | 新增统一期号选项与历史截止期解析 |
| `Services/DiagonalChainRenderer.cs` | 只负责绘制；斜连识别下沉到纯 `IDiagonalChainAnalyzer` |
| `Services/TrendSignalService.cs` | 新增五类视图过滤、冷号和斜连信号汇总 |
| `Controls/MatrixGrid.cs` | 继续渲染遗漏和冷号，但不再作为组号数据源 |
| `MainWindow.xaml.cs` | 抽取统一导航方法，支持期号、窗口和返回页上下文 |
| `Pages/TrendPage.xaml.cs` | 展示/发布与组号共用的 `TrendSignalSnapshot` |
| `Pages/GroupPage.xaml` | 首屏改为四站点期号轨道，再接组号设置和结果 |
| `Pages/GroupPage.xaml.cs` | 查询 readiness、处理站点导航、产物事件、取消和生成状态 |
| `Pages/TicketsPage.xaml.cs` | 支持按目标期加载，统计完成后保存 `TicketStatsArtifact` |
| `Pages/PositionPage.xaml.cs` | 支持导航期号，生成后另存 `PositionArtifact`，不修改冻结账本语义 |
| `Pages/KillPage.xaml.cs` | 支持目标期并持久化 `KillArtifact`，替换只靠 `_lastReport` 的临时状态 |
| `App.xaml.cs` | 注册 readiness、artifact adapters、组号服务与仓储 |
| `AppPaths.cs` | 增加上游产物、组号配置、运行记录目录 |
| `Resources/combination-rules.json` | 新增默认规则配置 |
| `Verification/Program.cs` | 注册组号验证组，或拆入独立源文件 |

## 17. 验收标准

- 只使用目标期之前的数据生成历史回放方案；
- 仪表盘始终明确显示目标期和要求的历史截止期；
- 0–4 项上游数据均可组号，结果必须明确显示实际使用和跳过的数据；
- 缺失、过期、错期和失败状态均有清楚文案及可执行入口；
- 点击上游站点携带目标期，返回后自动刷新并保持原目标期；
- 点位、杀号、复式票样本期号不一致时不混用；
- 所有输出满足双色球票面规则；
- 三种票型的注数、成本和预算判断正确；
- 用户锁定与排除行为可预测且有错误提示；
- 同一快照、规则版本、请求和种子输出字节级一致；
- 方案包含策略分明细、冲突证据、输入诊断和版本信息；
- 默认生成过程可取消、有超时和评估数量上限；
- 页面不直接依赖其他页面控件，不在 UI 线程执行密集计算；
- 不展示中奖概率承诺；
- 不把复式票样本误记为用户实际购票。

## 18. 免责声明

双色球开奖结果具有随机性。历史频次、点位、杀号、复式票样本和组合评分都不能保证未来命中。本模块输出的“策略分”只表示方案对当前配置规则的符合程度，不是中奖概率，也不构成购彩或收益承诺。请理性使用并自行控制预算。
