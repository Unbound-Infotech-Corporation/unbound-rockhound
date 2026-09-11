using FluentAssertions;
using GeoMineralTrace.Pipeline.Ingest;

namespace GeoMineralTrace.Pipeline.Tests;

public class YouTubeMediaFetcherTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", true)]
    [InlineData("https://www.youtube.com/shorts/abcdef12345", true)]
    [InlineData("https://www.instagram.com/p/ABC123xyz/", true)]
    [InlineData("https://www.instagram.com/reel/ABC123xyz/", true)]
    [InlineData("https://www.instagram.com/reels/ABC123xyz/", true)]
    [InlineData("https://www.instagram.com/tv/ABC123xyz/", true)]
    [InlineData("https://example.com/video", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void IsSupportedUrl_ValidatesRemoteLinks(string url, bool expected)
    {
        YouTubeMediaFetcher.IsSupportedUrl(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", RemoteMediaPlatform.YouTube)]
    [InlineData("https://www.instagram.com/p/ABC123xyz/", RemoteMediaPlatform.Instagram)]
    public void GetPlatform_IdentifiesSource(string url, RemoteMediaPlatform expected)
    {
        YouTubeMediaFetcher.GetPlatform(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/abcdef12345", "abcdef12345")]
    [InlineData("https://www.youtube.com/embed/xyzABC123__", "xyzABC123__")]
    [InlineData("https://www.instagram.com/p/ABC123xyz/", "ABC123xyz")]
    [InlineData("https://www.instagram.com/reel/XYZ_9-ab/", "XYZ_9-ab")]
    public void TryGetMediaId_ParsesCommonUrlShapes(string url, string expectedId)
    {
        YouTubeMediaFetcher.TryGetMediaId(url).Should().Be(expectedId);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/abcdef12345", "abcdef12345")]
    [InlineData("https://www.youtube.com/embed/xyzABC123__", "xyzABC123__")]
    public void TryGetVideoId_ParsesYouTubeOnly(string url, string expectedId)
    {
        YouTubeMediaFetcher.TryGetVideoId(url).Should().Be(expectedId);
        YouTubeMediaFetcher.TryGetVideoId("https://www.instagram.com/p/ABC123xyz/").Should().BeNull();
    }

    [Fact]
    public void TryCreateVideoRef_BuildsEmbedHtml()
    {
        var video = YouTubeMediaFetcher.TryCreateVideoRef("https://youtu.be/dQw4w9WgXcQ");
        video.Should().NotBeNull();
        video!.VideoId.Should().Be("dQw4w9WgXcQ");
        video.CanonicalWatchUrl.Should().Contain("dQw4w9WgXcQ");
        video.EmbedHtml.Should().Contain("iframe_api");
        video.EmbedHtml.Should().Contain("dQw4w9WgXcQ");
    }

    [Fact]
    public void TryCreateMediaRef_BuildsInstagramEmbedHtml()
    {
        var media = YouTubeMediaFetcher.TryCreateMediaRef("https://www.instagram.com/reel/ABC123xyz/");
        media.Should().NotBeNull();
        media!.Platform.Should().Be(RemoteMediaPlatform.Instagram);
        media.MediaId.Should().Be("ABC123xyz");
        media.CanonicalUrl.Should().Contain("/reel/ABC123xyz");
        media.EmbedHtml.Should().Contain("instagram.com/reel/ABC123xyz/embed");
    }

    [Fact]
    public void YouTubeLooksSignedIn_DetectsAuthCookies()
    {
        YouTubeSessionStore.LooksSignedIn([
            new SocialMediaCookie(".youtube.com", "LOGIN_INFO", "x", "/", true, 0)
        ]).Should().BeTrue();

        YouTubeSessionStore.LooksSignedIn([
            new SocialMediaCookie(".example.com", "LOGIN_INFO", "x", "/", true, 0)
        ]).Should().BeFalse();
    }

    [Fact]
    public void InstagramLooksSignedIn_DetectsAuthCookies()
    {
        InstagramSessionStore.LooksSignedIn([
            new SocialMediaCookie(".instagram.com", "sessionid", "x", "/", true, 0)
        ]).Should().BeTrue();

        InstagramSessionStore.LooksSignedIn([
            new SocialMediaCookie(".example.com", "sessionid", "x", "/", true, 0)
        ]).Should().BeFalse();
    }

    [Fact]
    public void NormalizeYouTubeWatchUrl_StripsPlaylist()
    {
        var url = "https://www.youtube.com/watch?v=MwAMiddqytE&list=PLfb8E7lARImtQblqDz_n-YVcdEu0XN4_X";
        YouTubeMediaFetcher.NormalizeYouTubeWatchUrl(url)
            .Should().Be("https://www.youtube.com/watch?v=MwAMiddqytE");
    }

    [Fact]
    public void MediaClipRange_FormatsDownloadSection()
    {
        var clip = new MediaClipRange(15, 45);
        clip.ToDownloadSectionSpec().Should().Be("*0:15.000-0:45.000");
    }

    [Theory]
    [InlineData("https://www.youtube.com/live/abc123", true)]
    [InlineData("https://www.youtube.com/watch?v=abc123", false)]
    public void IsYouTubeLiveUrl_DetectsLiveLinks(string url, bool expected)
    {
        YouTubeMediaFetcher.IsYouTubeLiveUrl(url).Should().Be(expected);
    }

    [Fact]
    public void IframePlayerHtml_IncludesCaptureApis()
    {
        var html = YouTubeIframePlayerHtml.BuildPlayerPage("abc123");
        html.Should().Contain("getDisplayMedia");
        html.Should().Contain("MediaRecorder");
        html.Should().Contain("iframe_api");
        // Must post objects, not JSON.stringify — otherwise WebView2 double-encodes messages.
        html.Should().Contain("webview.postMessage(o)");
        html.Should().NotContain("postMessage(JSON.stringify");
    }

    [Theory]
    [InlineData("https://www.youtube.com/shorts/00X2pwgYPZk", "00X2pwgYPZk")]
    [InlineData("https://youtube.com/shorts/00X2pwgYPZk?feature=share", "00X2pwgYPZk")]
    public void ShortsUrl_NormalizesToWatchAndExtractsId(string url, string id)
    {
        YouTubeMediaFetcher.GetPlatform(url).Should().Be(RemoteMediaPlatform.YouTube);
        YouTubeMediaFetcher.TryGetVideoId(url).Should().Be(id);
        YouTubeMediaFetcher.NormalizeYouTubeWatchUrl(url).Should().Be($"https://www.youtube.com/watch?v={id}");
    }
}
