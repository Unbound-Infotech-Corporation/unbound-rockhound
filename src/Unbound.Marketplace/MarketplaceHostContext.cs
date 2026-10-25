using Unbound.Marketplace.Checkout;

namespace Unbound.Marketplace;

/// <summary>
/// Composition root set by the host app before the marketplace page is shown.
/// The shared UI reads this so the library stays free of any one app's DI container.
/// </summary>
public static class MarketplaceHostContext
{
    public static MarketplaceHost? Current { get; set; }
}

/// <summary>Services the marketplace page needs. The host constructs this.</summary>
public sealed class MarketplaceHost
{
    public required MarketplaceOptions Options { get; init; }
    public required IMarketplaceCatalogClient Catalog { get; init; }
    public required ICheckoutLauncher Checkout { get; init; }
    public required IMarketplaceSessionClient Sessions { get; init; }
    public required IMarketplaceKeyActivator KeyActivator { get; init; }
    public required IMarketplaceOwnershipSource Ownership { get; init; }

    /// <summary>Opens a store page or download for products this install does not activate.</summary>
    public IExternalBrowser? Browser { get; init; }

    public MarketplacePurchaseFlow CreateFlow() => new(
        Options,
        Checkout,
        Sessions,
        KeyActivator);
}
