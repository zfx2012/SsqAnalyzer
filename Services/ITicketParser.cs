namespace SsqAnalyzer.Services;

/// <summary>
/// 号码文本解析 — 从 AI 响应文本和文件名中提取彩票类型、期号和号码。
/// </summary>
public interface ITicketParser
{
    /// <summary>从 AI 返回文本中提取期号</summary>
    int? ExtractPeriod(string text);

    /// <summary>从 AI 返回文本中提取彩种类型</summary>
    LotteryType? ExtractType(string text);

    /// <summary>从文件名推断彩种类型（支持中文名和英文缩写）</summary>
    LotteryType? DetectTypeFromFileName(string filePath);

    /// <summary>从文件名提取期号（按分隔符拆分，取 5-7 位纯数字段）</summary>
    int? ExtractPeriodFromFileName(string filePath);

    /// <summary>从 AI 返回文本解析复式票行（使用当前 UI 选中彩种的范围）</summary>
    List<Ticket> ParseTicketsFromText(string text);

    /// <summary>从 AI 返回文本解析复式票行（指定彩种类型决定号码范围）</summary>
    List<Ticket> ParseTicketsFromText(string text, LotteryType type);
}
