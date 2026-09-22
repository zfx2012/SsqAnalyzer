using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{

    private List<PositionBallScore> ScoreRedBalls(IReadOnlyList<DrawRecord> history, int issue)
    {
        var balls = Enumerable.Range(1, 33).ToArray();
        var window30 = history.TakeLast(_config.Window30).ToList();
        var window15 = history.TakeLast(_config.Window15).ToList();
        var window5 = history.TakeLast(_config.Window5).ToList();

        var omissions = balls.ToDictionary(ball => ball, ball => CalculateOmission(history, ball, false));
        var allCounts = balls.ToDictionary(ball => ball, ball => CountOccurrences(history, ball, false));
        var counts30 = balls.ToDictionary(ball => ball, ball => CountOccurrences(window30, ball, false));
        var counts15 = balls.ToDictionary(ball => ball, ball => CountOccurrences(window15, ball, false));
        var counts5 = balls.ToDictionary(ball => ball, ball => CountOccurrences(window5, ball, false));
        var seasonal = history.Where(record => IsNearIssuePosition(record.Period, issue, 4)).ToList();
        var seasonalRates = balls.ToDictionary(ball => ball, ball => Rate(CountOccurrences(seasonal, ball, false), seasonal.Count));
        var cycle = balls.ToDictionary(ball => ball, ball => CycleStability(history, ball, false));
        var transition = balls.ToDictionary(ball => ball, ball => ConditionalTransitionRate(history, ball));
        var neighbor = balls.ToDictionary(ball => ball, ball => NeighborTransitionRate(history, ball));
        var balance = BuildRedBalanceFeatures(window15, balls);

        var omissionRanks = Ranks(omissions);
        var allRanks = Ranks(allCounts);
        var ranks30 = Ranks(counts30);
        var ranks15 = Ranks(counts15);
        var ranks5 = Ranks(counts5);
        var seasonalRanks = Ranks(seasonalRates);
        var cycleRanks = Ranks(cycle);
        var transitionRanks = Ranks(transition);
        var neighborRanks = Ranks(neighbor);
        var balanceRanks = Ranks(balance);

        return balls.Select(ball =>
        {
            double longTerm = 0.55 * allRanks[ball] + 0.30 * cycleRanks[ball] + 0.15 * seasonalRanks[ball];
            double medium30 = 0.65 * ranks30[ball] + 0.35 * omissionRanks[ball];
            double short15 = 0.55 * ranks15[ball] + 0.25 * transitionRanks[ball] + 0.20 * balanceRanks[ball];
            double recentStructure = 0.45 * ranks5[ball] + 0.30 * transitionRanks[ball] + 0.25 * neighborRanks[ball];
            double total = longTerm * _config.RedLongTermWeight
                         + medium30 * _config.RedWindow30Weight
                         + short15 * _config.RedWindow15Weight
                         + recentStructure * _config.RedRecentStructureWeight;
            string direction = ranks30[ball] >= 0.70 ? "走热"
                : omissionRanks[ball] >= 0.70 ? "走冷" : "均衡";
            return new PositionBallScore(
                ball,
                omissions[ball],
                allCounts[ball],
                counts30[ball],
                counts15[ball],
                counts5[ball],
                RoundScore(longTerm),
                RoundScore(medium30),
                RoundScore(short15),
                RoundScore(recentStructure),
                RoundScore(total),
                direction);
        }).ToList();
    }

    private BlueSelection ScoreBlueBalls(IReadOnlyList<DrawRecord> history, int issue)
    {
        var balls = Enumerable.Range(1, 16).ToArray();
        var window30 = history.TakeLast(_config.Window30).ToList();
        var window16 = history.TakeLast(_config.BlueHotWindow).ToList();
        var window5 = history.TakeLast(_config.Window5).ToList();
        BlueFormulaHistory? formulaHistory = null;
        IReadOnlyList<FormulaMetric> formulas;
        if (_formulaMode == BlueFormulaMode.Legacy)
        {
            formulas = EvaluateLegacyBlueFormulas(history, issue);
        }
        else if (_formulaMode == BlueFormulaMode.Ensemble)
        {
            formulaHistory = new BlueFormulaHistory(history, _config.FormulaBacktestWindow);
            formulas = formulaHistory.Evaluate(history.Count, issue);
        }
        else
        {
            formulas = EvaluateRollingHotFormula(history, issue);
        }

        var omissions = balls.ToDictionary(ball => ball, ball => CalculateOmission(history, ball, true));
        var allCounts = balls.ToDictionary(ball => ball, ball => CountOccurrences(history, ball, true));
        var counts30 = balls.ToDictionary(ball => ball, ball => CountOccurrences(window30, ball, true));
        var counts16 = balls.ToDictionary(ball => ball, ball => CountOccurrences(window16, ball, true));
        var counts5 = balls.ToDictionary(ball => ball, ball => CountOccurrences(window5, ball, true));
        var formulaScores = _formulaMode switch
        {
            BlueFormulaMode.Legacy => CalculateLegacyFormulaScores(history, formulas),
            BlueFormulaMode.Ensemble => CalculateFormulaScores(formulas),
            BlueFormulaMode.RollingHot => CalculateRollingHotScores(history),
            _ => throw new InvalidOperationException("未知蓝球公式模式")
        };
        var exclusionFeatures = CalculateExclusionFeatures(history, history.Count);

        var scores = balls.Select(ball =>
        {
            double formulaScore = formulaScores[ball];
            double exclusionScore = exclusionFeatures[ball].Score;
            double combined = formulaScore * _config.BlueFormulaWeight
                            + exclusionScore * _config.BlueExclusionWeight;
            string votes = string.Join("+", formulas.Where(formula => formula.CurrentBall == ball)
                .OrderByDescending(formula => formula.SmoothedAccuracy)
                .Select(formula => formula.Name));
            string reason = exclusionFeatures[ball].Reason;
            return new PositionBlueScore(
                ball,
                omissions[ball],
                allCounts[ball],
                counts30[ball],
                counts16[ball],
                counts5[ball],
                RoundScore(formulaScore),
                RoundScore(exclusionScore),
                RoundScore(combined),
                votes,
                reason);
        }).ToList();

        var exclusionBlue = scores.OrderByDescending(score => score.ExclusionScore).ThenBy(score => score.Ball).First();
        PositionBlueScore formulaBlue;
        PathReliability formulaReliability;
        string formulaName;
        if (_formulaMode != BlueFormulaMode.Ensemble)
        {
            var bestFormula = formulas.OrderByDescending(formula => formula.SelectionScore)
                .ThenByDescending(formula => formula.SmoothedAccuracy)
                .ThenBy(formula => formula.Name)
                .First();
            formulaBlue = scores.First(score => score.Ball == bestFormula.CurrentBall);
            formulaReliability = new PathReliability(
                bestFormula.RawAccuracy,
                bestFormula.SmoothedAccuracy,
                bestFormula.SelectionScore);
            formulaName = bestFormula.Name;
        }
        else
        {
            formulaBlue = scores.OrderByDescending(score => score.FormulaScore).ThenBy(score => score.Ball).First();
            formulaReliability = EvaluateFormulaReliability(history, formulaHistory!);
            formulaName = string.IsNullOrWhiteSpace(formulaBlue.FormulaVotes)
                ? "公式集成"
                : $"公式集成({formulaBlue.FormulaVotes})";
        }
        var exclusionReliability = EvaluateExclusionReliability(history);
        var combinedBlue = scores.OrderByDescending(score => score.CombinedScore)
            .ThenBy(score => score.Ball)
            .First();
        return new BlueSelection(
            scores,
            formulaBlue.Ball,
            exclusionBlue.Ball,
            formulaName,
            formulaReliability.RawAccuracy,
            exclusionReliability.RawAccuracy,
            formulaReliability.SelectionScore,
            "公式+排除综合",
            combinedBlue.Ball);
    }
}
