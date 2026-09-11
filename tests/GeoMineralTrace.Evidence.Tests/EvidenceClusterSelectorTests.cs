using FluentAssertions;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.DeepAnalysis;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Evidence.Scoring;

namespace GeoMineralTrace.Evidence.Tests;

public class EvidenceClusterSelectorTests
{
    private static ScoredEvidence Scored(
        Guid session,
        EvidenceType type,
        string summary,
        double combined,
        string? key,
        EvidenceModality modality,
        Confidence? conf = null)
    {
        var item = new EvidenceItem
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = session,
            Type = type,
            Summary = summary,
            RawContent = summary,
            Confidence = conf ?? Confidence.From(combined)
        };
        var breakdown = new EvidenceScoreBreakdown(combined, combined, combined, combined, combined, combined);
        return new ScoredEvidence(item, breakdown, key, modality);
    }

    [Fact]
    public void PrefersCorroboratingCluster_OverLargerUnrelatedHighScorers()
    {
        var session = Guid.NewGuid();
        var scored = new List<ScoredEvidence>
        {
            // Corroborating cluster (place + OCR + mineral) — lower individual scores but multi-modal
            Scored(session, EvidenceType.PlaceNameMention, "Maury Mountain", 0.55, "place:maury mountain", EvidenceModality.AsrNer),
            Scored(session, EvidenceType.OcrText, "Maury Mountain trail", 0.52, "place:maury mountain", EvidenceModality.Ocr),
            Scored(session, EvidenceType.MineralNameMention, "obsidian tip", 0.50, "place:maury mountain", EvidenceModality.Mineral),

            // Five unrelated high scorers, each alone
            Scored(session, EvidenceType.PlaceNameMention, "Random A", 0.90, "place:random a", EvidenceModality.AsrNer),
            Scored(session, EvidenceType.PlaceNameMention, "Random B", 0.88, "place:random b", EvidenceModality.AsrNer),
            Scored(session, EvidenceType.PlaceNameMention, "Random C", 0.87, "place:random c", EvidenceModality.AsrNer),
            Scored(session, EvidenceType.Terrain, "sagebrush", 0.86, "orphan:t1", EvidenceModality.Vision),
            Scored(session, EvidenceType.Vegetation, "juniper", 0.85, "orphan:t2", EvidenceModality.Vision)
        };

        var options = new DeepAnalysisOptions
        {
            MinimumItemConfidence = 0.25,
            MinimumClusterConfidence = 0.35,
            MaxClusters = 2
        };

        var result = new EvidenceClusterSelector(options).Select(scored);
        result.SelectedClusters.Should().NotBeEmpty();
        result.SelectedClusters[0].Key.Should().Be("place:maury mountain");
        result.SelectedClusters[0].ModalityCount.Should().BeGreaterThanOrEqualTo(3);

        // Unrelated high scorers marked considered-not-selected (or not top cluster)
        var randomA = result.AllConsidered.Single(c => c.Scored.Item.Summary == "Random A");
        // May or may not be selected if it clears threshold alone — with MaxClusters=2 and
        // corroboration boost, Maury should rank first; Random A is single-modality.
        result.SelectedClusters[0].Items.Should().Contain(i => i.Item.Summary == "Maury Mountain");
    }

    [Fact]
    public void MergesStateAndMineral_IntoSpecificPlaceCluster()
    {
        var session = Guid.NewGuid();
        var scored = new List<ScoredEvidence>
        {
            Scored(session, EvidenceType.PlaceNameMention, "Place-like phrase: Yellowstone River", 0.70,
                "place:yellowstone river", EvidenceModality.AsrNer),
            Scored(session, EvidenceType.PlaceNameMention, "State/region mention: montana", 0.55,
                "state:MT", EvidenceModality.AsrNer),
            Scored(session, EvidenceType.MineralNameMention, "agate", 0.60,
                "mineral:agate", EvidenceModality.Mineral)
        };

        var result = new EvidenceClusterSelector(new DeepAnalysisOptions
        {
            MinimumItemConfidence = 0.25,
            MinimumClusterConfidence = 0.30,
            MaxClusters = 2
        }).Select(scored);

        result.SelectedClusters.Should().ContainSingle();
        result.SelectedClusters[0].Key.Should().Be("place:yellowstone river");
        result.SelectedClusters[0].Items.Should().HaveCount(3);
        result.SelectedClusters[0].ModalityCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void MarksNonSelected_WithConsideredNote()
    {
        var session = Guid.NewGuid();
        var scored = new List<ScoredEvidence>
        {
            Scored(session, EvidenceType.PlaceNameMention, "Yosemite", 0.70, "place:yosemite", EvidenceModality.AsrNer),
            Scored(session, EvidenceType.OcrText, "Yosemite NP", 0.68, "place:yosemite", EvidenceModality.Ocr),
            Scored(session, EvidenceType.Terrain, "weak desert", 0.20, "orphan:weak", EvidenceModality.Vision)
        };

        var result = new EvidenceClusterSelector(new DeepAnalysisOptions
        {
            MinimumItemConfidence = 0.25,
            MinimumClusterConfidence = 0.30
        }).Select(scored);

        result.ApplyAnnotations();
        var weak = scored.Single(s => s.Item.Summary == "weak desert").Item;
        weak.DeepSelected.Should().BeFalse();
        weak.DeepSelectionNote.Should().Contain("considered, not selected");
        weak.DeepScore.Should().BeApproximately(0.20, 0.01);
    }
}
