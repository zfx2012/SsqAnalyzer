# 周期交替规则修订（2026-09-29）

截至2026112期，100条正式执行器复核仅B-G-R-005-CY低于门槛：最近50次触发排除54个号码，正确44个，81.48%。固定门槛仍为红球82%、蓝球94%。

保留周期图，改为同一号码「开出→空3行→开出→空3行」时在下一行排除它。行指与目标开奖日同星期的历史开奖，不是连续实际开奖期。取消原先实际开奖遗漏3期以上的过滤；其他星期的开出不会破坏这个周期图形。

只比较间隔2、3、4行的固定图形：近50次分别为77.97%、88.89%、86.44%，全部历史触发分别1437、991、719次。选择达标且历史触发更多的间隔3行，而非追加特定期号或号码条件。间隔选择使用了同一历史数据，不是独立预测验证。

|指标|修订前|修订后|
|---|---:|---:|
|该规则最近50次正确排除率|44/54，81.48%|48/54，88.89%|
|100条当前历史门槛达标数|99|100|
|当前推荐红球数量|15|15|
|近500期平均剩余红球|13.586|13.658|
|近500期剩余6～15个|390期|383期|
|近500期剩余不足6个|0期|0期|
|近500期完整保留6个红球|2期|3期|

只变更这一条执行定义，其余99条保持。当前该规则未触发，当前合并结果仍为15个红球；不能由此保证以后每期都不超过15个。已有历史提交保持旧版本，旧统计与新规则按指纹区分。

验证：35/35组回归通过；周期正例、非周期噪声、周期空行反例、目标期开奖隔离及图示九行检查通过。正式JS的近500期及本期共501个结果，与Python按实际星期筛选后的八行独立计算一致。图示也修复了直接设置alternationGap时仍画成一行间隔的问题。

[候选与正式复核记录](Fixtures/cycle-gap-results-20260929.json)

```powershell
dotnet build Verification/SsqAnalyzer.PositionVerification.csproj -c Release -o tmp/gate-cycle/build
dotnet tmp/gate-cycle/build/SsqAnalyzer.Tests.dll --tune-alternation-gap bin/Debug/net10.0-windows/ssq_data.txt tmp/gate-cycle/trials.json B-G-R-005-CY
dotnet tmp/gate-cycle/build/SsqAnalyzer.Tests.dll --coverage-audit bin/Debug/net10.0-windows/ssq_data.txt tmp/gate-cycle/after.json
```

复现数字需使用相同数据指纹；候选工具只输出报告，不自动修改资源。
