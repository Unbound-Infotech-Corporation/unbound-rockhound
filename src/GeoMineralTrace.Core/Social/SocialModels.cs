namespace GeoMineralTrace.Core.Social;

/// <summary>Cloud profile row (Supabase public.profiles).</summary>
public sealed class SocialProfile
{
    public Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Handle { get; init; }
    public string Bio { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public string? HomeRegion { get; init; }
    /// <summary>When false, clients hide From Reddit discovery UI.</summary>
    public bool ShowRedditDiscovery { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Marketplace shops (loaded when requested).</summary>
    public IReadOnlyList<ProfileShop> Shops { get; init; } = Array.Empty<ProfileShop>();

    public SocialProfile WithShops(IReadOnlyList<ProfileShop> shops) => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Handle = Handle,
        Bio = Bio,
        AvatarUrl = AvatarUrl,
        HomeRegion = HomeRegion,
        ShowRedditDiscovery = ShowRedditDiscovery,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        Shops = shops
    };
}

/// <summary>Known rockhounding-related marketplaces a seller can link.</summary>
public static class MarketplaceKinds
{
    public const string Etsy = "etsy";
    public const string Ebay = "ebay";
    public const string Shopify = "shopify";
    public const string Whatnot = "whatnot";
    public const string Mercari = "mercari";
    public const string FacebookMarketplace = "facebook_marketplace";
    public const string AmazonHandmade = "amazon_handmade";
    public const string Poshmark = "poshmark";
    public const string Website = "website";
    public const string InstagramShop = "instagram_shop";
    public const string Other = "other";

    public static IReadOnlyList<(string Id, string Label)> All { get; } =
    [
        (Etsy, "Etsy"),
        (Ebay, "eBay"),
        (Shopify, "Shopify / own storefront"),
        (Whatnot, "Whatnot"),
        (Mercari, "Mercari"),
        (FacebookMarketplace, "Facebook Marketplace"),
        (AmazonHandmade, "Amazon Handmade"),
        (Poshmark, "Poshmark"),
        (InstagramShop, "Instagram Shop"),
        (Website, "Personal website"),
        (Other, "Other marketplace")
    ];

    public static string DisplayName(string id) =>
        All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)).Label
        ?? id;
}

public sealed class ProfileShop
{
    public Guid Id { get; init; }
    public Guid ProfileId { get; init; }
    public required string Marketplace { get; init; }
    public string ShopName { get; init; } = "";
    public required string ShopUrl { get; init; }
    public string Description { get; init; } = "";
    public int SortOrder { get; init; }
    public IReadOnlyList<ProfileShopMedia> Media { get; init; } = Array.Empty<ProfileShopMedia>();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public string MarketplaceLabel => MarketplaceKinds.DisplayName(Marketplace);
}

public sealed class ProfileShopMedia
{
    public Guid Id { get; init; }
    public Guid ShopId { get; init; }
    public required string MediaType { get; init; } // image | video
    public required string MediaUrl { get; init; }
    public string Caption { get; init; } = "";
    public int SortOrder { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public bool IsVideo => string.Equals(MediaType, "video", StringComparison.OrdinalIgnoreCase);
}

public sealed class ProfileShopDraft
{
    public string Marketplace { get; init; } = MarketplaceKinds.Etsy;
    public string ShopName { get; init; } = "";
    public required string ShopUrl { get; init; }
    public string Description { get; init; } = "";
    public int SortOrder { get; init; }
}

public sealed class SocialAuthSession
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }

    public bool IsExpired(DateTimeOffset? now = null) =>
        ExpiresAtUtc <= (now ?? DateTimeOffset.UtcNow).AddMinutes(1);
}

public sealed class SupabaseProjectConfig
{
    public required string Url { get; init; }
    public required string AnonKey { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url)
        && !string.IsNullOrWhiteSpace(AnonKey)
        && !Url.Contains("YOUR_PROJECT", StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(Url, UriKind.Absolute, out _);
}

/// <summary>Cloud forum category (Supabase public.forum_categories).</summary>
public sealed class ForumCategory
{
    public Guid Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string IconKey { get; init; } = "forum";
    public int SortOrder { get; init; }
    public bool IsRegionScoped { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Cloud forum thread (Supabase public.forum_threads).</summary>
public sealed class ForumThread
{
    public Guid Id { get; init; }
    public Guid CategoryId { get; init; }
    public Guid AuthorId { get; init; }
    public required string Title { get; init; }
    public string Body { get; init; } = "";
    public string? RegionTag { get; init; }
    public IReadOnlyList<string> ImageUrls { get; init; } = Array.Empty<string>();
    public int ReplyCount { get; init; }
    public int LikeCount { get; init; }
    public bool IsPinned { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Cloud forum reply (Supabase public.forum_posts).</summary>
public sealed class ForumPost
{
    public Guid Id { get; init; }
    public Guid ThreadId { get; init; }
    public Guid AuthorId { get; init; }
    public required string Body { get; init; }
    public IReadOnlyList<string> ImageUrls { get; init; } = Array.Empty<string>();
    public int LikeCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Stable forum category ids shared with Android.</summary>
public static class ForumCategoryIds
{
    public static readonly Guid WorldNews = Guid.Parse("11111111-1111-4111-8111-111111111101");
    public static readonly Guid UsaNews = Guid.Parse("11111111-1111-4111-8111-111111111102");
    public static readonly Guid Local = Guid.Parse("11111111-1111-4111-8111-111111111103");
    public static readonly Guid General = Guid.Parse("11111111-1111-4111-8111-111111111104");
    public static readonly Guid FromReddit = Guid.Parse("11111111-1111-4111-8111-111111111105");

    public const string FromRedditSlug = "from-reddit";
}

/// <summary>Ingested Reddit post (public.reddit_discoveries). Not a claim.</summary>
public sealed class RedditDiscovery
{
    public Guid Id { get; init; }
    public required string RedditThingId { get; init; }
    public required string Subreddit { get; init; }
    public required string Title { get; init; }
    public string Snippet { get; init; } = "";
    public required string Url { get; init; }
    public int Score { get; init; }
    public DateTimeOffset? PostedAt { get; init; }
    public DateTimeOffset FetchedAt { get; init; }
}

/// <summary>Curated public Facebook group link (public.facebook_directory).</summary>
public sealed class FacebookDirectoryEntry
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Url { get; init; }
    public string? RegionTag { get; init; }
    public string Notes { get; init; } = "";
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Community-sourced place mention (public.community_location_mentions).
/// Unverified — never merge into claims / rumoured local stores.
/// </summary>
public sealed class CommunityLocationMention
{
    public Guid Id { get; init; }
    public Guid? RedditDiscoveryId { get; init; }
    public required string SourceUrl { get; init; }
    public string SourceLabel { get; init; } = "";
    public required string MentionText { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public string? PlaceHint { get; init; }
    public float Confidence { get; init; } = 0.3f;
    public DateTimeOffset CreatedAt { get; init; }
}
