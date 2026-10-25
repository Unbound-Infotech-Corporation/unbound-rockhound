namespace Unbound.Marketplace;

/// <summary>
/// Public Unbound Infotech store endpoints. No secrets — the Base44 app id is the
/// published storefront id already embedded in unboundinfotech.com.
/// </summary>
public static class MarketplaceDefaults
{
    public const string BrandName = "Unbound Marketplace";
    public const string PublisherName = "Unbound Infotech";
    public const string StoreOrigin = "https://unboundinfotech.com";
    public const string Base44AppId = "69d5f18a091f6012136d4f5a";

    public static string CatalogUrl =>
        $"{StoreOrigin}/api/apps/{Base44AppId}/entities/Product?sort=sort_order&limit=50";

    public static string CheckoutFunctionUrl =>
        $"{StoreOrigin}/api/apps/{Base44AppId}/functions/createCheckoutSession";

    public static string StorePage(string productId) =>
        $"{StoreOrigin}/store/{productId}";
}
