using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{

    public PositionBacktestReport Backtest(
        int sampleSize = 200,
        int offsetFromLatest = 0,
        CancellationToken cancellationToken = default)
    {
        var records = OrderedRecords();
        if (sampleSize < 30) throw new ArgumentOutOfRangeException(nameof(sampleSize), "回测样本至少为 30 期");
        if (offsetFromLatest < 0) throw new ArgumentOutOfRangeException(nameof(offsetFromLatest), "回测偏移不能为负数");
        if (records.Count < 4) throw new InvalidOperationException("历史数据不足，无法执行滚动回测");

        int latestYear = records[^1].Period / 1000;
        var annualRecords = records.Where(record => record.Period / 1000 == latestYear).ToList();
        int endExclusive = annualRecords.Count - offsetFromLatest;
        if (endExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(offsetFromLatest), "回测偏移超出本年度数据范围");
        int startIndex = Math.Max(0, endExclusive - sampleSize);
        var targets = annualRecords.Skip(startIndex).Take(endExclusive - startIndex).ToList();
        int pointHits = 0;
        double randomPointHits = 0;
        var pointLifts = new List<double>(targets.Count);
        int formulaHits = 0, exclusionHits = 0, singleHits = 0, doubleHits = 0, tripleHits = 0;
        int firstHalfSingleHits = 0, secondHalfSingleHits = 0;
        var pointColumnHits = new int[6];
        for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = targets[targetIndex];
            var prediction = Predict(target.Period, "backtest");
            int targetPointHits = target.RedBalls.Count(actualBall =>
                prediction.RedPoints.Any(point => PositionPointRange.Contains(point, actualBall, 33)));
            pointHits += targetPointHits;
            for (int index = 0; index < prediction.RedPoints.Count; index++)
                if (target.RedBalls.Any(ball => PositionPointRange.Contains(prediction.RedPoints[index], ball, 33)))
                    pointColumnHits[index]++;
            int coveredNumbers = prediction.RedPoints
                .SelectMany(point => Enumerable.Range(Math.Max(1, point - 1), Math.Min(33, point + 1) - Math.Max(1, point - 1) + 1))
                .Distinct()
                .Count();
            double targetRandomPointHits = 6.0 * coveredNumbers / 33.0;
            randomPointHits += targetRandomPointHits;
            pointLifts.Add(targetPointHits - targetRandomPointHits);
            formulaHits += prediction.FormulaBlue == target.BlueBall ? 1 : 0;
            exclusionHits += prediction.ExclusionBlue == target.BlueBall ? 1 : 0;
            bool singleHit = prediction.SingleBlue == target.BlueBall;
            singleHits += singleHit ? 1 : 0;
            if (singleHit)
            {
                if (targetIndex < targets.Count / 2) firstHalfSingleHits++;
                else secondHalfSingleHits++;
            }
            doubleHits += prediction.DoubleBlue.Contains(target.BlueBall) ? 1 : 0;
            tripleHits += prediction.TripleBlue.Contains(target.BlueBall) ? 1 : 0;
        }

        int count = targets.Count;
        double formulaLower = WilsonLower95(formulaHits, count);
        double exclusionLower = WilsonLower95(exclusionHits, count);
        double singleLower = WilsonLower95(singleHits, count);
        int firstHalfCount = count / 2;
        int secondHalfCount = count - firstHalfCount;
        double firstHalfRate = firstHalfCount == 0 ? 0 : firstHalfSingleHits / (double)firstHalfCount;
        double secondHalfRate = secondHalfCount == 0 ? 0 : secondHalfSingleHits / (double)secondHalfCount;
        double pointLiftLower = MeanLower95(pointLifts);
        double firstHalfPointLift = AverageOrZero(pointLifts.Take(firstHalfCount));
        double secondHalfPointLift = AverageOrZero(pointLifts.Skip(firstHalfCount));
        bool redUsable = count >= 100
            && pointLiftLower > 0
            && firstHalfPointLift > 0
            && secondHalfPointLift > 0;
        bool blueUsable = count >= 100
            && singleLower > 1.0 / 16.0
            && firstHalfRate > 1.0 / 16.0
            && secondHalfRate > 1.0 / 16.0;
        return new PositionBacktestReport(
            count,
            targets[0].Period,
            targets[^1].Period,
            pointHits / (double)count,
            randomPointHits / count,
            formulaHits / (double)count,
            exclusionHits / (double)count,
            singleHits / (double)count,
            doubleHits / (double)count,
            tripleHits / (double)count,
            formulaLower,
            exclusionLower,
            singleLower,
            firstHalfRate,
            secondHalfRate,
            pointLiftLower,
            firstHalfPointLift,
            secondHalfPointLift,
            redUsable,
            blueUsable,
            redUsable && blueUsable)
        {
            OffsetFromLatest = offsetFromLatest,
            PointColumnHitRates = pointColumnHits.Select(hits => hits / (double)count).ToArray()
        };
    }

    public PositionBacktestComparison CompareBacktest(
        IPositionPredictor primary,
        int sampleSize = 200,
        int offsetFromLatest = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(primary);
        var records = OrderedRecords();
        if (sampleSize < 30) throw new ArgumentOutOfRangeException(nameof(sampleSize), "配对回测样本至少为 30 期");
        if (offsetFromLatest < 0) throw new ArgumentOutOfRangeException(nameof(offsetFromLatest), "配对回测偏移不能为负数");
        if (records.Count < 4) throw new InvalidOperationException("历史数据不足，无法执行配对回测");

        int endExclusive = records.Count - offsetFromLatest;
        if (endExclusive <= 3) throw new ArgumentOutOfRangeException(nameof(offsetFromLatest), "配对回测偏移超出历史数据范围");
        int startIndex = Math.Max(3, endExclusive - sampleSize);
        var targets = records.Skip(startIndex).Take(endExclusive - startIndex).ToList();
        int primaryPointHits = 0, candidatePointHits = 0;
        int primaryBlueHits = 0, candidateBlueHits = 0;
        int primaryDoubleHits = 0, candidateDoubleHits = 0;
        int primaryTripleHits = 0, candidateTripleHits = 0;
        int candidateBlueWins = 0, primaryBlueWins = 0, bothBlueHits = 0;
        int differentPoints = 0, differentSingleBlue = 0, differentDoubleBlue = 0, differentTripleBlue = 0;
        var pointAdvantages = new List<double>(targets.Count);
        var doubleBlueAdvantages = new List<double>(targets.Count);
        var tripleBlueAdvantages = new List<double>(targets.Count);

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primaryPrediction = primary.Predict(target.Period, "backtest");
            var candidatePrediction = Predict(target.Period, "backtest");
            if (!primaryPrediction.RedPoints.SequenceEqual(candidatePrediction.RedPoints)) differentPoints++;
            if (primaryPrediction.SingleBlue != candidatePrediction.SingleBlue) differentSingleBlue++;
            if (!primaryPrediction.DoubleBlue.SequenceEqual(candidatePrediction.DoubleBlue)) differentDoubleBlue++;
            if (!primaryPrediction.TripleBlue.SequenceEqual(candidatePrediction.TripleBlue)) differentTripleBlue++;

            int primaryHits = target.RedBalls.Count(actualBall =>
                primaryPrediction.RedPoints.Any(point => PositionPointRange.Contains(point, actualBall, 33)));
            int candidateHits = target.RedBalls.Count(actualBall =>
                candidatePrediction.RedPoints.Any(point => PositionPointRange.Contains(point, actualBall, 33)));
            primaryPointHits += primaryHits;
            candidatePointHits += candidateHits;
            pointAdvantages.Add(candidateHits - primaryHits);

            bool primaryHit = primaryPrediction.SingleBlue == target.BlueBall;
            bool candidateHit = candidatePrediction.SingleBlue == target.BlueBall;
            if (primaryHit) primaryBlueHits++;
            if (candidateHit) candidateBlueHits++;
            if (primaryHit && candidateHit) bothBlueHits++;
            else if (candidateHit) candidateBlueWins++;
            else if (primaryHit) primaryBlueWins++;

            bool primaryDoubleHit = primaryPrediction.DoubleBlue.Contains(target.BlueBall);
            bool candidateDoubleHit = candidatePrediction.DoubleBlue.Contains(target.BlueBall);
            bool primaryTripleHit = primaryPrediction.TripleBlue.Contains(target.BlueBall);
            bool candidateTripleHit = candidatePrediction.TripleBlue.Contains(target.BlueBall);
            if (primaryDoubleHit) primaryDoubleHits++;
            if (candidateDoubleHit) candidateDoubleHits++;
            if (primaryTripleHit) primaryTripleHits++;
            if (candidateTripleHit) candidateTripleHits++;
            doubleBlueAdvantages.Add((candidateDoubleHit ? 1 : 0) - (primaryDoubleHit ? 1 : 0));
            tripleBlueAdvantages.Add((candidateTripleHit ? 1 : 0) - (primaryTripleHit ? 1 : 0));
        }

        int count = targets.Count;
        int firstHalfCount = count / 2;
        int discordant = candidateBlueWins + primaryBlueWins;
        double pointLower = MeanLower95(pointAdvantages);
        double firstHalfAdvantage = AverageOrZero(pointAdvantages.Take(firstHalfCount));
        double secondHalfAdvantage = AverageOrZero(pointAdvantages.Skip(firstHalfCount));
        double candidateWinRate = discordant == 0 ? 0 : candidateBlueWins / (double)discordant;
        double candidateWinLower = WilsonLower95(candidateBlueWins, discordant);
        bool redSuperior = count >= 100
            && pointLower > 0
            && firstHalfAdvantage > 0
            && secondHalfAdvantage > 0;
        bool blueSuperior = discordant >= 30 && candidateWinLower > 0.5;
        double doubleLower = MeanLower95(doubleBlueAdvantages);
        double tripleLower = MeanLower95(tripleBlueAdvantages);
        double firstHalfDouble = AverageOrZero(doubleBlueAdvantages.Take(firstHalfCount));
        double secondHalfDouble = AverageOrZero(doubleBlueAdvantages.Skip(firstHalfCount));
        double firstHalfTriple = AverageOrZero(tripleBlueAdvantages.Take(firstHalfCount));
        double secondHalfTriple = AverageOrZero(tripleBlueAdvantages.Skip(firstHalfCount));
        bool doubleSuperior = count >= 100 && doubleLower > 0
            && firstHalfDouble > 0 && secondHalfDouble > 0;
        bool tripleSuperior = count >= 100 && tripleLower > 0
            && firstHalfTriple > 0 && secondHalfTriple > 0;

        return new PositionBacktestComparison(
            count,
            offsetFromLatest,
            targets[0].Period,
            targets[^1].Period,
            primary.RuleVersionId,
            RuleVersionId,
            differentPoints,
            differentSingleBlue,
            primaryPointHits / (double)count,
            candidatePointHits / (double)count,
            pointAdvantages.Average(),
            pointLower,
            firstHalfAdvantage,
            secondHalfAdvantage,
            primaryBlueHits / (double)count,
            candidateBlueHits / (double)count,
            candidateBlueWins,
            primaryBlueWins,
            bothBlueHits,
            discordant,
            candidateWinRate,
            candidateWinLower,
            redSuperior,
            blueSuperior,
            redSuperior && blueSuperior)
        {
            DifferentDoubleBlueCount = differentDoubleBlue,
            DifferentTripleBlueCount = differentTripleBlue,
            PrimaryDoubleBlueHitRate = primaryDoubleHits / (double)count,
            CandidateDoubleBlueHitRate = candidateDoubleHits / (double)count,
            DoubleBlueAdvantage = doubleBlueAdvantages.Average(),
            DoubleBlueAdvantageLower95 = doubleLower,
            FirstHalfDoubleBlueAdvantage = firstHalfDouble,
            SecondHalfDoubleBlueAdvantage = secondHalfDouble,
            PrimaryTripleBlueHitRate = primaryTripleHits / (double)count,
            CandidateTripleBlueHitRate = candidateTripleHits / (double)count,
            TripleBlueAdvantage = tripleBlueAdvantages.Average(),
            TripleBlueAdvantageLower95 = tripleLower,
            FirstHalfTripleBlueAdvantage = firstHalfTriple,
            SecondHalfTripleBlueAdvantage = secondHalfTriple,
            IsDoubleBluePairwiseSuperior = doubleSuperior,
            IsTripleBluePairwiseSuperior = tripleSuperior
        };
    }

    private static double WilsonLower95(int hits, int total)
    {
        if (total == 0) return 0;
        const double z = 1.959963984540054;
        double p = hits / (double)total;
        double denominator = 1 + z * z / total;
        double center = p + z * z / (2 * total);
        double margin = z * Math.Sqrt((p * (1 - p) + z * z / (4 * total)) / total);
        return (center - margin) / denominator;
    }

    private static double MeanLower95(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double mean = values.Average();
        if (values.Count == 1) return mean;
        double variance = values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1);
        const double z = 1.959963984540054;
        return mean - z * Math.Sqrt(variance / values.Count);
    }

    private static double AverageOrZero(IEnumerable<double> values)
    {
        var materialized = values as IReadOnlyCollection<double> ?? values.ToArray();
        return materialized.Count == 0 ? 0 : materialized.Average();
    }
}
