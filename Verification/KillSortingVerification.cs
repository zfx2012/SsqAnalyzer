using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SsqAnalyzer.Pages;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    private static void VerifyAllKillSortColumns()
    {
        var rules = Enumerable.Range(0, 60).Select(i => new KillRule
        {
            RuleId = $"RULE-{59 - i:D3}", Name = $"Name {i % 7}", Description = $"Description {i % 11}",
            BallType = i % 2 == 0 ? BallType.Red : BallType.Blue,
            Category = i % 3 == 0 ? RuleCategory.Pattern : RuleCategory.Formula,
            IsBuiltin = i % 4 == 0, IsEnabled = i % 3 != 0, ForceEnabled = i % 5 == 0,
            JsCode = "return [1];",
            BacktestStats = i % 6 == 0 ? null : Snapshot(i)
        }).ToArray();
        var data = new FakeDataService();
        var page = new KillPage(data, new ListRuleRepository(rules), DispatchProxy.Create<IKillEngine, OfflineDependencyProxy>(),
            DispatchProxy.Create<IBacktestEngine, OfflineDependencyProxy>(), new RuleContextBuilder(data), new KillSettings(), new GroupInputStore());
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        page.Width = 900; page.Height = 720;
        page.Measure(new Size(900, 720)); page.Arrange(new Rect(0, 0, 900, 720)); page.UpdateLayout();
        var grid = (DataGrid)page.FindName("RulesGrid");
        var columns = grid.Columns.Where(c => c.CanUserSort).ToArray();
        Assert(columns.Length == 11, "all eleven sortable kill columns covered");
        List<RuleRowViewModel> Rows() => grid.Items.Cast<RuleRowViewModel>().ToList();
        void Sort(DataGridColumn column)
        {
            typeof(KillPage).GetMethod("RulesGrid_Sorting", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, new object[] { grid, new DataGridSortingEventArgs(column) });
            PumpDispatcher(); page.UpdateLayout();
        }
        foreach (var column in columns)
        {
            InvokeClick(page, "ResetList_Click");
            foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
            {
                var expected = Expected(Rows(), column.SortMemberPath, direction).Select(r => r.RuleId).ToArray();
                Sort(column);
                Assert(Rows().Select(r => r.RuleId).SequenceEqual(expected), $"{column.Header}: correct {direction}, including ties and missing stats");
                var viewer = FindVisual<ScrollViewer>(grid)!;
                viewer.ScrollToVerticalOffset(10); viewer.ScrollToHorizontalOffset(60);
                PumpDispatcher(); page.UpdateLayout();
                double vertical = viewer.VerticalOffset, horizontal = viewer.HorizontalOffset;
                Assert(vertical > 0 && horizontal > 0, $"both scroll axes exercised: {vertical}/{horizontal}; " + string.Join(";", grid.Columns.Select(c => $"{c.Header}:{c.Width}/{c.ActualWidth}")));
                var row = Rows()[12]; grid.SelectedItem = row;
                var container = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(row);
                var checkbox = FindVisual<CheckBox>(container)!;
                var source = grid.ItemsSource;
                for (int click = 0; click < 2; click++)
                {
                    checkbox.SetCurrentValue(ToggleButton.IsCheckedProperty, !row.IsEnabled);
                    checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    PumpDispatcher(); page.UpdateLayout();
                    Assert(ReferenceEquals(source, grid.ItemsSource) && ReferenceEquals(container, grid.ItemContainerGenerator.ContainerFromItem(row)),
                        $"{column.Header}: toggle and queued notification preserve containers");
                    Assert(Rows().Select(r => r.RuleId).SequenceEqual(expected) && column.SortDirection == direction,
                        $"{column.Header}: toggle preserves ordering and arrow");
                    Assert(ReferenceEquals(row, grid.SelectedItem) && viewer.VerticalOffset == vertical && viewer.HorizontalOffset == horizontal,
                        $"{column.Header}: toggle preserves selection and scroll");
                    Assert(checkbox.IsChecked == rules.Single(r => r.RuleId == row.RuleId).IsEnabled, "saved checkbox state visible");
                }
                page.LoadData(); PumpDispatcher(); page.UpdateLayout();
                Assert(Rows().Select(r => r.RuleId).SequenceEqual(expected) && column.SortDirection == direction, $"{column.Header}: reload preserves sort");
            }
            Sort(column);
            Assert(column.SortDirection is null && Rows().Select(r => r.RuleId).SequenceEqual(rules.Select(r => r.RuleId)), $"{column.Header}: third click restores default");
        }
        var enabledColumn = columns.Single(c => c.SortMemberPath == "IsEnabled");
        Sort(enabledColumn);
        var edited = Rows()[12];
        typeof(KillPage).GetMethod("EnabledCheckBox_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, new object[] { new CheckBox { DataContext = edited, IsChecked = !edited.IsEnabled }, new RoutedEventArgs() });
        PumpDispatcher();
        var latest = Expected(Rows(), "IsEnabled", ListSortDirection.Descending).Select(r => r.RuleId).ToArray();
        Sort(enabledColumn);
        Assert(Rows().Select(r => r.RuleId).SequenceEqual(latest), "explicit enabled header click sorts latest edited states");
        ((ComboBox)page.FindName("StateFilter")).SelectedIndex = 1;
        var enabled = Rows()[0];
        typeof(KillPage).GetMethod("EnabledCheckBox_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, new object[] { new CheckBox { DataContext = enabled, IsChecked = false }, new RoutedEventArgs() });
        PumpDispatcher();
        Assert(Rows().All(r => r.IsEnabled) && Rows().All(r => r.RuleId != enabled.RuleId), "enabled filter removes newly disabled row without losing filter");
        InvokeClick(page, "ResetList_Click");
        var nameColumn = columns.Single(c => c.SortMemberPath == "Name");
        Sort(nameColumn);
        var allRows = Rows();
        var expectedVisible = allRows.Where(r => r.GateSortValue != 4).Select(r => r.RuleId).ToArray();
        Assert(expectedVisible.Length < allRows.Count, "hide-failed fixture contains failed rules");
        var enabledBefore = rules.Select(r => r.IsEnabled).ToArray();
        var hideFailed = (CheckBox)page.FindName("HideFailedCheckBox");
        hideFailed.IsChecked = true;
        Assert(Rows().Select(r => r.RuleId).SequenceEqual(expectedVisible), "hide failed preserves active sorting");
        Assert(Rows().Any(r => r.GateSortValue == 1) && Rows().Any(r => r.GateSortValue == 2)
            && Rows().Any(r => r.GateSortValue == 3), "hide failed retains forced, untested and insufficient rules");
        page.LoadData(); PumpDispatcher();
        Assert(Rows().Select(r => r.RuleId).SequenceEqual(expectedVisible), "reload preserves hide-failed filter");
        ((ComboBox)page.FindName("BallFilter")).SelectedIndex = 1;
        Assert(Rows().All(r => r.BallTypeLabel == "红球" && r.GateSortValue != 4), "hide failed combines with ball filter");
        Assert(rules.Select(r => r.IsEnabled).SequenceEqual(enabledBefore), "hide failed never changes rule enablement");
        InvokeClick(page, "ResetList_Click");
        Assert(hideFailed.IsChecked == false && Rows().Count == rules.Length, "reset restores failed rules");
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        static BacktestStatsSnapshot Snapshot(int i)
        {
            var stat = new BacktestWindowStat(50, 100, 60 + i % 4 * 10, .6 + i % 4 * .1, i % 6 == 1);
            return new(stat, stat, stat, new DateTime(2026, 1, 1));
        }
        static IEnumerable<RuleRowViewModel> Expected(List<RuleRowViewModel> rows, string key, ListSortDirection direction)
        {
            IComparable Value(RuleRowViewModel row) => key switch
            {
                "RuleId" => row.RuleId, "Name" => row.Name, "Description" => row.Description,
                "BallTypeLabel" => row.BallTypeLabel, "CategoryLabel" => row.CategoryLabel, "SourceLabel" => row.SourceLabel,
                "IsEnabled" => row.IsEnabled, "GateSortValue" => row.GateSortValue,
                "ExecutionLabel" => row.ExecutionLabel, "ActualWrongCount" => row.ActualWrongCount,
                "AccuracyValue" => row.AccuracyValue ?? 0, _ => throw new InvalidOperationException(key)
            };
            var available = rows.Where(r => key != "AccuracyValue" || r.AccuracyValue.HasValue);
            var sorted = direction == ListSortDirection.Ascending ? available.OrderBy(Value) : available.OrderByDescending(Value);
            return sorted.ThenBy(r => r.RuleId).Concat(key == "AccuracyValue"
                ? rows.Where(r => r.AccuracyValue is null).OrderBy(r => r.RuleId, StringComparer.Ordinal) : Array.Empty<RuleRowViewModel>());
        }
    }
}
