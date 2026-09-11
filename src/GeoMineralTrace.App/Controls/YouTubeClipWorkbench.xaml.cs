using System.Text.Json;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Pipeline.Ingest;
using GeoMineralTrace_App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace GeoMineralTrace_App.Controls;

public sealed partial class YouTubeClipWorkbench : UserControl
{
    private string? _loadedUrl;
    private string? _videoId;
    private double _durationSeconds;
    private double _currentSeconds;
    private double _inSeconds;
    private double _outSeconds;
    private bool _playerReady;
    private bool _isLive;
    private string? _dragging;
    private bool _loopPreviewActive;
    private bool _analyzeAfterRecording;
    private string _recordingState = "idle"; // idle, recording, paused, assembling
    private readonly RecordingChunkAssembler _recordingAssembler = new();

    public YouTubeClipWorkbench()
    {
        InitializeComponent();
        Loaded += YouTubeClipWorkbench_Loaded;
        ClipTimelineHost.SizeChanged += (_, _) => UpdateClipTimelineVisuals();
    }

    public string? LoadedUrl => _loadedUrl;
    public string? VideoId => _videoId;
    public double DurationSeconds => _durationSeconds;
    public bool IsPlayerReady => _playerReady;

    public event EventHandler<string>? StatusMessage;
    public event EventHandler<string>? RecordingCompleted;
    public event EventHandler? DownloadSegmentRequested;

    public MediaClipRange GetClipRange() => new(_inSeconds, _outSeconds);

    public void RestoreState(string? url, double inSeconds, double outSeconds)
    {
        _inSeconds = inSeconds;
        _outSeconds = outSeconds;
        if (!string.IsNullOrWhiteSpace(url))
            _ = LoadFromUrlAsync(url);
        else
            SyncTimeBoxes();
    }

    public (string? Url, double In, double Out) GetPersistedState() => (_loadedUrl, _inSeconds, _outSeconds);

    private async void YouTubeClipWorkbench_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureWebViewAsync();
        }
        catch (Exception ex)
        {
            ShowError("WebView2 failed to initialize: " + ex.Message);
        }
    }

    private async Task EnsureWebViewAsync()
    {
        if (PlayerWebView.CoreWebView2 is not null)
            return;

        await PlayerWebView.EnsureCoreWebView2Async();
        var core = PlayerWebView.CoreWebView2!;
        core.Settings.IsStatusBarEnabled = false;
        core.WebMessageReceived += PlayerWebView_WebMessageReceived;
        core.PermissionRequested += PlayerWebView_PermissionRequested;
    }

    private void PlayerWebView_PermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args)
    {
        // Allow display/media capture prompts used by getDisplayMedia + MediaRecorder.
        args.State = CoreWebView2PermissionState.Allow;
    }

    public async Task LoadFromUrlAsync(string url)
    {
        url = (YouTubeMediaFetcher.NormalizeYouTubeWatchUrl(url) ?? url).Trim();
        _loadedUrl = url;
        _playerReady = false;
        _durationSeconds = 0;
        _currentSeconds = 0;
        _loopPreviewActive = false;
        SetPlayerControlsEnabled(false);
        SetRecordingControlsEnabled(false);
        ShowLoading("Validating URL…");
        HideError();
        SetRecordingUi("idle", "Not recording");

        if (YouTubeMediaFetcher.GetPlatform(url) != RemoteMediaPlatform.YouTube)
        {
            ShowError("Only YouTube watch, shorts, and youtu.be links are supported for clip capture.");
            return;
        }

        if (YouTubeMediaFetcher.IsYouTubeLiveUrl(url))
        {
            ShowError("Live streams can't be clipped. Wait for the VOD replay URL after the broadcast ends.");
            return;
        }

        var videoId = YouTubeMediaFetcher.TryGetVideoId(url);
        if (videoId is null)
        {
            ShowError("Invalid YouTube URL — could not extract a video ID.");
            return;
        }

        _videoId = videoId;
        VideoTitleText.Text = $"YouTube · {videoId}";

        try
        {
            await EnsureWebViewAsync();
            ShowLoading("Loading YouTube player…");
            PlayerWebView.Visibility = Visibility.Visible;
            PlayerWebView.NavigateToString(YouTubeIframePlayerHtml.BuildPlayerPage(videoId));
        }
        catch (Exception ex)
        {
            ShowError("Failed to load player: " + ex.Message);
        }
    }

    private void PlayerWebView_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        // Prefer the raw string if the page posted a string; otherwise use JSON.
        // Older player pages JSON.stringify'd before postMessage, which double-encodes
        // WebMessageAsJson into a JSON string value — unwrap that case too.
        string json;
        try
        {
            try
            {
                json = args.TryGetWebMessageAsString();
            }
            catch (ArgumentException)
            {
                json = args.WebMessageAsJson;
            }

            if (string.IsNullOrWhiteSpace(json))
                json = args.WebMessageAsJson;
        }
        catch
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => HandlePlayerMessage(json));
    }

    private async void HandlePlayerMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // Double-encoded payload from older player pages that JSON.stringify'd first.
            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString();
                if (string.IsNullOrWhiteSpace(inner))
                    return;
                using var innerDoc = JsonDocument.Parse(inner);
                var innerRoot = innerDoc.RootElement;
                if (!innerRoot.TryGetProperty("type", out var innerType))
                    return;
                await HandlePlayerMessageCoreAsync(innerType.GetString(), innerRoot).ConfigureAwait(true);
                return;
            }

            if (!root.TryGetProperty("type", out var typeEl))
                return;

            await HandlePlayerMessageCoreAsync(typeEl.GetString(), root).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowError("Player message failed: " + ex.Message);
        }
    }

    private async Task HandlePlayerMessageCoreAsync(string? type, JsonElement root)
    {
        switch (type)
        {
            case "ready":
                OnPlayerReady(root);
                break;
            case "tick":
            case "time":
                if (root.TryGetProperty("current", out var cur))
                    _currentSeconds = cur.GetDouble();
                if (root.TryGetProperty("duration", out var dur) && dur.GetDouble() > 0)
                    _durationSeconds = dur.GetDouble();
                UpdateClipTimelineVisuals();
                break;
            case "error":
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "Video failed to load";
                ShowError(msg ?? "Video failed to load");
                break;
            case "captureStarted":
                _recordingState = "recording";
                SetRecordingUi("recording", "Recording…");
                SetRecordingControlsEnabled(true, recording: true);
                StatusMessage?.Invoke(this, "Recording — play the video and capture the shadow segment.");
                break;
            case "capturePaused":
                _recordingState = "paused";
                SetRecordingUi("paused", "Paused");
                PauseRecordingButton.IsEnabled = false;
                StopRecordingButton.IsEnabled = true;
                if (StartRecordingButton.IsEnabled)
                    StartRecordingButton.IsEnabled = false;
                break;
            case "captureResumed":
                _recordingState = "recording";
                SetRecordingUi("recording", "Recording…");
                PauseRecordingButton.IsEnabled = true;
                break;
            case "captureDenied":
                _recordingState = "idle";
                _analyzeAfterRecording = false;
                SetRecordingUi("idle", "Capture cancelled");
                SetRecordingControlsEnabled(_playerReady);
                ShowError(root.TryGetProperty("message", out var denied) ? denied.GetString() ?? "Capture denied" : "Capture denied");
                break;
            case "captureError":
                _recordingState = "idle";
                _analyzeAfterRecording = false;
                SetRecordingUi("idle", "Capture failed");
                SetRecordingControlsEnabled(_playerReady);
                ShowError(root.TryGetProperty("message", out var err) ? err.GetString() ?? "Capture error" : "Capture error");
                break;
            case "recordingProgress":
                if (root.TryGetProperty("elapsedSeconds", out var elapsed))
                    SetRecordingUi(_recordingState, $"Recording · {MediaTimeFormat.Format(elapsed.GetDouble())}");
                break;
            case "recordingStart":
                _recordingState = "assembling";
                _recordingAssembler.Begin(
                    root.TryGetProperty("mimeType", out var mime) ? mime.GetString() ?? "video/webm" : "video/webm",
                    root.TryGetProperty("totalBytes", out var tb) ? tb.GetInt32() : 0,
                    root.TryGetProperty("totalChunks", out var tc) ? tc.GetInt32() : 1);
                SetRecordingUi("assembling", "Saving recording…");
                break;
            case "recordingChunk":
                if (root.TryGetProperty("index", out var idx) && root.TryGetProperty("data", out var data))
                    _recordingAssembler.AddChunk(idx.GetInt32(), data.GetString() ?? "");
                break;
            case "recordingComplete":
                await FinalizeRecordingAsync();
                break;
            case "captureStopped":
                if (_recordingState is not "assembling")
                {
                    _recordingState = "idle";
                    SetRecordingUi("idle", "Not recording");
                    SetRecordingControlsEnabled(_playerReady);
                }
                break;
        }
    }

    private async Task FinalizeRecordingAsync()
    {
        try
        {
            var bytes = _recordingAssembler.ToArray();
            if (bytes.Length < 4096)
                throw new InvalidOperationException("Recording too short — select this app window, play the video during capture, and try again.");

            var clip = GetClipRange();
            try
            {
                clip.Validate(_durationSeconds);
            }
            catch
            {
                // Recorded manually without strict range — still allow if >= min bytes
            }

            var path = await CaptureRecordingStore.SaveRecordingAsync(
                bytes,
                _recordingAssembler.MimeType,
                _videoId ?? "capture");

            _recordingState = "idle";
            SetRecordingUi("idle", "Recording saved");
            SetRecordingControlsEnabled(_playerReady);
            PlayerFrame.BorderBrush = (Brush)Application.Current.Resources["GmtBrushBorderDefault"];
            HideError();
            StatusMessage?.Invoke(this, $"Capture saved: {Path.GetFileName(path)}");
            RecordingCompleted?.Invoke(this, path);

            if (_analyzeAfterRecording)
            {
                _analyzeAfterRecording = false;
                // AnalysisPage listens to RecordingCompleted and can auto-run
            }
        }
        catch (Exception ex)
        {
            _recordingState = "idle";
            _analyzeAfterRecording = false;
            SetRecordingUi("idle", "Save failed");
            SetRecordingControlsEnabled(_playerReady);
            ShowError(ex.Message);
        }
        finally
        {
            _recordingAssembler.Reset();
        }
    }

    private void OnPlayerReady(JsonElement root)
    {
        _playerReady = true;
        HideLoading();

        if (root.TryGetProperty("duration", out var dur))
            _durationSeconds = dur.GetDouble();
        if (root.TryGetProperty("isLive", out var live))
            _isLive = live.GetBoolean();

        if (_isLive)
        {
            ShowError("This appears to be a live stream. Clip capture requires a finished VOD.");
            return;
        }

        if (_durationSeconds <= 0)
        {
            ShowError("Could not read video duration.");
            return;
        }

        if (_outSeconds <= _inSeconds || _outSeconds > _durationSeconds)
        {
            _inSeconds = 0;
            _outSeconds = Math.Min(_durationSeconds, Math.Max(MediaClipRange.MinimumDurationSeconds, 30));
        }

        _outSeconds = Math.Min(_outSeconds, _durationSeconds);
        _inSeconds = Math.Clamp(_inSeconds, 0, Math.Max(0, _outSeconds - MediaClipRange.MinimumDurationSeconds));

        SyncTimeBoxes();
        SetPlayerControlsEnabled(true);
        SetRecordingControlsEnabled(true);
        UpdateClipTimelineVisuals();
        StatusMessage?.Invoke(this,
            "Player ready. Set In/Out, click Start recording or Record & analyze — " +
            "when the screen-share picker appears, choose this Unbound Rockhound window.");
    }

    private async void StartRecording_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();
            _analyzeAfterRecording = false;
            StatusMessage?.Invoke(this, "Opening screen-share picker — select this app window, then play the video.");
            await SendPlayerCommand(new { command = "startCapture" });
        }
        catch (Exception ex)
        {
            ShowError("Could not start capture: " + ex.Message);
        }
    }

    private async void PauseRecording_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_recordingState == "recording")
                await SendPlayerCommand(new { command = "pauseCapture" });
            else if (_recordingState == "paused")
                await SendPlayerCommand(new { command = "resumeCapture" });
        }
        catch (Exception ex)
        {
            ShowError("Capture control failed: " + ex.Message);
        }
    }

    private async void StopRecording_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SendPlayerCommand(new { command = "stopCapture" });
        }
        catch (Exception ex)
        {
            ShowError("Could not stop capture: " + ex.Message);
        }
    }

    private async void RecordAndAnalyze_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!TryValidateSelection(out var error))
            {
                ShowError(error);
                return;
            }

            HideError();
            _analyzeAfterRecording = true;
            StatusMessage?.Invoke(this,
                "Opening screen-share picker — select this app window. Playback will run your In/Out range.");
            await SendPlayerCommand(new
            {
                command = "startCapture",
                autoPlayFrom = _inSeconds,
                autoStopAt = _outSeconds
            });
        }
        catch (Exception ex)
        {
            _analyzeAfterRecording = false;
            ShowError("Could not start record & analyze: " + ex.Message);
        }
    }

    private void DownloadSegment_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateSelection(out var error))
        {
            ShowError(error);
            return;
        }

        DownloadSegmentRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetIn_Click(object sender, RoutedEventArgs e)
    {
        _inSeconds = Math.Clamp(_currentSeconds, 0, Math.Max(0, _outSeconds - MediaClipRange.MinimumDurationSeconds));
        SyncTimeBoxes();
        UpdateClipTimelineVisuals();
    }

    private void SetOut_Click(object sender, RoutedEventArgs e)
    {
        _outSeconds = Math.Clamp(_currentSeconds, _inSeconds + MediaClipRange.MinimumDurationSeconds, _durationSeconds);
        SyncTimeBoxes();
        UpdateClipTimelineVisuals();
    }

    private async void PreviewSelection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateSelection(out var error))
        {
            ShowError(error);
            return;
        }

        HideError();
        _loopPreviewActive = true;
        await SendPlayerCommand(new { command = "loopPreview", start = _inSeconds, end = _outSeconds });
        StatusMessage?.Invoke(this, "Previewing selected range (loop)…");
    }

    private bool TryValidateSelection(out string error)
    {
        error = "";
        if (!_playerReady)
        {
            error = "Load a video first.";
            return false;
        }

        try
        {
            new MediaClipRange(_inSeconds, _outSeconds).Validate(_durationSeconds);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void TimeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_durationSeconds <= 0) return;

        if (sender is TextBox box && box == InTimeBox && MediaTimeFormat.TryParse(InTimeBox.Text, out var newIn))
            _inSeconds = Math.Clamp(newIn, 0, Math.Max(0, _outSeconds - MediaClipRange.MinimumDurationSeconds));
        if (sender is TextBox box2 && box2 == OutTimeBox && MediaTimeFormat.TryParse(OutTimeBox.Text, out var newOut))
            _outSeconds = Math.Clamp(newOut, _inSeconds + MediaClipRange.MinimumDurationSeconds, _durationSeconds);

        SyncTimeBoxes();
        UpdateClipTimelineVisuals();
    }

    private void SyncTimeBoxes()
    {
        InTimeBox.Text = MediaTimeFormat.Format(_inSeconds);
        OutTimeBox.Text = MediaTimeFormat.Format(_outSeconds);
        var sel = Math.Max(0, _outSeconds - _inSeconds);
        SelectionDurationText.Text = sel > 0 ? $"Selected: {sel:F1}s" : "Selected: —";
        if (_playerReady)
            UpdateSelectionDependentActions(
                ready: true,
                recording: _recordingState is "recording" or "paused");
    }

    private void UpdateClipTimelineVisuals()
    {
        var width = ClipTimelineHost.ActualWidth;
        if (width <= 0 || _durationSeconds <= 0) return;

        var inX = _inSeconds / _durationSeconds * width;
        var outX = _outSeconds / _durationSeconds * width;
        var playX = _currentSeconds / _durationSeconds * width;

        ClipSelectionFill.Margin = new Thickness(inX, 0, 0, 0);
        ClipSelectionFill.Width = Math.Max(0, outX - inX);
        ClipPlayhead.Margin = new Thickness(Math.Max(0, playX - 1), 0, 0, 0);
        InHandle.Margin = new Thickness(Math.Max(0, inX - InHandle.Width / 2), 0, 0, 0);
        OutHandle.Margin = new Thickness(Math.Max(0, outX - OutHandle.Width / 2), 0, 0, 0);
    }

    private void ClipTimelineHost_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_durationSeconds <= 0 || _recordingState is "recording" or "paused") return;
        var pos = e.GetCurrentPoint(ClipTimelineHost).Position;
        var width = ClipTimelineHost.ActualWidth;
        if (width <= 0) return;

        var seconds = pos.X / width * _durationSeconds;
        var inDist = Math.Abs(seconds - _inSeconds);
        var outDist = Math.Abs(seconds - _outSeconds);
        const double handleHit = 12;

        if (inDist <= handleHit)
            _dragging = "in";
        else if (outDist <= handleHit)
            _dragging = "out";
        else
            _dragging = "seek";

        ClipTimelineHost.CapturePointer(e.Pointer);
        ApplyTimelinePointer(seconds);
        e.Handled = true;
    }

    private void ClipTimelineHost_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null || _durationSeconds <= 0) return;
        var pos = e.GetCurrentPoint(ClipTimelineHost).Position;
        var width = ClipTimelineHost.ActualWidth;
        if (width <= 0) return;
        var seconds = Math.Clamp(pos.X / width * _durationSeconds, 0, _durationSeconds);
        ApplyTimelinePointer(seconds);
    }

    private async void ClipTimelineHost_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null) return;
        _dragging = null;
        ClipTimelineHost.ReleasePointerCapture(e.Pointer);
        if (_loopPreviewActive)
            await SendPlayerCommand(new { command = "loopPreview", start = _inSeconds, end = _outSeconds });
    }

    private async void ApplyTimelinePointer(double seconds)
    {
        switch (_dragging)
        {
            case "in":
                _inSeconds = Math.Clamp(seconds, 0, Math.Max(0, _outSeconds - MediaClipRange.MinimumDurationSeconds));
                break;
            case "out":
                _outSeconds = Math.Clamp(seconds, _inSeconds + MediaClipRange.MinimumDurationSeconds, _durationSeconds);
                break;
            case "seek":
                _currentSeconds = seconds;
                await SendPlayerCommand(new { command = "seek", seconds });
                if (_loopPreviewActive)
                    await SendPlayerCommand(new { command = "stopLoop" });
                _loopPreviewActive = false;
                break;
        }

        SyncTimeBoxes();
        UpdateClipTimelineVisuals();
    }

    private async Task SendPlayerCommand(object payload)
    {
        if (PlayerWebView.CoreWebView2 is null) return;
        PlayerWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
        await Task.CompletedTask;
    }

    private void SetPlayerControlsEnabled(bool ready)
    {
        var waiting = "Waiting for the YouTube player to finish loading";
        ActionAvailability.Set(SetInButton, ready, "Set In to the current playhead time", waiting);
        ActionAvailability.Set(SetOutButton, ready, "Set Out to the current playhead time", waiting);
        ActionAvailability.Set(
            PreviewSelectionButton,
            ready,
            "Loop-preview the current In/Out selection in the YouTube player",
            waiting);

        UpdateSelectionDependentActions(ready, recording: _recordingState is "recording" or "paused");
    }

    private void SetRecordingControlsEnabled(bool ready, bool recording = false)
    {
        var playerWaiting = "Waiting for the YouTube player to finish loading";
        var recordingBusy = "Available when a capture is in progress";
        var idleOnly = "Stop the current capture first";

        ActionAvailability.Set(
            StartRecordingButton,
            ready && !recording && _recordingState != "assembling",
            "Start screen capture — select this app window when prompted",
            _recordingState == "assembling"
                ? "Saving the previous recording…"
                : recording
                    ? idleOnly
                    : playerWaiting);

        ActionAvailability.Set(
            PauseRecordingButton,
            recording,
            _recordingState == "paused" ? "Resume screen capture" : "Pause screen capture",
            recordingBusy);
        PauseRecordingButton.Content = _recordingState == "paused" ? "Resume" : "Pause";

        ActionAvailability.Set(
            StopRecordingButton,
            recording || _recordingState == "paused",
            "Stop capture and save the recording",
            recordingBusy);

        UpdateSelectionDependentActions(ready, recording);
    }

    private void UpdateSelectionDependentActions(bool ready, bool recording)
    {
        var waiting = "Waiting for the YouTube player to finish loading";
        string selectionError = "Set In and Out on the timeline first";
        var selectionOk = ready && TryValidateSelection(out selectionError!);

        ActionAvailability.Set(
            DownloadSegmentButton,
            selectionOk && !recording && _recordingState != "assembling",
            "Download the In/Out range with yt-dlp (fallback if screen capture is unavailable)",
            !ready
                ? waiting
                : _recordingState == "assembling"
                    ? "Saving the previous recording…"
                    : recording
                        ? "Stop the current capture first"
                        : selectionError);

        ActionAvailability.Set(
            RecordAndAnalyzeButton,
            selectionOk && !recording && _recordingState != "assembling",
            "Record the In/Out selection (screen capture) then analyze automatically",
            !ready
                ? waiting
                : _recordingState == "assembling"
                    ? "Saving the previous recording…"
                    : recording
                        ? "Stop the current capture first"
                        : selectionError);
    }

    private void SetRecordingUi(string state, string label)
    {
        RecordingStatusText.Text = label;
        var active = state is "recording" or "paused" or "assembling";
        RecIndicator.Opacity = active ? 1.0 : 0.35;
        PlayerFrame.BorderBrush = state == "recording"
            ? new SolidColorBrush(Microsoft.UI.Colors.IndianRed)
            : (Brush)Application.Current.Resources["GmtBrushBorderDefault"];
        PlayerFrame.BorderThickness = state == "recording" ? new Thickness(2) : new Thickness(1);
    }

    private void ShowLoading(string message)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingText.Text = message;
        PlayerWebView.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
    }

    private void HideLoading()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        PlayerWebView.Visibility = Visibility.Visible;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
        if (_recordingState == "idle")
            PlayerWebView.Visibility = Visibility.Collapsed;
        SetPlayerControlsEnabled(false);
        SetRecordingControlsEnabled(false);
        StatusMessage?.Invoke(this, message);
    }

    private void HideError() => ErrorPanel.Visibility = Visibility.Collapsed;

    public void Reset()
    {
        _loadedUrl = null;
        _videoId = null;
        _playerReady = false;
        _durationSeconds = 0;
        _inSeconds = 0;
        _outSeconds = 0;
        _recordingState = "idle";
        _recordingAssembler.Reset();
        VideoTitleText.Text = "Paste a YouTube URL to load";
        SetPlayerControlsEnabled(false);
        SetRecordingControlsEnabled(false);
        SetRecordingUi("idle", "Not recording");
        HideError();
        LoadingPanel.Visibility = Visibility.Collapsed;
        PlayerWebView.Visibility = Visibility.Collapsed;
    }

    public bool ShouldAnalyzeAfterRecording => _analyzeAfterRecording;
}
