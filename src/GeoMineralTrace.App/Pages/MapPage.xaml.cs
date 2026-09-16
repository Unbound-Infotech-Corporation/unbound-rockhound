using System.Globalization;
using System.Text;
using System.Text.Json;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Claims.Storage;
using GeoMineralTrace.Core.Claims;
using GeoMineralTrace.Core.Finds;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Hydrology;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Core.Map;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Core.Trails;
using GeoMineralTrace.Hydrology.Import;
using GeoMineralTrace.Hydrology.Storage;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace.Infrastructure.Social;
using GeoMineralTrace.Reporting.Kml;
using GeoMineralTrace.Rockhounding.Storage;
using GeoMineralTrace_App.Helpers;
using GeoMineralTrace_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GeoMineralTrace_App.Pages;

public sealed partial class MapPage : Page
{
    private readonly AnalysisSessionContext _session;
    private bool _webViewReady;
    private bool _layerUiReady;
    private MapSnapshot _lastSnapshot = MapSnapshot.Empty;
    private MapPinSelection? _selection;
    private MapFocusRequest? _focusRequest;

    public MapPage()
    {
        InitializeComponent();
        _session = App.Services.GetRequiredService<AnalysisSessionContext>();
        Loaded += MapPage_Loaded;

        BtnGoogleEarth.Click += (_, _) => _ = OpenEarthForSelectionAsync();
        BtnMapsView.Click += (_, _) => OpenMapsForSelection();
        BtnMapsDirections.Click += (_, _) => OpenDirectionsForSelection();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _focusRequest = e.Parameter as MapFocusRequest ?? MapFocusService.ConsumePending();
        if (!_webViewReady)
            return;

        try
        {
            await RefreshMapAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowMapError("Map refresh failed: " + ex.Message);
        }
    }

    private async void MapPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SyncLayerTogglesFromPreferences();
            await EnsureMapWebViewAsync().ConfigureAwait(true);
            await RefreshMapAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowMapError("Map failed to start: " + ex.Message);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureMapWebViewAsync().ConfigureAwait(true);
            await RefreshMapAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowMapError("Map refresh failed: " + ex.Message);
        }
    }

    private async void ImportTrails_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance));
            picker.FileTypeFilter.Add(".geojson");
            picker.FileTypeFilter.Add(".json");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            var store = App.Services.GetRequiredService<TrailStore>();
            await store.InitializeAsync().ConfigureAwait(true);
            var importer = App.Services.GetRequiredService<TrailsGeoJsonImporter>();
            var result = await importer.ImportFileAsync(file.Path, store).ConfigureAwait(true);

            await new ContentDialog
            {
                Title = "Trail import complete",
                Content =
                    $"Imported {result.Imported} named trails from {result.FeaturesRead} features.\n" +
                    $"Skipped unnamed: {result.SkippedUnnamed}. Skipped geometry: {result.SkippedGeometry}.\n\n" +
                    "Turn on the Access & hiking trails layer and refresh if needed.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();

            await RefreshMapAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Trail import failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private async void LayerToggled(object sender, RoutedEventArgs e)
    {
        if (!_layerUiReady)
            return;

        SaveLayerTogglesToPreferences();
        try
        {
            await RefreshMapAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowMapError("Map refresh failed: " + ex.Message);
        }
    }

    private void DismissSelection_Click(object sender, RoutedEventArgs e) => ClearSelection();

    private async void ExportVisible_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastSnapshot.IsEmpty)
                await RefreshMapAsync().ConfigureAwait(true);

            var findsStore = App.Services.GetRequiredService<PersonalFindStore>();
            await findsStore.InitializeAsync().ConfigureAwait(true);
            var finds = await findsStore.ListAsync().ConfigureAwait(true);

            var sessionMarkers = _lastSnapshot.Markers
                .Where(m => m.Kind is "hypothesis" or "hypothesis_lead" or "hypothesis_runner" or "locus" or "peak")
                .Select(m => (m.Label, m.Lat, m.Lon, m.Kind, (string?)null));

            var doc = KmlCatalogBuilder.BuildVisibleExport(
                $"{AppBranding.ProductName} — map export",
                _lastSnapshot.Localities,
                _lastSnapshot.Claims,
                finds,
                _lastSnapshot.Rivers,
                sessionMarkers);

            if (_lastSnapshot.Rumoured.Count > 0)
            {
                var rumourFolder = new KmlFolder { Name = "Rumoured (unverified)", Visible = true };
                foreach (var r in _lastSnapshot.Rumoured)
                {
                    if (r.Coordinates is not { } c || !c.IsValid)
                        continue;
                    rumourFolder.Placemarks.Add(new KmlPlacemark
                    {
                        Name = r.Name + " (rumoured)",
                        Latitude = c.LatitudeDegrees,
                        Longitude = c.LongitudeDegrees,
                        DescriptionHtml = $"<p>{System.Net.WebUtility.HtmlEncode(r.SourceLabel ?? r.SourceType.ToString())}</p>",
                        StyleId = "default"
                    });
                }

                doc.Folders.Add(rumourFolder);
            }

            var total = doc.Folders.Sum(f => f.Placemarks.Count);
            if (total == 0)
            {
                await new ContentDialog
                {
                    Title = "Nothing to export",
                    Content = "No plotted markers yet. Enable map layers or run Analyze to add session overlays.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
                return;
            }

            await GoogleEarthLauncher.OpenInGoogleEarthAsync(doc, XamlRoot, "map-visible").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Export failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private async Task EnsureMapWebViewAsync()
    {
        if (_webViewReady && MapView.CoreWebView2 is not null)
            return;

        await MapView.EnsureCoreWebView2Async();
        if (MapView.CoreWebView2 is null)
            throw new InvalidOperationException("WebView2 runtime is not available.");

        MapView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        MapView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webViewReady = true;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
                return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "marker")
                return;

            var lat = root.GetProperty("lat").GetDouble();
            var lon = root.GetProperty("lon").GetDouble();
            var label = root.TryGetProperty("label", out var labelProp) ? labelProp.GetString() ?? "Location" : "Location";
            var kind = root.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() ?? "" : "";
            var detail = root.TryGetProperty("detail", out var detailProp) ? detailProp.GetString() : null;

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                _selection = new MapPinSelection(lat, lon, label, kind, detail);
                SelectedTitle.Text = label;
                SelectedSubtitle.Text = string.IsNullOrWhiteSpace(detail)
                    ? $"{lat.ToString("F5", CultureInfo.InvariantCulture)}, {lon.ToString("F5", CultureInfo.InvariantCulture)} · {KindLabel(kind)}"
                    : detail;
                SelectionBar.Visibility = Visibility.Visible;
            });
        }
        catch
        {
            // Ignore malformed messages from the map.
        }
    }

    private async Task RefreshMapAsync()
    {
        _lastSnapshot = await BuildSnapshotAsync().ConfigureAwait(true);
        MapView.NavigateToString(BuildHtml(_lastSnapshot));
        _focusRequest = null;
    }

    private void ShowMapError(string message)
    {
        var safe = System.Net.WebUtility.HtmlEncode(message);
        MapView.NavigateToString(
            "<!DOCTYPE html><html><body style='background:#0f0f12;color:#e4e4e7;font:14px Segoe UI;padding:24px'>"
            + safe
            + "</body></html>");
    }

    private async Task<MapSnapshot> BuildSnapshotAsync()
    {
        var markers = new List<MapMarker>();
        var localities = new List<Locality>();
        var claims = new List<MiningClaim>();
        var rumoured = new List<RumouredSite>();
        var result = _session.LastPipelineResult ?? AnalysisPage.LastResult;
        GeoCoordinate? analysisFocus = null;
        var mineralFilter = _focusRequest?.MineralFilter?.Trim();
        var mineralHunt = !string.IsNullOrWhiteSpace(mineralFilter);
        var radiusMiles = _focusRequest?.RadiusMiles ?? HomeLocationPreferences.SearchRadiusMiles;
        if (mineralHunt && _focusRequest?.RadiusMiles is null)
            radiusMiles = 100;
        var radiusKm = radiusMiles * 1.609344;
        var hasHome = HomeLocationPreferences.TryGetHomeCoordinate(out var home);
        DispatcherQueue.TryEnqueue(() =>
        {
            var baseCaption = HomeLocationPreferences.StatusSummary()
                + " Toggle layers, tap a pin for Google Earth or Maps.";
            MapScopeCaption.Text = string.IsNullOrWhiteSpace(_focusRequest?.CaptionHint)
                ? baseCaption
                : _focusRequest!.CaptionHint + " · " + baseCaption;
        });

        // Mineral hunt: center on focus/home and only plot matching localities (+ home).
        if (mineralHunt)
        {
            GeoCoordinate huntCenter;
            if (_focusRequest is { } fr)
                huntCenter = new GeoCoordinate(fr.Latitude, fr.Longitude);
            else if (hasHome)
                huntCenter = home;
            else
                huntCenter = new GeoCoordinate(39.5, -98.35);

            if (hasHome)
            {
                var label = HomeLocationPreferences.HomeLocationLabel;
                markers.Add(new MapMarker(
                    home.LatitudeDegrees,
                    home.LongitudeDegrees,
                    string.IsNullOrWhiteSpace(label) ? "Home location" : $"⌂ {label}",
                    "#F97316",
                    "home",
                    $"Search center · {radiusMiles:0.#} mi radius",
                    10));
            }

            await AddLocalitiesNearAsync(
                markers,
                localities,
                huntCenter,
                radiusKm,
                limit: 500,
                mineralFilter: mineralFilter,
                emphasizeMineral: true).ConfigureAwait(false);

            return new MapSnapshot(
                markers,
                [],
                [],
                localities,
                [],
                [],
                _focusRequest);
        }

        if (result is not null && MapLayerPreferences.IsVisible(MapLayerKind.Hypothesis))
        {
            var ordered = HypothesisPresentation.OrderForDisplay(result.Hypotheses)
                .Where(h => h.Center is not null)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var h = ordered[i];
                var isLead = i == 0;
                analysisFocus ??= h.Center;
                var kind = isLead ? "hypothesis_lead" : "hypothesis_runner";
                var color = isLead ? "#E11D48" : "#6366F1";
                var radius = isLead ? 12.0 : 7.0;
                var detail = $"{h.Confidence.Value:P0} · {HypothesisPresentation.BuildReasoningSummary(h)}";
                markers.Add(new MapMarker(
                    h.Center!.Value.LatitudeDegrees,
                    h.Center.Value.LongitudeDegrees,
                    isLead ? $"★ {h.Label}" : h.Label,
                    color,
                    kind,
                    detail,
                    radius,
                    Id: h.Id.ToString(),
                    Highlight: _focusRequest?.HighlightHypothesisId == h.Id));
            }
        }

        if (result is not null)
        {
            try
            {
                var locStore = App.Services.GetRequiredService<LocalityStore>();
                await locStore.InitializeAsync().ConfigureAwait(true);

                foreach (var n in result.NearbyLocalities.Take(80))
                {
                    if (n.Latitude is not { } lat || n.Longitude is not { } lon)
                        continue;

                    var full = await locStore.GetByIdAsync(n.LocalityId).ConfigureAwait(true);
                    var kind = full is not null ? LocalityMarkerKind(full) : "locality_curated";
                    if (!MapLayerPreferences.MatchesMarkerKind(kind))
                        continue;

                    markers.Add(new MapMarker(
                        lat,
                        lon,
                        n.HumanActivityDataMayBeOutdated
                            ? $"{n.Name} ({n.StateCode}) · {n.AccessStatus} · outdated"
                            : $"{n.Name} ({n.StateCode}) · {n.AccessStatus}",
                        n.HumanActivityDataMayBeOutdated ? "#CA8A04" : LocalityColor(kind),
                        kind,
                        BuildLocalityDetail(full, n.Name),
                        7));

                    if (full is not null && localities.All(x => x.Id != full.Id))
                        localities.Add(full);
                }
            }
            catch
            {
                foreach (var n in result.NearbyLocalities.Take(80))
                {
                    if (n.Latitude is not { } lat || n.Longitude is not { } lon)
                        continue;
                    var kind = "locality_curated";
                    if (!MapLayerPreferences.MatchesMarkerKind(kind))
                        continue;

                    markers.Add(new MapMarker(lat, lon, $"{n.Name} ({n.StateCode})", "#22C55E", kind, null, 7));
                }
            }
        }

        var locus = _session.LastSolarLocus ?? result?.SolarLocus;
        if (locus is not null && MapLayerPreferences.IsVisible(MapLayerKind.SolarLocus))
        {
            foreach (var cell in locus.Cells.Where(c => c.RelativeProbability > 0.65).Take(80))
            {
                analysisFocus ??= cell.Center;
                markers.Add(new MapMarker(
                    cell.Center.LatitudeDegrees,
                    cell.Center.LongitudeDegrees,
                    $"Solar locus p={cell.RelativeProbability:F2}",
                    "#F59E0B",
                    "locus",
                    "Analysis solar probability cell",
                    6));
            }

            if (locus.PeakProbabilityCell is { } peak)
            {
                analysisFocus ??= peak;
                markers.Add(new MapMarker(
                    peak.LatitudeDegrees,
                    peak.LongitudeDegrees,
                    "Peak solar locus",
                    "#EF4444",
                    "peak",
                    "Highest relative solar locus cell",
                    9));
            }
        }

        // Catalog layers (claims, localities, rivers, trails) center on home when set + preferred.
        GeoCoordinate? catalogCenter = null;
        if (hasHome && HomeLocationPreferences.PreferHomeLocationOnMap)
            catalogCenter = home;
        else if (analysisFocus is { } af)
            catalogCenter = af;
        else if (hasHome)
            catalogCenter = home;

        if (hasHome)
        {
            var label = HomeLocationPreferences.HomeLocationLabel;
            markers.Add(new MapMarker(
                home.LatitudeDegrees,
                home.LongitudeDegrees,
                string.IsNullOrWhiteSpace(label) ? "Home location" : $"⌂ {label}",
                "#F97316",
                "home",
                $"Search center · {HomeLocationPreferences.SearchRadiusMiles:0.#} mi radius",
                10));
        }

        if (catalogCenter is { } center)
        {
            // Higher limits for a 100-mile window; stores still bbox-prefilter.
            await AddClaimsNearAsync(markers, claims, center, radiusKm, limit: 400).ConfigureAwait(false);
            await AddLocalitiesNearAsync(markers, localities, center, radiusKm, limit: 400).ConfigureAwait(false);
            await AddRumouredNearAsync(markers, rumoured, center, radiusKm, limit: 200).ConfigureAwait(false);
        }
        else
        {
            await AddLiveCatalogAsync(markers, localities, claims, rumoured).ConfigureAwait(false);
        }

        await AddPersonalFindsAsync(markers).ConfigureAwait(false);
        await AddTripWaypointsAsync(markers).ConfigureAwait(false);
        await AddCommunityRedditMentionsAsync(markers, catalogCenter, radiusKm).ConfigureAwait(false);

        var polylines = new List<MapPolyline>();
        var rivers = new List<Watercourse>();
        await AddRiversAsync(polylines, rivers, catalogCenter, radiusKm).ConfigureAwait(false);
        await AddTrailsAsync(polylines, catalogCenter, radiusKm).ConfigureAwait(false);

        return new MapSnapshot(
            markers.Where(m => MapLayerPreferences.MatchesMarkerKind(m.Kind) || m.Kind == "home").ToList(),
            polylines.Where(p =>
                (p.Kind == "river" && MapLayerPreferences.IsVisible(MapLayerKind.River))
                || (p.Kind == "trail" && MapLayerPreferences.IsVisible(MapLayerKind.Trail))).ToList(),
            rivers,
            localities,
            claims,
            rumoured,
            _focusRequest);
    }

    private static async Task AddTripWaypointsAsync(List<MapMarker> markers)
    {
        try
        {
            var store = App.Services.GetRequiredService<TripStore>();
            var trips = await store.ListTripsAsync().ConfigureAwait(false);
            foreach (var trip in trips)
            {
                var waypoints = await store.ListWaypointsAsync(trip.Id).ConfigureAwait(false);
                foreach (var w in waypoints)
                {
                    var detail = w.SourceAnalysisSessionId is not null
                        ? $"Trip waypoint · from video · {w.Notes ?? trip.Name}"
                        : $"Trip waypoint · {trip.Name}";
                    markers.Add(new MapMarker(
                        w.Latitude,
                        w.Longitude,
                        w.Name,
                        "#10B981",
                        "trip_waypoint",
                        detail,
                        7));
                }
            }
        }
        catch
        {
            // optional
        }
    }

    private static async Task AddLiveCatalogAsync(
        List<MapMarker> markers,
        List<Locality> localities,
        List<MiningClaim> claims,
        List<RumouredSite> rumoured)
    {
        try
        {
            var locStore = App.Services.GetRequiredService<LocalityStore>();
            await locStore.InitializeAsync().ConfigureAwait(false);
            foreach (var l in (await locStore.ListWithCoordinatesAsync(280).ConfigureAwait(false)).Take(280))
            {
                if (l.Coordinates is null)
                    continue;
                var kind = LocalityMarkerKind(l);
                if (!MapLayerPreferences.MatchesMarkerKind(kind))
                    continue;
                if (localities.Any(x => x.Id == l.Id))
                    continue;
                localities.Add(l);
                markers.Add(new MapMarker(
                    l.Coordinates.Value.LatitudeDegrees,
                    l.Coordinates.Value.LongitudeDegrees,
                    $"{l.Name} ({l.StateCode})",
                    LocalityColor(kind),
                    kind,
                    BuildLocalityDetail(l, l.Name),
                    6));
            }
        }
        catch
        {
            // optional
        }

        try
        {
            var claimStore = App.Services.GetRequiredService<ClaimStore>();
            await claimStore.InitializeAsync().ConfigureAwait(false);
            var sample = await claimStore.QueryAsync(status: ClaimStatus.Active, limit: 120).ConfigureAwait(false);
            if (sample.Count == 0)
                sample = await claimStore.QueryAsync(limit: 120).ConfigureAwait(false);
            foreach (var c in sample.Where(c => c.Coordinates is not null))
            {
                var kind = ClaimMarkerKind(c);
                if (!MapLayerPreferences.MatchesMarkerKind(kind))
                    continue;
                claims.Add(c);
                markers.Add(ClaimMarker(c, kind));
            }
        }
        catch
        {
            // optional
        }

        try
        {
            var rumourStore = App.Services.GetRequiredService<RumouredSiteStore>();
            await rumourStore.InitializeAsync().ConfigureAwait(false);
            foreach (var r in await rumourStore.ListWithCoordinatesAsync(120).ConfigureAwait(false))
            {
                if (r.Coordinates is null || !MapLayerPreferences.IsVisible(MapLayerKind.Rumoured))
                    continue;
                rumoured.Add(r);
                markers.Add(RumourMarker(r));
            }
        }
        catch
        {
            // optional
        }
    }

    private static async Task AddClaimsNearAsync(
        List<MapMarker> markers,
        List<MiningClaim> claims,
        GeoCoordinate center,
        double radiusKm,
        int limit)
    {
        try
        {
            var claimStore = App.Services.GetRequiredService<ClaimStore>();
            await claimStore.InitializeAsync().ConfigureAwait(false);
            var nearClaims = await claimStore.FindNearAsync(center, radiusKm, limit).ConfigureAwait(false);
            foreach (var c in nearClaims.Where(c => c.Coordinates is not null))
            {
                var kind = ClaimMarkerKind(c);
                if (!MapLayerPreferences.MatchesMarkerKind(kind))
                    continue;
                if (claims.Any(x => x.Id == c.Id))
                    continue;
                claims.Add(c);
                markers.Add(ClaimMarker(c, kind));
            }
        }
        catch
        {
            // optional
        }
    }

    private static async Task AddLocalitiesNearAsync(
        List<MapMarker> markers,
        List<Locality> localities,
        GeoCoordinate center,
        double radiusKm,
        int limit,
        string? mineralFilter = null,
        bool emphasizeMineral = false)
    {
        try
        {
            var locStore = App.Services.GetRequiredService<LocalityStore>();
            await locStore.InitializeAsync().ConfigureAwait(false);
            var near = await locStore.FindNearAsync(center, radiusKm).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(mineralFilter))
            {
                near = near
                    .Where(l => l.ReportedMinerals.Any(m =>
                        m.Contains(mineralFilter, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            foreach (var l in near.Take(limit))
            {
                if (l.Coordinates is null)
                    continue;
                var kind = LocalityMarkerKind(l);
                if (!emphasizeMineral && !MapLayerPreferences.MatchesMarkerKind(kind))
                    continue;
                if (localities.Any(x => x.Id == l.Id))
                    continue;
                localities.Add(l);
                var mineralHit = !string.IsNullOrWhiteSpace(mineralFilter)
                    && l.ReportedMinerals.Any(m => m.Contains(mineralFilter, StringComparison.OrdinalIgnoreCase));
                var title = mineralHit
                    ? $"◆ {l.Name} ({l.StateCode}) · {mineralFilter}"
                    : $"{l.Name} ({l.StateCode})";
                markers.Add(new MapMarker(
                    l.Coordinates.Value.LatitudeDegrees,
                    l.Coordinates.Value.LongitudeDegrees,
                    title,
                    mineralHit ? "#A855F7" : LocalityColor(kind),
                    kind,
                    BuildLocalityDetail(l, l.Name),
                    mineralHit ? 9 : 6,
                    Highlight: mineralHit));
            }
        }
        catch
        {
            // optional
        }
    }

    private static async Task AddCommunityRedditMentionsAsync(
        List<MapMarker> markers,
        GeoCoordinate? focus,
        double radiusKm)
    {
        if (!MapLayerPreferences.IsVisible(MapLayerKind.CommunityReddit))
            return;

        try
        {
            var forum = App.Services.GetRequiredService<ISocialForumService>();
            var auth = App.Services.GetRequiredService<ISocialAuthService>();
            if (auth.CurrentSession is null)
                return;

            foreach (var m in await forum.ListCommunityMentionsWithCoordsAsync(200).ConfigureAwait(false))
            {
                if (m.Lat is not { } lat || m.Lon is not { } lon)
                    continue;

                if (focus is { } center)
                {
                    var dKm = ApproximateKm(center.LatitudeDegrees, center.LongitudeDegrees, lat, lon);
                    if (dKm > radiusKm)
                        continue;
                }

                var place = string.IsNullOrWhiteSpace(m.PlaceHint) ? "" : $" · {m.PlaceHint}";
                var detail =
                    $"unverified — sourced from Reddit{place} · confidence {m.Confidence:P0} · {m.SourceUrl}";
                markers.Add(new MapMarker(
                    lat,
                    lon,
                    $"◇ {Truncate(m.MentionText, 48)}",
                    "#06B6D4",
                    "community_reddit",
                    detail,
                    7,
                    "2 4"));
            }
        }
        catch
        {
            // optional — signed-out or migration not applied
        }
    }

    private static double ApproximateKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371.0;
        static double Rad(double d) => d * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2))
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static string Truncate(string text, int max)
    {
        var t = (text ?? "").Trim();
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }

    private static async Task AddRumouredNearAsync(
        List<MapMarker> markers,
        List<RumouredSite> rumoured,
        GeoCoordinate center,
        double radiusKm,
        int limit)
    {
        if (!MapLayerPreferences.IsVisible(MapLayerKind.Rumoured))
            return;

        try
        {
            var rumourStore = App.Services.GetRequiredService<RumouredSiteStore>();
            await rumourStore.InitializeAsync().ConfigureAwait(false);
            foreach (var r in await rumourStore.FindNearAsync(center, radiusKm, limit).ConfigureAwait(false))
            {
                if (r.Coordinates is null || rumoured.Any(x => x.Id == r.Id))
                    continue;
                rumoured.Add(r);
                markers.Add(RumourMarker(r));
            }
        }
        catch
        {
            // optional
        }
    }

    private static async Task AddRiversAsync(
        List<MapPolyline> polylines,
        List<Watercourse> rivers,
        GeoCoordinate? focus,
        double radiusKm)
    {
        if (!MapLayerPreferences.IsVisible(MapLayerKind.River))
            return;

        try
        {
            var riverStore = App.Services.GetRequiredService<RiverStore>();
            await riverStore.InitializeAsync().ConfigureAwait(false);

            IReadOnlyList<Watercourse> candidates;
            if (focus is { } center)
                candidates = await riverStore.FindNearAsync(center, radiusKm, limit: 80).ConfigureAwait(false);
            else
                candidates = await riverStore.ListWithGeometryAsync(60).ConfigureAwait(false);

            foreach (var r in candidates)
            {
                if (r.Vertices.Count < 2)
                    continue;

                var minerals = await riverStore.GetMineralsForWatercourseAsync(r.Id).ConfigureAwait(false);
                rivers.Add(new Watercourse
                {
                    Id = r.Id,
                    ExternalId = r.ExternalId,
                    Name = r.Name,
                    StateCode = r.StateCode,
                    County = r.County,
                    Kind = r.Kind,
                    GnisId = r.GnisId,
                    LengthKm = r.LengthKm,
                    Vertices = r.Vertices,
                    Centroid = r.Centroid,
                    Sources = r.Sources,
                    SourceDataset = r.SourceDataset,
                    SourceVintage = r.SourceVintage,
                    Minerals = minerals
                });

                var curated = minerals.Where(m => m.AssociationKind == MineralAssociationKind.Curated)
                    .Select(m => m.MineralName).Take(3);
                var mineralSummary = string.Join(", ", curated);
                var detail = string.IsNullOrWhiteSpace(mineralSummary)
                    ? $"{r.Kind} · {r.StateCode}"
                    : $"Documented: {mineralSummary}";

                polylines.Add(new MapPolyline(
                    r.Vertices.Select(v => new[] { v.LatitudeDegrees, v.LongitudeDegrees }).ToList(),
                    $"{r.Name} ({r.StateCode})",
                    "#0EA5E9",
                    "river",
                    detail));
            }
        }
        catch
        {
            // optional
        }
    }

    private static async Task AddTrailsAsync(
        List<MapPolyline> polylines,
        GeoCoordinate? focus,
        double radiusKm)
    {
        if (!MapLayerPreferences.IsVisible(MapLayerKind.Trail))
            return;

        try
        {
            var trailStore = App.Services.GetRequiredService<TrailStore>();
            await trailStore.InitializeAsync().ConfigureAwait(false);

            IReadOnlyList<Trail> candidates;
            if (focus is { } center)
                candidates = await trailStore.FindNearAsync(center, radiusKm, limit: 80).ConfigureAwait(false);
            else
                candidates = await trailStore.ListWithGeometryAsync(60).ConfigureAwait(false);

            foreach (var t in candidates)
            {
                if (t.Vertices.Count < 2)
                    continue;

                var detail = string.IsNullOrWhiteSpace(t.Notes)
                    ? $"{t.Kind} · {t.StateCode}"
                    : $"{t.Kind} · {t.Notes}";

                polylines.Add(new MapPolyline(
                    t.Vertices.Select(v => new[] { v.LatitudeDegrees, v.LongitudeDegrees }).ToList(),
                    $"{t.Name} ({t.StateCode})",
                    "#84CC16",
                    "trail",
                    detail));
            }
        }
        catch
        {
            // optional
        }
    }

    private static async Task AddPersonalFindsAsync(List<MapMarker> markers)
    {
        if (!MapLayerPreferences.IsVisible(MapLayerKind.PersonalFind))
            return;

        try
        {
            var findsStore = App.Services.GetRequiredService<PersonalFindStore>();
            await findsStore.InitializeAsync().ConfigureAwait(false);
            foreach (var f in await findsStore.ListAsync().ConfigureAwait(false))
            {
                if (f.Coordinates is not { } c || !c.IsValid)
                    continue;
                markers.Add(new MapMarker(
                    c.LatitudeDegrees,
                    c.LongitudeDegrees,
                    f.Name,
                    "#EC4899",
                    "personal_find",
                    f.Notes,
                    7));
            }
        }
        catch
        {
            // optional
        }
    }

    private static MapMarker ClaimMarker(MiningClaim c, string kind)
    {
        var color = kind switch
        {
            "claim_expiring" => "#CA8A04",
            "claim_active" => "#5E6AD2",
            _ => "#A1A1AA"
        };
        return new MapMarker(
            c.Coordinates!.Value.LatitudeDegrees,
            c.Coordinates.Value.LongitudeDegrees,
            $"{c.ClaimName} · {c.Status}",
            color,
            kind,
            $"{c.ClaimType} · {c.StateCode} · serial {c.SerialNumber}",
            kind == "claim_expiring" ? 7 : 6);
    }

    private static MapMarker RumourMarker(RumouredSite r)
    {
        var minerals = r.ReportedMinerals.Count > 0 ? string.Join(", ", r.ReportedMinerals.Take(4)) : "unknown";
        var detail = $"Rumoured · {r.SourceType} · confidence {r.Confidence:P0} · {minerals}";
        if (!string.IsNullOrWhiteSpace(r.SourceLabel))
            detail += $" · {r.SourceLabel}";
        return new MapMarker(
            r.Coordinates!.Value.LatitudeDegrees,
            r.Coordinates.Value.LongitudeDegrees,
            $"⬡ {r.Name} ({r.StateCode}) · rumoured",
            "#A855F7",
            "rumoured",
            detail,
            6,
            "4 3");
    }

    private static string LocalityMarkerKind(Locality l) =>
        l.SourceDataset?.Contains("USGS", StringComparison.OrdinalIgnoreCase) == true
            ? "locality_usgs"
            : "locality_curated";

    private static string ClaimMarkerKind(MiningClaim c) => c.Status switch
    {
        ClaimStatus.Active => "claim_active",
        ClaimStatus.ExpiringSoon => "claim_expiring",
        _ => "claim_other"
    };

    private static string LocalityColor(string kind) =>
        kind == "locality_usgs" ? "#14B8A6" : "#22C55E";

    private static string? BuildLocalityDetail(Locality? full, string fallbackName)
    {
        if (full is null)
            return null;
        var minerals = full.ReportedMinerals.Count > 0
            ? string.Join(", ", full.ReportedMinerals.Take(5))
            : "—";
        return $"{full.SourceDataset ?? "Locality"} · {full.AccessStatus} · {minerals}";
    }

    private static string KindLabel(string kind) => kind switch
    {
        "hypothesis" or "hypothesis_lead" or "hypothesis_runner" => "Analysis hypothesis",
        "locus" => "Solar locus",
        "peak" => "Peak solar locus",
        "locality_curated" => "Curated site",
        "locality_usgs" => "USGS record",
        "claim_active" => "Active claim",
        "claim_expiring" => "Expiring claim",
        "claim_other" => "Claim",
        "rumoured" => "Rumoured lead",
        "community_reddit" => "Community Reddit (unverified)",
        "river" => "Named river",
        "trail" => "Access / hiking trail",
        "home" => "Home location",
        "personal_find" => "Personal find",
        _ => "Map pin"
    };

    private string BuildHtml(MapSnapshot snapshot)
    {
        var online = AppPreferences.OnlineEnrichmentAllowed;
        var markers = snapshot.Markers;
        var polylines = snapshot.Polylines;
        var markersJson = JsonSerializer.Serialize(markers.Select(m => new
        {
            lat = m.Lat,
            lon = m.Lon,
            label = m.Label,
            color = m.Color,
            kind = m.Kind,
            detail = m.Detail,
            radius = m.Radius,
            dash = m.DashArray,
            id = m.Id,
            highlight = m.Highlight
        }));
        var polylinesJson = JsonSerializer.Serialize(polylines.Select(p => new
        {
            vertices = p.Vertices,
            label = p.Label,
            color = p.Color,
            kind = p.Kind,
            detail = p.Detail
        }));

        var counts = markers.GroupBy(m => m.Kind).ToDictionary(g => g.Key, g => g.Count());
        var riverCount = polylines.Count;
        var rumourCount = counts.GetValueOrDefault("rumoured");
        var claimCount = counts.Where(kv => kv.Key.StartsWith("claim", StringComparison.Ordinal)).Sum(kv => kv.Value);
        var localityCount = counts.Where(kv => kv.Key.StartsWith("locality", StringComparison.Ordinal)).Sum(kv => kv.Value);

        var emptyNote = markers.Count == 0 && polylines.Count == 0
            ? "No markers match your layer toggles. Set a home location in Settings, enable layers, or import seed data."
            : online
                ? $"Live map · {HomeLocationPreferences.StatusSummary()} · {markers.Count} pins · {riverCount} lines · {localityCount} sites · {claimCount} claims · {rumourCount} rumoured."
                : "Offline tiles off — enable Settings → Online enrichment for satellite, LiDAR terrain, and clustering.";

        var focus = snapshot.Focus;
        var focusJson = focus is null
            ? "null"
            : JsonSerializer.Serialize(new
            {
                lat = focus.Latitude,
                lon = focus.Longitude,
                zoom = focus.Zoom,
                highlightId = focus.HighlightHypothesisId?.ToString()
            });

        var headAssets = online
            ? """
            <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"/>
            <link rel="stylesheet" href="https://unpkg.com/leaflet.markercluster@1.5.3/dist/MarkerCluster.css"/>
            <link rel="stylesheet" href="https://unpkg.com/leaflet.markercluster@1.5.3/dist/MarkerCluster.Default.css"/>
            <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
            <script src="https://unpkg.com/leaflet.markercluster@1.5.3/dist/leaflet.markercluster.js"></script>
            <script src="https://unpkg.com/leaflet.vectorgrid@1.3.0/dist/Leaflet.VectorGrid.bundled.min.js"></script>
            """
            : """
            <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"/>
            <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
            """;

        var tileJs = online ? BuildOnlineTileJs() : """
              L.rectangle([[24.5,-125],[49.5,-66.5]],{color:'#3f3f48',weight:1,fillColor:'#18181b',fillOpacity:0.9}).addTo(map);
              """;

        var markerJs = online
            ? """
              var cluster=L.markerClusterGroup({showCoverageOnHover:false,maxClusterRadius:48,animate:true});
              var group=[];
              var highlightLayer=null;
              markers.forEach(function(m){
                var opts={radius:m.radius||6,color:m.color,fillColor:m.color,fillOpacity:m.highlight?0.95:0.78,weight:m.highlight?3:1.6};
                if(m.dash){opts.dashArray=m.dash;}
                var c=L.circleMarker([m.lat,m.lon],opts);
                var popup='<b>'+m.label+'</b>';
                if(m.detail){popup+='<br/><span style="opacity:.85">'+m.detail+'</span>';}
                popup+='<br/><button onclick="postPin('+m.lat+','+m.lon+',\''+encodeURIComponent(m.label)+'\',\''+m.kind+'\',\''+encodeURIComponent(m.detail||'')+'\')">Select for Earth / Maps</button>';
                c.bindPopup(popup);
                c.on('click',function(){postPin(m.lat,m.lon,m.label,m.kind,m.detail||'');});
                if(m.kind==='hypothesis_lead'||m.kind==='hypothesis_runner'){c.addTo(map);group.push(c);}
                else{cluster.addLayer(c);group.push(c);}
                if(m.highlight){highlightLayer=c;}
              });
              map.addLayer(cluster);
              var focus=
              """ + focusJson + """
              ;
              if(focus&&focus.lat&&focus.lon){map.flyTo([focus.lat,focus.lon],focus.zoom||11,{duration:0.85});if(highlightLayer){setTimeout(function(){highlightLayer.openPopup();},900);}}
              else if(group.length){var fg=L.featureGroup(group);map.fitBounds(fg.getBounds().pad(0.22));}
              var riverGroup=[];
              polylines.forEach(function(p){
                var line=L.polyline(p.vertices,{color:p.color,weight:4.5,opacity:0.92,lineCap:'round'});
                var popup='<b>'+p.label+'</b>';
                if(p.detail){popup+='<br/><span style="opacity:.85">'+p.detail+'</span>';}
                var mid=p.vertices[Math.floor(p.vertices.length/2)];
                if(mid){popup+='<br/><button onclick="postPin('+mid[0]+','+mid[1]+',\''+encodeURIComponent(p.label)+'\',\''+p.kind+'\',\''+encodeURIComponent(p.detail||'')+'\')">Select for Earth / Maps</button>';}
                line.bindPopup(popup);
                line.addTo(map);
                riverGroup.push(line);
              });
              if(riverGroup.length && !group.length){var fg=L.featureGroup(riverGroup);map.fitBounds(fg.getBounds().pad(0.22));}
              else if(riverGroup.length && group.length){var fg=L.featureGroup(group.concat(riverGroup));map.fitBounds(fg.getBounds().pad(0.15));}
              """
            : """
              var group=[];
              markers.forEach(function(m){
                var opts={radius:m.radius||6,color:m.color,fillColor:m.color,fillOpacity:0.78,weight:1.6};
                if(m.dash){opts.dashArray=m.dash;}
                var c=L.circleMarker([m.lat,m.lon],opts).addTo(map).bindPopup(m.label);
                c.on('click',function(){postPin(m.lat,m.lon,m.label,m.kind,m.detail||'');});
                group.push(c);
              });
              polylines.forEach(function(p){
                var line=L.polyline(p.vertices,{color:p.color,weight:4.5,opacity:0.92,lineCap:'round'}).addTo(map).bindPopup(p.label);
                var mid=p.vertices[Math.floor(p.vertices.length/2)];
                if(mid){line.on('click',function(){postPin(mid[0],mid[1],p.label,p.kind,p.detail||'');});}
                group.push(line);
              });
              if(group.length){var fg=L.featureGroup(group);map.fitBounds(fg.getBounds().pad(0.22));}
              """;

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
        sb.Append(headAssets);
        sb.Append("""
            <style>
            html,body,#map{height:100%;margin:0;background:#0f0f12}
            .note{position:absolute;z-index:1000;left:300px;top:12px;background:rgba(15,15,18,.94);color:#e4e4e7;
              padding:10px 12px;font:12px/1.45 Segoe UI,sans-serif;max-width:460px;border:1px solid #3f3f48;border-radius:10px;box-shadow:0 8px 24px rgba(0,0,0,.35)}
            .leaflet-control-layers{background:rgba(15,15,18,.92)!important;color:#e4e4e7;border:1px solid #3f3f48!important;border-radius:8px;max-height:70vh;overflow:auto}
            .leaflet-control-layers-expanded{padding:10px 12px!important;font:12px/1.4 Segoe UI,sans-serif}
            .leaflet-control-layers label{color:#e4e4e7!important}
            .leaflet-popup-content button{margin-top:8px;padding:6px 10px;border-radius:6px;border:1px solid #5e6ad2;background:#5e6ad2;color:#fff;cursor:pointer}
            .legend{position:absolute;z-index:1000;right:12px;bottom:12px;background:rgba(15,15,18,.94);color:#e4e4e7;
              padding:10px 12px;font:11px/1.5 Segoe UI,sans-serif;border:1px solid #3f3f48;border-radius:10px;max-width:220px}
            .legend b{display:block;margin-bottom:4px;font-size:12px}
            </style>
            </head><body>
            """);
        sb.Append("<div class=\"note\">").Append(System.Net.WebUtility.HtmlEncode(emptyNote)).Append("</div>");
        sb.Append("""<div class="legend"><b>Pins, lines &amp; terrain</b><span style="color:#F97316">●</span> Home<br/><span style="color:#E11D48">●</span> Leading estimate<br/><span style="color:#0EA5E9">—</span> Rivers<br/><span style="color:#84CC16">—</span> Trails<br/><span style="opacity:.8">Hillshade = DEM/LiDAR-derived relief (not raw LAS)</span></div>""");
        sb.Append("<div id=\"map\"></div><script>");
        sb.Append("function postPin(lat,lon,label,kind,detail){if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage(JSON.stringify({type:'marker',lat:lat,lon:lon,label:label,kind:kind,detail:detail}));}}");
        sb.Append("var map=L.map('map').setView([39.5,-98.35],4);");
        sb.Append(tileJs);
        sb.Append("var markers=").Append(markersJson).Append(';');
        sb.Append("var polylines=").Append(polylinesJson).Append(';');
        sb.Append(markerJs);
        sb.Append("</script></body></html>");
        return sb.ToString();
    }

    private static string BuildOnlineTileJs()
    {
        var lidarOverlayOn = MapLayerPreferences.IsVisible(MapLayerKind.LidarTerrain);
        var geologyOn = MapLayerPreferences.IsVisible(MapLayerKind.UsgsGeology);
        var cngmOn = MapLayerPreferences.IsVisible(MapLayerKind.CooperativeNationalGeology);
        var contoursOn = MapLayerPreferences.IsVisible(MapLayerKind.ElevationContours);
        var opacity = MapLayerPreferences.LidarOpacity.ToString("0.##", CultureInfo.InvariantCulture);

        // Do not use tile.openstreetmap.org — OSM volunteer tiles block WebView/desktop apps
        // with HTTP 200 "Access blocked" placeholder images (see osm.wiki/Blocked).
        var js = """
              var hillshade=null;
              var geology=null;
              var cngm=null;
              var contours=null;
              """ + $"""
              var hillOpacity={opacity};
              """ + """
              var street=L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:19,attribution:'Tiles © Esri — World Street Map'});
              var carto=L.tileLayer('https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png',{
                maxZoom:20,subdomains:'abcd',attribution:'© OpenStreetMap © CARTO'});
              var sat=L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:19,attribution:'Tiles © Esri — World Imagery'});
              var usgsLidar=L.tileLayer('https://basemap.nationalmap.gov/arcgis/rest/services/USGSShadedReliefOnly/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:16,attribution:'USGS The National Map — 3DEP shaded relief (DEM/LiDAR-derived, not raw LAS)'});
              hillshade=L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/Elevation/World_Hillshade/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:16,opacity:hillOpacity,attribution:'Esri World Hillshade (DEM/LiDAR-derived)'});
              window.__hillshade=hillshade;
              window.__setHillshadeOpacity=function(o){if(window.__hillshade){window.__hillshade.setOpacity(o);}};
              var topo=L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:19,attribution:'Tiles © Esri — World Topo'});
              var usgsTopo=L.tileLayer('https://basemap.nationalmap.gov/arcgis/rest/services/USGSTopo/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:16,attribution:'USGS The National Map — Topo'});
              var usgsImagery=L.tileLayer('https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:16,attribution:'USGS The National Map — Imagery'});
              var openTopo=L.tileLayer('https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png',{
                maxZoom:17,attribution:'© OpenStreetMap, SRTM — © OpenTopoMap (CC-BY-SA)'});
              var satRelief=L.layerGroup([
                L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',{maxZoom:19,attribution:'Tiles © Esri'}),
                L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/Elevation/World_Hillshade/MapServer/tile/{z}/{y}/{x}',{
                  maxZoom:16,opacity:0.45,attribution:'Esri World Hillshade'})
              ]);
              geology=L.tileLayer.wms('""" + UsgsMapOverlayEndpoints.SgmcGeologyWms + """',{
                layers:'SGMC_Geology',format:'image/png',transparent:true,opacity:0.42,version:'1.3.0',
                attribution:'USGS SGMC geologic units (Horton et al.) — public WMS'});
              contours=L.tileLayer.wms('https://carto.nationalmap.gov/arcgis/services/contours/MapServer/WMSServer',{
                layers:'0',format:'image/png',transparent:true,opacity:0.65,version:'1.3.0',
                attribution:'USGS The National Map — Contours'});
              function cngmAgeColor(a,fallback){
                a=(a||'').toLowerCase();
                if(/holocene|greenlandian|meghalayan|quaternary|pleistocene|calabrian|chibanian|gelasian/.test(a)) return '#fde047';
                if(/pliocene|zanclean|piacenzian|neogene|miocene|messinian|serravallian|langhian|burdigalian/.test(a)) return '#facc15';
                if(/oligocene|chattian|rupelian|eocene|priabonian|bartonian|lutetian|paleogene|paleocene/.test(a)) return '#fb923c';
                if(/tertiary/.test(a)) return '#fdba74';
                if(/cretaceous|maastrichtian|campanian|santonian|coniacian|turonian|cenomanian|albian|aptian|hauterivian|valanginian/.test(a)) return '#4ade80';
                if(/jurassic|tithonian|kimmeridgian|callovian/.test(a)) return '#22d3ee';
                if(/triassic|norian|carnian/.test(a)) return '#c084fc';
                if(/permian|lopingian|guadalupian|cisuralian|kungurian|artinskian/.test(a)) return '#f87171';
                if(/pennsylvanian|moscovian|bashkirian|kasimovian|carboniferous/.test(a)) return '#60a5fa';
                if(/mississippian|tournaisian|visean|serpukhovian/.test(a)) return '#93c5fd';
                if(/devonian|famennian|frasnian|givetian|emsian/.test(a)) return '#a3e635';
                if(/silurian|pridoli|ludlow|wenlock|llandovery|aeronian/.test(a)) return '#86efac';
                if(/ordovician|hirnantian|katian|sandbian|darriwilian/.test(a)) return '#5eead4';
                if(/cambrian|furongian|miaolingian|terreneuvian|series 2/.test(a)) return '#34d399';
                if(/paleozoic/.test(a)) return '#38bdf8';
                if(/mesozoic/.test(a)) return '#4ade80';
                if(/archean|eoarchean|paleoarchean|mesoarchean|neoarchean/.test(a)) return '#db2777';
                if(/proterozoic|paleoproterozoic|mesoproterozoic|neoproterozoic|precambrian/.test(a)) return '#e879f9';
                return fallback;
              }
              function cngmPolyStyle(p){
                p=p||{};
                var g=(p.geomaterial||'').toLowerCase();
                var s=(p.synthesis_mapunitname||'').toLowerCase();
                var a=(p.min_age||p.max_age||'');
                var t=g+' '+s;
                function sty(c){return {fill:true,fillColor:c,fillOpacity:0.42,color:'#27272a',weight:0.25,opacity:0.35};}
                if(t.indexOf('unmapped')>=0) return {fill:true,fillColor:'#d4d4d8',fillOpacity:0.12,color:'#a1a1aa',weight:0.2,opacity:0.2};
                if(/water or ice|water and ice/.test(t)) return {fill:true,fillColor:'#93c5fd',fillOpacity:0.28,color:'#3b82f6',weight:0.15,opacity:0.25};
                if(t.indexOf('artificial')>=0||t.indexOf('human-engineered')>=0||t.indexOf('"made"')>=0) return sty('#a8a29e');
                if(/limestone|dolomite|carbonate|marble/.test(t)) return sty('#86efac');
                if(/ultramafic/.test(t)) return sty('#166534');
                if(/granitic|felsic/.test(t)) return sty('#f9a8d4');
                if(/mafic|gabbro/.test(t)) return sty('#b91c1c');
                if(/volcanic|lava|pyroclastic|tephra|extrusive/.test(t)) return sty('#ef4444');
                if(/intrusive|igneous/.test(t)) return sty('#e11d48');
                if(/schist|gneiss|quartzite|phyllite|slate|metamorphic|meta-/.test(t)) return sty('#c084fc');
                if(/glacial|till|ice-contact/.test(t)) return sty('#e5e7eb');
                if(/alluvial|colluvium|eolian|loess|dune|playa|lacustrine|coastal|marine sediment|peat/.test(t)) return sty(cngmAgeColor(a,'#fde68a'));
                return sty(cngmAgeColor(a,'#d6d3d1'));
              }
              try{
                if(L.vectorGrid&&L.vectorGrid.protobuf){
                  cngm=L.vectorGrid.protobuf('""" + UsgsMapOverlayEndpoints.CooperativeNationalGeologyVectorTiles + """',{
                    rendererFactory:L.canvas.tile,
                    interactive:false,
                    maxNativeZoom:13,
                    minZoom:6,
                    tileSize:512,
                    opacity:0.85,
                    attribution:'USGS Cooperative National Geologic Map v2 (NCGMP / NGMDB) — public domain',
                    vectorTileLayerStyles:{
                      'mapunitpolys_esurf':function(props){return cngmPolyStyle(props);},
                      'mapunitpolys_esurf/label':{fill:false,stroke:false,weight:0,opacity:0}
                    }
                  });
                  cngm.on('tileerror',function(){});
                }
              }catch(ex){cngm=null;}
              street.addTo(map);
              var overlayLayers={
                "Hillshade overlay (DEM/LiDAR-derived)":hillshade,
                "USGS State Geology (SGMC)":geology,
                "Elevation contours (USGS)":contours
              };
              if(cngm) overlayLayers["USGS Cooperative National Geologic Map (v2)"]=cngm;
              L.control.layers({
                "Streets (Esri)":street,
                "Streets (Carto / OSM)":carto,
                "Imagery (Esri)":sat,
                "Imagery + hillshade":satRelief,
                "USGS Imagery":usgsImagery,
                "USGS Topo (The National Map)":usgsTopo,
                "OpenTopoMap":openTopo,
                "Esri Topo":topo,
                "DEM hillshade (USGS 3DEP)":usgsLidar
              }, overlayLayers, {collapsed:true,position:'topright'}).addTo(map);
              """;

        if (lidarOverlayOn)
            js += "hillshade.addTo(map);";
        if (geologyOn)
            js += "geology.addTo(map);";
        if (cngmOn)
            js += "if(cngm)cngm.addTo(map);";
        if (contoursOn)
            js += "contours.addTo(map);";
        return js;
    }

    private void LidarOpacity_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_layerUiReady)
            return;

        MapLayerPreferences.LidarOpacity = LidarOpacitySlider.Value / 100.0;
        _ = ApplyHillshadeOpacityAsync();
    }

    private async Task ApplyHillshadeOpacityAsync()
    {
        try
        {
            if (MapView.CoreWebView2 is null)
                return;
            var opacity = MapLayerPreferences.LidarOpacity.ToString("0.##", CultureInfo.InvariantCulture);
            await MapView.CoreWebView2.ExecuteScriptAsync(
                $"if(window.__setHillshadeOpacity)window.__setHillshadeOpacity({opacity});").ConfigureAwait(true);
        }
        catch
        {
            // Overlay script is best-effort; next full refresh still applies opacity.
        }
    }

    private void SyncLayerTogglesFromPreferences()
    {
        _layerUiReady = false;
        LayerHypothesis.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.Hypothesis);
        LayerSolar.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.SolarLocus);
        LayerCurated.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.LocalityCurated);
        LayerUsgs.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.LocalityUsgs);
        LayerClaimActive.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.ClaimActive);
        LayerClaimExpiring.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.ClaimExpiring);
        LayerClaimOther.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.ClaimOther);
        LayerRumoured.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.Rumoured);
        LayerCommunityReddit.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.CommunityReddit);
        LayerRivers.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.River);
        LayerTrails.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.Trail);
        LayerPersonal.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.PersonalFind);
        LayerLidar.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.LidarTerrain);
        LayerGeology.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.UsgsGeology);
        LayerCngmGeology.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.CooperativeNationalGeology);
        LayerContours.IsOn = MapLayerPreferences.IsVisible(MapLayerKind.ElevationContours);
        LidarOpacitySlider.Value = MapLayerPreferences.LidarOpacity * 100.0;
        _layerUiReady = true;
    }

    private void SaveLayerTogglesToPreferences()
    {
        MapLayerPreferences.SetVisible(MapLayerKind.Hypothesis, LayerHypothesis.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.SolarLocus, LayerSolar.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.LocalityCurated, LayerCurated.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.LocalityUsgs, LayerUsgs.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.ClaimActive, LayerClaimActive.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.ClaimExpiring, LayerClaimExpiring.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.ClaimOther, LayerClaimOther.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.Rumoured, LayerRumoured.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.CommunityReddit, LayerCommunityReddit.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.River, LayerRivers.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.Trail, LayerTrails.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.PersonalFind, LayerPersonal.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.LidarTerrain, LayerLidar.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.UsgsGeology, LayerGeology.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.CooperativeNationalGeology, LayerCngmGeology.IsOn);
        MapLayerPreferences.SetVisible(MapLayerKind.ElevationContours, LayerContours.IsOn);
        MapLayerPreferences.LidarOpacity = LidarOpacitySlider.Value / 100.0;
    }

    private async Task OpenEarthForSelectionAsync()
    {
        if (_selection is null)
            return;
        await MapNavigationService.OpenInGoogleEarthAsync(
            _selection.Latitude,
            _selection.Longitude,
            _selection.Label,
            XamlRoot).ConfigureAwait(true);
    }

    private void OpenMapsForSelection()
    {
        if (_selection is null)
            return;
        MapNavigationService.OpenGoogleMapsLocation(_selection.Latitude, _selection.Longitude, _selection.Label);
    }

    private void OpenDirectionsForSelection()
    {
        if (_selection is null)
            return;
        MapNavigationService.OpenGoogleMapsDirections(_selection.Latitude, _selection.Longitude);
    }

    private void ClearSelection()
    {
        _selection = null;
        SelectionBar.Visibility = Visibility.Collapsed;
    }

    private sealed record MapPolyline(
        IReadOnlyList<double[]> Vertices,
        string Label,
        string Color,
        string Kind,
        string? Detail);

    private sealed record MapMarker(
        double Lat,
        double Lon,
        string Label,
        string Color,
        string Kind,
        string? Detail,
        double Radius,
        string? DashArray = null,
        string? Id = null,
        bool Highlight = false);

    private sealed record MapPinSelection(
        double Latitude,
        double Longitude,
        string Label,
        string Kind,
        string? Detail);

    private sealed record MapSnapshot(
        IReadOnlyList<MapMarker> Markers,
        IReadOnlyList<MapPolyline> Polylines,
        IReadOnlyList<Watercourse> Rivers,
        IReadOnlyList<Locality> Localities,
        IReadOnlyList<MiningClaim> Claims,
        IReadOnlyList<RumouredSite> Rumoured,
        MapFocusRequest? Focus = null)
    {
        public static MapSnapshot Empty { get; } = new([], [], [], [], [], [], null);
        public bool IsEmpty => Markers.Count == 0 && Polylines.Count == 0;
    }
}
