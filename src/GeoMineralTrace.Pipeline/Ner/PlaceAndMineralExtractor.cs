using System.Text.RegularExpressions;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;

namespace GeoMineralTrace.Pipeline.Ner;

/// <summary>
/// Offline place-name and mineral/gem mention extractor using curated lexicons
/// plus capitalized multi-word heuristics (place-like phrases require geo cues).
/// </summary>
public sealed class PlaceAndMineralExtractor
{
    private static readonly string[] Minerals =
    [
        "obsidian", "garnet", "almandine", "diamond", "turquoise", "opal", "agate", "jasper",
        "amethyst", "quartz", "fluorite", "beryl", "aquamarine", "emerald", "topaz", "tourmaline",
        "peridot", "jade", "malachite", "azurite", "pyrite", "gold", "copper", "silver",
        "geode", "thunder egg", "petrified wood", "sunstone", "labradorite", "moonstone"
    ];

    private static readonly string[] PlaceHints =
    [
        "mountain", "mount ", "creek", "river", "canyon", "valley", "park", "national forest",
        "blm", "forest service", "highway", "interstate", "route", "county",
        "mine", "mines", "quarry", "claim", "prospect", "diggings", "crossroads",
        "ridge", "butte", "mesa", "falls", "lake", "desert", "forest", "wilderness",
        "island", "beach", "cove", "pass", "peak", "summit", "trail", "campground"
    ];

    private static readonly HashSet<string> NonPlaceLeadWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "found", "finding", "searching", "unbelievable", "incredible", "amazing", "rare",
        "best", "watch", "subscribe", "check", "click", "like", "share", "follow",
        "working", "iphone", "gopro", "scuba", "diving", "underwater", "treasure",
        "returned", "owner", "police", "called", "exploded", "secret", "closed"
    };

    // Common US state names for weak place detection
    private static readonly string[] States =
    [
        "alabama","alaska","arizona","arkansas","california","colorado","connecticut","delaware",
        "florida","georgia","hawaii","idaho","illinois","indiana","iowa","kansas","kentucky",
        "louisiana","maine","maryland","massachusetts","michigan","minnesota","mississippi",
        "missouri","montana","nebraska","nevada","hampshire","jersey","mexico","york","carolina",
        "dakota","ohio","oklahoma","oregon","pennsylvania","rhode","tennessee","texas","utah",
        "vermont","virginia","washington","wisconsin","wyoming"
    ];

    public IReadOnlyList<EvidenceItem> ExtractFromText(
        Guid sessionId,
        string sourceMediaPath,
        string text,
        TimeSpan? timestamp = null,
        string? language = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var items = new List<EvidenceItem>();
        var lower = text.ToLowerInvariant();

        foreach (var mineral in Minerals)
        {
            if (!lower.Contains(mineral)) continue;
            items.Add(new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = sessionId,
                Type = EvidenceType.MineralNameMention,
                Summary = $"Mineral/gem mention: {mineral}",
                RawContent = mineral,
                SourceMediaPath = sourceMediaPath,
                MediaTimestamp = timestamp,
                Confidence = Confidence.High,
                DetectedLanguage = language,
                Notes = "Lexicon match — verify context (not always a local find)."
            });
        }

        foreach (var state in States)
        {
            if (!Regex.IsMatch(lower, $@"\b{Regex.Escape(state)}\b")) continue;
            items.Add(new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = sessionId,
                Type = EvidenceType.PlaceNameMention,
                Summary = $"State/region mention: {state}",
                RawContent = state,
                SourceMediaPath = sourceMediaPath,
                MediaTimestamp = timestamp,
                Confidence = Confidence.Medium,
                DetectedLanguage = language,
                Notes = "May refer to a remote topic rather than the filming site."
            });
        }

        // Capitalized place-like phrases — require a geo cue (mine/mountain/creek/…).
        foreach (Match m in Regex.Matches(text, @"\b([A-Z][a-z]+(?:\s+[A-Z][a-z]+)+)\b"))
        {
            var phrase = m.Value;
            if (!IsPlausiblePlacePhrase(phrase))
                continue;

            items.Add(new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = sessionId,
                Type = EvidenceType.PlaceNameMention,
                Summary = $"Place-like phrase: {phrase}",
                RawContent = phrase,
                SourceMediaPath = sourceMediaPath,
                MediaTimestamp = timestamp,
                Confidence = phrase.Contains("river", StringComparison.OrdinalIgnoreCase) ||
                             phrase.Contains("creek", StringComparison.OrdinalIgnoreCase)
                    ? Confidence.High
                    : Confidence.Medium,
                DetectedLanguage = language,
                Notes = "Capitalization + geo-cue heuristic — requires gazetteer confirmation.",
                Attributes = new Dictionary<string, string> { ["place"] = phrase }
            });
        }

        foreach (Match m in Regex.Matches(text, @"\b(?:US|I|SR|Hwy|Highway)[-\s]?\d{1,3}\b", RegexOptions.IgnoreCase))
        {
            items.Add(new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = sessionId,
                Type = EvidenceType.OcrText,
                Summary = $"Road designator: {m.Value}",
                RawContent = m.Value,
                SourceMediaPath = sourceMediaPath,
                MediaTimestamp = timestamp,
                Confidence = Confidence.High,
                DetectedLanguage = language,
                Notes = "Highway/route mentions strongly constrain region when authentic to the scene."
            });
        }

        return Deduplicate(items);
    }

    public static bool IsPlausiblePlacePhrase(string phrase)
    {
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
            return false;

        // Reject clickbait / marketing Title Case that isn't a place.
        if (words.Count(w => NonPlaceLeadWords.Contains(w)) >= 1 &&
            !PlaceHints.Any(h => phrase.Contains(h, StringComparison.OrdinalIgnoreCase)))
            return false;

        var pl = phrase.ToLowerInvariant();
        return PlaceHints.Any(h => pl.Contains(h, StringComparison.Ordinal));
    }

    private static List<EvidenceItem> Deduplicate(List<EvidenceItem> items) =>
        items
            .GroupBy(i => (i.Type, (i.RawContent ?? i.Summary).ToLowerInvariant()))
            .Select(g => g.First())
            .ToList();
}
