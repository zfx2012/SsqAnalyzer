namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 杀号规则只读视图接口。供 KillEngine/BacktestEngine/RuleRepository 使用。
/// </summary>
public interface IKillRule
{
    string RuleId { get; }
    string Name { get; }
    RuleCategory Category { get; }
    string? SubCategory { get; }
    BallType BallType { get; }
    bool IsBuiltin { get; }
    bool IsEnabled { get; }
    bool IsVisible { get; }             // 是否在杀号页列表显示（独立于 IsEnabled，仅自定义规则可隐藏）
    double MinAccuracy { get; }          // 旧文件兼容字段；实际门槛使用 KillSettings 的分球种固定值
    bool ForceEnabled { get; }           // 用户强制启用（覆盖门槛）
    string JsCode { get; }
    IReadOnlyDictionary<string, object> Params { get; }
    string Description { get; }
    IReadOnlyList<string> Tags { get; }
    string Source { get; }
    DateTime CreatedAt { get; }
    BacktestStatsSnapshot? BacktestStats { get; set; }  // 由回测刷新
}
