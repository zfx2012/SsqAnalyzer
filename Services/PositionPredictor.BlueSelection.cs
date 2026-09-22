using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{

    private int[] BuildNestedBlueSelection(BlueSelection selection)
    {
        var ranked = selection.Scores
            .OrderByDescending(score => score.CombinedScore)
            .ThenBy(score => score.Ball)
            .Select(score => score.Ball)
            .ToArray();
        bool anchorFormula = _blueSelectionMode == BlueSelectionMode.FormulaAnchored
            || (_blueSelectionMode == BlueSelectionMode.ReliabilityGatedFormula
                && selection.FormulaSelectionScore > 1.0 / 16.0);
        return anchorFormula
            ? new[] { selection.FormulaBlue }
                .Concat(ranked.Where(ball => ball != selection.FormulaBlue))
                .Take(3)
                .ToArray()
            : ranked.Take(3).ToArray();
    }

    private static Dictionary<int, double> CalculateFormulaScores(IReadOnlyList<FormulaMetric> formulas)
    {
        var votes = Enumerable.Range(1, 16).ToDictionary(
            ball => ball,
            ball => formulas.Where(formula => formula.CurrentBall == ball)
                .Sum(formula => formula.SelectionScore));
        return Ranks(votes);
    }

    private List<FormulaMetric> EvaluateRollingHotFormula(
        IReadOnlyList<DrawRecord> history,
        int targetIssue)
    {
        int start = Math.Max(3, history.Count - _config.FormulaBacktestWindow);
        int split = start + (history.Count - start) / 2;
        int hits = 0, trials = 0, firstHits = 0, firstTrials = 0, secondHits = 0, secondTrials = 0;
        for (int targetIndex = start; targetIndex < history.Count; targetIndex++)
        {
            int predicted = PredictRollingHot(history, targetIndex);
            bool hit = predicted == history[targetIndex].BlueBall;
            if (hit) hits++;
            trials++;
            if (targetIndex < split)
            {
                if (hit) firstHits++;
                firstTrials++;
            }
            else
            {
                if (hit) secondHits++;
                secondTrials++;
            }
        }

        int currentBall = PredictRollingHot(history, history.Count);
        double raw = trials == 0 ? 0 : hits / (double)trials;
        double smoothed = (hits + 1.0) / (trials + 16.0);
        double firstSmoothed = (firstHits + 1.0) / (firstTrials + 16.0);
        double secondSmoothed = (secondHits + 1.0) / (secondTrials + 16.0);
        return new List<FormulaMetric>
        {
            new("近30期热号", currentBall, raw, smoothed, Math.Min(firstSmoothed, secondSmoothed))
        };
    }

    private static Dictionary<int, double> CalculateRollingHotScores(IReadOnlyList<DrawRecord> history)
    {
        var recent = history.TakeLast(RollingHotWindow).ToList();
        var counts = Enumerable.Range(1, 16).ToDictionary(
            ball => ball,
            ball => CountOccurrences(recent, ball, true));
        return Ranks(counts);
    }

    private static int PredictRollingHot(IReadOnlyList<DrawRecord> records, int priorCount)
    {
        int start = Math.Max(0, priorCount - RollingHotWindow);
        var counts = new int[17];
        for (int index = start; index < priorCount; index++) counts[records[index].BlueBall]++;
        return Enumerable.Range(1, 16)
            .OrderByDescending(ball => counts[ball])
            .ThenBy(ball => ball)
            .First();
    }

    private List<FormulaMetric> EvaluateLegacyBlueFormulas(
        IReadOnlyList<DrawRecord> history,
        int targetIssue)
    {
        if (history.Count < 3)
            return new List<FormulaMetric>
            {
                new("样本不足", history[^1].BlueBall, 0, 1.0 / 16.0, 1.0 / 16.0)
            };

        var result = new List<FormulaMetric>();
        var cyclePredictions = BuildBlueCyclePredictions(history);
        int start = Math.Max(3, history.Count - _config.FormulaBacktestWindow);
        int split = start + (history.Count - start) / 2;
        foreach (var formula in Enum.GetValues<BlueFormulaKind>())
        {
            int hits = 0, trials = 0, firstHits = 0, firstTrials = 0, secondHits = 0, secondTrials = 0;
            for (int targetIndex = start; targetIndex < history.Count; targetIndex++)
            {
                int predicted = formula == BlueFormulaKind.CycleClosest
                    ? cyclePredictions[targetIndex]
                    : CalculateBlueFormula(formula, history, targetIndex, history[targetIndex].Period);
                bool hit = predicted == history[targetIndex].BlueBall;
                if (hit) hits++;
                trials++;
                if (targetIndex < split)
                {
                    if (hit) firstHits++;
                    firstTrials++;
                }
                else
                {
                    if (hit) secondHits++;
                    secondTrials++;
                }
            }
            int currentBall = formula == BlueFormulaKind.CycleClosest
                ? cyclePredictions[history.Count]
                : CalculateBlueFormula(formula, history, history.Count, targetIssue);
            double raw = trials == 0 ? 0 : hits / (double)trials;
            double smoothed = (hits + 1.0) / (trials + 16.0);
            double firstSmoothed = (firstHits + 1.0) / (firstTrials + 16.0);
            double secondSmoothed = (secondHits + 1.0) / (secondTrials + 16.0);
            result.Add(new FormulaMetric(
                FormulaName(formula),
                currentBall,
                raw,
                smoothed,
                Math.Min(firstSmoothed, secondSmoothed)));
        }
        return result;
    }

    private static Dictionary<int, double> CalculateLegacyFormulaScores(
        IReadOnlyList<DrawRecord> history,
        IReadOnlyList<FormulaMetric> formulas)
    {
        var balls = Enumerable.Range(1, 16).ToArray();
        var formulaVotes = balls.ToDictionary(
            ball => ball,
            ball => formulas.Where(formula => formula.CurrentBall == ball)
                .Sum(formula => formula.SelectionScore));
        var transition = BuildBlueTransitionFeatures(history, balls);
        var cycle = balls.ToDictionary(ball => ball, ball => CycleStability(history, ball, true));
        var voteRanks = Ranks(formulaVotes);
        var transitionRanks = Ranks(transition);
        var cycleRanks = Ranks(cycle);
        return balls.ToDictionary(ball => ball, ball => RoundScore(
            0.65 * voteRanks[ball]
          + 0.20 * transitionRanks[ball]
          + 0.15 * cycleRanks[ball]));
    }

    private static PathReliability EvaluateFormulaReliability(
        IReadOnlyList<DrawRecord> history,
        BlueFormulaHistory formulaHistory)
    {
        int start = Math.Max(3, history.Count - 200);
        int split = start + (history.Count - start) / 2;
        int hits = 0, trials = 0, firstHits = 0, firstTrials = 0, secondHits = 0, secondTrials = 0;
        for (int targetIndex = start; targetIndex < history.Count; targetIndex++)
        {
            var metrics = formulaHistory.Evaluate(targetIndex, history[targetIndex].Period);
            var scores = CalculateFormulaScores(metrics);
            int predicted = scores.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
            bool hit = predicted == history[targetIndex].BlueBall;
            if (hit) hits++;
            trials++;
            if (targetIndex < split)
            {
                if (hit) firstHits++;
                firstTrials++;
            }
            else
            {
                if (hit) secondHits++;
                secondTrials++;
            }
        }
        double raw = trials == 0 ? 0 : hits / (double)trials;
        double smoothed = (hits + 1.0) / (trials + 16.0);
        double selection = Math.Min(
            (firstHits + 1.0) / (firstTrials + 16.0),
            (secondHits + 1.0) / (secondTrials + 16.0));
        return new PathReliability(raw, smoothed, selection);
    }

    /// <summary>
    /// Computes each prefix using only draws before it. The sum of consecutive gaps is
    /// last - first, so first/last/count preserve the original average exactly without rescanning.
    /// The array belongs to this evaluation only; no results survive a data update.
    /// </summary>
    private static int[] BuildBlueCyclePredictions(IReadOnlyList<DrawRecord> records)
    {
        var predictions = new int[records.Count + 1];
        var counts = new int[17];
        var first = new int[17];
        var last = new int[17];
        predictions[0] = 1;
        for (int priorCount = 1; priorCount <= records.Count; priorCount++)
        {
            int latest = records[priorCount - 1].BlueBall;
            if (latest is >= 1 and <= 16)
            {
                if (counts[latest] == 0) first[latest] = priorCount - 1;
                counts[latest]++;
                last[latest] = priorCount - 1;
            }

            int bestBall = 1;
            double bestScore = -1;
            for (int ball = 1; ball <= 16; ball++)
            {
                double score = 0;
                if (counts[ball] >= 2)
                {
                    double averageGap = (last[ball] - first[ball]) / (double)(counts[ball] - 1);
                    int omission = priorCount - 1 - last[ball];
                    score = 1.0 / (1.0 + Math.Abs(omission - averageGap) / (averageGap + 1.0));
                }
                // Ascending traversal and strict comparison retain the smallest ball on ties.
                if (score > bestScore)
                {
                    bestScore = score;
                    bestBall = ball;
                }
            }
            predictions[priorCount] = bestBall;
        }
        return predictions;
    }

    private static int CalculateBlueFormula(
        BlueFormulaKind formula,
        IReadOnlyList<DrawRecord> records,
        int priorCount,
        int targetIssue)
    {
        var last = records[priorCount - 1];
        var previous = records[priorCount - 2];
        var third = records[priorCount - 3];
        return formula switch
        {
            BlueFormulaKind.TrendStep => Wrap16(last.BlueBall + last.BlueBall - previous.BlueBall),
            BlueFormulaKind.ThreeBlueSum => Wrap16(last.BlueBall + previous.BlueBall + third.BlueBall),
            BlueFormulaKind.RedBlueMix => Wrap16(last.RedSum + last.BlueBall),
            BlueFormulaKind.IssueShift => Wrap16(last.BlueBall + DigitSum(targetIssue)),
            BlueFormulaKind.CycleClosest => Enumerable.Range(1, 16)
                .OrderByDescending(ball => CycleStability(records, ball, true, priorCount))
                .ThenBy(ball => ball)
                .First(),
            _ => 1
        };
    }

    private static string FormulaName(BlueFormulaKind formula) => formula switch
    {
        BlueFormulaKind.TrendStep => "步长延续",
        BlueFormulaKind.ThreeBlueSum => "三期和值",
        BlueFormulaKind.RedBlueMix => "红蓝和值",
        BlueFormulaKind.IssueShift => "期号位移",
        BlueFormulaKind.CycleClosest => "周期贴合",
        _ => formula.ToString()
    };

    private static Dictionary<int, ExclusionFeature> CalculateExclusionFeatures(
        IReadOnlyList<DrawRecord> history,
        int endExclusive)
    {
        var balls = Enumerable.Range(1, 16).ToArray();
        int start = Math.Max(0, endExclusive - 16);
        int windowCount = endExclusive - start;
        var counts = balls.ToDictionary(ball => ball, ball =>
        {
            int count = 0;
            for (int i = start; i < endExclusive; i++)
                if (history[i].BlueBall == ball) count++;
            return count;
        });
        var hotRanks = Ranks(counts);
        int latestBlue = history[endExclusive - 1].BlueBall;
        var routeShares = Enumerable.Range(0, 3).ToDictionary(route => route, route =>
            Rate(Enumerable.Range(start, windowCount).Count(index => history[index].BlueBall % 3 == route), windowCount));
        var parityShares = Enumerable.Range(0, 2).ToDictionary(parity => parity, parity =>
            Rate(Enumerable.Range(start, windowCount).Count(index => history[index].BlueBall % 2 == parity), windowCount));
        var sizeShares = Enumerable.Range(0, 2).ToDictionary(size => size, size =>
            Rate(Enumerable.Range(start, windowCount).Count(index => (history[index].BlueBall <= 8 ? 0 : 1) == size), windowCount));
        var routeRanks = CategoryRanks(routeShares);
        var parityRanks = CategoryRanks(parityShares);
        var sizeRanks = CategoryRanks(sizeShares);

        return balls.ToDictionary(ball => ball, ball =>
        {
            double hotFeature = counts[ball] > 0 ? 0.60 + 0.40 * hotRanks[ball] : 0.15;
            double nonRepeat = ball == latestBlue ? 0.0 : 1.0;
            double score = 0.35 * hotFeature
                         + 0.25 * nonRepeat
                         + 0.18 * routeRanks[ball % 3]
                         + 0.11 * parityRanks[ball % 2]
                         + 0.11 * sizeRanks[ball <= 8 ? 0 : 1];
            string reason = $"{(counts[ball] > 0 ? "热16" : "冷16")}·{(ball == latestBlue ? "重号" : "非重号")}·{ball % 3}路·{(ball <= 8 ? "小" : "大")}·{(ball % 2 == 1 ? "奇" : "偶")}";
            return new ExclusionFeature(RoundScore(score), reason);
        });
    }

    private static PathReliability EvaluateExclusionReliability(IReadOnlyList<DrawRecord> history)
    {
        int start = Math.Max(3, history.Count - 200);
        int split = start + (history.Count - start) / 2;
        int hits = 0, trials = 0, firstHits = 0, firstTrials = 0, secondHits = 0, secondTrials = 0;
        for (int targetIndex = start; targetIndex < history.Count; targetIndex++)
        {
            var features = CalculateExclusionFeatures(history, targetIndex);
            int predicted = features.OrderByDescending(pair => pair.Value.Score).ThenBy(pair => pair.Key).First().Key;
            bool hit = predicted == history[targetIndex].BlueBall;
            if (hit) hits++;
            trials++;
            if (targetIndex < split)
            {
                if (hit) firstHits++;
                firstTrials++;
            }
            else
            {
                if (hit) secondHits++;
                secondTrials++;
            }
        }
        double raw = trials == 0 ? 0 : hits / (double)trials;
        double smoothed = (hits + 1.0) / (trials + 16.0);
        double selection = Math.Min(
            (firstHits + 1.0) / (firstTrials + 16.0),
            (secondHits + 1.0) / (secondTrials + 16.0));
        return new PathReliability(raw, smoothed, selection);
    }

    private static Dictionary<int, double> BuildBlueTransitionFeatures(
        IReadOnlyList<DrawRecord> history,
        IReadOnlyList<int> balls)
    {
        var result = balls.ToDictionary(ball => ball, _ => 0.0);
        if (history.Count < 2) return result;
        string latestContext = BlueContext(history[^1].BlueBall);
        for (int i = 1; i < history.Count; i++)
            if (BlueContext(history[i - 1].BlueBall) == latestContext) result[history[i].BlueBall]++;
        return result;
    }

    private sealed class BlueFormulaHistory
    {
        private readonly IReadOnlyList<DrawRecord> _records;
        private readonly int _window;
        private readonly int[] _cyclePredictions;
        private readonly IReadOnlyDictionary<BlueFormulaKind, int[]> _predictions;
        private readonly IReadOnlyDictionary<BlueFormulaKind, int[]> _hitPrefixes;

        public BlueFormulaHistory(IReadOnlyList<DrawRecord> records, int window)
        {
            _records = records;
            _window = window;
            _cyclePredictions = BuildBlueCyclePredictions(records);
            var predictions = new Dictionary<BlueFormulaKind, int[]>();
            var hitPrefixes = new Dictionary<BlueFormulaKind, int[]>();
            foreach (var formula in Enum.GetValues<BlueFormulaKind>())
            {
                var formulaPredictions = new int[records.Count];
                var hitPrefix = new int[records.Count + 1];
                for (int targetIndex = 0; targetIndex < records.Count; targetIndex++)
                {
                    hitPrefix[targetIndex + 1] = hitPrefix[targetIndex];
                    if (targetIndex < 3) continue;
                    int predicted = formula == BlueFormulaKind.CycleClosest
                        ? _cyclePredictions[targetIndex]
                        : CalculateBlueFormula(
                            formula,
                            records,
                            targetIndex,
                            records[targetIndex].Period);
                    formulaPredictions[targetIndex] = predicted;
                    if (predicted == records[targetIndex].BlueBall) hitPrefix[targetIndex + 1]++;
                }
                predictions[formula] = formulaPredictions;
                hitPrefixes[formula] = hitPrefix;
            }
            _predictions = predictions;
            _hitPrefixes = hitPrefixes;
        }

        public List<FormulaMetric> Evaluate(int priorCount, int targetIssue)
        {
            if (priorCount < 3)
            {
                int fallback = priorCount == 0 ? 1 : _records[priorCount - 1].BlueBall;
                return new List<FormulaMetric>
                {
                    new("样本不足", fallback, 0, 1.0 / 16.0, 1.0 / 16.0)
                };
            }

            int start = Math.Max(3, priorCount - _window);
            int split = start + (priorCount - start) / 2;
            int trials = priorCount - start;
            int firstTrials = split - start;
            int secondTrials = priorCount - split;
            var result = new List<FormulaMetric>();
            foreach (var formula in Enum.GetValues<BlueFormulaKind>())
            {
                var prefix = _hitPrefixes[formula];
                int hits = prefix[priorCount] - prefix[start];
                int firstHits = prefix[split] - prefix[start];
                int secondHits = prefix[priorCount] - prefix[split];
                int currentBall = priorCount < _records.Count
                    ? _predictions[formula][priorCount]
                    : formula == BlueFormulaKind.CycleClosest
                        ? _cyclePredictions[priorCount]
                        : CalculateBlueFormula(formula, _records, priorCount, targetIssue);
                double raw = trials == 0 ? 0 : hits / (double)trials;
                double smoothed = (hits + 1.0) / (trials + 16.0);
                double firstSmoothed = (firstHits + 1.0) / (firstTrials + 16.0);
                double secondSmoothed = (secondHits + 1.0) / (secondTrials + 16.0);
                result.Add(new FormulaMetric(
                    FormulaName(formula),
                    currentBall,
                    raw,
                    smoothed,
                    Math.Min(firstSmoothed, secondSmoothed)));
            }
            return result;
        }
    }
}
