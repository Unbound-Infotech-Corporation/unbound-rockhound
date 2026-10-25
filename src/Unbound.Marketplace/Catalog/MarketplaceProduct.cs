namespace Unbound.Marketplace.Catalog;

public enum MarketplaceOfferState
{
    Buy,
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
    public static MarketplaceOfferState Resolve(MarketplaceProduct product, IEnumerable<string>? ownedLicenseProductIds)
    {
        var status = (product.Status ?? "").Trim().Replace('-', '_').ToLowerInvariant();
        if (status is "coming_soon")
            return MarketplaceOfferState.ComingSoon;
        if (status is not ("active" or "published" or "live"))
            return MarketplaceOfferState.Unavailable;

        if (!string.IsNullOrWhiteSpace(product.LicenseProductId) && ownedLicenseProductIds is not null)
        {
            foreach (var owned in ownedLicenseProductIds)
            {
                if (string.Equals(owned?.Trim(), product.LicenseProductId.Trim(), StringComparison.OrdinalIgnoreCase))
                    return MarketplaceOfferState.Owned;
            }
        }

        return MarketplaceOfferState.Buy;
    }

    public static string ActionLabel(MarketplaceOfferState state) => state switch
    {
        MarketplaceOfferState.Owned => "Owned",
        MarketplaceOfferState.ComingSoon => "Coming soon",
        MarketplaceOfferState.Unavailable => "Unavailable",
        _ => "Buy"
    };
}
