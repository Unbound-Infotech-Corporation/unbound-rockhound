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
        act.Should().Throw<InvalidOperationException>();
    }
}
