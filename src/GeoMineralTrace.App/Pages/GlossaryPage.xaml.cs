using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Rockhounding.Storage;
using GeoMineralTrace_App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace GeoMineralTrace_App.Pages;

public sealed partial class GlossaryPage : Page
{
    private readonly MineralGlossaryStore _glossary;
    private readonly LocalityStore _localities;
    private MineralSpecies? _selected;
    private string? _pendingDeepLink;
    private bool _loaded;

    public GlossaryPage()
    {
        InitializeComponent();
        _glossary = App.Services.GetRequiredService<MineralGlossaryStore>();
        _localities = App.Services.GetRequiredService<LocalityStore>();
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            UsStateFilterHelper.Populate(StateBox);
            _loaded = true;
            await EnsureReadyAsync();
            await RefreshListAsync();
            if (!string.IsNullOrWhiteSpace(_pendingDeepLink))
                await SelectSpeciesAsync(_pendingDeepLink);
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string s && !string.IsNullOrWhiteSpace(s))
            _pendingDeepLink = s.Trim();
    }

    private async Task EnsureReadyAsync()
    {
        await _glossary.InitializeAsync().ConfigureAwait(true);
        await _localities.InitializeAsync().ConfigureAwait(true);
        var systems = await _glossary.ListCrystalSystemsAsync().ConfigureAwait(true);
        CrystalBox.Items.Clear();
        CrystalBox.Items.Add("(any crystal system)");
        foreach (var c in systems)
            CrystalBox.Items.Add(c);
        CrystalBox.SelectedIndex = 0;
    }

    private async void Filter_Changed(object sender, object e)
    {
        if (!_loaded) return;
        await RefreshListAsync();
    }

    private async void Mohs_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loaded) return;
        await RefreshListAsync();
    }

    private async Task RefreshListAsync()
    {
        try
        {
            double? mohsMin = double.IsNaN(MohsMinBox.Value) ? null : MohsMinBox.Value;
            double? mohsMax = double.IsNaN(MohsMaxBox.Value) ? null : MohsMaxBox.Value;
            var crystal = CrystalBox.SelectedIndex > 0 ? CrystalBox.SelectedItem?.ToString() : null;
            var state = UsStateFilterHelper.ResolveStateCode(StateBox);

            var filter = new MineralSpeciesFilter
            {
                NameQuery = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
                CrystalSystem = crystal,
                MohsMin = mohsMin,
                MohsMax = mohsMax,
                ColorContains = string.IsNullOrWhiteSpace(ColorBox.Text) ? null : ColorBox.Text.Trim(),
                StateCode = state
            };

            Func<string, Task<IReadOnlyList<string>>>? stateMinerals = null;
            if (!string.IsNullOrWhiteSpace(state))
            {
                stateMinerals = async code =>
                    await _localities.ListDistinctMineralsForStateAsync(code).ConfigureAwait(false);
            }

            var results = await _glossary.SearchAsync(filter, stateMinerals).ConfigureAwait(true);
            var rows = results.Select(s => new SpeciesRow(s)).ToList();
            SpeciesList.ItemsSource = rows;

            if (_selected is not null)
            {
                var match = rows.FirstOrDefault(r => r.Id == _selected.Id);
                if (match is not null)
                    SpeciesList.SelectedItem = match;
            }
            else if (rows.Count > 0 && _pendingDeepLink is null)
            {
                SpeciesList.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            DetailTitle.Text = "Glossary error";
            DetailDescription.Text = ex.Message;
        }
    }

    private async void SpeciesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeciesList.SelectedItem is not SpeciesRow row)
            return;
        await ShowDetailAsync(row.Species);
    }

    private async Task SelectSpeciesAsync(string idOrName)
    {
        var species = await _glossary.GetByIdOrNameAsync(idOrName).ConfigureAwait(true);
        if (species is null)
        {
            DetailTitle.Text = idOrName;
            DetailDescription.Text = "No glossary entry matches that name yet.";
            return;
        }

        await ShowDetailAsync(species);
        if (SpeciesList.ItemsSource is IEnumerable<SpeciesRow> rows)
        {
            var match = rows.FirstOrDefault(r => r.Id == species.Id);
            if (match is not null)
                SpeciesList.SelectedItem = match;
        }
    }

    private async Task ShowDetailAsync(MineralSpecies species)
    {
        _selected = species;
        LocalitiesButton.IsEnabled = true;
        LocalityHits.Visibility = Visibility.Collapsed;
        LocalityHits.ItemsSource = null;

        DetailTitle.Text = species.Name;
        DetailAliases.Text = species.Aliases.Count > 0
            ? "Also known as: " + string.Join(", ", species.Aliases)
            : "";

        var props = new List<PropRow>();
        if (!string.IsNullOrWhiteSpace(species.Formula))
            props.Add(new PropRow("Formula", species.Formula!));
        if (!string.IsNullOrWhiteSpace(species.CrystalSystem))
            props.Add(new PropRow("Crystal system", species.CrystalSystem!));
        if (species.MohsMin is not null || species.MohsMax is not null)
        {
            var mohs = species.MohsMin is not null && species.MohsMax is not null
                       && Math.Abs(species.MohsMin.Value - species.MohsMax.Value) > 0.05
                ? $"{species.MohsMin:0.#}–{species.MohsMax:0.#}"
                : $"{species.MohsMin ?? species.MohsMax:0.#}";
            props.Add(new PropRow("Mohs", mohs));
        }
        if (!string.IsNullOrWhiteSpace(species.ColorRange))
            props.Add(new PropRow("Color", species.ColorRange!));
        if (!string.IsNullOrWhiteSpace(species.Luster))
            props.Add(new PropRow("Luster", species.Luster!));
        if (!string.IsNullOrWhiteSpace(species.ImaStatus))
            props.Add(new PropRow("Status", species.ImaStatus!));
        if (species.ImaYear is not null)
            props.Add(new PropRow("IMA year", species.ImaYear.Value.ToString()));
        if (!string.IsNullOrWhiteSpace(species.WikidataId))
            props.Add(new PropRow("Wikidata", species.WikidataId!));
        PropertyList.ItemsSource = props;
        DetailDescription.Text = species.Description;

        var images = await _glossary.ListImagesAsync(species.Id).ConfigureAwait(true);
        var gallery = new List<ImageRow>();
        foreach (var img in images)
        {
            BitmapImage? bmp = null;
            if (!string.IsNullOrWhiteSpace(img.FilePath) && File.Exists(img.FilePath))
            {
                bmp = new BitmapImage();
                bmp.UriSource = new Uri(img.FilePath);
            }

            if (bmp is null)
                continue;

            gallery.Add(new ImageRow(bmp, img.AttributionText));
        }

        ImageGallery.ItemsSource = gallery;
        NoImageText.Visibility = gallery.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Localities_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;

        try
        {
            var hits = await _localities.SearchByMineralAsync(_selected.Name, limit: 40).ConfigureAwait(true);
            if (hits.Count == 0)
            {
                foreach (var alias in _selected.Aliases)
                {
                    hits = await _localities.SearchByMineralAsync(alias, limit: 40).ConfigureAwait(true);
                    if (hits.Count > 0) break;
                }
            }

            if (hits.Count > 0)
            {
                MainWindow.Instance?.NavigateTo(typeof(RockhoundingPage), "rockhounding", _selected.Name);
                return;
            }

            LocalityHits.ItemsSource = new[] { "No localities currently report this mineral in the local registry." };
            LocalityHits.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LocalityHits.ItemsSource = new[] { ex.Message };
            LocalityHits.Visibility = Visibility.Visible;
        }
    }

    private void Credits_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(GlossaryCreditsPage), "glossary-credits");

    private sealed class SpeciesRow
    {
        public SpeciesRow(MineralSpecies s)
        {
            Species = s;
            Id = s.Id;
            Name = s.Name;
            Subtitle = string.Join(" · ", new[]
            {
                s.Formula,
                s.CrystalSystem,
                s.MohsMin is not null
                    ? $"Mohs {s.MohsMin:0.#}" + (s.MohsMax is not null && Math.Abs(s.MohsMax.Value - s.MohsMin.Value) > 0.05
                        ? $"–{s.MohsMax:0.#}"
                        : "")
                    : null,
                s.ImageCount > 0 ? $"{s.ImageCount} img" : "text-only"
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        public MineralSpecies Species { get; }
        public string Id { get; }
        public string Name { get; }
        public string Subtitle { get; }
    }

    private sealed record PropRow(string Key, string Value);
    private sealed record ImageRow(BitmapImage Bitmap, string Attribution);
}
