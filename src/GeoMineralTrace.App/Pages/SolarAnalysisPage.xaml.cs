using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Solar;
using GeoMineralTrace.Evidence.Store;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace.Solar;
using GeoMineralTrace_App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GeoMineralTrace_App.Pages;

public sealed partial class SolarAnalysisPage : Page
{
    private readonly SolarLocusEngine _locusEngine;
    private readonly EvidenceBoardStore _board;
    private readonly AnalysisSessionContext _session;
    private EvidenceItem? _selectedKeyframe;
    private Guid _analysisSessionId;

    public SolarAnalysisPage()
    {
        InitializeComponent();
        _locusEngine = App.Services.GetRequiredService<SolarLocusEngine>();
        _board = App.Services.GetRequiredService<EvidenceBoardStore>();
        _session = App.Services.GetRequiredService<AnalysisSessionContext>();
        DatePicker.Date = DateTimeOffset.UtcNow.Date;
        TimePicker.Time = new TimeSpan(18, 0, 0);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _analysisSessionId = _session.CurrentSessionId
            ?? _session.LastPipelineResult?.Session.Id
            ?? Guid.NewGuid();

        if (_session.PendingKeyframe is { } pending)
        {
            await SelectKeyframeAsync(pending);
            _session.ClearPendingKeyframe();
        }

        RefreshKeyframeList();
    }

    private void RefreshKeyframeList()
    {
        var keyframes = _board.Filter(null, null, includeRejected: false)
            .Where(e => e.Type == EvidenceType.Keyframe && !string.IsNullOrWhiteSpace(e.PreviewAssetPath))
            .Where(e => e.AnalysisSessionId == _analysisSessionId ||
                        _session.LastPipelineResult is null)
            .OrderBy(e => e.MediaTimestamp ?? TimeSpan.Zero)
            .Select(e => new KeyframeRow(e))
            .ToList();

        KeyframeList.ItemsSource = keyframes;
        if (keyframes.Count == 0)
            KeyframeMetaText.Text = "No keyframe images yet. Run Analyze with ffmpeg installed, or open Evidence Board → Measure shadow on a keyframe.";
    }

    private async void KeyframeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyframeList.SelectedItem is KeyframeRow row)
            await SelectKeyframeAsync(row.Item);
    }

    private async Task SelectKeyframeAsync(EvidenceItem item)
    {
        _selectedKeyframe = item;
        _analysisSessionId = item.AnalysisSessionId;
        await MeasureCanvas.LoadImageAsync(item.PreviewAssetPath);

        KeyframeMetaText.Text =
            $"{Path.GetFileName(item.SourceMediaPath ?? item.PreviewAssetPath ?? "?")} · t={item.MediaTimestamp?.ToString(@"hh\:mm\:ss") ?? "?"} · frame {item.FrameIndex?.ToString() ?? "?"}";

        if (item.MediaTimestamp is { } ts)
        {
            var baseUtc = _session.LastPipelineResult?.Session.CreatedAtUtc ?? DateTimeOffset.UtcNow;
            var obs = baseUtc.ToUniversalTime().Subtract(TimeSpan.FromTicks(baseUtc.TimeOfDay.Ticks)).Add(ts);
            DatePicker.Date = obs.Date;
            TimePicker.Time = obs.TimeOfDay;
        }
    }

    private void MeasureCanvas_MeasurementChanged(object sender, EventArgs e)
    {
        UpdateInspectorFromCanvas();
        ApplyCanvasRatio_Click(sender, new RoutedEventArgs());
    }

    private void UpdateInspectorFromCanvas()
    {
        RowHeight.Value = MeasureCanvas.ObjectHeightPixels > 0
            ? $"{MeasureCanvas.ObjectHeightPixels:F1} px"
            : "—";
        RowShadow.Value = MeasureCanvas.ShadowLengthPixels > 0
            ? $"{MeasureCanvas.ShadowLengthPixels:F1} px"
            : "—";
        if (MeasureCanvas.HasCompleteMeasurement && MeasureCanvas.ShadowLengthPixels > 0)
        {
            var ratio = MeasureCanvas.ObjectHeightPixels / MeasureCanvas.ShadowLengthPixels;
            RowRatio.Value = ratio.ToString("F3");
        }
        else
            RowRatio.Value = "—";

        RowAzimuth.Value = MeasureCanvas.ShadowAzimuthDegrees is { } az
            ? $"{az:F0}°"
            : "—";
    }

    private void ApplyCanvasRatio_Click(object sender, RoutedEventArgs e)
    {
        if (!MeasureCanvas.HasCompleteMeasurement)
            return;

        ObjectHeightBox.Value = MeasureCanvas.ObjectHeightPixels;
        ShadowLengthBox.Value = MeasureCanvas.ShadowLengthPixels;
        if (MeasureCanvas.ShadowAzimuthDegrees is { } az)
        {
            AzimuthBox.Value = az;
            UseAzimuthCheck.IsChecked = false;
        }
    }

    private void RefreshKeyframes_Click(object sender, RoutedEventArgs e) => RefreshKeyframeList();

    private ShadowMeasurement BuildMeasurement()
    {
        var date = DatePicker.Date?.DateTime.Date ?? DateTime.UtcNow.Date;
        var time = TimePicker.Time;
        var utc = new DateTimeOffset(date.Year, date.Month, date.Day, time.Hours, time.Minutes, time.Seconds, TimeSpan.Zero);
        double? azimuth = UseAzimuthCheck.IsChecked == true ? AzimuthBox.Value : null;

        return new ShadowMeasurement
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = _analysisSessionId,
            EvidenceId = _selectedKeyframe?.Id,
            SourceMediaPath = _selectedKeyframe?.SourceMediaPath ?? _selectedKeyframe?.PreviewAssetPath ?? "(manual)",
            MediaTimestamp = _selectedKeyframe?.MediaTimestamp,
            FrameIndex = _selectedKeyframe?.FrameIndex,
            ObservationUtc = utc,
            ObjectHeight = ObjectHeightBox.Value,
            ShadowLength = ShadowLengthBox.Value,
            ShadowAzimuthDegrees = azimuth,
            MeasurementConfidence = _selectedKeyframe is null ? Confidence.Medium : Confidence.High,
            Notes = _selectedKeyframe is null
                ? "Manual measurement"
                : $"Interactive keyframe measurement on {Path.GetFileName(_selectedKeyframe.PreviewAssetPath ?? "?")}"
        };
    }

    private void ComputeElevation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var m = BuildMeasurement();
            ResultBlock.Text =
                $"Estimated solar elevation: {m.EstimatedSolarElevationDegrees:F2}°\n" +
                $"Observation UTC: {m.ObservationUtc:u}\n" +
                $"Ratio h/L = {m.ObjectHeight / m.ShadowLength:F4}";
        }
        catch (Exception ex)
        {
            ResultBlock.Text = $"Error: {ex.Message}";
        }
    }

    private async void SaveEvidence_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var m = BuildMeasurement();
            _board.Add(new EvidenceItem
            {
                Id = m.Id,
                AnalysisSessionId = m.AnalysisSessionId,
                Type = EvidenceType.ShadowMeasurement,
                Summary = $"Shadow h/L → elev ≈ {m.EstimatedSolarElevationDegrees:F1}°",
                RawContent = $"{m.ObjectHeight:F3},{m.ShadowLength:F3}",
                SourceMediaPath = m.SourceMediaPath,
                PreviewAssetPath = _selectedKeyframe?.PreviewAssetPath,
                MediaTimestamp = m.MediaTimestamp,
                FrameIndex = m.FrameIndex,
                Confidence = m.MeasurementConfidence,
                Notes = m.Notes,
                Attributes = new Dictionary<string, string>
                {
                    ["objectHeight"] = m.ObjectHeight.ToString("F4"),
                    ["shadowLength"] = m.ShadowLength.ToString("F4"),
                    ["elevationDeg"] = m.EstimatedSolarElevationDegrees.ToString("F2"),
                    ["azimuthDeg"] = m.ShadowAzimuthDegrees?.ToString("F1") ?? ""
                }
            });

            _session.CurrentSessionId ??= m.AnalysisSessionId;
            await _session.RefuseAsync();
            AnalysisPage.LastResult = _session.LastPipelineResult;
            ResultBlock.Text = "Shadow measurement saved to Evidence Board and hypotheses refreshed.";
        }
        catch (Exception ex)
        {
            ResultBlock.Text = $"Error: {ex.Message}";
        }
    }

    private void AddTrajectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var m = BuildMeasurement();
            if (_session.Trajectory.Count > 0)
            {
                m = new ShadowMeasurement
                {
                    Id = m.Id,
                    AnalysisSessionId = _session.Trajectory[0].AnalysisSessionId,
                    EvidenceId = m.EvidenceId,
                    SourceMediaPath = m.SourceMediaPath,
                    MediaTimestamp = m.MediaTimestamp,
                    FrameIndex = m.FrameIndex,
                    ObservationUtc = m.ObservationUtc,
                    ObjectHeight = m.ObjectHeight,
                    ShadowLength = m.ShadowLength,
                    ShadowAzimuthDegrees = m.ShadowAzimuthDegrees,
                    MeasurementConfidence = m.MeasurementConfidence,
                    Notes = m.Notes
                };
            }

            _session.Trajectory.Add(m);
            _analysisSessionId = m.AnalysisSessionId;
            TrajectoryList.ItemsSource = null;
            TrajectoryList.ItemsSource = _session.Trajectory
                .Select(t => $"{t.ObservationUtc:u} · elev≈{t.EstimatedSolarElevationDegrees:F1}° · {Path.GetFileName(t.SourceMediaPath)}")
                .ToList();
        }
        catch (Exception ex)
        {
            ResultBlock.Text = $"Error: {ex.Message}";
        }
    }

    private async void BuildLocus_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var measurements = _session.Trajectory.Count > 0
                ? _session.Trajectory.ToList()
                : [BuildMeasurement()];

            SolarLocusResult locus;
            if (measurements.Count >= 2)
            {
                locus = _locusEngine.ComputeTrajectoryLocus(
                    measurements[0].AnalysisSessionId,
                    measurements,
                    LocusSearchBounds.ContiguousUnitedStates,
                    gridStepDegrees: 2.0,
                    elevationToleranceDegrees: ToleranceBox.Value);
            }
            else
            {
                locus = _locusEngine.ComputeLocus(
                    measurements[0].AnalysisSessionId,
                    measurements,
                    LocusSearchBounds.ContiguousUnitedStates,
                    gridStepDegrees: 2.0,
                    elevationToleranceDegrees: ToleranceBox.Value,
                    azimuthToleranceDegrees: measurements[0].ShadowAzimuthDegrees.HasValue ? 10.0 : null);
            }

            _session.LastSolarLocus = locus;
            _session.CurrentSessionId = measurements[0].AnalysisSessionId;

            _board.Add(new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = measurements[0].AnalysisSessionId,
                Type = EvidenceType.SolarLocus,
                Summary = $"Solar locus peak {locus.PeakProbabilityCell} (elev {locus.TargetElevationDegrees:F1}°)",
                Confidence = locus.OverallConfidence,
                Notes = string.Join("; ", locus.Limitations.Take(2))
            });

            await _session.RefuseAsync();

            AnalysisPage.LastResult = _session.LastPipelineResult;

            var peak = locus.PeakProbabilityCell?.ToString() ?? "(no peak in bounds)";
            var measurementCount = measurements.Count;
            ResultBlock.Text =
                $"Mode: {(measurementCount >= 2 ? "trajectory" : "single")}\n" +
                $"Target elevation: {locus.TargetElevationDegrees:F2}°\n" +
                $"Cells retained: {locus.Cells.Count}\n" +
                $"Peak cell: {peak}\n" +
                $"Locus confidence: {locus.OverallConfidence}\n" +
                $"Hypotheses refreshed: {_session.LastPipelineResult?.Hypotheses.Count ?? 0}\n\n" +
                $"Assumptions:\n- {string.Join("\n- ", locus.Assumptions)}\n\n" +
                $"Limitations:\n- {string.Join("\n- ", locus.Limitations)}\n\n" +
                "Open Map / Hypotheses to see the fused solar peak.";

            ApplyResultConfidence(locus.OverallConfidence, measurementCount);
        }
        catch (Exception ex)
        {
            ResultBlock.Text = $"Error: {ex.Message}";
            ResultBlock.ClearValue(TextBlock.ForegroundProperty);
            ResultBlock.ClearValue(TextBlock.FontWeightProperty);
            ResultBlock.Opacity = 1;
        }
    }

    private void ApplyResultConfidence(Confidence confidence, int corroboratingMeasurements)
    {
        var visual = ConfidenceVisual.ForValue(confidence, corroboratingMeasurements);
        ResultBlock.Foreground = visual.TitleBrush;
        ResultBlock.FontWeight = visual.TitleWeight;
        ResultBlock.Opacity = visual.RowOpacity;
    }

    private sealed record KeyframeRow(EvidenceItem Item)
    {
        public string Label =>
            $"{Item.MediaTimestamp?.ToString(@"mm\:ss") ?? "??:??"} · {Path.GetFileName(Item.PreviewAssetPath ?? Item.SourceMediaPath ?? "frame")}";
    }
}
