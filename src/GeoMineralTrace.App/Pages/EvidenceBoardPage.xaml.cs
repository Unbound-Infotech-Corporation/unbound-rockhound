using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Evidence.Store;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace.Pipeline.Persistence;
using GeoMineralTrace_App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI.Text;

namespace GeoMineralTrace_App.Pages;

public sealed partial class EvidenceBoardPage : Page
{
    private readonly EvidenceBoardStore _store;
    private readonly AnalysisSessionContext _session;
    private readonly AnalysisSessionStore _sessions;
    private Guid? _selectedId;
    private EvidenceItem? _selectedItem;
    private bool _hydrating;
    private bool _emptyActionWired;

    public EvidenceBoardPage()
    {
        InitializeComponent();
        _store = App.Services.GetRequiredService<EvidenceBoardStore>();
        _session = App.Services.GetRequiredService<AnalysisSessionContext>();
        _sessions = App.Services.GetRequiredService<AnalysisSessionStore>();
        TypeFilter.Items.Add("All types");
        foreach (EvidenceType t in Enum.GetValues<EvidenceType>())
            TypeFilter.Items.Add(t.ToString());
        TypeFilter.SelectedIndex = 0;
        Loaded += (_, _) =>
        {
            if (_emptyActionWired)
                return;
            EmptyState.ActionLabel = "Go to Analyze";
            EmptyState.ActionClick += (_, _) => MainWindow.Instance?.NavigateTo(typeof(AnalysisPage), "analysis");
            _emptyActionWired = true;
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await EnsureEvidenceLoadedAsync();
        Refresh();
    }

    private async Task EnsureEvidenceLoadedAsync()
    {
        if (_hydrating)
            return;

        // Prefer the live pipeline result for the current session.
        if (_session.LastPipelineResult?.Evidence is { Count: > 0 } live)
        {
            var sessionId = _session.LastPipelineResult.Session.Id;
            var board = _store.All();
            var mismatched = board.Count == 0
                             || board.Any(e => e.AnalysisSessionId != sessionId)
                             || board.Count != live.Count;
            if (mismatched)
            {
                _store.Clear();
                _store.AddRange(live);
            }

            _session.CurrentSessionId = sessionId;
            return;
        }

        if (_store.All().Count > 0)
            return;

        // After Clear / BeginNewAnalysis, stay empty until the user Load Session or runs Analyze.
        if (!_session.AllowDiskHydrate)
            return;

        var latestId = _sessions.ListSessionIds()
            .Select(id => (Id: id, Path: Path.Combine(_sessions.Root, id.ToString("N"), "session.json")))
            .Where(x => File.Exists(x.Path))
            .OrderByDescending(x => File.GetLastWriteTimeUtc(x.Path))
            .Select(x => x.Id)
            .FirstOrDefault();
        if (latestId == Guid.Empty)
            return;

        try
        {
            _hydrating = true;
            var doc = await _sessions.LoadAsync(latestId);
            if (doc is null || doc.Evidence.Count == 0)
                return;
            await _session.HydrateFromDocumentAsync(doc);
        }
        catch
        {
            // Board stays empty; user can Load session from Analyze.
        }
        finally
        {
            _hydrating = false;
        }
    }

    private void Refresh()
    {
        EvidenceType? type = null;
        if (TypeFilter.SelectedIndex > 0 &&
            Enum.TryParse<EvidenceType>(TypeFilter.SelectedItem?.ToString(), out var parsed))
            type = parsed;

        var items = _store.Filter(type, SearchBox.Text, includeRejected: ShowRejected.IsOn);
        var rows = items.Select(e => new EvidenceRow(e)).ToList();
        EvidenceList.ItemsSource = rows;
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EvidenceList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();
    private void TypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();
    private void ShowRejected_Toggled(object sender, RoutedEventArgs e) => Refresh();

    private void EvidenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EvidenceList.SelectedItem is EvidenceRow row)
        {
            _selectedId = row.Id;
            _selectedItem = row.Item;
        }
        else
        {
            _selectedId = null;
            _selectedItem = null;
        }

        OpenGlossaryButton.IsEnabled =
            _selectedItem is { Type: EvidenceType.MineralNameMention }
            && !string.IsNullOrWhiteSpace(_selectedItem.RawContent);
    }

    private void OpenGlossary_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is not { Type: EvidenceType.MineralNameMention } item
            || string.IsNullOrWhiteSpace(item.RawContent))
            return;

        MainWindow.Instance?.NavigateTo(typeof(GlossaryPage), "glossary", item.RawContent.Trim());
    }

    private async void MeasureShadow_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is not { Type: EvidenceType.Keyframe } item)
        {
            await new ContentDialog
            {
                Title = "Select a keyframe",
                Content = "Choose a Keyframe evidence row with an image preview (requires ffmpeg during analysis).",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(item.PreviewAssetPath) ||
            (!File.Exists(item.PreviewAssetPath) && !File.Exists(item.SourceMediaPath ?? "")))
        {
            await new ContentDialog
            {
                Title = "No keyframe image",
                Content = "This keyframe has no saved bitmap yet. Install ffmpeg and re-run analysis, or use manual entry on Solar / Shadow.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
            return;
        }

        _session.PendingKeyframe = item;
        _session.CurrentSessionId = item.AnalysisSessionId;
        MainWindow.Instance?.NavigateTo(typeof(SolarAnalysisPage), "solar");
    }

    private async void Reject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId is not { } id) return;
        _store.SetRejected(id, true);
        await RefuseAndRefreshAsync();
    }

    private async void AcceptWeight_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId is not { } id) return;
        _store.SetRejected(id, false);
        _store.SetWeight(id, EvidenceWeight.Accepted);
        await RefuseAndRefreshAsync();
    }

    private async void Downweight_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId is not { } id) return;
        _store.SetWeight(id, EvidenceWeight.Downweighted);
        await RefuseAndRefreshAsync();
    }

    private async Task RefuseAndRefreshAsync()
    {
        try
        {
            await _session.RefuseAsync();
            AnalysisPage.LastResult = _session.LastPipelineResult;
        }
        catch
        {
            // Board still updates even if fusion has no session yet.
        }

        Refresh();
    }

    private async void LoadSample_Click(object sender, RoutedEventArgs e)
    {
        var session = Guid.NewGuid();
        // Drop any prior solar locus / pipeline result so sample fusion is not skewed.
        _session.BeginSyntheticBoard(session);
        AnalysisPage.LastResult = null;
        _store.AddRange(
        [
            new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.OcrText,
                Summary = "Road sign: \"US-191\"",
                RawContent = "US-191",
                Confidence = Confidence.High,
                MediaTimestamp = TimeSpan.FromSeconds(42),
                Notes = "Sample OCR clue."
            },
            new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.MineralNameMention,
                Summary = "Audio: \"obsidian\"",
                RawContent = "obsidian",
                Confidence = Confidence.Medium,
                MediaTimestamp = TimeSpan.FromSeconds(91)
            },
            new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.PlaceNameMention,
                Summary = "Maury Mountain",
                RawContent = "Maury Mountain",
                Confidence = Confidence.Medium,
                MediaTimestamp = TimeSpan.FromSeconds(55)
            },
            new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.ShadowMeasurement,
                Summary = "Shadow ratio h/L ≈ 0.85 (≈40.4° elevation)",
                Confidence = Confidence.High,
                MediaTimestamp = TimeSpan.FromSeconds(120)
            }
        ]);

        try
        {
            await _session.RefuseAsync();
            AnalysisPage.LastResult = _session.LastPipelineResult;
        }
        catch
        {
            // ignore
        }

        Refresh();
    }

    private sealed class EvidenceRow
    {
        public EvidenceRow(EvidenceItem item)
        {
            Item = item;
            var visual = ConfidenceVisual.ForEvidence(item);
            RowOpacity = visual.RowOpacity;
            DetailOpacity = visual.DetailOpacity;
            TitleWeight = visual.TitleWeight;
            ConfidenceWeight = visual.ConfidenceWeight;
            TitleBrush = visual.TitleBrush;
            DetailBrush = visual.DetailBrush;
            ConfidenceBrush = visual.ConfidenceBrush;
        }

        public EvidenceItem Item { get; }
        public Guid Id => Item.Id;
        public string Type => Item.Type.ToString();
        public string Summary => Item.IsRejected ? $"[REJECTED] {Item.Summary}" : Item.Summary;
        public string Notes
        {
            get
            {
                var visual = ConfidenceVisual.ForEvidence(Item);
                var hint = visual.CorroborationNote is { Length: > 0 } n ? $" · {n}" : "";
                var deep = Item.DeepScore is { } ds
                    ? $" · deep={ds:F2}{(Item.DeepSelected == true ? " ✓selected" : Item.DeepSelected == false ? " (not selected)" : "")}"
                    : "";
                var deepNote = !string.IsNullOrWhiteSpace(Item.DeepSelectionNote)
                    ? $" · {Item.DeepSelectionNote}"
                    : "";
                return $"{Item.Notes} · weight={Item.UserWeight}{hint}{deep}{deepNote}";
            }
        }
        public string ConfidenceLabel => Item.Confidence.ToString();
        public double RowOpacity { get; }
        public double DetailOpacity { get; }
        public FontWeight TitleWeight { get; }
        public FontWeight ConfidenceWeight { get; }
        public Brush TitleBrush { get; }
        public Brush DetailBrush { get; }
        public Brush ConfidenceBrush { get; }
    }
}