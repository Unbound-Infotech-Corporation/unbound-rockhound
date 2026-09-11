using GeoMineralTrace.Core.App;
using GeoMineralTrace.Infrastructure.DependencyInjection;
using GeoMineralTrace.Infrastructure.Licensing;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace.Infrastructure.Social;
using GeoMineralTrace.Pipeline.Abstractions;
using GeoMineralTrace.Pipeline.Audio;
using GeoMineralTrace.Pipeline.Ocr;
using GeoMineralTrace_App.Helpers;
using GeoMineralTrace_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace GeoMineralTrace_App;

/// <summary>
/// Application entry point. Owns the DI container and main window reference
/// used by pages for HWND pickers and service resolution.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public static IServiceProvider Services { get; private set; } = null!;
    public static Window MainWindowInstance => ((App)Current)._window
        ?? throw new InvalidOperationException("Main window is not available yet.");

    public App()
    {
        _ = AppDataPaths.LocalRoot;
        RequestedTheme = AppThemeHelper.ResolveRequestedTheme();
        InitializeComponent();
        DiagnosticReportCenter.Initialize();
        UnhandledException += OnUnhandledException;
        Services = BuildServices();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        DiagnosticReportCenter.Log($"UnhandledException: {e.Exception.GetType().Name}: {e.Exception.Message}");
        var reportId = DiagnosticReportCenter.RecordException(e.Exception, "WinUI.UnhandledException", fatal: true);
        CrashReportPrompt.TryShow(reportId);
        e.Handled = true;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var bootstrap = Services.GetRequiredService<SeedDataBootstrapper>();
            await bootstrap.EnsureSeededAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            DiagnosticReportCenter.LogError(ex, "Seed bootstrap");
            try
            {
                var store = Services.GetRequiredService<GeoMineralTrace.Rockhounding.Storage.LocalityStore>();
                await store.InitializeAsync().ConfigureAwait(true);
            }
            catch (Exception initEx)
            {
                DiagnosticReportCenter.LogError(initEx, "LocalityStore init fallback");
            }
        }

        _window = new MainWindow();
        _window.Activate();
        CrashReportPrompt.TryShowPendingFromPreviousSession();

        try
        {
            await Services.GetRequiredService<ILicenseService>().InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            DiagnosticReportCenter.LogError(ex, "License restore");
        }

        try
        {
            await Services.GetRequiredService<ISocialAuthService>().InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            DiagnosticReportCenter.LogError(ex, "Social auth restore");
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddGeoMineralTraceCore();

        services.AddSingleton<IOcrEngine>(_ => new CompositeOcrEngine(
            "Windows.Media.Ocr + Sidecar",
            new WindowsMediaOcrEngine(),
            new SidecarOcrEngine()));

        services.AddSingleton<ITranscriptionEngine>(_ => new CompositeTranscriptionEngine(
            "Sidecar + Whisper CLI",
            new SidecarTranscriptionEngine(),
            new WhisperCliTranscriptionEngine()));

        return services.BuildServiceProvider();
    }
}

