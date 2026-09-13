using System.Diagnostics;
using System.Reflection;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Updates;
using GeoMineralTrace.Infrastructure.Licensing;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace_App.Helpers;
using GeoMineralTrace_App.Pages;
using GeoMineralTrace_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GeoMineralTrace_App;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private readonly AnalysisSessionContext _session;
    private readonly ILicenseService _license;
    private readonly AppUpdateService _updates = new();
    private AppReleaseManifest? _pendingUpdate;
    private readonly DispatcherQueueTimer _updateTimer;

    private static readonly HashSet<string> LicenseFreeTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "about", "activate", "signin", "techniques", "settings"
    };

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        _session = App.Services.GetRequiredService<AnalysisSessionContext>();
        _license = App.Services.GetRequiredService<ILicenseService>();
        _session.ActiveCaseChanged += (_, _) => UpdateActiveCaseBreadcrumb();
        UpdateActiveCaseBreadcrumb();
        SetVersionBadge();
        UpdateLicenseBanner();
        _license.LicenseChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateLicenseBanner);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));

        NavFrame.Navigate(typeof(HomePage));

        _updateTimer = DispatcherQueue.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromSeconds(2);
        _updateTimer.IsRepeating = false;
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesQuietAsync();
        _updateTimer.Start();
    }

    private void SetVersionBadge()
    {
        try
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionBadge.Text = v is null ? AppBranding.ProductName : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            VersionBadge.Text = AppBranding.ProductName;
        }
    }

    private void UpdateLicenseBanner()
    {
        try
        {
            if (_license.IsEntitled)
            {
                LicenseInfoBar.IsOpen = false;
                return;
            }

            LicenseInfoBar.Title = "License required";
            LicenseInfoBar.Message =
                "Your trial has ended or this PC is not activated. Open Activate to enter your Stripe purchase key.";
            LicenseInfoBar.Severity = InfoBarSeverity.Warning;
            LicenseInfoBar.IsOpen = true;
        }
        catch
        {
            // Window may be tearing down.
        }
    }

    private void LicenseActivate_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(typeof(ActivatePage), "activate");

    private void UpdateActiveCaseBreadcrumb()
    {
        try
        {
            ActiveCaseBreadcrumb.Text = "Active case: " + _session.ActiveCaseCaption;
        }
        catch
        {
            // Window may be tearing down.
        }
    }

    public async Task CheckForUpdatesQuietAsync()
    {
        try
        {
            var result = await _updates.CheckAsync().ConfigureAwait(true);
            if (!result.HasUpdate || result.Manifest is null)
                return;

            _pendingUpdate = result.Manifest;
            UpdateInfoBar.Title = result.Manifest.Mandatory
                ? $"Important update {result.Manifest.Version}"
                : $"Update {result.Manifest.Version} available";
            UpdateInfoBar.Message =
                $"{result.Message}  Close this banner to be reminded later. Download installs beside your current folder — your LocalAppData cases are kept.";
            UpdateInfoBar.Severity = result.Manifest.Mandatory
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Success;
            UpdateInfoBar.IsOpen = true;
            DiagnosticReportCenter.Log($"Update available: {result.Manifest.Version} (local {result.LocalVersion})");
        }
        catch (Exception ex)
        {
            DiagnosticReportCenter.LogError(ex, "Update check");
        }
    }

    private void UpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        var url = _pendingUpdate?.DownloadUrl ?? _pendingUpdate?.ReleaseNotesUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            UpdateInfoBar.Message = "This release feed has no download URL yet. Ask your vendor for the latest zip.";
            return;
        }

        TryOpenUrl(url);
    }

    private void UpdateInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        if (!string.IsNullOrWhiteSpace(_pendingUpdate?.Version))
            AppPreferences.DismissedUpdateVersion = _pendingUpdate.Version;
        UpdateInfoBar.IsOpen = false;
    }

    private static void TryOpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagnosticReportCenter.LogError(ex, "Open update URL");
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args) =>
        NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
            NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            TryNavigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            return;

        if (!LicenseFreeTags.Contains(tag) && !_license.IsEntitled)
        {
            _ = PromptLicenseRequiredAsync();
            NavigateTo(typeof(ActivatePage), "activate");
            return;
        }

        var pageType = ResolvePage(tag);
        if (NavFrame.CurrentSourcePageType != pageType)
            TryNavigate(pageType);
    }

    private async Task PromptLicenseRequiredAsync()
    {
        try
        {
            var dlg = new ContentDialog
            {
                Title = "Activation required",
                Content =
                    "Purchase and activate a license to use analysis and research tools. A 14-day trial starts on first launch.",
                PrimaryButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await dlg.ShowAsync();
        }
        catch
        {
            // ignore
        }
    }

    public void NavigateTo(Type pageType, string? navTag = null, object? parameter = null)
    {
        if (navTag is not null)
        {
            foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
            {
                if (item.Tag?.ToString() == navTag)
                {
                    NavView.SelectedItem = item;
                    break;
                }
            }
        }

        if (NavFrame.CurrentSourcePageType != pageType || parameter is not null)
            TryNavigate(pageType, parameter);

        UpdateActiveCaseBreadcrumb();
    }

    private static Type ResolvePage(string tag) => tag switch
    {
        "home" => typeof(HomePage),
        "analysis" => typeof(AnalysisPage),
        "cases" => typeof(CasesPage),
        "evidence" => typeof(EvidenceBoardPage),
        "hypotheses" => typeof(HypothesesPage),
        "solar" => typeof(SolarAnalysisPage),
        "rockhounding" => typeof(RockhoundingPage),
        "glossary" => typeof(GlossaryPage),
        "glossary-credits" => typeof(GlossaryCreditsPage),
        "rivers" => typeof(RiversPage),
        "publicmines" => typeof(PublicMinesPage),
        "claims" => typeof(ClaimsPage),
        "history" => typeof(AnalysisHistoryPage),
        "trips" => typeof(TripsPage),
        "forum" => typeof(ForumPage),
        "profile" => typeof(ProfilePage),
        "signin" => typeof(SignInPage),
        "activate" => typeof(ActivatePage),
        "map" => typeof(MapPage),
        "reports" => typeof(ReportsPage),
        "techniques" => typeof(TechniquesPage),
        "about" => typeof(AboutPage),
        _ => throw new InvalidOperationException($"Unknown navigation tag: {tag}")
    };

    private void TryNavigate(Type pageType, object? parameter = null)
    {
        try
        {
            NavFrame.Navigate(pageType, parameter);
            DiagnosticReportCenter.SetNavigationContext(pageType.Name);
            DiagnosticReportCenter.SetLastUserAction($"Navigate → {pageType.Name}");
            UpdateActiveCaseBreadcrumb();
        }
        catch (Exception ex)
        {
            DiagnosticReportCenter.LogError(ex, $"Navigate {pageType.Name}");
            var reportId = DiagnosticReportCenter.RecordException(ex, $"Navigation.{pageType.Name}");
            CrashReportPrompt.TryShow(reportId);
        }
    }
}
