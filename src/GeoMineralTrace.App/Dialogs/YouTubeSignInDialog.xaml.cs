using GeoMineralTrace.Pipeline.Ingest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace GeoMineralTrace_App.Dialogs;

public sealed partial class YouTubeSignInDialog : ContentDialog
{
    public bool SessionSaved { get; private set; }

    public YouTubeSignInDialog()
    {
        InitializeComponent();
        Loaded += YouTubeSignInDialog_Loaded;
        IsPrimaryButtonEnabled = false;
    }

    private async void YouTubeSignInDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await AuthWebView.EnsureCoreWebView2Async();
            AuthWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            AuthWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            AuthWebView.NavigationCompleted += AuthWebView_NavigationCompleted;
            AuthWebView.Source = new Uri(
                "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F&hl=en");
            StatusLabel.Text = "Complete Google sign-in in the window below…";
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
            var signedIn = YouTubeSessionStore.LooksSignedIn(cookies);
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
            if (!YouTubeSessionStore.LooksSignedIn(cookies))
            {
                args.Cancel = true;
                StatusLabel.Text = "Still not signed in. Finish login, then try Save again.";
                return;
            }

            YouTubeSessionStore.WriteNetscapeCookies(cookies);
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
            "https://www.youtube.com",
            "https://youtube.com",
            "https://accounts.google.com",
            "https://www.google.com",
            "https://google.com"
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
                var expires = ToUnixExpires(c.Expires);
                map[key] = new SocialMediaCookie(
                    c.Domain,
                    c.Name,
                    c.Value,
                    c.Path,
                    c.IsSecure,
                    expires);
            }
        }

        return map.Values.ToList();
    }

    private static long ToUnixExpires(double expires)
    {
        // WebView2 Cookie.Expires is a double; 0 = session cookie.
        if (expires <= 0)
            return 0;

        // Prefer treating large values as Unix seconds.
        if (expires > 1_000_000_000d && expires < 10_000_000_000d)
            return (long)expires;

        try
        {
            // Fallback: OLE Automation date.
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
