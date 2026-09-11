using GeoMineralTrace.Rockhounding.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GeoMineralTrace_App.Pages;

public sealed partial class GlossaryCreditsPage : Page
{
    public GlossaryCreditsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var store = App.Services.GetRequiredService<MineralGlossaryStore>();
            await store.InitializeAsync().ConfigureAwait(true);
            var attrs = await store.ListAllAttributionsAsync().ConfigureAwait(true);
            CreditsList.ItemsSource = attrs.Select(a => new CreditRow(
                a.SpeciesId,
                a.LicenseType,
                a.AttributionText,
                a.SourceUrl,
                Uri.TryCreate(a.SourceUrl, UriKind.Absolute, out var u) ? u : null)).ToList();
        }
        catch (Exception ex)
        {
            CreditsList.ItemsSource = new[]
            {
                new CreditRow("(error)", "", ex.Message, "", null)
            };
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(GlossaryPage), "glossary");

    private sealed record CreditRow(
        string SpeciesId,
        string LicenseType,
        string AttributionText,
        string SourceUrl,
        Uri? SourceUri);
}
