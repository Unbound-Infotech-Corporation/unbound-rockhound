using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace_App.Pages;

namespace GeoMineralTrace_App.Services;

public sealed record HypothesisMapPin(
    Guid Id,
    double Latitude,
    double Longitude,
    string Label,
    double Confidence,
    string Summary,
    bool IsLead,
    int Rank);

public sealed class MapFocusRequest
{
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public int Zoom { get; init; } = 11;
    public Guid? HighlightHypothesisId { get; init; }
    public IReadOnlyList<HypothesisMapPin> HypothesisPins { get; init; } = [];

    /// <summary>When set, map emphasizes localities reporting this gem/mineral within the hunt radius.</summary>
    public string? MineralFilter { get; init; }

    /// <summary>Override home search radius for this focus (miles). Used by “find today” hunts.</summary>
    public double? RadiusMiles { get; init; }

    /// <summary>Optional status line shown under the map caption.</summary>
    public string? CaptionHint { get; init; }
}

/// <summary>Passes in-app map center/zoom intent between pages.</summary>
public static class MapFocusService
{
    public static MapFocusRequest? Pending { get; private set; }

    public static MapFocusRequest FromHypotheses(
        IEnumerable<LocationHypothesis> hypotheses,
        Guid? highlightHypothesisId = null,
        int zoom = 11)
    {
        var ordered = HypothesisPresentation.OrderForDisplay(hypotheses)
            .Where(h => h.Center is not null)
            .ToList();

        if (ordered.Count == 0)
            throw new InvalidOperationException("No geolocated hypotheses to show on the map.");

        var lead = ordered.FirstOrDefault();
        var highlight = highlightHypothesisId is { } id
            ? ordered.FirstOrDefault(h => h.Id == id) ?? lead
            : lead;

        var center = highlight?.Center ?? ordered[0].Center
            ?? throw new InvalidOperationException("No geolocated hypotheses to show on the map.");
        var pins = ordered.Select((h, i) =>
        {
            var c = h.Center!.Value;
            return new HypothesisMapPin(
                h.Id,
                c.LatitudeDegrees,
                c.LongitudeDegrees,
                h.Label,
                h.Confidence.Value,
                HypothesisPresentation.BuildReasoningSummary(h),
                i == 0,
                h.Rank);
        }).ToList();

        return new MapFocusRequest
        {
            Latitude = center.LatitudeDegrees,
            Longitude = center.LongitudeDegrees,
            Zoom = zoom,
            HighlightHypothesisId = highlight?.Id,
            HypothesisPins = pins
        };
    }

    public static void NavigateToMap(MapFocusRequest request)
    {
        Pending = request;
        MainWindow.Instance?.NavigateTo(typeof(MapPage), "map", request);
    }

    public static void NavigateToHypotheses(IReadOnlyList<LocationHypothesis> hypotheses, Guid? highlightId = null) =>
        NavigateToMap(FromHypotheses(hypotheses, highlightId));

    public static MapFocusRequest FromHistoryEntry(AnalysisHistoryEntry entry, int zoom = 11)
    {
        if (entry.LeadLatitude is not { } lat || entry.LeadLongitude is not { } lon)
            throw new InvalidOperationException("This history entry has no saved coordinates.");

        var summary = entry.LeadReasoningSummary ?? "Saved analysis hypothesis";
        var pin = new HypothesisMapPin(
            entry.LeadHypothesisId ?? entry.SessionId,
            lat,
            lon,
            entry.LeadLabel ?? entry.Title,
            entry.LeadConfidence ?? 0,
            summary,
            true,
            1);

        return new MapFocusRequest
        {
            Latitude = lat,
            Longitude = lon,
            Zoom = zoom,
            HighlightHypothesisId = pin.Id,
            HypothesisPins = [pin]
        };
    }

    public static void NavigateFromHistory(AnalysisHistoryEntry entry) =>
        NavigateToMap(FromHistoryEntry(entry));

    /// <summary>Home “find today” — center on home and filter localities by mineral within radius.</summary>
    public static MapFocusRequest ForMineralHunt(
        string mineral,
        double latitude,
        double longitude,
        double radiusMiles = 100,
        int matchCount = 0)
    {
        var name = mineral.Trim();
        var zoom = radiusMiles <= 50 ? 10 : radiusMiles <= 100 ? 9 : radiusMiles <= 250 ? 8 : 7;
        return new MapFocusRequest
        {
            Latitude = latitude,
            Longitude = longitude,
            Zoom = zoom,
            MineralFilter = name,
            RadiusMiles = radiusMiles,
            CaptionHint = matchCount > 0
                ? $"Find today: {name} — {matchCount} site(s) within {radiusMiles:0} mi"
                : $"Find today: {name} — no sites in the local database within {radiusMiles:0} mi"
        };
    }

    public static void NavigateToMineralHunt(
        string mineral,
        double latitude,
        double longitude,
        double radiusMiles = 100,
        int matchCount = 0) =>
        NavigateToMap(ForMineralHunt(mineral, latitude, longitude, radiusMiles, matchCount));

    public static MapFocusRequest? ConsumePending()
    {
        var pending = Pending;
        Pending = null;
        return pending;
    }
}
