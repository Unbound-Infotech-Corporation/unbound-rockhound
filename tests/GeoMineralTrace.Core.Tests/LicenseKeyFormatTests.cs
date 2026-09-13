using FluentAssertions;
using GeoMineralTrace.Core.Licensing;

namespace GeoMineralTrace.Core.Tests;

public class LicenseKeyFormatTests
{
    [Fact]
    public void Normalize_AcceptsDashedAndUndashed()
    {
        LicenseKeyFormat.Normalize("ur-abcd-efgh-jklm-npqr")
            .Should().Be("UR-ABCD-EFGH-JKLM-NPQR");
        LicenseKeyFormat.Normalize("URABCDEFGHJKLMNPQR")
            .Should().Be("UR-ABCD-EFGH-JKLM-NPQR");
    }

    [Fact]
    public void Normalize_RejectsBadLength()
    {
        var act = () => LicenseKeyFormat.Normalize("UR-ABCD");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*16 characters*");
    }

    [Fact]
    public void TryNormalize_Empty_ExplainsPaste()
    {
        LicenseKeyFormat.TryNormalize("  ", out _, out var error).Should().BeFalse();
        error.Should().Contain("Paste");
    }

    [Fact]
    public void TryNormalize_AcceptsSpacesAndLowercase()
    {
        LicenseKeyFormat.TryNormalize(" ur abcd efgh jklm npqr ", out var key, out var error)
            .Should().BeTrue();
        error.Should().BeNull();
        key.Should().Be("UR-ABCD-EFGH-JKLM-NPQR");
    }

    [Fact]
    public void LooksComplete_MatchesNormalize()
    {
        LicenseKeyFormat.LooksComplete("URABCDEFGHJKLMNPQR").Should().BeTrue();
        LicenseKeyFormat.LooksComplete("UR-ABCD").Should().BeFalse();
    }
}
