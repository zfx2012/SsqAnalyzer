namespace SsqAnalyzer.Services;

/// <summary>
/// 凭证安全存储 — 使用 DPAPI 加密保护 API Key 和 B站 Cookie。
/// </summary>
public interface ICredentialStore
{
    // ===== API Key 配置 =====

    /// <summary>保存 API Key 及相关配置（DPAPI 加密存储）</summary>
    void SaveApiKey(string plainText, string? model = null, string? wsId = null, string? apiHost = null);

    /// <summary>加载 API Key（DPAPI 解密）</summary>
    string? LoadApiKey();

    /// <summary>加载当前选择的 AI 模型名称（未配置时返回 null，以 API 默认值为准）</summary>
    string? LoadModel();

    /// <summary>加载业务空间 ID（百炼 WorkspaceId）</summary>
    string? LoadWsId();

    /// <summary>加载自定义 API Host（DashScope 兼容 API）</summary>
    string? LoadApiHost();

    // ===== LLM API 配置（NL→Code 等 OpenAI 兼容文本模型，独立于上方千问/多模态） =====

    /// <summary>保存 LLM API Key（OpenAI 兼容：DeepSeek/GLM/Minimax/Kimi 等）</summary>
    void SaveLlmApiKey(string? apiKey, string? baseUrl, string? model);

    /// <summary>加载 LLM API Key</summary>
    string? LoadLlmApiKey();

    /// <summary>加载 LLM BaseUrl（含协议+路径前缀，如 https://api.deepseek.com/v1）</summary>
    string? LoadLlmBaseUrl();

    /// <summary>加载 LLM 模型名（如 deepseek-chat）</summary>
    string? LoadLlmModel();

    // ===== B站 Cookie 存储 =====

    /// <summary>保存 B站 Cookie（DPAPI 加密存储）</summary>
    void SaveBiliCookie(string cookie, string? sessdata, string? biliJct, string? buvid3);

    /// <summary>加载 B站 Cookie 字符串（DPAPI 解密）</summary>
    string? LoadBiliCookieString();

    /// <summary>B站 Cookie 是否已配置（用于搜索和下载）</summary>
    bool IsBiliLoggedIn { get; }

    /// <summary>B站 SESSDATA</summary>
    string? BiliSessdata { get; }

    /// <summary>B站 buvid3</summary>
    string? BiliBuvid3 { get; }

    // ===== B站 UID 默认值 =====

    /// <summary>加载 B站 UID 默认值（DPAPI 解密）</summary>
    string? LoadBiliUid();

    /// <summary>保存 B站 UID 默认值</summary>
    void SaveBiliUid(string? uid);
}
