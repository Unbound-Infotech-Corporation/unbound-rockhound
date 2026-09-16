using System.Globalization;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Geology;
using GeoMineralTrace.Core.Map;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Infrastructure.Geology;
using GeoMineralTrace.Rockhounding.Storage;
using GeoMineralTrace_App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace GeoMineralTrace_App.Pages;

public sealed partial class MapPage
{
    private CancellationTokenSource? _prospectCts;

    private async Task RunProspectGuessAsync()
    {
        if (_selection is null)
            return;

        _prospectCts?.Cancel();
        _prospectCts?.Dispose();
        _prospectCts = new CancellationTokenSource();
        var ct = _prospectCts.Token;

        ProspectGuessText.Visibility = Visibility.Visible;
        ProspectGuessText.Text = "Prospect Guess — looking up geologic unit and nearby records…";

        var lat = _selection.Latitude;
        var lon = _selection.Longitude;
        var online = AppPreferences.OnlineEnrichmentAllowed;
        var theme = MapLayerPreferences.CngmTheme;

        GeologicMapUnit? unit = null;
        string? status = null;
        var usedOnline = false;

        try
        {
            if (online)
            {
                try
                {
                    var client = App.Services.GetRequiredService<CngmIdentifyClient>();
                    unit = await client.IdentifyAsync(new GeoCoordinate(lat, lon), theme, ct)
                        .ConfigureAwait(true);
                    usedOnline = true;
                    if (unit is null)
                        status = "NGMDB identify returned no unit at this point (or the service was unavailable). Nearby locality minerals still apply.";
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    status = "Geologic identify failed: " + ex.Message + " Nearby locality minerals still apply.";
                }
            }
            else
            {
                status = "Online enrichment is off — geologic tiles and NGMDB identify are skipped. Local corridor minerals only.";
            }

            IReadOnlyList<NearbyMineralOccurrence> nearby = [];
            try
            {
                nearby = await LoadNearbyOccurrencesAsync(new GeoCoordinate(lat, lon), ct)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                status = string.IsNullOrWhiteSpace(status)
                    ? "Nearby locality lookup failed: " + ex.Message
                    : status + " Nearby lookup failed: " + ex.Message;
            }

            if (ct.IsCancellationRequested)
                return;

            var result = ProspectGuessScorer.Score(unit, nearby, usedOnline, status);
            var panel = ProspectGuessScorer.FormatPanel(result);
            ProspectGuessText.Text = panel;

            if (unit is not null && (_selection.Kind is "tap" or "home" || string.IsNullOrWhiteSpace(_selection.Kind)))
            {
                SelectedTitle.Text = unit.DisplayTitle;
                SelectedSubtitle.Text =
                    $"{lat.ToString("F5", CultureInfo.InvariantCulture)}, {lon.ToString("F5", CultureInfo.InvariantCulture)}"
                    + " · CNGM " + unit.ThemeLabel
                    + (string.IsNullOrWhiteSpace(unit.GeoMaterial) ? "" : " · " + unit.GeoMaterial);
            }
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer tap
        }
        catch (Exception ex)
        {
            ProspectGuessText.Visibility = Visibility.Visible;
            ProspectGuessText.Text =
                "Prospect Guess could not finish: " + ex.Message + Environment.NewLine + Environment.NewLine
                + ProspectGuessResult.StandardDisclaimer;
        }
    }

    private static async Task<IReadOnlyList<NearbyMineralOccurrence>> LoadNearbyOccurrencesAsync(
        GeoCoordinate center,
        CancellationToken cancellationToken)
    {
        var store = App.Services.GetRequiredService<LocalityStore>();
        await store.InitializeAsync().ConfigureAwait(true);
        var hits = await store.FindNearAsync(center, ProspectGuessScorer.NearbyRadiusKm, cancellationToken)
            .ConfigureAwait(true);

        return hits
            .Where(l => l.Coordinates is { } c && c.IsValid)
            .Select(l =>
            {
                var km = LocalityStore.HaversineKm(center, l.Coordinates!.Value);
                var usgs = IsUsgsRecord(l);
                return new NearbyMineralOccurrence(
                    l.Name,
                    l.SourceDataset ?? (usgs ? "USGS" : "curated"),
                    l.ReportedMinerals,
                    km,
                    IsCurated: !usgs);
            })
            .OrderBy(n => n.DistanceKm)
            .Take(40)
            .ToList();
    }

    private static bool IsUsgsRecord(Locality locality)
    {
        if ((locality.SourceDataset ?? "").Contains("USGS", StringComparison.OrdinalIgnoreCase))
            return true;
        return (locality.ExternalId ?? "").StartsWith("usgs-", StringComparison.OrdinalIgnoreCase);
    }
}
