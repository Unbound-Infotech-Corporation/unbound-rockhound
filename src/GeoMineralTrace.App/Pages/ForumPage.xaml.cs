using GeoMineralTrace.Core.Social;
using GeoMineralTrace.Infrastructure.Social;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace GeoMineralTrace_App.Pages;

public sealed partial class ForumPage : Page
{
    private readonly ISocialAuthService _auth;
    private readonly ISocialForumService _forum;
    private readonly ISocialProfileService _profiles;

    private ForumCategory? _selectedCategory;
    private ForumThread? _selectedThread;
    private bool _likedThread;
    private bool _redditMode;
    private readonly List<string> _pendingThreadImages = [];
    private readonly List<string> _pendingReplyImages = [];

    public ForumPage()
    {
        InitializeComponent();
        _auth = App.Services.GetRequiredService<ISocialAuthService>();
        _forum = App.Services.GetRequiredService<ISocialForumService>();
        _profiles = App.Services.GetRequiredService<ISocialProfileService>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ShowRootAsync();
    }

    private async Task ShowRootAsync()
    {
        HideAll();
        if (_auth.CurrentSession is null)
        {
            SignedOutPanel.Visibility = Visibility.Visible;
            return;
        }

        CategoriesPanel.Visibility = Visibility.Visible;
        CategoriesStatus.Text = "Loading categories…";
        FacebookStatus.Text = "Loading Facebook directory…";
        try
        {
            var profile = await _profiles.GetByIdAsync(_auth.CurrentSession.UserId)
                ?? _auth.CurrentProfile;
            var showReddit = profile?.ShowRedditDiscovery ?? true;

            var cats = (await _forum.ListCategoriesAsync())
                .Where(c =>
                    showReddit ||
                    (c.Id != ForumCategoryIds.FromReddit &&
                     !string.Equals(c.Slug, ForumCategoryIds.FromRedditSlug, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            CategoriesList.ItemsSource = cats;
            CategoriesStatus.Text = cats.Count == 0
                ? "No categories yet — apply Phase 2/3 migrations in Supabase."
                : showReddit
                    ? $"{cats.Count} categories."
                    : $"{cats.Count} categories (From Reddit hidden — enable in Edit profile).";

            var fb = await _forum.ListFacebookDirectoryAsync();
            FacebookList.ItemsSource = fb;
            FacebookStatus.Text = fb.Count == 0
                ? "No Facebook directory rows (apply Phase 3 migration)."
                : $"{fb.Count} public group links.";
        }
        catch (Exception ex)
        {
            CategoriesStatus.Text = ex.Message;
            FacebookStatus.Text = ex.Message;
        }
    }

    private void HideAll()
    {
        SignedOutPanel.Visibility = Visibility.Collapsed;
        CategoriesPanel.Visibility = Visibility.Collapsed;
        ThreadsPanel.Visibility = Visibility.Collapsed;
        NewThreadPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Collapsed;
    }

    private void GoSignIn_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.NavigateTo(typeof(SignInPage), "signin");

    private async void CategoriesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ForumCategory cat)
            return;
        _selectedCategory = cat;
        await LoadThreadsAsync();
    }

    private async void FacebookList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not FacebookDirectoryEntry entry)
            return;
        if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri))
            return;
        await Launcher.LaunchUriAsync(uri);
    }

    private async Task LoadThreadsAsync()
    {
        if (_selectedCategory is null)
            return;

        HideAll();
        ThreadsPanel.Visibility = Visibility.Visible;
        ThreadsTitle.Text = _selectedCategory.Name;
        ThreadsStatus.Text = "Loading…";
        RegionNoteText.Visibility = Visibility.Collapsed;
        NewThreadPanel.Visibility = Visibility.Collapsed;

        _redditMode = _selectedCategory.Id == ForumCategoryIds.FromReddit
            || string.Equals(_selectedCategory.Slug, ForumCategoryIds.FromRedditSlug, StringComparison.OrdinalIgnoreCase);

        NewThreadButton.Visibility = _redditMode ? Visibility.Collapsed : Visibility.Visible;
        ProcessNerButton.Visibility = _redditMode ? Visibility.Visible : Visibility.Collapsed;
        ThreadsList.Visibility = _redditMode ? Visibility.Collapsed : Visibility.Visible;
        RedditList.Visibility = _redditMode ? Visibility.Visible : Visibility.Collapsed;

        if (_redditMode)
        {
            RegionNoteText.Text =
                "Read-only Reddit discoveries. Unverified community chatter — not claims. Open a row for the source link.";
            RegionNoteText.Visibility = Visibility.Visible;
            try
            {
                var discoveries = await _forum.ListRedditDiscoveriesAsync();
                RedditList.ItemsSource = discoveries;
                ThreadsStatus.Text = discoveries.Count == 0
                    ? "No Reddit discoveries yet. Operator should run the reddit-discover edge function."
                    : $"{discoveries.Count} discoveries.";
            }
            catch (Exception ex)
            {
                ThreadsStatus.Text = ex.Message;
            }
            return;
        }

        string? regionFilter = null;
        if (_selectedCategory.IsRegionScoped || _selectedCategory.Id == ForumCategoryIds.Local)
        {
            var home = _auth.CurrentProfile?.HomeRegion;
            if (string.IsNullOrWhiteSpace(home))
            {
                RegionNoteText.Text =
                    "Home region is not set on your profile — showing all Local threads. Set Home region in Profile to filter.";
                RegionNoteText.Visibility = Visibility.Visible;
            }
            else
            {
                regionFilter = home;
                RegionNoteText.Text = $"Filtered to your home region: {home}";
                RegionNoteText.Visibility = Visibility.Visible;
            }
        }

        try
        {
            var threads = await _forum.ListThreadsAsync(_selectedCategory.Id, regionFilter);
            ThreadsList.ItemsSource = threads;
            ThreadsStatus.Text = threads.Count == 0 ? "No threads yet. Start one." : $"{threads.Count} threads.";
        }
        catch (Exception ex)
        {
            ThreadsStatus.Text = ex.Message;
        }
    }

    private async void ProcessNer_Click(object sender, RoutedEventArgs e)
    {
        // Client cannot INSERT mentions (RLS). Preview heuristic + remind operator to run edge.
        try
        {
            var discoveries = await _forum.ListRedditDiscoveriesAsync(80);
            var withState = 0;
            foreach (var d in discoveries)
            {
                var hit = CommunityPlaceHeuristic.TryResolveUsState($"{d.Title} {d.Snippet}");
                if (hit is not null)
                    withState++;
            }

            ThreadsStatus.Text =
                $"Community NER preview: {withState}/{discoveries.Count} titles match a US state centroid. " +
                "Lat/lon writes are performed by the reddit-discover edge (service role). " +
                "Re-run that job to refresh community_location_mentions — never merges into claims.";
        }
        catch (Exception ex)
        {
            ThreadsStatus.Text = ex.Message;
        }
    }

    private async void RedditList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not RedditDiscovery d)
            return;
        if (!Uri.TryCreate(d.Url, UriKind.Absolute, out var uri))
            return;
        await Launcher.LaunchUriAsync(uri);
    }

    private async void BackToCategories_Click(object sender, RoutedEventArgs e)
    {
        _selectedCategory = null;
        _redditMode = false;
        await ShowRootAsync();
    }

    private void ShowNewThread_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategory is null || _redditMode)
            return;

        NewThreadPanel.Visibility = Visibility.Visible;
        NewTitleBox.Text = "";
        NewBodyBox.Text = "";
        _pendingThreadImages.Clear();
        NewThreadAttachStatus.Text = "";

        var showRegion = _selectedCategory.IsRegionScoped || _selectedCategory.Id == ForumCategoryIds.Local;
        NewRegionBox.Visibility = showRegion ? Visibility.Visible : Visibility.Collapsed;
        NewRegionBox.Text = showRegion ? (_auth.CurrentProfile?.HomeRegion ?? "") : "";
    }

    private void CancelNewThread_Click(object sender, RoutedEventArgs e)
    {
        NewThreadPanel.Visibility = Visibility.Collapsed;
        _pendingThreadImages.Clear();
    }

    private async void AttachNewThreadImage_Click(object sender, RoutedEventArgs e)
    {
        var url = await PickAndUploadAsync();
        if (url is null)
            return;
        _pendingThreadImages.Add(url);
        NewThreadAttachStatus.Text = $"{_pendingThreadImages.Count} image(s) attached.";
    }

    private async void CreateThread_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategory is null || _redditMode)
            return;

        NewThreadAttachStatus.Text = "Posting…";
        try
        {
            string? region = null;
            if (NewRegionBox.Visibility == Visibility.Visible)
                region = string.IsNullOrWhiteSpace(NewRegionBox.Text) ? null : NewRegionBox.Text.Trim();

            var thread = await _forum.CreateThreadAsync(
                _selectedCategory.Id,
                NewTitleBox.Text,
                NewBodyBox.Text,
                region,
                _pendingThreadImages);

            NewThreadPanel.Visibility = Visibility.Collapsed;
            _pendingThreadImages.Clear();
            await OpenThreadAsync(thread.Id);
        }
        catch (Exception ex)
        {
            NewThreadAttachStatus.Text = ex.Message;
        }
    }

    private async void ThreadsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ForumThread thread)
            return;
        await OpenThreadAsync(thread.Id);
    }

    private async Task OpenThreadAsync(Guid threadId)
    {
        HideAll();
        DetailPanel.Visibility = Visibility.Visible;
        DetailStatus.Text = "Loading…";
        try
        {
            var thread = await _forum.GetThreadAsync(threadId)
                ?? throw new InvalidOperationException("Thread not found.");
            _selectedThread = thread;
            _likedThread = await _forum.HasLikedThreadAsync(threadId);

            DetailTitle.Text = thread.Title;
            DetailBody.Text = string.IsNullOrWhiteSpace(thread.Body) ? "(no body)" : thread.Body;
            var region = string.IsNullOrWhiteSpace(thread.RegionTag) ? "" : $" · {thread.RegionTag}";
            DetailMeta.Text =
                $"{thread.ReplyCount} replies · {thread.LikeCount} likes · {thread.CreatedAt:g}{region}";
            DetailImages.ItemsSource = thread.ImageUrls
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(TryBitmap)
                .Where(b => b is not null)
                .ToList();
            UpdateLikeButton();

            var posts = await _forum.ListPostsAsync(threadId);
            PostsList.ItemsSource = posts;
            ReplyBox.Text = "";
            _pendingReplyImages.Clear();
            ReplyAttachStatus.Text = "";
            DetailStatus.Text = posts.Count == 0 ? "No replies yet." : $"{posts.Count} replies.";
        }
        catch (Exception ex)
        {
            DetailStatus.Text = ex.Message;
        }
    }

    private void UpdateLikeButton()
    {
        LikeButton.Content = _likedThread ? "Unlike" : "Like";
        LikeStatus.Text = _selectedThread is null
            ? ""
            : $"{_selectedThread.LikeCount} likes";
    }

    private async void BackToThreads_Click(object sender, RoutedEventArgs e)
    {
        _selectedThread = null;
        await LoadThreadsAsync();
    }

    private async void Like_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedThread is null)
            return;

        try
        {
            _likedThread = await _forum.ToggleThreadLikeAsync(_selectedThread.Id);
            var refreshed = await _forum.GetThreadAsync(_selectedThread.Id);
            if (refreshed is not null)
                _selectedThread = refreshed;
            UpdateLikeButton();
            if (_selectedThread is not null)
            {
                var region = string.IsNullOrWhiteSpace(_selectedThread.RegionTag) ? "" : $" · {_selectedThread.RegionTag}";
                DetailMeta.Text =
                    $"{_selectedThread.ReplyCount} replies · {_selectedThread.LikeCount} likes · {_selectedThread.CreatedAt:g}{region}";
            }
        }
        catch (Exception ex)
        {
            DetailStatus.Text = ex.Message;
        }
    }

    private async void AttachReplyImage_Click(object sender, RoutedEventArgs e)
    {
        var url = await PickAndUploadAsync();
        if (url is null)
            return;
        _pendingReplyImages.Add(url);
        ReplyAttachStatus.Text = $"{_pendingReplyImages.Count} image(s) attached.";
    }

    private async void CreateReply_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedThread is null)
            return;

        ReplyAttachStatus.Text = "Posting…";
        try
        {
            await _forum.CreatePostAsync(_selectedThread.Id, ReplyBox.Text, _pendingReplyImages);
            _pendingReplyImages.Clear();
            await OpenThreadAsync(_selectedThread.Id);
        }
        catch (Exception ex)
        {
            ReplyAttachStatus.Text = ex.Message;
        }
    }

    private static BitmapImage? TryBitmap(string url)
    {
        try
        {
            return new BitmapImage(new Uri(url));
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> PickAndUploadAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance));
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".webp");
        picker.FileTypeFilter.Add(".gif");
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return null;

        try
        {
            using var rastream = await file.OpenReadAsync();
            await using var stream = rastream.AsStreamForRead();
            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType;
            return await _forum.UploadAttachmentAsync(stream, contentType, file.FileType);
        }
        catch (Exception ex)
        {
            DetailStatus.Text = ex.Message;
            NewThreadAttachStatus.Text = ex.Message;
            ReplyAttachStatus.Text = ex.Message;
            return null;
        }
    }
}
