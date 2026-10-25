using Unbound.Marketplace;

namespace Unbound.Marketplace.Catalog;

public enum MarketplaceOfferState
{
    Buy,
    GetIt,
    Owned,
    ComingSoon,
    Unavailable
}

/// <summary>One sellable row normalized from the Unbound Infotech store catalog.</summary>
public sealed record MarketplaceProduct
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string ShortDescription { get; init; } = "";
    public decimal? Price { get; init; }
    public string PriceLabel { get; init; } = "";
    public string? IconUrl { get; init; }
    public string Status { get; init; } = "active";
    public string? LicenseProductId { get; init; }
    public string? StoreUrl { get; init; }
    public string? DownloadUrl { get; init; }
    public bool Featured { get; init; }
    public double SortOrder { get; init; }
    public string Monogram { get; init; } = "?";
}

public static class MarketplaceOffer
{
    public static string NormalizeStatus(string? status) =>
        (status ?? "").Trim().Replace('-', '_').ToLowerInvariant();

    public static bool IsSellableStatus(string? status)
    {
        var normalized = NormalizeStatus(status);
        return normalized is "active" or "published" or "live";
    }

    /// <summary>Whether this host's panel should show the row. Policy lives on <see cref="MarketplaceOptions"/>.</summary>
    public static bool IsListed(MarketplaceProduct product, MarketplaceOptions options)
    {
        var status = NormalizeStatus(product.Status);
        if (options.HideComingSoon && status == "coming_soon")
            return false;
        if (options.HideInactive && status != "coming_soon" && !IsSellableStatus(status))
            return false;
        return true;
    }

    public static MarketplaceOfferState Resolve(
        MarketplaceProduct product,
        IEnumerable<string>? ownedLicenseProductIds,
        MarketplaceOptions? options = null)
    {
        var status = NormalizeStatus(product.Status);
        if (status is "coming_soon")
            return MarketplaceOfferState.ComingSoon;
        if (!IsSellableStatus(status))
            return MarketplaceOfferState.Unavailable;

        if (IsOwned(product, ownedLicenseProductIds))
            return MarketplaceOfferState.Owned;

        if (string.IsNullOrWhiteSpace(product.LicenseProductId))
            return MarketplaceOfferState.GetIt;

        if (options is not null && !CanActivateHere(product, options))
            return MarketplaceOfferState.GetIt;

        return MarketplaceOfferState.Buy;
    }

    /// <summary>
    /// True when the row has a license product id this install is allowed to buy and activate.
    /// Unmapped rows (no <c>license_product_id</c> and no host alias) are never activatable.
    /// </summary>
    public static bool CanActivateHere(MarketplaceProduct product, MarketplaceOptions options)
    {
        var id = product.LicenseProductId?.Trim();
        if (string.IsNullOrEmpty(id))
            return false;

        foreach (var allowed in ActivatableIds(options))
        {
            if (string.Equals(allowed?.Trim(), id, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Download when the catalog has one, otherwise the public store page. Https only.</summary>
    public static Uri? ExternalLink(MarketplaceProduct product)
    {
        if (TryHttps(product.DownloadUrl, out var download))
            return download;
        if (TryHttps(product.StoreUrl, out var store))
            return store;
        return null;
    }

    public static string ActionLabel(MarketplaceOfferState state) => state switch
    {
        MarketplaceOfferState.Owned => "Owned",
        MarketplaceOfferState.GetIt => "Get it",
        MarketplaceOfferState.ComingSoon => "Coming soon",
        MarketplaceOfferState.Unavailable => "Unavailable",
        _ => "Buy"
    };

    public static string Footnote(MarketplaceOfferState state, bool isThisApp) => state switch
    {
        MarketplaceOfferState.GetIt => "Get it opens the store page or download. This install does not activate that license.",
        MarketplaceOfferState.Buy when isThisApp => "Buy opens Stripe. The license activates in this app.",
        MarketplaceOfferState.Buy => "Buy opens Stripe Checkout in your browser.",
        MarketplaceOfferState.Owned when isThisApp => "This install already holds the license.",
        MarketplaceOfferState.Owned => "Licensed.",
        _ => ""
    };

    private static bool IsOwned(MarketplaceProduct product, IEnumerable<string>? ownedLicenseProductIds)
    {
        if (string.IsNullOrWhiteSpace(product.LicenseProductId) || ownedLicenseProductIds is null)
            return false;

        foreach (var owned in ownedLicenseProductIds)
        {
            if (string.Equals(owned?.Trim(), product.LicenseProductId.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> ActivatableIds(MarketplaceOptions options)
    {
        if (options.ActivatableLicenseProductIds.Count > 0)
            return options.ActivatableLicenseProductIds;
        if (!string.IsNullOrWhiteSpace(options.HostLicenseProductId))
            return new[] { options.HostLicenseProductId };
        return Array.Empty<string>();
    }

    private static bool TryHttps(string? raw, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var parsed))
            return false;
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        uri = parsed;
        return true;
    }
}
