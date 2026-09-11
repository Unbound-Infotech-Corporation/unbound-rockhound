using GeoMineralTrace.Core.DeepAnalysis;
using GeoMineralTrace.Core.Geo;
namespace GeoMineralTrace.Core.Hypothesis;

/// <summary>Shared formatting for hypothesis hero cards, map popups, and history rows.</summary>
public static class HypothesisPresentation
{
    public const double RunnerUpMinimumConfidence = 0.25;

    public static string BuildReasoningSummary(LocationHypothesis h, int maxParts = 2)
    {
        var parts = new List<string>();
        if (h.Reasoning is { Count: > 0 } reasoning)
            parts.AddRange(reasoning.Take(maxParts));
        else if (h.UncertaintyNotes is { Count: > 0 } notes)
            parts.AddRange(notes.Take(maxParts));
        return parts.Count == 0 ? "Ranked by corroborating evidence." : string.Join(" · ", parts);
    }

    public static string FormatCoordinates(GeoCoordinate? center) =>
        center is not { } c
            ? "Coordinates pending"
            : $"{c.LatitudeDegrees:F4}°, {c.LongitudeDegrees:F4}°";

    public static IReadOnlyList<LocationHypothesis> OrderForDisplay(IEnumerable<LocationHypothesis> hypotheses) =>
        hypotheses.OrderBy(h => h.Rank).ThenByDescending(h => h.Confidence.Value).ToList();

    public static LocationHypothesis? SelectLead(IEnumerable<LocationHypothesis> hypotheses) =>
        OrderForDisplay(hypotheses).FirstOrDefault();

    public static IReadOnlyList<LocationHypothesis> SelectRunnersUp(
        IEnumerable<LocationHypothesis> hypotheses,
        DeepAnalysisOptions? options = null)
    {
        var ordered = OrderForDisplay(hypotheses);
        if (ordered.Count <= 1)
            return [];

        var threshold = options?.MinimumClusterConfidence ?? RunnerUpMinimumConfidence;
        var maxRunners = Math.Max(0, (options?.MaxClusters ?? 3) - 1);
        return ordered.Skip(1)
            .Where(h => h.Confidence.Value >= threshold)
            .Take(maxRunners)
            .ToList();
    }
}
