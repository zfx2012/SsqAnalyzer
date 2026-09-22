using System.Globalization;
using SsqAnalyzer.Models;

// Frozen pre-optimization parser used only as a compatibility oracle.
internal static partial class DataParsingVerification
{
        internal static List<DrawRecord> ParseReference(string[] lines)
        {
            var records = new List<DrawRecord>(lines.Length);
            foreach (var line in lines)
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 9) continue;

                if (!int.TryParse(parts[0], out int period)) continue;
                if (!DateTime.TryParseExact(parts[1], "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                var reds = new int[6];
                bool redOk = true;
                for (int i = 0; i < 6; i++)
                {
                    if (!int.TryParse(parts[2 + i], out reds[i]))
                    { redOk = false; break; }
                }
                if (!redOk) continue;
                Array.Sort(reds);

                if (!int.TryParse(parts[8], out int blue)) continue;

                // 早期数据(2003-2004)可能不包含销售/奖池字段
                long p15 = 0, p16 = 0, p18 = 0, p20 = 0;
                int p17 = 0, p19 = 0, p21 = 0, p23 = 0, p25 = 0, p27 = 0;
                if (parts.Length > 15) long.TryParse(parts[15], out p15);
                if (parts.Length > 16) long.TryParse(parts[16], out p16);
                if (parts.Length > 17) int.TryParse(parts[17], out p17);
                if (parts.Length > 18) long.TryParse(parts[18], out p18);
                if (parts.Length > 19) int.TryParse(parts[19], out p19);
                if (parts.Length > 20) long.TryParse(parts[20], out p20);
                if (parts.Length > 21) int.TryParse(parts[21], out p21);
                if (parts.Length > 23) int.TryParse(parts[23], out p23);
                if (parts.Length > 25) int.TryParse(parts[25], out p25);
                if (parts.Length > 27) int.TryParse(parts[27], out p27);

                records.Add(new DrawRecord
                {
                    Period = period,
                    DrawDate = date,
                    RedBalls = reds,
                    BlueBall = blue,
                    SalesAmount = p15,
                    PoolAmount = p16,
                    FirstPrizeCount = p17,
                    FirstPrizeAmount = p18,
                    SecondPrizeCount = p19,
                    SecondPrizeAmount = p20,
                    ThirdPrizeCount = p21,
                    FourthPrizeCount = p23,
                    FifthPrizeCount = p25,
                    SixthPrizeCount = p27
                });
            }
            return records;
        }

}
