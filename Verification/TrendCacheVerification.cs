using System.Windows;
using SsqAnalyzer.Controls;
using SsqAnalyzer.Models;

internal static partial class VerificationSuite
{
    private static void VerifyTrendCellReuse()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var rows = Enumerable.Range(0, 12).Select(index => new DrawRecord
                {
                    Period = 2026001 + index, DrawDate = new DateTime(2026, 1, 1).AddDays(index),
                    RedBalls = new[] { 1, 5, 10, 15, 20, 25 }, BlueBall = index % 16 + 1
                }).ToList();
                var matrix = new MatrixGrid();
                matrix.SetFullData(rows);
                matrix.Render(rows, true, true, 12);
                var originalCells = matrix.Children.Cast<UIElement>().ToHashSet();
                // Hide existing text first: a recycled cell must not retain Hidden.
                matrix.Render(rows, false, false, 12);
                rows[^1].RedBalls = new[] { 2, 6, 11, 16, 21, 26 };
                matrix.Render(rows, true, true, 12, true);
                Assert(matrix.Children.Cast<UIElement>().Any(originalCells.Contains), "full rebuild reuses cells");
                CompareFresh(12, true, true, true);
                matrix.Render(rows, false, false, 3);
                CompareFresh(3, false, false, false);
                matrix.Render(rows, true, true, 12);
                CompareFresh(12, true, true, false);
                matrix.Render(new List<DrawRecord>(), true, true, 0);
                matrix.Render(rows, true, true, 12);
                CompareFresh(12, true, true, false);

                void CompareFresh(int count, bool miss, bool cold, bool route)
                {
                    var fresh = new MatrixGrid();
                    fresh.SetFullData(rows);
                    fresh.Render(rows, miss, cold, count, route);
                    Assert(TrendPerformanceMeasurements.VisualHash(matrix) == TrendPerformanceMeasurements.VisualHash(fresh),
                        "reused cells match a fresh matrix after visibility, order or window changes");
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Trend cell reuse verification failed.", failure);
    }

    private static void VerifyTrendCacheInvalidation()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var all = Enumerable.Range(0, 8).Select(index => new DrawRecord
                {
                    Period = 2026001 + index, DrawDate = new DateTime(2026, 1, 1).AddDays(index),
                    RedBalls = new[] { 1, 5, 10, 15, 20, 25 }, BlueBall = 4
                }).ToList();
                var rows = all.TakeLast(4).ToList();
                var matrix = new MatrixGrid();
                matrix.SetFullData(all);
                matrix.Render(rows, true, true, 4);
                var firstCell = matrix.RowDefinitions[0];
                string initial = TrendPerformanceMeasurements.VisualHash(matrix);
                rows = rows.ToList();
                matrix.Render(rows, true, true, 4);
                Assert(ReferenceEquals(firstCell, matrix.RowDefinitions[0]), "equal list reuses controls");
                Assert(initial == TrendPerformanceMeasurements.VisualHash(matrix), "equal list preserves pixels");

                CheckMutation(() => rows[^1].Period++, "period correction");
                CheckMutation(() => rows[^1].DrawDate = rows[^1].DrawDate.AddDays(1), "date correction");
                CheckMutation(() => rows[^1].RedBalls = new[] { 2, 6, 11, 16, 21, 26 }, "red correction");
                CheckMutation(() => rows[^1].BlueBall = 9, "blue correction");
                CheckMutation(() => all[3].BlueBall = 16, "history outside visible rows");

                firstCell = matrix.RowDefinitions[0];
                matrix.Render(rows, true, true, 3);
                Assert(!ReferenceEquals(firstCell, matrix.RowDefinitions[0]), "period limit invalidates controls");
                firstCell = matrix.RowDefinitions[0];
                matrix.Render(rows, true, true, 3, true);
                Assert(!ReferenceEquals(firstCell, matrix.RowDefinitions[0]), "012 invalidates controls");
                matrix.Render(new List<DrawRecord>(), true, true, 3, true);
                matrix.Render(rows, true, true, 4);
                CompareFresh("empty recovery");

                void CheckMutation(Action mutate, string name)
                {
                    var prior = matrix.RowDefinitions[0];
                    mutate();
                    matrix.Render(rows, true, true, 4);
                    Assert(!ReferenceEquals(prior, matrix.RowDefinitions[0]), name + " invalidates controls");
                    CompareFresh(name);
                }

                void CompareFresh(string name)
                {
                    var fresh = new MatrixGrid();
                    fresh.SetFullData(all);
                    fresh.Render(rows, true, true, 4);
                    Assert(TrendPerformanceMeasurements.VisualHash(matrix) == TrendPerformanceMeasurements.VisualHash(fresh),
                        name + " matches fresh rendering");
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Trend cache verification failed.", failure);
    }
}
