using System.Net.Http;
using System.Reflection;
using SsqAnalyzer.Services;

internal static partial class VerificationSuite
{
    private static void VerifyAnonymousBiliWbiSearch()
    {
        var handler = new AnonymousBiliHandler();
        using var http = new HttpClient(handler);
        var constructor = typeof(BiliService).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(ITicketStore), typeof(HttpClient) },
            modifiers: null)!;
        var service = (BiliService)constructor.Invoke(new object[] { new TicketStore(), http });
    
        var result = service.SearchByUid(546195).GetAwaiter().GetResult();
        Assert(result?["code"]?.GetValue<int>() == 0, "anonymous UID search accepts nav -101 WBI keys");
        Assert(!handler.SawCookie, "anonymous Bili requests omit Cookie header");
    }
    private static void VerifyDownloadIntegrityMetadata()
    {
        Assert(typeof(DownloadService).GetMethod(
            "ValidateExistingYtDlp", BindingFlags.Instance | BindingFlags.NonPublic) == null,
            "existing local yt-dlp no longer triggers online validation");
    
        var extractHash = typeof(DownloadService).GetMethod(
            "ExtractExpectedHash", BindingFlags.Static | BindingFlags.NonPublic)!;
        const string expected = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        var hash = (string?)extractHash.Invoke(null, new object[] { $"{expected.ToLowerInvariant()}  yt-dlp.exe\n" });
        Assert(hash == expected, "official SHA2-256SUMS entry for yt-dlp.exe is parsed");
    }
    private static void VerifyKeywordVideoSearchDisabled()
    {
        Assert(typeof(IVideoSearchService).GetMethod("SearchByKeyword") == null
            && typeof(BiliService).GetMethod("SearchByKeyword") == null
            && typeof(SsqAnalyzer.Pages.CompoundStatsPage).GetMethod(
                "SearchByKeyword", BindingFlags.Instance | BindingFlags.NonPublic) == null,
            "keyword search API and page path are removed");
    }
    private static void VerifyVideoResultDownloadEligibility()
    {
        var message = new SsqAnalyzer.Pages.CompoundStatsPage.VideoInfo
        {
            Title = "获取失败: B站请求失败，HTTP 412"
        };
        Assert(!message.IsDownloadable && message.HasError && message.TitleWithExt == message.Title,
            "status messages without BVID cannot be downloaded");
    
        var video = new SsqAnalyzer.Pages.CompoundStatsPage.VideoInfo
        {
            Bvid = "BV1example",
            Title = "2026086期复式票"
        };
        Assert(video.IsDownloadable && !video.HasError && video.TitleWithExt.EndsWith(".mp4"),
            "real BVID results remain downloadable");
    }
    private static void VerifyEstimatedAnalysisProgress()
    {
        var estimate = typeof(SsqAnalyzer.Pages.CompoundStatsPage).GetMethod(
            "EstimatedAnalysisProgress", BindingFlags.Static | BindingFlags.NonPublic)!;
        double At(double seconds) => (double)estimate.Invoke(null, new object[] { seconds })!;
    
        Assert(At(0) == 0 && At(15) >= 30 && At(60) >= 65 && At(120) >= 88,
            "estimated analysis progress advances across elapsed-time stages");
        Assert(At(600) == 95, "estimated analysis progress never reaches completion before AI responds");
    }
    private static void VerifyEstimatedDownloadProgress()
    {
        var estimate = typeof(SsqAnalyzer.Pages.CompoundStatsPage).GetMethod(
            "EstimatedDownloadProgress", BindingFlags.Static | BindingFlags.NonPublic)!;
        double At(double seconds) => (double)estimate.Invoke(null, new object[] { seconds })!;
    
        Assert(At(0) == 0 && At(5) >= 15 && At(20) >= 60 && At(60) >= 88,
            "estimated download progress advances while yt-dlp is silent");
        Assert(At(300) == 95, "estimated download progress waits below completion");
    }
}
