using System.Runtime.InteropServices.WindowsRuntime;
using GeoMineralTrace.Core.Social;
using GeoMineralTrace.Infrastructure.Social;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GeoMineralTrace_App.Pages;

public sealed partial class ProfileEditPage : Page
{
    private readonly ISocialAuthService _auth;
    private readonly ISocialProfileService _profiles;
    private List<ProfileShop> _shops = [];

    public ProfileEditPage()
    {
        InitializeComponent();
        _auth = App.Services.GetRequiredService<ISocialAuthService>();
        _profiles = App.Services.GetRequiredService<ISocialProfileService>();
        foreach (var (id, label) in MarketplaceKinds.All)
            MarketplaceCombo.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        if (MarketplaceCombo.Items.Count > 0)
            MarketplaceCombo.SelectedIndex = 0;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_auth.CurrentSession is null)
        {
            MainWindow.Instance?.NavigateTo(typeof(SignInPage), "signin");
            return;
        }

        try
        {
            var me = await _profiles.GetByIdAsync(_auth.CurrentSession.UserId);
            if (me is null)
                return;
            DisplayNameBox.Text = me.DisplayName;
            HandleBox.Text = me.Handle ?? "";
            BioBox.Text = me.Bio;
            RegionBox.Text = me.HomeRegion ?? "";
            ShowRedditToggle.IsOn = me.ShowRedditDiscovery;
            _shops = me.Shops.ToList();
            RenderShops();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void RenderShops()
    {
        ShopsEditList.Children.Clear();
        if (_shops.Count == 0)
        {
            ShopsEditList.Children.Add(new TextBlock
            {
                Text = "No shops linked yet.",
                Style = (Style)Application.Current.Resources["GmtTextCaption"]
            });
            return;
        }

        foreach (var shop in _shops)
        {
            var block = new StackPanel { Spacing = 6 };
            var title = string.IsNullOrWhiteSpace(shop.ShopName)
                ? shop.MarketplaceLabel
                : $"{shop.MarketplaceLabel} · {shop.ShopName}";
            block.Children.Add(new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources["GmtTextBody"],
                TextWrapping = TextWrapping.Wrap
            });
            block.Children.Add(new TextBlock
            {
                Text = shop.ShopUrl,
                Style = (Style)Application.Current.Resources["GmtTextCaption"],
                TextWrapping = TextWrapping.Wrap
            });

            if (shop.Media.Count > 0)
            {
                var mediaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                foreach (var media in shop.Media)
                {
                    if (media.IsVideo)
                    {
                        mediaRow.Children.Add(new TextBlock
                        {
                            Text = "Video",
                            VerticalAlignment = VerticalAlignment.Center,
                            Style = (Style)Application.Current.Resources["GmtTextCaption"]
                        });
                    }
                    else
                    {
                        try
                        {
                            mediaRow.Children.Add(new Image
                            {
                                Width = 64,
                                Height = 64,
                                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                                Source = new BitmapImage(new Uri(media.MediaUrl))
                            });
                        }
                        catch
                        {
                            // ignore
                        }
                    }

                    var removeMedia = new Button
                    {
                        Content = "Remove media",
                        Tag = media.Id,
                        Style = (Style)Application.Current.Resources["GmtButtonGhost"]
                    };
                    removeMedia.Click += RemoveMedia_Click;
                    mediaRow.Children.Add(removeMedia);
                }

                block.Children.Add(mediaRow);
            }

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var addMedia = new Button
            {
                Content = "Add image/video…",
                Tag = shop.Id,
                Style = (Style)Application.Current.Resources["GmtButtonSecondary"]
            };
            addMedia.Click += AddShopMedia_Click;
            var remove = new Button
            {
                Content = "Remove shop",
                Tag = shop.Id,
                Style = (Style)Application.Current.Resources["GmtButtonDestructive"]
            };
            remove.Click += RemoveShop_Click;
            actions.Children.Add(addMedia);
            actions.Children.Add(remove);
            block.Children.Add(actions);

            ShopsEditList.Children.Add(block);
        }
    }

    private async void AddShop_Click(object sender, RoutedEventArgs e)
    {
        if (_auth.CurrentSession is null)
            return;
        if (_shops.Count >= 12)
        {
            ShopStatusText.Text = "Maximum of 12 shops per profile.";
            return;
        }

        var marketplace = MarketplaceCombo.SelectedItem is ComboBoxItem { Tag: string id }
            ? id
            : MarketplaceKinds.Etsy;
        var url = ShopUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            ShopStatusText.Text = "Shop URL is required.";
            return;
        }

        ShopStatusText.Text = "Adding shop…";
        try
        {
            var shop = await _profiles.AddShopAsync(new ProfileShopDraft
            {
                Marketplace = marketplace,
                ShopName = ShopNameBox.Text.Trim(),
                ShopUrl = url,
                Description = ShopDescBox.Text.Trim(),
                SortOrder = _shops.Count
            });
            _shops.Add(shop);
            ShopNameBox.Text = "";
            ShopUrlBox.Text = "";
            ShopDescBox.Text = "";
            RenderShops();
            ShopStatusText.Text = "Shop added. You can attach images or videos next.";
        }
        catch (Exception ex)
        {
            ShopStatusText.Text = ex.Message;
        }
    }

    private async void RemoveShop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid shopId })
            return;
        ShopStatusText.Text = "Removing…";
        try
        {
            await _profiles.DeleteShopAsync(shopId);
            _shops.RemoveAll(s => s.Id == shopId);
            RenderShops();
            ShopStatusText.Text = "Shop removed.";
        }
        catch (Exception ex)
        {
            ShopStatusText.Text = ex.Message;
        }
    }

    private async void AddShopMedia_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid shopId })
            return;

        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".webp");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".webm");
        picker.FileTypeFilter.Add(".mov");
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        ShopStatusText.Text = "Uploading media…";
        try
        {
            using var rastream = await file.OpenReadAsync();
            await using var stream = rastream.AsStreamForRead();
            var contentType = file.ContentType;
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = file.FileType.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                              || file.FileType.Equals(".webm", StringComparison.OrdinalIgnoreCase)
                              || file.FileType.Equals(".mov", StringComparison.OrdinalIgnoreCase)
                    ? "video/mp4"
                    : "image/jpeg";
            }

            var media = await _profiles.UploadShopMediaAsync(shopId, stream, contentType, file.FileType);
            var idx = _shops.FindIndex(s => s.Id == shopId);
            if (idx >= 0)
            {
                var existing = _shops[idx];
                var mediaList = existing.Media.ToList();
                mediaList.Add(media);
                _shops[idx] = new ProfileShop
                {
                    Id = existing.Id,
                    ProfileId = existing.ProfileId,
                    Marketplace = existing.Marketplace,
                    ShopName = existing.ShopName,
                    ShopUrl = existing.ShopUrl,
                    Description = existing.Description,
                    SortOrder = existing.SortOrder,
                    CreatedAt = existing.CreatedAt,
                    UpdatedAt = existing.UpdatedAt,
                    Media = mediaList
                };
            }

            RenderShops();
            ShopStatusText.Text = "Media uploaded.";
        }
        catch (Exception ex)
        {
            ShopStatusText.Text = ex.Message;
        }
    }

    private async void RemoveMedia_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid mediaId })
            return;
        ShopStatusText.Text = "Removing media…";
        try
        {
            await _profiles.DeleteShopMediaAsync(mediaId);
            for (var i = 0; i < _shops.Count; i++)
            {
                var shop = _shops[i];
                if (shop.Media.All(m => m.Id != mediaId))
                    continue;
                _shops[i] = new ProfileShop
                {
                    Id = shop.Id,
                    ProfileId = shop.ProfileId,
                    Marketplace = shop.Marketplace,
                    ShopName = shop.ShopName,
                    ShopUrl = shop.ShopUrl,
                    Description = shop.Description,
                    SortOrder = shop.SortOrder,
                    CreatedAt = shop.CreatedAt,
                    UpdatedAt = shop.UpdatedAt,
                    Media = shop.Media.Where(m => m.Id != mediaId).ToList()
                };
            }

            RenderShops();
            ShopStatusText.Text = "Media removed.";
        }
        catch (Exception ex)
        {
            ShopStatusText.Text = ex.Message;
        }
    }

    private async void PickAvatar_Click(object sender, RoutedEventArgs e)
    {
        if (_auth.CurrentSession is null)
            return;

        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".webp");
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        StatusText.Text = "Uploading avatar…";
        try
        {
            using var rastream = await file.OpenReadAsync();
            await using var stream = rastream.AsStreamForRead();
            var contentType = file.ContentType;
            if (string.IsNullOrWhiteSpace(contentType))
                contentType = "image/jpeg";
            var url = await _profiles.UploadAvatarAsync(stream, contentType, file.FileType);
            StatusText.Text = "Avatar updated: " + url;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Saving…";
        try
        {
            await _profiles.UpdateAsync(new SocialProfileUpdate
            {
                DisplayName = DisplayNameBox.Text,
                Handle = HandleBox.Text,
                Bio = BioBox.Text,
                HomeRegion = RegionBox.Text,
                ShowRedditDiscovery = ShowRedditToggle.IsOn
            });
            StatusText.Text = "Saved.";
            MainWindow.Instance?.NavigateTo(typeof(ProfilePage), "profile");
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(ProfilePage), "profile");
}
