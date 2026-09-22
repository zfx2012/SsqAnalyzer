using System.Text.Json.Nodes;

namespace SsqAnalyzer.Services;

/// <summary>
/// 视频搜索服务接口 — 支持 B站等平台的视频搜索和元数据获取。
/// </summary>
public interface IVideoSearchService
{
    /// <summary>按 UID 搜索用户视频列表</summary>
    Task<JsonNode?> SearchByUid(long uid, CancellationToken ct = default);

    /// <summary>按 BVID 查询单个视频信息</summary>
    Task<JsonNode?> SearchByBvid(string bvid, CancellationToken ct = default);
}
