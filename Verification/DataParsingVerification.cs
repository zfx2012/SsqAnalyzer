using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

internal static partial class DataParsingVerification
{
    public static void Run()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var lines = new List<string>(SsqRawData.Lines);
            lines.AddRange(new[] { "", "   ", "invalid", "2026001 2026-01-01 1 2 3 4 5 6 7",
                "\t2026001 2026-01-01 6 5 4 3 2 1 7\r", "2026001\t2026-01-01 1 2 3 4 5 6 7",
                "2026001 2026-02-30 1 2 3 4 5 6 7" });
            var random = new Random(4917);
            string[] invalid = { "oops", "2147483648", "-1", "+2", "9223372036854775808", "\t4\t" };
            for (int index = 0; index < 1200; index++)
            {
                var fields = SsqRawData.Lines[index % SsqRawData.Lines.Length].Split(' ');
                fields[random.Next(fields.Length)] = invalid[random.Next(invalid.Length)];
                lines.Add("\t " + string.Join(index % 2 == 0 ? " " : "   ", fields.Take(random.Next(fields.Length + 1))) + " \r");
            }
            foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("zh-CN"), CultureInfo.GetCultureInfo("en-US") })
            {
                CultureInfo.CurrentCulture = culture;
                var input = lines.ToArray();
                if (Snapshot(ParseReference(input)) != Snapshot(DataService.ParseLines(input)))
                    throw new InvalidOperationException($"Data parsing differs in {culture.Name}.");
            }
            if (DataService.ParseLines(Array.Empty<string>()).Count != 0)
                throw new InvalidOperationException("Empty input must remain empty.");
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    public static void Measure(string outputPath)
    {
        Run();
        var lines = SsqRawData.Lines;
        var results = new List<object>();
        string expected = Snapshot(ParseReference(lines));
        foreach (var (name, parse) in new (string, Func<string[], List<DrawRecord>>)[]
        {
            ("reference-split", ParseReference), ("span-fields", DataService.ParseLines)
        })
        {
            parse(lines);
            var times = new List<double>();
            var allocations = new List<long>();
            for (int sample = 0; sample < 5; sample++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                List<DrawRecord>? result = null;
                for (int operation = 0; operation < 10; operation++) result = parse(lines);
                times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds / 10);
                allocations.Add((GC.GetAllocatedBytesForCurrentThread() - bytes) / 10);
                if (Snapshot(result!) != expected) throw new InvalidOperationException("Benchmark result changed.");
            }
            results.Add(new { Name = name, MedianMilliseconds = times.Order().ElementAt(2),
                MedianAllocatedBytes = allocations.Order().ElementAt(2), Milliseconds = times, AllocatedBytes = allocations });
            Console.WriteLine($"{name}: {times.Order().ElementAt(2):F2} ms, {allocations.Order().ElementAt(2) / 1048576.0:F3} MiB");
        }
        string path = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Culture = CultureInfo.CurrentCulture.Name,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Lines = lines.Length, Samples = 5, OperationsPerSample = 10,
            ResultHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expected))), Results = results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Report: {path}");
    }

    private static string Snapshot(List<DrawRecord> records) => JsonSerializer.Serialize(records.Select(r => new
    {
        r.Period, r.DrawDate, r.RedBalls, r.BlueBall, r.SalesAmount, r.PoolAmount,
        r.FirstPrizeCount, r.FirstPrizeAmount, r.SecondPrizeCount, r.SecondPrizeAmount,
        r.ThirdPrizeCount, r.FourthPrizeCount, r.FifthPrizeCount, r.SixthPrizeCount
    }));
}
