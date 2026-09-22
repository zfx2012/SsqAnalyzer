namespace SsqAnalyzer.Services.Kill;

/// <summary>球号 + 当前遗漏值。用于 ctx.getMissValues(window) 返回结构。</summary>
public readonly record struct MissValue(int Ball, int Value);
