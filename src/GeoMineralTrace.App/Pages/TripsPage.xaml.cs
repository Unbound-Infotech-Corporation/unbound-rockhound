using GeoMineralTrace.Core.Trips;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GeoMineralTrace_App.Pages;

public sealed partial class TripsPage : Page
{
    private readonly TripStore _trips;
    private readonly AnalysisSessionContext _session;
    private List<Trip> _tripCatalog = [];
    private List<WaypointRow> _waypointRows = [];

    public TripsPage()
    {
        InitializeComponent();
        _trips = App.Services.GetRequiredService<TripStore>();
        _session = App.Services.GetRequiredService<AnalysisSessionContext>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void NewTrip_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "Trip name", Text = "Field trip" };
        var result = await new ContentDialog
        {
            Title = "New trip",
            Content = nameBox,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        }.ShowAsync();

        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
            return;

        await _trips.CreateTripAsync(nameBox.Text);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _tripCatalog = (await _trips.ListTripsAsync()).ToList();
        TripList.ItemsSource = _tripCatalog.Select(t => $"{t.Name} · {t.CreatedAtUtc.ToLocalTime():d}").ToList();
        TripEmpty.Visibility = _tripCatalog.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TripList.Visibility = _tripCatalog.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (_tripCatalog.Count == 0)
        {
            WaypointList.ItemsSource = null;
            WaypointEmpty.Visibility = Visibility.Visible;
        }
    }

    private async void TripList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TripList.SelectedIndex < 0 || TripList.SelectedIndex >= _tripCatalog.Count)
        {
            WaypointEmpty.Visibility = Visibility.Visible;
            WaypointList.ItemsSource = null;
            return;
        }

        var trip = _tripCatalog[TripList.SelectedIndex];
        WaypointHeader.Text = $"WAYPOINTS — {trip.Name.ToUpperInvariant()}";
        var waypoints = await _trips.ListWaypointsAsync(trip.Id);
        _waypointRows = waypoints.Select(WaypointRow.From).ToList();
        WaypointList.ItemsSource = _waypointRows;
        WaypointEmpty.Visibility = _waypointRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ViewAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid waypointId })
            return;
        var row = _waypointRows.FirstOrDefault(w => w.WaypointId == waypointId);
        if (row?.SourceSessionId is not { } sessionId)
            return;

        try
        {
            await _session.OpenCaseAsync(sessionId);
            MainWindow.Instance?.NavigateTo(typeof(HypothesesPage), "hypotheses");
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Could not open analysis",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private void WaypointMap_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid waypointId })
            return;
        var row = _waypointRows.FirstOrDefault(w => w.WaypointId == waypointId);
        if (row is null)
            return;

        MapFocusService.NavigateToMap(new MapFocusRequest
        {
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            Zoom = 12,
            HighlightHypothesisId = row.SourceHypothesisId,
            HypothesisPins =
            [
                new HypothesisMapPin(
                    row.SourceHypothesisId ?? row.WaypointId,
                    row.Latitude,
                    row.Longitude,
                    row.Name,
                    0,
                    row.Notes ?? "Trip waypoint",
                    true,
                    1)
            ]
        });
    }

    private async void RemoveWaypoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid waypointId })
            return;

        await _trips.RemoveWaypointAsync(waypointId);
        TripList_SelectionChanged(TripList, null!);
    }

    private sealed class WaypointRow
    {
        public Guid WaypointId { get; init; }
        public Guid? SourceSessionId { get; init; }
        public Guid? SourceHypothesisId { get; init; }
        public required string Name { get; init; }
        public required string Coordinates { get; init; }
        public string? Notes { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public Visibility VideoBadgeVisibility { get; init; }
        public Visibility ViewAnalysisVisibility { get; init; }

        public static WaypointRow From(Waypoint w) => new()
        {
            WaypointId = w.Id,
            SourceSessionId = w.SourceAnalysisSessionId,
            SourceHypothesisId = w.SourceHypothesisId,
            Name = w.Name,
            Coordinates = $"{w.Latitude:F4}°, {w.Longitude:F4}° · {w.SiteType}",
            Notes = w.Notes ?? w.SourceVideoTitle,
            Latitude = w.Latitude,
            Longitude = w.Longitude,
            VideoBadgeVisibility = w.SourceAnalysisSessionId is not null ? Visibility.Visible : Visibility.Collapsed,
            ViewAnalysisVisibility = w.SourceAnalysisSessionId is not null ? Visibility.Visible : Visibility.Collapsed
        };
    }
}
