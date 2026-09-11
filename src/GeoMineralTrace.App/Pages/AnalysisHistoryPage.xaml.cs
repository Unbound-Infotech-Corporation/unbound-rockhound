using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GeoMineralTrace_App.Pages;

public sealed partial class AnalysisHistoryPage : Page
{
    private readonly AnalysisHistoryStore _history;
    private readonly AnalysisSessionContext _session;
    private List<AnalysisHistoryEntry> _entries = [];

    public AnalysisHistoryPage()
    {
        InitializeComponent();
        _history = App.Services.GetRequiredService<AnalysisHistoryStore>();
        _session = App.Services.GetRequiredService<AnalysisSessionContext>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        _entries = (await _history.ListAsync()).ToList();
        var rows = _entries.Select(HistoryRow.From).ToList();
        HistoryList.ItemsSource = rows;
        HistoryEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ViewOnMap_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid sessionId })
            return;

        var entry = _entries.FirstOrDefault(x => x.SessionId == sessionId);
        if (entry is null)
            return;

        try
        {
            await _session.OpenCaseAsync(sessionId);
            var hypotheses = _session.LastPipelineResult?.Hypotheses
                .Where(h => h.Center is not null)
                .ToList();
            if (hypotheses is { Count: > 0 })
            {
                MapFocusService.NavigateToHypotheses(hypotheses, entry.LeadHypothesisId);
                return;
            }

            MapFocusService.NavigateFromHistory(entry);
        }
        catch (Exception ex)
        {
            try
            {
                MapFocusService.NavigateFromHistory(entry);
            }
            catch
            {
                await new ContentDialog
                {
                    Title = "Cannot open map",
                    Content = ex.Message,
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
            }
        }
    }

    private async void OpenHypotheses_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid sessionId })
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
                Title = "Could not open case",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid sessionId })
            return;

        var entry = _entries.FirstOrDefault(x => x.SessionId == sessionId);
        if (entry is null)
            return;

        var confirm = await new ContentDialog
        {
            Title = "Remove from history?",
            Content = $"Remove “{entry.Title}” from the history list. Session files on disk are kept — delete those from Cases if needed.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        }.ShowAsync();

        if (confirm != ContentDialogResult.Primary)
            return;

        await _history.RemoveAsync(sessionId);
        await RefreshAsync();
    }

    private sealed class HistoryRow
    {
        public Guid SessionId { get; init; }
        public required string Title { get; init; }
        public required string Subtitle { get; init; }
        public required string LeadSummary { get; init; }
        public required string ConfidenceLabel { get; init; }

        public static HistoryRow From(AnalysisHistoryEntry e)
        {
            var when = e.CompletedAtUtc.ToLocalTime().ToString("g");
            var lead = string.IsNullOrWhiteSpace(e.LeadLabel) ? "No lead hypothesis saved" : e.LeadLabel;
            var conf = e.LeadConfidence is { } c ? $"{c:P0} confidence" : "—";
            var summary = e.LeadReasoningSummary ?? "Open hypotheses for full reasoning.";
            if (e.LeadLatitude is { } lat && e.LeadLongitude is { } lon)
                summary += $" · {lat:F4}°, {lon:F4}°";

            return new HistoryRow
            {
                SessionId = e.SessionId,
                Title = e.Title,
                Subtitle = $"{when} · {e.HypothesisCount} hypothesis(es)" +
                           (string.IsNullOrWhiteSpace(e.SourceUrl) ? "" : " · video URL"),
                LeadSummary = $"{lead} — {summary}",
                ConfidenceLabel = conf
            };
        }
    }
}
