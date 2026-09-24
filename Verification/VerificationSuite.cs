internal static partial class VerificationSuite
{
    public static void Run()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("command line compatibility", VerifyCommandLineCompatibility),
            ("blue cycle prefix compatibility", VerifyBlueCyclePrefixCompatibility),
            ("conditional probability buffer compatibility", VerifyConditionalProbabilityBufferCompatibility),
            ("analysis statistics compatibility", AnalysisVerification.Run),
            ("data text parsing compatibility", DataParsingVerification.Run),
            ("cancellation before result commit", VerifyCancellationBeforeCommit),
            ("kill backtest correctness and persistence", VerifyBacktestCorrectness),
            ("kill research and forward validation", VerifyKillEvaluation),
            ("kill report submission and review", VerifyKillSubmissions),
            ("point range boundaries", VerifyPointRanges),
            ("builtin kill rule curation", VerifyBuiltinKillRuleCuration),
            ("ticket data period parsing", VerifyTicketDataPeriodParsing),
            ("anonymous Bili WBI search", VerifyAnonymousBiliWbiSearch),
            ("keyword video search disabled", VerifyKeywordVideoSearchDisabled),
            ("video result download eligibility", VerifyVideoResultDownloadEligibility),
            ("download integrity metadata", VerifyDownloadIntegrityMetadata),
            ("estimated AI analysis progress", VerifyEstimatedAnalysisProgress),
            ("estimated download progress", VerifyEstimatedDownloadProgress),
            ("trend matrix lifecycle and interaction", VerifyTrendMatrixLifecycleAndInteraction),
            ("trend cache invalidation", VerifyTrendCacheInvalidation),
            ("trend cell reuse compatibility", VerifyTrendCellReuse),
            ("group recommendation module", VerifyGroupRecommendationModule),
            ("point-only research", VerifyPointOnlyResearch),
            ("annual workbook prediction scope", VerifyAnnualWorkbookPredictionScope),
            ("workbook cancellation boundaries", VerifyWorkbookCancellation),
            ("annual short-term predictor scope", VerifyAnnualShortTermPredictorScope),
            ("annual short-term point research", VerifyAnnualShortTermPointResearch),
            ("predictor invariants and no future data", VerifyPredictorInvariants),
            ("forward ledger lifecycle", VerifyForwardLedgerLifecycle),
            ("forward point absolute gates", VerifyForwardPointAbsoluteGates),
            ("ledger immutable hash", VerifyLedgerImmutableHash),
            ("corrupt ledger protection", VerifyCorruptLedgerProtection),
            ("builtin ID migration", VerifyBuiltinIdMigration),
            ("page loading and subscription lifecycle", VerifyPageLifecycle)
        };
        int passed = 0;
        foreach (var test in tests)
        {
            test.Run();
            passed++;
            Console.WriteLine($"PASS {test.Name}");
        }
        Console.WriteLine($"Passed {passed}/{tests.Length} verification groups.");
    }
}
