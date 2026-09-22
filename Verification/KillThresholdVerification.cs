using System.Windows;
using System.Windows.Controls;
using SsqAnalyzer.Models;
using SsqAnalyzer.Pages;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    private static void VerifyFixedKillThresholds()
    {
        var data = new FakeDataService();
        data.SetRecords(Enumerable.Range(0, 101).Select(i => new DrawRecord
        {
            Period = 2026001 + i, DrawDate = new DateTime(2026, 1, 1).AddDays(i),
            RedBalls = new[] { 1, 2, 3, 4, 5, 6 }, BlueBall = 1
        }).ToArray());
        foreach (bool injectSettings in new[] { false, true })
        foreach (var (ball, correct, passed) in new[]
        {
            (BallType.Red, 81, false), (BallType.Red, 82, true),
            (BallType.Blue, 93, false), (BallType.Blue, 94, true)
        })
        {
            var rule = new KillRule
            {
                RuleId = "threshold", Name = "threshold", BallType = ball, Category = RuleCategory.Formula,
                JsCode = "", MinAccuracy = .1 // Legacy per-rule fields must not weaken the fixed threshold.
            };
            var repo = new CancellationRepository(rule);
            var context = new RuleContextBuilder(data);
            var settings = new KillSettings();
            var backtest = new BacktestEngine(new ThresholdExecutor(correct), context, repo, data, injectSettings ? settings : null);
            var stats = backtest.Run(rule, BacktestWindow.Last100Triggers);
            Assert(stats.Accuracy == correct / 100d && rule.IsEnabled == passed, $"{ball} {correct}% backtest gate (injected={injectSettings})");
            var engine = new KillEngine(repo, new ThresholdExecutor(0), context, injectSettings ? settings : null);
            var report = engine.Execute(new[] { rule });
            var killed = (ball == BallType.Red ? report.KilledRedBalls : report.KilledBlueBalls).Single();
            Assert(killed.Confidence == (passed ? ConfidenceLevel.Medium : ConfidenceLevel.Low), "report shares ball-specific boundary");
            var page = new KillPage(data, repo, engine, backtest, context, settings, new GroupInputStore());
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            ((Button)page.FindName("Btn100")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var row = ((DataGrid)page.FindName("RulesGrid")).Items.Cast<RuleRowViewModel>().Single();
            Assert(row.GateSortValue == (passed ? 0 : 4), "page shares backtest and report threshold");
            Assert(row.MinAccuracyLabel == (ball == BallType.Red ? "82%" : "94%"), "row shows correct fixed threshold");
            Assert(page.FindName("ThresholdSlider") is null, "manual threshold slider removed");
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
    }

    private sealed class ThresholdExecutor(int correctCount) : IRuleExecutor
    {
        private int _calls;
        public KillResult Execute(IKillRule rule, RuleContext context) => new()
        {
            RuleId = rule.RuleId, BallType = rule.BallType,
            KilledBalls = new[] { ++_calls <= correctCount ? (rule.BallType == BallType.Red ? 33 : 16) : 1 }
        };
    }
}
