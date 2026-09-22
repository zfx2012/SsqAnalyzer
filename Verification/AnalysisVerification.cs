using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

/// <summary>Compare optimized statistics with the previous implementation, including tie ordering.</summary>
internal static class AnalysisVerification
{
    public static void Run()
    {
        var data = new FakeDataService();
        var service = new AnalysisService(data);
        var random = new Random(20260920);
        var records = Enumerable.Range(1, 250).Select(issue => new DrawRecord
        {
            Period = issue,
            RedBalls = Enumerable.Range(0, 6).Select(_ => random.Next(1, 34)).ToArray(),
            BlueBall = random.Next(1, 17)
        }).ToArray();

        foreach (var source in new[] { Array.Empty<DrawRecord>(), records })
        {
            data.SetRecords(source);
            foreach (int periods in new[] { -1, 0, 1, 10, 30, 250, 1000 })
            {
                var recent = source.TakeLast(Math.Max(0, periods)).ToList();
                var red = service.GetRedFrequency(periods);
                var blue = service.GetBlueFrequency(periods);
                Check(red.Keys.SequenceEqual(Enumerable.Range(1, 33)), "red key order");
                Check(blue.Keys.SequenceEqual(Enumerable.Range(1, 16)), "blue key order");
                Check(red.All(pair => pair.Value == recent.Count(draw => draw.RedBalls.Contains(pair.Key))),
                    "red draw frequencies, including duplicates");
                Check(blue.All(pair => pair.Value == recent.Count(draw => draw.BlueBall == pair.Key)),
                    "blue draw frequencies");

                var previousPairs = new Dictionary<string, int>();
                foreach (var draw in recent)
                    for (int i = 0; i < draw.RedBalls.Count; i++)
                        for (int j = i + 1; j < draw.RedBalls.Count; j++)
                        {
                            string key = $"{draw.RedBalls[i]}-{draw.RedBalls[j]}";
                            previousPairs[key] = previousPairs.GetValueOrDefault(key) + 1;
                        }

                var expected = previousPairs.OrderByDescending(pair => pair.Value).Take(20)
                    .Select((pair, index) => (pair.Key, pair.Value, Rank: index + 1));
                var actual = service.GetPairFrequencies(periods)
                    .Select(pair => ($"{pair.N1}-{pair.N2}", pair.Count, pair.Rank));
                Check(actual.SequenceEqual(expected), "pair counts, direction, ties and ranks");
            }
        }

        data.SetRecords(new DrawRecord { RedBalls = new[] { -1, 0, 1, 1, 33, 34 }, BlueBall = 17 });
        Check(service.GetRedFrequency(1).Values.Sum() == 2, "ignore out-of-range red numbers");
        Check(service.GetBlueFrequency(1).Values.Sum() == 0, "ignore out-of-range blue numbers");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Statistics compatibility: {message}");
    }
}
