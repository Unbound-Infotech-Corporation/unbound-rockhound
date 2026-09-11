using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Core.Solar;
using GeoMineralTrace.Evidence.Scoring;

namespace GeoMineralTrace.Hypothesis.Fusion;

/// <summary>
/// Generates one ranked hypothesis per selected Deep Analysis cluster,
/// with inspectable corroboration reasoning. Reuses <see cref="HypothesisFusionEngine"/>.
/// </summary>
public sealed class DeepHypothesisGenerator
{
    private readonly HypothesisFusionEngine _fusion;

    public DeepHypothesisGenerator(HypothesisFusionEngine? fusion = null)
    {
        _fusion = fusion ?? new HypothesisFusionEngine();
    }

    public IReadOnlyList<LocationHypothesis> Generate(
        Guid analysisSessionId,
        DeepAnalysisSelectionResult selection,
        SolarLocusResult? solarLocus = null)
    {
        if (selection.SelectedClusters.Count == 0)
            return [];

        var hypotheses = new List<LocationHypothesis>();
        foreach (var cluster in selection.SelectedClusters)
        {
            var clusterEvidence = cluster.Items.Select(i => i.Item).ToList();
            // Only pass solar into Fuse when this cluster includes solar evidence or is the solar key.
            var solarForCluster = cluster.Key.StartsWith("solar:", StringComparison.OrdinalIgnoreCase)
                                  || clusterEvidence.Any(e => e.Type is EvidenceType.ShadowMeasurement or EvidenceType.SolarLocus)
                ? solarLocus
                : null;

            var fused = _fusion.Fuse(analysisSessionId, clusterEvidence, solarForCluster);
            if (fused.Count == 0)
            {
                // Always emit at least one hypothesis for a selected cluster so reasoning is visible.
                hypotheses.Add(BuildFallbackHypothesis(analysisSessionId, cluster));
                continue;
            }

            // Take the top fused hyp for this cluster and enrich with Deep Analysis reasoning.
            var top = fused[0];
            var reasoning = BuildReasoning(cluster, top);
            hypotheses.Add(new LocationHypothesis
            {
                Id = top.Id,
                AnalysisSessionId = analysisSessionId,
                Label = $"Deep: {top.Label}",
                Center = top.Center,
                RadiusKm = top.RadiusKm,
                RegionDescription = top.RegionDescription,
                Confidence = Confidence.From(Math.Clamp(
                    top.Confidence.Value * 0.55 + cluster.ClusterScore * 0.45, 0.05, 0.98)),
                SupportingEvidenceIds = clusterEvidence.Select(e => e.Id).Distinct().ToList(),
                UncertaintyNotes = top.UncertaintyNotes,
                Reasoning = reasoning
            });
        }

        return hypotheses
            .OrderByDescending(h => h.Confidence.Value)
            .Select((h, i) =>
            {
                h.Rank = i + 1;
                return h;
            })
            .ToList();
    }

    private static LocationHypothesis BuildFallbackHypothesis(Guid sessionId, EvidenceCluster cluster)
    {
        var labels = cluster.Items
            .Select(i => i.Item.Summary)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4);
        return new LocationHypothesis
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = sessionId,
            Label = $"Deep cluster: {string.Join("; ", labels)}",
            RegionDescription = $"Corroborating cluster key={cluster.Key}",
            Confidence = Confidence.From(cluster.ClusterScore),
            SupportingEvidenceIds = cluster.Items.Select(i => i.Item.Id).ToList(),
            UncertaintyNotes =
            [
                "Cluster cleared Deep Analysis threshold but first-pass Fuse produced no typed hypothesis.",
                "Treat as a lead set — verify on map and Evidence Board."
            ],
            Reasoning = BuildReasoning(cluster, null)
        };
    }

    private static IReadOnlyList<string> BuildReasoning(EvidenceCluster cluster, LocationHypothesis? fusedTop)
    {
        var lines = new List<string>
        {
            $"Cluster key '{cluster.Key}' scored {cluster.ClusterScore:F2} " +
            $"(avg item {cluster.AverageConfidence:F2} × {cluster.ModalityCount} modalities).",
            "Selected evidence:"
        };

        foreach (var item in cluster.Items.OrderByDescending(i => i.Breakdown.Combined))
        {
            lines.Add(
                $"  • [{item.Modality}] {item.Item.Type}: {item.Item.Summary} " +
                $"(combined={item.Breakdown.Combined:F2}; {item.Breakdown.Notes})");
        }

        if (fusedTop is not null)
            lines.Add($"Fuse top label: {fusedTop.Label} (base conf={fusedTop.Confidence.Value:F2}).");

        lines.Add(
            "Corroboration: multi-modal agreement within this cluster was preferred over " +
            "unrelated high-scoring single cues.");
        return lines;
    }
}
