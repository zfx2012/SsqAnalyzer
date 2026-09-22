using System.Collections.ObjectModel;

namespace SsqAnalyzer.Services;

/// <summary>
/// 票务数据存储联合接口（向后兼容） — 合并了 ITicketRepository / ICredentialStore / ITicketParser。
/// 新代码请按需注入具体接口，不再新增对 ITicketStore 的依赖。
/// </summary>
public interface ITicketStore : ITicketRepository, ICredentialStore, ITicketParser
{
}
