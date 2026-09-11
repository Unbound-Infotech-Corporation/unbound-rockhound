using FluentAssertions;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Hypothesis.Fusion;

namespace GeoMineralTrace.Hypothesis.Tests;

public class FusionEngineTests
{
    [Fact]
    public void Fuse_RanksGpsAboveWeakPlaceMention_AndRespectsRejection()
    {
        var session = Guid.NewGuid();
        var gpsId = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var evidence = new List<EvidenceItem>
        {
            new()
            {
                Id = gpsId,
                AnalysisSessionId = session,
                Type = EvidenceType.GpsEmbed,
                Summary = "Embedded GPS",
                Confidence = Confidence.VeryHigh,
                Attributes = new Dictionary<string, string> { ["lat"] = "44.1", ["lon"] = "-120.5" }
            },
            new()
            {
                Id = placeId,
                AnalysisSessionId = session,
                Type = EvidenceType.PlaceNameMention,
                Summary = "Somewhere",
                Confidence = Confidence.Low,
                IsRejected = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.MineralNameMention,
                Summary = "obsidian",
                RawContent = "obsidian",
                Confidence = Confidence.High
            }
        };

        var ranked = new HypothesisFusionEngine().Fuse(session, evidence);
        ranked.Should().NotBeEmpty();
        ranked[0].SupportingEvidenceIds.Should().Contain(gpsId);
        ranked.Should().NotContain(h => h.SupportingEvidenceIds.Contains(placeId));
        ranked.Should().Contain(h => h.Label.Contains("Mineral", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fuse_AttachesCoords_ForKnownPlaceNames()
    {
        var session = Guid.NewGuid();
        var evidence = new List<EvidenceItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.PlaceNameMention,
                Summary = "Yosemite",
                Confidence = Confidence.Medium,
                Attributes = new Dictionary<string, string> { ["place"] = "Yosemite" }
            }
        };

        var ranked = new HypothesisFusionEngine().Fuse(session, evidence);
        ranked.Should().ContainSingle();
        ranked[0].Center.Should().NotBeNull();
        ranked[0].Center!.Value.LongitudeDegrees.Should().BeApproximately(-119.538, 0.01);
        ranked[0].Label.Should().Contain("Yosemite");
    }

    [Fact]
    public void Fuse_EmitsWeakSceneHypothesis_WhenOnlyTerrainTagsExist()
    {
        var session = Guid.NewGuid();
        var evidence = new List<EvidenceItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.Terrain,
                Summary = "desert / arid",
                Confidence = Confidence.Medium
            },
            new()
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.Vegetation,
                Summary = "sagebrush",
                Confidence = Confidence.Low
            }
        };

        var ranked = new HypothesisFusionEngine().Fuse(session, evidence);
        ranked.Should().ContainSingle();
        ranked[0].Label.Should().Contain("Scene-region");
        ranked[0].Confidence.Value.Should().BeLessThan(0.5);
    }

    [Fact]
    public void Fuse_RanksYellowstoneRiver_AboveMontanaStateCentroid()
    {
        var session = Guid.NewGuid();
        var evidence = new List<EvidenceItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.PlaceNameMention,
                Summary = "State/region mention: montana",
                RawContent = "montana",
                Confidence = Confidence.Medium
            },
            new()
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.PlaceNameMention,
                Summary = "Place-like phrase: Yellowstone River",
                RawContent = "Yellowstone River",
                Confidence = Confidence.High,
                Attributes = new Dictionary<string, string> { ["place"] = "Yellowstone River" }
            },
            new()
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.MineralNameMention,
                Summary = "Mineral/gem mention: agate",
                RawContent = "agate",
                Confidence = Confidence.High
            }
        };

        var ranked = new HypothesisFusionEngine().Fuse(session, evidence);
        ranked[0].Label.Should().Contain("Yellowstone River");
        ranked[0].Center.Should().NotBeNull();
        ranked[0].Center!.Value.LatitudeDegrees.Should().BeApproximately(46.408, 0.05);
        ranked[0].SupportingEvidenceIds.Count.Should().BeGreaterThan(1);
    }
}
