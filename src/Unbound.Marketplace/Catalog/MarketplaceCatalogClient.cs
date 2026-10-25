namespace Unbound.Marketplace.Catalog;

public enum MarketplaceCatalogSource
{
    Remote,
    Fallback,
    Unavailable
}

public sealed record MarketplaceCatalogResult(
    IReadOnlyList<MarketplaceProduct> Products,
    MarketplaceCatalogSource Source,
    string? Notice);

public interface IMarketplaceCatalogClient
{
    Task<MarketplaceCatalogResult> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class MarketplaceCatalogClient : IMarketplaceCatalogClient
{
    public const string OfflineNotice =
        "You're offline or the Unbound Infotech store could not be reached. Showing the bundled Unbound Marketplace catalog.";

    public const string UnreadableNotice =
        "The store catalog could not be read. Showing the bundled Unbound Marketplace catalog.";

    public const string EmptyNotice =
        "The store catalog was empty. Showing the bundled Unbound Marketplace catalog.";

    private readonly HttpClient _http;
    private readonly MarketplaceOptions _options;

    public MarketplaceCatalogClient(HttpClient http, MarketplaceOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<MarketplaceCatalogResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.CatalogUrl))
            return LoadFallback(UnreadableNotice);

        try
        {
            using var response = await _http.GetAsync(_options.CatalogUrl, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return LoadFallback(OfflineNotice);

            IReadOnlyList<MarketplaceProduct> products;
            try
            {
                products = MarketplaceCatalogParser.Parse(payload, _options);
            }
            catch (MarketplaceCatalogException)
            {
                return LoadFallback(UnreadableNotice);
            }

            if (products.Count == 0)
                return LoadFallback(EmptyNotice);

            return new MarketplaceCatalogResult(products, MarketplaceCatalogSource.Remote, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            return LoadFallback(OfflineNotice);
        }
    }

    public static string ReadFallbackJson()
    {
        var assembly = typeof(MarketplaceCatalogClient).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(resource => resource.EndsWith("fallback-catalog.json", StringComparison.Ordinal));
        if (name is null)
            throw new MarketplaceCatalogException("The bundled Unbound Marketplace catalog is missing.");

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new MarketplaceCatalogException("The bundled Unbound Marketplace catalog could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private MarketplaceCatalogResult LoadFallback(string notice)
    {
        try
        {
            var products = MarketplaceCatalogParser.Parse(ReadFallbackJson(), _options);
            if (products.Count == 0)
            {
                return new MarketplaceCatalogResult(
                    Array.Empty<MarketplaceProduct>(),
                    MarketplaceCatalogSource.Unavailable,
                    "The Unbound Marketplace catalog is unavailable.");
            }

            return new MarketplaceCatalogResult(products, MarketplaceCatalogSource.Fallback, notice);
        }
        catch (MarketplaceCatalogException)
        {
            return new MarketplaceCatalogResult(
                Array.Empty<MarketplaceProduct>(),
                MarketplaceCatalogSource.Unavailable,
                "The Unbound Marketplace catalog is unavailable.");
        }
    }
}
