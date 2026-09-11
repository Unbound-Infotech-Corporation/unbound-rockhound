using System.Diagnostics;
using System.Globalization;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Infrastructure.Social;
using GeoMineralTrace.Pipeline.Abstractions;
using GeoMineralTrace.Pipeline.Audio;
using GeoMineralTrace.Pipeline.Ingest;
using GeoMineralTrace.Pipeline.Keyframes;
using GeoMineralTrace_App.Dialogs;
using GeoMineralTrace_App.Helpers;
using GeoMineralTrace_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Path = System.IO.Path;
using WinRT.Interop;

namespace GeoMineralTrace_App.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _themeInitialized;
    private bool _homeUiReady;

    public SettingsPage()
    {
        InitializeComponent();
        OnlineEnrichmentToggle.IsOn = AppPreferences.OnlineEnrichmentAllowed;
        DataPathText.Text = AppDataPaths.LocalRoot;
        Loaded += (_, _) =>
        {
            SelectSavedTheme();
            RefreshEngineStatus();
            RefreshYouTubeSessionStatus();
            RefreshInstagramSessionStatus();
            RefreshCloudAccountStatus();
            RefreshGoogleEarthSettings();
            RefreshDiagnostics();
            RefreshUpdateSettings();
            RefreshHomeLocationSettings();
        };
    }

    private sealed record ReportRow(string Id, string Path, string Title, string Subtitle);

    private void RefreshDiagnostics()
    {
        SupportEmailBox.Text = AppPreferences.SupportReportEmail
            ?? DiagnosticReportCenter.DefaultSupportEmail;
        var reports = DiagnosticReportCenter.ListReports();
        ReportList.ItemsSource = reports.Select(r => new ReportRow(
            ExtractReportId(r.Path),
            r.Path,
            r.Title,
            r.LastWriteUtc.ToLocalTime().ToString("g"))).ToList();
        DiagnosticsStatus.Text =
            $"Reports folder: {DiagnosticReportCenter.ReportsDirectory}\n" +
            $"App log: {DiagnosticReportCenter.AppLogPath}\n" +
            $"{reports.Count} saved report(s).";
        OpenSelectedReportButton.IsEnabled = false;
    }

    private static string ExtractReportId(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var idx = name.LastIndexOf('-');
        return idx >= 0 && idx < name.Length - 1 ? name[(idx + 1)..] : name;
    }

    private void SupportEmail_LostFocus(object sender, RoutedEventArgs e)
    {
        AppPreferences.SupportReportEmail = string.IsNullOrWhiteSpace(SupportEmailBox.Text)
            ? null
            : SupportEmailBox.Text.Trim();
    }

    private void ReportList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        OpenSelectedReportButton.IsEnabled = ReportList.SelectedItem is ReportRow;

    private async void ReviewSelectedReport_Click(object sender, RoutedEventArgs e)
    {
        if (ReportList.SelectedItem is not ReportRow row)
            return;

        var dialog = new CrashReportDialog(row.Id, row.Path) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        RefreshDiagnostics();
    }

    private async void SendTestReport_Click(object sender, RoutedEventArgs e)
    {
        var reportId = DiagnosticReportCenter.RecordException(
            new InvalidOperationException("User-initiated test diagnostic report from Settings."),
            "Settings.SendTestReport");
        var dialog = new CrashReportDialog(reportId) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        RefreshDiagnostics();
    }

    private void OpenReportsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DiagnosticReportCenter.ReportsDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = DiagnosticReportCenter.ReportsDirectory,
            UseShellExecute = true
        });
    }

    private void OpenAppLog_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(DiagnosticReportCenter.AppLogPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DiagnosticReportCenter.AppLogPath,
                UseShellExecute = true
            });
            return;
        }

        _ = new ContentDialog
        {
            Title = "No app log yet",
            Content = "The app log is created after the first diagnostic event.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        }.ShowAsync();
    }

    private void GoogleEarthSettings_LostFocus(object sender, RoutedEventArgs e) =>
        SaveGoogleEarthSettings();

    private void RefreshGoogleEarthSettings()
    {
        GoogleEarthPathBox.Text = AppPreferences.GoogleEarthProPath ?? "";
        GoogleEarthExportsBox.Text = AppPreferences.GoogleEarthExportsPath ?? "";
        var resolved = GoogleEarthLauncher.ResolveGoogleEarthExecutable();
        var exports = GoogleEarthLauncher.ExportsDirectory;
        GoogleEarthStatus.Text = resolved is null
            ? "Google Earth Pro not detected — browse to googleearth.exe or install from google.com/earth."
            : $"Resolved: {resolved}\nExports: {exports}";
    }

    private void SaveGoogleEarthSettings()
    {
        AppPreferences.GoogleEarthProPath = string.IsNullOrWhiteSpace(GoogleEarthPathBox.Text)
            ? null
            : GoogleEarthPathBox.Text.Trim();
        AppPreferences.GoogleEarthExportsPath = string.IsNullOrWhiteSpace(GoogleEarthExportsBox.Text)
            ? null
            : GoogleEarthExportsBox.Text.Trim();
        RefreshGoogleEarthSettings();
        RefreshEngineStatus();
    }

    private async void BrowseGoogleEarth_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance));
        picker.FileTypeFilter.Add(".exe");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        GoogleEarthPathBox.Text = file.Path;
        SaveGoogleEarthSettings();
    }

    private async void BrowseExportsFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        GoogleEarthExportsBox.Text = folder.Path;
        SaveGoogleEarthSettings();
    }

    private void OpenExportsFolder_Click(object sender, RoutedEventArgs e)
    {
        SaveGoogleEarthSettings();
        GoogleEarthLauncher.OpenExportsFolder();
    }

    private async void TestGoogleEarth_Click(object sender, RoutedEventArgs e)
    {
        SaveGoogleEarthSettings();
        var exe = GoogleEarthLauncher.ResolveGoogleEarthExecutable();
        if (exe is null)
        {
            await new ContentDialog
            {
                Title = "Google Earth not found",
                Content = "Browse to googleearth.exe above, or install Google Earth Pro.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Launch failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private void RefreshInstagramSessionStatus()
    {
        if (InstagramSessionStore.HasSavedSession)
        {
            var when = InstagramSessionStore.LastSavedUtc?.ToLocalTime().ToString("g") ?? "unknown";
            InstagramSessionStatus.Text = $"Signed in — session saved {when}. Stream & analyze will use this account.";
            InstagramSignOutButton.IsEnabled = true;
        }
        else
        {
            InstagramSessionStatus.Text = "Not signed in — most Instagram posts require sign-in.";
            InstagramSignOutButton.IsEnabled = false;
        }
    }

    private async void InstagramSignIn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InstagramSignInDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        RefreshInstagramSessionStatus();
        RefreshEngineStatus();
    }

    private void InstagramSignOut_Click(object sender, RoutedEventArgs e)
    {
        InstagramSessionStore.ClearSession();
        RefreshInstagramSessionStatus();
        RefreshEngineStatus();
    }

    private void RefreshYouTubeSessionStatus()
    {
        if (YouTubeSessionStore.HasSavedSession)
        {
            var when = YouTubeSessionStore.LastSavedUtc?.ToLocalTime().ToString("g") ?? "unknown";
            YouTubeSessionStatus.Text = $"Signed in — session saved {when}. Stream & analyze will use this account.";
            YouTubeSignOutButton.IsEnabled = true;
        }
        else
        {
            YouTubeSessionStatus.Text = "Not signed in — public videos work without an account.";
            YouTubeSignOutButton.IsEnabled = false;
        }
    }

    private async void YouTubeSignIn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new YouTubeSignInDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        RefreshYouTubeSessionStatus();
        RefreshEngineStatus();
    }

    private void YouTubeSignOut_Click(object sender, RoutedEventArgs e)
    {
        YouTubeSessionStore.ClearSession();
        RefreshYouTubeSessionStatus();
        RefreshEngineStatus();
    }

    private void RefreshEngineStatus()
    {
        var ffmpeg = FfmpegKeyframeExtractor.ResolveFfmpeg();
        var ytdlp = YouTubeMediaFetcher.ResolveYtDlp();
        var whisper = WhisperCliTranscriptionEngine.ResolveWhisper();
        var ocr = App.Services.GetRequiredService<IOcrEngine>();
        var asr = App.Services.GetRequiredService<ITranscriptionEngine>();

        EngineStatusText.Text =
            $"OCR engine: {ocr.EngineName}\n" +
            $"  → App wires Windows.Media.Ocr first, then Sidecar (.ocr.txt). If only Sidecar runs, bitmaps may be empty/unreadable.\n" +
            $"ASR engine: {asr.EngineName}\n" +
            $"  → App wires Sidecar (.srt/.vtt/.txt) first, then Whisper CLI when installed.\n" +
            $"ffmpeg: {(ffmpeg ?? "not found — winget install ffmpeg")}\n" +
            $"yt-dlp: {(ytdlp ?? "not found — winget install yt-dlp")}\n" +
            $"whisper: {(whisper is null ? "not found (optional) — pip install openai-whisper  OR place media.whisper.srt / media.srt" : $"{whisper.Kind} @ {whisper.Path}")}\n" +
            $"YouTube session: {(YouTubeSessionStore.HasSavedSession ? "signed in" : "none")}\n" +
            $"Instagram session: {(InstagramSessionStore.HasSavedSession ? "signed in" : "none")}\n" +
            $"Google Earth: {(GoogleEarthLauncher.ResolveGoogleEarthExecutable() ?? "not configured")}\n" +
            $"KML exports: {GoogleEarthLauncher.ExportsDirectory}\n" +
            $"Online enrichment: {(AppPreferences.OnlineEnrichmentAllowed ? "permitted" : "local-only")}\n" +
            $"Solar in auto-pipeline: not run (manual keyframe shadow measure on Solar page)";
    }

    private void SelectSavedTheme()
    {
        var tag = AppThemeHelper.GetSavedThemeTag();
        for (var i = 0; i < ThemeCombo.Items.Count; i++)
        {
            if (ThemeCombo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == tag)
            {
                ThemeCombo.SelectedIndex = i;
                break;
            }
        }

        _themeInitialized = true;
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_themeInitialized || ThemeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
            return;

        AppThemeHelper.SaveTheme(tag);

        if (App.MainWindowInstance.Content is FrameworkElement root)
        {
            root.RequestedTheme = tag switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    private void OnlineEnrichmentToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppPreferences.OnlineEnrichmentAllowed = OnlineEnrichmentToggle.IsOn;
        RefreshEngineStatus();
    }

    private void RefreshHomeLocationSettings()
    {
        _homeUiReady = false;

        if (StateCentroidCombo.Items.Count == 0)
        {
            StateCentroidCombo.Items.Add(new ComboBoxItem { Content = "(choose a state…)", Tag = null });
            foreach (var place in UsPlaceCentroids.All)
            {
                StateCentroidCombo.Items.Add(new ComboBoxItem
                {
                    Content = place.Display,
                    Tag = place
                });
            }

            StateCentroidCombo.SelectedIndex = 0;
        }

        if (HomeLocationPreferences.TryGetHomeCoordinate(out var home))
        {
            HomeLatBox.Text = home.LatitudeDegrees.ToString("0.######", CultureInfo.InvariantCulture);
            HomeLonBox.Text = home.LongitudeDegrees.ToString("0.######", CultureInfo.InvariantCulture);
        }
        else
        {
            HomeLatBox.Text = "";
            HomeLonBox.Text = "";
        }

        HomeLabelBox.Text = HomeLocationPreferences.HomeLocationLabel ?? "";
        SearchRadiusBox.Value = HomeLocationPreferences.SearchRadiusMiles;
        PreferHomeOnMapToggle.IsOn = HomeLocationPreferences.PreferHomeLocationOnMap;
        HomeLocationStatus.Text = HomeLocationPreferences.StatusSummary();
        _homeUiReady = true;
    }

    private void StateCentroidCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_homeUiReady || StateCentroidCombo.SelectedItem is not ComboBoxItem item)
            return;
        if (item.Tag is not UsPlaceCentroids.Place place)
            return;

        HomeLatBox.Text = place.Latitude.ToString("0.######", CultureInfo.InvariantCulture);
        HomeLonBox.Text = place.Longitude.ToString("0.######", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(HomeLabelBox.Text))
            HomeLabelBox.Text = place.Display;
        SaveHomeLocationFromFields(showDialogOnError: false);
    }

    private void HomeLocation_LostFocus(object sender, RoutedEventArgs e) =>
        SaveHomeLocationFromFields(showDialogOnError: false);

    private void SearchRadiusBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_homeUiReady || double.IsNaN(SearchRadiusBox.Value))
            return;

        HomeLocationPreferences.SearchRadiusMiles = SearchRadiusBox.Value;
        HomeLocationStatus.Text = HomeLocationPreferences.StatusSummary();
    }

    private void PreferHomeOnMapToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_homeUiReady)
            return;
        HomeLocationPreferences.PreferHomeLocationOnMap = PreferHomeOnMapToggle.IsOn;
        HomeLocationStatus.Text = HomeLocationPreferences.StatusSummary();
    }

    private void SaveHomeLocation_Click(object sender, RoutedEventArgs e) =>
        SaveHomeLocationFromFields(showDialogOnError: true);

    private void ClearHomeLocation_Click(object sender, RoutedEventArgs e)
    {
        HomeLocationPreferences.ClearHomeCoordinate();
        HomeLatBox.Text = "";
        HomeLonBox.Text = "";
        HomeLabelBox.Text = "";
        if (StateCentroidCombo.Items.Count > 0)
            StateCentroidCombo.SelectedIndex = 0;
        HomeLocationStatus.Text = HomeLocationPreferences.StatusSummary();
    }

    private void OpenMapFromHome_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(MapPage), "map");

    private async void SaveHomeLocationFromFields(bool showDialogOnError)
    {
        HomeLocationPreferences.HomeLocationLabel = HomeLabelBox.Text;

        if (string.IsNullOrWhiteSpace(HomeLatBox.Text) && string.IsNullOrWhiteSpace(HomeLonBox.Text))
        {
            HomeLocationStatus.Text = HomeLocationPreferences.StatusSummary();
            return;
        }

        if (!double.TryParse(HomeLatBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(HomeLonBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            if (showDialogOnError)
            {
                await new ContentDialog
                {
                    Title = "Invalid coordinates",
                    Content = "Enter latitude and longitude as decimal degrees (e.g. 39.7392 / -104.9903).",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
            }

            return;
        }

        var coord = new GeoCoordinate(lat, lon);
        if (!coord.IsValid)
        {
            if (showDialogOnError)
            {
                await new ContentDialog
                {
                    Title = "Out of range",
                    Content = "Latitude must be −90…90 and longitude −180…180.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
            }

            return;
        }

        HomeLocationPreferences.SetHomeCoordinate(coord, HomeLabelBox.Text);
        if (!double.IsNaN(SearchRadiusBox.Value))
            HomeLocationPreferences.SearchRadiusMiles = SearchRadiusBox.Value;
        HomeLocationPreferences.PreferHomeLocationOnMap = PreferHomeOnMapToggle.IsOn;
        HomeLocationStatus.Text = HomeLocationPreferences.StatusSummary();
    }

    private void RefreshUpdateSettings()
    {
        CheckUpdatesToggle.IsOn = AppPreferences.CheckForUpdatesAllowed;
        UpdateFeedUrlBox.Text = AppPreferences.UpdateFeedUrl ?? "";
        var dismissed = AppPreferences.DismissedUpdateVersion;
        UpdateStatusText.Text = string.IsNullOrWhiteSpace(dismissed)
            ? $"Current build: {new AppUpdateService().LocalVersionDisplay}. Default feed: {AppUpdateService.DefaultFeedUrl}"
            : $"Current build: {new AppUpdateService().LocalVersionDisplay}. Dismissed update: {dismissed}";
    }

    private void CheckUpdatesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppPreferences.CheckForUpdatesAllowed = CheckUpdatesToggle.IsOn;
    }

    private void UpdateFeedUrl_LostFocus(object sender, RoutedEventArgs e)
    {
        AppPreferences.UpdateFeedUrl = string.IsNullOrWhiteSpace(UpdateFeedUrlBox.Text)
            ? null
            : UpdateFeedUrlBox.Text.Trim();
    }

    private async void CheckUpdatesNow_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = "Checking…";
        var service = new AppUpdateService();
        var result = await service.CheckAsync(force: true);
        UpdateStatusText.Text = result.Message;
        if (result.HasUpdate && MainWindow.Instance is { } main)
            await main.CheckForUpdatesQuietAsync();
    }

    private void ClearDismissedUpdate_Click(object sender, RoutedEventArgs e)
    {
        AppPreferences.DismissedUpdateVersion = null;
        RefreshUpdateSettings();
        UpdateStatusText.Text = "Cleared dismissed update. Use Check now to look again.";
    }

    private void RefreshCloudAccountStatus()
    {
        try
        {
            var auth = App.Services.GetRequiredService<ISocialAuthService>();
            if (!auth.IsConfigured)
            {
                CloudAccountStatus.Text = "Supabase not configured — open Sign in to paste project URL + anon key.";
                CloudSignOutButton.IsEnabled = false;
                return;
            }

            if (auth.CurrentSession is { } session)
            {
                var name = auth.CurrentProfile?.DisplayName ?? session.Email;
                CloudAccountStatus.Text = $"Signed in as {name} ({session.Email}).";
                CloudSignOutButton.IsEnabled = true;
            }
            else
            {
                CloudAccountStatus.Text = "Not signed in — create an account to use cloud profiles.";
                CloudSignOutButton.IsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            CloudAccountStatus.Text = "Account status unavailable: " + ex.Message;
            CloudSignOutButton.IsEnabled = false;
        }
    }

    private void CloudSignIn_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(SignInPage), "signin");

    private void CloudProfile_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(ProfilePage), "profile");

    private async void CloudSignOut_Click(object sender, RoutedEventArgs e)
    {
        await App.Services.GetRequiredService<ISocialAuthService>().SignOutAsync();
        RefreshCloudAccountStatus();
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DataPathText.Text);
        Process.Start(new ProcessStartInfo
        {
            FileName = DataPathText.Text,
            UseShellExecute = true
        });
    }
}
