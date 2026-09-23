using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SsqAnalyzer.Controls;
using SsqAnalyzer.Models;
using SsqAnalyzer.Pages;
using SsqAnalyzer.Services;

internal static partial class VerificationSuite
{
    internal static void VerifyPageLifecycle()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? app = null;
            try
            {
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/SsqAnalyzer;component/Themes/DarkTheme.xaml", UriKind.Relative)
                });
                var data = new FakeDataService();
                data.SetRecords(Record(2026001));
                var trend = new TrendPage(data, new AnalysisService(data));
                CheckLifecycle(trend, data);
                trend.LoadData();
                data.SetRecords(Record(2026003));
                trend.LoadData();
                Assert(VisibleRecords(trend).Single().Period == 2026003, "explicit reload invalidates trend filter cache");
                data.ClearAllData();
                trend.LoadData();
                Assert(VisibleRecords(trend).Count == 0, "empty reload clears trend");
                data.SetRecords(Record(2026005));
                trend.LoadData();
                Assert(VisibleRecords(trend).Single().Period == 2026005, "trend reload recovers after empty data");

                var history = new HistoryPage(data);
                CheckLifecycle(history, data);
                ((TextBox)history.FindName("HistoryPeriodInput")).Text = "005";
                typeof(HistoryPage).GetMethod("DoQuery", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(history, null);
                Assert(VisibleRecords(history).Count == 1, "history initial query");
                data.SetRecords(Record(2025005), Record(2026005));
                history.LoadData();
                Assert(VisibleRecords(history).Count == 2, "history reload refreshes active query");
                data.ClearAllData();
                history.LoadData();
                Assert(VisibleRecords(history).Count == 0, "history reload clears stale results");
                VerifyAsyncPageUpdates();
                VerifyKillPageInteractions();
                VerifyAllKillSortColumns();
                VerifyFixedKillThresholds();
                VerifyKillEvaluationWindow();
            }
            catch (Exception exception) { failure = exception; }
            finally { app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Page lifecycle verification failed.", failure);

        static DrawRecord Record(int period) => new()
        {
            Period = period, DrawDate = new DateTime(2026, 1, 1),
            RedBalls = new[] { 1, 5, 10, 15, 20, 25 }, BlueBall = 4
        };

        static List<DrawRecord> VisibleRecords(UserControl page) =>
            (List<DrawRecord>)typeof(MatrixGrid).GetField("_recent", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(page.FindName("MatrixView"))!;

        static void CheckLifecycle(UserControl page, FakeDataService data)
        {
            object? Renderer() => page.GetType().GetField("_diagRenderer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
            var renderer = Renderer();
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            int reads = data.ReadCount;
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert(data.SubscriberCount == 1 && data.ReadCount == reads, "duplicate Loaded does not subscribe or reload twice");
            data.NotifyDataUpdated();
            Drain();
            Assert(data.ReadCount == reads + 1, "one notification causes one reload");
            data.NotifyDataUpdated();
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            reads = data.ReadCount;
            Drain();
            Assert(data.SubscriberCount == 0 && data.ReadCount == reads, "unload cancels queued refresh");
            data.NotifyDataUpdated();
            Drain();
            Assert(data.ReadCount == reads, "unloaded page does not receive data updates");
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            data.NotifyDataUpdated();
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            reads = data.ReadCount;
            Drain();
            Assert(data.ReadCount == reads, "old queued refresh cannot affect reloaded page");
            Assert(ReferenceEquals(renderer, Renderer()), "reload keeps one diagonal renderer");
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert(data.SubscriberCount == 0, "final unload removes subscription");
        }

        static void Drain()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
