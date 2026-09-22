using System.Windows.Media;

namespace SsqAnalyzer;

/// <summary>
/// 全局主题颜色刷统一管理（避免各控件重复定义相同颜色）
/// </summary>
internal static class UiColors
{
    // 背景
    public static readonly Brush BgHeader = Fx(new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)));
    public static readonly Brush BgRow0 = Fx(new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)));
    public static readonly Brush BgRow1 = Fx(new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)));
    public static readonly Brush BgDivider = Fx(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)));
    public static readonly Brush BgRowHi = Fx(new SolidColorBrush(Color.FromRgb(0xEA, 0xF4, 0xFC)));
    public static readonly Brush BgPredRow = Fx(new SolidColorBrush(Color.FromRgb(0xF8, 0xF4, 0xF0)));
    public static readonly Brush BgZoneEmpty = Fx(new SolidColorBrush(Color.FromArgb(100, 255, 200, 200)));

    // 边框
    public static readonly Brush Border = Fx(new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)));
    public static readonly Brush BorderDivider = Fx(new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)));

    // 号码球
    public static readonly Brush Red = Fx(new SolidColorBrush(Color.FromRgb(0xE0, 0x48, 0x48)));
    public static readonly Brush Blue = Fx(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)));
    public static readonly Brush ColdStroke = Fx(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));

    // 文字
    public static readonly Brush TextSec = Fx(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)));
    public static readonly Brush TextTri = Fx(new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)));

    // 遗漏颜色
    public static readonly Brush MissHigh = Fx(new SolidColorBrush(Color.FromRgb(0xE0, 0x48, 0x48)));
    public static readonly Brush MissMid = Fx(new SolidColorBrush(Color.FromRgb(0xD4, 0x94, 0x3A)));
    public static readonly Brush MissLow = Fx(new SolidColorBrush(Color.FromRgb(0x7A, 0xAA, 0x3A)));
    public static readonly Brush MissNone = Fx(new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)));
    public static readonly Brush MissBlueHot = Fx(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)));
    public static readonly Brush MissBlueWarm = Fx(new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)));

    // 统计行
    public static readonly Brush StatsBg = Fx(new SolidColorBrush(Color.FromArgb(30, 0, 120, 212)));
    public static readonly Brush White = Brushes.White;

    private static Brush Fx(Brush b) { b.Freeze(); return b; }

    // 动态统计单元格颜色
    public static Brush StatsCellBg(int freq, double avg, Brush fallback)
    {
        if (freq <= 0) return fallback;
        if (freq >= avg) return White;
        double ratio = freq / avg;
        int r = 250 - (int)(ratio * 200);
        int gb = 120 + (int)(ratio * 100);
        r = Math.Clamp(r, 50, 250);
        gb = Math.Clamp(gb, 120, 220);
        return new SolidColorBrush(Color.FromRgb((byte)r, (byte)gb, (byte)gb));
    }
}
