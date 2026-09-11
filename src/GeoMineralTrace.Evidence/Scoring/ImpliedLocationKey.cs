using System.Globalization;
using System.Text.RegularExpressions;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Evidence.Scoring;

/// <summary>
/// Lightweight implied-location key for clustering without a full gazetteer.
/// GPS uses ~0.5° cells; place text is normalized; state codes extracted.
/// </summary>
public static class ImpliedLocationKey
{
    private static readonly Regex StateCode = new(
        @"\b(AL|AK|AZ|AR|CA|CO|CT|DE|FL|GA|HI|ID|IL|IN|IA|KS|KY|LA|ME|MD|MA|MI|MN|MS|MO|MT|NE|NV|NH|NJ|NM|NY|NC|ND|OH|OK|OR|PA|RI|SC|SD|TN|TX|UT|VT|VA|WA|WV|WI|WY)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> LandmarkCues = new(StringComparer.OrdinalIgnoreCase)
    {
        "mine", "mountain", "mtn", "canyon", "creek", "river", "butte", "gulch", "ranch",
        "springs", "falls", "peak", "pass", "ridge", "valley", "desert", "lake",
        "forest", "park", "quarry", "claim", "diggings"
    };

    /// <summary>Full U.S. state/region names → postal codes for clustering with place leads.</summary>
    private static readonly Dictionary<string, string> StateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alabama"] = "AL", ["alaska"] = "AK", ["arizona"] = "AZ", ["arkansas"] = "AR",
        ["california"] = "CA", ["colorado"] = "CO", ["connecticut"] = "CT", ["delaware"] = "DE",
        ["florida"] = "FL", ["georgia"] = "GA", ["hawaii"] = "HI", ["idaho"] = "ID",
        ["illinois"] = "IL", ["indiana"] = "IN", ["iowa"] = "IA", ["kansas"] = "KS",
        ["kentucky"] = "KY", ["louisiana"] = "LA", ["maine"] = "ME", ["maryland"] = "MD",
        ["massachusetts"] = "MA", ["michigan"] = "MI", ["minnesota"] = "MN", ["mississippi"] = "MS",
        ["missouri"] = "MO", ["montana"] = "MT", ["nebraska"] = "NE", ["nevada"] = "NV",
        ["ohio"] = "OH", ["oklahoma"] = "OK", ["oregon"] = "OR", ["pennsylvania"] = "PA",
        ["tennessee"] = "TN", ["texas"] = "TX", ["utah"] = "UT", ["vermont"] = "VT",
        ["virginia"] = "VA", ["washington"] = "WA", ["wisconsin"] = "WI", ["wyoming"] = "WY",
        ["new mexico"] = "NM", ["new york"] = "NY", ["new jersey"] = "NJ", ["new hampshire"] = "NH",
        ["north carolina"] = "NC", ["south carolina"] = "SC", ["north dakota"] = "ND",
        ["south dakota"] = "SD", ["west virginia"] = "WV", ["rhode island"] = "RI"
    };

    public static string? FromEvidence(EvidenceItem item)
    {
        // Non-locative plumbing must never invent place keys (was clustering
        // "Metadata parse limited…" as place:metadata parse limited).
        if (item.Type is EvidenceType.Metadata or EvidenceType.Keyframe
            or EvidenceType.AudioTranscript)
            return null;

        // Typed mineral / solar leads must not go through place-phrase extraction
        // ("Mineral/gem mention: agate" was becoming place:agate mineral).
        if (item.Type == EvidenceType.MineralNameMention)
        {
            var mineral = (item.RawContent ?? item.Summary).Trim().ToLowerInvariant();
            if (mineral.StartsWith("mineral/", StringComparison.Ordinal) ||
                mineral.Contains("mention:", StringComparison.Ordinal))
                mineral = (item.RawContent ?? mineral).Trim().ToLowerInvariant();
            if (mineral.Length > 0)
                return $"mineral:{mineral}";
        }

        if (item.Type is EvidenceType.ShadowMeasurement or EvidenceType.SolarLocus)
            return "solar:locus";

        if (item.Type == EvidenceType.GpsEmbed && TryGps(item, out var lat, out var lon))
            return $"gps:{Math.Round(lat * 2) / 2:F1},{Math.Round(lon * 2) / 2:F1}";

        // Explicit NER state leads must map to state:* before generic phrase extraction
        // ("State/region mention: montana" would otherwise become place:region mention).
        if (item.Type == EvidenceType.PlaceNameMention &&
            item.Summary.StartsWith("State/region", StringComparison.OrdinalIgnoreCase) &&
            TryStateKey(item.RawContent) is { } stateLead)
            return stateLead;

        var text = $"{item.RawContent} {item.Summary}";
        if (item.Attributes?.TryGetValue("place", out var place) == true && !string.IsNullOrWhiteSpace(place))
            text = place + " " + text;

        var placeToken = ExtractPlaceToken(text);
        if (!string.IsNullOrWhiteSpace(placeToken) && !IsStateOnlyToken(placeToken) && !IsNoisePlaceToken(placeToken))
            return $"place:{placeToken}";

        var stateKey = TryStateKey(text) ?? TryStateKey(item.RawContent) ?? TryStateKey(item.Summary);
        if (stateKey is not null)
            return stateKey;

        return null;
    }

    public static GeoCoordinate? TryGetCoordinate(EvidenceItem item)
    {
        if (TryGps(item, out var lat, out var lon))
            return new GeoCoordinate(lat, lon);
        return null;
    }

    public static EvidenceModality ModalityOf(EvidenceItem item) =>
        item.Type switch
        {
            EvidenceType.GpsEmbed => EvidenceModality.Gps,
            EvidenceType.OcrText or EvidenceType.LicensePlate => EvidenceModality.Ocr,
            EvidenceType.AudioTranscript or EvidenceType.PlaceNameMention or EvidenceType.LanguageDialect
                => EvidenceModality.AsrNer,
            EvidenceType.MineralNameMention => EvidenceModality.Mineral,
            EvidenceType.ShadowMeasurement or EvidenceType.SolarLocus => EvidenceModality.Solar,
            EvidenceType.Terrain or EvidenceType.Vegetation or EvidenceType.SoilRock
                or EvidenceType.MiningCue or EvidenceType.Architecture or EvidenceType.Landmark
                or EvidenceType.Cultural or EvidenceType.Keyframe
                => EvidenceModality.Vision,
            _ => EvidenceModality.Other
        };

    public static double SpecificityScore(EvidenceItem item)
    {
        if (item.Type == EvidenceType.GpsEmbed)
            return 1.0;

        var text = $"{item.RawContent} {item.Summary}";
        if (LooksLikeAddress(text))
            return 0.95;

        if (LandmarkCues.Any(c => text.Contains(c, StringComparison.OrdinalIgnoreCase))
            && item.Type is EvidenceType.PlaceNameMention or EvidenceType.OcrText or EvidenceType.Landmark)
            return 0.85;

        if (item.Type == EvidenceType.PlaceNameMention)
        {
            if (TryStateKey(item.RawContent) is not null && ExtractPlaceToken(text) is null)
                return 0.35;
            if (LandmarkCues.Any(c => text.Contains(c, StringComparison.OrdinalIgnoreCase)))
                return 0.88;
            return 0.65;
        }

        if (item.Type == EvidenceType.OcrText &&
            (text.Contains("US-", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("Highway", StringComparison.OrdinalIgnoreCase)))
            return 0.55;

        if (item.Type == EvidenceType.MineralNameMention)
            return 0.40;

        if (item.Type is EvidenceType.ShadowMeasurement or EvidenceType.SolarLocus)
            return 0.50;

        if (item.Type is EvidenceType.Terrain or EvidenceType.Vegetation or EvidenceType.SoilRock)
            return 0.18;

        return 0.30;
    }

    private static bool TryGps(EvidenceItem item, out double lat, out double lon)
    {
        lat = lon = 0;
        if (item.Attributes is null) return false;
        return item.Attributes.TryGetValue("lat", out var latS)
               && item.Attributes.TryGetValue("lon", out var lonS)
               && double.TryParse(latS, NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
               && double.TryParse(lonS, NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
    }

    private static string? ExtractPlaceToken(string text)
    {
        // Prefer multi-word proper-ish tokens ending with landmark cues.
        var words = Regex.Matches(text, @"[A-Za-z][A-Za-z\-']+(?:\s+[A-Za-z][A-Za-z\-']+){0,3}");
        string? bestLandmark = null;
        string? bestGeneric = null;
        foreach (Match m in words)
        {
            var phrase = m.Value.Trim();
            if (phrase.Length < 4) continue;

            // Truncate at landmark cue so "Maury Mountain trail" → "maury mountain".
            var landmarkTrimmed = TruncateAtLandmark(phrase);
            if (landmarkTrimmed is not null)
            {
                if (bestLandmark is null || landmarkTrimmed.Length < bestLandmark.Length)
                    bestLandmark = landmarkTrimmed;
                continue;
            }

            if (phrase.Split(' ').Length >= 2 && bestGeneric is null)
                bestGeneric = Normalize(phrase);
        }

        return bestLandmark ?? bestGeneric;
    }

    private static string? TruncateAtLandmark(string phrase)
    {
        var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!LandmarkCues.Contains(parts[i]))
                continue;
            // Include up through the landmark word; drop trailing "trail"/"sign"/etc.
            var take = parts.Take(i + 1).ToArray();
            if (take.Length == 0) return null;
            return Normalize(string.Join(' ', take));
        }

        return null;
    }

    private static string Normalize(string s) =>
        Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");

    private static bool LooksLikeAddress(string text) =>
        Regex.IsMatch(text, @"\d{1,5}\s+[A-Za-z].+\b(St|Street|Rd|Road|Ave|Avenue|Blvd|Hwy)\b",
            RegexOptions.IgnoreCase);

    private static bool IsStateOnlyToken(string token)
    {
        var n = Normalize(token);
        return StateNames.ContainsKey(n) || (n.Length == 2 && StateCode.IsMatch(n));
    }

    private static readonly HashSet<string> NoisePlaceTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "metadata", "parse", "limited", "file format", "metadata parse", "metadata parse limited",
        "could not", "determined", "sidecar", "transcript", "no transcript", "working copy",
        "source title", "source title desc"
    };

    private static bool IsNoisePlaceToken(string token)
    {
        var n = Normalize(token);
        if (NoisePlaceTokens.Contains(n))
            return true;
        return n.Contains("metadata", StringComparison.OrdinalIgnoreCase)
               || n.Contains("parse limited", StringComparison.OrdinalIgnoreCase)
               || n.Contains("file format", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryStateKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var code = StateCode.Match(text);
        if (code.Success)
            return $"state:{code.Value.ToUpperInvariant()}";

        // Prefer longer multi-word state names first ("new mexico" before "mexico").
        foreach (var (name, postal) in StateNames.OrderByDescending(kv => kv.Key.Length))
        {
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase))
                return $"state:{postal}";
        }

        return null;
    }
}

public enum EvidenceModality
{
    Gps,
    Ocr,
    AsrNer,
    Mineral,
    Solar,
    Vision,
    Other
}
