using FluentAssertions;
using GeoMineralTrace.Core.Social;

namespace GeoMineralTrace.Core.Tests.Social;

public sealed class SocialModelsTests
{
    [Fact]
    public void Session_IsExpired_when_past_skew_window()
    {
        var session = new SocialAuthSession
        {
            UserId = Guid.NewGuid(),
            Email = "a@example.com",
            AccessToken = "at",
            RefreshToken = "rt",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(30)
        };

        session.IsExpired(DateTimeOffset.UtcNow).Should().BeTrue();
        session.IsExpired(DateTimeOffset.UtcNow.AddMinutes(-5)).Should().BeFalse();
    }

    [Fact]
    public void SupabaseConfig_rejects_placeholder_and_empty()
    {
        new SupabaseProjectConfig { Url = "", AnonKey = "x" }.IsConfigured.Should().BeFalse();
        new SupabaseProjectConfig
        {
            Url = "https://YOUR_PROJECT.supabase.co",
            AnonKey = "eyJ"
        }.IsConfigured.Should().BeFalse();
        new SupabaseProjectConfig
        {
            Url = "https://abcdefgh.supabase.co",
            AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9"
        }.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void CommunityPlaceHeuristic_resolves_state_name_and_code()
    {
        var byName = CommunityPlaceHeuristic.TryResolveUsState("Agate near Montana river");
        byName.Should().NotBeNull();
        byName!.Code.Should().Be("MT");
        byName.Latitude.Should().BeApproximately(46.92, 0.1);

        var byCode = CommunityPlaceHeuristic.TryResolveUsState("Trip report from AZ last weekend");
        byCode.Should().NotBeNull();
        byCode!.Code.Should().Be("AZ");

        CommunityPlaceHeuristic.TryResolveUsState("No place here").Should().BeNull();
    }

    [Fact]
    public void ForumCategoryIds_FromReddit_is_stable()
    {
        ForumCategoryIds.FromReddit.Should().Be(Guid.Parse("11111111-1111-4111-8111-111111111105"));
        ForumCategoryIds.FromRedditSlug.Should().Be("from-reddit");
    }
}
