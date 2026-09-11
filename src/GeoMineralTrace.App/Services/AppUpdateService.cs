using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Updates;

namespace GeoMineralTrace_App.Services;

/// <summary>
/// Fetches a hosted <see cref="AppReleaseManifest"/> and compares it to the running build.
/// Network use is gated by <see cref="Helpers.AppPreferences.CheckForUpdatesAllowed"/>.
/// </summary>
public sealed class AppUpdateService
{
    public const string DefaultProductId = AppBranding.UpdateProductId;

    /// <summary>
    /// Default public feed. Replace with your CDN / GitHub Releases raw URL before shipping.
    /// </summary>
    public const string DefaultFeedUrl = AppBranding.DefaultUpdateFeedUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppBranding.UserAgentPrefix}/0.6");
        return client;
    }

    public string LocalVersionDisplay
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return AppVersionComparer.FormatDisplay(v);
        }
    }

    public async Task<AppUpdateCheckResult> CheckAsync(CancellationToken ct = default, bool force = false)
    {
        if (!force && !Helpers.AppPreferences.CheckForUpdatesAllowed)
        {
            return AppUpdateCheckResult.Skipped(
                "Update checks are off in Settings. Turn on “Check for updates” to look for new releases.");
        }

        var feedUrl = Helpers.AppPreferences.UpdateFeedUrl ?? DefaultFeedUrl;
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            return AppUpdateCheckResult.Failed("Update feed URL is empty.");
        }

        try
        {
            AppReleaseManifest? manifest;
            if (feedUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                || Path.IsPathRooted(feedUrl))
            {
                var path = feedUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(feedUrl).LocalPath
                    : feedUrl;
                await using var stream = File.OpenRead(path);
                manifest = await JsonSerializer.DeserializeAsync<AppReleaseManifest>(stream, JsonOptions, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                using var response = await Http.GetAsync(feedUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return AppUpdateCheckResult.Failed(
                        $"Update feed returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
                }

                manifest = await response.Content.ReadFromJsonAsync<AppReleaseManifest>(JsonOptions, ct)
                    .ConfigureAwait(false);
            }

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
                return AppUpdateCheckResult.Failed("Update feed JSON was empty or missing Version.");

            if (!string.IsNullOrWhiteSpace(manifest.ProductId)
                && !string.Equals(manifest.ProductId, DefaultProductId, StringComparison.OrdinalIgnoreCase))
            {
                return AppUpdateCheckResult.Failed(
                    $"Feed productId '{manifest.ProductId}' does not match '{DefaultProductId}'.");
            }

            var local = LocalVersionDisplay;
            var dismissed = Helpers.AppPreferences.DismissedUpdateVersion;
            if (!string.IsNullOrWhiteSpace(dismissed)
                && string.Equals(dismissed.Trim(), manifest.Version.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return AppUpdateCheckResult.UpToDate(local, manifest,
                    $"Update {manifest.Version} was dismissed. Clear dismissal in Settings to see it again.");
            }

            if (!AppVersionComparer.IsNewer(manifest.Version, local))
            {
                return AppUpdateCheckResult.UpToDate(local, manifest);
            }

            return AppUpdateCheckResult.Available(local, manifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AppUpdateCheckResult.Failed($"Could not reach update feed: {ex.Message}");
        }
    }
}

public sealed class AppUpdateCheckResult
{
    private AppUpdateCheckResult(
        AppUpdateCheckStatus status,
        string localVersion,
        AppReleaseManifest? manifest,
        string message)
    {
        Status = status;
        LocalVersion = localVersion;
        Manifest = manifest;
        Message = message;
    }

    public AppUpdateCheckStatus Status { get; }
    public string LocalVersion { get; }
    public AppReleaseManifest? Manifest { get; }
    public string Message { get; }

    public bool HasUpdate => Status == AppUpdateCheckStatus.UpdateAvailable;

    public static AppUpdateCheckResult Available(string local, AppReleaseManifest manifest) =>
        new(AppUpdateCheckStatus.UpdateAvailable, local, manifest,
            manifest.Title ?? $"Version {manifest.Version} is available (you have {local}).");

    public static AppUpdateCheckResult UpToDate(string local, AppReleaseManifest? manifest, string? note = null) =>
        new(AppUpdateCheckStatus.UpToDate, local, manifest,
            note ?? $"You are on the latest version ({local}).");

    public static AppUpdateCheckResult Skipped(string message) =>
        new(AppUpdateCheckStatus.Skipped, "", null, message);

    public static AppUpdateCheckResult Failed(string message) =>
        new(AppUpdateCheckStatus.Failed, "", null, message);
}

public enum AppUpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    Skipped,
    Failed
}
