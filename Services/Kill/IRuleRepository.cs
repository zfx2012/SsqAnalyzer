namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 规则仓储接口（架构设计 §3.7）。
/// 合并内置规则（嵌入式资源，只读）+ 用户规则（JSON 文件持久化）。
/// </summary>
public interface IRuleRepository
{
    /// <summary>全部规则（内置 + 用户）。</summary>
    IReadOnlyList<IKillRule> GetAll();

    /// <summary>按 ruleId 查找；不存在返回 null。</summary>
    IKillRule? Find(string ruleId);

    /// <summary>仅 IsEnabled=true 的规则。</summary>
    IReadOnlyList<IKillRule> GetEnabled();

    /// <summary>新增自定义规则（IsBuiltin=false）。内置规则不可 Add。</summary>
    void Add(IKillRule rule);

    /// <summary>更新规则（启用/禁用、强制启用、参数等）。内置规则仅允许改 IsEnabled/ForceEnabled。</summary>
    void Update(IKillRule rule);

    /// <summary>删除规则。仅允许 IsBuiltin=false；内置规则抛异常。</summary>
    void Delete(string ruleId);

    /// <summary>保存回测统计到规则。内置规则仅更新内存（不持久化，见 §7.5）。</summary>
    void SaveBacktestStats(string ruleId, BacktestStatsSnapshot stats);

    /// <summary>规则集变更（增删改、启用状态切换）时触发。</summary>
    event Action? RulesChanged;
}
