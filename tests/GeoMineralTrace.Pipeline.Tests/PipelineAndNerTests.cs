using FluentAssertions;
using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Evidence.Store;
using GeoMineralTrace.Hypothesis.Fusion;
using GeoMineralTrace.Pipeline;
using GeoMineralTrace.Pipeline.Audio;
using GeoMineralTrace.Pipeline.Ner;
using GeoMineralTrace.Pipeline.Persistence;
using GeoMineralTrace.Reporting;
using GeoMineralTrace.Rockhounding.Import;

namespace GeoMineralTrace.Pipeline.Tests;

public class PipelineAndNerTests
{
    [Fact]
    public void Ner_ExtractsMineralsAndHighways()
    {
        var ner = new PlaceAndMineralExtractor();
        var items = ner.ExtractFromText(Guid.NewGuid(), "clip.mp4",
            "We found obsidian near Maury Mountain off US-191 in Oregon.");
        items.Should().Contain(i => i.Type == EvidenceType.MineralNameMention);
        items.Should().Contain(i => i.Type == EvidenceType.PlaceNameMention || i.Type == EvidenceType.OcrText);
    }

    [Fact]
    public void SrtParser_ReadsSegments()
    {
        var srt = """
            1
            00:00:01,000 --> 00:00:04,000
            Looking for garnet

            2
            00:00:05,000 --> 00:00:08,000
            Near the creek
            """;
        var result = SidecarTranscriptionEngine.ParseSrt(srt);
        result.Segments.Should().HaveCount(2);
        result.FullText.Should().Contain("garnet");
    }

    [Fact]
    public async Task Pipeline_RunsOnImageWithSidecars()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gmt-pipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var img = Path.Combine(dir, "frame.jpg");
            await File.WriteAllBytesAsync(img, [0xFF, 0xD8, 0xFF, 0xD9]);
            await File.WriteAllTextAsync(img + ".ocr.txt", "US-95 WELCOME");
            await File.WriteAllTextAsync(Path.ChangeExtension(img, ".txt"), "obsidian collecting tip");
            await File.WriteAllTextAsync(Path.ChangeExtension(img, ".tags.txt"), "desert mine");

            var board = new EvidenceBoardStore();
            var store = new AnalysisSessionStore(Path.Combine(dir, "sessions"));
            var fusion = new HypothesisFusionEngine();
            var pipeline = new AnalysisPipeline(board, store, fusion);

            var result = await pipeline.RunAsync([img]);
            result.Evidence.Should().NotBeEmpty();
            result.Session.Status.Should().Be(AnalysisStatus.Completed);

            var exporter = new ResearchReportExporter();
            var paths = await exporter.ExportAsync(result, Path.Combine(dir, "reports"));
            File.Exists(paths.MarkdownPath).Should().BeTrue();
            (await File.ReadAllTextAsync(paths.MarkdownPath)).Should().Contain("Research Report");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CsvImporter_ParsesSample()
    {
        var csv = """
            name,state_code,county,latitude,longitude,minerals,land_type,access_status,difficulty,access_notes,sources
            Test Site,OR,Crook,44.1,-120.5,obsidian;agate,PublicBlm,Open,Beginner,notes,src1
            """;
        var list = new LocalityCsvImporter().Parse(csv);
        list.Should().ContainSingle();
        list[0].ReportedMinerals.Should().Contain("obsidian");
        list[0].SystemRating.Should().NotBeNull();
    }
}
