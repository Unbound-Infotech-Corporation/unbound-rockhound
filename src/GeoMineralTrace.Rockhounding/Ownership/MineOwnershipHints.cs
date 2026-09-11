using GeoMineralTrace.Core.Rockhounding;

namespace GeoMineralTrace.Rockhounding.Ownership;

/// <summary>
/// Heuristic land-tenure hints from names / notes when authoritative parcel data is absent.
/// Never invents Public/Private for generic USGS occurrence rows without a signal.
/// </summary>
public static class MineOwnershipHints
{
    public static LandType InferFromText(string? name, string? accessNotes, string? enrichment)
    {
        var s = $"{name} {accessNotes} {enrichment}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(s))
            return LandType.Unknown;

        if (ContainsAny(s, "patented", "fee mining", "fee dig", "pay-to-dig", "pay to dig",
                "private property", "private mine", "private land", "privately owned",
                "museum mine", "tourist mine", "commercial dig"))
            return LandType.Private;

        if (ContainsAny(s, "national forest", "usfs", "forest service", "nfs "))
            return LandType.PublicUsfs;

        if (ContainsAny(s, " blm", "bureau of land", "public domain", "public lands"))
            return LandType.PublicBlm;

        if (ContainsAny(s, "state park", "state recreational", "crater of diamonds"))
            return LandType.StatePark;

        if (ContainsAny(s, "state land", "state trust", "wildlife management"))
            return LandType.StateLand;

        if (ContainsAny(s, "mining claim", "unpatented claim", "claim block"))
            return LandType.Claim;

        return LandType.Unknown;
    }

    public static string DisplayLabel(LandType land) => land switch
    {
        LandType.PublicBlm => "Public (BLM)",
        LandType.PublicUsfs => "Public (USFS)",
        LandType.StatePark => "Public (state park)",
        LandType.StateLand => "Public (state land)",
        LandType.Private => "Private",
        LandType.Claim => "Federal claim vicinity",
        LandType.Mixed => "Mixed / check parcels",
        LandType.Other => "Other",
        _ => "Ownership unknown"
    };

    public static bool IsPublicSurface(LandType land) =>
        land is LandType.PublicBlm or LandType.PublicUsfs or LandType.StatePark or LandType.StateLand;

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
