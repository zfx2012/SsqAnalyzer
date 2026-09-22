namespace SsqAnalyzer.Services.Kill;

/// <summary>固定的分球种杀号门槛，不读取旧的可调门槛配置。</summary>
public interface IKillSettings
{
    double GetMinAccuracy(BallType ballType);
}
