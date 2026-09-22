using SsqAnalyzer.Services;

internal static partial class VerificationSuite
{
    private static void VerifyWorkbookCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool stopped = false;
        try { PositionWorkbookExporter.GeneratePredictions(Array.Empty<SsqAnalyzer.Models.DrawRecord>(), cancellationToken: cancelled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Assert(stopped, "pre-cancelled export stops before loading caches or computing predictions");

        using var during = new CancellationTokenSource();
        var records = BuildRecords(6);
        foreach (var record in records) { record.Period += 1000; record.DrawDate = record.DrawDate.AddYears(1); }
        bool missingHistoryRejected = false;
        try { PositionWorkbookExporter.GetMissingPredictionCount(records, 2026); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("之前没有历史数据")) { missingHistoryRejected = true; }
        Assert(missingHistoryRejected, "incomplete annual history reports a clear error before opening export dialog");
        stopped = false;
        var progress = new CancelExportProgress(during);
        try { PositionWorkbookExporter.GeneratePredictions(BuildRecords(20).Concat(records).ToArray(), progress, 2026, during.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Assert(progress.Reports > 0 && stopped, "cancel at final progress prevents cache commit and export result");
    }

    private sealed class CancelExportProgress(CancellationTokenSource cts) : IProgress<int>
    {
        public int Reports { get; private set; }
        public void Report(int value) { Reports++; cts.Cancel(); }
    }

    private static void VerifyAnnualWorkbookPredictionScope()
    {
        var historical = BuildRecords(20);
        var currentYear = BuildRecords(6);
        for (int index = 0; index < currentYear.Count; index++)
        {
            currentYear[index].Period = 2026001 + index;
            currentYear[index].DrawDate = new DateTime(2026, 1, 1).AddDays(index * 3);
        }
        currentYear[^1].DrawDate = new DateTime(2025, 12, 31);
        var records = historical.Concat(currentYear).OrderBy(record => record.Period).ToArray();
    
        Assert(PositionWorkbookExporter.GetDefaultPredictionYear(records) == 2026,
            "annual export uses issue year even when a draw date is inconsistent");
        Assert(PositionWorkbookExporter.GetPredictionIssueCount(records, 2026) == 7,
            "annual export contains current-year draws and next issue only");
        Assert(PositionWorkbookExporter.GetMissingPredictionCount(records, 2026) == 7,
            "annual cache audit ignores pre-2026 prediction rows");
    
        bool rejectedOldYear = false;
        try
        {
            PositionWorkbookExporter.GetPredictionIssueCount(records, 2025);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejectedOldYear = true;
        }
        Assert(rejectedOldYear, "annual point prediction starts in 2026");
    }
}
