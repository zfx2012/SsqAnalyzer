using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using SsqAnalyzer.Controls;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

/// <summary>Opt-in, offline measurements. Timing is informational, never a pass/fail assertion.</summary>
internal static class PerformanceMeasurements
{
    public static void Run(string? outputPath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunOnStaThread(outputPath); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Performance measurement failed.", failure);
    }

    private static void RunOnStaThread(string? outputPath)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var records = CreateRecords();
        var data = new FakeDataService();
        data.SetRecords(records.ToArray());
        var predictor = new PositionPredictor(data);
        int issue = predictor.GetIssueOptions()[0];
        var tickets = new TicketStore { Period = issue };
        tickets.AddTickets(records.TakeLast(20).Select(record => new Ticket
        {
            Reds = record.RedBalls.Concat(new[] { 33 }).Distinct().Order().ToList(),
            Blues = new List<int> { record.BlueBall }
        }).ToList());
        var groups = new GroupService(data, tickets, new GroupInputStore());
        var matrix = new MatrixGrid();
        var trendRecords = records.TakeLast(100).ToList();
        matrix.SetFullData(records);
        var researchRecords = records.Where(record => record.Period < 2026000).TakeLast(60)
            .Concat(records.Where(record => record.Period >= 2026000).Take(30)).ToArray();
        var shortHistory = records.TakeLast(30).ToArray();
        var results = new List<Measurement>();

        Measure("predict-next", 3, 1, () => predictor.Predict(issue, "backtest"));
        Measure("backtest-30", 3, 1, () => predictor.Backtest(30));
        Measure("group-20", 5, 10, () => groups.Generate(issue, 20));
        Measure("trend-100-refresh-and-layout", 3, 1, () =>
        {
            matrix.Render(trendRecords, true, true, 100);
            matrix.Measure(new Size(1600, double.PositiveInfinity));
            matrix.Arrange(new Rect(new Point(), matrix.DesiredSize));
            return new { Children = matrix.Children.Count, Rows = matrix.RowDefinitions.Count, matrix.DesiredSize };
        });
        Measure("trend-100-equal-list-refresh-and-layout", 3, 1, () =>
        {
            matrix.Render(trendRecords.ToList(), true, true, 100);
            matrix.Measure(new Size(1600, double.PositiveInfinity));
            matrix.Arrange(new Rect(new Point(), matrix.DesiredSize));
            return new { Children = matrix.Children.Count, Rows = matrix.RowDefinitions.Count, matrix.DesiredSize };
        });
        Measure("blue-score", 3, 1, () => Invoke("ScoreBlueBalls", predictor, records, issue));
        Measure("blue-formula-evaluation", 3, 1, () => Invoke("EvaluateLegacyBlueFormulas", predictor, records, issue));
        Measure("blue-exclusion-reliability", 3, 1, () => Invoke("EvaluateExclusionReliability", null, records));
        Measure("dynamic-model", 3, 3, () => PositionPredictor.GetDynamicHierarchicalBallProbabilities(shortHistory));
        Measure("conditional-dynamic-model", 3, 3, () => PositionPredictor.GetDynamicConditionalHierarchicalBallProbabilities(shortHistory));
        Measure("annual-research-30", 3, 1, () => AnnualShortPointResearch.Run(researchRecords));

        var report = new
        {
            Fixture = "performance-v1-seed-731927",
            FixtureHash = Hash(records),
            RecordCount = records.Count,
            CurrentYearRecords = records.Count(record => record.Period >= 2026000),
            ResearchRecords = researchRecords.Length,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            Build = typeof(PerformanceMeasurements).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
            AllocationScope = "GC.GetAllocatedBytesForCurrentThread; excludes other threads and native/GPU allocations",
            Results = results
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (outputPath is null) Console.WriteLine(json);
        else
        {
            string fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json);
            Console.WriteLine($"Report: {fullPath}");
        }

        void Measure<T>(string name, int samples, int operations, Func<T> operation)
        {
            Console.WriteLine($"Measuring {name}...");
            string expectedHash = Hash(operation()); // One untimed warm-up; result serialization stays outside measurements.
            var times = new double[samples];
            var allocations = new long[samples];
            for (int sample = 0; sample < samples; sample++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                T value = default!;
                for (int index = 0; index < operations; index++) value = operation();
                times[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / operations;
                allocations[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / operations;
                if (Hash(value) != expectedHash) throw new InvalidOperationException($"Non-deterministic result: {name}");
            }
            var result = new Measurement(name, samples, operations, times.Order().ElementAt(samples / 2),
                allocations.Order().ElementAt(samples / 2), times, allocations, expectedHash);
            results.Add(result);
            Console.WriteLine($"{name}: {result.MedianMilliseconds:F2} ms, {result.MedianAllocatedBytes / 1048576.0:F2} MiB/op");
        }
    }

    private static object Invoke(string method, object? target, params object[] arguments) =>
        typeof(PositionPredictor).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!
            .Invoke(target, arguments)!;

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private static List<DrawRecord> CreateRecords()
    {
        var random = new Random(731927);
        return Enumerable.Range(0, 510).Select(index => new DrawRecord
        {
            Period = index < 450 ? (2023 + index / 150) * 1000 + index % 150 + 1 : 2026001 + index - 450,
            DrawDate = index < 450 ? new DateTime(2023 + index / 150, 1, 1).AddDays(index % 150 * 2)
                : new DateTime(2026, 1, 1).AddDays((index - 450) * 2),
            RedBalls = Enumerable.Range(1, 33).OrderBy(_ => random.Next()).Take(6).Order().ToArray(),
            BlueBall = random.Next(1, 17)
        }).ToList();
    }

    private sealed record Measurement(string Name, int Samples, int OperationsPerSample,
        double MedianMilliseconds, long MedianAllocatedBytes, double[] Milliseconds,
        long[] AllocatedBytes, string ResultHash);
}
