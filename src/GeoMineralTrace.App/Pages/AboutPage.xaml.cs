using System.Reflection;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Licensing;
using GeoMineralTrace.Infrastructure.Licensing;
using GeoMineralTrace_App.Helpers;
using GeoMineralTrace_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GeoMineralTrace_App.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null
            ? "Version unknown"
            : $"Version {v.Major}.{v.Minor}.{v.Build}  ·  {AppBranding.CompanyShortName}";

        var email = AppPreferences.SupportReportEmail
                    ?? DiagnosticReportCenter.DefaultSupportEmail;
        SupportText.Text =
            string.IsNullOrWhiteSpace(email)
                ? $"Publisher: {AppBranding.CompanyName}. Set a support email in Settings → Diagnostics, or use {AppBranding.SupportEmail}."
                : $"Publisher: {AppBranding.CompanyName}\nSupport: {email}\n{AppBranding.CompanyWebsite}";

        try
        {
            var license = App.Services.GetRequiredService<ILicenseService>();
            var e = license.Current;
            if (license.IsEntitled && e is not null
                && string.Equals(e.Status, LicenseStatuses.Active, StringComparison.OrdinalIgnoreCase))
            {
                LicenseStatusText.Text = string.IsNullOrWhiteSpace(e.Email)
                    ? $"Licensed · {MaskKey(e.LicenseKey)}"
                    : $"Licensed to {e.Email}";
            }
            else if (license.IsEntitled && e?.TrialEndsUtc is { } end)
            {
                var days = Math.Max(0, (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalDays));
                LicenseStatusText.Text = $"Evaluation trial — {days} day(s) remaining";
            }
            else
            {
                LicenseStatusText.Text = "Not activated — open Activate to enter your purchase key";
            }
        }
        catch
        {
            LicenseStatusText.Text = "License status unavailable";
        }
    }

    private void Activate_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(ActivatePage), "activate");

    private static string MaskKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 8)
            return "UR-••••";
        return key[..7] + "••••";
    }
}
