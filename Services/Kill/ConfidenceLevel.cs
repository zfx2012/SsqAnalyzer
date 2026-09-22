namespace SsqAnalyzer.Services.Kill;

/// <summary>
/// 杀号置信度：门槛按球种固定：红球 82%、蓝球 94%。
/// High：≥2 条达标规则；Medium：仅 1 条达标；Low：仅被不达标规则杀（含强制启用）。
/// </summary>
public enum ConfidenceLevel
{
    High,
    Medium,
    Low
}
