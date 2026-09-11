using GeoMineralTrace.Core.Social;
using GeoMineralTrace.Infrastructure.Social;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GeoMineralTrace_App.Pages;

public sealed partial class SignInPage : Page
{
    private readonly ISocialAuthService _auth;
    private readonly SupabaseConfigStore _config;

    public SignInPage()
    {
        InitializeComponent();
        _auth = App.Services.GetRequiredService<ISocialAuthService>();
        _config = App.Services.GetRequiredService<SupabaseConfigStore>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var cfg = _config.Load();
        UrlBox.Text = cfg.Url;
        AnonKeyBox.Password = cfg.AnonKey;
        ConfigStatus.Text = cfg.IsConfigured
            ? "Project settings loaded."
            : "Paste your Supabase URL and anon key (see docs/social-layer.md).";

        if (_auth.CurrentSession is not null)
        {
            StatusText.Text = $"Already signed in as {_auth.CurrentSession.Email}.";
            MainWindow.Instance?.NavigateTo(typeof(ProfilePage), "profile");
        }
    }

    private void ModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var signUp = ModeToggle.IsOn;
        DisplayNameBox.Visibility = signUp ? Visibility.Visible : Visibility.Collapsed;
        SubmitButton.Content = signUp ? "Create account" : "Sign in";
    }

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        _config.Save(new SupabaseProjectConfig
        {
            Url = UrlBox.Text.Trim(),
            AnonKey = AnonKeyBox.Password.Trim()
        });
        ConfigStatus.Text = _config.Load().IsConfigured
            ? "Saved. You can sign in now."
            : "Saved, but URL/key still look incomplete.";
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        SubmitButton.IsEnabled = false;
        StatusText.Text = ModeToggle.IsOn ? "Creating account…" : "Signing in…";
        try
        {
            if (ModeToggle.IsOn)
            {
                await _auth.SignUpAsync(EmailBox.Text, PasswordBox.Password, DisplayNameBox.Text);
                StatusText.Text = "Account created. If email confirmation is enabled in Supabase, confirm before signing in.";
            }
            else
            {
                await _auth.SignInAsync(EmailBox.Text, PasswordBox.Password);
            }

            if (_auth.CurrentSession is not null)
                MainWindow.Instance?.NavigateTo(typeof(ProfilePage), "profile");
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }
}
