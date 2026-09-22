using System.Collections.ObjectModel;

namespace SsqAnalyzer.Services;

/// <summary>
/// 复式票数据仓库 — 管理彩票数据的 CRUD、文件持久化和类型切换。
/// </summary>
public interface ITicketRepository
{
    /// <summary>当前已加载的所有复式票</summary>
    List<Ticket> Tickets { get; }

    /// <summary>当前期号（从文件中提取或手动指定）</summary>
    int? Period { get; set; }

    /// <summary>当前选中的彩票类型（双色球/大乐透）</summary>
    LotteryType CurrentType { get; set; }

    /// <summary>当前类型对应的红球最大号码（SSQ=33, DLT=35）</summary>
    int RedMax { get; }

    /// <summary>当前类型对应的蓝球最大号码（SSQ=16, DLT=12）</summary>
    int BlueMax { get; }

    /// <summary>票数据变更事件</summary>
    event Action? TicketsChanged;

    /// <summary>期号变更事件</summary>
    event Action? PeriodChanged;

    /// <summary>彩票类型变更事件</summary>
    event Action? TypeChanged;

    /// <summary>添加一张复式票</summary>
    void AddTicket(Ticket t);

    /// <summary>批量添加复式票</summary>
    void AddTickets(IEnumerable<Ticket> tickets);

    /// <summary>删除指定索引的复式票</summary>
    void RemoveTicketAt(int idx);

    /// <summary>清空所有票数据</summary>
    void Clear();

    /// <summary>从本地数据文件加载复式票</summary>
    bool LoadFromFile(string filePath);

    /// <summary>加载最新期号的数据文件</summary>
    int? LoadLatestData();

    /// <summary>列出所有数据文件（包括 .txt 和 .html）</summary>
    List<string> ListDataFiles();

    /// <summary>验证数据文件中的号码是否在所选彩种的合法范围内</summary>
    (bool hasError, string? msg) ValidateTicketRange(string filePath);

    /// <summary>检查指定类型+期号的数据文件是否已存在</summary>
    bool DataFileExists(LotteryType type, int? period);

    /// <summary>将解析结果保存到数据文件</summary>
    /// <param name="type">彩票类型，为空时自动推断</param>
    /// <returns>保存的文件名（不含路径），为空表示未保存</returns>
    string SaveResultAsFile(List<Ticket> tickets, int? period, LotteryType? type = null);
}
