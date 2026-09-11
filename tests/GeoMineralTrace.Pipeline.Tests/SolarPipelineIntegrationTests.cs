using FluentAssertions;
using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Solar;
using GeoMineralTrace.Evidence.Store;
using GeoMineralTrace.Hypothesis.Fusion;
using GeoMineralTrace.Pipeline;
using GeoMineralTrace.Pipeline.Persistence;
using GeoMineralTrace.Solar;

namespace GeoMineralTrace.Pipeline.Tests;

public class SolarPipelineIntegrationTests
{
    [Fact]
    public async Task Pipeline_IncludesSolarLocus_WhenShadowEvidencePresent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gmt-solar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var sessionId = Guid.NewGuid();
            var calc = new SolarPositionCalculator();
            var denver = new GeoCoordinate(39.7392, -104.9903);
            var utc = new DateTimeOffset(2024, 6, 21, 19, 0, 0, TimeSpan.Zero);
            var truth = calc.Calculate(denver, utc);
            var ratio = Math.Tan(GeoCoordinate.DegreesToRadians(truth.ElevationDegrees));

            var img = Path.Combine(dir, "frame.jpg");
            await File.WriteAllBytesAsync(img, [0xFF, 0xD8, 0xFF, 0xD9]);
            await File.WriteAllTextAsync(img + ".ocr.txt", "US-95");
            await File.WriteAllTextAsync(Path.ChangeExtension(img, ".txt"), "rock collecting near denver");

            var board = new EvidenceBoardStore();
            board.Add(new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = sessionId,
                Type = EvidenceType.ShadowMeasurement,
                Summary = "Keyframe shadow",
                RawContent = $"{ratio:F6},1.0",
                SourceMediaPath = img,
                Attributes = new Dictionary<string, string>
                {
                    ["objectHeight"] = ratio.ToString("F6"),
                    ["shadowLength"] = "1.0"
                },
                Confidence = Confidence.High
            });

            var store = new AnalysisSessionStore(Path.Combine(dir, "sessions"));
            var fusion = new HypothesisFusionEngine();
            var pipeline = new AnalysisPipeline(board, store, fusion);

            var result = await pipeline.RunAsync([img], new AnalysisPipelineOptions
            {
                DisplayNameOverride = "Solar integration test"
            });

            result.Session.Status.Should().Be(AnalysisStatus.Completed);
            result.SolarLocus.Should().NotBeNull("pipeline must wire shadow evidence into fusion");
            result.SolarLocus!.MeasurementIds.Should().HaveCount(1);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
