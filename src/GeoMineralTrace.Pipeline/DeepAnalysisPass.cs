using GeoMineralTrace.Core.DeepAnalysis;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Core.Solar;
using GeoMineralTrace.Evidence.Scoring;
using GeoMineralTrace.Hypothesis.Fusion;

namespace GeoMineralTrace.Pipeline;

/// <summary>
/// Orchestrates Deep Analysis: score → select corroborating clusters → generate hypotheses.
/// Scoring/selection live in Evidence; hypothesis generation in Hypothesis.
/// </summary>
public sealed class DeepAnalysisPass
{
    private readonly EvidenceScorer _scorer;
    private readonly EvidenceClusterSelector _selector;
    private readonly DeepHypothesisGenerator _generator;
    private readonly DeepAnalysisOptions _options;

    public DeepAnalysisPass(
        DeepAnalysisOptions? options = null,
        HypothesisFusionEngine? fusion = null)
    {
        _options = options ?? new DeepAnalysisOptions();
        _options.NormalizeWeights();
        _scorer = new EvidenceScorer(_options);
        _selector = new EvidenceClusterSelector(_options);
        _generator = new DeepHypothesisGenerator(fusion);
    }

    public DeepAnalysisResult Run(
        Guid analysisSessionId,
        IReadOnlyList<EvidenceItem> evidence,
        SolarLocusResult? solarLocus = null)
    {
        var scored = _scorer.ScoreAll(evidence);
        var selection = _selector.Select(scored);
        selection.ApplyAnnotations();
        var hypotheses = _generator.Generate(analysisSessionId, selection, solarLocus);

        return new DeepAnalysisResult(selection, hypotheses, _options);
    }
}

public sealed record DeepAnalysisResult(
    DeepAnalysisSelectionResult Selection,
    IReadOnlyList<LocationHypothesis> Hypotheses,
    DeepAnalysisOptions Options);
