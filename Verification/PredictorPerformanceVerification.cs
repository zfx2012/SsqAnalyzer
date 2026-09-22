using System.Reflection;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

internal static partial class VerificationSuite
{
    private static void VerifyBlueCyclePrefixCompatibility()
    {
        var optimized = typeof(PositionPredictor).GetMethod("BuildBlueCyclePredictions", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<IReadOnlyList<DrawRecord>, int[]>>();
        var random = new Random(93271);
        var sequences = new[]
        {
            Array.Empty<int>(), new[] { 8 }, new[] { 8, 8 }, new[] { 1, 2, 3, 4, 5 },
            Enumerable.Repeat(16, 100).ToArray(),
            Enumerable.Range(0, 120).Select(index => index % 2 + 1).ToArray(),
            Enumerable.Range(0, 257).Select(_ => random.Next(1, 17)).ToArray(),
            new[] { 0, 17, 1, 1, 17, 16, 0, 16 }
        };
        foreach (var sequence in sequences)
        {
            var records = sequence.Select(blue => new DrawRecord { BlueBall = blue }).ToArray();
            var predictions = optimized(records);
            Assert(predictions.Length == records.Length + 1, "blue cycle includes empty and complete prefixes");
            for (int priorCount = 0; priorCount <= records.Length; priorCount++)
            {
                int expected = Enumerable.Range(1, 16)
                    .OrderByDescending(ball => OriginalCycleScore(records, priorCount, ball))
                    .ThenBy(ball => ball).First();
                Assert(predictions[priorCount] == expected, "blue cycle matches original scan, including ties");
            }
            int cut = records.Length / 2;
            var changedFuture = records.Select((record, index) => new DrawRecord
            {
                BlueBall = index < cut ? record.BlueBall : 7
            }).ToArray();
            Assert(predictions.Take(cut + 1).SequenceEqual(optimized(changedFuture).Take(cut + 1)),
                "blue cycle prefixes cannot see future draws");
            Assert(predictions.SequenceEqual(optimized(records)), "blue cycle evaluations do not share mutable state");
        }
    }

    private static double OriginalCycleScore(IReadOnlyList<DrawRecord> history, int endExclusive, int ball)
    {
        var positions = new List<int>();
        for (int index = 0; index < endExclusive; index++)
            if (history[index].BlueBall == ball) positions.Add(index);
        if (positions.Count < 2) return 0;
        double gap = positions.Zip(positions.Skip(1), (left, right) => right - left).Average();
        int omission = endExclusive - 1 - positions[^1];
        return 1.0 / (1.0 + Math.Abs(omission - gap) / (gap + 1.0));
    }

    private static void VerifyConditionalProbabilityBufferCompatibility()
    {
        var optimized = typeof(PositionPredictor).GetMethod("ConditionalInclusionProbabilities",
                BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(IReadOnlyList<double>), typeof(int), typeof(double[,]), typeof(double[,]) }, null)!
            .CreateDelegate<Func<IReadOnlyList<double>, int, double[,], double[,], double[]>>();
        var random = new Random(27271);
        foreach (var shape in new[] { (Count: 33, Draws: 6), (Count: 8, Draws: 3), (Count: 6, Draws: 0), (Count: 6, Draws: 6) })
        {
            var prefix = new double[shape.Count + 1, shape.Draws + 1];
            var suffix = new double[shape.Count + 1, shape.Draws + 1];
            for (int iteration = 0; iteration < 12; iteration++)
            {
                var weights = Enumerable.Range(0, shape.Count)
                    .Select(_ => iteration % 3 == 0 ? 1.0 : Math.Exp(random.NextDouble() * 12 - 6)).ToArray();
                var actual = optimized(weights, shape.Draws, prefix, suffix);
                var expected = OriginalConditionalProbabilities(weights, shape.Draws);
                Assert(actual.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(expected.Select(BitConverter.DoubleToInt64Bits)),
                    "reused conditional buffers preserve exact floating point results");
                Assert(Math.Abs(actual.Sum() - shape.Draws) < 1e-10, "conditional marginals sum to draw size");
            }
        }
    }

    // The pre-optimization algorithm creates fresh zero-initialized tables on every call.
    private static double[] OriginalConditionalProbabilities(IReadOnlyList<double> weights, int drawSize)
    {
        int count = weights.Count;
        var prefix = new double[count + 1, drawSize + 1];
        var suffix = new double[count + 1, drawSize + 1];
        prefix[0, 0] = 1;
        for (int index = 0; index < count; index++)
        {
            prefix[index + 1, 0] = 1;
            for (int selected = 1; selected <= drawSize; selected++)
                prefix[index + 1, selected] = prefix[index, selected] + weights[index] * prefix[index, selected - 1];
        }
        suffix[count, 0] = 1;
        for (int index = count - 1; index >= 0; index--)
        {
            suffix[index, 0] = 1;
            for (int selected = 1; selected <= drawSize; selected++)
                suffix[index, selected] = suffix[index + 1, selected] + weights[index] * suffix[index + 1, selected - 1];
        }
        double denominator = prefix[count, drawSize];
        return Enumerable.Range(0, count).Select(index =>
        {
            double excluded = 0;
            for (int left = 0; left < drawSize; left++)
                excluded += prefix[index, left] * suffix[index + 1, drawSize - 1 - left];
            return weights[index] * excluded / denominator;
        }).ToArray();
    }
}
