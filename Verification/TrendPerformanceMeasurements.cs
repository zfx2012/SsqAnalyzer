using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SsqAnalyzer.Controls;
using SsqAnalyzer.Models;

internal static class TrendPerformanceMeasurements
{
    public static void Run(string outputDirectory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Measure(outputDirectory); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Trend measurement failed.", failure);
    }

    private static void Measure(string outputDirectory)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        var random = new Random(53612);
        var all = Enumerable.Range(0, 150).Select(index => new DrawRecord
        {
            Period = 2026001 + index,
            DrawDate = new DateTime(2026, 1, 1).AddDays(index * 2),
            RedBalls = Enumerable.Range(1, 33).OrderBy(_ => random.Next()).Take(6).Order().ToArray(),
            BlueBall = random.Next(1, 17)
        }).ToList();
        var rows = all.TakeLast(100).ToList();
        var alternate = rows.Select(Clone).ToList();
        alternate[^1].Period += 1000;
        var matrix = new MatrixGrid();
        matrix.SetFullData(all);
        var results = new List<object>();
        bool alternateData = false, routeMode = false;
        Sample("same-list-refresh", () => matrix.Render(rows, true, true, 100));
        Sample("equal-content-new-list", () => matrix.Render(rows.ToList(), true, true, 100));
        Sample("changed-data-rebuild", () =>
        {
            alternateData = !alternateData;
            matrix.Render(alternateData ? alternate : rows, true, true, 100);
        });
        Sample("normal-012-switch", () =>
        {
            routeMode = !routeMode;
            matrix.Render(rows, true, true, 100, routeMode);
        });

        Sample("initial-100-layout", () =>
        {
            matrix = new MatrixGrid();
            matrix.SetFullData(all);
            matrix.Render(rows, true, true, 100);
        });
        var large = Enumerable.Range(0, 300).Select(index =>
        {
            var record = Clone(all[index % all.Count]);
            record.Period = 2026001 + index;
            record.DrawDate = new DateTime(2026, 1, 1).AddDays(index * 2);
            return record;
        }).ToList();
        Sample("initial-300-layout", () =>
        {
            matrix = new MatrixGrid();
            matrix.SetFullData(large);
            matrix.Render(large, true, true, 300);
        });
        Sample("period-300-to-100", () => matrix.Render(large, true, true, 100),
            () => matrix.Render(large, true, true, 300));
        Sample("period-100-to-300", () => matrix.Render(large, true, true, 300),
            () => matrix.Render(large, true, true, 100));

        var visual = new MatrixGrid();
        visual.SetFullData(all);
        var visible = all.TakeLast(12).ToList();
        var pictures = new List<object>();
        Capture("normal", visible, true, true);
        Capture("equal-list", visible.ToList(), true, true);
        Capture("miss-off", visible, false, true);
        Capture("cold-off", visible, false, false);
        Capture("equal-list-after-options", visible.ToList(), false, false);
        Capture("options-restored", visible, true, true);
        Capture("012", visible, true, true, true);
        Capture("012-equal-list", visible.ToList(), true, true, true);
        var selections = (IDictionary)typeof(MatrixGrid).GetField("_selections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visual)!;
        selections[(0, true, 1)] = true;
        selections[(1, false, 16)] = true;
        var updateSelection = typeof(MatrixGrid).GetMethod("UpdatePredRowCells", BindingFlags.Instance | BindingFlags.NonPublic)!;
        updateSelection.Invoke(visual, new object[] { 0 });
        updateSelection.Invoke(visual, new object[] { 1 });
        Capture("selected-equal-list", visible.ToList(), true, true, true);
        Capture("selected-normal", visible, true, true);
        Capture("parity-filter", all.Where(record => record.Period % 2 == 0).TakeLast(12).ToList(), true, true);
        Capture("empty", new List<DrawRecord>(), true, true);
        Capture("restored", visible, true, true);
        visual.SetFullData(large);
        Capture("large-300", large, true, true);
        Capture("large-100", large.TakeLast(100).ToList(), true, true);

        File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new
        {
            Fixture = "trend-v2-seed-53612",
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
            Samples = 5,
            AllocationScope = "current thread managed allocations; excludes native/GPU memory",
            Results = results,
            Pictures = pictures
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Report and {pictures.Count} rendered PNGs: {directory}");

        void Sample(string name, Action render, Action? prepare = null)
        {
            prepare?.Invoke();
            Layout(matrix);
            render();
            Layout(matrix);
            var times = new List<double>();
            var allocated = new List<long>();
            for (int sample = 0; sample < 5; sample++)
            {
                if (prepare is not null)
                {
                    prepare();
                    Layout(matrix);
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                render();
                Layout(matrix);
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                long allocation = GC.GetAllocatedBytesForCurrentThread() - bytes;
                times.Add(elapsed);
                allocated.Add(allocation);
            }
            results.Add(new { Name = name, MedianMilliseconds = times.Order().ElementAt(2),
                MedianAllocatedBytes = allocated.Order().ElementAt(2), Milliseconds = times, AllocatedBytes = allocated });
            Console.WriteLine($"{name}: {times.Order().ElementAt(2):F2} ms, {allocated.Order().ElementAt(2) / 1048576.0:F3} MiB");
        }

        void Capture(string name, List<DrawRecord> source, bool showMiss, bool showCold, bool show012 = false)
        {
            visual.Render(source, showMiss, showCold, source.Count, show012);
            string hash = VisualHash(visual, Path.Combine(directory, name + ".png"));
            pictures.Add(new { Name = name, PixelHash = hash, Rows = visual.RowDefinitions.Count,
                Children = visual.Children.Count, Width = visual.ActualWidth, Height = visual.ActualHeight });
        }
    }

    internal static string VisualHash(MatrixGrid matrix, string? filePath = null)
    {
        Layout(matrix);
        int width = (int)Math.Ceiling(matrix.ActualWidth);
        int height = (int)Math.Ceiling(matrix.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(matrix);
        byte[] pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        if (filePath is not null)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(filePath);
            encoder.Save(stream);
        }
        return Convert.ToHexString(SHA256.HashData(pixels));
    }

    private static void Layout(MatrixGrid matrix)
    {
        matrix.Measure(new Size(1200, double.PositiveInfinity));
        matrix.Arrange(new Rect(0, 0, 1200, matrix.DesiredSize.Height));
        matrix.UpdateLayout();
    }

    private static DrawRecord Clone(DrawRecord record) => new()
    {
        Period = record.Period, DrawDate = record.DrawDate,
        RedBalls = record.RedBalls.ToArray(), BlueBall = record.BlueBall
    };
}
