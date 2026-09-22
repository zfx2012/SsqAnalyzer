using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

public interface IPositionValidationStore
{
    string FilePath { get; }
    string? LastError { get; }
    event Action? Changed;
    bool Freeze(PositionPrediction prediction);
    int Reconcile();
    PositionForwardSummary GetSummary(string ruleVersionId);
    IReadOnlyList<PositionForwardSummary> GetSummaries();
    PositionLedgerIntegrityReport GetIntegrityReport();
    int SealMissingHashes();
    PositionExperimentComparison CompareVersions(
        string primaryRuleVersionId,
        string candidateRuleVersionId);
}
