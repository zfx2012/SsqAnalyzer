using System.Collections.ObjectModel;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services
{
    /// <summary>
    /// 分析服务：双色球号码分析、杀号、组号、频率统计等
    /// </summary>
    public class AnalysisService : IAnalysisService
    {
        private readonly IDataService _dataService;

        public AnalysisService(IDataService dataService)
        {
            _dataService = dataService;
        }

        private List<DrawRecord> GetData() => _dataService.GetAllRecords();

        /// <summary>
        /// 获取最近N期数据
        /// </summary>
        public List<DrawRecord> GetRecent(int n) => n <= 0
            ? new List<DrawRecord>()
            : GetData().TakeLast(n).ToList();

        /// <summary>
        /// 计算红球频率
        /// </summary>
        public Dictionary<int, int> GetRedFrequency(int periods)
        {
            var recent = GetRecent(periods);
            var freq = Enumerable.Range(1, 33).ToDictionary(number => number, _ => 0);
            Span<bool> seen = stackalloc bool[34];
            foreach (var draw in recent)
            {
                seen.Clear();
                foreach (int number in draw.RedBalls)
                {
                    // Count draws containing the number, even if an imported draw has duplicates.
                    if (number is < 1 or > 33 || seen[number]) continue;
                    seen[number] = true;
                    freq[number]++;
                }
            }
            return freq;
        }

        /// <summary>
        /// 计算蓝球频率
        /// </summary>
        public Dictionary<int, int> GetBlueFrequency(int periods)
        {
            var recent = GetRecent(periods);
            var freq = Enumerable.Range(1, 16).ToDictionary(number => number, _ => 0);
            foreach (var draw in recent)
            {
                if (draw.BlueBall is >= 1 and <= 16)
                    freq[draw.BlueBall]++;
            }
            return freq;
        }

        /// <summary>
        /// 获取杀号推荐（占位实现：频率分桶）。
        /// 新代码请改用 <see cref="SsqAnalyzer.Services.Kill.IKillEngine.Execute"/>，新杀号引擎支持多规则 + 回测 + 逐号码溯源。
        /// </summary>
        [Obsolete("使用 IKillEngine.Execute 替代。旧实现为频率分桶占位逻辑，保留 1 个版本后移除。")]
        public List<KillRecommendation> GetKillRecommendations(int periods)
        {
            var redFreq = GetRedFrequency(periods);
            var blueFreq = GetBlueFrequency(periods);

            var results = new List<KillRecommendation>();

            // 高置信度杀号：出现≤3次
            var killHigh = redFreq.Where(kv => kv.Value <= 3).Select(kv => kv.Key).ToList();
            results.Add(new KillRecommendation
            {
                Title = "高置信度杀号",
                Confidence = "high",
                Numbers = new ObservableCollection<int>(killHigh),
                Reason = $"近{periods}期出现≤3次，连续多期未开出，出号概率较低。建议排除这 {killHigh.Count} 个红球。"
            });

            // 中置信度杀号：出现4-5次
            var killMid = redFreq.Where(kv => kv.Value > 3 && kv.Value <= 5).Select(kv => kv.Key).ToList();
            results.Add(new KillRecommendation
            {
                Title = "中置信度杀号",
                Confidence = "mid",
                Numbers = new ObservableCollection<int>(killMid),
                Reason = $"近{periods}期出现4-5次，近期热度下降。可结合走势酌情排除这 {killMid.Count} 个红球。"
            });

            return results;
        }

        /// <summary>
        /// 获取组号推荐（3组不同策略）
        /// </summary>
        public List<GroupRecommendation> GetGroupRecommendations(int periods)
        {
            if (GetRecent(periods).Count == 0)
                return new List<GroupRecommendation>();

            var redFreq = GetRedFrequency(periods);
            var blueFreq = GetBlueFrequency(periods);

            var hotRed = redFreq.Where(kv => kv.Value >= 12).Select(kv => kv.Key).ToList();
            var warmRed = redFreq.Where(kv => kv.Value >= 6 && kv.Value <= 11).Select(kv => kv.Key).ToList();
            var coldRed = redFreq.Where(kv => kv.Value <= 5).Select(kv => kv.Key).ToList();
            var hotBlue = blueFreq.Where(kv => kv.Value >= 5).Select(kv => kv.Key).ToList();
            var coldBlue = blueFreq.Where(kv => kv.Value <= 2).Select(kv => kv.Key).ToList();

            var groups = new List<GroupRecommendation>
            {
                new GroupRecommendation
                {
                    Index = 1, Style = "均衡型",
                    Description = "3热+2温+1冷，冷热兼顾，适合稳定投注",
                    RedBalls = PickN(hotRed, 3).Concat(PickN(warmRed, 2)).Concat(PickN(coldRed, 1)).OrderBy(x => x).ToArray(),
                    BlueBall = hotBlue.Count > 0 ? hotBlue[Random.Shared.Next(hotBlue.Count)] : Random.Shared.Next(1, 17)
                },
                new GroupRecommendation
                {
                    Index = 2, Style = "热号型",
                    Description = "4热+2温，追热号策略，近期活跃号码为主",
                    RedBalls = PickN(hotRed, 4).Concat(PickN(warmRed, 2)).OrderBy(x => x).ToArray(),
                    BlueBall = coldBlue.Count > 0 ? coldBlue[Random.Shared.Next(coldBlue.Count)] : Random.Shared.Next(1, 17)
                },
                new GroupRecommendation
                {
                    Index = 3, Style = "冷号型",
                    Description = "2热+2温+2冷，博冷号回补，适合追冷策略",
                    RedBalls = PickN(hotRed, 2).Concat(PickN(warmRed, 2)).Concat(PickN(coldRed, 2)).OrderBy(x => x).ToArray(),
                    BlueBall = hotBlue.Count > 0 ? hotBlue[Random.Shared.Next(hotBlue.Count)] : Random.Shared.Next(1, 17)
                }
            };

            return groups
                .Where(g => g.RedBalls.Length == 6
                    && g.RedBalls.Distinct().Count() == 6
                    && g.RedBalls.All(n => n is >= 1 and <= 33)
                    && g.BlueBall is >= 1 and <= 16)
                .ToList();
        }

        /// <summary>
        /// 获取红球组合频率 TOP 20
        /// </summary>
        public List<PairFrequency> GetPairFrequencies(int periods)
        {
            var recent = GetRecent(periods);
            var pairFreq = new Dictionary<(int N1, int N2), int>();

            foreach (var draw in recent)
            {
                for (int i = 0; i < draw.RedBalls.Count; i++)
                {
                    for (int j = i + 1; j < draw.RedBalls.Count; j++)
                    {
                        var key = (draw.RedBalls[i], draw.RedBalls[j]);
                        pairFreq[key] = pairFreq.GetValueOrDefault(key) + 1;
                    }
                }
            }

            return pairFreq.OrderByDescending(kv => kv.Value)
                .Take(20)
                .Select((kv, i) => new PairFrequency
                {
                    N1 = kv.Key.N1,
                    N2 = kv.Key.N2,
                    Count = kv.Value,
                    Rank = i + 1
                })
                .ToList();
        }

        /// <summary>
        /// 生成复式成本对照表
        /// </summary>
        public List<CompoundCost> GetCompoundCosts()
        {
            var combos = new[]
            {
                ("7+1", 7, 1), ("8+1", 8, 1), ("9+1", 9, 1), ("10+1", 10, 1), ("11+1", 11, 1),
                ("7+2", 7, 2), ("8+2", 8, 2), ("6+2", 6, 2), ("6+3", 6, 3), ("6+4", 6, 4)
            };

            return combos.Select(c =>
            {
                var bets = (int)(Combination(c.Item2, 6) * Combination(c.Item3, 1));
                return new CompoundCost
                {
                    Label = c.Item1,
                    RedCount = c.Item2,
                    BlueCount = c.Item3,
                    Bets = bets,
                    Cost = bets * 2
                };
            }).ToList();
        }

        /// <summary>
        /// 获取红球复式推荐
        /// </summary>
        public (List<int> core, List<int> alt) GetRedCompoundRecommendation(int periods)
        {
            var freq = GetRedFrequency(periods);
            var core = freq.Where(kv => kv.Value >= 8).Select(kv => kv.Key).OrderBy(x => x).ToList();
            var alt = freq.Where(kv => kv.Value >= 4 && kv.Value <= 7 && !core.Contains(kv.Key))
                .Select(kv => kv.Key).OrderBy(x => x).ToList();
            return (core, alt);
        }

        /// <summary>
        /// 获取蓝球复式推荐
        /// </summary>
        public (List<int> core, List<int> alt) GetBlueCompoundRecommendation(int periods)
        {
            var freq = GetBlueFrequency(periods);
            var core = freq.Where(kv => kv.Value >= 3).Select(kv => kv.Key).OrderBy(x => x).ToList();
            var alt = freq.Where(kv => kv.Value >= 1 && kv.Value <= 2 && !core.Contains(kv.Key))
                .Select(kv => kv.Key).OrderBy(x => x).ToList();
            return (core, alt);
        }

        /// <summary>
        /// 计算三区分布数据
        /// </summary>
        public List<ChartSeries> GetZoneDistribution(int periods)
        {
            var recent = GetRecent(periods);
            var labels = recent.Select(d => d.ShortPeriod).ToList();
            var zoneDefs = new[] { ("一区 01-11", 1, 11), ("二区 12-22", 12, 22), ("三区 23-33", 23, 33) };
            var colors = new[] { ("rgba(224,72,72,0.65)", "#e04848"), ("rgba(212,148,58,0.55)", "#d4943a"), ("rgba(59,130,246,0.45)", "#3b82f6") };

            return zoneDefs.Select((z, zi) => new ChartSeries
            {
                Name = z.Item1,
                Values = recent.Select(d => (double)d.RedBalls.Count(n => n >= z.Item2 && n <= z.Item3)).ToList(),
                IsBar = true,
                FillColor = colors[zi].Item1,
                StrokeColor = colors[zi].Item2
            }).ToList();
        }

        /// <summary>
        /// 计算和值数据
        /// </summary>
        public (List<string> labels, List<double> values) GetSumData(int periods)
        {
            var recent = GetRecent(periods);
            return (
                recent.Select(d => d.ShortPeriod).ToList(),
                recent.Select(d => (double)d.RedSum).ToList()
            );
        }

        /// <summary>
        /// 计算跨度数据
        /// </summary>
        public (List<string> labels, List<double> values) GetSpanData(int periods)
        {
            var recent = GetRecent(periods);
            return (
                recent.Select(d => d.ShortPeriod).ToList(),
                recent.Select(d => (double)d.RedSpan).ToList()
            );
        }

        /// <summary>
        /// 获取奇偶统计数据
        /// </summary>
        public (int oddSum, int evenSum, int maxOdd, int maxEven) GetOddEvenStats(int periods)
        {
            var recent = GetRecent(periods);
            if (recent.Count == 0) return (0, 0, 0, 0);
            var odds = recent.Select(d => d.OddCount).ToList();
            var evens = recent.Select(d => d.EvenCount).ToList();
            return (odds.Sum(), evens.Sum(), odds.Max(), evens.Max());
        }

        /// <summary>
        /// 获取跨度统计数据
        /// </summary>
        public (double avg, int max, int min, double gt25Percent) GetSpanStats(int periods)
        {
            var recent = GetRecent(periods);
            if (recent.Count == 0) return (0, 0, 0, 0);
            var spans = recent.Select(d => d.RedSpan).ToList();
            return (
                Math.Round(spans.Average(), 1),
                spans.Max(),
                spans.Min(),
                Math.Round((double)spans.Count(s => s > 25) / spans.Count * 100)
            );
        }

        private static long Combination(int n, int k)
        {
            if (k < 0 || k > n) return 0;
            if (k == 0 || k == n) return 1;
            k = Math.Min(k, n - k);
            long result = 1;
            for (int i = 1; i <= k; i++)
                result = result * (n - k + i) / i;
            return result;
        }

        private static List<int> PickN(List<int> source, int n)
        {
            var copy = new List<int>(source);
            var result = new List<int>();
            for (int i = 0; i < n && copy.Count > 0; i++)
            {
                var idx = Random.Shared.Next(copy.Count);
                result.Add(copy[idx]);
                copy.RemoveAt(idx);
            }
            return result;
        }
    }
}
