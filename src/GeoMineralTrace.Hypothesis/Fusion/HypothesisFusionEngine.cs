using System.Globalization;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Core.Solar;

namespace GeoMineralTrace.Hypothesis.Fusion;

/// <summary>
/// Fuses evidence streams into ranked location hypotheses with uncertainty notes.
/// Respects user reject / re-weight via <see cref="EvidenceItem.EffectiveWeight"/>.
/// </summary>
public sealed class HypothesisFusionEngine
{
    public IReadOnlyList<LocationHypothesis> Fuse(
        Guid analysisSessionId,
        IReadOnlyList<EvidenceItem> evidence,
        SolarLocusResult? solarLocus = null)
    {
        var active = evidence.Where(e => !e.IsRejected && e.EffectiveWeight > 0).ToList();
        var hypotheses = new List<LocationHypothesis>();

        if (solarLocus?.PeakProbabilityCell is { } peak)
        {
            hypotheses.Add(new LocationHypothesis
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = analysisSessionId,
                Label = $"Solar locus peak near {peak}",
                Center = peak,
                RadiusKm = EstimateRadiusKm(solarLocus),
                RegionDescription = "Region consistent with measured solar elevation/azimuth constraints.",
                Confidence = Confidence.From(solarLocus.OverallConfidence.Value *
                    AverageWeight(active.Where(e => e.Type is EvidenceType.ShadowMeasurement or EvidenceType.SolarLocus))),
                SupportingEvidenceIds = solarLocus.MeasurementIds
                    .Concat(active.Where(e => e.Type is EvidenceType.ShadowMeasurement or EvidenceType.SolarLocus)
                        .Select(e => e.Id))
                    .Distinct()
                    .ToList(),
                UncertaintyNotes = solarLocus.Limitations.ToList()
            });
        }

        foreach (var place in active.Where(e => e.Type == EvidenceType.PlaceNameMention))
        {
            string? attrPlace = null;
            place.Attributes?.TryGetValue("place", out attrPlace);
            var center = PlaceGazetteer.TryResolve(place.RawContent)
                         ?? PlaceGazetteer.TryResolve(place.Summary)
                         ?? PlaceGazetteer.TryResolve(attrPlace);

            var isStateOnly = place.Summary.StartsWith("State/region", StringComparison.OrdinalIgnoreCase);
            var specificity = PlaceSpecificity(place, center, isStateOnly);
            var confMul = center is null ? 0.45 : 0.62;
            confMul *= specificity;

            // Corroborate specific places with co-mentioned states / minerals from the same board.
            var support = new List<Guid> { place.Id };
            var notes = new List<string>
            {
                "Place-name mentions may be spoken about remote locations, media, or unrelated context.",
                center is null
                    ? "No offline gazetteer match — treat as a text lead only."
                    : "Coordinates from offline gazetteer seed — verify against map and land status."
            };

            if (!isStateOnly && center is not null)
            {
                var states = active.Where(e =>
                    e.Type == EvidenceType.PlaceNameMention &&
                    e.Id != place.Id &&
                    e.Summary.StartsWith("State/region", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var s in states)
                {
                    support.Add(s.Id);
                    confMul = Math.Min(confMul * 1.12, 0.92);
                    notes.Add($"Corroborated by state/region cue '{s.RawContent ?? s.Summary}'.");
                }

                var mineralLeads = active.Where(e => e.Type == EvidenceType.MineralNameMention).ToList();
                if (mineralLeads.Count > 0)
                {
                    foreach (var m in mineralLeads.Take(3))
                        support.Add(m.Id);
                    confMul = Math.Min(confMul * 1.08, 0.95);
                    notes.Add($"Co-mentioned mineral(s): {string.Join(", ", mineralLeads.Select(m => m.RawContent ?? m.Summary).Take(3))}.");
                }
            }

            hypotheses.Add(new LocationHypothesis
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = analysisSessionId,
                Label = isStateOnly
                    ? $"State/region: {place.RawContent ?? place.Summary}"
                    : $"Place mention: {place.Summary}",
                Center = center,
                RadiusKm = center is null ? null : (isStateOnly ? 250 : EstimatePlaceRadiusKm(place)),
                RegionDescription = place.RawContent ?? place.Summary,
                Confidence = Confidence.From(Math.Clamp(place.EffectiveWeight * confMul, 0, 0.95)),
                SupportingEvidenceIds = support.Distinct().ToList(),
                UncertaintyNotes = notes
            });
        }

        foreach (var road in active.Where(e =>
                     e.Type == EvidenceType.OcrText &&
                     (e.RawContent?.Contains("US", StringComparison.OrdinalIgnoreCase) == true ||
                      e.Summary.Contains("Road", StringComparison.OrdinalIgnoreCase) ||
                      e.Summary.Contains("Highway", StringComparison.OrdinalIgnoreCase))))
        {
            hypotheses.Add(new LocationHypothesis
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = analysisSessionId,
                Label = $"Road/route cue: {road.Summary}",
                RegionDescription = road.RawContent ?? road.Summary,
                Confidence = Confidence.From(Math.Clamp(road.EffectiveWeight * 0.55, 0, 0.9)),
                SupportingEvidenceIds = [road.Id],
                UncertaintyNotes =
                [
                    "Route numbers constrain corridors but span long distances.",
                    "Confirm the sign is physically in-scene (not a poster/TV)."
                ]
            });
        }

        foreach (var gps in active.Where(e => e.Type == EvidenceType.GpsEmbed))
        {
            GeoCoordinate? center = null;
            if (gps.Attributes is not null &&
                gps.Attributes.TryGetValue("lat", out var latS) &&
                gps.Attributes.TryGetValue("lon", out var lonS) &&
                double.TryParse(latS, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(lonS, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                center = new GeoCoordinate(lat, lon);
            }

            hypotheses.Add(new LocationHypothesis
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = analysisSessionId,
                Label = $"Embedded GPS: {gps.Summary}",
                Center = center,
                RadiusKm = 5,
                RegionDescription = gps.RawContent ?? gps.Summary,
                Confidence = Confidence.From(Math.Min(0.95, gps.EffectiveWeight)),
                SupportingEvidenceIds = [gps.Id],
                UncertaintyNotes =
                [
                    "Embedded GPS can be spoofed, stale, or refer to a device home location rather than the filmed scene."
                ]
            });
        }

        var minerals = active.Where(e => e.Type == EvidenceType.MineralNameMention).ToList();
        if (minerals.Count > 0)
        {
            var names = minerals.Select(m => m.RawContent ?? m.Summary).Distinct(StringComparer.OrdinalIgnoreCase);
            hypotheses.Add(new LocationHypothesis
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = analysisSessionId,
                Label = $"Mineral cluster: {string.Join(", ", names.Take(5))}",
                RegionDescription = "Mineral mentions cross-linked to rockhounding localities (see Nearby panel).",
                Confidence = Confidence.From(Math.Clamp(minerals.Average(m => m.EffectiveWeight) * 0.35, 0.1, 0.7)),
                SupportingEvidenceIds = minerals.Select(m => m.Id).ToList(),
                UncertaintyNotes =
                [
                    "Mineral talk does not prove the speaker is at a collecting site.",
                    "Cross-check with land status; closed sites must be excluded from field plans."
                ]
            });
        }

        // Weak regional leads from vision/scene tags when text leads are thin.
        var sceneTags = active.Where(e =>
            e.Type is EvidenceType.Terrain or EvidenceType.Vegetation
                or EvidenceType.MiningCue or EvidenceType.SoilRock).ToList();
        if (sceneTags.Count > 0 && hypotheses.Count == 0)
        {
            var labels = sceneTags
                .Select(s => s.Summary)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
            hypotheses.Add(new LocationHypothesis
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = analysisSessionId,
                Label = $"Scene-region cue: {string.Join(", ", labels)}",
                RegionDescription =
                    "Heuristic scene tags only — constrain biome/landform, not a point location.",
                Confidence = Confidence.From(Math.Clamp(
                    sceneTags.Average(s => s.EffectiveWeight) * 0.22, 0.08, 0.4)),
                SupportingEvidenceIds = sceneTags.Select(s => s.Id).ToList(),
                UncertaintyNotes =
                [
                    "Scene tags are weak without OCR/ASR place or mineral corroboration.",
                    "Use Solar / Shadow and Evidence Board to strengthen or reject this lead."
                ]
            });
        }

        return hypotheses
            .OrderByDescending(h => h.Confidence.Value)
            .ThenBy(h => h.RadiusKm ?? 9999) // Prefer tighter geographic claims when confidence ties
            .ThenByDescending(h => h.SupportingEvidenceIds.Count)
            .Select((h, i) =>
            {
                h.Rank = i + 1;
                return h;
            })
            .ToList();
    }

    private static double PlaceSpecificity(EvidenceItem place, GeoCoordinate? center, bool isStateOnly)
    {
        if (isStateOnly)
            return 0.78; // Soft-penalize state centroids vs landmarks

        var text = $"{place.RawContent} {place.Summary}";
        var words = (place.RawContent ?? place.Summary)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var mul = 1.0;
        if (words >= 2)
            mul *= 1.18;
        if (text.Contains("river", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("creek", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("canyon", StringComparison.OrdinalIgnoreCase))
            mul *= 1.12;
        if (center is not null)
            mul *= 1.05;
        return mul;
    }

    private static double EstimatePlaceRadiusKm(EvidenceItem place)
    {
        var text = $"{place.RawContent} {place.Summary}";
        if (text.Contains("river", StringComparison.OrdinalIgnoreCase))
            return 120; // Rivers are corridors, not point sites
        if (text.Contains("creek", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("canyon", StringComparison.OrdinalIgnoreCase))
            return 60;
        return 40;
    }

    private static double EstimateRadiusKm(SolarLocusResult locus)
    {
        if (locus.TargetAzimuthDegrees.HasValue)
            return 150;
        if (locus.MeasurementIds.Count >= 2)
            return 250;
        return 800;
    }

    private static double AverageWeight(IEnumerable<EvidenceItem> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? 1.0 : Math.Clamp(list.Average(i => Math.Min(i.EffectiveWeight, 1.5)), 0.3, 1.2);
    }
}
