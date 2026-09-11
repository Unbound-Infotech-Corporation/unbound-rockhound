using GeoMineralTrace.Core.DeepAnalysis;
using GeoMineralTrace.Core.Evidence;

namespace GeoMineralTrace.Evidence.Scoring;

/// <summary>
/// Selects the smallest corroborating evidence set(s): cluster by implied location,
/// score each cluster by avg confidence × modality diversity, keep clusters above threshold.
/// Non-selected items are retained with "considered, not selected" notes.
/// </summary>
public sealed class EvidenceClusterSelector
{
    private readonly DeepAnalysisOptions _options;

    public EvidenceClusterSelector(DeepAnalysisOptions? options = null)
    {
        _options = options ?? new DeepAnalysisOptions();
    }

    public DeepAnalysisSelectionResult Select(IReadOnlyList<ScoredEvidence> scored)
    {
        var eligible = scored
            .Where(s => s.Breakdown.Combined >= _options.MinimumItemConfidence)
            .ToList();

        var clusters = new List<EvidenceCluster>();

        // Group by location key; unkeyed items form singleton clusters only if highly specific.
        foreach (var group in eligible.GroupBy(s => s.LocationKey ?? $"orphan:{s.Item.Id:N}"))
        {
            var items = group.ToList();
            clusters.Add(BuildCluster(group.Key, items));
        }

        // Attach mineral / solar / state satellites to the strongest place|gps cluster
        // so "#montana" + "Yellowstone River" corroborate instead of competing.
        clusters = MergeSatelliteClusters(clusters);

        // Prefer fewer high-corroboration clusters over many unrelated high scorers.
        var selectedClusters = clusters
            .Where(c => c.ClusterScore >= _options.MinimumClusterConfidence)
            .OrderByDescending(c => c.ClusterScore)
            .ThenByDescending(c => c.ModalityCount)
            .ThenByDescending(c => c.Items.Count)
            .Take(_options.MaxClusters)
            .ToList();

        // If nothing cleared the threshold but we have eligible items, take the single best cluster
        // only when it has ≥2 modalities (avoid overconfident single-cue hypotheses).
        if (selectedClusters.Count == 0)
        {
            var best = clusters
                .OrderByDescending(c => c.ClusterScore)
                .FirstOrDefault(c => c.ModalityCount >= 2 && c.ClusterScore >= _options.MinimumClusterConfidence * 0.85);
            if (best is not null)
                selectedClusters = [best];
        }

        var selectedIds = selectedClusters
            .SelectMany(c => c.Items.Select(i => i.Item.Id))
            .ToHashSet();

        var considered = new List<ConsideredEvidence>();
        foreach (var s in scored)
        {
            if (selectedIds.Contains(s.Item.Id))
            {
                considered.Add(new ConsideredEvidence(
                    s,
                    Selected: true,
                    Note: $"Selected into cluster '{s.LocationKey ?? "orphan"}' (score={s.Breakdown.Combined:F2})."));
            }
            else
            {
                var reason = s.Breakdown.Combined < _options.MinimumItemConfidence
                    ? $"considered, not selected: item score {s.Breakdown.Combined:F2} below threshold {_options.MinimumItemConfidence:F2}"
                    : selectedClusters.Count == 0
                        ? $"considered, not selected: no cluster cleared minimum {_options.MinimumClusterConfidence:F2} (best key={s.LocationKey ?? "none"})"
                        : $"considered, not selected: not in top corroborating cluster(s) (score={s.Breakdown.Combined:F2}, key={s.LocationKey ?? "none"})";
                considered.Add(new ConsideredEvidence(s, Selected: false, Note: reason));
            }
        }

        return new DeepAnalysisSelectionResult(selectedClusters, considered, _options);
    }

    private static EvidenceCluster BuildCluster(string key, IReadOnlyList<ScoredEvidence> items)
    {
        var modalities = items.Select(i => i.Modality).Distinct().Count();
        // Place + state (+ mineral) are independent geographic claims even when both are AsrNer.
        var claimDiversity = items
            .Select(i => ClaimBucket(i))
            .Distinct()
            .Count();
        var diversity = Math.Max(modalities, claimDiversity);

        var avg = items.Average(i => i.Breakdown.Combined);
        var clusterScore = Math.Clamp(avg * Math.Sqrt(diversity) / 2.0 * Math.Min(diversity, 4), 0, 1);
        if (diversity >= 2)
            clusterScore = Math.Clamp(clusterScore + 0.12 * (diversity - 1), 0, 1);
        if (items.Any(i => (i.LocationKey ?? "").StartsWith("state:", StringComparison.OrdinalIgnoreCase))
            && key.StartsWith("place:", StringComparison.OrdinalIgnoreCase))
            clusterScore = Math.Clamp(clusterScore + 0.10, 0, 1);

        return new EvidenceCluster(key, items, avg, diversity, clusterScore);
    }

    private static string ClaimBucket(ScoredEvidence s)
    {
        var key = s.LocationKey ?? "";
        if (key.StartsWith("place:", StringComparison.OrdinalIgnoreCase)) return "place";
        if (key.StartsWith("state:", StringComparison.OrdinalIgnoreCase)) return "state";
        if (key.StartsWith("mineral:", StringComparison.OrdinalIgnoreCase)) return "mineral";
        if (key.StartsWith("gps:", StringComparison.OrdinalIgnoreCase)) return "gps";
        if (key.StartsWith("solar:", StringComparison.OrdinalIgnoreCase)) return "solar";
        return s.Modality.ToString();
    }

    private static List<EvidenceCluster> MergeSatelliteClusters(List<EvidenceCluster> clusters)
    {
        var anchors = clusters
            .Where(c => c.Key.StartsWith("place:", StringComparison.OrdinalIgnoreCase)
                        || c.Key.StartsWith("gps:", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.ClusterScore)
            .ToList();
        if (anchors.Count == 0)
            return clusters;

        var primary = anchors[0];
        var satellites = clusters
            .Where(c => c.Key.StartsWith("mineral:", StringComparison.OrdinalIgnoreCase)
                        || c.Key.StartsWith("solar:", StringComparison.OrdinalIgnoreCase)
                        || c.Key.StartsWith("state:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (satellites.Count == 0)
            return clusters;

        // Prefer the most specific place:* anchor (longest landmark-ish key) when several exist.
        var bestPlace = anchors
            .Where(c => c.Key.StartsWith("place:", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(PlaceAnchorQuality)
            .ThenByDescending(c => c.Key.Length)
            .ThenByDescending(c => c.ClusterScore)
            .FirstOrDefault() ?? primary;

        var mergedItems = bestPlace.Items.Concat(satellites.SelectMany(s => s.Items)).ToList();
        var merged = BuildCluster(bestPlace.Key, mergedItems);
        var rest = clusters
            .Where(c => c.Key != bestPlace.Key && !satellites.Any(s => s.Key == c.Key))
            .ToList();
        rest.Insert(0, merged);
        return rest;
    }

    private static double PlaceAnchorQuality(EvidenceCluster c)
    {
        var key = c.Key;
        var score = 0.0;
        foreach (var cue in new[] { "river", "creek", "canyon", "mountain", "butte", "gulch", "mine", "falls", "lake" })
        {
            if (key.Contains(cue, StringComparison.OrdinalIgnoreCase))
                score += 3.0;
        }
        // Multi-word place keys are usually real toponyms.
        score += key.Count(ch => ch == ' ') * 1.5;
        score += Math.Min(c.ClusterScore, 1.0);
        return score;
    }
}

public sealed record EvidenceCluster(
    string Key,
    IReadOnlyList<ScoredEvidence> Items,
    double AverageConfidence,
    int ModalityCount,
    double ClusterScore);

public sealed record ConsideredEvidence(
    ScoredEvidence Scored,
    bool Selected,
    string Note);

public sealed record DeepAnalysisSelectionResult(
    IReadOnlyList<EvidenceCluster> SelectedClusters,
    IReadOnlyList<ConsideredEvidence> AllConsidered,
    DeepAnalysisOptions Options)
{
    public IReadOnlyList<EvidenceItem> SelectedEvidence =>
        SelectedClusters.SelectMany(c => c.Items.Select(i => i.Item)).ToList();

    /// <summary>Annotate mutable Deep* fields on the original evidence items.</summary>
    public void ApplyAnnotations()
    {
        foreach (var c in AllConsidered)
        {
            var item = c.Scored.Item;
            item.DeepScore = c.Scored.Breakdown.Combined;
            item.DeepSelected = c.Selected;
            item.DeepSelectionNote = c.Note;
            item.DeepBreakdown = c.Scored.Breakdown;
        }
    }
}
