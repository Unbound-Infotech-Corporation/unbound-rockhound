using GeoMineralTrace.Pipeline.Ingest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace GeoMineralTrace_App.Dialogs;

public sealed partial class InstagramSignInDialog : ContentDialog
{
    public bool SessionSaved { get; private set; }

    public InstagramSignInDialog()
    {
        InitializeComponent();
        Loaded += InstagramSignInDialog_Loaded;
        IsPrimaryButtonEnabled = false;
    }

    private async void InstagramSignInDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await AuthWebView.EnsureCoreWebView2Async();
            AuthWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            AuthWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            AuthWebView.NavigationCompleted += AuthWebView_NavigationCompleted;
            AuthWebView.Source = new Uri("https://www.instagram.com/accounts/login/");
            StatusLabel.Text = "Complete Instagram sign-in in the window below…";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "WebView2 failed: " + ex.Message;
        }
    }

    private async void AuthWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        try
        {
            var cookies = await CollectCookiesAsync().ConfigureAwait(true);
            var signedIn = InstagramSessionStore.LooksSignedIn(cookies);
            IsPrimaryButtonEnabled = signedIn;
            StatusLabel.Text = signedIn
                ? "Signed in detected — click Save session."
                : "Waiting for sign-in…";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var cookies = await CollectCookiesAsync().ConfigureAwait(true);
            if (!InstagramSessionStore.LooksSignedIn(cookies))
            {
                args.Cancel = true;
                StatusLabel.Text = "Still not signed in. Finish login, then try Save again.";
                return;
            }

            InstagramSessionStore.WriteNetscapeCookies(cookies);
            SessionSaved = true;
            StatusLabel.Text = "Session saved.";
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            StatusLabel.Text = ex.Message;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<List<SocialMediaCookie>> CollectCookiesAsync()
    {
        if (AuthWebView.CoreWebView2 is null)
            return [];

        var uris = new[]
        {
            "https://www.instagram.com",
            "https://instagram.com"
        };

        var map = new Dictionary<string, SocialMediaCookie>(StringComparer.OrdinalIgnoreCase);
        foreach (var uri in uris)
        {
            IReadOnlyList<CoreWebView2Cookie> batch;
            try
            {
                batch = await AuthWebView.CoreWebView2.CookieManager.GetCookiesAsync(uri);
            }
            catch
            {
                continue;
            }

            foreach (var c in batch)
            {
                var key = $"{c.Domain}|{c.Path}|{c.Name}";
                map[key] = new SocialMediaCookie(
                    c.Domain,
                    c.Name,
                    c.Value,
                    c.Path,
                    c.IsSecure,
                    ToUnixExpires(c.Expires));
            }
        }

        return map.Values.ToList();
    }

    private static long ToUnixExpires(double expires)
    {
        if (expires <= 0)
            return 0;

        if (expires > 1_000_000_000d && expires < 10_000_000_000d)
            return (long)expires;

        try
        {
            var dt = DateTime.FromOADate(expires);
            if (dt.Kind == DateTimeKind.Unspecified)
                dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return new DateTimeOffset(dt.ToUniversalTime()).ToUnixTimeSeconds();
        }
        catch
        {
            return 0;
        }
    }
}
