namespace Unbound.Marketplace;

/// <summary>
/// Canonical <c>license_product_id</c> values shared by the store, Stripe metadata, and
/// <c>license-activate</c> / <c>license-validate</c>. Keep this list aligned with
/// <c>ALLOWED_PRODUCT_IDS</c> in <c>supabase/functions/_shared/license.ts</c>.
/// </summary>
public static class LicenseProductIds
{
    public const string Rockhound = "unbound-rockhound";
    public const string HeirloomUnbound = "heirloom-unbound";
    public const string ViralForge = "viralforge";
    public const string UnboundAstrology = "unbound-astrology";
    public const string UnboundOs = "unboundos";

    public static readonly IReadOnlySet<string> Allowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Rockhound,
        HeirloomUnbound,
        ViralForge,
        UnboundAstrology,
        UnboundOs
    };
}
