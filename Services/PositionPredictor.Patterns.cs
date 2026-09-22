using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public sealed partial class PositionPredictor
{

    private sealed class RecentStructurePatterns
    {
        private readonly RecentPatternWindow _window30;
        private readonly RecentPatternWindow _window15;
        private readonly RecentPatternWindow _window5;
        private readonly IReadOnlySet<int> _latestReds;

        private RecentStructurePatterns(IReadOnlyList<DrawRecord> records)
        {
            _window30 = new RecentPatternWindow(records.TakeLast(30).ToList());
            _window15 = new RecentPatternWindow(records.TakeLast(15).ToList());
            _window5 = new RecentPatternWindow(records.TakeLast(5).ToList());
            _latestReds = records[^1].RedBalls.ToHashSet();
        }

        public static RecentStructurePatterns Create(IReadOnlyList<DrawRecord> records) => new(records);

        public double Score(IReadOnlyList<int> balls)
        {
            int repeats = balls.Count(_latestReds.Contains);
            return 0.28 * _window30.Score(balls, repeats)
                 + 0.42 * _window15.Score(balls, repeats)
                 + 0.30 * _window5.Score(balls, repeats);
        }
    }

    private sealed class RecentPatternWindow
    {
        private readonly Dictionary<int, int> _odd;
        private readonly Dictionary<int, int> _links;
        private readonly Dictionary<int, int> _repeats;
        private readonly Dictionary<int, int> _primes;
        private readonly Dictionary<string, int> _routes;
        private readonly Dictionary<string, int> _zones;
        private readonly double _medianSum;
        private readonly double _sumMad;
        private readonly double _medianSpan;
        private readonly double _spanMad;

        public RecentPatternWindow(IReadOnlyList<DrawRecord> records)
        {
            _odd = Counts(records.Select(record => record.OddCount));
            _links = Counts(records.Select(record => LinkCount(record.RedBalls)));
            _primes = Counts(records.Select(record => record.RedBalls.Count(IsPrime)));
            _routes = Counts(records.Select(record => RoutePattern(record.RedBalls)));
            _zones = Counts(records.Select(record => ZonePattern(record.RedBalls)));
            _repeats = new Dictionary<int, int>();
            for (int index = 1; index < records.Count; index++)
            {
                int repeatCount = records[index].RedBalls.Intersect(records[index - 1].RedBalls).Count();
                _repeats[repeatCount] = _repeats.GetValueOrDefault(repeatCount) + 1;
            }

            var sums = records.Select(record => (double)record.RedSum).ToArray();
            var spans = records.Select(record => (double)record.RedSpan).ToArray();
            _medianSum = Median(sums);
            _sumMad = Median(sums.Select(value => Math.Abs(value - _medianSum)).ToArray());
            _medianSpan = Median(spans);
            _spanMad = Median(spans.Select(value => Math.Abs(value - _medianSpan)).ToArray());
        }

        public double Score(IReadOnlyList<int> balls, int repeats) =>
            (Relative(_odd, balls.Count(ball => ball % 2 == 1))
           + Relative(_links, LinkCount(balls))
           + Relative(_repeats, repeats)
           + Relative(_primes, balls.Count(IsPrime))
           + Relative(_routes, RoutePattern(balls))
           + Relative(_zones, ZonePattern(balls))
           + Closeness(balls.Sum(), _medianSum, _sumMad)
           + Closeness(balls[^1] - balls[0], _medianSpan, _spanMad)) / 8.0;

        private static Dictionary<T, int> Counts<T>(IEnumerable<T> values) where T : notnull =>
            values.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());

        private static double Relative<T>(IReadOnlyDictionary<T, int> counts, T key) where T : notnull
        {
            int maximum = counts.Count == 0 ? 0 : counts.Values.Max();
            int value = counts.TryGetValue(key, out int count) ? count : 0;
            return (value + 1.0) / (maximum + 1.0);
        }

        private static double Closeness(double value, double median, double mad) =>
            1.0 / (1.0 + Math.Abs(value - median) / (mad + 1.0));

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(value => value).ToArray();
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
        }

        private static string RoutePattern(IEnumerable<int> balls) =>
            string.Join(':', Enumerable.Range(0, 3).Select(route => balls.Count(ball => ball % 3 == route)));

        private static string ZonePattern(IEnumerable<int> balls) =>
            string.Join(':', Enumerable.Range(0, 3).Select(zone => balls.Count(ball => Zone(ball) == zone)));

        private static int LinkCount(IEnumerable<int> balls)
        {
            var sorted = balls.OrderBy(ball => ball).ToArray();
            int count = 0;
            for (int index = 1; index < sorted.Length; index++)
                if (sorted[index] - sorted[index - 1] == 1
                    && (index == 1 || sorted[index - 1] - sorted[index - 2] != 1)) count++;
            return count;
        }

        private static bool IsPrime(int value) => value is 2 or 3 or 5 or 7 or 11 or 13 or 17 or 19 or 23 or 29 or 31;
    }

    private sealed class MultiScaleRangeCoverage
    {
        private readonly double[] _weightedRates;

        private MultiScaleRangeCoverage(IReadOnlyList<DrawRecord> history)
        {
            var window30 = history.TakeLast(30).ToList();
            var window15 = history.TakeLast(15).ToList();
            var window5 = history.TakeLast(5).ToList();
            _weightedRates = new double[34];
            for (int ball = 1; ball <= 33; ball++)
            {
                _weightedRates[ball] = 0.20 * Rate(CountOccurrences(history, ball, false), history.Count)
                                     + 0.30 * Rate(CountOccurrences(window30, ball, false), window30.Count)
                                     + 0.30 * Rate(CountOccurrences(window15, ball, false), window15.Count)
                                     + 0.20 * Rate(CountOccurrences(window5, ball, false), window5.Count);
            }
        }

        public static MultiScaleRangeCoverage Create(IReadOnlyList<DrawRecord> history) => new(history);

        public double Score(IReadOnlyList<int> points)
        {
            var covered = points.SelectMany(point =>
                    Enumerable.Range(Math.Max(1, point - 1), Math.Min(33, point + 1) - Math.Max(1, point - 1) + 1))
                .Distinct();
            return covered.Sum(ball => _weightedRates[ball]) / 6.0;
        }
    }

    private sealed class MultiScaleCombinationPatterns
    {
        private readonly PatternWindow _history;
        private readonly PatternWindow _window30;
        private readonly PatternWindow _window15;
        private readonly PatternWindow _window5;
        private readonly IReadOnlySet<int> _latestReds;

        private MultiScaleCombinationPatterns(IReadOnlyList<DrawRecord> records)
        {
            _history = new PatternWindow(records);
            _window30 = new PatternWindow(records.TakeLast(30).ToList());
            _window15 = new PatternWindow(records.TakeLast(15).ToList());
            _window5 = new PatternWindow(records.TakeLast(5).ToList());
            _latestReds = records[^1].RedBalls.ToHashSet();
        }

        public static MultiScaleCombinationPatterns Create(IReadOnlyList<DrawRecord> records) => new(records);

        public double Score(IReadOnlyList<int> balls)
        {
            int repeats = balls.Count(_latestReds.Contains);
            return 0.20 * _history.Score(balls, repeats)
                 + 0.30 * _window30.Score(balls, repeats)
                 + 0.30 * _window15.Score(balls, repeats)
                 + 0.20 * _window5.Score(balls, repeats);
        }
    }

    private sealed class PatternWindow
    {
        private readonly Dictionary<int, int> _odd;
        private readonly Dictionary<int, int> _links;
        private readonly Dictionary<int, int> _repeats;
        private readonly Dictionary<string, int> _routes;
        private readonly Dictionary<string, int> _zones;
        private readonly double _medianSum;
        private readonly double _sumMad;
        private readonly double _medianSpan;
        private readonly double _spanMad;

        public PatternWindow(IReadOnlyList<DrawRecord> records)
        {
            _odd = Counts(records.Select(record => record.OddCount));
            _links = Counts(records.Select(record => LinkCount(record.RedBalls)));
            _routes = Counts(records.Select(record => RoutePattern(record.RedBalls)));
            _zones = Counts(records.Select(record => ZonePattern(record.RedBalls)));
            _repeats = new Dictionary<int, int>();
            for (int i = 1; i < records.Count; i++)
            {
                int value = records[i].RedBalls.Intersect(records[i - 1].RedBalls).Count();
                _repeats[value] = _repeats.GetValueOrDefault(value) + 1;
            }
            var sums = records.Select(record => (double)record.RedSum).ToArray();
            var spans = records.Select(record => (double)record.RedSpan).ToArray();
            _medianSum = Median(sums);
            _sumMad = Median(sums.Select(value => Math.Abs(value - _medianSum)).ToArray());
            _medianSpan = Median(spans);
            _spanMad = Median(spans.Select(value => Math.Abs(value - _medianSpan)).ToArray());
        }

        public double Score(IReadOnlyList<int> balls, int repeats) =>
            (Relative(_odd, balls.Count(ball => ball % 2 == 1))
           + Relative(_links, LinkCount(balls))
           + Relative(_repeats, repeats)
           + Relative(_routes, RoutePattern(balls))
           + Relative(_zones, ZonePattern(balls))
           + Closeness(balls.Sum(), _medianSum, _sumMad)
           + Closeness(balls[^1] - balls[0], _medianSpan, _spanMad)) / 7.0;

        private static Dictionary<T, int> Counts<T>(IEnumerable<T> values) where T : notnull =>
            values.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());

        private static double Relative<T>(IReadOnlyDictionary<T, int> counts, T key) where T : notnull
        {
            int max = counts.Count == 0 ? 0 : counts.Values.Max();
            int value = counts.TryGetValue(key, out int count) ? count : 0;
            return (value + 1.0) / (max + 1.0);
        }

        private static double Closeness(double value, double median, double mad) =>
            1.0 / (1.0 + Math.Abs(value - median) / (mad + 1.0));

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(value => value).ToArray();
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
        }

        private static string RoutePattern(IEnumerable<int> balls) =>
            string.Join(':', Enumerable.Range(0, 3).Select(route => balls.Count(ball => ball % 3 == route)));

        private static string ZonePattern(IEnumerable<int> balls) =>
            string.Join(':', Enumerable.Range(0, 3).Select(zone => balls.Count(ball => Zone(ball) == zone)));

        private static int LinkCount(IEnumerable<int> balls)
        {
            var sorted = balls.OrderBy(ball => ball).ToArray();
            int count = 0;
            for (int i = 1; i < sorted.Length; i++)
                if (sorted[i] - sorted[i - 1] == 1 && (i == 1 || sorted[i - 1] - sorted[i - 2] != 1)) count++;
            return count;
        }
    }
}
