using GeoMineralTrace.Claims;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Hydrology;
using GeoMineralTrace.Hydrology.Geo;
using GeoMineralTrace.Hydrology.Storage;
using GeoMineralTrace.Reporting.Kml;
using GeoMineralTrace.Rockhounding.Ownership;
using GeoMineralTrace.Rockhounding.Storage;
using GeoMineralTrace_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GeoMineralTrace_App.Pages;

public sealed partial class PublicMinesPage : Page
{
    private const int PageSize = 80;

    private List<Row> _rows = [];
    private Locality? _selected;
    private int _pageIndex;
    private int _totalMatches;

    public PublicMinesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await SearchAsync(resetPage: true);
    }

    private async void Search_Click(object sender, RoutedEventArgs e) =>
        await SearchAsync(resetPage: true);

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pageIndex <= 0) return;
        _pageIndex--;
        await SearchAsync(resetPage: false);
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if ((_pageIndex + 1) * PageSize >= _totalMatches) return;
        _pageIndex++;
        await SearchAsync(resetPage: false);
    }

    private async Task SearchAsync(bool resetPage)
    {
        try
        {
            var store = App.Services.GetRequiredService<LocalityStore>();
            await store.InitializeAsync();

            if (resetPage)
                _pageIndex = 0;

            var state = string.IsNullOrWhiteSpace(StateBox.Text) ? null : StateBox.Text.Trim();
            var mineral = string.IsNullOrWhiteSpace(MineralBox.Text) ? null : MineralBox.Text.Trim();
            LandType? land = null;
            if (LandFilter.SelectedItem is ComboBoxItem { Tag: string tag } &&
                Enum.TryParse<LandType>(tag, ignoreCase: true, out var parsed) &&
                MineOwnershipHints.IsPublicSurface(parsed))
            {
                land = parsed;
            }

            _totalMatches = await store.CountPublicAsync(state, land, mineral).ConfigureAwait(true);
            var page = await store.ListPublicAsync(
                state, land, mineral, PageSize, _pageIndex * PageSize).ConfigureAwait(true);

            _rows = page.Select(ToRow).ToList();
            MineList.ItemsSource = _rows;
            MineEmpty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            MineList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            ClearDetail();
            UpdatePagerUi();
        }
        catch (Exception ex)
        {
            ResultStats.Text = ex.Message;
            PrevPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
        }
    }

    private void UpdatePagerUi()
    {
        if (_totalMatches == 0)
        {
            ResultStats.Text = "Showing 0 of 0 public sites.";
            PrevPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
            return;
        }

        var from = _pageIndex * PageSize + 1;
        var to = Math.Min((_pageIndex + 1) * PageSize, _totalMatches);
        ResultStats.Text = $"Showing {from:N0}–{to:N0} of {_totalMatches:N0} public sites.";
        PrevPageButton.IsEnabled = _pageIndex > 0;
        NextPageButton.IsEnabled = to < _totalMatches;
    }

    private async void MineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenEarthToolbarButton.IsEnabled = false;
        _selected = null;
        if (MineList.SelectedItem is not Row row || row.Id == Guid.Empty)
        {
            ClearDetail();
            return;
        }

        try
        {
            var store = App.Services.GetRequiredService<LocalityStore>();
            _selected = await store.GetByIdAsync(row.Id).ConfigureAwait(true);
            if (_selected is null)
            {
                ClearDetail();
                return;
            }

            ShowDetail(_selected);
            OpenEarthToolbarButton.IsEnabled = _selected.Coordinates is { } g && g.IsValid;
            await LoadNearbyClaimsAsync(_selected).ConfigureAwait(true);
            await LoadNearbyRiversAsync(_selected).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            DetailPlaceholder.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            DetailTitle.Text = "Failed to load detail";
            DetailLocation.Text = ex.Message;
        }
    }

    private void ClearDetail()
    {
        DetailPlaceholder.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Collapsed;
        OpenEarthToolbarButton.IsEnabled = false;
        DetailNearbyClaims.Text = "";
        DetailNearbyRivers.Text = "";
    }

    private async Task LoadNearbyRiversAsync(Locality locality)
    {
        if (locality.Coordinates is not { } coords || !coords.IsValid)
        {
            DetailNearbyRivers.Text = "No coordinates — cannot search for nearby rivers.";
            return;
        }

        try
        {
            var rivers = App.Services.GetRequiredService<RiverStore>();
            var cross = App.Services.GetRequiredService<RiverLocalityCrossLink>();
            await rivers.InitializeAsync().ConfigureAwait(true);
            var hits = await cross.FindRiversNearLocalityAsync(rivers, locality, corridorRadiusKm: 8)
                .ConfigureAwait(true);
            DetailNearbyRivers.Text = hits.Count == 0
                ? "No named rivers within 8 km corridor (import NHD data for coverage)."
                : string.Join("\n", hits.Take(6).Select(w =>
                {
                    var dist = w.Vertices.Count >= 2
                        ? PolylineDistance.MinDistanceKm(coords, w.Vertices)
                        : 0;
                    return $"• {w.Name} ({w.StateCode}) · {dist:F1} km";
                }));
        }
        catch (Exception ex)
        {
            DetailNearbyRivers.Text = ex.Message;
        }
    }

    private async Task LoadNearbyClaimsAsync(Locality locality)
    {
        if (locality.Coordinates is not { } coords || !coords.IsValid)
        {
            DetailNearbyClaims.Text = "No coordinates — cannot search for nearby claims.";
            return;
        }

        try
        {
            var cross = App.Services.GetRequiredService<ClaimLocalityCrossLink>();
            var hits = await cross.ClaimsNearLocalityAsync(locality, radiusKm: 25).ConfigureAwait(true);
            DetailNearbyClaims.Text = hits.Count == 0
                ? "No BLM claims within 25 km in the local database (import national BLM data for coverage)."
                : string.Join("\n", hits.Take(8).Select(h =>
                    $"• {h.Claim.ClaimName} ({h.Claim.StateCode}) · {ClaimStatusClassifier.StatusBadge(h.Claim.Status)} · {h.DistanceKm:F1} km"));
        }
        catch (Exception ex)
        {
            DetailNearbyClaims.Text = ex.Message;
        }
    }

    private void ShowDetail(Locality l)
    {
        DetailPlaceholder.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;

        DetailTitle.Text = l.Name;
        DetailOwnership.Text = MineOwnershipHints.DisplayLabel(l.LandType).ToUpperInvariant();

        var coords = l.Coordinates is { } c
            ? $"{c.LatitudeDegrees:F5}, {c.LongitudeDegrees:F5}"
              + (c.AltitudeMeters is { } alt ? $" · elev {alt:F0} m" : "")
            : "No coordinates";
        DetailLocation.Text =
            $"State: {l.StateCode}" +
            (string.IsNullOrWhiteSpace(l.County) ? "" : $" · County: {l.County}") +
            $"\nCoordinates: {coords}";

        DetailMinerals.Text =
            (l.ReportedMinerals.Count > 0
                ? string.Join(", ", l.ReportedMinerals)
                : "No minerals listed") +
            (string.IsNullOrWhiteSpace(l.TypicalFinds) ? "" : $"\nTypical finds: {l.TypicalFinds}");

        var outdated = l.HumanActivityDataMayBeOutdated
            ? "\n⚠ Human-activity / ownership fields may be outdated — verify before visiting."
            : "";
        DetailAccess.Text =
            $"Access status: {l.AccessStatus}" +
            (string.IsNullOrWhiteSpace(l.AccessNotes) ? "" : $"\n{l.AccessNotes}") +
            outdated;

        DetailLimits.Text = string.IsNullOrWhiteSpace(l.CollectingLimits)
            ? "No collecting-limit notes on file."
            : l.CollectingLimits;

        DetailDifficulty.Text =
            $"Difficulty: {l.Difficulty}" +
            (string.IsNullOrWhiteSpace(l.SeasonalityNotes)
                ? ""
                : $"\nSeasonality: {l.SeasonalityNotes}");

        DetailHazards.Text = string.IsNullOrWhiteSpace(l.HazardNotes)
            ? "No hazard notes on file."
            : l.HazardNotes;

        DetailServices.Text = string.IsNullOrWhiteSpace(l.NearestServices)
            ? "No nearest-services notes on file."
            : l.NearestServices;

        DetailEnrichment.Text = string.IsNullOrWhiteSpace(l.EnrichmentSummary)
            ? "No geology/enrichment summary."
            : l.EnrichmentSummary;

        if (l.SystemRating is { } r)
        {
            DetailRatings.Text =
                $"System overall {r.Overall:F1}/10 · accessibility {r.Accessibility:F1} · productivity {r.Productivity:F1} · " +
                $"legal clarity {r.LegalClarity:F1} · recency {r.Recency:F1} · beginner {r.BeginnerFriendliness:F1} · " +
                $"variety {r.Variety:F1} · safety {(r.Safety ?? 5):F1}\n" +
                $"User average {l.UserAverageRating:F1} (n={l.UserRatingCount})";
        }
        else
        {
            DetailRatings.Text =
                $"No system rating.\nUser average {l.UserAverageRating:F1} (n={l.UserRatingCount})";
        }

        var sources = l.Sources.Count > 0 ? string.Join("\n• ", l.Sources) : "(none)";
        DetailSources.Text =
            $"Dataset: {l.SourceDataset ?? "—"}" +
            (l.SourceVintage is { } v ? $" ({v})" : "") +
            $"\n• {sources}" +
            (l.LastVerifiedUtc is { } verified
                ? $"\nLast verified (local): {verified:u}"
                : "");

        DetailIds.Text =
            $"Id: {l.Id:N}\nExternalId: {l.ExternalId ?? "(none)"}";
    }

    private async void OpenEarth_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_selected is null)
            {
                await new ContentDialog
                {
                    Title = "Select a public mine",
                    Content = "Select a row in the list first.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
                return;
            }

            if (_selected.Coordinates is null || !_selected.Coordinates.Value.IsValid)
            {
                await new ContentDialog
                {
                    Title = "No coordinates",
                    Content = "This site has no map coordinates, so a KML placemark cannot be created.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
                return;
            }

            var doc = KmlCatalogBuilder.SingleLocality(_selected);
            await GoogleEarthLauncher.OpenInGoogleEarthAsync(doc, XamlRoot, _selected.Name).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Google Earth export failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private void OpenRegistry_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(RockhoundingPage), "rockhounding");

    private static Row ToRow(Locality l)
    {
        var ownership = MineOwnershipHints.DisplayLabel(l.LandType);
        var minerals = l.ReportedMinerals.Count > 0
            ? string.Join(", ", l.ReportedMinerals.Take(6))
            : "—";
        return new Row(
            l.Id,
            $"{l.Name} · {l.StateCode}",
            $"{ownership} · {l.AccessStatus} · {minerals}");
    }

    private sealed record Row(Guid Id, string Title, string Meta);
}
