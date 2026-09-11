using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Social;

namespace GeoMineralTrace.Infrastructure.Social;

public interface ISocialAuthService
{
    bool IsConfigured { get; }
    SocialAuthSession? CurrentSession { get; }
    SocialProfile? CurrentProfile { get; }
    event EventHandler? AuthStateChanged;

    Task InitializeAsync(CancellationToken ct = default);
    Task<SocialAuthSession> SignUpAsync(string email, string password, string? displayName = null, CancellationToken ct = default);
    Task<SocialAuthSession> SignInAsync(string email, string password, CancellationToken ct = default);
    Task SignOutAsync(CancellationToken ct = default);
    Task<SocialAuthSession?> RefreshIfNeededAsync(CancellationToken ct = default);
}

public interface ISocialProfileService
{
    Task<SocialProfile?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<SocialProfile?> GetByHandleAsync(string handle, CancellationToken ct = default);
    Task<SocialProfile> UpdateAsync(SocialProfileUpdate update, CancellationToken ct = default);
    Task<string> UploadAvatarAsync(Stream imageStream, string contentType, string fileExtension, CancellationToken ct = default);

    Task<IReadOnlyList<ProfileShop>> ListShopsAsync(Guid profileId, CancellationToken ct = default);
    Task<ProfileShop> AddShopAsync(ProfileShopDraft draft, CancellationToken ct = default);
    Task<ProfileShop> UpdateShopAsync(Guid shopId, ProfileShopDraft draft, CancellationToken ct = default);
    Task DeleteShopAsync(Guid shopId, CancellationToken ct = default);
    Task<ProfileShopMedia> UploadShopMediaAsync(
        Guid shopId,
        Stream stream,
        string contentType,
        string fileExtension,
        string? caption = null,
        CancellationToken ct = default);
    Task DeleteShopMediaAsync(Guid mediaId, CancellationToken ct = default);
}

public sealed class SocialProfileUpdate
{
    public string? DisplayName { get; init; }
    public string? Handle { get; init; }
    public string? Bio { get; init; }
    public string? HomeRegion { get; init; }
    public string? AvatarUrl { get; init; }
    public bool? ShowRedditDiscovery { get; init; }
}

public sealed class SupabaseConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path = AppDataPaths.Sub("supabase.json");

    public SupabaseProjectConfig Load()
    {
        try
        {
            var fromEnvUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var fromEnvKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");
            if (!string.IsNullOrWhiteSpace(fromEnvUrl) && !string.IsNullOrWhiteSpace(fromEnvKey))
            {
                return new SupabaseProjectConfig { Url = fromEnvUrl.TrimEnd('/'), AnonKey = fromEnvKey.Trim() };
            }

            if (!File.Exists(_path))
                return new SupabaseProjectConfig { Url = "", AnonKey = "" };

            var json = File.ReadAllText(_path);
            var dto = JsonSerializer.Deserialize<ConfigDto>(json, JsonOptions);
            return new SupabaseProjectConfig
            {
                Url = (dto?.Url ?? "").TrimEnd('/'),
                AnonKey = (dto?.AnonKey ?? "").Trim()
            };
        }
        catch
        {
            return new SupabaseProjectConfig { Url = "", AnonKey = "" };
        }
    }

    public void Save(SupabaseProjectConfig config)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var dto = new ConfigDto { Url = config.Url.TrimEnd('/'), AnonKey = config.AnonKey.Trim() };
        File.WriteAllText(_path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private sealed class ConfigDto
    {
        public string Url { get; set; } = "";
        public string AnonKey { get; set; } = "";
    }
}

/// <summary>DPAPI-protected session file under LocalAppData.</summary>
public sealed class SocialSessionStore
{
    private readonly string _path = AppDataPaths.Sub("social-session.bin");

    public void Save(SocialAuthSession session)
    {
        var json = JsonSerializer.Serialize(session, SessionJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(_path, protectedBytes);
    }

    public SocialAuthSession? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return null;
            var protectedBytes = File.ReadAllBytes(_path);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SocialAuthSession>(Encoding.UTF8.GetString(bytes), SessionJson.Options);
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch
        {
            // ignore
        }
    }
}

internal static class SessionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public sealed class SupabaseSocialClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly SupabaseProjectConfig _config;

    public SupabaseSocialClient(HttpClient http, SupabaseProjectConfig config)
    {
        _http = http;
        _config = config;
    }

    public bool IsConfigured => _config.IsConfigured;
    public HttpClient Http => _http;
    public SupabaseProjectConfig Config => _config;

    /// <summary>Builds an authenticated Supabase REST/Storage request (shared with shop APIs).</summary>
    public HttpRequestMessage CreatePublicRequest(HttpMethod method, string path, string? accessToken) =>
        CreateRequest(method, path, accessToken);

    public async Task<AuthTokenResponse> SignUpAsync(string email, string password, string? displayName, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["password"] = password
        };
        if (!string.IsNullOrWhiteSpace(displayName))
            body["data"] = new Dictionary<string, string> { ["display_name"] = displayName.Trim() };

        return await PostAuthAsync("/auth/v1/signup", body, bearer: null, ct).ConfigureAwait(false);
    }

    public async Task<AuthTokenResponse> SignInAsync(string email, string password, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["password"] = password
        };
        return await PostAuthAsync("/auth/v1/token?grant_type=password", body, bearer: null, ct).ConfigureAwait(false);
    }

    public async Task<AuthTokenResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var body = new Dictionary<string, object?> { ["refresh_token"] = refreshToken };
        return await PostAuthAsync("/auth/v1/token?grant_type=refresh_token", body, bearer: null, ct).ConfigureAwait(false);
    }

    public async Task SignOutAsync(string accessToken, CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Post, "/auth/v1/logout", accessToken);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        // 204 / 200 OK — ignore failures on logout
    }

    public async Task<ProfileRow?> GetProfileAsync(Guid id, string accessToken, CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Get, $"/rest/v1/profiles?id=eq.{id}&select=*", accessToken);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Get profile failed ({(int)resp.StatusCode}): {json}");
        var rows = JsonSerializer.Deserialize<List<ProfileRow>>(json, Json) ?? [];
        return rows.FirstOrDefault();
    }

    public async Task<ProfileRow?> GetProfileByHandleAsync(string handle, string accessToken, CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(handle.Trim().ToLowerInvariant());
        using var req = CreateRequest(HttpMethod.Get, $"/rest/v1/profiles?handle=eq.{encoded}&select=*", accessToken);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Get profile by handle failed ({(int)resp.StatusCode}): {json}");
        var rows = JsonSerializer.Deserialize<List<ProfileRow>>(json, Json) ?? [];
        return rows.FirstOrDefault();
    }

    public async Task<ProfileRow> PatchProfileAsync(Guid id, Dictionary<string, object?> patch, string accessToken, CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Patch, $"/rest/v1/profiles?id=eq.{id}", accessToken);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        req.Content = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Update profile failed ({(int)resp.StatusCode}): {json}");
        var rows = JsonSerializer.Deserialize<List<ProfileRow>>(json, Json) ?? [];
        return rows.FirstOrDefault()
            ?? throw new InvalidOperationException("Update returned no rows (RLS may have blocked the write).");
    }

    /// <summary>Used by RLS verification tests — returns raw status + body.</summary>
    public async Task<(int StatusCode, string Body)> RawPatchProfileAsync(
        Guid targetId,
        string accessToken,
        string jsonBody,
        CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Patch, $"/rest/v1/profiles?id=eq.{targetId}", accessToken);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ((int)resp.StatusCode, body);
    }

    public async Task<string> UploadAvatarAsync(
        Guid userId,
        Stream stream,
        string contentType,
        string extension,
        string accessToken,
        CancellationToken ct)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        if (ext is not ("jpg" or "jpeg" or "png" or "webp" or "gif"))
            ext = "jpg";
        var objectPath = $"{userId:D}/avatar.{ext}";
        using var req = CreateRequest(HttpMethod.Post, $"/storage/v1/object/avatars/{objectPath}?upsert=true", accessToken);
        req.Content = new StreamContent(stream);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Avatar upload failed ({(int)resp.StatusCode}): {json}");

        return $"{_config.Url}/storage/v1/object/public/avatars/{objectPath}";
    }

    private async Task<AuthTokenResponse> PostAuthAsync(
        string path,
        Dictionary<string, object?> body,
        string? bearer,
        CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Post, path, bearer);
        req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(ParseAuthError(json, (int)resp.StatusCode));
        var token = JsonSerializer.Deserialize<AuthTokenResponse>(json, Json)
            ?? throw new InvalidOperationException("Empty auth response.");
        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new InvalidOperationException("Auth response missing access_token. Confirm email may be required in Supabase Auth settings.");
        return token;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? accessToken)
    {
        var req = new HttpRequestMessage(method, _config.Url.TrimEnd('/') + path);
        req.Headers.TryAddWithoutValidation("apikey", _config.AnonKey);
        if (!string.IsNullOrWhiteSpace(accessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        else
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AnonKey);
        return req;
    }

    private static string ParseAuthError(string json, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("msg", out var msg))
                return msg.GetString() ?? json;
            if (doc.RootElement.TryGetProperty("error_description", out var desc))
                return desc.GetString() ?? json;
            if (doc.RootElement.TryGetProperty("message", out var message))
                return message.GetString() ?? json;
        }
        catch
        {
            // fall through
        }
        return $"Auth failed ({status}): {json}";
    }
}

public sealed class AuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("user")]
    public AuthUserDto? User { get; set; }
}

public sealed class AuthUserDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

public sealed class ProfileRow
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("bio")]
    public string Bio { get; set; } = "";

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("home_region")]
    public string? HomeRegion { get; set; }

    [JsonPropertyName("show_reddit_discovery")]
    public bool ShowRedditDiscovery { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public SocialProfile ToDomain() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Handle = Handle,
        Bio = Bio ?? "",
        AvatarUrl = AvatarUrl,
        HomeRegion = HomeRegion,
        ShowRedditDiscovery = ShowRedditDiscovery,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}

public sealed class SocialAuthService : ISocialAuthService
{
    private readonly SupabaseConfigStore _configStore;
    private readonly SocialSessionStore _sessionStore;
    private readonly HttpClient _http;
    private SupabaseSocialClient? _client;
    private SocialAuthSession? _session;
    private SocialProfile? _profile;

    public SocialAuthService(SupabaseConfigStore configStore, SocialSessionStore sessionStore, HttpClient http)
    {
        _configStore = configStore;
        _sessionStore = sessionStore;
        _http = http;
    }

    public bool IsConfigured => GetClient().IsConfigured;
    public SocialAuthSession? CurrentSession => _session;
    public SocialProfile? CurrentProfile => _profile;
    public event EventHandler? AuthStateChanged;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var saved = _sessionStore.Load();
        if (saved is null || !IsConfigured)
            return;

        _session = saved;
        try
        {
            await RefreshIfNeededAsync(ct).ConfigureAwait(false);
            await LoadProfileAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            _session = null;
            _profile = null;
            _sessionStore.Clear();
        }

        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<SocialAuthSession> SignUpAsync(string email, string password, string? displayName = null, CancellationToken ct = default)
    {
        var client = GetClient();
        if (!client.IsConfigured)
            throw new InvalidOperationException("Configure Supabase URL and anon key in Settings (or supabase.json) first.");

        var token = await client.SignUpAsync(email.Trim(), password, displayName, ct).ConfigureAwait(false);
        await ApplyTokenAsync(token, email.Trim(), ct).ConfigureAwait(false);
        return _session!;
    }

    public async Task<SocialAuthSession> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        var client = GetClient();
        if (!client.IsConfigured)
            throw new InvalidOperationException("Configure Supabase URL and anon key in Settings (or supabase.json) first.");

        var token = await client.SignInAsync(email.Trim(), password, ct).ConfigureAwait(false);
        await ApplyTokenAsync(token, email.Trim(), ct).ConfigureAwait(false);
        return _session!;
    }

    public Task SignOutAsync(CancellationToken ct = default)
    {
        var session = _session;
        _session = null;
        _profile = null;
        _sessionStore.Clear();
        AuthStateChanged?.Invoke(this, EventArgs.Empty);

        if (session is not null && IsConfigured)
        {
            _ = GetClient().SignOutAsync(session.AccessToken, ct);
        }

        return Task.CompletedTask;
    }

    public async Task<SocialAuthSession?> RefreshIfNeededAsync(CancellationToken ct = default)
    {
        if (_session is null || !IsConfigured)
            return _session;

        if (!_session.IsExpired())
            return _session;

        var token = await GetClient().RefreshAsync(_session.RefreshToken, ct).ConfigureAwait(false);
        await ApplyTokenAsync(token, _session.Email, ct).ConfigureAwait(false);
        return _session;
    }

    public SupabaseSocialClient GetApiClient() => GetClient();

    private async Task ApplyTokenAsync(AuthTokenResponse token, string fallbackEmail, CancellationToken ct)
    {
        if (!Guid.TryParse(token.User?.Id, out var userId))
            throw new InvalidOperationException("Auth response missing user id.");

        _session = new SocialAuthSession
        {
            UserId = userId,
            Email = token.User?.Email ?? fallbackEmail,
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3600)
        };
        _sessionStore.Save(_session);
        await LoadProfileAsync(ct).ConfigureAwait(false);
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadProfileAsync(CancellationToken ct)
    {
        if (_session is null)
        {
            _profile = null;
            return;
        }

        // Trigger may lag; retry briefly
        for (var i = 0; i < 5; i++)
        {
            var row = await GetClient().GetProfileAsync(_session.UserId, _session.AccessToken, ct).ConfigureAwait(false);
            if (row is not null)
            {
                _profile = row.ToDomain();
                return;
            }
            await Task.Delay(200 * (i + 1), ct).ConfigureAwait(false);
        }

        _profile = null;
    }

    private SupabaseSocialClient GetClient()
    {
        var config = _configStore.Load();
        _client = new SupabaseSocialClient(_http, config);
        return _client;
    }
}

public sealed class SocialProfileService : ISocialProfileService
{
    private readonly ISocialAuthService _auth;
    private readonly SocialAuthService _authConcrete;
    private readonly HttpClient _http;
    private readonly SupabaseConfigStore _configStore;

    public SocialProfileService(ISocialAuthService auth, SocialAuthService authConcrete, HttpClient http, SupabaseConfigStore configStore)
    {
        _auth = auth;
        _authConcrete = authConcrete;
        _http = http;
        _configStore = configStore;
    }

    public async Task<SocialProfile?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var row = await Client().GetProfileAsync(userId, session.AccessToken, ct).ConfigureAwait(false);
        if (row is null)
            return null;
        var profile = row.ToDomain();
        try
        {
            var shops = await ProfileShopApi.ListShopsAsync(Client(), userId, session.AccessToken, ct)
                .ConfigureAwait(false);
            return profile.WithShops(shops);
        }
        catch
        {
            // Shops table may not be migrated yet — still return profile.
            return profile;
        }
    }

    public async Task<SocialProfile?> GetByHandleAsync(string handle, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var row = await Client().GetProfileByHandleAsync(handle, session.AccessToken, ct).ConfigureAwait(false);
        if (row is null)
            return null;
        var profile = row.ToDomain();
        try
        {
            var shops = await ProfileShopApi.ListShopsAsync(Client(), profile.Id, session.AccessToken, ct)
                .ConfigureAwait(false);
            return profile.WithShops(shops);
        }
        catch
        {
            return profile;
        }
    }

    public async Task<SocialProfile> UpdateAsync(SocialProfileUpdate update, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var patch = new Dictionary<string, object?>();
        if (update.DisplayName is not null)
            patch["display_name"] = update.DisplayName.Trim();
        if (update.Handle is not null)
            patch["handle"] = string.IsNullOrWhiteSpace(update.Handle) ? null : update.Handle.Trim().ToLowerInvariant();
        if (update.Bio is not null)
            patch["bio"] = update.Bio;
        if (update.HomeRegion is not null)
            patch["home_region"] = string.IsNullOrWhiteSpace(update.HomeRegion) ? null : update.HomeRegion.Trim();
        if (update.AvatarUrl is not null)
            patch["avatar_url"] = update.AvatarUrl;
        if (update.ShowRedditDiscovery is not null)
            patch["show_reddit_discovery"] = update.ShowRedditDiscovery.Value;

        if (patch.Count == 0)
        {
            var existing = await GetByIdAsync(session.UserId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Profile not found.");
            return existing;
        }

        var row = await Client().PatchProfileAsync(session.UserId, patch, session.AccessToken, ct).ConfigureAwait(false);
        return row.ToDomain();
    }

    public async Task<string> UploadAvatarAsync(Stream imageStream, string contentType, string fileExtension, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var url = await Client()
            .UploadAvatarAsync(session.UserId, imageStream, contentType, fileExtension, session.AccessToken, ct)
            .ConfigureAwait(false);
        await UpdateAsync(new SocialProfileUpdate { AvatarUrl = url }, ct).ConfigureAwait(false);
        return url;
    }

    public async Task<IReadOnlyList<ProfileShop>> ListShopsAsync(Guid profileId, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        return await ProfileShopApi.ListShopsAsync(Client(), profileId, session.AccessToken, ct).ConfigureAwait(false);
    }

    public async Task<ProfileShop> AddShopAsync(ProfileShopDraft draft, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        return await ProfileShopApi.InsertShopAsync(Client(), session.UserId, draft, session.AccessToken, ct)
            .ConfigureAwait(false);
    }

    public async Task<ProfileShop> UpdateShopAsync(Guid shopId, ProfileShopDraft draft, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        return await ProfileShopApi.UpdateShopAsync(Client(), shopId, draft, session.AccessToken, ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteShopAsync(Guid shopId, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        await ProfileShopApi.DeleteShopAsync(Client(), shopId, session.AccessToken, ct).ConfigureAwait(false);
    }

    public async Task<ProfileShopMedia> UploadShopMediaAsync(
        Guid shopId,
        Stream stream,
        string contentType,
        string fileExtension,
        string? caption = null,
        CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        var url = await ProfileShopApi.UploadShopMediaFileAsync(
                Client(),
                session.UserId,
                shopId,
                stream,
                contentType,
                fileExtension,
                session.AccessToken,
                ct)
            .ConfigureAwait(false);
        var mediaType = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
        return await ProfileShopApi.AddMediaAsync(Client(), shopId, mediaType, url, caption, session.AccessToken, ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteShopMediaAsync(Guid mediaId, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);
        await ProfileShopApi.DeleteMediaAsync(Client(), mediaId, session.AccessToken, ct).ConfigureAwait(false);
    }

    private async Task<SocialAuthSession> RequireSessionAsync(CancellationToken ct)
    {
        await _auth.RefreshIfNeededAsync(ct).ConfigureAwait(false);
        return _auth.CurrentSession
            ?? throw new InvalidOperationException("Sign in to manage your cloud profile.");
    }

    private SupabaseSocialClient Client()
    {
        if (_authConcrete.GetApiClient() is { IsConfigured: true } c)
            return c;
        return new SupabaseSocialClient(_http, _configStore.Load());
    }
}
