namespace GeoMineralTrace.Core.Licensing;

/// <summary>
/// Canonical Stripe / Base44 product id for Unbound Rockhound.
/// Webhooks and Activate must store and query this exact value.
/// </summary>
public static class LicenseProductId
{
    public const string Canonical = "unbound-rockhound";

    /// <summary>
    /// True when metadata is missing (dedicated webhook) or names this product
    /// under common aliases ("Unbound Rockhound", "unbound_rockhound", etc.).
    /// </summary>
    public static bool IsRockhound(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var n = Normalize(raw);
        return n is Canonical or "rockhound" or "unboundrockhound";
    }

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Canonical;

        var chars = raw.Trim().ToLowerInvariant().Select(ch =>
            char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var collapsed = new string(chars);
        while (collapsed.Contains("--", StringComparison.Ordinal))
            collapsed = collapsed.Replace("--", "-", StringComparison.Ordinal);
        return collapsed.Trim('-');
    }
}
