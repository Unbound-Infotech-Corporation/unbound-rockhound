using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoMineralTrace.Core.Social;

namespace GeoMineralTrace.Infrastructure.Social;

public interface ISocialForumService
{
    Task<IReadOnlyList<ForumCategory>> ListCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ForumThread>> ListThreadsAsync(Guid categoryId, string? homeRegionFilter = null, CancellationToken ct = default);
    Task<ForumThread?> GetThreadAsync(Guid threadId, CancellationToken ct = default);
    Task<IReadOnlyList<ForumPost>> ListPostsAsync(Guid threadId, CancellationToken ct = default);
    Task<ForumThread> CreateThreadAsync(
        Guid categoryId,
        string title,
        string body,
        string? regionTag = null,
        IReadOnlyList<string>? imageUrls = null,
        CancellationToken ct = default);
    Task<ForumPost> CreatePostAsync(
        Guid threadId,
        string body,
        IReadOnlyList<string>? imageUrls = null,
        CancellationToken ct = default);
    /// <summary>Returns true if the thread is liked after the toggle.</summary>
    Task<bool> ToggleThreadLikeAsync(Guid threadId, CancellationToken ct = default);
    Task<bool> HasLikedThreadAsync(Guid threadId, CancellationToken ct = default);
    Task<string> UploadAttachmentAsync(Stream stream, string contentType, string ext, CancellationToken ct = default);

    Task<IReadOnlyList<RedditDiscovery>> ListRedditDiscoveriesAsync(int limit = 50, CancellationToken ct = default);
    Task<IReadOnlyList<FacebookDirectoryEntry>> ListFacebookDirectoryAsync(CancellationToken ct = default);
    /// <summary>Mentions with coordinates only — for map layer (never merge into claims).</summary>
    Task<IReadOnlyList<CommunityLocationMention>> ListCommunityMentionsWithCoordsAsync(
        int limit = 200,
        CancellationToken ct = default);
}

public sealed class SocialForumService : ISocialForumService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISocialAuthService _auth;
    private readonly SocialAuthService _authConcrete;
    private readonly HttpClient _http;
    private readonly SupabaseConfigStore _configStore;

    public SocialForumService(
        ISocialAuthService auth,
        SocialAuthService authConcrete,
        HttpClient http,
        SupabaseConfigStore configStore)
    {
        _auth = auth;
        _authConcrete = authConcrete;
        _http = http;
        _configStore = configStore;
    }

    public async Task<IReadOnlyList<ForumCategory>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var path = "/rest/v1/forum_categories?select=*&order=sort_order.asc";
        var rows = await GetRowsAsync<ForumCategoryRow>(path, session.AccessToken, ct).ConfigureAwait(false);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<ForumThread>> ListThreadsAsync(
        Guid categoryId,
        string? homeRegionFilter = null,
        CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var path =
            $"/rest/v1/forum_threads?category_id=eq.{categoryId:D}&select=*&order=is_pinned.desc,updated_at.desc";
        if (!string.IsNullOrWhiteSpace(homeRegionFilter))
        {
            var encoded = Uri.EscapeDataString(homeRegionFilter.Trim());
            path += $"&region_tag=eq.{encoded}";
        }

        var rows = await GetRowsAsync<ForumThreadRow>(path, session.AccessToken, ct).ConfigureAwait(false);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<ForumThread?> GetThreadAsync(Guid threadId, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var path = $"/rest/v1/forum_threads?id=eq.{threadId:D}&select=*";
        var rows = await GetRowsAsync<ForumThreadRow>(path, session.AccessToken, ct).ConfigureAwait(false);
        return rows.FirstOrDefault()?.ToDomain();
    }

    public async Task<IReadOnlyList<ForumPost>> ListPostsAsync(Guid threadId, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var path = $"/rest/v1/forum_posts?thread_id=eq.{threadId:D}&select=*&order=created_at.asc";
        var rows = await GetRowsAsync<ForumPostRow>(path, session.AccessToken, ct).ConfigureAwait(false);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<ForumThread> CreateThreadAsync(
        Guid categoryId,
        string title,
        string body,
        string? regionTag = null,
        IReadOnlyList<string>? imageUrls = null,
        CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
            throw new InvalidOperationException("Thread title is required.");

        var payload = new Dictionary<string, object?>
        {
            ["category_id"] = categoryId,
            ["author_id"] = session.UserId,
            ["title"] = trimmedTitle,
            ["body"] = body ?? "",
            ["image_urls"] = NormalizeUrls(imageUrls)
        };
        if (!string.IsNullOrWhiteSpace(regionTag))
            payload["region_tag"] = regionTag.Trim();

        var row = await PostReturningAsync<ForumThreadRow>(
            "/rest/v1/forum_threads",
            payload,
            session.AccessToken,
            ct).ConfigureAwait(false);
        return row.ToDomain();
    }

    public async Task<ForumPost> CreatePostAsync(
        Guid threadId,
        string body,
        IReadOnlyList<string>? imageUrls = null,
        CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var trimmed = (body ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Reply body is required.");

        var payload = new Dictionary<string, object?>
        {
            ["thread_id"] = threadId,
            ["author_id"] = session.UserId,
            ["body"] = trimmed,
            ["image_urls"] = NormalizeUrls(imageUrls)
        };

        var row = await PostReturningAsync<ForumPostRow>(
            "/rest/v1/forum_posts",
            payload,
            session.AccessToken,
            ct).ConfigureAwait(false);
        return row.ToDomain();
    }

    public async Task<bool> ToggleThreadLikeAsync(Guid threadId, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        if (await HasLikedThreadAsync(threadId, ct).ConfigureAwait(false))
        {
            await DeleteLikeAsync(threadId, session, ct).ConfigureAwait(false);
            return false;
        }

        await InsertLikeAsync(threadId, session, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> HasLikedThreadAsync(Guid threadId, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var path =
            $"/rest/v1/forum_likes?user_id=eq.{session.UserId:D}&thread_id=eq.{threadId:D}&select=id&limit=1";
        var rows = await GetRowsAsync<ForumLikeRow>(path, session.AccessToken, ct).ConfigureAwait(false);
        return rows.Count > 0;
    }

    public async Task<string> UploadAttachmentAsync(
        Stream stream,
        string contentType,
        string ext,
        CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var config = ResolveConfig();

        var normalizedExt = ext.TrimStart('.').ToLowerInvariant();
        if (normalizedExt is not ("jpg" or "jpeg" or "png" or "webp" or "gif"))
            normalizedExt = "jpg";

        var objectPath = $"{session.UserId:D}/{Guid.NewGuid():N}.{normalizedExt}";
        using var req = CreateRequest(HttpMethod.Post, $"/storage/v1/object/forum-attachments/{objectPath}", session.AccessToken);
        req.Content = new StreamContent(stream);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Attachment upload failed ({(int)resp.StatusCode}): {json}");

        return $"{config.Url.TrimEnd('/')}/storage/v1/object/public/forum-attachments/{objectPath}";
    }

    public async Task<IReadOnlyList<RedditDiscovery>> ListRedditDiscoveriesAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var capped = Math.Clamp(limit, 1, 200);
        var path = $"/rest/v1/reddit_discoveries?select=*&order=fetched_at.desc&limit={capped}";
        var rows = await GetRowsAsync<RedditDiscoveryRow>(path, session.AccessToken, ct).ConfigureAwait(false);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<FacebookDirectoryEntry>> ListFacebookDirectoryAsync(
        CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var path = "/rest/v1/facebook_directory?select=*&order=sort_order.asc";
        var rows = await GetRowsAsync<FacebookDirectoryRow>(path, session.AccessToken, ct).ConfigureAwait(false);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<CommunityLocationMention>> ListCommunityMentionsWithCoordsAsync(
        int limit = 200,
        CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var capped = Math.Clamp(limit, 1, 500);
        // PostgREST: not.is.null filters
        var path =
            $"/rest/v1/community_location_mentions?select=*&lat=not.is.null&lon=not.is.null&order=created_at.desc&limit={capped}";
        var rows = await GetRowsAsync<CommunityMentionRow>(path, session.AccessToken, ct).ConfigureAwait(false);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    private async Task InsertLikeAsync(Guid threadId, SocialAuthSession session, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["user_id"] = session.UserId,
            ["thread_id"] = threadId
        };
        using var req = CreateRequest(HttpMethod.Post, "/rest/v1/forum_likes", session.AccessToken);
        req.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        req.Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Like failed ({(int)resp.StatusCode}): {json}");
    }

    private async Task DeleteLikeAsync(Guid threadId, SocialAuthSession session, CancellationToken ct)
    {
        var path =
            $"/rest/v1/forum_likes?user_id=eq.{session.UserId:D}&thread_id=eq.{threadId:D}";
        using var req = CreateRequest(HttpMethod.Delete, path, session.AccessToken);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Unlike failed ({(int)resp.StatusCode}): {json}");
    }

    private async Task<List<T>> GetRowsAsync<T>(string path, string accessToken, CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Get, path, accessToken);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Forum request failed ({(int)resp.StatusCode}): {json}");
        return JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];
    }

    private async Task<T> PostReturningAsync<T>(
        string path,
        Dictionary<string, object?> payload,
        string accessToken,
        CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Post, path, accessToken);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        req.Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Forum write failed ({(int)resp.StatusCode}): {json}");
        var rows = JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];
        return rows.FirstOrDefault()
            ?? throw new InvalidOperationException("Forum write returned no rows (RLS may have blocked the insert).");
    }

    private async Task<SocialAuthSession> RequireSessionAsync(CancellationToken ct)
    {
        await _auth.RefreshIfNeededAsync(ct).ConfigureAwait(false);
        return _auth.CurrentSession
            ?? throw new InvalidOperationException("Sign in to use the forum.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string accessToken)
    {
        var cfg = ResolveConfig();
        var req = new HttpRequestMessage(method, cfg.Url.TrimEnd('/') + path);
        req.Headers.TryAddWithoutValidation("apikey", cfg.AnonKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return req;
    }

    private SupabaseProjectConfig ResolveConfig()
    {
        // Match SocialProfileService: prefer auth client's configured project.
        if (_authConcrete.GetApiClient() is { IsConfigured: true })
        {
            var fromStore = _configStore.Load();
            if (fromStore.IsConfigured)
                return fromStore;
        }

        var config = _configStore.Load();
        if (!config.IsConfigured)
            throw new InvalidOperationException("Configure Supabase URL and anon key first.");
        return config;
    }

    private static string[] NormalizeUrls(IReadOnlyList<string>? imageUrls)
    {
        if (imageUrls is null || imageUrls.Count == 0)
            return [];
        return imageUrls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Take(8)
            .ToArray();
    }
}

internal sealed class ForumCategoryRow
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("icon_key")]
    public string IconKey { get; set; } = "forum";

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

    [JsonPropertyName("is_region_scoped")]
    public bool IsRegionScoped { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public ForumCategory ToDomain() => new()
    {
        Id = Id,
        Slug = Slug,
        Name = Name,
        Description = Description ?? "",
        IconKey = IconKey ?? "forum",
        SortOrder = SortOrder,
        IsRegionScoped = IsRegionScoped,
        CreatedAt = CreatedAt
    };
}

internal sealed class ForumThreadRow
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("category_id")]
    public Guid CategoryId { get; set; }

    [JsonPropertyName("author_id")]
    public Guid AuthorId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("region_tag")]
    public string? RegionTag { get; set; }

    [JsonPropertyName("image_urls")]
    public string[]? ImageUrls { get; set; }

    [JsonPropertyName("reply_count")]
    public int ReplyCount { get; set; }

    [JsonPropertyName("like_count")]
    public int LikeCount { get; set; }

    [JsonPropertyName("is_pinned")]
    public bool IsPinned { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public ForumThread ToDomain() => new()
    {
        Id = Id,
        CategoryId = CategoryId,
        AuthorId = AuthorId,
        Title = Title,
        Body = Body ?? "",
        RegionTag = RegionTag,
        ImageUrls = ImageUrls ?? [],
        ReplyCount = ReplyCount,
        LikeCount = LikeCount,
        IsPinned = IsPinned,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}

internal sealed class ForumPostRow
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("thread_id")]
    public Guid ThreadId { get; set; }

    [JsonPropertyName("author_id")]
    public Guid AuthorId { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("image_urls")]
    public string[]? ImageUrls { get; set; }

    [JsonPropertyName("like_count")]
    public int LikeCount { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public ForumPost ToDomain() => new()
    {
        Id = Id,
        ThreadId = ThreadId,
        AuthorId = AuthorId,
        Body = Body,
        ImageUrls = ImageUrls ?? [],
        LikeCount = LikeCount,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}

internal sealed class ForumLikeRow
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }
}

internal sealed class RedditDiscoveryRow
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("reddit_thing_id")]
    public string RedditThingId { get; set; } = "";

    [JsonPropertyName("subreddit")]
    public string Subreddit { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("posted_at")]
    public DateTimeOffset? PostedAt { get; set; }

    [JsonPropertyName("fetched_at")]
    public DateTimeOffset FetchedAt { get; set; }

    public RedditDiscovery ToDomain() => new()
    {
        Id = Id,
        RedditThingId = RedditThingId,
        Subreddit = Subreddit,
        Title = Title,
        Snippet = Snippet ?? "",
        Url = Url,
        Score = Score,
        PostedAt = PostedAt,
        FetchedAt = FetchedAt
    };
}

internal sealed class FacebookDirectoryRow
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("region_tag")]
    public string? RegionTag { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public FacebookDirectoryEntry ToDomain() => new()
    {
        Id = Id,
        Name = Name,
        Url = Url,
        RegionTag = RegionTag,
        Notes = Notes ?? "",
        SortOrder = SortOrder,
        IsActive = IsActive,
        UpdatedAt = UpdatedAt
    };
}

internal sealed class CommunityMentionRow
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("reddit_discovery_id")]
    public Guid? RedditDiscoveryId { get; set; }

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = "";

    [JsonPropertyName("source_label")]
    public string SourceLabel { get; set; } = "";

    [JsonPropertyName("mention_text")]
    public string MentionText { get; set; } = "";

    [JsonPropertyName("lat")]
    public double? Lat { get; set; }

    [JsonPropertyName("lon")]
    public double? Lon { get; set; }

    [JsonPropertyName("place_hint")]
    public string? PlaceHint { get; set; }

    [JsonPropertyName("confidence")]
    public float Confidence { get; set; } = 0.3f;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public CommunityLocationMention ToDomain() => new()
    {
        Id = Id,
        RedditDiscoveryId = RedditDiscoveryId,
        SourceUrl = SourceUrl,
        SourceLabel = SourceLabel ?? "",
        MentionText = MentionText,
        Lat = Lat,
        Lon = Lon,
        PlaceHint = PlaceHint,
        Confidence = Confidence,
        CreatedAt = CreatedAt
    };
}
