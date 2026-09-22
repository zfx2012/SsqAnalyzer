# 组号重复计算优化

日期：2026-09-22。

本轮仅调整 GroupService.Generate 内部计算顺序，不改变候选池、评分、同分排序、来源权重或每注说明规则。

原来在最多 16 个候选红球中枚举六球组合时，会先对全部 8,008 个组合进行展示排序，再保留所需候选。现在评分及同分值仍按原顺序计算，筛出最多 80 个候选后才生成升序号码数组。同一次 Generate 使用的走势冷号和斜连号码去重集合也只汇总一次，供各注说明共用。没有新增跨调用缓存，因此数据变化后不会沿用旧集合。

## 测量

Windows、.NET 10.0.9、Release，`DOTNET_TieredCompilation=0`。沿用通用性能入口固定种子 731927 的 510 期数据及 20 张模拟票据。先预热，测量 5 组，每组生成 10 次，取每次操作的中位值。序列化和哈希比较不计时。

| 生成 20 注 | 优化前 | 优化后 |
|---|---:|---:|
| 耗时 | 2.77 ms | 1.48 ms |
| 当前线程托管分配 | 5.36 MiB | 2.30 MiB |

本次耗时减少约 47%，分配减少约 57%。数据为固定离线样本，非页面端到端耗时，分配量也不是常驻内存或峰值。其他测量项的时间波动不归因于本轮改动。

## 兼容性

改动前生产程序集独立构建，与改动后使用同一通用性能测量程序。11 项结果哈希全部相同，包括完整组号结果的号码、评分、排序、来源及说明。现有组号验证增加 1、5、20 注的数量、号码升序和重复调用一致性检查，继续覆盖四类来源、输入不足及号码合法性。

```powershell
dotnet build Verification/SsqAnalyzer.PositionVerification.csproj -c Release -o tmp/group-build
dotnet tmp/group-build/SsqAnalyzer.Tests.dll
$env:DOTNET_TieredCompilation = '0'
dotnet tmp/group-build/SsqAnalyzer.Tests.dll --benchmark tmp/group-results.json
```

原始结果在 `tmp/group-pass/before.json`、`after.json` 和 `comparison.json`，改动前源码保存在 `tmp/group-pass/source/`。
