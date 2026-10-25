namespace Unbound.Marketplace;

/// <summary>Host configuration for the shared marketplace. Apps supply their own license id map.</summary>
public sealed class MarketplaceOptions
{
    public string CatalogUrl { get; init; } = MarketplaceDefaults.CatalogUrl;

    public string CheckoutFunctionUrl { get; init; } = MarketplaceDefaults.CheckoutFunctionUrl;

    public string StoreOrigin { get; init; } = MarketplaceDefaults.StoreOrigin;

    /// <summary>License product id this installation can activate, for example unbound-rockhound.</summary>
    public string? HostLicenseProductId { get; init; }

    /// <summary>
    /// License product ids this install may offer as Buy and activate.
    /// When empty, only <see cref="HostLicenseProductId"/> is activatable.
    /// A catalog row with no resolved license product id is never Buy.
    /// </summary>
    public IReadOnlySet<string> ActivatableLicenseProductIds { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hide <c>coming_soon</c> rows. Rockhound leaves this on so Heirloom stays off the panel.</summary>
    public bool HideComingSoon { get; init; } = true;

    /// <summary>Hide rows that are not active, published, or live (and not coming soon, which has its own flag).</summary>
    public bool HideInactive { get; init; } = true;

    /// <summary>Customer-facing name of the host app, used only for captions.</summary>
    public string HostProductDisplayName { get; init; } = "";

    public int MaxPollAttempts { get; init; } = 8;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Base44 entity id → Stripe / license product id, used when the catalog row has no license field.</summary>
    public IReadOnlyDictionary<string, string> LicenseProductIdByCatalogId { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Product display name → license product id.</summary>
    public IReadOnlyDictionary<string, string> LicenseProductIdByName { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
