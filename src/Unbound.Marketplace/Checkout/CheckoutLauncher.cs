using System.Net.Http.Json;
using System.Text.Json;

namespace Unbound.Marketplace.Checkout;

public sealed record CheckoutLaunchResult(bool Ok, string? CheckoutUrl, string? SessionId, string? Error);

public interface IExternalBrowser
{
    void Open(Uri url);
}

/// <summary>Opens https links in the operating system default browser. No card data is handled here.</summary>
public sealed class ShellExternalBrowser : IExternalBrowser
{
    public void Open(Uri url)
    {
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only https links can be opened.");

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }
}

public interface ICheckoutLauncher
{
    Task<CheckoutLaunchResult> LaunchAsync(MarketplaceProduct product, CancellationToken cancellationToken = default);
}

public static class CheckoutResponseParser
{
    public static bool TryReadUrl(string json, out string? url, out string? error)
    {
        url = null;
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Checkout response was not a JSON object.";
                return false;
            }

            url = ReadUrl(root);
            if (string.IsNullOrWhiteSpace(url)
                && root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object)
            {
                url = ReadUrl(data);
            }

            if (!string.IsNullOrWhiteSpace(url))
                return true;

            error = ReadError(root) ?? "Stripe Checkout did not return a payment link.";
            return false;
        }
        catch (JsonException)
        {
            error = "Checkout response could not be read.";
            return false;
        }
    }

    public static bool IsStripeCheckoutUrl(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        var host = uri.IdnHost;
        return host.Equals("checkout.stripe.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".stripe.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadUrl(JsonElement obj)
    {
        foreach (var name in new[] { "url", "checkoutUrl", "checkout_url" })
        {
            if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }

    private static string? ReadError(JsonElement obj)
    {
        foreach (var name in new[] { "error", "message" })
        {
            if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        return null;
    }
}

public static partial class CheckoutSessionIds
{
    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![A-Za-z0-9])cs_(?:live|test)_[A-Za-z0-9]+", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SessionPattern();

    public static string? TryExtract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = SessionPattern().Match(text);
        return match.Success ? match.Value : null;
    }

    public static string Mask(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return "";
        return sessionId.Length <= 16 ? sessionId : sessionId[..14] + "…";
    }
}

/// <summary>
/// Asks the Unbound Infotech store to create a Stripe Checkout session and opens it in the browser.
/// The store function is the same one the public storefront calls: POST { product_id }.
/// </summary>
public sealed class CheckoutLauncher : ICheckoutLauncher
{
    private readonly HttpClient _http;
    private readonly MarketplaceOptions _options;
    private readonly IExternalBrowser _browser;

    public CheckoutLauncher(HttpClient http, MarketplaceOptions options, IExternalBrowser browser)
    {
        _http = http;
        _options = options;
        _browser = browser;
    }

    public async Task<CheckoutLaunchResult> LaunchAsync(MarketplaceProduct product, CancellationToken cancellationToken = default)
    {
        var status = (product.Status ?? "").Trim().Replace('-', '_');
        if (!status.Equals("active", StringComparison.OrdinalIgnoreCase)
            && !status.Equals("published", StringComparison.OrdinalIgnoreCase)
            && !status.Equals("live", StringComparison.OrdinalIgnoreCase))
        {
            return new CheckoutLaunchResult(false, null, null, "This product is not available to buy yet.");
        }

        try
        {
            using var response = await _http.PostAsJsonAsync(
                _options.CheckoutFunctionUrl,
                new Dictionary<string, string> { ["product_id"] = product.Id },
                cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!CheckoutResponseParser.TryReadUrl(payload, out var url, out var error) || string.IsNullOrWhiteSpace(url))
            {
                var message = response.IsSuccessStatusCode
                    ? error ?? "Stripe Checkout did not start."
                    : error ?? "The Unbound Infotech store could not start Stripe Checkout.";
                return new CheckoutLaunchResult(false, null, null, message);
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !CheckoutResponseParser.IsStripeCheckoutUrl(uri))
                return new CheckoutLaunchResult(false, null, null, "Checkout did not return a Stripe payment link.");

            var sessionId = CheckoutSessionIds.TryExtract(url);
            try
            {
                _browser.Open(uri);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            {
                return new CheckoutLaunchResult(
                    false,
                    url,
                    sessionId,
                    "Stripe Checkout was created, but the default browser did not open. Paste the checkout session id from your receipt when payment finishes.");
            }

            return new CheckoutLaunchResult(true, url, sessionId, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new CheckoutLaunchResult(false, null, null, "Could not reach Unbound Infotech checkout. Check your connection and try again.");
        }
    }
}
