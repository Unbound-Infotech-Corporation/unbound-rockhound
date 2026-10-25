using System.Net;
using FluentAssertions;
using Unbound.Marketplace;
using Unbound.Marketplace.Catalog;

namespace Unbound.Marketplace.Tests;

public class CatalogParserTests
{
    private const string Base44List = """
        [
          {
            "name": "Sample Kit",
            "id": "sample-1",
            "price": 1,
            "status": "active",
            "is_sample": true,
            "tagline": "Ignore me"
          },
          {
            "featured": true,
            "image_url": null,
            "description": "A private archive.",
            "price": 79.99,
            "name": "Heirloom — Voice Archive & Digital Twin",
            "tagline": "Build the version of yourself that gets to stay.",
            "status": "coming_soon",
            "sort_order": 1.0,
            "id": "6a988e26f11c4642bf4888e8"
          },
          {
            "image_url": "https://media.base44.com/images/public/69d5f18a091f6012136d4f5a/c994f45cd_logo.png",
            "description": "Long description that should not replace the tagline.",
            "price": 39.99,
            "name": "Unbound Rockhound",
            "tagline": "Special launch price $39.99 — forensic video geolocation & rockhounding research for Windows.",
            "status": "active",
            "sort_order": 10,
            "id": "6a9949122f0c8cf48076190a"
          }
        ]
        """;

    [Fact]
    public void Parse_ReadsBase44Array_AndSkipsSamples()
    {
        var options = new MarketplaceOptions
        {
            LicenseProductIdByCatalogId = new Dictionary<string, string>
            {
                ["6a9949122f0c8cf48076190a"] = "unbound-rockhound"
            }
        };

        var products = MarketplaceCatalogParser.Parse(Base44List, options);

        products.Should().HaveCount(2);
        products[0].Name.Should().Be("Heirloom — Voice Archive & Digital Twin");
        products[0].Price.Should().Be(79.99m);
        products[0].PriceLabel.Should().Be("$79.99");
        products[0].Status.Should().Be("coming_soon");
        products[0].ShortDescription.Should().Be("Build the version of yourself that gets to stay.");
        products[0].Monogram.Should().Be("HV");

        var rockhound = products[1];
        rockhound.Name.Should().Be("Unbound Rockhound");
        rockhound.Price.Should().Be(39.99m);
        rockhound.PriceLabel.Should().Be("$39.99");
        rockhound.LicenseProductId.Should().Be("unbound-rockhound");
        rockhound.IconUrl.Should().StartWith("https://media.base44.com/");
        rockhound.StoreUrl.Should().Be("https://unboundinfotech.com/store/6a9949122f0c8cf48076190a");
        rockhound.ShortDescription.Should().Contain("forensic video geolocation");
    }

    [Fact]
    public void Parse_AcceptsWrappedProducts_AndStringPrice()
    {
        const string json = """
            { "products": [ { "id": "os", "name": "UnboundOS", "price": "$12.50", "licenseProductId": "unbound-os", "description": "Line one.\nLine two stays out of the short text when a tagline is missing and this sentence keeps going so the parser has to collapse whitespace." } ] }
            """;

        var products = MarketplaceCatalogParser.Parse(json);

        products.Should().ContainSingle();
        products[0].Price.Should().Be(12.50m);
        products[0].LicenseProductId.Should().Be("unbound-os");
        products[0].ShortDescription.Should().StartWith("Line one. Line two");
        products[0].ShortDescription.Should().NotContain("\n");
    }

    [Fact]
    public void Parse_RejectsObjectsWithoutAnArray()
    {
        var act = () => MarketplaceCatalogParser.Parse("""{ "name": "nope" }""");
        act.Should().Throw<MarketplaceCatalogException>();
    }

    [Fact]
    public void Offer_ShowsOwnedBuyOrComingSoon()
    {
        var owned = new MarketplaceProduct
        {
            Id = "1",
            Name = "Unbound Rockhound",
            Status = "active",
            LicenseProductId = "unbound-rockhound"
        };
        var coming = owned with { Status = "coming_soon", Id = "2", Name = "Heirloom" };

        MarketplaceOffer.Resolve(owned, ["Unbound-Rockhound"]).Should().Be(MarketplaceOfferState.Owned);
        MarketplaceOffer.Resolve(owned, Array.Empty<string>()).Should().Be(MarketplaceOfferState.Buy);
        MarketplaceOffer.Resolve(coming, ["unbound-rockhound"]).Should().Be(MarketplaceOfferState.ComingSoon);
        MarketplaceOffer.Resolve(owned with { Status = "archived" }, ["unbound-rockhound"]).Should().Be(MarketplaceOfferState.Unavailable);
        MarketplaceOffer.ActionLabel(MarketplaceOfferState.Owned).Should().Be("Owned");
        MarketplaceOffer.ActionLabel(MarketplaceOfferState.Buy).Should().Be("Buy");
        MarketplaceOffer.ActionLabel(MarketplaceOfferState.GetIt).Should().Be("Get it");
    }

    [Fact]
    public void Offer_GetItWhenThisInstallCannotActivate_AndHidesUnlistedStatuses()
    {
        var rockhound = new MarketplaceOptions
        {
            HostLicenseProductId = LicenseProductIds.Rockhound,
            HideComingSoon = true,
            HideInactive = true,
            ActivatableLicenseProductIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                LicenseProductIds.Rockhound
            }
        };

        var owned = new MarketplaceProduct
        {
            Id = "6a9949122f0c8cf48076190a",
            Name = "Unbound Rockhound",
            Status = "active",
            LicenseProductId = LicenseProductIds.Rockhound,
            StoreUrl = "https://unboundinfotech.com/store/6a9949122f0c8cf48076190a"
        };
        var clore = new MarketplaceProduct
        {
            Id = "6a988e26f11c4642bf4888e9",
            Name = "Clore.ai Fleet Optimizer",
            Status = "active",
            StoreUrl = "https://unboundinfotech.com/store/6a988e26f11c4642bf4888e9"
        };
        var heirloom = owned with
        {
            Id = "6a988e26f11c4642bf4888e8",
            Name = "Heirloom — Voice Archive & Digital Twin",
            Status = "coming_soon",
            LicenseProductId = LicenseProductIds.HeirloomUnbound
        };
        var otherApp = owned with
        {
            Id = "heirloom-live",
            Name = "Heirloom Unbound",
            LicenseProductId = LicenseProductIds.HeirloomUnbound,
            DownloadUrl = "https://unboundinfotech.com/downloads/heirloom.zip",
            StoreUrl = "https://unboundinfotech.com/store/heirloom"
        };

        MarketplaceOffer.IsListed(heirloom, rockhound).Should().BeFalse();
        MarketplaceOffer.IsListed(owned with { Status = "archived" }, rockhound).Should().BeFalse();
        MarketplaceOffer.IsListed(clore, rockhound).Should().BeTrue();
        MarketplaceOffer.Resolve(clore, null, rockhound).Should().Be(MarketplaceOfferState.GetIt);
        MarketplaceOffer.Resolve(owned, null, rockhound).Should().Be(MarketplaceOfferState.Buy);
        MarketplaceOffer.Resolve(otherApp, null, rockhound).Should().Be(MarketplaceOfferState.GetIt);
        MarketplaceOffer.ExternalLink(clore)!.AbsoluteUri.Should().Be(clore.StoreUrl);
        MarketplaceOffer.ExternalLink(otherApp)!.AbsoluteUri.Should().Be(otherApp.DownloadUrl);
        MarketplaceOffer.CanActivateHere(clore, rockhound).Should().BeFalse();

        var mappedClore = clore with { LicenseProductId = LicenseProductIds.Rockhound };
        MarketplaceOffer.Resolve(mappedClore, null, rockhound).Should().Be(MarketplaceOfferState.Buy);

        var showDrafts = new MarketplaceOptions
        {
            HostLicenseProductId = LicenseProductIds.HeirloomUnbound,
            HideComingSoon = false,
            HideInactive = false
        };
        MarketplaceOffer.IsListed(heirloom, showDrafts).Should().BeTrue();
        MarketplaceOffer.Resolve(heirloom, null, showDrafts).Should().Be(MarketplaceOfferState.ComingSoon);
        MarketplaceOffer.Resolve(otherApp, null, showDrafts).Should().Be(MarketplaceOfferState.Buy);
    }

    [Fact]
    public void FallbackListing_RockhoundDoesNotBuyCloreOrShowComingSoon()
    {
        var options = new MarketplaceOptions
        {
            HostLicenseProductId = LicenseProductIds.Rockhound,
            LicenseProductIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Unbound Rockhound"] = LicenseProductIds.Rockhound
            }
        };

        var visible = MarketplaceCatalogParser.Parse(MarketplaceCatalogClient.ReadFallbackJson(), options)
            .Where(product => MarketplaceOffer.IsListed(product, options))
            .ToList();

        visible.Select(product => product.Name).Should().BeEquivalentTo(
            "Clore.ai Fleet Optimizer",
            "Unbound Rockhound");
        MarketplaceOffer.Resolve(visible.Single(product => product.Name.StartsWith("Clore", StringComparison.Ordinal)), null, options)
            .Should().Be(MarketplaceOfferState.GetIt);
        MarketplaceOffer.Resolve(visible.Single(product => product.Name == "Unbound Rockhound"), null, options)
            .Should().Be(MarketplaceOfferState.Buy);
    }

    [Fact]
    public void LicenseProductIds_MatchTheSupabaseAllowlist()
    {
        LicenseProductIds.Allowlist.Should().BeEquivalentTo(
            "unbound-rockhound",
            "heirloom-unbound",
            "viralforge",
            "unbound-astrology",
            "unboundos");
    }

    [Fact]
    public void FallbackCatalog_IncludesRockhoundAtLaunchPrice()
    {
        var products = MarketplaceCatalogParser.Parse(MarketplaceCatalogClient.ReadFallbackJson());
        var rockhound = products.Should().ContainSingle(p => p.Name == "Unbound Rockhound").Subject;
        rockhound.Price.Should().Be(39.99m);
        rockhound.LicenseProductId.Should().Be("unbound-rockhound");
        rockhound.Id.Should().Be("6a9949122f0c8cf48076190a");
        products.Should().NotContain(p => p.Name.Contains("sample", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Client_UsesFallbackWhenTheStoreIsOffline()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = new MarketplaceCatalogClient(new HttpClient(handler), new MarketplaceOptions
        {
            CatalogUrl = "https://unboundinfotech.com/api/apps/69d5f18a091f6012136d4f5a/entities/Product",
            LicenseProductIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Unbound Rockhound"] = "unbound-rockhound"
            }
        });

        var result = await client.LoadAsync();

        result.Source.Should().Be(MarketplaceCatalogSource.Fallback);
        result.Notice.Should().NotBeNullOrWhiteSpace();
        result.Products.Should().Contain(p => p.Name == "Unbound Rockhound" && p.Price == 39.99m);
    }

    [Fact]
    public async Task Client_KeepsRemoteRowsWhenTheStoreResponds()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":"live","name":"Unbound Astrology","price":19,"status":"active","tagline":"Charts from Unbound Infotech."}]""")
        });
        var client = new MarketplaceCatalogClient(new HttpClient(handler), new MarketplaceOptions());

        var result = await client.LoadAsync();

        result.Source.Should().Be(MarketplaceCatalogSource.Remote);
        result.Notice.Should().BeNull();
        result.Products.Should().ContainSingle(p => p.Name == "Unbound Astrology" && p.PriceLabel == "$19.00");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
