using GeoMineralTrace.Core.Licensing;
using GeoMineralTrace.Infrastructure.Licensing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GeoMineralTrace_App.Pages;

public sealed partial class ActivatePage : Page
{
    private readonly ILicenseService _license;

    public ActivatePage()
    {
        InitializeComponent();
        _license = App.Services.GetRequiredService<ILicenseService>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshStatus();
        if (e.Parameter is string key && !string.IsNullOrWhiteSpace(key)
            && !string.Equals(key, "activate", StringComparison.OrdinalIgnoreCase))
        {
            KeyBox.Text = key;
        }

        UpdateKeyHint();
    }

    private void KeyBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateKeyHint();

    private void UpdateKeyHint()
    {
        if (string.IsNullOrWhiteSpace(KeyBox.Text))
        {
            KeyHint.Text = "Keys look like UR-ABCD-EFGH-JKLM-NPQR. Spaces and missing dashes are OK.";
            return;
        }

        if (LicenseKeyFormat.LooksComplete(KeyBox.Text))
        {
            KeyHint.Text = $"Ready: {LicenseKeyFormat.Normalize(KeyBox.Text)}";
            return;
        }

        KeyHint.Text = "That does not look complete yet — keep pasting until you have 16 characters after UR.";
    }

    private void RefreshStatus()
    {
        var e = _license.Current;
        if (_license.IsEntitled && e is not null)
        {
            if (string.Equals(e.Status, LicenseStatuses.Active, StringComparison.OrdinalIgnoreCase))
            {
                StatusBar.Severity = InfoBarSeverity.Success;
                StatusBar.Title = "Licensed";
                StatusBar.Message = string.IsNullOrWhiteSpace(e.Email)
                    ? $"Active on this PC · {MaskKey(e.LicenseKey)}"
                    : $"Licensed to {e.Email} · {MaskKey(e.LicenseKey)}";
                DetailText.Text = $"Last checked: {e.ValidatedAtUtc:u} · Max devices: {e.MaxActivations}. Analyze and research tools are unlocked.";
            }
            else
            {
                StatusBar.Severity = InfoBarSeverity.Informational;
                StatusBar.Title = "Trial";
                var days = e.TrialEndsUtc is { } end
                    ? Math.Max(0, (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalDays))
                    : 0;
                StatusBar.Message = $"{days} day(s) left in the evaluation trial. Paste a purchased key anytime.";
                DetailText.Text = e.TrialEndsUtc is { } t
                    ? $"Trial ends {t:u} (UTC)."
                    : "";
            }
        }
        else
        {
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.Title = "Activation required";
            StatusBar.Message = e?.Status switch
            {
                LicenseStatuses.Refunded => "This license was refunded. Purchase again or contact support.",
                LicenseStatuses.Revoked => "This license was revoked. Contact support@unboundinfotech.com.",
                LicenseStatuses.Expired => "Your trial has ended. Enter a purchased license key to continue.",
                _ => "Enter your license key to unlock Analyze and research tools."
            };
            DetailText.Text = $"Device id: {_license.GetDeviceFingerprint()[..8]}…";
        }
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        ActivateButton.IsEnabled = false;
        StatusBar.IsOpen = true;
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Title = "Activating…";
        StatusBar.Message = "Contacting license server…";
        try
        {
            var result = await _license.ActivateAsync(KeyBox.Text).ConfigureAwait(true);
            if (!result.Ok)
            {
                StatusBar.Severity = InfoBarSeverity.Error;
                StatusBar.Title = "Activation failed";
                StatusBar.Message = result.Error ?? "Unknown error — check Sign in (Supabase) and the key, then retry.";
                return;
            }

            RefreshStatus();
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Title = "Activated";
            StatusBar.Message = "This PC is licensed. Analyze, Map, and research tools are unlocked.";
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = "Activation failed";
            StatusBar.Message = ex.Message;
        }
        finally
        {
            ActivateButton.IsEnabled = true;
        }
    }

    private static string MaskKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 8)
            return "UR-••••";
        return key[..7] + "••••";
    }
}
