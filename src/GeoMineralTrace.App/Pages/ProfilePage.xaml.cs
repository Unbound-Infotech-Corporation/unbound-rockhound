using GeoMineralTrace.Core.Social;
using GeoMineralTrace.Infrastructure.Social;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace GeoMineralTrace_App.Pages;

public sealed partial class ProfilePage : Page
{
    private readonly ISocialAuthService _auth;
    private readonly ISocialProfileService _profiles;
    private Guid? _viewingId;
    private bool _isOwn;

    public ProfilePage()
    {
        InitializeComponent();
        _auth = App.Services.GetRequiredService<ISocialAuthService>();
        _profiles = App.Services.GetRequiredService<ISocialProfileService>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is Guid id)
            _viewingId = id;
        else if (e.Parameter is string s && Guid.TryParse(s, out var parsed))
            _viewingId = parsed;
        else
            _viewingId = _auth.CurrentSession?.UserId;

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_auth.CurrentSession is null)
        {
            SignedOutPanel.Visibility = Visibility.Visible;
            ProfilePanel.Visibility = Visibility.Collapsed;
            ShopsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SignedOutPanel.Visibility = Visibility.Collapsed;
        ProfilePanel.Visibility = Visibility.Visible;

        var targetId = _viewingId ?? _auth.CurrentSession.UserId;
        _isOwn = targetId == _auth.CurrentSession.UserId;
        OwnerActions.Visibility = _isOwn ? Visibility.Visible : Visibility.Collapsed;
        EmailText.Visibility = _isOwn ? Visibility.Visible : Visibility.Collapsed;
        EmailText.Text = _isOwn ? _auth.CurrentSession.Email : "";

        try
        {
            StatusText.Text = "Loading…";
            var profile = await _profiles.GetByIdAsync(targetId);
            if (profile is null)
            {
                StatusText.Text = "Profile not found (trigger may still be creating it — try Edit to save).";
                DisplayNameText.Text = _isOwn ? "Your profile" : "Unknown";
                ShopsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            Bind(profile);
            StatusText.Text = _isOwn ? "This is your profile." : "Public profile (read-only).";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void Bind(SocialProfile profile)
    {
        DisplayNameText.Text = profile.DisplayName;
        HandleText.Text = string.IsNullOrWhiteSpace(profile.Handle) ? "(no handle)" : $"@{profile.Handle}";
        BioText.Text = string.IsNullOrWhiteSpace(profile.Bio) ? "No bio yet." : profile.Bio;
        RegionText.Text = string.IsNullOrWhiteSpace(profile.HomeRegion) ? "—" : profile.HomeRegion;
        if (!string.IsNullOrWhiteSpace(profile.AvatarUrl))
        {
            try
            {
                AvatarImage.Source = new BitmapImage(new Uri(profile.AvatarUrl));
            }
            catch
            {
                AvatarImage.Source = null;
            }
        }
        else
        {
            AvatarImage.Source = null;
        }

        BindShops(profile.Shops);
    }

    private void BindShops(IReadOnlyList<ProfileShop> shops)
    {
        ShopsList.Children.Clear();
        if (shops.Count == 0)
        {
            ShopsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ShopsPanel.Visibility = Visibility.Visible;
        foreach (var shop in shops)
        {
            var card = new StackPanel { Spacing = 6 };
            var title = string.IsNullOrWhiteSpace(shop.ShopName)
                ? shop.MarketplaceLabel
                : $"{shop.MarketplaceLabel} · {shop.ShopName}";
            card.Children.Add(new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources["GmtTextBody"],
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(shop.Description))
            {
                card.Children.Add(new TextBlock
                {
                    Text = shop.Description,
                    Style = (Style)Application.Current.Resources["GmtTextCaption"],
                    TextWrapping = TextWrapping.Wrap
                });
            }

            card.Children.Add(new HyperlinkButton
            {
                Content = "Open shop",
                NavigateUri = Uri.TryCreate(shop.ShopUrl, UriKind.Absolute, out var shopUri) ? shopUri : null
            });

            if (shop.Media.Count > 0)
            {
                var mediaRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8
                };
                foreach (var media in shop.Media.Take(8))
                {
                    if (media.IsVideo)
                    {
                        var openVideo = new Button
                        {
                            Content = "▶ Video",
                            Tag = media.MediaUrl,
                            Style = (Style)Application.Current.Resources["GmtButtonSecondary"]
                        };
                        openVideo.Click += OpenMedia_Click;
                        mediaRow.Children.Add(openVideo);
                    }
                    else
                    {
                        try
                        {
                            var img = new Image
                            {
                                Width = 96,
                                Height = 96,
                                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                                Source = new BitmapImage(new Uri(media.MediaUrl)),
                                Tag = media.MediaUrl
                            };
                            img.Tapped += async (_, _) =>
                            {
                                if (Uri.TryCreate(media.MediaUrl, UriKind.Absolute, out var u))
                                    await Launcher.LaunchUriAsync(u);
                            };
                            mediaRow.Children.Add(img);
                        }
                        catch
                        {
                            // skip bad URLs
                        }
                    }
                }

                card.Children.Add(mediaRow);
            }

            ShopsList.Children.Add(card);
        }
    }

    private async void OpenMedia_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url }
            && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Launcher.LaunchUriAsync(uri);
    }

    private void GoSignIn_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(SignInPage), "signin");

    private void Edit_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(ProfileEditPage), "profile");

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        await _auth.SignOutAsync();
        await ReloadAsync();
    }

    private async void Lookup_Click(object sender, RoutedEventArgs e)
    {
        if (_auth.CurrentSession is null)
        {
            StatusText.Text = "Sign in first to look up other profiles.";
            return;
        }

        var q = LookupBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(q))
            return;

        try
        {
            SocialProfile? profile;
            if (Guid.TryParse(q, out var id))
                profile = await _profiles.GetByIdAsync(id);
            else
                profile = await _profiles.GetByHandleAsync(q.TrimStart('@'));

            if (profile is null)
            {
                StatusText.Text = "No profile matched that handle or id.";
                return;
            }

            _viewingId = profile.Id;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
