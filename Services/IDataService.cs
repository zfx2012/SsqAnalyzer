using System.Collections.ObjectModel;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

/// <summary>开奖数据服务接口 — 负责从远程 API 获取历史开奖记录并缓存到本地文件</summary>
public interface IDataService
{
    /// <summary>获取所有已加载的开奖记录</summary>
    List<DrawRecord> GetAllRecords();

    /// <summary>获取最新的期号</summary>
    int GetLastPeriod();

    /// <summary>获取已加载的记录数量</summary>
    int GetRecordCount();

    /// <summary>异步更新开奖数据（从远程 API 拉取最新期次并缓存到本地）</summary>
    Task<int> TryUpdateAsync();

    /// <summary>清除所有已加载的数据</summary>
    void ClearAllData();

    /// <summary>重置内存缓存（下次访问时从文件重新加载）</summary>
    void ResetCache();

    /// <summary>是否存在本地缓存文件</summary>
    bool HasLocalFile { get; }

    /// <summary>本地缓存文件的完整路径</summary>
    string LocalDataFilePath { get; }

    /// <summary>最近一次操作的错误消息，无错误时为 null</summary>
    string? LastErrorMessage { get; }

    /// <summary>数据更新事件（数据变更时触发，通知 UI 刷新）</summary>
    event Action? DataUpdated;

    /// <summary>手动触发数据更新事件</summary>
    void NotifyDataUpdated();
}
