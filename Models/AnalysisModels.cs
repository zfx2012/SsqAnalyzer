using System.Collections.ObjectModel;

namespace SsqAnalyzer.Models
{
    /// <summary>
    /// 号码频率统计
    /// </summary>
    public class BallFrequency
    {
        public int Number { get; set; }
        public int Count { get; set; }
        public string Label => Number.ToString("D2");
        public double FrequencyPercent { get; set; }
    }

    /// <summary>
    /// 红球组合频率
    /// </summary>
    public class PairFrequency
    {
        public int N1 { get; set; }
        public int N2 { get; set; }
        public int Count { get; set; }
        public int Rank { get; set; }
        public string Key => $"{N1:D2}-{N2:D2}";
        public string Balls => $"{N1:D2} {N2:D2}";
    }

    /// <summary>
    /// 杀号推荐
    /// </summary>
    public class KillRecommendation
    {
        public string Title { get; set; } = "";
        public string Confidence { get; set; } = ""; // "high" or "mid"
        public ObservableCollection<int> Numbers { get; set; } = new();
        public string Reason { get; set; } = "";
        public bool HasItems => Numbers.Count > 0;
    }

    /// <summary>
    /// 组号推荐
    /// </summary>
    public class GroupRecommendation
    {
        public int Index { get; set; }
        public string Style { get; set; } = "";
        public string Description { get; set; } = "";
        public int[] RedBalls { get; set; } = Array.Empty<int>();
        public int BlueBall { get; set; }
        public string BlueLabel => BlueBall.ToString("D2");
        public string RedLabels => string.Join(" ", RedBalls.Select(n => n.ToString("D2")));
    }

    /// <summary>
    /// 复式成本
    /// </summary>
    public class CompoundCost
    {
        public string Label { get; set; } = "";
        public int RedCount { get; set; }
        public int BlueCount { get; set; }
        public int Bets { get; set; }
        public int Cost { get; set; }
    }

    /// <summary>
    /// 周期分组的统计数据点
    /// </summary>
    public class ChartDataPoint
    {
        public string Label { get; set; } = "";
        public double Value { get; set; }
        public string? Color { get; set; }
    }

    /// <summary>
    /// 图表系列数据
    /// </summary>
    public class ChartSeries
    {
        public string Name { get; set; } = "";
        public List<double> Values { get; set; } = new();
        public string FillColor { get; set; } = "";
        public string StrokeColor { get; set; } = "";
        public bool IsBar { get; set; } = true;
    }

    /// <summary>
    /// 导航页面定义
    /// </summary>
    public class NavPage
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "";
        public string? Badge { get; set; }
    }
}
