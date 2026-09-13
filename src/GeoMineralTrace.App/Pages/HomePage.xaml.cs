using GeoMineralTrace.Core.Licensing;
using GeoMineralTrace.Infrastructure.Licensing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GeoMineralTrace_App.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshLicenseBanner();
            PlayEnterAnimation();
        };
    }

    private void RefreshLicenseBanner()
    {
        try
        {
            var license = App.Services.GetRequiredService<ILicenseService>();
            var e = license.Current;
            if (license.IsEntitled && e is not null
                && string.Equals(e.Status, LicenseStatuses.Active, StringComparison.OrdinalIgnoreCase))
            {
                LicenseBanner.IsOpen = false;
                return;
            }

            LicenseBanner.IsOpen = true;
            if (license.IsEntitled && e?.TrialEndsUtc is { } end)
            {
                var days = Math.Max(0, (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalDays));
                LicenseBanner.Severity = InfoBarSeverity.Informational;
                LicenseBanner.Title = "Evaluation trial";
                LicenseBanner.Message = $"{days} day(s) remaining. After the trial, paste your purchase key on Activate.";
            }
            else
            {
                LicenseBanner.Severity = InfoBarSeverity.Warning;
                LicenseBanner.Title = "Activation required";
                LicenseBanner.Message = "Your trial has ended. Open Activate and paste the key from your Unbound Infotech purchase email.";
            }
        }
        catch
        {
            LicenseBanner.IsOpen = false;
        }
    }

    private void PlayEnterAnimation()
    {
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(320)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, HomeRoot);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            From = 12,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(360)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, HomeRootTranslate);
        Storyboard.SetTargetProperty(slide, "Y");

        var sb = new Storyboard();
        sb.Children.Add(fade);
        sb.Children.Add(slide);
        sb.Begin();

        StaggerButton(BtnAnalyze, 40);
        StaggerButton(BtnPublicMines, 70);
        StaggerButton(BtnEvidence, 100);
        StaggerButton(BtnSolar, 140);
        StaggerButton(BtnMap, 180);
    }

    private static void StaggerButton(UIElement button, double delayMs)
    {
        button.Opacity = 0;
        button.RenderTransform = new TranslateTransform { Y = 8 };
        button.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = new Duration(TimeSpan.FromMilliseconds(280)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, button);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            From = 8,
            To = 0,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, button.RenderTransform);
        Storyboard.SetTargetProperty(slide, "Y");

        var sb = new Storyboard();
        sb.Children.Add(fade);
        sb.Children.Add(slide);
        sb.Begin();
    }

    private void GoAnalyze_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(AnalysisPage), "analysis");

    private void GoPublicMines_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(PublicMinesPage), "publicmines");

    private void GoEvidence_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(EvidenceBoardPage), "evidence");

    private void GoSolar_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(SolarAnalysisPage), "solar");

    private void GoMap_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(MapPage), "map");

    private void GoActivate_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(ActivatePage), "activate");
}
