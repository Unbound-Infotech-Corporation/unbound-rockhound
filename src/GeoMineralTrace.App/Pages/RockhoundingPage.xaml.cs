using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace.Reporting.Kml;
using GeoMineralTrace.Rockhounding.Import;
using GeoMineralTrace.Rockhounding.Ownership;
using GeoMineralTrace.Rockhounding.Storage;
using GeoMineralTrace_App.Helpers;
using GeoMineralTrace_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GeoMineralTrace_App.Pages;

public sealed partial class RockhoundingPage : Page
{
    private const int PageSize = 100;

    private List<Row> _rows = [];
    private int _pageIndex;
    private int _totalMatches;
    private string _lastState = "OR";
    private string? _lastMineral;
    private bool _lastBeginnerOnly;
    private string _lastOwnershipTag = "";
    private bool _nearHomeMode;
    private string? _pendingMineralFilter;

    public RockhoundingPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            UsStateFilterHelper.Populate(StateBox);
            if (!string.IsNullOrWhiteSpace(_pendingMineralFilter))
            {
                MineralBox.Text = _pendingMineralFilter;
                _pendingMineralFilter = null;
            }
            await SearchAsync(resetPage: true);
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string mineral && !string.IsNullOrWhiteSpace(mineral))
        {
            _pendingMineralFilter = mineral.Trim();
            if (IsLoaded)
            {
                MineralBox.Text = _pendingMineralFilter;
                _pendingMineralFilter = null;
                _ = SearchAsync(resetPage: true);
            }
        }
    }

    private async void LocalityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MineralChips.ItemsSource = null;
        if (LocalityList.SelectedItem is not Row row || row.Id == Guid.Empty)
            return;

        try
        {
            var store = App.Services.GetRequiredService<LocalityStore>();
            await store.InitializeAsync().ConfigureAwait(true);
            var loc = await store.GetByIdAsync(row.Id).ConfigureAwait(true);
            MineralChips.ItemsSource = loc?.ReportedMinerals.Where(m => !string.IsNullOrWhiteSpace(m)).ToList() ?? [];
        }
        catch
        {
            MineralChips.ItemsSource = null;
        }
    }

    private void MineralChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mineral } || string.IsNullOrWhiteSpace(mineral))
            return;
        MainWindow.Instance?.NavigateTo(typeof(GlossaryPage), "glossary", mineral.Trim());
    }

    private async void Search_Click(object sender, RoutedEventArgs e) =>
        await SearchAsync(resetPage: true);

    private async void OwnershipBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await SearchAsync(resetPage: true);
    }

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

            _lastState = UsStateFilterHelper.ResolveStateCode(StateBox) ?? "";
            _lastMineral = string.IsNullOrWhiteSpace(MineralBox.Text) ? null : MineralBox.Text.Trim();
            _lastBeginnerOnly = BeginnerOnly.IsChecked == true;
            _lastOwnershipTag = (OwnershipBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            _nearHomeMode = HomeLocationPreferences.TryGetHomeCoordinate(out _)
                && string.IsNullOrWhiteSpace(_lastState);
            if (resetPage)
                _pageIndex = 0;

            IReadOnlyList<Locality> page;
            if (_nearHomeMode)
            {
                var (filteredTotal, filteredPage) = await LoadNearHomePageAsync(store).ConfigureAwait(true);
                _totalMatches = filteredTotal;
                page = filteredPage;
            }
            else if (_lastBeginnerOnly || _lastMineral is not null || NeedsInMemoryOwnershipFilter(_lastOwnershipTag))
            {
                if (string.IsNullOrWhiteSpace(_lastState))
                    _lastState = "OR";
                var (filteredTotal, filteredPage) = await LoadFilteredPageAsync(store).ConfigureAwait(true);
                _totalMatches = filteredTotal;
                page = filteredPage;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_lastState))
                    _lastState = "OR";
                var land = ParseLandFilter(_lastOwnershipTag);
                _totalMatches = await store.CountByStateAsync(_lastState, land).ConfigureAwait(true);
                page = await store.ListByStateAsync(
                    _lastState, limit: PageSize, offset: _pageIndex * PageSize, landType: land).ConfigureAwait(true);
            }

            _rows = page.Select(ToRow).ToList();
            LocalityList.ItemsSource = _rows;
            LocalityEmpty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LocalityList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdatePagerUi();
        }
        catch (Exception ex)
        {
            LocalityList.ItemsSource = new[] { new Row(Guid.Empty, "Database error", ex.Message, "") };
            ResultStats.Text = ex.Message;
            PrevPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
        }
    }

    private async Task<(int Total, IReadOnlyList<Locality> Page)> LoadNearHomePageAsync(LocalityStore store)
    {
        if (!HomeLocationPreferences.TryGetHomeCoordinate(out var home))
            return (0, []);

        var near = await store.FindNearAsync(home, HomeLocationPreferences.SearchRadiusKm).ConfigureAwait(true);
        var filtered = near
            .Where(MatchesActiveFilters)
            .Where(l => _lastMineral is null
                || l.ReportedMinerals.Any(m => m.Contains(_lastMineral, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(l => l.SystemRating?.Overall ?? 0)
            .ToList();

        var total = filtered.Count;
        var page = filtered
            .Skip(_pageIndex * PageSize)
            .Take(PageSize)
            .ToList();
        return (total, page);
    }

    private static bool NeedsInMemoryOwnershipFilter(string tag) =>
        tag is "public"; // multiple LandType values

    private static LandType? ParseLandFilter(string tag) => tag switch
    {
        "private" => LandType.Private,
        "claim" => LandType.Claim,
        "unknown" => LandType.Unknown,
        _ => null
    };

    private async Task<(int Total, IReadOnlyList<Locality> Page)> LoadFilteredPageAsync(LocalityStore store)
    {
        const int scanChunk = 500;
        var filtered = new List<Locality>();
        var offset = 0;
        int scanned;
        var land = ParseLandFilter(_lastOwnershipTag);
        do
        {
            IReadOnlyList<Locality> chunk;
            if (_lastMineral is { } mineral)
                chunk = await store.SearchByMineralAsync(mineral, limit: scanChunk, offset: offset).ConfigureAwait(true);
            else
                chunk = await store.ListByStateAsync(_lastState, limit: scanChunk, offset: offset, landType: land).ConfigureAwait(true);

            scanned = chunk.Count;
            offset += scanned;
            filtered.AddRange(chunk.Where(MatchesActiveFilters));
        } while (scanned == scanChunk && filtered.Count < (_pageIndex + 1) * PageSize + 1);

        const int maxScan = 20_000;
        while (scanned == scanChunk && offset < maxScan)
        {
            IReadOnlyList<Locality> chunk;
            if (_lastMineral is { } mineral)
                chunk = await store.SearchByMineralAsync(mineral, limit: scanChunk, offset: offset).ConfigureAwait(true);
            else
                chunk = await store.ListByStateAsync(_lastState, limit: scanChunk, offset: offset, landType: land).ConfigureAwait(true);
            scanned = chunk.Count;
            offset += scanned;
            filtered.AddRange(chunk.Where(MatchesActiveFilters));
        }

        var total = filtered.Count;
        var page = filtered
            .OrderByDescending(l => l.SystemRating?.Overall ?? 0)
            .Skip(_pageIndex * PageSize)
            .Take(PageSize)
            .ToList();
        return (total, page);
    }

    private bool MatchesActiveFilters(Locality l)
    {
        if (_lastBeginnerOnly && !IsBeginnerFriendly(l))
            return false;
        if (_lastOwnershipTag == "public" && !MineOwnershipHints.IsPublicSurface(l.LandType))
            return false;
        if (_lastOwnershipTag == "private" && l.LandType != LandType.Private)
            return false;
        if (_lastOwnershipTag == "claim" && l.LandType != LandType.Claim)
            return false;
        if (_lastOwnershipTag == "unknown" && l.LandType != LandType.Unknown)
            return false;
        return true;
    }

    private async Task<(int Total, IReadOnlyList<Locality> Page)> LoadBeginnerPageAsync(LocalityStore store) =>
        await LoadFilteredPageAsync(store).ConfigureAwait(true);

    private static bool IsBeginnerFriendly(Locality l) =>
        l.AccessStatus == AccessStatus.Open &&
        l.Difficulty is DifficultyLevel.Beginner or DifficultyLevel.Intermediate &&
        (l.SystemRating?.Overall ?? 0) >= 5;

    private void UpdatePagerUi()
    {
        if (_totalMatches == 0)
        {
            ResultStats.Text = "Showing 0 of 0 sites.";
            PrevPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
            return;
        }

        var from = _pageIndex * PageSize + 1;
        var to = Math.Min((_pageIndex + 1) * PageSize, _totalMatches);
        var filters = new List<string>();
        if (_nearHomeMode)
            filters.Add($"within {HomeLocationPreferences.SearchRadiusMiles:0.#} mi of home");
        else if (!string.IsNullOrWhiteSpace(_lastState))
            filters.Add($"state {_lastState}");
        if (_lastBeginnerOnly) filters.Add("beginner-friendly");
        if (!string.IsNullOrEmpty(_lastOwnershipTag)) filters.Add($"ownership={_lastOwnershipTag}");
        ResultStats.Text = $"Showing {from:N0}–{to:N0} of {_totalMatches:N0}"
            + (filters.Count > 0 ? $" ({string.Join(", ", filters)})" : "")
            + ".";
        PrevPageButton.IsEnabled = _pageIndex > 0;
        NextPageButton.IsEnabled = to < _totalMatches;
    }

    private static Row ToRow(Locality l)
    {
        var outdated = l.HumanActivityDataMayBeOutdated ? "  [OUTDATED ACTIVITY]" : "";
        var restricted = l.AccessStatus is AccessStatus.Closed or AccessStatus.Restricted
            ? "  [RESTRICTED]"
            : "";
        var source = l.SourceDataset is { } ds
            ? $" · {ds}" + (l.SourceVintage is { } v ? $" ({v})" : "")
            : "";
        var legal = l.SystemRating is { } r
            ? $" · legal clarity {r.LegalClarity:F1}/10"
            : "";
        var ownership = MineOwnershipHints.DisplayLabel(l.LandType);
        return new Row(
            l.Id,
            $"{l.Name} · {l.StateCode}{restricted}{outdated}",
            $"Minerals: {string.Join(", ", l.ReportedMinerals)} · System {l.SystemRating?.Overall:F1}/10 · User avg {l.UserAverageRating:F1} (n={l.UserRatingCount}) · {l.AccessStatus} · {ownership}{source}{legal}",
            l.AccessNotes ?? "");
    }

    private async void ClassifyOwnership_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = new ContentDialog
            {
                Title = "Classify mine ownership?",
                Content =
                    "This offline pass tags Unknown USGS sites near active BLM claims as Claim vicinity, " +
                    "and applies text hints for private/public language. It does not replace a parcel survey.",
                PrimaryButtonText = "Run",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            var enricher = App.Services.GetRequiredService<MineOwnershipEnricher>();
            ResultStats.Text = "Classifying ownership… (this can take several minutes)";
            var result = await enricher.EnrichAsync(overwriteKnown: false).ConfigureAwait(true);

            var counts = await App.Services.GetRequiredService<LocalityStore>().CountByLandTypeAsync().ConfigureAwait(true);
            var summary = string.Join("\n", counts.OrderBy(kv => kv.Key).Select(kv =>
                $"  {MineOwnershipHints.DisplayLabel(kv.Key)}: {kv.Value:N0}"));
            await new ContentDialog
            {
                Title = "Ownership classification complete",
                Content =
                    $"Updated {result.Updated:N0} of {result.Scanned:N0} sites.\n" +
                    $"Private hints: {result.HintPrivate:N0} · Public hints: {result.HintPublic:N0} · Claim tags: {result.ClaimTagged:N0}\n\n" +
                    summary,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
            await SearchAsync(resetPage: true);
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Classification failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private async void Rate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (LocalityList.SelectedItem is not Row row || row.Id == Guid.Empty)
                return;

            var ratings = App.Services.GetRequiredService<UserRatingService>();
            await ratings.AddRatingAsync(row.Id, StarsBox.Value, NotesBox.Text);
            await SearchAsync(resetPage: false);
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Rating failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private async void OpenEarth_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (LocalityList.SelectedItem is not Row row || row.Id == Guid.Empty)
            {
                await new ContentDialog
                {
                    Title = "Select a locality",
                    Content = "Select a row in the list, then open it in Google Earth.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
                return;
            }

            var store = App.Services.GetRequiredService<LocalityStore>();
            await store.InitializeAsync();
            var locality = await store.GetByIdAsync(row.Id);
            if (locality is null)
            {
                await new ContentDialog
                {
                    Title = "Not found",
                    Content = "Could not load that locality from the local database.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
                return;
            }

            if (locality.Coordinates is null || !locality.Coordinates.Value.IsValid)
            {
                await new ContentDialog
                {
                    Title = "No coordinates",
                    Content = "This locality has no map coordinates, so a KML placemark cannot be created.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
                return;
            }

            var doc = KmlCatalogBuilder.SingleLocality(locality);
            await GoogleEarthLauncher.OpenInGoogleEarthAsync(doc, XamlRoot, locality.Name).ConfigureAwait(true);
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

    private async void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance));
            picker.FileTypeFilter.Add(".csv");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var importer = App.Services.GetRequiredService<LocalityCsvImporter>();
            var store = App.Services.GetRequiredService<LocalityStore>();
            var count = await importer.ImportFileAsync(file.Path, store);
            await new ContentDialog
            {
                Title = "Import complete",
                Content = $"Imported {count} localities from CSV.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
            await SearchAsync(resetPage: true);
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "CSV import failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private async void ImportUsgs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance));
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".txt");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var importer = App.Services.GetRequiredService<UsgsMrdsImporter>();
            var store = App.Services.GetRequiredService<LocalityStore>();
            var info = new FileInfo(file.Path);
            if (info.Length < 5_000_000)
            {
                var text = await File.ReadAllTextAsync(file.Path);
                var preview = importer.Preview(text);
                var dialog = new ContentDialog
                {
                    Title = "Import USGS MRDS / Critical Minerals file?",
                    Content =
                        $"Rows in file: {preview.TotalRows}\n" +
                        $"Importable (with coordinates): {preview.ImportableRows}\n" +
                        $"Flagged restricted/closed: {preview.FlaggedRestricted}\n\n" +
                        "USGS records are mineral occurrences — not vetted collectable sites.\n" +
                        "Legal clarity will be LOW; human-activity status may be outdated (MRDS vintage 2011).\n\n" +
                        "Samples:\n• " + string.Join("\n• ", preview.SampleNames),
                    PrimaryButtonText = "Import",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };

                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                    return;
            }
            else
            {
                var dialog = new ContentDialog
                {
                    Title = "Import large USGS extract?",
                    Content =
                        $"File is {info.Length / (1024.0 * 1024.0):F1} MB. " +
                        "National MRDS imports may take several minutes.\n\n" +
                        "United States rows only will be imported. Records get LOW legal-clarity and an outdated-activity flag.",
                    PrimaryButtonText = "Import",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                    return;
            }

            var unitedStatesOnly = info.Length >= 5_000_000;
            var result = await importer.ImportFileWithStatsAsync(
                file.Path,
                store,
                unitedStatesOnly: unitedStatesOnly);

            var topStates = string.Join(
                "\n",
                result.CountsByState.OrderByDescending(kv => kv.Value).Take(8)
                    .Select(kv => $"  {kv.Key}: {kv.Value:N0}"));
            var invalidNote = result.InvalidCoordinates > 0
                ? $"\nInvalid coordinates flagged (not imported): {result.InvalidCoordinates:N0}"
                : "";

            await new ContentDialog
            {
                Title = "USGS import complete",
                Content =
                    $"{result.DatasetLabel} (vintage {result.SourceVintage})\n" +
                    $"Upserted: {result.Upserted:N0}\n" +
                    $"Deduped against existing: {result.DedupedAgainstExisting:N0}\n" +
                    $"Skipped non-US: {result.SkippedNonUs:N0}" +
                    invalidNote +
                    (string.IsNullOrEmpty(topStates) ? "" : $"\n\nBy state:\n{topStates}"),
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
            await SearchAsync(resetPage: true);
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "USGS import failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private sealed record Row(Guid Id, string Title, string Meta, string Access);
}
