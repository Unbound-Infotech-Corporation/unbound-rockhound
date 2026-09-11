using FluentAssertions;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.DeepAnalysis;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Evidence.Scoring;
using GeoMineralTrace.Hypothesis.Fusion;

namespace GeoMineralTrace.Hypothesis.Tests;

public class DeepHypothesisGeneratorTests
{
    [Fact]
    public void Generate_EmitsOneHypothesisPerSelectedCluster_WithReasoning()
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
                RawContent = "Yosemite",
                Confidence = Confidence.High,
                Attributes = new Dictionary<string, string> { ["place"] = "Yosemite" }
            },
            new()
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.OcrText,
                Summary = "Yosemite Valley sign",
                RawContent = "Welcome to Yosemite",
                Confidence = Confidence.High
            },
            new()
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = session,
                Type = EvidenceType.GpsEmbed,
                Summary = "GPS",
                Confidence = Confidence.VeryHigh,
                Attributes = new Dictionary<string, string> { ["lat"] = "37.865", ["lon"] = "-119.538" }
            }
        };

        var options = new DeepAnalysisOptions
        {
            MinimumItemConfidence = 0.2,
            MinimumClusterConfidence = 0.25
        };
        var scored = new EvidenceScorer(options).ScoreAll(evidence);
        var selection = new EvidenceClusterSelector(options).Select(scored);
        var hyps = new DeepHypothesisGenerator().Generate(session, selection);

        hyps.Should().NotBeEmpty();
        hyps[0].Reasoning.Should().NotBeNullOrEmpty();
        hyps[0].Reasoning![0].Should().Contain("Cluster key");
        hyps[0].SupportingEvidenceIds.Should().NotBeEmpty();
        hyps[0].Label.Should().StartWith("Deep:");
        selection.SelectedEvidence.Should().NotBeEmpty();
    }
}
