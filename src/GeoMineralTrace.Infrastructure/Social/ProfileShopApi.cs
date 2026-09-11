using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoMineralTrace.Core.Social;

namespace GeoMineralTrace.Infrastructure.Social;

/// <summary>REST helpers for profile marketplace shops (keeps SocialAuthServices thinner).</summary>
public static class ProfileShopApi
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<IReadOnlyList<ProfileShop>> ListShopsAsync(
        SupabaseSocialClient client,
        Guid profileId,
        string accessToken,
        CancellationToken ct)
    {
        var path =
            $"/rest/v1/profile_shops?profile_id=eq.{profileId}&select=*,profile_shop_media(*)&order=sort_order.asc";
        using var req = client.CreatePublicRequest(HttpMethod.Get, path, accessToken);
        using var resp = await client.Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"List shops failed ({(int)resp.StatusCode}): {body}");

        var rows = JsonSerializer.Deserialize<List<ShopRow>>(body, Json) ?? [];
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public static async Task<ProfileShop> InsertShopAsync(
        SupabaseSocialClient client,
        Guid profileId,
        ProfileShopDraft draft,
        string accessToken,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["profile_id"] = profileId,
            ["marketplace"] = NormalizeMarketplace(draft.Marketplace),
            ["shop_name"] = (draft.ShopName ?? "").Trim(),
            ["shop_url"] = NormalizeUrl(draft.ShopUrl),
            ["description"] = (draft.Description ?? "").Trim(),
            ["sort_order"] = draft.SortOrder
        };

        using var req = client.CreatePublicRequest(HttpMethod.Post, "/rest/v1/profile_shops", accessToken);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        req.Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");
        using var resp = await client.Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Add shop failed ({(int)resp.StatusCode}): {body}");

        var rows = JsonSerializer.Deserialize<List<ShopRow>>(body, Json) ?? [];
        return rows.FirstOrDefault()?.ToDomain()
            ?? throw new InvalidOperationException("Add shop returned no rows.");
    }

    public static async Task<ProfileShop> UpdateShopAsync(
        SupabaseSocialClient client,
        Guid shopId,
        ProfileShopDraft draft,
        string accessToken,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["marketplace"] = NormalizeMarketplace(draft.Marketplace),
            ["shop_name"] = (draft.ShopName ?? "").Trim(),
            ["shop_url"] = NormalizeUrl(draft.ShopUrl),
            ["description"] = (draft.Description ?? "").Trim(),
            ["sort_order"] = draft.SortOrder
        };

        using var req = client.CreatePublicRequest(
            HttpMethod.Patch,
            $"/rest/v1/profile_shops?id=eq.{shopId}",
            accessToken);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        req.Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");
        using var resp = await client.Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Update shop failed ({(int)resp.StatusCode}): {body}");

        var rows = JsonSerializer.Deserialize<List<ShopRow>>(body, Json) ?? [];
        return rows.FirstOrDefault()?.ToDomain()
            ?? throw new InvalidOperationException("Update shop returned no rows.");
    }

    public static async Task DeleteShopAsync(
        SupabaseSocialClient client,
        Guid shopId,
        string accessToken,
        CancellationToken ct)
    {
        using var req = client.CreatePublicRequest(
            HttpMethod.Delete,
            $"/rest/v1/profile_shops?id=eq.{shopId}",
            accessToken);
        using var resp = await client.Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Delete shop failed ({(int)resp.StatusCode}): {body}");
        }
    }

    public static async Task<ProfileShopMedia> AddMediaAsync(
        SupabaseSocialClient client,
        Guid shopId,
        string mediaType,
        string mediaUrl,
        string? caption,
        string accessToken,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["shop_id"] = shopId,
            ["media_type"] = mediaType is "video" ? "video" : "image",
            ["media_url"] = mediaUrl.Trim(),
            ["caption"] = (caption ?? "").Trim(),
            ["sort_order"] = 0
        };

        using var req = client.CreatePublicRequest(HttpMethod.Post, "/rest/v1/profile_shop_media", accessToken);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        req.Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");
        using var resp = await client.Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Add shop media failed ({(int)resp.StatusCode}): {body}");

        var rows = JsonSerializer.Deserialize<List<MediaRow>>(body, Json) ?? [];
        return rows.FirstOrDefault()?.ToDomain()
            ?? throw new InvalidOperationException("Add media returned no rows.");
    }

    public static async Task DeleteMediaAsync(
        SupabaseSocialClient client,
        Guid mediaId,
        string accessToken,
        CancellationToken ct)
    {
        using var req = client.CreatePublicRequest(
            HttpMethod.Delete,
            $"/rest/v1/profile_shop_media?id=eq.{mediaId}",
            accessToken);
        using var resp = await client.Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Delete media failed ({(int)resp.StatusCode}): {body}");
        }
    }

    public static async Task<string> UploadShopMediaFileAsync(
        SupabaseSocialClient client,
        Guid userId,
        Guid shopId,
        Stream stream,
        string contentType,
        string extension,
        string accessToken,
        CancellationToken ct)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        if (ext is "jpeg")
            ext = "jpg";
        var isVideo = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                      || ext is "mp4" or "webm" or "mov";
        if (!isVideo && ext is not ("jpg" or "png" or "webp" or "gif"))
            ext = "jpg";
        if (isVideo && ext is not ("mp4" or "webm" or "mov"))
            ext = "mp4";

        var objectPath = $"{userId:D}/{shopId:D}/{Guid.NewGuid():N}.{ext}";
        using var req = client.CreatePublicRequest(
            HttpMethod.Post,
            $"/storage/v1/object/shop-media/{objectPath}?upsert=true",
            accessToken);
        req.Content = new StreamContent(stream);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType)
                ? (isVideo ? "video/mp4" : "image/jpeg")
                : contentType);

        using var resp = await client.Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Shop media upload failed ({(int)resp.StatusCode}): {body}");

        return $"{client.Config.Url.TrimEnd('/')}/storage/v1/object/public/shop-media/{objectPath}";
    }

    private static string NormalizeMarketplace(string raw)
    {
        var id = (raw ?? "").Trim().ToLowerInvariant();
        if (MarketplaceKinds.All.Any(x => x.Id == id))
            return id;
        return MarketplaceKinds.Other;
    }

    private static string NormalizeUrl(string raw)
    {
        var url = (raw ?? "").Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Shop URL must be a valid http(s) link.");
        return uri.AbsoluteUri;
    }

    private sealed class ShopRow
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("profile_id")]
        public Guid ProfileId { get; set; }

        [JsonPropertyName("marketplace")]
        public string Marketplace { get; set; } = "";

        [JsonPropertyName("shop_name")]
        public string ShopName { get; set; } = "";

        [JsonPropertyName("shop_url")]
        public string ShopUrl { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [JsonPropertyName("profile_shop_media")]
        public List<MediaRow>? ProfileShopMedia { get; set; }

        public ProfileShop ToDomain() => new()
        {
            Id = Id,
            ProfileId = ProfileId,
            Marketplace = Marketplace,
            ShopName = ShopName ?? "",
            ShopUrl = ShopUrl,
            Description = Description ?? "",
            SortOrder = SortOrder,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Media = (ProfileShopMedia ?? [])
                .OrderBy(m => m.SortOrder)
                .Select(m => m.ToDomain())
                .ToList()
        };
    }

    private sealed class MediaRow
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("shop_id")]
        public Guid ShopId { get; set; }

        [JsonPropertyName("media_type")]
        public string MediaType { get; set; } = "image";

        [JsonPropertyName("media_url")]
        public string MediaUrl { get; set; } = "";

        [JsonPropertyName("caption")]
        public string Caption { get; set; } = "";

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        public ProfileShopMedia ToDomain() => new()
        {
            Id = Id,
            ShopId = ShopId,
            MediaType = MediaType,
            MediaUrl = MediaUrl,
            Caption = Caption ?? "",
            SortOrder = SortOrder,
            CreatedAt = CreatedAt
        };
    }
}
