using FluentAssertions;
using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.DeepAnalysis;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Evidence.Store;
using GeoMineralTrace.Hypothesis.Fusion;
using GeoMineralTrace.Pipeline;
using GeoMineralTrace.Pipeline.Persistence;

namespace GeoMineralTrace.Pipeline.Tests;

public class DeepAnalysisIntegrationTests
{
    [Fact]
    public async Task PipelineThenDeepAnalysis_ProducesHypothesisWithExplanation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gmt-deep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var img = Path.Combine(dir, "frame.jpg");
            await File.WriteAllBytesAsync(img, [0xFF, 0xD8, 0xFF, 0xD9]);
            await File.WriteAllTextAsync(img + ".ocr.txt", "Welcome to Yosemite Valley");
            await File.WriteAllTextAsync(Path.ChangeExtension(img, ".txt"),
                "Hiking in Yosemite near Half Dome looking for quartz.");
            await File.WriteAllTextAsync(Path.ChangeExtension(img, ".tags.txt"), "granite mountain");

            var board = new EvidenceBoardStore();
            var store = new AnalysisSessionStore(Path.Combine(dir, "sessions"));
            var fusion = new HypothesisFusionEngine();
            var pipeline = new AnalysisPipeline(board, store, fusion);

            var pipelineResult = await pipeline.RunAsync([img], new AnalysisPipelineOptions
            {
                DisplayNameOverride = "Deep analysis integration"
            });
            pipelineResult.Session.Status.Should().Be(AnalysisStatus.Completed);
            pipelineResult.Evidence.Should().NotBeEmpty();

            // Seed a GPS corroboration matching Yosemite for a strong multi-modal cluster.
            board.Add(new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = pipelineResult.Session.Id,
                Type = EvidenceType.GpsEmbed,
                Summary = "Embedded GPS near Half Dome",
                Confidence = Confidence.VeryHigh,
                Attributes = new Dictionary<string, string>
                {
                    ["lat"] = "37.746",
                    ["lon"] = "-119.533"
                }
            });

            var allEvidence = board.All().Where(e => e.AnalysisSessionId == pipelineResult.Session.Id).ToList();
            var deep = new DeepAnalysisPass(new DeepAnalysisOptions
            {
                MinimumItemConfidence = 0.15,
                MinimumClusterConfidence = 0.25
            }, fusion).Run(pipelineResult.Session.Id, allEvidence);

            deep.Hypotheses.Should().NotBeEmpty("deep pass should form at least one cluster hypothesis");
            deep.Hypotheses[0].Confidence.Value.Should().BeGreaterThan(0.2);
            deep.Hypotheses[0].Reasoning.Should().NotBeNullOrEmpty();
            deep.Selection.AllConsidered.Should().NotBeEmpty();
            deep.Selection.AllConsidered.Should().Contain(c => c.Selected)
                .And.Subject.Should().Contain(c => c.Scored.Breakdown.Combined >= 0);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
