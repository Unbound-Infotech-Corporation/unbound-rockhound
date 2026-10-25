using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Licensing;
using GeoMineralTrace.Infrastructure.Licensing;
using GeoMineralTrace.Infrastructure.Social;
using Microsoft.Extensions.DependencyInjection;
using Unbound.Marketplace;
using Unbound.Marketplace.Activation;
using Unbound.Marketplace.Catalog;
using Unbound.Marketplace.Checkout;

namespace GeoMineralTrace_App.Marketplace;

/// <summary>
/// Rockhound composition for the shared Unbound Marketplace.
/// Activation always goes through <see cref="ILicenseService"/> so the existing Activate page stays the source of truth.
/// </summary>
public static class RockhoundMarketplace
{
    /// <summary>Base44 Product id for the public Unbound Rockhound listing.</summary>
    public const string StoreListingId = "6a9949122f0c8cf48076190a";

    public static MarketplaceOptions CreateOptions()
    {
        var catalog = Environment.GetEnvironmentVariable("UNBOUND_MARKETPLACE_CATALOG_URL");
        var checkout = Environment.GetEnvironmentVariable("UNBOUND_MARKETPLACE_CHECKOUT_URL");
        return new MarketplaceOptions
        {
            CatalogUrl = string.IsNullOrWhiteSpace(catalog) ? MarketplaceDefaults.CatalogUrl : catalog.Trim(),
            CheckoutFunctionUrl = string.IsNullOrWhiteSpace(checkout) ? MarketplaceDefaults.CheckoutFunctionUrl : checkout.Trim(),
            HostLicenseProductId = LicenseProductId.Canonical,
            HostProductDisplayName = AppBranding.ProductName,
            HideComingSoon = true,
            HideInactive = true,
            ActivatableLicenseProductIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                LicenseProductId.Canonical
            },
            LicenseProductIdByCatalogId = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [StoreListingId] = LicenseProductId.Canonical
            },
            LicenseProductIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AppBranding.ProductName] = LicenseProductId.Canonical
            }
        };
    }

    public static IServiceCollection AddRockhoundMarketplace(this IServiceCollection services)
    {
        services.AddSingleton(CreateOptions());
        services.AddSingleton<IMarketplaceSupabaseSettings, RockhoundSupabaseSettings>();
        services.AddSingleton<IMarketplaceOwnershipSource, RockhoundOwnershipSource>();
        services.AddSingleton<IMarketplaceKeyActivator, RockhoundKeyActivator>();
        services.AddSingleton<IExternalBrowser, ShellExternalBrowser>();

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", AppBranding.UserAgentPrefix + " UnboundMarketplace/1.0");

        services.AddSingleton<IMarketplaceCatalogClient>(sp =>
            new MarketplaceCatalogClient(http, sp.GetRequiredService<MarketplaceOptions>()));
        services.AddSingleton<ICheckoutLauncher>(sp =>
            new CheckoutLauncher(
                http,
                sp.GetRequiredService<MarketplaceOptions>(),
                sp.GetRequiredService<IExternalBrowser>()));
        services.AddSingleton<IMarketplaceSessionClient>(sp =>
            new SupabaseLicenseSessionClient(http, sp.GetRequiredService<IMarketplaceSupabaseSettings>()));
        services.AddSingleton(sp => new MarketplaceHost
        {
            Options = sp.GetRequiredService<MarketplaceOptions>(),
            Catalog = sp.GetRequiredService<IMarketplaceCatalogClient>(),
            Checkout = sp.GetRequiredService<ICheckoutLauncher>(),
            Sessions = sp.GetRequiredService<IMarketplaceSessionClient>(),
            KeyActivator = sp.GetRequiredService<IMarketplaceKeyActivator>(),
            Ownership = sp.GetRequiredService<IMarketplaceOwnershipSource>(),
            Browser = sp.GetRequiredService<IExternalBrowser>()
        });
        return services;
    }
}

public sealed class RockhoundSupabaseSettings : IMarketplaceSupabaseSettings
{
    private readonly SupabaseConfigStore _store;

    public RockhoundSupabaseSettings(SupabaseConfigStore store) => _store = store;

    public bool IsConfigured => _store.Load().IsConfigured;
    public string Url => _store.Load().Url;
    public string AnonKey => _store.Load().AnonKey;
}

public sealed class RockhoundOwnershipSource : IMarketplaceOwnershipSource
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly ILicenseService _license;

    public RockhoundOwnershipSource(ILicenseService license)
    {
        _license = license;
        _license.LicenseChanged += (_, _) => OwnershipChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OwnershipChanged;

    public IReadOnlySet<string> GetOwnedLicenseProductIds()
    {
        var current = _license.Current;
        if (current is null
            || string.IsNullOrWhiteSpace(current.LicenseKey)
            || !string.Equals(current.Status, LicenseStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        var id = string.IsNullOrWhiteSpace(current.ProductId)
            ? LicenseProductId.Canonical
            : current.ProductId;
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
    }
}

/// <summary>Forwards marketplace activation to the existing license service. Does not call the Edge function itself.</summary>
public sealed class RockhoundKeyActivator : IMarketplaceKeyActivator
{
    private readonly ILicenseService _license;

    public RockhoundKeyActivator(ILicenseService license) => _license = license;

    public async Task<MarketplaceKeyActivation> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _license.ActivateAsync(licenseKey, cancellationToken).ConfigureAwait(false);
            return new MarketplaceKeyActivation(result.Ok, result.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new MarketplaceKeyActivation(false, ex.Message);
        }
    }
}
