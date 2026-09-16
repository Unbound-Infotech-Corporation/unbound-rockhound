namespace GeoMineralTrace.Core.Geology;

/// <summary>Transparent uncertainty band for a prospect guess. Never “certain”.</summary>
public enum ProspectGuessBand
{
    Speculative = 0,
    Plausible = 1,
    Stronger = 2
}

public sealed record NearbyMineralOccurrence(
    string LocalityName,
    string? SourceDataset,
    IReadOnlyList<string> Minerals,
    double DistanceKm,
    bool IsCurated);

public sealed record ProspectGuessHint(
    string Mineral,
    ProspectGuessBand Band,
    string Reason,
    IReadOnlyList<string> Citations);

public sealed record ProspectGuessResult(
    GeologicMapUnit? Unit,
    IReadOnlyList<ProspectGuessHint> Hints,
    IReadOnlyList<string> NearbySummaries,
    string Disclaimer,
    bool UsedOnlineGeology,
    string? StatusNote)
{
    public static string StandardDisclaimer { get; } =
        "Research aid only — probabilistic guesses, not a collecting permit or land-status opinion. " +
        "The Cooperative National Geologic Map is a national synthesis (not outcrop-scale truth). " +
        "Verify BLM / USFS / state rules, mining claims, and private property before any visit. " +
        "Never collect without a legal right to do so.";
}

/// <summary>Keyword rule mapping GeMS GeoMaterial / unit text → candidate minerals.</summary>
public sealed record GeoMaterialAssociationRule(
    string Id,
    IReadOnlyList<string> MatchKeywords,
    IReadOnlyList<string> Minerals,
    ProspectGuessBand GeologyOnlyBand,
    string Reason);
