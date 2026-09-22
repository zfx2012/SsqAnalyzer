namespace SsqAnalyzer.Services.Kill;

/// <summary>红球 82%、蓝球 94%；固定规则，无手动调节或配置文件覆盖。</summary>
public sealed class KillSettings : IKillSettings
{
    public const double RedMinAccuracy = 0.82;
    public const double BlueMinAccuracy = 0.94;

    public double GetMinAccuracy(BallType ballType) => For(ballType);

    public static double For(BallType ballType) => ballType switch
    {
        BallType.Red => RedMinAccuracy,
        BallType.Blue => BlueMinAccuracy,
        _ => throw new ArgumentOutOfRangeException(nameof(ballType))
    };
}
