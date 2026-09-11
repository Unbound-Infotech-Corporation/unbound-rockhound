using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.DeepAnalysis;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Core.Solar;
using GeoMineralTrace.Evidence.Store;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace.Pipeline;
using GeoMineralTrace.Pipeline.Diagnostics;
using GeoMineralTrace.Solar;
using GeoMineralTrace_App.Helpers;
using GeoMineralTrace_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI.Text;

namespace GeoMineralTrace_App.Pages;

public sealed partial class HypothesesPage : Page
{
    private readonly AnalysisSessionContext _session;
    private readonly DeepAnalysisOptions _deepOptions;
    private List<LocationHypothesis> _hypotheses = [];
    private LocationHypothesis? _lead;
    private bool _emptyActionWired;

    public HypothesesPage()
    {
        InitializeComponent();
        _session = App.Services.GetRequiredService<AnalysisSessionContext>();
        _deepOptions = App.Services.GetRequiredService<DeepAnalysisOptions>();
        Loaded += (_, _) =>
        {
            if (_emptyActionWired)
                return;
            HypothesisEmpty.ActionLabel = "Fuse sample evidence";
            HypothesisEmpty.ActionClick += FuseSample_Click;
            _emptyActionWired = true;
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadFromSessionAsync();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) =>
        await LoadFromSessionAsync(refuse: true);

    private async Task LoadFromSessionAsync(bool refuse = false)
    {
        try
        {
            if (refuse || (_session.LastPipelineResult is null && _session.CurrentSessionId is not null))
                await _session.RefuseAsync();

            var result = _session.LastPipelineResult ?? AnalysisPage.LastResult;
            if (result is null)
            {
                _hypotheses = [];
                _lead = null;
                Bind();
                SolarGapBanner.Visibility = Visibility.Collapsed;
                NearbyList.ItemsSource = null;
                return;
            }

            AnalysisPage.LastResult = result;
            _hypotheses = result.Hypotheses.ToList();
            _lead = HypothesisPresentation.SelectLead(_hypotheses);
            Bind();
            UpdateSolarGapBanner(result);
            NearbyList.ItemsSource = result.NearbyLocalities.Select(FormatNearby).ToList();
        }
        catch (Exception ex)
        {
            NearbyList.ItemsSource = new[] { $"Error: {ex.Message}" };
        }
    }

    private void Bind()
    {
        var rows = _hypotheses
            .OrderBy(h => h.Rank)
            .Select(h => new HypothesisRow(h))
            .ToList();

        HypothesisList.ItemsSource = rows;
        var hasAny = rows.Count > 0;
        HypothesisEmpty.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
        HypothesisList.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;

        if (!hasAny)
        {
            LeadHeroPanel.Visibility = Visibility.Collapsed;
            RunnersExpander.Visibility = Visibility.Collapsed;
            var reason = _session.LastPipelineResult?.Session.FusionEmptyReason;
            HypothesisEmpty.Description = string.IsNullOrWhiteSpace(reason)
                ? "No location hypotheses for the active case. Run Analyze, open a Cases record, or fuse sample evidence."
                : reason;
            HypothesisEmpty.Title = _session.LastPipelineResult is null
                ? "No active case"
                : "No hypotheses for this case";
        }
        else
        {
            BindLeadHero();
            BindRunners();
        }

        var boundId = _session.CurrentSessionId ?? _session.LastPipelineResult?.Session.Id;
        if (boundId is { } sid)
        {
            AnalysisDiagnosticLog.AppendSection(sid, "UI HANDOFF — HypothesesPage.Bind",
            [
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] Hypotheses page rebound",
                $"Hypothesis rows displayed:  {rows.Count}",
                _lead is not null ? $"Lead label:               {_lead.Label}" : "Lead label:               (none)"
            ]);
        }

        UpdateSolarGapBanner(_session.LastPipelineResult);
    }

    private void BindLeadHero()
    {
        if (_lead is null)
        {
            LeadHeroPanel.Visibility = Visibility.Collapsed;
            UpdateGlossaryButton(null);
            return;
        }

        LeadHeroPanel.Visibility = Visibility.Visible;
        LeadTitle.Text = _lead.Label;
        LeadCoordinates.Text = HypothesisPresentation.FormatCoordinates(_lead.Center);
        LeadConfidence.Text = $"{_lead.Confidence.Value:P0}";
        LeadConfidence.Foreground = ConfidenceBrushFor(_lead.Confidence.Value);
        LeadReasoning.Text = HypothesisPresentation.BuildReasoningSummary(_lead, 3);
        ViewOnMapButton.IsEnabled = _lead.Center is not null;
        AddToTripButton.IsEnabled = _lead.Center is not null;
        UpdateGlossaryButton(_lead);
    }

    private void UpdateGlossaryButton(LocationHypothesis? h)
    {
        var mineral = DetectMineralName(h);
        if (mineral is null)
        {
            OpenGlossaryButton.Visibility = Visibility.Collapsed;
            OpenGlossaryButton.Tag = null;
            return;
        }

        OpenGlossaryButton.Visibility = Visibility.Visible;
        OpenGlossaryButton.Content = $"Open “{mineral}” in Glossary";
        OpenGlossaryButton.Tag = mineral;
    }

    private string? DetectMineralName(LocationHypothesis? h)
    {
        if (h is null) return null;

        var evidence = _session.LastPipelineResult?.Evidence
                       ?? App.Services.GetRequiredService<EvidenceBoardStore>().All();
        foreach (var id in h.SupportingEvidenceIds)
        {
            var item = evidence.FirstOrDefault(e => e.Id == id);
            if (item is { Type: EvidenceType.MineralNameMention }
                && !string.IsNullOrWhiteSpace(item.RawContent))
                return item.RawContent.Trim();
        }

        var haystack = string.Join(' ',
            new[] { h.Label }
                .Concat(h.Reasoning ?? Array.Empty<string>())
                .Concat(h.UncertaintyNotes ?? Array.Empty<string>()));
        if (string.IsNullOrWhiteSpace(haystack))
            return null;

        var lower = haystack.ToLowerInvariant();
        string[] lexicon =
        [
            "obsidian", "garnet", "almandine", "diamond", "turquoise", "opal", "agate", "jasper",
            "amethyst", "quartz", "fluorite", "beryl", "aquamarine", "emerald", "topaz", "tourmaline",
            "peridot", "jade", "malachite", "azurite", "pyrite", "gold", "copper", "silver",
            "sunstone", "labradorite", "moonstone", "calcite", "hematite", "chalcedony"
        ];
        return lexicon.FirstOrDefault(m => lower.Contains(m, StringComparison.Ordinal));
    }

    private void OpenGlossary_Click(object sender, RoutedEventArgs e)
    {
        var mineral = OpenGlossaryButton.Tag as string
                      ?? DetectMineralName(_lead);
        if (string.IsNullOrWhiteSpace(mineral))
            return;
        MainWindow.Instance?.NavigateTo(typeof(GlossaryPage), "glossary", mineral);
    }

    private void BindRunners()
    {
        var runners = HypothesisPresentation.SelectRunnersUp(_hypotheses, _deepOptions)
            .Select(h => new RunnerRow(h))
            .ToList();
        RunnersExpander.Visibility = runners.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RunnersHeader.Text = runners.Count == 1
            ? "1 alternate candidate"
            : $"{runners.Count} alternate candidates";
        RunnersList.ItemsSource = runners;
    }

    private static Brush ConfidenceBrushFor(double value) =>
        value >= 0.65
            ? (Brush)Application.Current.Resources["GmtBrushConfidenceHigh"]
            : value >= 0.4
                ? (Brush)Application.Current.Resources["GmtBrushConfidenceMedium"]
                : (Brush)Application.Current.Resources["GmtBrushConfidenceLow"];

    private void ViewOnMap_Click(object sender, RoutedEventArgs e) =>
        NavigateMap(highlightId: _lead?.Id);

    private void RunnerMap_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
            NavigateMap(highlightId: id);
    }

    private void NavigateMap(Guid? highlightId)
    {
        try
        {
            var geolocated = _hypotheses.Where(h => h.Center is not null).ToList();
            if (geolocated.Count == 0)
                return;
            MapFocusService.NavigateToHypotheses(geolocated, highlightId);
        }
        catch (Exception ex)
        {
            _ = new ContentDialog
            {
                Title = "Cannot show on map",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private async void AddToTrip_Click(object sender, RoutedEventArgs e)
    {
        if (_lead is null)
            return;
        var session = _session.LastPipelineResult?.Session;
        await TripActions.AddHypothesisToTripAsync(
            XamlRoot,
            _lead,
            session?.Id,
            session?.DisplayName);
    }

    private async void AcceptLead_Click(object sender, RoutedEventArgs e)
    {
        if (_lead is null)
            return;
        HypothesisList.SelectedItem = HypothesisList.Items
            .OfType<HypothesisRow>()
            .FirstOrDefault(r => r.Id == _lead.Id);
        await SetDispositionAsync(HypothesisDisposition.Accepted);
    }

    private async void RejectLead_Click(object sender, RoutedEventArgs e)
    {
        if (_lead is null)
            return;
        HypothesisList.SelectedItem = HypothesisList.Items
            .OfType<HypothesisRow>()
            .FirstOrDefault(r => r.Id == _lead.Id);
        await SetDispositionAsync(HypothesisDisposition.Rejected);
    }

    private void OpenEvidence_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(EvidenceBoardPage), "evidence");

    private void UpdateSolarGapBanner(AnalysisPipelineResult? result)
    {
        var hasSolar = result?.SolarLocus is not null || _session.LastSolarLocus is not null;
        SolarGapBanner.Visibility = hasSolar ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpenSolar_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(SolarAnalysisPage), "solar");

    private static string FormatNearby(NearbyLocalityHit n)
    {
        var status = n.HumanActivityDataMayBeOutdated
            ? $"{n.AccessStatus} (outdated)"
            : n.AccessStatus;
        var source = n.SourceDataset is { } ds
            ? $" · {ds}" + (n.SourceVintage is { } v ? $" {v}" : "")
            : "";
        var legal = n.LegalClarity is { } lc ? $" · legal {lc:F1}" : "";
        return $"{n.Name} ({n.StateCode}) · {status} · rating {n.Rating:F1}{legal}{source}" +
               (n.DistanceKm is { } d ? $" · {d:F1} km" : "") +
               (n.AccessStatus is "Closed" or "Restricted" || n.HumanActivityDataMayBeOutdated
                   ? " [FLAGGED]"
                   : "");
    }

    private async void FuseSample_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var session = Guid.NewGuid();
            var store = App.Services.GetRequiredService<EvidenceBoardStore>();
            var locusEngine = App.Services.GetRequiredService<SolarLocusEngine>();

            _session.BeginSyntheticBoard(session);
            AnalysisPage.LastResult = null;
            store.AddRange(
            [
                new EvidenceItem
                {
                    Id = Guid.NewGuid(),
                    AnalysisSessionId = session,
                    Type = EvidenceType.PlaceNameMention,
                    Summary = "Maury Mountain",
                    RawContent = "Maury Mountain",
                    Confidence = Confidence.Medium
                },
                new EvidenceItem
                {
                    Id = Guid.NewGuid(),
                    AnalysisSessionId = session,
                    Type = EvidenceType.MineralNameMention,
                    Summary = "obsidian",
                    RawContent = "obsidian",
                    Confidence = Confidence.High
                }
            ]);

            var measurement = new ShadowMeasurement
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                EvidenceId = null,
                SourceMediaPath = "sample.mp4",
                ObservationUtc = new DateTimeOffset(2024, 6, 21, 19, 0, 0, TimeSpan.Zero),
                ObjectHeight = 1.0,
                ShadowLength = 0.85,
                MeasurementConfidence = Confidence.High
            };

            var locus = locusEngine.ComputeLocus(
                session, [measurement], new LocusSearchBounds(42, 46, -122, -118), 1.0);

            _session.LastSolarLocus = locus;
            _session.LastPipelineResult = new AnalysisPipelineResult
            {
                Session = new AnalysisSession
                {
                    Id = session,
                    DisplayName = "Sample fusion",
                    Status = AnalysisStatus.Completed,
                    SourceMediaPaths = ["sample.mp4"]
                },
                Evidence = store.All(),
                Hypotheses = [],
                SolarLocus = locus,
                NearbyLocalities = []
            };

            await _session.RefuseAsync();
            AnalysisPage.LastResult = _session.LastPipelineResult;
            await LoadFromSessionAsync();
        }
        catch (Exception ex)
        {
            NearbyList.ItemsSource = new[] { $"Error: {ex.Message}" };
        }
    }

    private async Task SetDispositionAsync(HypothesisDisposition disposition)
    {
        if (HypothesisList.SelectedItem is not HypothesisRow row)
            return;
        var h = _hypotheses.FirstOrDefault(x => x.Id == row.Id);
        if (h is null)
            return;
        h.Disposition = disposition;

        if (_session.LastPipelineResult is not null)
        {
            _session.LastPipelineResult = new AnalysisPipelineResult
            {
                Session = _session.LastPipelineResult.Session,
                Evidence = _session.LastPipelineResult.Evidence,
                Hypotheses = _hypotheses,
                SolarLocus = _session.LastPipelineResult.SolarLocus,
                NearbyLocalities = _session.LastPipelineResult.NearbyLocalities
            };
            AnalysisPage.LastResult = _session.LastPipelineResult;
            await _session.PersistAsync();
        }

        _lead = HypothesisPresentation.SelectLead(_hypotheses);
        Bind();
    }

    private sealed class RunnerRow(LocationHypothesis h)
    {
        public Guid Id { get; } = h.Id;
        public string Title { get; } = h.Label;
        public string Coordinates { get; } = HypothesisPresentation.FormatCoordinates(h.Center);
        public string Summary { get; } = HypothesisPresentation.BuildReasoningSummary(h);
        public string ConfidenceLabel { get; } = $"{h.Confidence.Value:P0}";
    }

    private sealed class HypothesisRow
    {
        public HypothesisRow(LocationHypothesis h)
        {
            Id = h.Id;
            var visual = ConfidenceVisual.ForHypothesis(h);
            RowOpacity = visual.RowOpacity;
            DetailOpacity = visual.DetailOpacity;
            TitleWeight = visual.TitleWeight;
            ConfidenceWeight = visual.ConfidenceWeight;
            TitleBrush = visual.TitleBrush;
            DetailBrush = visual.DetailBrush;
            ConfidenceBrush = visual.ConfidenceBrush;

            Title = $"{h.Disposition}: {h.Label}";
            var corroboration = visual.CorroborationNote is { Length: > 0 } note ? $" · {note}" : "";
            Detail = $"rank={h.Rank} · conf={h.Confidence} · evidence={h.SupportingEvidenceIds.Count}{corroboration}" +
                     (h.Center is { } c ? $" · {c.LatitudeDegrees:F3},{c.LongitudeDegrees:F3}" : "");
            ConfidenceLabel = h.Confidence.ToString();
        }

        public Guid Id { get; }
        public string Title { get; }
        public string Detail { get; }
        public string ConfidenceLabel { get; }
        public double RowOpacity { get; }
        public double DetailOpacity { get; }
        public FontWeight TitleWeight { get; }
        public FontWeight ConfidenceWeight { get; }
        public Brush TitleBrush { get; }
        public Brush DetailBrush { get; }
        public Brush ConfidenceBrush { get; }
    }
}
