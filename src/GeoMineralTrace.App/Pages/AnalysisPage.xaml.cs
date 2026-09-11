using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Core.Progress;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace.Pipeline;
using GeoMineralTrace.Pipeline.Audio;
using GeoMineralTrace.Pipeline.Diagnostics;
using GeoMineralTrace.Pipeline.Ingest;
using GeoMineralTrace.Pipeline.Keyframes;
using GeoMineralTrace.Pipeline.Persistence;
using GeoMineralTrace.Reporting;
using GeoMineralTrace_App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Path = System.IO.Path;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace GeoMineralTrace_App.Pages;

public sealed partial class AnalysisPage : Page
{
    private readonly List<string> _files = [];
    private CancellationTokenSource? _cts;
    private double _timelineFraction;
    private bool _scrubDragging;
    private DispatcherTimer? _playheadTimer;
    private DispatcherTimer? _youtubeUrlDebounce;
    private string? _persistedYouTubeUrl;
    private double _persistedInSeconds;
    private double _persistedOutSeconds;
    private bool _analysisBusy;
    public static AnalysisPipelineResult? LastResult { get; set; }

    public AnalysisPage()
    {
        InitializeComponent();
        YouTubeClipPanel.StatusMessage += (_, msg) => DispatcherQueue.TryEnqueue(() => AppendLog(msg));
        YouTubeClipPanel.RecordingCompleted += YouTubeClipPanel_RecordingCompleted;
        YouTubeClipPanel.DownloadSegmentRequested += (_, _) => _ = DownloadYouTubeSegmentAsync(analyzeAfter: true);

        QueueEmpty.ActionLabel = "Add local files";
        QueueEmpty.ActionClick += AddFiles_Click;

        Loaded += (_, _) =>
        {
            UpdateToolStatus();
            UpdateRemoteAuthUi();
            UpdateTimeline(_timelineFraction);
            DrawWaveform();
            RefreshKeyframeStrip(LastResult ?? App.Services.GetRequiredService<AnalysisSessionContext>().LastPipelineResult);
            StartPlayheadTimer();
            UpdateTransportAvailability();
            UpdateRunCancelAvailability(busy: false);

            if (!string.IsNullOrWhiteSpace(_persistedYouTubeUrl))
                YouTubeClipPanel.RestoreState(_persistedYouTubeUrl, _persistedInSeconds, _persistedOutSeconds);
        };
        Unloaded += (_, _) =>
        {
            if (_playheadTimer is not null)
            {
                _playheadTimer.Stop();
                _playheadTimer = null;
            }

            _youtubeUrlDebounce?.Stop();
            PreviewPlayer.MediaPlayer?.Pause();
        };
    }

    private void StartPlayheadTimer()
    {
        if (_playheadTimer is not null)
        {
            _playheadTimer.Stop();
            _playheadTimer = null;
        }

        _playheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _playheadTimer.Tick += (_, _) =>
        {
            var player = PreviewPlayer.MediaPlayer;
            if (player is null || PreviewPlayer.Visibility != Visibility.Visible) return;
            var dur = player.PlaybackSession.NaturalDuration.TotalSeconds;
            if (dur <= 0 || double.IsNaN(dur)) return;
            var pos = player.PlaybackSession.Position;
            UpdateTimeline(pos.TotalSeconds / dur);
            UpdateTimecodeReadout(pos, player.PlaybackSession.NaturalDuration);
            SyncTransportPlayIcon(player.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing);
            UpdateTransportAvailability();
        };
        _playheadTimer.Start();
    }

    private void UpdateToolStatus()
    {
        var ffmpeg = FfmpegKeyframeExtractor.ResolveFfmpeg();
        var ytdlp = YouTubeMediaFetcher.ResolveYtDlp();
        var whisper = WhisperCliTranscriptionEngine.ResolveWhisper();
        ToolStatusText.Text =
            $"ffmpeg: {(ffmpeg is null ? "missing" : "ready")} | " +
            $"yt-dlp: {(ytdlp is null ? "missing" : "ready")} | " +
            $"whisper: {(whisper is null ? "optional" : "ready")} | " +
            $"yt: {(YouTubeSessionStore.HasSavedSession ? "signed in" : "guest")} | " +
            $"ig: {(InstagramSessionStore.HasSavedSession ? "signed in" : "guest")} | " +
            $"online: {(AppPreferences.OnlineEnrichmentAllowed ? "on" : "off")}";
    }

    private void UpdateRemoteAuthUi()
    {
        YouTubeSignInButton.Content = YouTubeSessionStore.HasSavedSession
            ? "YouTube signed in - manage in Settings"
            : "Sign in to YouTube...";

        InstagramSignInButton.Content = InstagramSessionStore.HasSavedSession
            ? "Instagram signed in - manage in Settings"
            : "Sign in to Instagram...";

        YouTubeAuthCaption.Text = YouTubeSessionStore.HasSavedSession || InstagramSessionStore.HasSavedSession
            ? "YouTube clips: screen capture in the player. Instagram / full downloads: yt-dlp. Manage accounts in Settings."
            : "YouTube: paste a URL to open clip capture. Instagram: sign in for most posts. Cookies stay local.";
    }

    private async void YouTubeSignIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new GeoMineralTrace_App.Dialogs.YouTubeSignInDialog { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
            UpdateRemoteAuthUi();
            UpdateToolStatus();
        }
        catch (Exception ex)
        {
            AppendLog($"YouTube sign-in failed: {ex.Message}");
            StatusText.Text = "Sign-in failed";
        }
    }

    private async void InstagramSignIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new GeoMineralTrace_App.Dialogs.InstagramSignInDialog { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
            UpdateRemoteAuthUi();
            UpdateToolStatus();
        }
        catch (Exception ex)
        {
            AppendLog($"Instagram sign-in failed: {ex.Message}");
            StatusText.Text = "Sign-in failed";
        }
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
            InitializeWithWindow.Initialize(picker, hwnd);
            foreach (var ext in new[] { ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".webm", ".jpg", ".jpeg", ".png", ".bmp", "*" })
                picker.FileTypeFilter.Add(ext);
            var files = await picker.PickMultipleFilesAsync();
            if (files is null) return;
            foreach (var f in files)
                _files.Add(f.Path);
            RefreshFileList();
            if (_files.Count > 0)
                LoadMediaPreview(_files[^1]);
        }
        catch (Exception ex)
        {
            AppendLog($"Add files failed: {ex.Message}");
            StatusText.Text = "Could not add files";
        }
    }

    private void YouTubeUrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateRunCancelAvailability(_analysisBusy);
        var url = YouTubeUrlBox.Text?.Trim() ?? "";
        if (YouTubeMediaFetcher.GetPlatform(url) != RemoteMediaPlatform.YouTube)
            return;

        _youtubeUrlDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _youtubeUrlDebounce.Stop();
        _youtubeUrlDebounce.Tick -= YouTubeUrlDebounce_Tick;
        _youtubeUrlDebounce.Tick += YouTubeUrlDebounce_Tick;
        _youtubeUrlDebounce.Start();
    }

    private async void YouTubeUrlDebounce_Tick(object? sender, object e)
    {
        _youtubeUrlDebounce?.Stop();
        await LoadYouTubeClipToolAsync();
    }

    private async void YouTubeUrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await LoadYouTubeClipToolAsync();
            return;
        }
    }

    /// <summary>
    /// Ensures TextChanged fires for paste / automation / programmatic fills that may not raise it.
    /// </summary>
    private void YouTubeUrlBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var url = YouTubeUrlBox.Text?.Trim() ?? "";
        if (YouTubeMediaFetcher.GetPlatform(url) != RemoteMediaPlatform.YouTube)
            return;
        if (string.Equals(YouTubeClipPanel.LoadedUrl, YouTubeMediaFetcher.NormalizeYouTubeWatchUrl(url) ?? url,
                StringComparison.OrdinalIgnoreCase)
            && YouTubeClipPanel.Visibility == Visibility.Visible)
            return;

        _youtubeUrlDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _youtubeUrlDebounce.Stop();
        _youtubeUrlDebounce.Tick -= YouTubeUrlDebounce_Tick;
        _youtubeUrlDebounce.Tick += YouTubeUrlDebounce_Tick;
        _youtubeUrlDebounce.Start();
    }

    private async Task LoadYouTubeClipToolAsync()
    {
        var raw = YouTubeUrlBox.Text?.Trim() ?? "";
        var url = YouTubeMediaFetcher.NormalizeYouTubeWatchUrl(raw) ?? raw;
        if (YouTubeMediaFetcher.GetPlatform(url) != RemoteMediaPlatform.YouTube)
        {
            StatusText.Text = "YouTube URL required";
            AppendLog("Enter a YouTube watch, shorts, or youtu.be URL for clip capture.");
            return;
        }

        if (string.Equals(YouTubeClipPanel.LoadedUrl, url, StringComparison.OrdinalIgnoreCase)
            && YouTubeClipPanel.Visibility == Visibility.Visible)
            return;

        HideLocalPreviewSurfaces();
        HideYouTubePreview();
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        YouTubeClipPanel.Visibility = Visibility.Visible;

        await YouTubeClipPanel.LoadFromUrlAsync(url);
        // Keep the user's pasted form (e.g. /shorts/) so download UX can treat Shorts differently.
        if (string.IsNullOrWhiteSpace(YouTubeUrlBox.Text))
            YouTubeUrlBox.Text = raw;
        PreviewStatusText.Text = "YouTube clip capture";
        StatusText.Text = "Clip tool loaded — use Record & analyze selection";
        AppendLog("YouTube clip capture ready. Set In/Out, click Record & analyze selection, then pick this app window in the screen-share dialog.");
        PersistYouTubeClipState();
        UpdateTransportAvailability();
    }

    private async Task PromptYouTubeCaptureAsync()
    {
        await new ContentDialog
        {
            Title = "YouTube uses screen capture",
            Content = "Run analysis does not download YouTube videos.\n\n"
                      + "1. Set In and Out on the timeline\n"
                      + "2. Click Record & analyze selection\n"
                      + "3. When prompted, share this Unbound Rockhound window\n"
                      + "4. The selected range plays and records automatically\n\n"
                      + "Use Full video download (yt-dlp) only if you need the entire file.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        }.ShowAsync();
    }

    private async void YouTubeClipPanel_RecordingCompleted(object? sender, string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_persistedYouTubeUrl))
            {
                await MediaSourceSidecar.WriteAsync(path, new MediaSourceSidecar
                {
                    SourceUrl = _persistedYouTubeUrl,
                    Title = "Capture · " + (YouTubeMediaFetcher.TryGetVideoId(_persistedYouTubeUrl) ?? "YouTube"),
                    Platform = "ScreenCapture",
                    CapturedAtUtc = DateTimeOffset.UtcNow
                });
            }

            ReplaceAnalysisQueue([path], analyzeImmediately: YouTubeClipPanel.ShouldAnalyzeAfterRecording);
            RefreshFileList();
            LoadMediaPreview(path);
            AppendLog($"Screen capture queued: {path}");
            PreviewStatusText.Text = Path.GetFileName(path);
            PersistYouTubeClipState();

            if (YouTubeClipPanel.ShouldAnalyzeAfterRecording)
            {
                _cts = new CancellationTokenSource();
                SetBusy(true);
                StagePanel.Reset();
                try
                {
                    await RunAnalysisInternalAsync();
                }
                finally
                {
                    SetBusy(false);
                    _cts?.Dispose();
                    _cts = null;
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Capture handoff failed";
            AppendLog($"ERROR: {ex.Message}");
            SetBusy(false);
        }
    }

    private async Task DownloadYouTubeSegmentAsync(bool analyzeAfter)
    {
        var url = YouTubeClipPanel.LoadedUrl ?? YouTubeUrlBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
            return;

        MediaClipRange clip;
        try
        {
            clip = YouTubeClipPanel.GetClipRange();
            clip.Validate(YouTubeClipPanel.DurationSeconds);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Invalid selection";
            AppendLog($"ERROR: {ex.Message}");
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        StagePanel.Reset();
        var fetcher = App.Services.GetRequiredService<YouTubeMediaFetcher>();
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var progress = new Progress<string>(msg =>
        {
            dispatcher.TryEnqueue(() =>
            {
                StatusText.Text = msg.Length > 80 ? msg[..77] + "..." : msg;
                AppendLog(msg);
            });
        });

        try
        {
            AppendLog($"yt-dlp segment: {clip.ToDownloadSectionSpec()} - {url}");
            ProgressBar.IsIndeterminate = true;
            var result = await Task.Run(
                () => fetcher.PrepareClipAsync(url, clip, progress, _cts.Token),
                _cts.Token);

            ReplaceAnalysisQueue([result.LocalFilePath], analyzeImmediately: analyzeAfter);
            RefreshFileList();
            LoadMediaPreview(result.LocalFilePath);
            AppendLog($"Segment downloaded: {result.LocalFilePath}");
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0.15;

            if (analyzeAfter)
                await RunAnalysisInternalAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled";
            AppendLog("Segment download cancelled.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Segment download failed";
            AppendLog($"ERROR: {ex.Message}");
            ProgressBar.IsIndeterminate = false;
        }
        finally
        {
            // Always clear busy here. RunAnalysisInternalAsync also clears busy when it runs.
            SetBusy(false);
            ProgressBar.IsIndeterminate = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void PersistYouTubeClipState()
    {
        var (url, inSec, outSec) = YouTubeClipPanel.GetPersistedState();
        _persistedYouTubeUrl = url;
        _persistedInSeconds = inSec;
        _persistedOutSeconds = outSec;
    }

    private async void PreviewYouTube_Click(object sender, RoutedEventArgs e) =>
        await ShowRemoteStreamPreviewAsync();

    private async void DownloadAndAnalyze_Click(object sender, RoutedEventArgs e)
    {
        var url = YouTubeUrlBox.Text?.Trim() ?? "";
        if (YouTubeMediaFetcher.GetPlatform(url) != RemoteMediaPlatform.YouTube
            && YouTubeMediaFetcher.GetPlatform(url) != RemoteMediaPlatform.Instagram)
        {
            StatusText.Text = "Paste a YouTube or Instagram URL first";
            AppendLog("Download needs a YouTube or Instagram URL in the box.");
            return;
        }

        if (YouTubeMediaFetcher.GetPlatform(url) == RemoteMediaPlatform.YouTube)
        {
            // Prefer the pasted form; also treat short clip-tool durations as Shorts-sized downloads.
            var isShorts = url.Contains("/shorts/", StringComparison.OrdinalIgnoreCase)
                || (YouTubeClipPanel.DurationSeconds > 0 && YouTubeClipPanel.DurationSeconds <= 60);
            var confirm = await new ContentDialog
            {
                Title = isShorts ? "Download this YouTube Short?" : "Download entire YouTube video?",
                Content = isShorts
                    ? "This uses yt-dlp to download the Short as a local MP4 (usually a few MB), then runs analysis."
                    : "This uses yt-dlp to download the full video file (can be hundreds of MB). "
                      + "For a shadow segment, use Record & analyze selection (screen capture) instead.",
                PrimaryButtonText = isShorts ? "Download & analyze" : "Download full video",
                CloseButtonText = "Cancel",
                DefaultButton = isShorts ? ContentDialogButton.Primary : ContentDialogButton.Close,
                XamlRoot = XamlRoot
            }.ShowAsync();
            if (confirm != ContentDialogResult.Primary)
                return;
        }

        await PrepareRemoteMediaAsync(streamWorkingCopy: false, analyzeAfter: true);
    }

    private async Task ShowRemoteStreamPreviewAsync()
    {
        var url = YouTubeUrlBox.Text?.Trim() ?? "";
        if (YouTubeMediaFetcher.GetPlatform(url) == RemoteMediaPlatform.YouTube)
        {
            await LoadYouTubeClipToolAsync();
            return;
        }

        var media = YouTubeMediaFetcher.TryCreateMediaRef(url);
        if (media is null)
        {
            StatusText.Text = "Invalid URL";
            AppendLog("Invalid or empty YouTube / Instagram URL.");
            return;
        }

        if (!AppPreferences.OnlineEnrichmentAllowed)
        {
            var confirm = await new ContentDialog
            {
                Title = "Online stream",
                Content = "Online enrichment is off in Settings. In-app preview needs the network. Continue?",
                PrimaryButtonText = "Preview",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            }.ShowAsync();
            if (confirm != ContentDialogResult.Primary)
                return;
        }

        try
        {
            HideLocalPreviewSurfaces();
            YouTubePreview.Visibility = Visibility.Visible;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            if (YouTubePreview.CoreWebView2 is null)
                await YouTubePreview.EnsureCoreWebView2Async();
            YouTubePreview.NavigateToString(media.EmbedHtml);
            YouTubeUrlBox.Text = media.CanonicalUrl;
            StatusText.Text = "Streaming preview";
            PreviewStatusText.Text = media.Platform == RemoteMediaPlatform.Instagram
                ? $"Instagram {media.MediaId}"
                : $"YouTube {media.MediaId}";
            AppendLog($"In-app stream preview: {media.CanonicalUrl}");
        }
        catch (Exception ex)
        {
            YouTubePreview.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            StatusText.Text = "Preview failed";
            AppendLog($"ERROR: {ex.Message}");
        }
    }

    private async Task PrepareRemoteMediaAsync(bool streamWorkingCopy, bool analyzeAfter)
    {
        var raw = YouTubeUrlBox.Text?.Trim() ?? "";
        var url = YouTubeMediaFetcher.GetPlatform(raw) == RemoteMediaPlatform.YouTube
            ? YouTubeMediaFetcher.NormalizeYouTubeWatchUrl(raw) ?? raw
            : raw;
        if (!YouTubeMediaFetcher.IsSupportedUrl(url))
        {
            StatusText.Text = "Invalid URL";
            AppendLog("Invalid or empty YouTube / Instagram URL.");
            return;
        }

        var platform = YouTubeMediaFetcher.GetPlatform(url);
        if (platform == RemoteMediaPlatform.YouTube && streamWorkingCopy)
        {
            await LoadYouTubeClipToolAsync();
            await PromptYouTubeCaptureAsync();
            return;
        }
        if (platform == RemoteMediaPlatform.Instagram && !InstagramSessionStore.HasSavedSession)
        {
            var signIn = await new ContentDialog
            {
                Title = "Instagram sign-in recommended",
                Content = "Most Instagram posts require a signed-in session. Sign in now, or continue as guest (may fail on private or login-gated posts).",
                PrimaryButtonText = "Sign in",
                SecondaryButtonText = streamWorkingCopy ? "Continue" : "Download anyway",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            }.ShowAsync();

            if (signIn == ContentDialogResult.Primary)
            {
                await new GeoMineralTrace_App.Dialogs.InstagramSignInDialog { XamlRoot = XamlRoot }.ShowAsync();
                UpdateRemoteAuthUi();
                UpdateToolStatus();
            }
            else if (signIn == ContentDialogResult.None)
            {
                return;
            }
        }

        if (!AppPreferences.OnlineEnrichmentAllowed)
        {
            var platformLabel = platform == RemoteMediaPlatform.Instagram ? "Instagram" : "YouTube";
            var confirm = await new ContentDialog
            {
                Title = streamWorkingCopy ? "Online stream analysis" : "Online download",
                Content = streamWorkingCopy
                    ? $"Online enrichment is off in Settings. Stream analysis still needs the network for a temporary working copy ({platformLabel}). Continue?"
                    : $"Online enrichment is off in Settings. {platformLabel} download still needs the network. Continue?",
                PrimaryButtonText = streamWorkingCopy ? "Stream & analyze" : "Download",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            }.ShowAsync();
            if (confirm != ContentDialogResult.Primary)
                return;
        }

        if (streamWorkingCopy && YouTubeMediaFetcher.TryCreateMediaRef(url) is { } preview)
        {
            try
            {
                HideLocalPreviewSurfaces();
                YouTubePreview.Visibility = Visibility.Visible;
                if (YouTubePreview.CoreWebView2 is null)
                    await YouTubePreview.EnsureCoreWebView2Async();
                YouTubePreview.NavigateToString(preview.EmbedHtml);
            }
            catch
            {
                // Preview is optional; analysis can continue without it.
            }
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        StagePanel.Reset();
        var fetcher = App.Services.GetRequiredService<YouTubeMediaFetcher>();
        var dispatcher = DispatcherQueue.GetForCurrentThread();

        var label = streamWorkingCopy ? "Preparing stream working copy..." : "Downloading remote media...";
        var downloadProgress = new Progress<string>(msg =>
        {
            dispatcher.TryEnqueue(() =>
            {
                StatusText.Text = msg.Length > 80 ? msg[..77] + "..." : msg;
                PreviewStatusText.Text = label;
                AppendLog(msg);
            });
        });

        try
        {
            AppendLog(streamWorkingCopy
                ? $"Stream & analyze: {url}"
                : $"Full download: {url}");
            ProgressBar.IsIndeterminate = true;
            if (YouTubePreview.Visibility != Visibility.Visible)
                PreviewPlaceholder.Visibility = Visibility.Visible;

            var result = await Task.Run(
                () => streamWorkingCopy
                    ? fetcher.PrepareStreamWorkingCopyAsync(url, progress: downloadProgress, cancellationToken: _cts.Token)
                    : fetcher.DownloadAsync(url, progress: downloadProgress, cancellationToken: _cts.Token),
                _cts.Token);

            var pathsToQueue = result.AllLocalPaths is { Count: > 0 }
                ? result.AllLocalPaths
                : [result.LocalFilePath];

            // Always replace the queue for a new remote URL — appending left prior videos in the
            // fusion evidence pool and made Hypotheses look "stuck" on the first analysis.
            ReplaceAnalysisQueue(pathsToQueue, analyzeImmediately: analyzeAfter);

            RefreshFileList();
            LoadMediaPreview(result.LocalFilePath);
            if (pathsToQueue.Count > 1)
                AppendLog($"Queued {pathsToQueue.Count} carousel items from {platform}.");
            AppendLog(result.IsStreamWorkingCopy
                ? $"Working copy queued: {result.LocalFilePath}"
                : $"Downloaded & queued: {result.LocalFilePath}");
            YouTubeUrlBox.Text = url;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0.15;
            UpdateTimeline(0.15);
            StatusText.Text = analyzeAfter
                ? (streamWorkingCopy ? "Working copy ready - analyzing..." : "Download complete - analyzing...")
                : (streamWorkingCopy ? "Working copy ready" : "Download complete");
            PreviewStatusText.Text = Path.GetFileName(result.LocalFilePath);

            if (analyzeAfter)
                await RunAnalysisInternalAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled";
            AppendLog("Remote media prepare cancelled.");
        }
        catch (Exception ex)
        {
            StatusText.Text = streamWorkingCopy ? "Stream prepare failed" : "Download failed";
            AppendLog($"ERROR: {ex.Message}");
            ProgressBar.IsIndeterminate = false;
        }
        finally
        {
            if (!analyzeAfter || StatusText.Text.Contains("fail", StringComparison.OrdinalIgnoreCase)
                              || StatusText.Text.Contains("Cancel", StringComparison.OrdinalIgnoreCase))
            {
                SetBusy(false);
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    private void HideLocalPreviewSurfaces()
    {
        PreviewPlayer.Source = null;
        PreviewPlayer.Visibility = Visibility.Collapsed;
        PreviewImage.Visibility = Visibility.Collapsed;
    }

    private void HideYouTubePreview()
    {
        YouTubePreview.Visibility = Visibility.Collapsed;
    }

    private void HideYouTubeClipPanel()
    {
        YouTubeClipPanel.Visibility = Visibility.Collapsed;
        UpdateTransportAvailability();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        HideYouTubePreview();
        HideYouTubeClipPanel();
        YouTubeClipPanel.Reset();
        PreviewPlayer.Source = null;
        PreviewPlayer.Visibility = Visibility.Collapsed;
        _files.Clear();
        RefreshFileList();
        LogText.Text = "";
        StatusText.Text = "Idle";
        PreviewStatusText.Text = "Preview - select media or run analysis";
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        KeyframeStrip.Children.Clear();
        ProgressBar.Value = 0;
        ProgressBar.IsIndeterminate = false;
        UpdateTimeline(0);
        StagePanel.Reset();
        _persistedYouTubeUrl = null;
        _persistedInSeconds = 0;
        _persistedOutSeconds = 0;
        UpdateToolStatus();
        LastResult = null;
        App.Services.GetRequiredService<AnalysisSessionContext>().Reset();
        DrawWaveform();
        UpdatePlayerConfidenceReadout(null);
        ClearAnalysisError();
        TimecodePositionText.Text = "00:00:00.000";
        TimecodeDurationText.Text = "00:00:00.000";
        SyncTransportPlayIcon(false);
        UpdateTransportAvailability();
        UpdateRunCancelAvailability(busy: false);
    }

    private async void LoadSession_Click(object sender, RoutedEventArgs e)
    {
        var store = App.Services.GetRequiredService<AnalysisSessionStore>();
        var ids = store.ListSessionIds();
        if (ids.Count == 0)
        {
            await new ContentDialog
            {
                Title = "No saved sessions",
                Content = "Run an analysis first. Sessions are saved under LocalAppData\\UnboundRockhound\\sessions.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
            return;
        }

        var combo = new ComboBox { MinWidth = 360, HorizontalAlignment = HorizontalAlignment.Stretch };
        var labels = new List<(Guid Id, string Label)>();
        foreach (var id in ids.Take(40))
        {
            var doc = await store.LoadAsync(id);
            if (doc is null) continue;
            var label = $"{doc.Session.DisplayName} | {doc.Session.CreatedAtUtc:u} | {doc.Evidence.Count} clues";
            labels.Add((id, label));
            combo.Items.Add(label);
        }

        if (combo.Items.Count == 0)
        {
            await new ContentDialog
            {
                Title = "No readable sessions",
                Content = "Session folders exist but could not be loaded.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
            return;
        }

        combo.SelectedIndex = 0;
        var dialog = new ContentDialog
        {
            Title = "Load analysis session",
            Content = combo,
            PrimaryButtonText = "Load",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var selected = labels[combo.SelectedIndex];
        var loaded = await store.LoadAsync(selected.Id);
        if (loaded is null) return;

        var session = App.Services.GetRequiredService<AnalysisSessionContext>();
        await session.HydrateFromDocumentAsync(loaded);
        LastResult = session.LastPipelineResult;
        _files.Clear();
        _files.AddRange(loaded.Session.SourceMediaPaths.Where(File.Exists));
        RefreshFileList();
        RefreshKeyframeStrip(LastResult);
        if (_files.Count > 0)
            LoadMediaPreview(_files[0]);
        StatusText.Text = "Session loaded";
        PreviewStatusText.Text = loaded.Session.DisplayName;
        AppendLog($"Loaded session {selected.Id:N} | evidence={loaded.Evidence.Count} | hypotheses={session.LastPipelineResult?.Hypotheses.Count ?? 0}");
        StagePanel.MarkAllComplete();
        UpdateTimeline(1);
        ProgressBar.Value = 1;
        DrawWaveform(seed: selected.Id.GetHashCode());
        UpdatePlayerConfidenceReadout(LastResult);
        ClearAnalysisError();
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var raw = YouTubeUrlBox.Text?.Trim() ?? "";
            var urlIsRemote = YouTubeMediaFetcher.IsSupportedUrl(raw);

            // A pasted remote URL is a new submission when the queue is empty, or when the
            // queue still holds a prior remote download that does not match this URL.
            // Do not override an explicit local-file queue just because the URL box has text.
            if (urlIsRemote && (_files.Count == 0 ||
                                (QueueLooksLikeRemoteDownloads() && QueueDoesNotMatchRemoteUrl(raw))))
            {
                if (YouTubeMediaFetcher.GetPlatform(raw) == RemoteMediaPlatform.YouTube)
                {
                    await LoadYouTubeClipToolAsync();
                    await PromptYouTubeCaptureAsync();
                    return;
                }

                await PrepareRemoteMediaAsync(streamWorkingCopy: true, analyzeAfter: true);
                return;
            }

            if (_files.Count == 0)
            {
                StatusText.Text = "Add media first";
                return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true);
            StagePanel.Reset();
            try
            {
                await RunAnalysisInternalAsync();
            }
            finally
            {
                SetBusy(false);
                _cts?.Dispose();
                _cts = null;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Run failed";
            AppendLog($"ERROR: {ex.Message}");
            ShowAnalysisError(ex.Message);
            SetBusy(false);
        }
    }

    /// <summary>
    /// Replace the Analyze queue for a new media submission so fusion cannot see prior videos.
    /// </summary>
    private void ReplaceAnalysisQueue(IReadOnlyList<string> paths, bool analyzeImmediately)
    {
        var prior = _files.Count;
        _files.Clear();
        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            _files.Add(path);

        // Always clear prior session/hypotheses when the queue is replaced for a new capture,
        // even if analyze is deferred (Record without immediate analyze).
        App.Services.GetRequiredService<AnalysisSessionContext>()
            .BeginNewAnalysis(
                $"queue-replace priorFiles={prior} newFiles={_files.Count} analyzeNow={analyzeImmediately}");
        LastResult = null;

        AppendLog(
            $"Analysis queue replaced ({prior} → {_files.Count}): " +
            string.Join(", ", _files.Select(Path.GetFileName)));
    }

    private bool QueueDoesNotMatchRemoteUrl(string rawUrl)
    {
        var media = YouTubeMediaFetcher.TryCreateMediaRef(rawUrl);
        if (media is null || string.IsNullOrWhiteSpace(media.MediaId))
            return _files.Count > 0;

        return !_files.Any(p =>
            p.Contains(media.MediaId, StringComparison.OrdinalIgnoreCase));
    }

    private bool QueueLooksLikeRemoteDownloads() =>
        _files.Count > 0 && _files.All(p =>
            p.Contains(AppDataPaths.FolderName, StringComparison.OrdinalIgnoreCase) &&
            (p.Contains($"{Path.DirectorySeparatorChar}downloads{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
             || p.Contains($"{Path.AltDirectorySeparatorChar}downloads{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
             || p.Contains("working-copy", StringComparison.OrdinalIgnoreCase)
             || p.Contains("captures", StringComparison.OrdinalIgnoreCase)
             || p.Contains("clips", StringComparison.OrdinalIgnoreCase)));

    private async Task RunAnalysisInternalAsync()
    {
        if (_files.Count == 0)
        {
            StatusText.Text = "Nothing to analyze";
            return;
        }

        var pipeline = App.Services.GetRequiredService<AnalysisPipeline>();
        var exporter = App.Services.GetRequiredService<ResearchReportExporter>();
        var sessionCtx = App.Services.GetRequiredService<AnalysisSessionContext>();
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var token = _cts?.Token ?? CancellationToken.None;

        ProgressBar.IsIndeterminate = false;
        PreviewStatusText.Text = $"Analyzing {_files.Count} file(s)...";

        var options = await BuildPipelineOptionsAsync(_files[0], token).ConfigureAwait(true);

        var progress = new Progress<ProgressReport>(r =>
        {
            dispatcher.TryEnqueue(() =>
            {
                if (r.SessionId is { } sid)
                    sessionCtx.ReportLiveProgress(sid, r, r.DisplayName ?? options.DisplayNameOverride ?? "Analysis");

                ProgressBar.Value = r.FractionComplete;
                UpdateTimeline(r.FractionComplete);
                StatusText.Text = r.Message ?? r.Stage;
                ActiveStageCaption.Text = string.IsNullOrWhiteSpace(r.Message)
                    ? r.Stage
                    : $"{r.Stage} — {r.Message}";
                ActiveStageCaption.Visibility = Visibility.Visible;
                StagePanel.ApplyStage(r.Stage, r.Stage.Equals("done", StringComparison.OrdinalIgnoreCase));
                UpdateTransportAvailability();
                UpdateRunCancelAvailability(busy: true);
            });
        });

        try
        {
            // Drop prior session before every real run so cancel/fail cannot leave
            // old CurrentSessionId paired with a board filled by the new pipeline session.
            sessionCtx.BeginNewAnalysis($"run-local files={_files.Count}");
            LastResult = null;

            AppendLog($"Starting analysis ({_files.Count} file(s)): {string.Join(", ", _files.Select(Path.GetFileName))}");
            if (!string.IsNullOrWhiteSpace(options.DisplayNameOverride))
                AppendLog($"Case title: {options.DisplayNameOverride}");

            var result = await Task.Run(
                async () => await pipeline.RunAsync(_files, options, progress: progress, cancellationToken: token)
                    .ConfigureAwait(false),
                token).ConfigureAwait(true);
            LastResult = result;
            sessionCtx.ApplyPipelineResult(result);
            var diagPath = AnalysisDiagnosticLog.TryGetPath(result.Session.Id)
                           ?? AnalysisDiagnosticLog.LatestLogPath;
            AppendLog(
                $"Complete - session={result.Session.Id:N} | evidence={result.Evidence.Count} | " +
                $"hypotheses={result.Hypotheses.Count}");
            if (!string.IsNullOrWhiteSpace(diagPath))
                AppendLog($"Diagnostic log: {diagPath}");
            if (result.Hypotheses.Count == 0 && !string.IsNullOrWhiteSpace(result.Session.FusionEmptyReason))
                AppendLog($"No hypotheses: {result.Session.FusionEmptyReason}");
            else if (result.Hypotheses.Count > 0)
                AppendLog($"Top hypothesis: {result.Hypotheses[0].Label} ({result.Hypotheses[0].Confidence.Value:P0})");

            var reportsDir = AppDataPaths.Sub("Reports");
            var paths = await exporter.ExportAsync(result, reportsDir);
            AppendLog($"Report exported: {paths.MarkdownPath}");
            StatusText.Text = result.Hypotheses.Count > 0
                ? $"Analysis complete — {result.Hypotheses.Count} hypothesis(es)"
                : "Analysis complete — 0 hypotheses (see Cases)";
            PreviewStatusText.Text = "Open Cases, Evidence Board, Hypotheses, or Map";
            ProgressBar.Value = 1;
            UpdateTimeline(1);
            StagePanel.MarkAllComplete();
            RefreshKeyframeStrip(result);
            DrawWaveform(seed: result.Session.Id.GetHashCode());
            UpdatePlayerConfidenceReadout(result);
            ClearAnalysisError();
            ActiveStageCaption.Visibility = Visibility.Collapsed;
            UpdateRunCancelAvailability(busy: false);

            // Auto-open this case's hypotheses so completion is obvious.
            MainWindow.Instance?.NavigateTo(typeof(HypothesesPage), "hypotheses");
        }
        catch (OperationCanceledException)
        {
            // Pipeline may have written partial evidence for a new session id — drop the live
            // board so RefuseAsync cannot fuse orphans under a mismatched session.
            sessionCtx.Reset();
            LastResult = null;
            StatusText.Text = "Cancelled";
            AppendLog("Cancelled.");
            PreviewStatusText.Text = "Cancelled — queue kept; re-run when ready";
            RefreshKeyframeStrip(null);
            UpdatePlayerConfidenceReadout(null);
            // Do not rethrow — callers are often async void UI handlers.
        }
        catch (Exception ex)
        {
            sessionCtx.Reset();
            LastResult = null;
            StatusText.Text = "Analysis failed";
            AppendLog($"ERROR: {ex.Message}");
            ShowAnalysisError(ex.Message);
            RefreshKeyframeStrip(null);
            UpdatePlayerConfidenceReadout(null);
            // Do not rethrow — surface via StatusText/log only.
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task<AnalysisPipelineOptions> BuildPipelineOptionsAsync(
        string primaryMedia,
        CancellationToken cancellationToken)
    {
        var options = new AnalysisPipelineOptions();
        var urlBox = YouTubeUrlBox.Text?.Trim();
        var sidecar = await MediaSourceSidecar.TryReadAsync(primaryMedia, cancellationToken).ConfigureAwait(false);

        options.SourceUrl = sidecar?.SourceUrl
                            ?? (YouTubeMediaFetcher.IsSupportedUrl(urlBox) ? urlBox : null);
        options.SourceTitle = sidecar?.Title;
        options.SourceDescription = sidecar?.Description;
        options.DisplayNameOverride = !string.IsNullOrWhiteSpace(sidecar?.Title)
            ? sidecar!.Title
            : null;

        if (!string.IsNullOrWhiteSpace(options.SourceUrl))
        {
            var platform = YouTubeMediaFetcher.GetPlatform(options.SourceUrl);
            options.SourceKind = platform switch
            {
                RemoteMediaPlatform.YouTube => AnalysisSourceKind.YouTube,
                RemoteMediaPlatform.Instagram => AnalysisSourceKind.Instagram,
                _ => AnalysisSourceKind.LocalFile
            };
            options.DiagnosticIngestNotes.Add(
                $"URL-backed source: {options.SourceUrl} (platform={platform}; working file must be local after yt-dlp/capture)");
        }
        else if (primaryMedia.Contains("captures", StringComparison.OrdinalIgnoreCase) ||
                 primaryMedia.Contains("clips", StringComparison.OrdinalIgnoreCase))
        {
            options.SourceKind = AnalysisSourceKind.ScreenCapture;
            options.SourceUrl ??= _persistedYouTubeUrl;
            if (string.IsNullOrWhiteSpace(options.DisplayNameOverride) &&
                !string.IsNullOrWhiteSpace(options.SourceUrl))
            {
                options.DisplayNameOverride =
                    "Capture · " + (YouTubeMediaFetcher.TryGetVideoId(options.SourceUrl) ?? "YouTube");
            }

            options.DiagnosticIngestNotes.Add(
                $"Screen-capture path: {_persistedYouTubeUrl ?? "(no URL)"} → file {primaryMedia}");
        }
        else
        {
            options.DiagnosticIngestNotes.Add($"Local file pick/queue: {primaryMedia}");
        }

        if (sidecar is not null)
            options.DiagnosticIngestNotes.Add(
                $"Sidecar present: title={sidecar.Title ?? "(none)"}; platform={sidecar.Platform ?? "(none)"}");
        else
            options.DiagnosticIngestNotes.Add("Sidecar .gmt-source.json: absent");

        try
        {
            if (File.Exists(primaryMedia))
            {
                var fi = new FileInfo(primaryMedia);
                options.DiagnosticIngestNotes.Add(
                    $"UI preflight size={fi.Length:N0} bytes modified={fi.LastWriteTimeUtc:O}");
            }
        }
        catch
        {
            // diagnostics only
        }

        return options;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedIndex < 0 || FileList.SelectedIndex >= _files.Count)
            return;
        LoadMediaPreview(_files[FileList.SelectedIndex]);
    }

    private void RefreshFileList()
    {
        var names = _files.Select(Path.GetFileName).Where(n => n is not null).Cast<string>().ToList();
        FileList.ItemsSource = names;
        QueueEmpty.Visibility = names.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FileList.Visibility = names.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (names.Count > 0)
            PreviewStatusText.Text = names[0];
        UpdateRunCancelAvailability(_analysisBusy);
    }

    private void LoadMediaPreview(string path)
    {
        if (!File.Exists(path))
            return;

        HideYouTubePreview();
        HideYouTubeClipPanel();

        if (IsVideo(path))
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            PreviewPlayer.Visibility = Visibility.Visible;
            PreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(path));
            PreviewStatusText.Text = Path.GetFileName(path);
            DrawWaveform(seed: path.GetHashCode());

            var player = PreviewPlayer.MediaPlayer;
            if (player is not null)
            {
                player.MediaOpened -= PreviewPlayer_MediaOpened;
                player.MediaOpened += PreviewPlayer_MediaOpened;
            }
        }
        else if (IsImage(path))
        {
            PreviewPlayer.Source = null;
            PreviewPlayer.Visibility = Visibility.Collapsed;
            ShowPreview(path, null);
        }

        UpdateTransportAvailability();
    }

    private void PreviewPlayer_MediaOpened(Windows.Media.Playback.MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateTransportAvailability();
            var session = sender.PlaybackSession;
            if (session.NaturalDuration > TimeSpan.Zero)
                UpdateTimecodeReadout(session.Position, session.NaturalDuration);
        });
    }

    private void RefreshKeyframeStrip(AnalysisPipelineResult? result)
    {
        KeyframeStrip.Children.Clear();
        if (result is null) return;

        var frames = result.Evidence
            .Where(e => e.Type == EvidenceType.Keyframe &&
                        !string.IsNullOrWhiteSpace(e.PreviewAssetPath) &&
                        File.Exists(e.PreviewAssetPath) &&
                        IsImage(e.PreviewAssetPath!))
            .OrderBy(e => e.MediaTimestamp)
            .Take(40)
            .ToList();

        foreach (var frame in frames)
        {
            var path = frame.PreviewAssetPath!;
            var prominence = ConfidenceProminence.FromEvidence(frame);
            var visual = ConfidenceVisual.Build(prominence, null);
            var border = new Border
            {
                Width = 96,
                Height = 64,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(prominence >= 0.65 ? 2 : 1),
                BorderBrush = visual.ConfidenceBrush,
                Opacity = visual.BorderOpacity,
                Tag = path
            };
            var img = new Image { Stretch = Stretch.UniformToFill };
            img.Source = new BitmapImage(new Uri(path));
            border.Child = img;
            border.PointerPressed += (_, _) =>
            {
                PreviewPlayer.Visibility = Visibility.Collapsed;
                PreviewPlayer.Source = null;
                ShowPreview(path, frame.MediaTimestamp);
            };
            ToolTipService.SetToolTip(border, frame.Summary);
            KeyframeStrip.Children.Add(border);
        }

        if (frames.Count > 0 && PreviewPlayer.Visibility != Visibility.Visible)
            ShowPreview(frames[0].PreviewAssetPath!, frames[0].MediaTimestamp);
        else if (frames.Count == 0 && FfmpegKeyframeExtractor.ResolveFfmpeg() is null)
            PreviewStatusText.Text = "No keyframe images - install ffmpeg and re-run";

        DrawWaveform(seed: result.Session.Id.GetHashCode());
    }

    private void ShowPreview(string path, TimeSpan? timestamp)
    {
        PreviewImage.Source = new BitmapImage(new Uri(path));
        PreviewImage.Visibility = Visibility.Visible;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        PreviewStatusText.Text = $"{Path.GetFileName(path)}" +
                                 (timestamp is { } t ? $" | {t:hh\\:mm\\:ss}" : "");
    }

    private static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideo(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".avi", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".webm", StringComparison.OrdinalIgnoreCase);
    }

    private void SetBusy(bool busy)
    {
        _analysisBusy = busy;
        UpdateRunCancelAvailability(busy);
        if (busy)
        {
            ClearAnalysisError();
            ActiveStageCaption.Visibility = Visibility.Visible;
        }
        else
        {
            ActiveStageCaption.Visibility = Visibility.Collapsed;
            ActiveStageCaption.Text = "";
        }

        UpdateTransportAvailability();
    }

    private void UpdateRunCancelAvailability(bool busy)
    {
        var hasQueue = _files.Count > 0;
        var hasUrl = YouTubeMediaFetcher.IsSupportedUrl(YouTubeUrlBox.Text?.Trim() ?? "");
        var canStart = !busy && (hasQueue || hasUrl);
        var sessionCtx = App.Services.GetRequiredService<AnalysisSessionContext>();
        var canDeep = !busy &&
                      (LastResult?.Evidence.Count > 0
                       || sessionCtx.LastPipelineResult?.Evidence.Count > 0
                       || sessionCtx.CurrentSessionId is not null);

        ActionAvailability.Set(
            RunButton,
            canStart,
            hasQueue
                ? $"Run analysis on {_files.Count} queued file(s)"
                : "Prepare / download the URL in the box, then analyze",
            busy
                ? $"Analysis in progress{(string.IsNullOrWhiteSpace(ActiveStageCaption.Text) ? "" : $": {ActiveStageCaption.Text}")}"
                : "Add media to the queue or paste a YouTube / Instagram URL first");

        ActionAvailability.Set(
            DeepAnalysisButton,
            canDeep,
            "Score Evidence Board, select corroborating clusters, generate ranked hypotheses",
            busy
                ? "Wait for the current run to finish"
                : "Run initial analysis first so evidence is on the board");

        ActionAvailability.Set(
            CancelButton,
            busy,
            "Cancel the running analysis / download",
            "No analysis is running");
    }

    private async void DeepAnalysis_Click(object sender, RoutedEventArgs e)
    {
        var sessionCtx = App.Services.GetRequiredService<AnalysisSessionContext>();
        if (sessionCtx.CurrentSessionId is null && sessionCtx.LastPipelineResult is null && LastResult is null)
        {
            StatusText.Text = "Run analysis first";
            return;
        }

        // Prefer applying LastResult if session context was cleared somehow.
        if (sessionCtx.LastPipelineResult is null && LastResult is not null)
            sessionCtx.ApplyPipelineResult(LastResult);

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var dispatcher = DispatcherQueue.GetForCurrentThread();

        SetBusy(true);
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = 0;
        StatusText.Text = "Deep Analysis…";
        AppendLog("Deep Analysis started — scoring evidence and selecting corroborating clusters…");

        var progress = new Progress<ProgressReport>(r =>
        {
            dispatcher.TryEnqueue(() =>
            {
                if (r.SessionId is { } sid)
                    sessionCtx.ReportLiveProgress(sid, r, r.DisplayName ?? "Deep Analysis");

                ProgressBar.Value = r.FractionComplete;
                StatusText.Text = r.Message ?? r.Stage;
                ActiveStageCaption.Text = string.IsNullOrWhiteSpace(r.Message)
                    ? r.Stage
                    : $"{r.Stage} — {r.Message}";
                ActiveStageCaption.Visibility = Visibility.Visible;
                UpdateRunCancelAvailability(busy: true);
            });
        });

        try
        {
            var deep = await Task.Run(
                async () => await sessionCtx.RunDeepAnalysisAsync(progress, token).ConfigureAwait(false),
                token).ConfigureAwait(true);

            LastResult = sessionCtx.LastPipelineResult;
            var selected = deep.Selection.SelectedEvidence.Count;
            var considered = deep.Selection.AllConsidered.Count(c => !c.Selected);
            AppendLog(
                $"Deep Analysis complete — selected={selected}, considered-not-selected={considered}, " +
                $"hypotheses={deep.Hypotheses.Count} (min cluster={deep.Options.MinimumClusterConfidence:F2})");
            foreach (var h in deep.Hypotheses.Take(3))
            {
                AppendLog($"  #{h.Rank} {h.Label} ({h.Confidence.Value:P0})");
                if (h.Reasoning is { Count: > 0 })
                    AppendLog($"     {h.Reasoning[0]}");
            }

            StatusText.Text = deep.Hypotheses.Count > 0
                ? $"Deep Analysis — {deep.Hypotheses.Count} hypothesis(es)"
                : "Deep Analysis — no cluster cleared threshold";
            PreviewStatusText.Text = "Review Evidence Board scores and Hypotheses reasoning";
            ProgressBar.Value = 1;
            UpdatePlayerConfidenceReadout(LastResult);
            ClearAnalysisError();
            MainWindow.Instance?.NavigateTo(typeof(HypothesesPage), "hypotheses");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Deep Analysis cancelled";
            AppendLog("Deep Analysis cancelled.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Deep Analysis failed";
            AppendLog($"Deep Analysis ERROR: {ex.Message}");
            ShowAnalysisError(ex.Message);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool HasPlayableLocalPreview()
    {
        var player = PreviewPlayer.MediaPlayer;
        // Source is enough to enable transport; duration may arrive after MediaOpened.
        return PreviewPlayer.Visibility == Visibility.Visible
               && player is not null
               && PreviewPlayer.Source is not null;
    }

    private void UpdateTransportAvailability()
    {
        var busy = _analysisBusy;
        var playable = HasPlayableLocalPreview();
        var youtubeClipVisible = YouTubeClipPanel.Visibility == Visibility.Visible;

        string disabledReason;
        string caption;
        if (busy)
        {
            disabledReason = string.IsNullOrWhiteSpace(ActiveStageCaption.Text)
                ? "Transport locked while analysis is running"
                : $"Transport locked — {ActiveStageCaption.Text}";
            caption = string.IsNullOrWhiteSpace(ActiveStageCaption.Text)
                ? "Analyzing…"
                : ActiveStageCaption.Text;
        }
        else if (youtubeClipVisible && !playable)
        {
            disabledReason =
                "These controls drive the local MediaPlayer preview. Use the YouTube clip workbench (In/Out + Record) for the embedded player.";
            caption = "YouTube clip mode";
        }
        else if (!playable)
        {
            disabledReason = "Load a local video into the preview (queue a file, download, or finish a capture) to enable transport.";
            caption = "No local preview";
        }
        else
        {
            disabledReason = "";
            caption = "Local preview ready";
        }

        var enable = playable && !busy;
        ActionAvailability.Set(TransportRewindButton, enable, "Skip back 5 seconds", disabledReason);
        ActionAvailability.Set(
            TransportPlayPauseButton,
            enable,
            "Play / pause local preview",
            disabledReason);
        ActionAvailability.Set(TransportForwardButton, enable, "Skip ahead 5 seconds", disabledReason);
        ActionAvailability.Set(
            TransportFrameBackButton,
            enable,
            "Step back ~1 frame (1/30 s)",
            disabledReason);
        ActionAvailability.Set(
            TransportFrameForwardButton,
            enable,
            "Step forward ~1 frame (1/30 s)",
            disabledReason);

        TransportAvailabilityCaption.Text = caption;
        TransportAvailabilityCaption.Visibility = enable ? Visibility.Collapsed : Visibility.Visible;

        ToolTipService.SetToolTip(
            TimelineScrubberHost,
            enable
                ? "Click to seek the local preview playhead"
                : disabledReason);
        TimelineScrubberHost.IsHitTestVisible = enable;
        ActionAvailability.ApplyCursor(TimelineScrubberHost, enable);
    }

    private void TimelineTrack_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateTimeline(_timelineFraction);

    private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        DrawWaveform();

    private void TimelineTrack_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _scrubDragging = true;
        TimelineTrack.CapturePointer(e.Pointer);
        SeekTimelineFromPointer(e.GetCurrentPoint(TimelineTrack).Position.X);
        e.Handled = true;
    }

    private void TimelineTrack_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_scrubDragging) return;
        SeekTimelineFromPointer(e.GetCurrentPoint(TimelineTrack).Position.X);
        e.Handled = true;
    }

    private void TimelineTrack_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_scrubDragging) return;
        _scrubDragging = false;
        TimelineTrack.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void SeekTimelineFromPointer(double pointerX)
    {
        var width = TimelineTrack.ActualWidth;
        if (width <= 0) return;
        var fraction = Math.Clamp(pointerX / width, 0, 1);
        UpdateTimeline(fraction);

        var player = PreviewPlayer.MediaPlayer;
        if (player is not null && PreviewPlayer.Visibility == Visibility.Visible)
        {
            var dur = player.PlaybackSession.NaturalDuration;
            var durSec = dur.TotalSeconds;
            if (durSec > 0 && !double.IsNaN(durSec))
            {
                var seek = TimeSpan.FromSeconds(durSec * fraction);
                player.PlaybackSession.Position = seek;
                UpdateTimecodeReadout(seek, dur);
            }
        }
    }

    private void UpdateTimeline(double fraction)
    {
        _timelineFraction = Math.Clamp(fraction, 0, 1);
        var width = TimelineTrack.ActualWidth;
        if (width <= 0) return;
        var w = width * _timelineFraction;
        TimelineFill.Width = w;
        TimelinePlayhead.Margin = new Thickness(Math.Max(0, w - 1), 0, 0, 0);
        TimelinePlayheadGlow.Margin = new Thickness(Math.Max(0, w - 3), 0, 0, 0);
        TimelineSelectionBand.Width = Math.Max(0, w);
        TimelineFractionText.Text = FormatTimelineCaption();
    }

    private string FormatTimelineCaption()
    {
        var player = PreviewPlayer.MediaPlayer;
        if (player is not null && PreviewPlayer.Visibility == Visibility.Visible)
        {
            var dur = player.PlaybackSession.NaturalDuration;
            if (dur.TotalSeconds > 0 && !double.IsNaN(dur.TotalSeconds))
            {
                var pos = TimeSpan.FromSeconds(dur.TotalSeconds * _timelineFraction);
                return $"{FormatTimecode(pos)} / {FormatTimecode(dur)}";
            }
        }

        return $"{_timelineFraction:P0}";
    }

    private void UpdateTimecodeReadout(TimeSpan position, TimeSpan duration)
    {
        TimecodePositionText.Text = FormatTimecode(position);
        TimecodeDurationText.Text = FormatTimecode(duration);
    }

    private static string FormatTimecode(TimeSpan t)
    {
        if (t < TimeSpan.Zero || double.IsNaN(t.TotalSeconds))
            return "00:00:00.000";
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }

    private void SyncTransportPlayIcon(bool playing) =>
        TransportPlayPauseIcon.Glyph = playing ? "\uE769" : "\uE768";

    private void TransportPlayPause_Click(object sender, RoutedEventArgs e)
    {
        var player = PreviewPlayer.MediaPlayer;
        if (player is null || PreviewPlayer.Visibility != Visibility.Visible) return;
        if (player.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing)
        {
            player.Pause();
            SyncTransportPlayIcon(false);
        }
        else
        {
            player.Play();
            SyncTransportPlayIcon(true);
        }
    }

    private void TransportRewind_Click(object sender, RoutedEventArgs e) =>
        NudgePlayback(TimeSpan.FromSeconds(-5));

    private void TransportForward_Click(object sender, RoutedEventArgs e) =>
        NudgePlayback(TimeSpan.FromSeconds(5));

    private void TransportFrameBack_Click(object sender, RoutedEventArgs e) =>
        NudgePlayback(TimeSpan.FromMilliseconds(-1.0 / 30 * 1000));

    private void TransportFrameForward_Click(object sender, RoutedEventArgs e) =>
        NudgePlayback(TimeSpan.FromMilliseconds(1.0 / 30 * 1000));

    private void NudgePlayback(TimeSpan delta)
    {
        var player = PreviewPlayer.MediaPlayer;
        if (player is null || PreviewPlayer.Visibility != Visibility.Visible) return;
        var session = player.PlaybackSession;
        var dur = session.NaturalDuration;
        var next = session.Position + delta;
        if (next < TimeSpan.Zero) next = TimeSpan.Zero;
        if (dur > TimeSpan.Zero && next > dur) next = dur;
        session.Position = next;
        if (dur.TotalSeconds > 0)
            UpdateTimeline(next.TotalSeconds / dur.TotalSeconds);
        UpdateTimecodeReadout(next, dur);
    }

    private void DrawWaveform(int? seed = null)
    {
        WaveformCanvas.Children.Clear();
        var w = WaveformCanvas.ActualWidth;
        var h = WaveformCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var rng = new Random(seed ?? (_files.FirstOrDefault()?.GetHashCode() ?? 42));
        var bars = (int)Math.Clamp(w / 4, 40, 160);
        var brush = new SolidColorBrush(ThemeResources.GetColor("GmtTimelineWaveform", ActualTheme));
        var active = new SolidColorBrush(ThemeResources.GetColor("GmtTimelineWaveformActive", ActualTheme));
        var activeUntil = (int)(bars * _timelineFraction);

        for (var i = 0; i < bars; i++)
        {
            var amp = 0.15 + rng.NextDouble() * 0.85;
            var barH = Math.Max(2, h * amp);
            var rect = new Rectangle
            {
                Width = Math.Max(1, (w / bars) - 1),
                Height = barH,
                Fill = i <= activeUntil ? active : brush,
                RadiusX = 1,
                RadiusY = 1,
                Opacity = i <= activeUntil ? 0.9 : 0.65
            };
            Canvas.SetLeft(rect, i * (w / bars));
            Canvas.SetTop(rect, (h - barH) / 2);
            WaveformCanvas.Children.Add(rect);
        }

        DrawEvidenceMarkers(w, h);
    }

    private void DrawEvidenceMarkers(double width, double height)
    {
        var result = LastResult ?? App.Services.GetRequiredService<AnalysisSessionContext>().LastPipelineResult;
        if (result is null) return;

        var durationSeconds = ResolvePreviewDurationSeconds();
        if (durationSeconds <= 0) return;

        var markers = result.Evidence
            .Where(e => !e.IsRejected && e.MediaTimestamp is { } ts && ts.TotalSeconds >= 0)
            .GroupBy(e => (int)(e.MediaTimestamp!.Value.TotalMilliseconds / 250))
            .Select(g => g.First())
            .ToList();

        foreach (var ev in markers)
        {
            var frac = Math.Clamp(ev.MediaTimestamp!.Value.TotalSeconds / durationSeconds, 0, 1);
            var x = frac * width;
            var markerH = height * (ev.Type == EvidenceType.ShadowMeasurement ? 0.95 : 0.72);
            var rect = new Rectangle
            {
                Width = ev.Type == EvidenceType.ShadowMeasurement ? 3 : 2,
                Height = markerH,
                Fill = EvidenceMarkerBrush(ev.Type),
                RadiusX = 1,
                RadiusY = 1,
                Opacity = ev.Type == EvidenceType.Keyframe ? 0.95 : 0.82
            };
            Canvas.SetLeft(rect, Math.Max(0, x - rect.Width / 2));
            Canvas.SetTop(rect, (height - markerH) / 2);
            ToolTipService.SetToolTip(rect, $"{ev.Type}: {ev.Summary}");
            WaveformCanvas.Children.Add(rect);
        }
    }

    private double ResolvePreviewDurationSeconds()
    {
        var player = PreviewPlayer.MediaPlayer;
        if (player is not null && PreviewPlayer.Visibility == Visibility.Visible)
        {
            var dur = player.PlaybackSession.NaturalDuration.TotalSeconds;
            if (dur > 0 && !double.IsNaN(dur))
                return dur;
        }

        var result = LastResult ?? App.Services.GetRequiredService<AnalysisSessionContext>().LastPipelineResult;
        var maxTs = result?.Evidence
            .Where(e => e.MediaTimestamp is not null)
            .Select(e => e.MediaTimestamp!.Value.TotalSeconds)
            .DefaultIfEmpty(0)
            .Max();
        return maxTs ?? 0;
    }

    private SolidColorBrush EvidenceMarkerBrush(EvidenceType type)
    {
        var key = type switch
        {
            EvidenceType.ShadowMeasurement => "GmtColorEvidenceSolar",
            EvidenceType.Keyframe => "GmtColorEvidenceKeyframe",
            EvidenceType.GpsEmbed => "GmtColorEvidenceGps",
            EvidenceType.PlaceNameMention or EvidenceType.MineralNameMention => "GmtColorEvidenceNer",
            _ => "GmtColorEvidenceGeneric"
        };
        return new SolidColorBrush(ThemeResources.GetColor(key, ActualTheme));
    }

    private void AppendLog(string line) =>
        LogText.Text = string.IsNullOrEmpty(LogText.Text) ? line : LogText.Text + "\n" + line;

    private void ShowAnalysisError(string message)
    {
        AnalysisErrorBar.Message = message;
        AnalysisErrorBar.IsOpen = true;
    }

    private void ClearAnalysisError()
    {
        AnalysisErrorBar.IsOpen = false;
        AnalysisErrorBar.Message = "";
    }

    private void UpdatePlayerConfidenceReadout(AnalysisPipelineResult? result)
    {
        if (result?.Hypotheses is not { Count: > 0 })
        {
            PlayerConfidenceText.Text = "--";
            PlayerConfidenceText.Foreground = new SolidColorBrush(
                ThemeResources.GetColor("GmtColorTextTertiary", ActualTheme));
            PlayerConfidenceText.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            PlayerConfidenceText.Opacity = 0.55;
            return;
        }

        var top = result.Hypotheses.OrderBy(h => h.Rank).First();
        var visual = ConfidenceVisual.ForHypothesis(top);
        PlayerConfidenceText.Text = top.Confidence.ToString();
        PlayerConfidenceText.Foreground = visual.ConfidenceBrush;
        PlayerConfidenceText.FontWeight = visual.ConfidenceWeight;
        PlayerConfidenceText.Opacity = visual.RowOpacity;
    }
}