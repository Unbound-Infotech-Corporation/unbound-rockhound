using FluentAssertions;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Solar;
using GeoMineralTrace.Solar;

namespace GeoMineralTrace.Solar.Tests;

public class SolarLocusFromEvidenceTests
{
    [Fact]
    public void TryComputeLocus_ReturnsNull_WhenNoShadowEvidence()
    {
        var sessionId = Guid.NewGuid();
        var evidence = new List<EvidenceItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = sessionId,
                Type = EvidenceType.PlaceNameMention,
                Summary = "Oregon",
                RawContent = "Oregon",
                Confidence = Confidence.Low
            }
        };

        SolarLocusFromEvidence.TryComputeLocus(sessionId, evidence).Should().BeNull();
    }

    [Fact]
    public void TryComputeLocus_ComputesLocus_FromShadowMeasurementEvidence()
    {
        var calc = new SolarPositionCalculator();
        var engine = new SolarLocusEngine(calc);
        var denver = new GeoCoordinate(39.7392, -104.9903);
        var utc = new DateTimeOffset(2024, 6, 21, 19, 0, 0, TimeSpan.Zero);
        var truth = calc.Calculate(denver, utc);
        var ratio = Math.Tan(GeoCoordinate.DegreesToRadians(truth.ElevationDegrees));
        var sessionId = Guid.NewGuid();

        var evidence = new List<EvidenceItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = sessionId,
                Type = EvidenceType.ShadowMeasurement,
                Summary = "Shadow h/L",
                RawContent = $"{ratio:F6},1.0",
                Attributes = new Dictionary<string, string>
                {
                    ["objectHeight"] = ratio.ToString("F6"),
                    ["shadowLength"] = "1.0",
                    ["elevationDeg"] = truth.ElevationDegrees.ToString("F2")
                },
                Confidence = Confidence.High
            }
        };

        var locus = SolarLocusFromEvidence.TryComputeLocus(
            sessionId,
            evidence,
            engine,
            new LocusSearchBounds(35, 45, -110, -95),
            utc);

        locus.Should().NotBeNull();
        locus!.Cells.Should().NotBeEmpty();
        locus.MeasurementIds.Should().HaveCount(1);
    }

    [Fact]
    public void ShadowMeasurementParser_ParsesAttributesAndRawContent()
    {
        var sessionId = Guid.NewGuid();
        var item = new EvidenceItem
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = sessionId,
            Type = EvidenceType.ShadowMeasurement,
            Summary = "test",
            RawContent = "2.0,4.0",
            Confidence = Confidence.Medium,
            Attributes = new Dictionary<string, string>
            {
                ["objectHeight"] = "2.0",
                ["shadowLength"] = "4.0"
            }
        };

        var parsed = ShadowMeasurementParser.ParseFromEvidence(sessionId, [item]);
        parsed.Should().ContainSingle();
        parsed[0].EstimatedSolarElevationDegrees.Should().BeApproximately(26.57, 0.1);
    }
}
