using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Unbound.Marketplace.Activation;

public enum MarketplaceSessionLookupKind
{
    Pending,
    Found,
    NotConfigured,
    Failed
}

public sealed record MarketplaceSessionLookup(
    MarketplaceSessionLookupKind Kind,
    string? LicenseKey,
    string? ProductId,
    string? Message);

public interface IMarketplaceSupabaseSettings
{
    bool IsConfigured { get; }
    string Url { get; }
    string AnonKey { get; }
}

public interface IMarketplaceSessionClient
{
    Task<MarketplaceSessionLookup> LookupAsync(string checkoutSessionId, CancellationToken cancellationToken = default);
}

public static class MarketplaceSessionLookupParser
{
    public static MarketplaceSessionLookup Parse(HttpStatusCode status, string payload)
    {
        string? licenseKey = null;
        string? productId = null;
        string? message = null;
        string? error = null;
        var ok = false;
        var pending = false;

        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = doc.RootElement;
                    ok = ReadBool(root, "ok") == true;
                    pending = ReadBool(root, "pending") == true;
                    licenseKey = ReadString(root, "licenseKey", "license_key");
                    productId = ReadString(root, "productId", "product_id");
                    message = ReadString(root, "message");
                    error = ReadString(root, "error");
                }
            }
            catch (JsonException)
            {
                return new MarketplaceSessionLookup(
                    MarketplaceSessionLookupKind.Failed,
                    null,
                    null,
                    "The license server returned an unreadable response.");
            }
        }

        if (ok && !string.IsNullOrWhiteSpace(licenseKey))
        {
            return new MarketplaceSessionLookup(
                MarketplaceSessionLookupKind.Found,
                licenseKey.Trim(),
                string.IsNullOrWhiteSpace(productId) ? null : productId.Trim(),
                message);
        }

        if (pending || status == HttpStatusCode.NotFound)
        {
            return new MarketplaceSessionLookup(
                MarketplaceSessionLookupKind.Pending,
                null,
                productId,
                message ?? "License not ready yet — the purchase may still be processing.");
        }

        var detail = error ?? message;
        if (string.IsNullOrWhiteSpace(detail))
            detail = $"License lookup failed ({(int)status}).";
        return new MarketplaceSessionLookup(MarketplaceSessionLookupKind.Failed, null, productId, detail);
    }

    private static string? ReadString(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }

    private static bool? ReadBool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}

/// <summary>
/// Calls the existing public license-by-session Edge function. It does not activate a device.
/// </summary>
public sealed class SupabaseLicenseSessionClient : IMarketplaceSessionClient
{
    private readonly HttpClient _http;
    private readonly IMarketplaceSupabaseSettings _settings;

    public SupabaseLicenseSessionClient(HttpClient http, IMarketplaceSupabaseSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<MarketplaceSessionLookup> LookupAsync(string checkoutSessionId, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            return new MarketplaceSessionLookup(
                MarketplaceSessionLookupKind.NotConfigured,
                null,
                null,
                "Configure Supabase URL and anon key under Sign in before this PC can pick up a license automatically. You can still paste a key.");
        }

        var sessionId = (checkoutSessionId ?? "").Trim();
        if (CheckoutSessionIds.TryExtract(sessionId) is null)
        {
            return new MarketplaceSessionLookup(
                MarketplaceSessionLookupKind.Failed,
                null,
                null,
                "That checkout session id is not valid.");
        }

        var url = $"{_settings.Url.TrimEnd('/')}/functions/v1/license-by-session";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);
            request.Headers.TryAddWithoutValidation("apikey", _settings.AnonKey);
            request.Content = JsonContent.Create(new { checkoutSessionId = sessionId });
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return MarketplaceSessionLookupParser.Parse(response.StatusCode, payload);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new MarketplaceSessionLookup(
                MarketplaceSessionLookupKind.Failed,
                null,
                null,
                "The license server could not be reached. Paste the key from your email when you have it.");
        }
    }
}
