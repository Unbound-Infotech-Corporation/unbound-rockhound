using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Geology;
using GeoMineralTrace.Core.Map;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace GeoMineralTrace.Infrastructure.Geology;

/// <summary>
/// Point-identify against public CNGM FeatureServer layers. Short-lived memory cache; fails soft.
/// </summary>
public sealed class CngmIdentifyClient
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(12);
    private const int MaxCacheEntries = 256;

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CngmIdentifyClient>? _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public CngmIdentifyClient(IHttpClientFactory httpFactory, ILogger<CngmIdentifyClient>? logger = null)
    {
        _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        _logger = logger;
    }

    public async Task<GeologicMapUnit?> IdentifyAsync(
        GeoCoordinate coordinate,
        CngmTheme theme,
        CancellationToken cancellationToken = default)
    {
        if (!coordinate.IsValid)
            return null;

        var key = CacheKey(theme, coordinate);
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTimeOffset.UtcNow)
            return cached.Unit;

        try
        {
            var url = UsgsMapOverlayEndpoints.BuildIdentifyQueryUrl(
                theme,
                coordinate.LatitudeDegrees,
                coordinate.LongitudeDegrees);
            var http = _httpFactory.CreateClient("usgs-cngm");
            using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("CNGM identify HTTP {Status} for {Theme}", (int)response.StatusCode, theme);
                return Cache(key, null);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var unit = CngmIdentifyJsonParser.Parse(json, theme);
            if (unit is { IsEmpty: true })
                unit = null;
            return Cache(key, unit);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogInformation(ex, "CNGM identify failed (soft) for {Theme}", theme);
            return null;
        }
    }

    private GeologicMapUnit? Cache(string key, GeologicMapUnit? unit)
    {
        if (_cache.Count >= MaxCacheEntries)
        {
            foreach (var stale in _cache)
            {
                if (stale.Value.ExpiresUtc <= DateTimeOffset.UtcNow)
                    _cache.TryRemove(stale.Key, out _);
            }
        }

        _cache[key] = new CacheEntry(DateTimeOffset.UtcNow.Add(CacheTtl), unit);
        return unit;
    }

    private static string CacheKey(CngmTheme theme, GeoCoordinate coordinate)
    {
        // ~11 m grid — enough to reuse taps without mixing adjacent units at synthesis scale.
        var lat = Math.Round(coordinate.LatitudeDegrees, 4, MidpointRounding.AwayFromZero)
            .ToString("0.0000", CultureInfo.InvariantCulture);
        var lon = Math.Round(coordinate.LongitudeDegrees, 4, MidpointRounding.AwayFromZero)
            .ToString("0.0000", CultureInfo.InvariantCulture);
        return $"{theme}:{lat},{lon}";
    }

    private readonly record struct CacheEntry(DateTimeOffset ExpiresUtc, GeologicMapUnit? Unit);
}
