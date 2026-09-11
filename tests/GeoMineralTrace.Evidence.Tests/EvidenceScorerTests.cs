using FluentAssertions;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.DeepAnalysis;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Evidence.Scoring;

namespace GeoMineralTrace.Evidence.Tests;

public class EvidenceScorerTests
{
    private static EvidenceItem Item(
        Guid session,
        EvidenceType type,
        string summary,
        Confidence conf,
        string? raw = null,
        Dictionary<string, string>? attrs = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = session,
            Type = type,
            Summary = summary,
            RawContent = raw ?? summary,
            Confidence = conf,
            Attributes = attrs
        };

    [Fact]
    public void FullyCorroborating_MultiModal_ScoresHigh()
    {
        var session = Guid.NewGuid();
        var evidence = new List<EvidenceItem>
        {
            Item(session, EvidenceType.PlaceNameMention, "Maury Mountain", Confidence.High,
                attrs: new() { ["place"] = "Maury Mountain" }),
            Item(session, EvidenceType.OcrText, "Road sign near Maury Mountain", Confidence.High,
                raw: "Maury Mountain trail"),
            Item(session, EvidenceType.MineralNameMention, "obsidian", Confidence.High, raw: "obsidian"),
            Item(session, EvidenceType.GpsEmbed, "Embedded GPS", Confidence.VeryHigh,
                attrs: new() { ["lat"] = "44.08", ["lon"] = "-120.76" })
        };

        // Force shared place key on mineral by including place in summary for OCR/place.
        var scored = new EvidenceScorer().ScoreAll(evidence);
        scored.Should().HaveCount(4);

        var place = scored.Single(s => s.Item.Type == EvidenceType.PlaceNameMention);
        var ocr = scored.Single(s => s.Item.Type == EvidenceType.OcrText);

        // Place + OCR share Maury Mountain → corroboration should be elevated.
        place.Breakdown.Corroboration.Should().BeGreaterThanOrEqualTo(0.55);
        ocr.Breakdown.Corroboration.Should().BeGreaterThanOrEqualTo(0.55);
        place.Breakdown.Combined.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void ContradictoryPlaces_ScoreLowConsistency()
    {
        var session = Guid.NewGuid();
        var evidence = new List<EvidenceItem>
        {
            Item(session, EvidenceType.PlaceNameMention, "Yosemite", Confidence.High,
                attrs: new() { ["place"] = "Yosemite" }),
            Item(session, EvidenceType.PlaceNameMention, "Death Valley", Confidence.Medium,
                attrs: new() { ["place"] = "Death Valley" }),
            Item(session, EvidenceType.PlaceNameMention, "Yosemite Valley", Confidence.High,
                attrs: new() { ["place"] = "Yosemite" })
        };

        var scored = new EvidenceScorer().ScoreAll(evidence);
        var deathValley = scored.Single(s => s.Item.Summary.Contains("Death Valley"));
        deathValley.Breakdown.Consistency.Should().BeLessThan(0.5);
        deathValley.Breakdown.Combined.Should().BeLessThan(
            scored.Where(s => s.Item.Summary.Contains("Yosemite")).Average(s => s.Breakdown.Combined));
    }

    [Fact]
    public void SingleModality_Specific_ScoresModerate()
    {
        var session = Guid.NewGuid();
        var evidence = new List<EvidenceItem>
        {
            Item(session, EvidenceType.PlaceNameMention, "Glass Buttes", Confidence.High,
                attrs: new() { ["place"] = "Glass Buttes" })
        };

        var scored = new EvidenceScorer().ScoreAll(evidence).Single();
        scored.Breakdown.Specificity.Should().BeGreaterThanOrEqualTo(0.65);
        scored.Breakdown.Corroboration.Should().BeLessThanOrEqualTo(0.30); // single modality
        scored.Breakdown.Combined.Should().BeInRange(0.30, 0.70);
    }

    [Fact]
    public void ImplausibleSolarElevation_ScoresNearZeroPlausibility()
    {
        var session = Guid.NewGuid();
        var evidence = new List<EvidenceItem>
        {
            Item(session, EvidenceType.ShadowMeasurement, "Bad shadow", Confidence.High,
                raw: "0.01,10.0",
                attrs: new() { ["objectHeight"] = "0.01", ["shadowLength"] = "10.0", ["elevationDeg"] = "0.05" })
        };

        var scored = new EvidenceScorer().ScoreAll(evidence).Single();
        scored.Breakdown.Plausibility.Should().BeLessThan(0.1);
    }

    [Fact]
    public void UsesConfigurableWeights_FromOptions()
    {
        var session = Guid.NewGuid();
        var evidence = new List<EvidenceItem>
        {
            Item(session, EvidenceType.Terrain, "desert", Confidence.Low)
        };

        var options = new DeepAnalysisOptions
        {
            WeightSourceReliability = 1.0,
            WeightCorroboration = 0,
            WeightSpecificity = 0,
            WeightConsistency = 0,
            WeightPlausibility = 0
        };
        var scored = new EvidenceScorer(options).ScoreAll(evidence).Single();
        scored.Breakdown.Combined.Should().BeApproximately(scored.Breakdown.SourceReliability, 0.01);
    }
}
