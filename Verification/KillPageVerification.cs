using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SsqAnalyzer.Pages;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    private static void VerifyKillPageInteractions()
    {
        var empty = new BacktestWindowStat(0, 0, 0, 0, true);
        var poor = new BacktestWindowStat(30, 100, 60, .6, false)
        {
            ErrorSamples = new[] { new BacktestErrorSample(2026001, new[] { 1 }, new[] { 1 }, new[] { 1 }) }
        };
        var good = new BacktestWindowStat(50, 100, 95, .95, false);
        KillRule Rule(string id, string name, string description, BallType ball, BacktestStatsSnapshot? stats) => new()
        {
            RuleId = id, Name = name, Description = description, BallType = ball,
            Category = RuleCategory.Formula, JsCode = "return [1];", BacktestStats = stats
        };
        var first = Rule("Z-01", "Alpha", "Zulu description", BallType.Red, new(poor, good, empty, DateTime.UtcNow));
        var second = Rule("A-02", "Zulu", "Alpha description", BallType.Blue, null);
        var third = Rule("B-03", "Beta", "Middle description", BallType.Red, new(empty, empty, empty, DateTime.UtcNow));
        var repo = new ListRuleRepository(first, second, third);
        var data = new FakeDataService();
        var page = new KillPage(data, repo, DispatchProxy.Create<IKillEngine, OfflineDependencyProxy>(),
            DispatchProxy.Create<IBacktestEngine, OfflineDependencyProxy>(), new RuleContextBuilder(data),
            new KillSettings(), new GroupInputStore());
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var grid = (DataGrid)page.FindName("RulesGrid");
        List<RuleRowViewModel> Rows() => grid.Items.Cast<RuleRowViewModel>().ToList();
        void Sort(string key) => typeof(KillPage).GetMethod("RulesGrid_Sorting", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, new object[] { grid, new DataGridSortingEventArgs(grid.Columns.Single(c => c.SortMemberPath == key)) });
        Assert(Rows()[1].GateLabel == "待判定" && Rows()[2].GateLabel == "待判定", "untested and empty samples are not labelled passed");
        grid.SelectedItem = Rows()[0];
        Sort("Name");
        Assert(Rows().Select(r => r.Name).SequenceEqual(new[] { "Alpha", "Beta", "Zulu" }), "name ascending uses names");
        Assert(((RuleRowViewModel)grid.SelectedItem).RuleId == first.RuleId, "sorting preserves selection");
        page.LoadData();
        Assert(Rows()[0].Name == "Alpha" && grid.Columns.Single(c => c.SortMemberPath == "Name").SortDirection == ListSortDirection.Ascending, "refresh preserves sorting and arrow");
        Sort("Name");
        Assert(Rows()[0].Name == "Zulu", "name descending");
        Sort("Name");
        Assert(Rows().Select(r => r.RuleId).SequenceEqual(new[] { "Z-01", "A-02", "B-03" }), "third click restores repository order");
        Sort("Description");
        Assert(Rows()[0].RuleId == second.RuleId, "description sorts by text");
        Sort("AccuracyValue");
        Sort("AccuracyValue");
        Assert(Rows()[0].RuleId == first.RuleId && Rows().Skip(1).All(r => r.AccuracyValue is null), "unknown accuracy always last");
        ((TextBox)page.FindName("RuleSearch")).Text = "alpha";
        Assert(Rows().Count == 2, "search matches name and description case insensitively");
        ((ComboBox)page.FindName("BallFilter")).SelectedIndex = 1;
        Assert(Rows().Single().RuleId == first.RuleId, "filters combine");
        ((TextBox)page.FindName("RuleSearch")).Text = "no match";
        Assert(Rows().Count == 0 && ((TextBlock)page.FindName("EmptyHint")).Visibility == Visibility.Visible, "filtered empty state");
        InvokeClick(page, "ResetList_Click");
        Assert(Rows().Count == 3 && grid.Columns.All(c => c.SortDirection is null), "reset clears filters and sorting");
        ((Button)page.FindName("Btn30")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(Rows()[0].AccuracyValue == .6 && Rows()[0].GateLabel == "✗不达标", "window click refreshes accuracy and gate");
        page.LoadData();
        Assert(Rows()[0].AccuracyValue == .6, "reload preserves window");
        var checkbox = new CheckBox { DataContext = Rows()[0], IsChecked = false };
        typeof(KillPage).GetMethod("EnabledCheckBox_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, new object[] { checkbox, new RoutedEventArgs() });
        Assert(!first.IsEnabled && !Rows()[0].IsEnabled && repo.Updates == 1, "toggle persists and redraws");
        foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
        {
            Sort("GateSortValue");
            var source = grid.ItemsSource;
            var order = Rows().Select(r => r.RuleId).ToArray();
            var target = Rows().Single(r => r.RuleId == first.RuleId);
            grid.SelectedItem = target;
            for (int click = 0; click < 2; click++)
            {
                var toggle = new CheckBox { DataContext = target, IsChecked = !target.IsEnabled };
                typeof(KillPage).GetMethod("EnabledCheckBox_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(page, new object[] { toggle, new RoutedEventArgs() });
                PumpDispatcher(); // Include the repository notification, not only the click handler.
                Assert(ReferenceEquals(source, grid.ItemsSource), "toggle after gate sorting must not replace ItemsSource");
                Assert(ReferenceEquals(target, grid.SelectedItem), "toggle keeps selected row instance");
                Assert(Rows().Select(r => r.RuleId).SequenceEqual(order), "toggle preserves gate-sorted order");
                Assert(grid.Columns.Single(c => c.SortMemberPath == "GateSortValue").SortDirection == direction,
                    "toggle and queued refresh preserve gate sort arrow");
                Assert(target.IsEnabled == first.IsEnabled, "in-place checkbox value follows saved value");
            }
        }
        ((ComboBox)page.FindName("StateFilter")).SelectedIndex = 2;
        Assert(Rows().Single().RuleId == first.RuleId, "disabled filter");
        InvokeClick(page, "ResetList_Click");
        page.Width = 1200; page.Height = 720;
        page.Measure(new Size(1200, 720)); page.Arrange(new Rect(0, 0, 1200, 720)); page.UpdateLayout();
        SaveKillPreview(page, "kill-page.png", 1200, 720);
        var details = new KillRuleDetailWindow(first, new KillSettings(), 30);
        UIElement Errors(int window) => (UIElement)typeof(KillRuleDetailWindow)
            .GetMethod("BuildErrorSamplesSection", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { first.BacktestStats!, window })!;
        Assert(Errors(30) is StackPanel, "selected window contains its own error sample");
        Assert(Errors(50) is TextBlock text && text.Text.Contains("未记录"), "empty selected window does not borrow errors from another window");
        var content = (FrameworkElement)details.Content;
        content.Measure(new Size(760, 680)); content.Arrange(new Rect(0, 0, 760, 680)); content.UpdateLayout();
        SaveKillPreview(content, "kill-details.png", 760, 680);
        details.Close();
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        var preview = new KillPage(data, new ListRuleRepository(BuiltinRules.LoadAll().Cast<IKillRule>().ToArray()),
            DispatchProxy.Create<IKillEngine, OfflineDependencyProxy>(), DispatchProxy.Create<IBacktestEngine, OfflineDependencyProxy>(),
            new RuleContextBuilder(data), new KillSettings(), new GroupInputStore());
        preview.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        foreach (int width in new[] { 1200, 900 })
        {
            preview.Width = width; preview.Height = 720;
            preview.Measure(new Size(width, 720)); preview.Arrange(new Rect(0, 0, width, 720)); preview.UpdateLayout();
            SaveKillPreview(preview, $"kill-rules-{width}.png", width, 720);
        }
        var previewGrid = (DataGrid)preview.FindName("RulesGrid");
        typeof(KillPage).GetMethod("RulesGrid_Sorting", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(preview, new object[] { previewGrid, new DataGridSortingEventArgs(previewGrid.Columns.Single(c => c.SortMemberPath == "GateSortValue")) });
        PumpDispatcher();
        preview.UpdateLayout();
        var viewer = FindVisual<ScrollViewer>(previewGrid)!;
        viewer.ScrollToVerticalOffset(10);
        viewer.ScrollToHorizontalOffset(60);
        PumpDispatcher();
        preview.UpdateLayout();
        double vertical = viewer.VerticalOffset, horizontal = viewer.HorizontalOffset;
        Assert(vertical > 0, "checkbox regression uses a scrolled real grid");
        var item = (RuleRowViewModel)previewGrid.Items[12];
        previewGrid.SelectedItem = item;
        var container = (DataGridRow)previewGrid.ItemContainerGenerator.ContainerFromItem(item);
        var realCheckbox = FindVisual<CheckBox>(container)!;
        var originalSource = previewGrid.ItemsSource;
        for (int click = 0; click < 2; click++)
        {
            realCheckbox.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, !item.IsEnabled);
            realCheckbox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            PumpDispatcher();
            preview.UpdateLayout();
            Assert(ReferenceEquals(originalSource, previewGrid.ItemsSource)
                && ReferenceEquals(container, previewGrid.ItemContainerGenerator.ContainerFromItem(item)),
                "real checkbox preserves grid source and row container after repository notification");
            Assert(viewer.VerticalOffset == vertical && viewer.HorizontalOffset == horizontal,
                "real checkbox preserves vertical and horizontal scroll");
            Assert(realCheckbox.IsChecked == item.IsEnabled, "real checkbox binding receives in-place row update");
        }
        preview.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindVisual<T>(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }

    private static void SaveKillPreview(FrameworkElement element, string name, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, name)); encoder.Save(file);
    }

    private sealed class ListRuleRepository(params IKillRule[] rules) : IRuleRepository
    {
        public int Updates { get; private set; }
        public event Action? RulesChanged;
        public IReadOnlyList<IKillRule> GetAll() => rules;
        public IReadOnlyList<IKillRule> GetEnabled() => rules.Where(r => r.IsEnabled).ToList();
        public IKillRule? Find(string id) => rules.FirstOrDefault(r => r.RuleId == id);
        public void Update(IKillRule rule) { Updates++; RulesChanged?.Invoke(); }
        public void Add(IKillRule rule) => throw new NotSupportedException();
        public void Delete(string id) => throw new NotSupportedException();
        public void SaveBacktestStats(string id, BacktestStatsSnapshot stats) => throw new NotSupportedException();
    }
}
