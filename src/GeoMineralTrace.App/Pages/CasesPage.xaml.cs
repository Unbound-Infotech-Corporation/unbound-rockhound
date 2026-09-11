using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace.Pipeline.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace GeoMineralTrace_App.Pages;

public sealed partial class CasesPage : Page
{
    private readonly AnalysisSessionContext _session;
    private DispatcherTimer? _refreshTimer;
    private Guid? _selectedId;

    public CasesPage()
    {
        InitializeComponent();
        _session = App.Services.GetRequiredService<AnalysisSessionContext>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await RefreshAsync();
        _session.LiveProgressChanged += OnLiveProgressChanged;
        _refreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick -= RefreshTimer_Tick;
        _refreshTimer.Tick += RefreshTimer_Tick;
        _refreshTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _session.LiveProgressChanged -= OnLiveProgressChanged;
        if (_refreshTimer is not null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= RefreshTimer_Tick;
        }
    }

    private async void RefreshTimer_Tick(object? sender, object e) => await RefreshAsync();

    private void OnLiveProgressChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(async () =>
        {
            try { await RefreshAsync(); }
            catch { /* ignore UI refresh races */ }
        });

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            var summaries = await _session.ListCasesAsync();
            var rows = summaries.Select(s => CaseRow.From(s, _session)).ToList();
            CaseList.ItemsSource = rows;
            CaseEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CaseList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            if (_session.CurrentSessionId is { } active)
            {
                CaseList.SelectedItem = rows.FirstOrDefault(r => r.Id == active);
                _selectedId = active;
            }
        }
        catch (Exception ex)
        {
            CaseEmpty.Description = $"Could not load cases: {ex.Message}";
            CaseEmpty.Visibility = Visibility.Visible;
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs e) => await OpenSelectedAsync();

    private async void CaseList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        await OpenSelectedAsync();

    private async Task OpenSelectedAsync()
    {
        if (CaseList.SelectedItem is not CaseRow row)
            return;

        try
        {
            await _session.OpenCaseAsync(row.Id);
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

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (CaseList.SelectedItem is not CaseRow row)
            return;

        var confirm = await new ContentDialog
        {
            Title = "Delete this analysis case?",
            Content = $"Permanently delete \"{row.Title}\" and its saved evidence/hypotheses from disk.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        }.ShowAsync();

        if (confirm != ContentDialogResult.Primary)
            return;

        await _session.DeleteCaseAsync(row.Id);
        await RefreshAsync();
    }

    private void Analyze_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(AnalysisPage), "analysis");

    private sealed class CaseRow
    {
        public required Guid Id { get; init; }
        public required string Title { get; init; }
        public required string Subtitle { get; init; }
        public required string StatusLabel { get; init; }
        public required string CountsLabel { get; init; }
        public required string ProgressLine { get; init; }

        public static CaseRow From(AnalysisCaseSummary s, AnalysisSessionContext ctx)
        {
            var live = ctx.LiveRuns.TryGetValue(s.Id, out var lp) ? lp : null;
            var status = live is not null ? AnalysisStatus.Running : s.Status;
            var stage = live?.Stage ?? s.CurrentStage ?? "—";
            var frac = live?.Fraction ?? s.ProgressFraction;
            var msg = live?.Message;

            var kind = s.SourceKind.ToString();
            var when = s.CreatedAtUtc.ToLocalTime().ToString("g");
            var url = string.IsNullOrWhiteSpace(s.SourceUrl) ? "" : $" · {s.SourceUrl}";
            var active = ctx.CurrentSessionId == s.Id ? " · VIEWING" : "";

            return new CaseRow
            {
                Id = s.Id,
                Title = s.DisplayName + active,
                Subtitle = $"{kind} · {when}{url}",
                StatusLabel = status.ToString(),
                CountsLabel = $"{s.EvidenceCount} evid · {s.HypothesisCount} hyp",
                ProgressLine = status is AnalysisStatus.Running or AnalysisStatus.Created or AnalysisStatus.Queued
                    ? $"{stage} ({frac:P0}){(string.IsNullOrWhiteSpace(msg) ? "" : " — " + msg)}"
                    : status == AnalysisStatus.Completed && s.HypothesisCount == 0
                        ? (s.FusionEmptyReason ?? "Completed with 0 hypotheses")
                        : status == AnalysisStatus.Failed
                            ? (s.ErrorMessage ?? "Failed")
                            : status == AnalysisStatus.Cancelled
                                ? "Cancelled"
                                : $"Completed · {s.HypothesisCount} hypothesis(es)"
            };
        }
    }
}


