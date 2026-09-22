using System.IO;

namespace SsqAnalyzer;

/// <summary>Application paths. This portable deployment keeps runtime data beside the executable.</summary>
public static class AppPaths
{
    public static string Root { get; } = AppDomain.CurrentDomain.BaseDirectory;

    public static string DataDirectory => Path.Combine(Root, "data");
    public static string VideoDirectory => Path.Combine(Root, "video");
    public static string SettingsDirectory => Root;
    public static string DataFile => Path.Combine(Root, "ssq_data.txt");
    public static string ApiKeyFile => Path.Combine(Root, "api_key");
    public static string BiliCookieFile => Path.Combine(Root, "bili_cookie");
    public static string CrashLogFile => Path.Combine(Root, "crash.log");
    public static string PositionValidationFile => Path.Combine(Root, "position_forward_validation.json");

    public static string LegacyRoot => Root;
    public static string LegacyDataFile => Path.Combine(LegacyRoot, "ssq_data.txt");
    public static string LegacyApiKeyFile => Path.Combine(LegacyRoot, "api_key");
    public static string LegacyBiliCookieFile => Path.Combine(LegacyRoot, "bili_cookie");

    public static string ExistingFile(string primary, string legacy) =>
        File.Exists(primary) ? primary : legacy;

    public static void EnsureRoot() => Directory.CreateDirectory(Root);
}
