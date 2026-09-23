namespace SsqAnalyzer.Services.Kill;

/// <summary>One complete draw is the resampling unit; failures never become successful abstentions.</summary>
public sealed record KillEvaluationPeriod(int Period, int KilledCount, int HitCount, bool Failed = false);

public sealed record KillEvaluationMetrics
{
    public int Evaluated { get; init; }
    public int Failed { get; init; }
    public int Triggered { get; init; }
    public int WrongPeriods { get; init; }
    public int Killed { get; init; }
    public int Correct { get; init; }
    public int LongestWrongStreak { get; init; }
    public double Baseline { get; init; }
    public double? Accuracy => Killed == 0 ? null : (double)Correct / Killed;
    public double? Excess => Accuracy - Baseline;
    public double? TriggerRate => Evaluated == Failed ? null : (double)Triggered / (Evaluated - Failed);
    public double? AverageKilled => Triggered == 0 ? null : (double)Killed / Triggered;
    public double? WrongPeriodRate => Triggered == 0 ? null : (double)WrongPeriods / Triggered;
    public double? PreserveRate => 1 - WrongPeriodRate;
    public double? Lower95 { get; init; }
    public double? Upper95 { get; init; }
    public double? RandomLower95 { get; init; }
    public double? RandomUpper95 { get; init; }
    public double? RandomTailProbability { get; init; }
    public int ComparisonCount { get; init; } = 1;
    public double? AdjustedProbability => RandomTailProbability is { } p ? Math.Min(1, p * ComparisonCount) : null;
    public string Evidence => Failed > 0 ? "有执行失败，不评价优势"
        : Triggered < 30 ? "触发少于 30 次，仅供观察"
        : AdjustedProbability is null ? "尚无随机对照"
        : Excess > 0 && AdjustedProbability < .05 ? "探索性对照差异，需前向验证" : "未见明确随机对照优势";

    public static KillEvaluationMetrics Compute(IEnumerable<KillEvaluationPeriod> observations, BallType ball,
        int comparisons = 1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var periods = observations.OrderBy(p => p.Period).ToArray();
        int max = ball == BallType.Red ? 33 : 16, draws = ball == BallType.Red ? 6 : 1;
        if (periods.Any(p => p.KilledCount < 0 || p.KilledCount > max || p.HitCount < 0 || p.HitCount > Math.Min(draws, p.KilledCount)))
            throw new ArgumentException("回测样本中的杀号或错杀数量无效。");
        var valid = periods.Where(p => !p.Failed).ToArray();
        int longest = 0, streak = 0;
        foreach (var p in periods)
        {
            streak = !p.Failed && p.HitCount > 0 ? streak + 1 : 0;
            longest = Math.Max(longest, streak);
        }
        var metrics = new KillEvaluationMetrics
        {
            Evaluated = periods.Length, Failed = periods.Length - valid.Length,
            Triggered = valid.Count(p => p.KilledCount > 0), WrongPeriods = valid.Count(p => p.HitCount > 0),
            Killed = valid.Sum(p => p.KilledCount), Correct = valid.Sum(p => p.KilledCount - p.HitCount),
            LongestWrongStreak = longest, Baseline = 1 - (double)draws / max, ComparisonCount = Math.Max(1, comparisons)
        };
        if (metrics.Triggered < 30 || metrics.Failed > 0 || metrics.Killed == 0) return metrics;

        // Conditional exploratory null: preserve every observed trigger and kill count.
        // Hypergeometric hits account for drawing WITHOUT replacement within a draw.
        const int repetitions = 2000;
        var random = new Random(20260923);
        var cdf = valid.Select(p => p.KilledCount).Distinct().ToDictionary(k => k, k => HitCdf(max, draws, k));
        var nullAccuracy = new double[repetitions];
        int atLeastObserved = 0;
        for (int r = 0; r < repetitions; r++)
        {
            if (r % 16 == 0) ct.ThrowIfCancellationRequested();
            int hits = 0;
            foreach (var period in valid)
            {
                if (period.KilledCount == 0) continue;
                double u = random.NextDouble();
                var cumulative = cdf[period.KilledCount];
                int h = 0;
                while (h < cumulative.Length - 1 && u > cumulative[h]) h++;
                hits += h;
            }
            nullAccuracy[r] = (double)(metrics.Killed - hits) / metrics.Killed;
            if (metrics.Killed - hits >= metrics.Correct) atLeastObserved++;
        }
        Array.Sort(nullAccuracy);

        // Circular moving-block bootstrap keeps same-draw dependence and some adjacent-draw dependence.
        // This is uncertainty of the retrospective estimate, NOT a next-draw prediction interval.
        int n = valid.Length, block = Math.Max(1, (int)Math.Ceiling(Math.Pow(n, 1d / 3)));
        var killPrefix = new long[2 * n + 1];
        var correctPrefix = new long[2 * n + 1];
        for (int i = 0; i < 2 * n; i++)
        {
            killPrefix[i + 1] = killPrefix[i] + valid[i % n].KilledCount;
            correctPrefix[i + 1] = correctPrefix[i] + valid[i % n].KilledCount - valid[i % n].HitCount;
        }
        var bootstrap = new List<double>(1000);
        for (int r = 0; r < 1000; r++)
        {
            if (r % 16 == 0) ct.ThrowIfCancellationRequested();
            long kills = 0, correct = 0;
            for (int sampled = 0; sampled < n; sampled += block)
            {
                int start = random.Next(n), length = Math.Min(block, n - sampled);
                kills += killPrefix[start + length] - killPrefix[start];
                correct += correctPrefix[start + length] - correctPrefix[start];
            }
            if (kills > 0) bootstrap.Add((double)correct / kills);
        }
        bootstrap.Sort();
        ct.ThrowIfCancellationRequested();
        return metrics with
        {
            Lower95 = bootstrap.Count == 0 ? null : Quantile(bootstrap, .025),
            Upper95 = bootstrap.Count == 0 ? null : Quantile(bootstrap, .975),
            RandomLower95 = Quantile(nullAccuracy, .025), RandomUpper95 = Quantile(nullAccuracy, .975),
            RandomTailProbability = (1d + atLeastObserved) / (repetitions + 1)
        };
    }

    internal static double[] HitCdf(int population, int drawn, int killed)
    {
        var cdf = new double[Math.Min(killed, drawn) + 1];
        double denominator = Choose(population, drawn), sum = 0;
        for (int h = 0; h < cdf.Length; h++)
        {
            sum += Choose(killed, h) * Choose(population - killed, drawn - h) / denominator;
            cdf[h] = Math.Min(1, sum);
        }
        cdf[^1] = 1;
        return cdf;
    }
    private static double Choose(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        double value = 1;
        for (int i = 1; i <= k; i++) value *= (double)(n - i + 1) / i;
        return value;
    }
    private static double Quantile(IReadOnlyList<double> sorted, double q) => sorted[(int)Math.Round((sorted.Count - 1) * q)];
}
