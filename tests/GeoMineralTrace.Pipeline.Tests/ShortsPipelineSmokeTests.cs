using FluentAssertions;
using GeoMineralTrace.Evidence.Store;
using GeoMineralTrace.Hypothesis.Fusion;
using GeoMineralTrace.Pipeline;
using GeoMineralTrace.Pipeline.Audio;
using GeoMineralTrace.Pipeline.Ingest;
using GeoMineralTrace.Pipeline.Keyframes;
using GeoMineralTrace.Pipeline.Ocr;
using GeoMineralTrace.Pipeline.Persistence;
using GeoMineralTrace.Pipeline.Vision;

namespace GeoMineralTrace.Pipeline.Tests;

public class ShortsPipelineSmokeTests
{
    [Fact]
    public async Task KeyframeExtractor_HandlesAv1ShortsClipQuickly()
    {
        var media = @"F:\GeoMineralTrace\testdata\shorts-00X2pwgYPZk\00X2pwgYPZk.mp4";
        if (!File.Exists(media))
            return; // local fixture only

        var outDir = Path.Combine(Path.GetTempPath(), "gmt-kf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var extractor = new FfmpegKeyframeExtractor();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var frames = await extractor.ExtractAsync(media, outDir, cancellationToken: cts.Token);
            sw.Stop();
            frames.Should().NotBeEmpty();
            frames.Should().OnlyContain(f => File.Exists(f.ImagePath));
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60));
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task AnalysisPipeline_CompletesShortsDownloadWithoutHanging()
    {
        var media = @"F:\GeoMineralTrace\testdata\shorts-00X2pwgYPZk\00X2pwgYPZk.mp4";
        if (!File.Exists(media))
            return;

        var board = new EvidenceBoardStore();
        var store = new AnalysisSessionStore();
        var pipeline = new AnalysisPipeline(
            board,
            store,
            new HypothesisFusionEngine(),
            keyframes: new FfmpegKeyframeExtractor(),
            ocr: new SidecarOcrEngine(),
            transcription: new SidecarTranscriptionEngine(),
            sceneTagger: new HeuristicSceneTagger());

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await pipeline.RunAsync([media], cancellationToken: cts.Token);
        sw.Stop();

        result.Evidence.Should().NotBeEmpty();
        result.Evidence.Should().Contain(e => e.Type == GeoMineralTrace.Core.Evidence.EvidenceType.Keyframe);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ShortsUrl_CreatesPlayerPageWithFixedPostMessage()
    {
        var url = "https://www.youtube.com/shorts/00X2pwgYPZk";
        var media = YouTubeMediaFetcher.TryCreateMediaRef(url);
        media.Should().NotBeNull();
        media!.MediaId.Should().Be("00X2pwgYPZk");
        media.EmbedHtml.Should().Contain("00X2pwgYPZk");
        media.EmbedHtml.Should().Contain("webview.postMessage(o)");
    }
}
