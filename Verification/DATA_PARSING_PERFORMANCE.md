# 数据文本解析优化

日期：2026-09-22。

数据服务已有 Lazy 缓存，重复调用 GetAllRecords 不会重复读取和解析。此次保留缓存失效、网络更新返回码、通知及文件保存流程，只优化共用的 ParseLines 路径。

旧实现对每行执行 Trim 和 Split，为所有字段创建字符串。当前实现直接扫描原字符串的片段，复用栈上的 28 个字段范围并调用数值解析的 span 重载。第 28 个字段之后的内容原来没有使用，现在无需继续拆分。该实现不缓存额外的开奖记录副本。

保留原有空格分隔规则、行首尾空白处理、日期格式、必需字段失败时跳过整行、可选奖项字段失败时置零、红球排序以及原始记录顺序。没有新增号码范围校验、去重或数据纠正规则。

## 测量

Windows、.NET 10.0.9、Release，关闭分层 JIT。使用全部内嵌开奖数据，每个实现先预热，再测量 5 组，每组解析 10 次，报告每次解析的中位值。序列化、结果比较、文件读取和网络下载均不计时。

| 实现 | 每次解析耗时 | 每次托管分配 |
|---|---:|---:|
| 原字符串拆分 | 2.83 ms | 4.811 MiB |
| 文本片段解析 | 2.11 ms | 0.502 MiB |

本次测量耗时减少约 25%，分配减少约 90%。这是解析阶段的测量，不代表整个文件加载或应用启动获得同等加速。分配量为当前线程累计分配，不是常驻内存或峰值。

## 验证

验证程序保存优化前的解析方法作为独立参考，与新实现比较全部持久化字段及记录顺序。样本包含全部内嵌数据、空行、缺字段、非法日期、制表符、额外空格，以及固定种子生成的 1,200 条字段截断、无效整数、溢出数值等变体；在 Invariant、zh-CN、en-US 三种区域设置下逐一比较。

```powershell
dotnet build Verification/SsqAnalyzer.PositionVerification.csproj -c Release -o tmp/data-build
dotnet tmp/data-build/SsqAnalyzer.Tests.dll
$env:DOTNET_TieredCompilation = '0'
dotnet tmp/data-build/SsqAnalyzer.Tests.dll --data-benchmark tmp/data-results.json
```

本轮原始测量在 `tmp/data-parsing-pass/report.json`，原生产代码备份在 `tmp/data-parsing-pass/source/`。不会读取或修改用户的本地开奖记录文件。
