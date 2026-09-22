using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public interface IPositionPredictor
{
    string RuleVersionId { get; }
    IReadOnlyList<int> GetIssueOptions();
    PositionPrediction Predict(int issue, string runMode = "live");
    PositionBacktestReport Backtest(
        int sampleSize = 200,
        int offsetFromLatest = 0,
        CancellationToken cancellationToken = default);
}
