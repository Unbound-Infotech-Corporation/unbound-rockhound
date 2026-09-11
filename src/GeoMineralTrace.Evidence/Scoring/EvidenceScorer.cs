using System.Globalization;
using GeoMineralTrace.Core.DeepAnalysis;
using GeoMineralTrace.Core.Evidence;

namespace GeoMineralTrace.Evidence.Scoring;

/// <summary>
/// Scores each evidence item on the Deep Analysis rubric.
/// See RESEARCH.md § Deep Analysis Scoring Rubric and <see cref="EvidenceScoreBreakdown"/>.
/// </summary>
public sealed class EvidenceScorer
{
    private readonly DeepAnalysisOptions _options;

    public EvidenceScorer(DeepAnalysisOptions? options = null)
    {
        _options = options ?? new DeepAnalysisOptions();
        _options.NormalizeWeights();
    }

    public IReadOnlyList<ScoredEvidence> ScoreAll(IReadOnlyList<EvidenceItem> evidence)
    {
        var active = evidence.Where(e => !e.IsRejected).ToList();
        var keys = active.ToDictionary(e => e.Id, e => ImpliedLocationKey.FromEvidence(e));
        var modalitiesByKey = BuildModalityIndex(active, keys);

        // Majority location key among high-confidence items for consistency.
        var majorityKey = active
            .Where(e => e.Confidence.Value >= 0.5 && keys[e.Id] is not null)
            .GroupBy(e => keys[e.Id]!)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Average(x => x.Confidence.Value))
            .Select(g => g.Key)
            .FirstOrDefault();

        var scored = new List<ScoredEvidence>(active.Count);
        foreach (var item in active)
        {
            var key = keys[item.Id];
            var source = ScoreSourceReliability(item);
            var corroboration = ScoreCorroboration(item, key, modalitiesByKey);
            var specificity = ImpliedLocationKey.SpecificityScore(item);
            var consistency = ScoreConsistency(item, key, majorityKey);
            var plausibility = ScorePlausibility(item);

            var combined =
                _options.WeightSourceReliability * source +
                _options.WeightCorroboration * corroboration +
                _options.WeightSpecificity * specificity +
                _options.WeightConsistency * consistency +
                _options.WeightPlausibility * plausibility;

            combined = Math.Clamp(combined, 0, 1);

            var notes = BuildNotes(source, corroboration, specificity, consistency, plausibility, key);
            var breakdown = new EvidenceScoreBreakdown(
                source, corroboration, specificity, consistency, plausibility, combined, notes);

            scored.Add(new ScoredEvidence(item, breakdown, key, ImpliedLocationKey.ModalityOf(item)));
        }

        return scored;
    }

    private static double ScoreSourceReliability(EvidenceItem item)
    {
        // Prefer existing extractor confidence; boost Accepted, soften Downweighted.
        var baseConf = Math.Clamp(item.Confidence.Value, 0, 1);
        var weightMul = item.UserWeight switch
        {
            EvidenceWeight.Accepted => 1.1,
            EvidenceWeight.Upweighted => 1.05,
            EvidenceWeight.Downweighted => 0.7,
            _ => 1.0
        };
        return Math.Clamp(baseConf * weightMul, 0, 1);
    }

    private static double ScoreCorroboration(
        EvidenceItem item,
        string? key,
        IReadOnlyDictionary<string, HashSet<EvidenceModality>> modalitiesByKey)
    {
        if (key is null)
            return 0.15; // Unanchored evidence can't corroborate geographically.

        if (!modalitiesByKey.TryGetValue(key, out var modalities) || modalities.Count == 0)
            return 0.2;

        // Cross-modal agreement is the strongest signal — scale by distinct modalities.
        // 1 modality → 0.25, 2 → 0.55, 3 → 0.80, 4+ → 1.0
        return modalities.Count switch
        {
            1 => 0.25,
            2 => 0.55,
            3 => 0.80,
            _ => 1.0
        };
    }

    private static double ScoreConsistency(EvidenceItem item, string? key, string? majorityKey)
    {
        if (majorityKey is null || key is null)
            return 0.55; // Neutral when no context yet.

        if (string.Equals(key, majorityKey, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        // Soft conflict: same state prefix vs different place.
        if (key.StartsWith("state:", StringComparison.Ordinal) &&
            majorityKey.StartsWith("place:", StringComparison.Ordinal))
            return 0.45;

        if (key.StartsWith("mineral:", StringComparison.Ordinal) ||
            key.StartsWith("solar:", StringComparison.Ordinal))
            return 0.70; // Minerals/solar don't contradict place alone.

        // Hard conflict between distinct places / GPS cells.
        if ((key.StartsWith("place:") || key.StartsWith("gps:")) &&
            (majorityKey.StartsWith("place:") || majorityKey.StartsWith("gps:")))
        {
            // Penalize unless this item is clearly higher confidence than context.
            return item.Confidence.Value >= 0.75 ? 0.45 : 0.15;
        }

        return 0.40;
    }

    private static double ScorePlausibility(EvidenceItem item)
    {
        if (item.Type is not (EvidenceType.ShadowMeasurement or EvidenceType.SolarLocus))
            return 0.75; // Non-solar: mild default (no strong temporal claim).

        // Solar: elevation must be daytime-plausible; discard night / nonsense ratios.
        if (item.Attributes?.TryGetValue("elevationDeg", out var elevRaw) == true &&
            double.TryParse(elevRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var elev))
        {
            if (elev is < 2 or > 88)
                return 0.05; // Near-horizon or zenith extremes → usually bad measurement.
            if (elev is >= 5 and <= 75)
                return 1.0;
            return 0.55;
        }

        // Fall back to height/shadow raw pair.
        if (!string.IsNullOrWhiteSpace(item.RawContent))
        {
            var parts = item.RawContent.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var l)
                && h > 0 && l > 0)
            {
                var elevRad = Math.Atan(h / l);
                var elevDeg = elevRad * (180.0 / Math.PI);
                if (elevDeg is < 2 or > 88) return 0.05;
                if (elevDeg is >= 5 and <= 75) return 1.0;
                return 0.55;
            }
        }

        return 0.40;
    }

    private static Dictionary<string, HashSet<EvidenceModality>> BuildModalityIndex(
        IReadOnlyList<EvidenceItem> active,
        IReadOnlyDictionary<Guid, string?> keys)
    {
        var dict = new Dictionary<string, HashSet<EvidenceModality>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in active)
        {
            if (keys[item.Id] is not { } key) continue;
            if (!dict.TryGetValue(key, out var set))
            {
                set = [];
                dict[key] = set;
            }

            set.Add(ImpliedLocationKey.ModalityOf(item));
        }

        return dict;
    }

    private static string BuildNotes(
        double source, double corr, double spec, double cons, double plaus, string? key) =>
        $"SR={source:F2} C={corr:F2} S={spec:F2} Con={cons:F2} P={plaus:F2} key={key ?? "(none)"}";
}

public sealed record ScoredEvidence(
    EvidenceItem Item,
    EvidenceScoreBreakdown Breakdown,
    string? LocationKey,
    EvidenceModality Modality);
