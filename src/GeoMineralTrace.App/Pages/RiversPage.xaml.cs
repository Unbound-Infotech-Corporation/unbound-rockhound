using GeoMineralTrace.Core.Hydrology;
using GeoMineralTrace.Hydrology;
using GeoMineralTrace.Hydrology.Enrichment;
using GeoMineralTrace.Hydrology.Import;
using GeoMineralTrace.Hydrology.Storage;
using GeoMineralTrace.Rockhounding.Storage;
using GeoMineralTrace_App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GeoMineralTrace_App.Pages;

public sealed partial class RiversPage : Page
{
    private List<RiverRow> _rows = [];
    private Watercourse? _selected;

    public RiversPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await SearchAsync();
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async Task SearchAsync()
    {
        try
        {
            var store = App.Services.GetRequiredService<RiverStore>();
            await store.InitializeAsync();

            var state = string.IsNullOrWhiteSpace(StateBox.Text) ? null : StateBox.Text.Trim();
            var name = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim();
            var mineral = string.IsNullOrWhiteSpace(MineralBox.Text) ? null : MineralBox.Text.Trim();
            var curatedOnly = CuratedOnlyBox.IsChecked == true;

            IReadOnlyList<Watercourse> results;
            string scopeNote;
            if (HomeLocationPreferences.TryGetHomeCoordinate(out var home)
                && string.IsNullOrWhiteSpace(state)
                && string.IsNullOrWhiteSpace(name)
                && string.IsNullOrWhiteSpace(mineral)
                && !curatedOnly)
            {
                results = await store.FindNearAsync(home, HomeLocationPreferences.SearchRadiusKm, limit: 400);
                scopeNote = $"Within {HomeLocationPreferences.SearchRadiusMiles:0.#} mi of home location.";
            }
            else
            {
                results = await store.SearchAsync(state, name, mineral, curatedOnly, limit: 400);
                if (HomeLocationPreferences.TryGetHomeCoordinate(out home))
                {
                    var radiusKm = HomeLocationPreferences.SearchRadiusKm;
                    results = results
                        .Where(r => r.Centroid is { } c
                            && GeoMineralTrace.Hydrology.Geo.PolylineDistance.HaversineKm(home, c) <= radiusKm)
                        .ToList();
                    scopeNote = $"Filtered to {HomeLocationPreferences.SearchRadiusMiles:0.#} mi of home (plus your search filters).";
                }
                else
                {
                    scopeNote = "No home location — showing national search. Set one in Settings to scope results.";
                }
            }

            _rows = [];
            foreach (var r in results)
            {
                var minerals = await store.GetMineralsForWatercourseAsync(r.Id);
                _rows.Add(ToRow(r, minerals));
            }

            RiverList.ItemsSource = _rows;
            RiverEmpty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RiverList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            ShowDetailPlaceholder();

            var total = await store.CountAsync();
            StatsText.Text = total > 0
                ? $"Database: {total:N0} named watercourses. Showing {_rows.Count:N0}. {scopeNote}"
                : "No rivers in database — import NHD GeoJSON or use the bundled seed data.";
        }
        catch (Exception ex)
        {
            RiverList.ItemsSource = new[] { new RiverRow(Guid.Empty, "Database error", ex.Message) };
            ShowDetailPlaceholder();
        }
    }

    private async void RiverList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RiverList.SelectedItem is not RiverRow row || row.Id == Guid.Empty)
        {
            ShowDetailPlaceholder();
            return;
        }

        var store = App.Services.GetRequiredService<RiverStore>();
        await store.InitializeAsync();
        _selected = await store.GetWithMineralsAsync(row.Id);
        if (_selected is null)
        {
            ShowDetailPlaceholder();
            return;
        }

        DetailTitle.Text = $"{_selected.Name} ({_selected.StateCode})";
        DetailMeta.Text = $"{_selected.Kind} · {(_selected.LengthKm ?? 0):F1} km · {MineKindLabel(_selected)} · {_selected.SourceDataset ?? "—"}";

        var curated = _selected.Minerals.Where(m => m.AssociationKind == MineralAssociationKind.Curated).ToList();
        var inferred = _selected.Minerals.Where(m => m.AssociationKind == MineralAssociationKind.ProximityInferred).ToList();

        DetailCurated.Text = curated.Count == 0
            ? "—"
            : string.Join(Environment.NewLine, curated.Select(FormatCuratedMineral));
        DetailInferred.Text = inferred.Count == 0
            ? "—"
            : string.Join(Environment.NewLine, inferred.Take(25).Select(FormatInferredMineral))
              + (inferred.Count > 25 ? $"{Environment.NewLine}… and {inferred.Count - 25} more" : "");

        var crossLink = App.Services.GetRequiredService<RiverLocalityCrossLink>();
        var locStore = App.Services.GetRequiredService<LocalityStore>();
        var nearSites = await crossLink.FindLocalitiesNearRiverAsync(store, locStore, _selected, corridorRadiusKm: 5, limit: 12);
        DetailLocalities.Text = nearSites.Count == 0
            ? "No collecting localities within 5 km of river corridor."
            : string.Join(Environment.NewLine, nearSites.Select(l =>
                $"• {l.Name} ({l.StateCode}) — {string.Join(", ", l.ReportedMinerals.Take(3))}"));

        DetailSources.Text = _selected.Sources.Count > 0
            ? string.Join(" · ", _selected.Sources)
            : "—";
    }

    private static string FormatCuratedMineral(RiverMineralAssociation m) =>
        $"• {m.MineralName}" +
        (m.Notes is { Length: > 0 } n ? $" — {n}" : "") +
        (m.Citation is { Length: > 0 } c ? $"{Environment.NewLine}  ↳ {c}" : "");

    private static string FormatInferredMineral(RiverMineralAssociation m) =>
        $"• {m.MineralName} ({m.Confidence}, {m.DistanceKm:F1} km)" +
        (m.Notes is { Length: > 0 } n ? $" — {n}" : "");

    private static string MineKindLabel(Watercourse w) => w.ExternalId?.StartsWith("seed:", StringComparison.Ordinal) == true
        ? "seed"
        : w.SourceDataset ?? "imported";

    private void ShowDetailPlaceholder()
    {
        _selected = null;
        DetailTitle.Text = "Select a river";
        DetailMeta.Text = "Search by state, name, or mineral. Toggle curated-only to hide inferred nearby links.";
        DetailCurated.Text = "—";
        DetailInferred.Text = "—";
        DetailLocalities.Text = "—";
        DetailSources.Text = "—";
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
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

            var store = App.Services.GetRequiredService<RiverStore>();
            await store.InitializeAsync();
            var importer = App.Services.GetRequiredService<NhdWatercourseImporter>();
            var result = await importer.ImportFileAsync(file.Path, store);

            await new ContentDialog
            {
                Title = "NHD import complete",
                Content = $"Imported {result.Imported} named watercourses from {result.FeaturesRead} features.\nSkipped unnamed: {result.SkippedUnnamed}.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();

            await SearchAsync();
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Import failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private async void Enrich_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var store = App.Services.GetRequiredService<RiverStore>();
            var locStore = App.Services.GetRequiredService<LocalityStore>();
            await store.InitializeAsync();
            await locStore.InitializeAsync();

            var enricher = new RiverMineralEnricher();
            var result = await enricher.EnrichFromLocalitiesAsync(store, locStore, corridorRadiusKm: 3);

            await new ContentDialog
            {
                Title = "Proximity enrichment",
                Content = $"Scanned {result.WatercoursesScanned} rivers.\nAdded {result.AssociationsAdded} inferred mineral links.\nSkipped {result.SkippedCuratedDup} curated duplicates.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();

            await SearchAsync();
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Enrichment failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private static RiverRow ToRow(Watercourse w, IReadOnlyList<RiverMineralAssociation> minerals)
    {
        var curated = minerals.Count(m => m.AssociationKind == MineralAssociationKind.Curated);
        var inferred = minerals.Count(m => m.AssociationKind == MineralAssociationKind.ProximityInferred);
        var mineralPreview = string.Join(", ", minerals.Select(m => m.MineralName).Distinct().Take(4));
        return new RiverRow(
            w.Id,
            $"{w.Name} · {w.StateCode}",
            $"{w.Kind} · {curated} documented · {inferred} nearby{(mineralPreview.Length > 0 ? $" · {mineralPreview}" : "")}");
    }

    private sealed record RiverRow(Guid Id, string Title, string Meta);
}
