using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SsqAnalyzer.Models;
using SsqAnalyzer.Pages;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    // Called on the existing lifecycle suite's STA/Application, without opening windows.
    private static void VerifyAsyncPageUpdates()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            var data = new FakeDataService();
            data.SetRecords(BuildRecords(12).ToArray());
            var inner = new PositionPredictor(data);
            foreach (bool fail in new[] { false, true })
            {
                using var predictor = new DelayedPredictor(inner, fail);
                var page = new PositionPage(data, predictor,
                    DispatchProxy.Create<IPositionValidationStore, OfflineDependencyProxy>(), new GroupInputStore());
                page.LoadData();
                InvokeClick(page, "Backtest_Click");
                PumpUntil(() => predictor.Started.IsSet);
                page.LoadData();
                string currentStatus = ((TextBlock)page.FindName("StatusText")).Text;
                Assert(predictor.Token.IsCancellationRequested, "data reload cancels running position backtest");
                predictor.Release.Set();
                PumpUntil(() => ((Button)page.FindName("BacktestButton")).IsEnabled);
                Assert(((TextBlock)page.FindName("StatusText")).Text == currentStatus,
                    "late backtest success or failure cannot replace current prediction status");
            }
            using (var predictor = new DelayedPredictor(inner, false))
            {
                var page = new PositionPage(data, predictor,
                    DispatchProxy.Create<IPositionValidationStore, OfflineDependencyProxy>(), new GroupInputStore());
                InvokeClick(page, "Backtest_Click");
                PumpUntil(() => predictor.Started.IsSet);
                page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Assert(predictor.Token.IsCancellationRequested, "unloading position page cancels backtest");
                ((TextBlock)page.FindName("StatusText")).Text = "new page state";
                predictor.Release.Set();
                PumpUntil(() => ((Button)page.FindName("BacktestButton")).IsEnabled);
                Assert(((TextBlock)page.FindName("StatusText")).Text == "new page state", "unloaded page ignores late report");
            }

            var store = DispatchProxy.Create<ITicketStore, OfflineDependencyProxy>();
            var search = new DelayedVideoSearch();
            var video = new CompoundStatsPage(store, search, new DownloadService(), new AiAnalysisService());
            var box = (TextBox)video.FindName("SearchBox");
            foreach (bool fail in new[] { false, true })
            {
                int start = search.Requests.Count;
                box.Text = "12345";
                InvokeClick(video, "SearchVideo_Click");
                box.Text = "23456";
                InvokeClick(video, "SearchVideo_Click");
                search.Requests[start + 1].SetResult(VideoResult("new result"));
                PumpUntil(() => video.SearchResults.Any(item => item.Title == "new result"));
                if (fail) search.Requests[start].SetException(new IOException("old request failed"));
                else search.Requests[start].SetResult(VideoResult("old result"));
                PumpDispatcher();
                Assert(video.SearchResults.Single().Title == "new result", "late search cannot overwrite newer result");
            }

            var nl = new DelayedNlService();
            var repo = new CancellationRepository();
            using var noCancel = new CancellationTokenSource();
            var executor = new CancellingExecutor(noCancel, int.MaxValue, false);
            var builder = new RuleContextBuilder(data);
            var window = new AddRuleWindow(repo, executor, new BacktestEngine(executor, builder, repo, data),
                builder, store, nl, new KillSettings());
            ((TextBox)window.FindName("NlDescBox")).Text = "original description";
            InvokeClick(window, "Generate_Click");
            ((TextBox)window.FindName("CodeBox")).Text = "user edited code";
            Assert(nl.Token.IsCancellationRequested, "editing form cancels code generation");
            nl.Result.SetResult("old generated code");
            PumpUntil(() => ((Button)window.FindName("BtnGenerate")).IsEnabled);
            Assert(((TextBox)window.FindName("CodeBox")).Text == "user edited code", "late code cannot replace manual edits");
            window.Close();

            var handler = new DelayedHttpHandler();
            var config = new LlmApiConfigDialog(store, new HttpClient(handler));
            ((TextBox)config.FindName("UrlBox")).Text = "https://offline.invalid/v1";
            ((PasswordBox)config.FindName("ApiKeyBox")).Password = "offline-test-key";
            ((ComboBox)config.FindName("ModelBox")).Text = "chosen-model";
            InvokeClick(config, "FetchModels_Click");
            ((TextBox)config.FindName("UrlBox")).Text = "https://changed.invalid/v1";
            Assert(handler.Token.IsCancellationRequested, "editing endpoint cancels model request");
            handler.Result.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[{\"id\":\"old-model\"}]}")
            });
            PumpUntil(() => ((Button)config.FindName("BtnFetchModels")).IsEnabled);
            Assert(((ComboBox)config.FindName("ModelBox")).Text == "chosen-model", "old endpoint cannot replace selected model");
            config.Close();
            new BiliLoginDialog().Close(); // Closing before Loaded must also release lifetime safely.

            var settingsData = new DelayedDataService();
            var settings = new SettingsPage(settingsData, store, store);
            InvokeClick(settings, "BtnUpdate_Click");
            typeof(SettingsPage).GetMethod("ShowSimplePopup", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(settings, new object[] { "info", "new popup" });
            settingsData.Result.SetResult(0);
            PumpUntil(() => ((Button)settings.FindName("BtnUpdate")).IsEnabled);
            Assert(((TextBlock)settings.FindName("PopupText")).Text == "new popup"
                && ((FrameworkElement)settings.FindName("PopupOverlay")).Visibility == Visibility.Visible,
                "old data update cannot replace or dismiss a newer popup");

            var snapshots = new GroupInputStore();
            foreach (var page in new UserControl[]
            {
                new RecordsPage(data), new SettingsPage(data, store, store),
                new GroupPage(data, store, snapshots, new GroupService(data, store, snapshots)),
                new KillPage(data, repo, DispatchProxy.Create<IKillEngine, OfflineDependencyProxy>(),
                    new BacktestEngine(executor, builder, repo, data), builder,
                    new KillSettings(), snapshots)
            })
            {
                page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                PumpDispatcher();
                int reads = data.ReadCount;
                page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                PumpDispatcher();
                Assert(data.ReadCount == reads && data.SubscriberCount == 1, "repeated load avoids duplicate page subscriptions");
                data.NotifyDataUpdated();
                page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                PumpDispatcher();
                Assert(data.ReadCount == reads && data.SubscriberCount == 0, "unloaded page ignores queued data refresh");
            }
            var ticketPage = new TicketsPage(store);
            var proxy = (OfflineDependencyProxy)(object)store;
            ticketPage.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            PumpDispatcher();
            Assert(proxy.SubscriptionCount("PeriodChanged") == 1, "loaded ticket page subscribes once to period changes");
            ticketPage.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert(proxy.SubscriptionCount("PeriodChanged") == 0, "ticket page removes period subscription on unload");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void InvokeClick(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, new object[] { target, new RoutedEventArgs() });

    private static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("UI fixture did not finish.");
            PumpDispatcher();
            Thread.Sleep(1);
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static JsonNode VideoResult(string title) => JsonNode.Parse(
        "{\"code\":0,\"data\":{\"list\":{\"vlist\":[{\"bvid\":\"BVfixture\",\"title\":\"" + title + "\"}]}}}")!;

    private sealed class DelayedPredictor(IPositionPredictor inner, bool fail) : IPositionPredictor, IDisposable
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public CancellationToken Token { get; private set; }
        public string RuleVersionId => inner.RuleVersionId;
        public IReadOnlyList<int> GetIssueOptions() => inner.GetIssueOptions();
        public PositionPrediction Predict(int issue, string runMode = "live") => inner.Predict(issue, runMode);
        public PositionBacktestReport Backtest(int sampleSize = 200, int offsetFromLatest = 0, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Started.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Backtest fixture not released.");
            if (fail) throw new InvalidOperationException("late fixture failure");
            return inner.Backtest(2); // Deliberately ignores cancellation to test the UI guard.
        }
        public void Dispose() { Release.Set(); }
    }

    private sealed class DelayedVideoSearch : IVideoSearchService
    {
        public List<TaskCompletionSource<JsonNode?>> Requests { get; } = new();
        public Task<JsonNode?> SearchByUid(long uid, CancellationToken ct = default)
        {
            var request = new TaskCompletionSource<JsonNode?>();
            Requests.Add(request);
            return request.Task;
        }
        public Task<JsonNode?> SearchByBvid(string bvid, CancellationToken ct = default) => SearchByUid(0, ct);
    }

    private sealed class DelayedNlService : INlToCodeService
    {
        public TaskCompletionSource<string> Result { get; } = new();
        public CancellationToken Token { get; private set; }
        public Task<string> GenerateAsync(string description, BallType ball, RuleCategory category,
            string key, string model, string? host, CancellationToken ct = default)
        { Token = ct; return Result.Task; }
        public (string System, string User) BuildPrompt(string description, BallType ball, RuleCategory category) => ("", "");
        public string? ExtractJsCode(string content) => content;
    }

    private sealed class DelayedHttpHandler : HttpMessageHandler
    {
        public TaskCompletionSource<HttpResponseMessage> Result { get; } = new();
        public CancellationToken Token { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Token = ct; return Result.Task; }
    }

    private sealed class DelayedDataService : IDataService
    {
        public TaskCompletionSource<int> Result { get; } = new();
        public event Action? DataUpdated { add { } remove { } }
        public bool HasLocalFile => false;
        public string LocalDataFilePath => "";
        public string? LastErrorMessage => null;
        public List<DrawRecord> GetAllRecords() => new();
        public int GetLastPeriod() => -1;
        public int GetRecordCount() => 0;
        public Task<int> TryUpdateAsync() => Result.Task;
        public void ClearAllData() { }
        public void ResetCache() { }
        public void NotifyDataUpdated() { }
    }
}

// No credentials or user files are accessed by these UI tests.
public class OfflineDependencyProxy : DispatchProxy
{
    private readonly Dictionary<string, Delegate?> _events = new();
    public int SubscriptionCount(string name) => _events.GetValueOrDefault(name)?.GetInvocationList().Length ?? 0;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        string name = method!.Name;
        if (name.StartsWith("add_") || name.StartsWith("remove_"))
        {
            string eventName = name[(name.StartsWith("add_") ? 4 : 7)..];
            var previous = _events.GetValueOrDefault(eventName);
            _events[eventName] = name.StartsWith("add_")
                ? Delegate.Combine(previous, (Delegate?)args![0]) : Delegate.Remove(previous, (Delegate?)args![0]);
            return null;
        }
        return name switch
        {
        "get_Tickets" => new List<Ticket>(),
        "get_RedMax" => 33,
        "get_BlueMax" => 16,
        "LoadLlmApiKey" => "offline-test-key",
        "LoadLlmBaseUrl" => "https://offline.invalid/v1",
        "LoadLlmModel" => "offline-model",
        _ => method.ReturnType == typeof(void) ? null
            : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null
        };
    }
}
