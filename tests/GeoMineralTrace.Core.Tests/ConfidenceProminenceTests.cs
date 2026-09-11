using FluentAssertions;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;

namespace GeoMineralTrace.Core.Tests;

public class ConfidenceProminenceTests
{
    [Theory]
    [InlineData(0.9, 1, 0.495)] // single source dampens high confidence
    [InlineData(0.9, 3, 0.9)]
    [InlineData(0.25, 1, 0.1375)]
    public void FromConfidence_scales_with_corroboration(double conf, int sources, double expectedApprox)
    {
        var p = ConfidenceProminence.FromConfidence(conf, sources);
        p.Should().BeApproximately(expectedApprox, 0.001);
    }

    [Fact]
    public void FromEvidence_rejected_is_visually_muted()
    {
        var item = new EvidenceItem
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = Guid.NewGuid(),
            Type = EvidenceType.OcrText,
            Summary = "test",
            Confidence = Confidence.High,
            IsRejected = true
        };

        ConfidenceProminence.FromEvidence(item).Should().BeLessThan(0.2);
    }

    [Fact]
    public void FromEvidence_accepted_ranks_above_neutral()
    {
        var session = Guid.NewGuid();
        var neutral = new EvidenceItem
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = session,
            Type = EvidenceType.OcrText,
            Summary = "neutral",
            Confidence = Confidence.Medium
        };
        var accepted = new EvidenceItem
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = session,
            Type = EvidenceType.OcrText,
            Summary = "accepted",
            Confidence = Confidence.Medium,
            UserWeight = EvidenceWeight.Accepted
        };

        ConfidenceProminence.FromEvidence(accepted).Should().BeGreaterThan(
            ConfidenceProminence.FromEvidence(neutral));
    }
}
