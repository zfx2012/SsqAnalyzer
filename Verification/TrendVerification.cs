using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using SsqAnalyzer.Controls;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

internal static partial class VerificationSuite
{
    private static void VerifyTrendMatrixLifecycleAndInteraction()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var matrix = new MatrixGrid();
                matrix.Render(new List<DrawRecord>(), true, true, 30);
                Assert(matrix.IsInitialized, "empty trend matrix completes initialization");
    
                var records = new List<DrawRecord>
                {
                    new() { Period = 2026001, DrawDate = new DateTime(2026, 1, 1), RedBalls = new[] { 1, 5, 14, 20, 25, 30 }, BlueBall = 4 },
                    new() { Period = 2026002, DrawDate = new DateTime(2026, 1, 4), RedBalls = new[] { 1, 6, 15, 21, 27, 32 }, BlueBall = 4 },
                    new() { Period = 2026003, DrawDate = new DateTime(2026, 1, 6), RedBalls = new[] { 1, 7, 10, 11, 12, 24 }, BlueBall = 4 },
                    new() { Period = 2026004, DrawDate = new DateTime(2026, 1, 8), RedBalls = new[] { 2, 8, 13, 18, 23, 28 }, BlueBall = 5 }
                };
                matrix.SetFullData(records);
                matrix.Render(records, true, true, records.Count);
                Assert(matrix.RowDefinitions.Count == records.Count + 4, "trend matrix recovers after empty data");
                Assert(HistoricalBallColor(matrix, 1, true, 1) == System.Windows.Media.Color.FromRgb(0x91, 0x22, 0xD0),
                    "three repeated draws use purple");
                Assert(HistoricalBallColor(matrix, 2, true, 6) == System.Windows.Media.Color.FromRgb(0xFF, 0x78, 0x00),
                    "three diagonal draws use orange");
                Assert(HistoricalBallColor(matrix, 3, true, 11) == System.Windows.Media.Color.FromRgb(0x5F, 0x73, 0x6F),
                    "three consecutive balls use gray green");
                Assert(HistoricalBallColor(matrix, 1, true, 14) == System.Windows.Media.Color.FromRgb(0xE0, 0x48, 0x48),
                    "ordinary red ball keeps its base color");
    
                var selections = (IDictionary)typeof(MatrixGrid)
                    .GetField("_selections", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(matrix)!;
                selections[(0, true, 1)] = true;
                var routeRecords = new List<DrawRecord>
                {
                    new() { Period = 2026001, DrawDate = new DateTime(2026, 1, 1), RedBalls = new[] { 2, 8, 14, 20, 26, 33 }, BlueBall = 3 },
                    new() { Period = 2026002, DrawDate = new DateTime(2026, 1, 4), RedBalls = new[] { 1, 3, 10, 16, 22, 28 }, BlueBall = 6 },
                    new() { Period = 2026003, DrawDate = new DateTime(2026, 1, 6), RedBalls = new[] { 4, 5, 11, 17, 23, 29 }, BlueBall = 9 },
                    new() { Period = 2026004, DrawDate = new DateTime(2026, 1, 8), RedBalls = new[] { 6, 7, 12, 18, 24, 30 }, BlueBall = 12 }
                };
                matrix.SetFullData(routeRecords);
                matrix.Render(routeRecords, true, true, routeRecords.Count, showZO2: true);
                int predRow = records.Count + 1;
                var selectedOne = matrix.Children.OfType<Border>().SingleOrDefault(border =>
                    Grid.GetRow(border) == predRow && Equals(border.Tag, "ball:red:1"));
                Assert(selectedOne != null && Grid.GetColumn(selectedOne) == Array.IndexOf(MatrixGrid.RedOrderZO2, 1) + 2,
                    "012 mode preserves selected ball identity");
                Assert(matrix.GetTrendColumn(33, true) == 10
                    && matrix.GetTrendColumn(1, true) == 11
                    && matrix.GetTrendColumn(4, true) == 12,
                    "012 diagonal logic uses reordered red columns");
                Assert(HistoricalBallColor(matrix, 2, true, 1) == System.Windows.Media.Color.FromRgb(0xFF, 0x78, 0x00),
                    "012 three-diagonal color follows reordered columns");
    
                var overlay = new Grid();
                var renderer = new DiagonalChainRenderer(matrix, overlay);
                var renderPending = typeof(DiagonalChainRenderer)
                    .GetMethod("RenderPending", BindingFlags.Instance | BindingFlags.NonPublic)!;
                renderer.Update(routeRecords, 0);
                renderer.Clear();
                renderPending.Invoke(renderer, null);
                Assert(overlay.Visibility == Visibility.Collapsed, "cleared diagonal render stays cancelled");
    
                renderer.Update(routeRecords, 0);
                renderPending.Invoke(renderer, null);
                Assert(overlay.Visibility == Visibility.Visible, "diagonal test data produces an overlay");
                var recentReds = (List<List<int>>)typeof(DiagonalChainRenderer)
                    .GetField("_recentReds", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(renderer)!;
                var chains = (IEnumerable)typeof(DiagonalChainRenderer)
                    .GetMethod("FindChains", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { recentReds, new List<int> { 3, 2, 1, 0 }, 0, 32, 3, 0 })!;
                Assert(chains.Cast<object>().Any(chain =>
                    (int)chain.GetType().GetProperty("PredCol")!.GetValue(chain)! == 14
                    && (int)chain.GetType().GetProperty("Step")!.GetValue(chain)! == 1),
                    "012 diagonal prediction continues in reordered column space");
                typeof(DiagonalChainRenderer)
                    .GetMethod("OnMatrixSizeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(renderer, new object?[] { matrix, null });
                bool resizePending = (bool)typeof(DiagonalChainRenderer)
                    .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(renderer)!;
                Assert(resizePending, "visible diagonal overlay schedules resize redraw");
                renderer.Clear();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new InvalidOperationException("trend matrix verification failed", failure);
    }
    private static System.Windows.Media.Color HistoricalBallColor(MatrixGrid matrix, int row, bool isRed, int number)
    {
        string tag = $"ball:{(isRed ? "red" : "blue")}:{number}";
        var border = matrix.Children.OfType<Border>().Single(item =>
            Grid.GetRow(item) == row && Equals(item.Tag, tag));
        var panel = (Grid)border.Child;
        return ((System.Windows.Media.SolidColorBrush)panel.Children.OfType<System.Windows.Shapes.Ellipse>().Single().Fill).Color;
    }
}
