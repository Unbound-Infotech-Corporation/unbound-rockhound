using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Licensing;
using GeoMineralTrace.Infrastructure.Social;

namespace GeoMineralTrace.Infrastructure.Licensing;

public interface ILicenseService
{
    /// <summary>True when paid activation is valid, or trial still open.</summary>
    bool IsEntitled { get; }

    LicenseEntitlement? Current { get; }
    event EventHandler? LicenseChanged;

    Task InitializeAsync(CancellationToken ct = default);
    Task<LicenseActivateResult> ActivateAsync(string licenseKey, CancellationToken ct = default);
    Task RefreshOnlineAsync(CancellationToken ct = default);
    void DeactivateLocally();
    string GetDeviceFingerprint();
}

/// <summary>DPAPI-protected local license cache under LocalAppData.</summary>
public sealed class LicenseLocalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _path = AppDataPaths.Sub("license.bin");

    public void Save(LicenseEntitlement entitlement)
    {
        var json = JsonSerializer.Serialize(entitlement, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(_path, protectedBytes);
    }

    public LicenseEntitlement? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return null;
            var protectedBytes = File.ReadAllBytes(_path);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<LicenseEntitlement>(Encoding.UTF8.GetString(bytes), JsonOptions);
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

public sealed class LicenseService : ILicenseService
{
    public const int TrialDays = 14;
    private static readonly TimeSpan OnlineRecheckInterval = TimeSpan.FromDays(7);

    private readonly SupabaseConfigStore _config;
    private readonly LicenseLocalStore _store;
    private readonly HttpClient _http;
    private LicenseEntitlement? _current;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LicenseService(SupabaseConfigStore config, LicenseLocalStore store, HttpClient http)
    {
        _config = config;
        _store = store;
        _http = http;
    }

    public LicenseEntitlement? Current => _current;
    public event EventHandler? LicenseChanged;

    public bool IsEntitled
    {
        get
        {
            var e = _current;
            if (e is null)
                return false;
            if (string.Equals(e.Status, LicenseStatuses.Active, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(e.LicenseKey))
                return true;
            if (string.Equals(e.Status, LicenseStatuses.Trial, StringComparison.OrdinalIgnoreCase)
                && e.TrialEndsUtc is { } end
                && end > DateTimeOffset.UtcNow)
                return true;
            return false;
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _current = _store.Load();
        if (_current is null)
        {
            var started = DateTimeOffset.UtcNow;
            _current = new LicenseEntitlement
            {
                ProductId = AppBranding.UpdateProductId,
                Status = LicenseStatuses.Trial,
                TrialStartedUtc = started,
                TrialEndsUtc = started.AddDays(TrialDays),
                ValidatedAtUtc = started
            };
            _store.Save(_current);
            LicenseChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Expire local trial
        if (string.Equals(_current.Status, LicenseStatuses.Trial, StringComparison.OrdinalIgnoreCase)
            && _current.TrialEndsUtc is { } ends
            && ends <= DateTimeOffset.UtcNow)
        {
            _current = new LicenseEntitlement
            {
                ProductId = _current.ProductId,
                Status = LicenseStatuses.Expired,
                TrialStartedUtc = _current.TrialStartedUtc,
                TrialEndsUtc = _current.TrialEndsUtc,
                ValidatedAtUtc = DateTimeOffset.UtcNow
            };
            _store.Save(_current);
        }

        LicenseChanged?.Invoke(this, EventArgs.Empty);

        // Periodic online revalidation for paid seats
        if (!string.IsNullOrWhiteSpace(_current.LicenseKey)
            && string.Equals(_current.Status, LicenseStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            var last = _current.ValidatedAtUtc ?? DateTimeOffset.MinValue;
            if (DateTimeOffset.UtcNow - last >= OnlineRecheckInterval)
            {
                try
                {
                    await RefreshOnlineAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    // Offline grace: keep local active entitlement
                }
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task<LicenseActivateResult> ActivateAsync(string licenseKey, CancellationToken ct = default)
    {
        var cfg = _config.Load();
        if (!cfg.IsConfigured)
        {
            return new LicenseActivateResult
            {
                Ok = false,
                Error = "Configure Supabase URL and anon key under Sign in / Cloud account first (same project that hosts license functions)."
            };
        }

        string normalized;
        try
        {
            normalized = LicenseKeyFormat.Normalize(licenseKey);
        }
        catch (Exception ex)
        {
            return new LicenseActivateResult { Ok = false, Error = ex.Message };
        }

        var url = $"{cfg.Url.TrimEnd('/')}/functions/v1/license-activate";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.AnonKey);
        req.Headers.TryAddWithoutValidation("apikey", cfg.AnonKey);
        req.Content = JsonContent.Create(new
        {
            licenseKey = normalized,
            deviceFingerprint = GetDeviceFingerprint(),
            deviceLabel = $"{Environment.MachineName} ({Environment.UserName})",
            productId = AppBranding.UpdateProductId
        });

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var payload = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        ActivateDto? dto = null;
        try
        {
            dto = JsonSerializer.Deserialize<ActivateDto>(payload, JsonOptions);
        }
        catch
        {
            // ignore
        }

        if (!res.IsSuccessStatusCode || dto?.Ok != true)
        {
            var err = dto?.Error ?? $"Activation failed ({(int)res.StatusCode}).";
            return new LicenseActivateResult { Ok = false, Error = err };
        }

        _current = new LicenseEntitlement
        {
            ProductId = dto.ProductId ?? AppBranding.UpdateProductId,
            LicenseKey = dto.LicenseKey ?? normalized,
            Email = dto.Email,
            Status = LicenseStatuses.Active,
            MaxActivations = dto.MaxActivations ?? 3,
            ValidatedAtUtc = ParseUtc(dto.ValidatedAtUtc) ?? DateTimeOffset.UtcNow,
            TrialStartedUtc = _current?.TrialStartedUtc,
            TrialEndsUtc = _current?.TrialEndsUtc
        };
        _store.Save(_current);
        LicenseChanged?.Invoke(this, EventArgs.Empty);
        return new LicenseActivateResult { Ok = true, Entitlement = _current };
    }

    public async Task RefreshOnlineAsync(CancellationToken ct = default)
    {
        if (_current is null || string.IsNullOrWhiteSpace(_current.LicenseKey))
            return;

        var cfg = _config.Load();
        if (!cfg.IsConfigured)
            return;

        var url = $"{cfg.Url.TrimEnd('/')}/functions/v1/license-validate";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.AnonKey);
        req.Headers.TryAddWithoutValidation("apikey", cfg.AnonKey);
        req.Content = JsonContent.Create(new
        {
            licenseKey = _current.LicenseKey,
            deviceFingerprint = GetDeviceFingerprint(),
            productId = AppBranding.UpdateProductId
        });

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var payload = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        ActivateDto? dto = null;
        try
        {
            dto = JsonSerializer.Deserialize<ActivateDto>(payload, JsonOptions);
        }
        catch
        {
            return;
        }

        if (res.IsSuccessStatusCode && dto?.Ok == true)
        {
            _current = new LicenseEntitlement
            {
                ProductId = dto.ProductId ?? _current.ProductId,
                LicenseKey = dto.LicenseKey ?? _current.LicenseKey,
                Email = dto.Email ?? _current.Email,
                Status = LicenseStatuses.Active,
                MaxActivations = dto.MaxActivations ?? _current.MaxActivations,
                ValidatedAtUtc = ParseUtc(dto.ValidatedAtUtc) ?? DateTimeOffset.UtcNow,
                TrialStartedUtc = _current.TrialStartedUtc,
                TrialEndsUtc = _current.TrialEndsUtc
            };
            _store.Save(_current);
            LicenseChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Explicit revoke / refund / unbound
        if (res.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            var status = dto?.Status switch
            {
                "refunded" => LicenseStatuses.Refunded,
                "revoked" => LicenseStatuses.Revoked,
                _ => LicenseStatuses.Revoked
            };
            _current = new LicenseEntitlement
            {
                ProductId = _current.ProductId,
                LicenseKey = _current.LicenseKey,
                Email = _current.Email,
                Status = status,
                MaxActivations = _current.MaxActivations,
                ValidatedAtUtc = DateTimeOffset.UtcNow,
                TrialStartedUtc = _current.TrialStartedUtc,
                TrialEndsUtc = _current.TrialEndsUtc
            };
            _store.Save(_current);
            LicenseChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void DeactivateLocally()
    {
        _store.Clear();
        _current = null;
        LicenseChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetDeviceFingerprint()
    {
        var raw = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion.VersionString}|{AppBranding.UpdateProductId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32];
    }

    private static DateTimeOffset? ParseUtc(string? s) =>
        DateTimeOffset.TryParse(s, out var dto) ? dto.ToUniversalTime() : null;

    private sealed class ActivateDto
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? LicenseKey { get; set; }
        public string? Email { get; set; }
        public string? Status { get; set; }
        public string? ProductId { get; set; }
        public int? MaxActivations { get; set; }
        public string? ValidatedAtUtc { get; set; }
    }
}
