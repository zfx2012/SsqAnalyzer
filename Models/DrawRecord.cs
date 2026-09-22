namespace SsqAnalyzer.Models
{
    /// <summary>
    /// 双色球开奖记录
    /// </summary>
    public class DrawRecord
    {
        public int Period { get; set; }            // 期号，如 2026068
        public DateTime DrawDate { get; set; }      // 开奖日期
        public IReadOnlyList<int> RedBalls { get; set; } = Array.Empty<int>();
        public int BlueBall { get; set; }            // 1个蓝球，1-16
        public long SalesAmount { get; set; }        // 销售额（元）
        public long PoolAmount { get; set; }         // 奖池金额（元）
        public int FirstPrizeCount { get; set; }     // 一等奖注数
        public long FirstPrizeAmount { get; set; }   // 一等奖单注奖金（元）
        public int SecondPrizeCount { get; set; }    // 二等奖注数
        public long SecondPrizeAmount { get; set; }  // 二等奖单注奖金（元）
        public int ThirdPrizeCount { get; set; }     // 三等奖注数
        public int FourthPrizeCount { get; set; }    // 四等奖注数
        public int FifthPrizeCount { get; set; }     // 五等奖注数
        public int SixthPrizeCount { get; set; }     // 六等奖注数

        // 计算属性
        public int RedSum => RedBalls.Sum();
        public int RedSpan => RedBalls.Max() - RedBalls.Min();
        public int OddCount => RedBalls.Count(n => n % 2 == 1);
        public int EvenCount => 6 - OddCount;

        // 三区比：1-11一区, 12-22二区, 23-33三区
        public string ZoneLabel
        {
            get
            {
                int z1 = RedBalls.Count(n => n <= 11);
                int z2 = RedBalls.Count(n => n >= 12 && n <= 22);
                int z3 = RedBalls.Count(n => n >= 23);
                return $"{z1}:{z2}:{z3}";
            }
        }

        // 大小比：1-16小, 17-33大
        public string BigSmallLabel
        {
            get
            {
                int small = RedBalls.Count(n => n <= 16);
                int big = 6 - small;
                return $"{big}:{small}";
            }
        }

        // 质合比：质数集合
        private static readonly HashSet<int> Primes = new() { 2,3,5,7,11,13,17,19,23,29,31 };
        public string PrimeLabel
        {
            get
            {
                int p = RedBalls.Count(n => Primes.Contains(n));
                return $"{p}:{6 - p}";
            }
        }

        // 连号个数：连续段计数（3连号算1个，如 16,17,18 → 1）
        public int LinkCount
        {
            get
            {
                int count = 0;
                int i = 0;
                while (i < RedBalls.Count)
                {
                    int j = i;
                    while (j + 1 < RedBalls.Count && RedBalls[j + 1] - RedBalls[j] == 1)
                        j++;
                    if (j > i) count++; // 至少2个连续算1个连号
                    i = j + 1;
                }
                return count;
            }
        }

        // 012路：按模3余数
        public string ZO2Label
        {
            get
            {
                int r0 = RedBalls.Count(n => n % 3 == 0);
                int r1 = RedBalls.Count(n => n % 3 == 1);
                int r2 = RedBalls.Count(n => n % 3 == 2);
                return $"{r0}:{r1}:{r2}";
            }
        }

        public string PeriodLabel => Period.ToString();
        public string ShortPeriod => (Period % 10000).ToString("D4");
        public string DateLabel => DrawDate.ToString("yyyy-MM-dd");
        public int Year => DrawDate.Year;
        public string WeekDayName => DrawDate.DayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            DayOfWeek.Sunday => "周日",
            _ => ""
        };
    }
}
