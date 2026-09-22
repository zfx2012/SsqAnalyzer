using System.IO;
using SsqAnalyzer.Services;

internal static partial class VerificationSuite
{
    private static void VerifyTicketDataPeriodParsing()
    {
        WithTestDirectory(testRoot =>
        {
            const string ticket = "01,02,03,04,05,06;07";
            var store = new TicketStore();
    
            string periodFile = Path.Combine(testRoot, "SSQ_2026085.txt");
            File.WriteAllText(periodFile, ticket);
            Assert(store.LoadFromFile(periodFile) && store.Period == 2026085,
                "valid seven-digit period is loaded");
    
            string timestampFile = Path.Combine(testRoot, "SSQ_20260728_153000.txt");
            File.WriteAllText(timestampFile, ticket);
            Assert(store.LoadFromFile(timestampFile) && store.Period is null,
                "timestamp suffix is not treated as a period");
        });
    }
}
