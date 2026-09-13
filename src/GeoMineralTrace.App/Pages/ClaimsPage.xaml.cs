using GeoMineralTrace.Claims;
using GeoMineralTrace.Claims.Import;
using GeoMineralTrace.Claims.Storage;
using GeoMineralTrace.Core.Claims;
using GeoMineralTrace.Reporting.Kml;
using GeoMineralTrace_App.Helpers;
using GeoMineralTrace_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GeoMineralTrace_App.Pages;

public sealed partial class ClaimsPage : Page
{
    private List<ClaimRow> _rows = [];
    private MiningClaim? _selected;

    public ClaimsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            UsStateFilterHelper.Populate(StateBox);
            StatusFilter.SelectedIndex = 0;
            // NumberBox is km; convert from Settings miles preference.
            NearbyRadiusBox.Value = Math.Clamp(HomeLocationPreferences.SearchRadiusKm, 1, 200);
            await SearchAsync();
        };
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async Task SearchAsync()
    {
        try
        {
            var store = App.Services.GetRequiredService<ClaimStore>();
            await store.InitializeAsync();

            var statusTag = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            ClaimStatus? status = statusTag switch
            {
                "ExpiringSoon" => ClaimStatus.ExpiringSoon,
                "Active" => ClaimStatus.Active,
                "LapsedReopenable" => ClaimStatus.LapsedReopenable,
                "Closed" => ClaimStatus.Closed,
                _ => null
            };

            bool? deadlineOnly = statusTag == "Sept1" ? true : null;

            var state = UsStateFilterHelper.ResolveStateCode(StateBox);
            var mineral = string.IsNullOrWhiteSpace(MineralBox.Text) ? null : MineralBox.Text.Trim();

            IReadOnlyList<MiningClaim> results;
            string scopeNote;
            if (HomeLocationPreferences.TryGetHomeCoordinate(out var home)
                && string.IsNullOrWhiteSpace(state)
                && string.IsNullOrWhiteSpace(mineral)
                && status is null
                && deadlineOnly is null)
            {
                results = await store.FindNearAsync(home, HomeLocationPreferences.SearchRadiusKm, limit: 400);
                scopeNote = $"Within {HomeLocationPreferences.SearchRadiusMiles:0.#} mi of home.";
            }
            else
            {
                results = await store.QueryAsync(state, status, deadlineOnly, mineral, limit: 400);
                if (HomeLocationPreferences.TryGetHomeCoordinate(out home))
                {
                    var radiusKm = HomeLocationPreferences.SearchRadiusKm;
                    results = results
                        .Where(c => c.Coordinates is { } coord
                            && ClaimStore.HaversineKm(home, coord) <= radiusKm)
                        .ToList();
                    scopeNote = $"Filtered to {HomeLocationPreferences.SearchRadiusMiles:0.#} mi of home (plus your filters).";
                }
                else
                {
                    scopeNote = "No home location — showing national search. Set one in Settings.";
                }
            }

            _rows = results.Select(ToRow).ToList();
            ClaimList.ItemsSource = _rows;
            ClaimEmpty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ClaimList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdateEmptyStateHint(state, status, deadlineOnly);
            ShowDetailPlaceholder();
            OpenEarthToolbarButton.IsEnabled = false;
            _selected = null;

            var byStatus = await store.CountByStatusAsync();
            var total = byStatus.Values.Sum();
            var summary = string.Join(" · ", byStatus.Select(kv => $"{kv.Key}: {kv.Value:N0}"));
            if (ClaimImportSanity.IsLikelyDemoOnlyDatabase(total))
            {
                StatsText.Text =
                    ClaimImportSanity.DemoOnlyWarning(total) +
                    $"\n\nCurrent filter shows {_rows.Count:N0}. {scopeNote} Breakdown: {summary}.";
            }
            else
            {
                StatsText.Text = total > 0
                    ? $"Database: {total:N0} claims total. {summary}. Showing {_rows.Count:N0}. {scopeNote}"
                    : "No claims in database — import a BLM extract or use the bundled sample rows.";
            }
        }
        catch (Exception ex)
        {
            ClaimList.ItemsSource = new[] { new ClaimRow(Guid.Empty, "Database error", ex.Message, "") };
            ShowDetailPlaceholder();
            OpenEarthToolbarButton.IsEnabled = false;
        }
    }

    private void UpdateEmptyStateHint(string? state, ClaimStatus? status, bool? deadlineOnly)
    {
        if (_rows.Count > 0)
            return;

        var hints = new List<string>();
        if (!string.IsNullOrWhiteSpace(state))
            hints.Add($"state={state.ToUpperInvariant()}");
        if (status is { } st)
            hints.Add(ClaimStatusClassifier.StatusBadge(st));
        if (deadlineOnly == true)
            hints.Add("Sept 1 advisory only");

        ClaimEmpty.Description = hints.Count > 0
            ? $"No claims match ({string.Join(", ", hints)}). Try All statuses with an empty state box, or import BLM data."
            : "Import a BLM MLRS GeoJSON/CSV extract, or use the bundled sample claims.";
    }

    private async void ClaimList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClaimList.SelectedItem is not ClaimRow row || row.Id == Guid.Empty)
        {
            ShowDetailPlaceholder();
            OpenEarthToolbarButton.IsEnabled = false;
            _selected = null;
            return;
        }

        try
        {
            var store = App.Services.GetRequiredService<ClaimStore>();
            _selected = await store.GetByIdAsync(row.Id);
            if (_selected is null)
            {
                ShowDetailPlaceholder();
                OpenEarthToolbarButton.IsEnabled = false;
                return;
            }

            BindDetail(_selected);
            DetailPlaceholder.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            OpenEarthToolbarButton.IsEnabled = _selected.Coordinates is { } g && g.IsValid;

            await RefreshNearbyAsync();
        }
        catch (Exception ex)
        {
            NearbyLocalities.Text = ex.Message;
            NearbyClaims.Text = ex.Message;
        }
    }

    private async void NearbyRadius_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!IsLoaded || _selected is null || double.IsNaN(args.NewValue))
            return;
        await RefreshNearbyAsync();
    }

    private async void RefreshNearby_Click(object sender, RoutedEventArgs e) =>
        await RefreshNearbyAsync();

    private async Task RefreshNearbyAsync()
    {
        if (_selected is null)
            return;

        var radius = NearbyRadiusBox.Value is >= 1 and <= 200 ? NearbyRadiusBox.Value : 25;
        var cross = App.Services.GetRequiredService<ClaimLocalityCrossLink>();
        var store = App.Services.GetRequiredService<ClaimStore>();

        if (_selected.Coordinates is not { } center || !center.IsValid)
        {
            NearbyClaims.Text = "No coordinates — cannot search nearby.";
            NearbyLocalities.Text = "No coordinates — cannot search nearby.";
            return;
        }

        var nearbyLocalities = await cross.LocalitiesNearClaimAsync(_selected, radiusKm: radius);
        NearbyLocalities.Text = nearbyLocalities.Count == 0
            ? $"No rockhounding localities within {radius:0.#} km."
            : string.Join("\n", nearbyLocalities.Take(10).Select(n =>
                $"• {n.Locality.Name} ({n.Locality.StateCode}) · {n.DistanceKm:F1} km · {n.Locality.AccessStatus}"));

        var nearbyClaims = await store.FindNearAsync(center, radiusKm: radius, limit: 15);
        var others = nearbyClaims.Where(c => c.Id != _selected.Id).ToList();
        NearbyClaims.Text = others.Count == 0
            ? $"No other claims within {radius:0.#} km (import national BLM data for wider coverage)."
            : string.Join("\n", others.Select(c =>
                $"• {c.ClaimName} ({c.StateCode}) · {ClaimStatusClassifier.StatusBadge(c.Status)} · " +
                $"{(c.Coordinates is { } g ? ClaimStore.HaversineKm(center, g) : 0):F1} km"));
    }

    private void ShowDetailPlaceholder()
    {
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailPlaceholder.Visibility = Visibility.Visible;
    }

    private void BindDetail(MiningClaim c)
    {
        DetailTitle.Text = c.ClaimName;
        DetailBadge.Text = ClaimStatusClassifier.StatusBadge(c.Status)
            + (c.MaintenanceDeadlineApproaching ? " · Sept 1 fee check" : "");

        DetailExplainer.Text = c.LegalNotes ?? ClaimStatusClassifier.LegalExplainer(c.Status);

        var coords = c.Coordinates is { } g
            ? $"{g.LatitudeDegrees:F5}, {g.LongitudeDegrees:F5}"
            : "Not in this extract";
        DetailLocation.Text =
            $"State: {c.StateCode}" +
            (string.IsNullOrWhiteSpace(c.County) ? "" : $" · County: {c.County}") +
            $"\nBLM field office: {c.FieldOffice ?? "—"}" +
            $"\nCoordinates: {coords}" +
            $"\nLegal description (PLSS): {c.LegalDescription ?? "—"}" +
            $"\nTownship / Range / Section: {c.Township ?? "—"} / {c.Range ?? "—"} / {c.Section ?? "—"}";

        DetailHolder.Text =
            $"Claimant of record: {c.ClaimantOfRecord ?? "— (not in extract; check MLRS)"}" +
            $"\nLocation date: {c.LocationDate?.ToString("yyyy-MM-dd") ?? "—"}" +
            $"\nLast maintenance fee paid: {c.LastMaintenanceFeePaid?.ToString("yyyy-MM-dd") ?? "—"}" +
            $"\nLast assessment year: {c.LastAssessmentYear?.ToString() ?? "—"}" +
            $"\nBLM disposition code: {c.BlmCaseDisposition ?? "—"}";

        DetailMinerals.Text =
            (c.Minerals.Count > 0 ? $"Minerals: {string.Join(", ", c.Minerals)}" : "Minerals: —") +
            $"\nClaim type: {c.ClaimType}" +
            (c.Acres is { } acres ? $"\nAcres: {acres:F2}" : "") +
            $"\nSource: {c.SourceDataset} · imported {c.SourceImportedUtc:u}";

        DetailMeta.Text =
            $"Serial: {c.SerialNumber}\n" +
            $"Record ID: {c.Id:N}\n" +
            (string.IsNullOrWhiteSpace(c.ExternalId) ? "" : $"External: {c.ExternalId}\n") +
            (string.IsNullOrWhiteSpace(c.LegacySerialNumber) ? "" : $"Legacy serial: {c.LegacySerialNumber}");
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance));
            picker.FileTypeFilter.Add(".geojson");
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".csv");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var importer = App.Services.GetRequiredService<BlmMlrsClaimImporter>();
            var store = App.Services.GetRequiredService<ClaimStore>();
            await store.InitializeAsync();

            var confirm = new ContentDialog
            {
                Title = "Import BLM claims file?",
                Content =
                    "Claims are not sold by BLM. Active = informational / private transfer only. " +
                    "Lapsed = possible ground for staking a new claim. Expiring Soon = maintenance-fee deadline risk.\n\n" +
                    $"File: {file.Name}",
                PrimaryButtonText = "Import",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            var result = await importer.ImportFileAsync(file.Path, store);
            var byState = string.Join("\n", result.CountsByState.OrderByDescending(kv => kv.Value).Take(10)
                .Select(kv => $"  {kv.Key}: {kv.Value:N0}"));
            var byStatus = string.Join("\n", result.CountsByStatus.Select(kv => $"  {kv.Key}: {kv.Value:N0}"));

            await new ContentDialog
            {
                Title = "Claims import complete",
                Content =
                    $"Upserted {result.Upserted:N0}\nSkipped (no serial): {result.SkippedNoSerial:N0}\n" +
                    $"Without coordinates: {result.SkippedNoCoords:N0}\n\nBy status:\n{byStatus}\n\nBy state:\n{byState}",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();

            await SearchAsync();
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Claims import failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private async void RowEarth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;
        var id = btn.Tag is Guid g
            ? g
            : Guid.TryParse(btn.Tag?.ToString(), out var parsed) ? parsed : Guid.Empty;
        if (id == Guid.Empty)
            return;

        try
        {
            var store = App.Services.GetRequiredService<ClaimStore>();
            await store.InitializeAsync();
            var claim = await store.GetByIdAsync(id);
            if (claim is null)
            {
                await new ContentDialog
                {
                    Title = "Not found",
                    Content = "Could not load that claim.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
                return;
            }

            await OpenClaimInEarthAsync(claim).ConfigureAwait(true);
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

    private async void OpenEarth_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            await new ContentDialog
            {
                Title = "Select a claim",
                Content = "Select a claim in the list (or use the Earth button on a row).",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
            return;
        }

        await OpenClaimInEarthAsync(_selected).ConfigureAwait(true);
    }

    private async Task OpenClaimInEarthAsync(MiningClaim claim)
    {
        if (claim.Coordinates is null || !claim.Coordinates.Value.IsValid)
        {
            await new ContentDialog
            {
                Title = "No coordinates",
                Content = "This claim has no map coordinates in the local extract, so a KML placemark cannot be created.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
            return;
        }

        try
        {
            var doc = KmlCatalogBuilder.SingleClaim(claim);
            await GoogleEarthLauncher.OpenInGoogleEarthAsync(doc, XamlRoot, claim.ClaimName).ConfigureAwait(true);
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

    private static ClaimRow ToRow(MiningClaim c)
    {
        var badge = ClaimStatusClassifier.StatusBadge(c.Status);
        if (c.Status == ClaimStatus.ExpiringSoon)
            badge = "⚠ " + badge;
        if (c.MaintenanceDeadlineApproaching)
            badge += " · fee check";

        return new ClaimRow(
            c.Id,
            $"{c.ClaimName} · {c.StateCode}  [{badge}]",
            $"{c.ClaimType} · {c.SerialNumber} · claimant: {c.ClaimantOfRecord ?? "—"} · " +
            (c.Coordinates is { } g ? $"{g.LatitudeDegrees:F4},{g.LongitudeDegrees:F4}" : "no coords"),
            c.LegalNotes ?? "");
    }

    private sealed record ClaimRow(Guid Id, string Title, string Meta, string Notes);
}
