using System.Text;
using System.Text.RegularExpressions;
using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Core.Progress;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Core.Solar;
using GeoMineralTrace.Evidence.Store;
using GeoMineralTrace.Hypothesis.Fusion;
using GeoMineralTrace.Pipeline.Abstractions;
using GeoMineralTrace.Pipeline.Audio;
using GeoMineralTrace.Pipeline.Diagnostics;
using GeoMineralTrace.Pipeline.Ingest;
using GeoMineralTrace.Pipeline.Keyframes;
using GeoMineralTrace.Pipeline.Metadata;
using GeoMineralTrace.Pipeline.Ner;
using GeoMineralTrace.Pipeline.Ocr;
using GeoMineralTrace.Pipeline.Persistence;
using GeoMineralTrace.Pipeline.Vision;
using GeoMineralTrace.Rockhounding.Storage;
using GeoMineralTrace.Solar;
using Microsoft.Extensions.Logging;

namespace GeoMineralTrace.Pipeline;

public sealed class AnalysisPipelineOptions
{
    public bool RunOcr { get; set; } = true;
    public bool RunTranscription { get; set; } = true;
    public bool RunSceneTags { get; set; } = true;
    public bool RunFusion { get; set; } = true;
    public bool CrossLinkMinerals { get; set; } = true;

    /// <summary>Optional display name (YouTube title, etc.) instead of filename.</summary>
    public string? DisplayNameOverride { get; set; }

    public string? SourceUrl { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceDescription { get; set; }
    public AnalysisSourceKind? SourceKind { get; set; }

    /// <summary>
    /// Optional pre-pipeline ingest notes from the UI (yt-dlp vs capture vs local).
    /// Diagnostics only — not used by analysis logic.
    /// </summary>
    public List<string> DiagnosticIngestNotes { get; } = [];
}

public sealed class AnalysisPipelineResult
{
    public required AnalysisSession Session { get; init; }
    public required IReadOnlyList<EvidenceItem> Evidence { get; init; }
    public required IReadOnlyList<LocationHypothesis> Hypotheses { get; init; }
    public SolarLocusResult? SolarLocus { get; init; }
    public IReadOnlyList<NearbyLocalityHit> NearbyLocalities { get; init; } = [];
}

public sealed record NearbyLocalityHit(
    Guid LocalityId,
    string Name,
    string StateCode,
    double? DistanceKm,
    string AccessStatus,
    double? Rating,
    IReadOnlyList<string> Minerals,
    double? Latitude = null,
    double? Longitude = null,
    string? SourceDataset = null,
    string? SourceVintage = null,
    bool HumanActivityDataMayBeOutdated = false,
    double? LegalClarity = null);

/// <summary>
/// End-to-end analysis orchestrator with progress reporting and cancellation.
/// </summary>
public sealed class AnalysisPipeline
{
    private readonly MediaMetadataExtractor _metadata = new();
    private readonly PlaceAndMineralExtractor _ner = new();
    private readonly IKeyframeExtractor _keyframes;
    private readonly IOcrEngine _ocr;
    private readonly ITranscriptionEngine _transcription;
    private readonly ISceneTagger _sceneTagger;
    private readonly HypothesisFusionEngine _fusion;
    private readonly AnalysisSessionStore _store;
    private readonly EvidenceBoardStore _evidenceBoard;
    private readonly LocalityStore? _localities;
    private readonly ILogger<AnalysisPipeline>? _logger;

    public AnalysisPipeline(
        EvidenceBoardStore evidenceBoard,
        AnalysisSessionStore store,
        HypothesisFusionEngine fusion,
        IKeyframeExtractor? keyframes = null,
        IOcrEngine? ocr = null,
        ITranscriptionEngine? transcription = null,
        ISceneTagger? sceneTagger = null,
        LocalityStore? localities = null,
        ILogger<AnalysisPipeline>? logger = null)
    {
        _evidenceBoard = evidenceBoard;
        _store = store;
        _fusion = fusion;
        _keyframes = keyframes ?? new FfmpegKeyframeExtractor();
        _ocr = ocr ?? new SidecarOcrEngine();
        _transcription = transcription ?? new SidecarTranscriptionEngine();
        _sceneTagger = sceneTagger ?? new HeuristicSceneTagger();
        _localities = localities;
        _logger = logger;
    }

    public async Task<AnalysisPipelineResult> RunAsync(
        IReadOnlyList<string> mediaPaths,
        AnalysisPipelineOptions? options = null,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AnalysisPipelineOptions();
        if (mediaPaths.Count == 0)
            throw new ArgumentException("At least one media path is required.", nameof(mediaPaths));

        foreach (var p in mediaPaths)
        {
            if (!File.Exists(p))
                throw new FileNotFoundException("Media not found.", p);
        }

        var displayName = !string.IsNullOrWhiteSpace(options.DisplayNameOverride)
            ? options.DisplayNameOverride!.Trim()
            : mediaPaths.Count == 1
                ? Path.GetFileName(mediaPaths[0])
                : $"Batch ({mediaPaths.Count} files)";

        var sourceKind = options.SourceKind
            ?? (mediaPaths.Count > 1
                ? AnalysisSourceKind.Batch
                : InferSourceKind(mediaPaths[0], options.SourceUrl));

        var session = new AnalysisSession
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName!,
            SourceMediaPaths = mediaPaths.ToList(),
            SourceUrl = options.SourceUrl,
            SourceKind = sourceKind,
            Status = AnalysisStatus.Running,
            CurrentStage = "starting",
            ProgressFraction = 0
        };

        using var diag = AnalysisDiagnosticLog.Begin(session.Id);
        diag.Section("SESSION");
        diag.KeyValue("New session ID generated", "YES");
        diag.KeyValue("Session ID", session.Id.ToString("N"));
        diag.KeyValue("Display name", session.DisplayName);
        diag.KeyValue("Source kind", session.SourceKind.ToString());
        diag.KeyValue("Source URL (options)", options.SourceUrl);
        diag.KeyValue("Source title (options)", options.SourceTitle);
        diag.KeyValue("Analysis started (UTC)", diag.StartedAtUtc.ToString("O"));
        diag.KeyValue("Analysis started (local)", DateTimeOffset.Now.ToString("O"));
        diag.KeyValue("Media path count", mediaPaths.Count.ToString());
        for (var mi = 0; mi < mediaPaths.Count; mi++)
            diag.Line($"  media[{mi}] as received: {mediaPaths[mi]}");

        diag.Section("INGEST");
        if (options.DiagnosticIngestNotes.Count == 0)
            diag.Line("UI ingest notes: (none provided — pipeline received paths only)");
        else
            foreach (var note in options.DiagnosticIngestNotes)
                diag.Line("UI ingest: " + note);

        diag.KeyValue("Inferred path",
            !string.IsNullOrWhiteSpace(options.SourceUrl)
                ? $"Remote/URL-backed ({session.SourceKind}) — pipeline operates on local file after download/capture"
                : $"Local file ({session.SourceKind})");

        foreach (var mediaPath in mediaPaths)
            diag.LogFileFingerprint("Working media", mediaPath);

        _logger?.LogInformation(
            "Created AnalysisSession {SessionId} display={DisplayName} files={FileCount} kind={Kind} url={Url} diag={Diag}",
            session.Id, session.DisplayName, mediaPaths.Count, session.SourceKind, session.SourceUrl ?? "(none)",
            diag.FilePath);
        System.Diagnostics.Debug.WriteLine(
            $"[AnalysisPipeline] NEW session={session.Id:N} name={session.DisplayName} files={mediaPaths.Count} kind={session.SourceKind} diag={diag.FilePath}");

        // Persist a Running case immediately so Cases/History can show it while work proceeds.
        await _store.SaveAsync(session, [], cancellationToken: cancellationToken).ConfigureAwait(false);

        var cts = session.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cts.Token;
        var allEvidence = new List<EvidenceItem>();
        var sessionDir = _store.GetSessionDirectory(session.Id);

        // New analysis owns the board — avoid leaking clues from a prior session.
        var boardBeforeClear = _evidenceBoard.All();
        diag.Line($"Evidence board before clear: {boardBeforeClear.Count} row(s)");
        if (boardBeforeClear.Count > 0)
        {
            var priorIds = boardBeforeClear.Select(e => e.AnalysisSessionId).Distinct().ToList();
            diag.Line($"  prior board session IDs: {string.Join(", ", priorIds.Select(id => id.ToString("N")))}");
        }

        _evidenceBoard.Clear();
        diag.Line("Evidence board cleared for new session.");

        try
        {
            var fileIndex = 0;
            foreach (var media in mediaPaths)
            {
                token.ThrowIfCancellationRequested();
                fileIndex++;
                var baseFrac = (fileIndex - 1) / (double)mediaPaths.Count;
                var span = 1.0 / mediaPaths.Count;

                diag.StageStart("METADATA");
                Report(progress, session, "metadata", baseFrac + 0.05 * span, $"Metadata: {Path.GetFileName(media)}");
                var meta = _metadata.Extract(session.Id, media);
                allEvidence.AddRange(meta);
                _evidenceBoard.AddRange(meta);
                LogStageCounts("metadata", media, meta.Count, allEvidence);
                diag.StageEnd("METADATA", $"{meta.Count} item(s)");
                if (meta.Count > 0)
                    diag.Sample("metadata", string.Join(" | ", meta.Select(m => m.Summary)));

                // Remote title/description sidecar + filename NER (YouTube often has no EXIF places).
                diag.StageStart("SOURCE-NER");
                var sourceLeads = await ExtractSourceLeadsAsync(
                    session.Id, media, options, token).ConfigureAwait(false);
                if (sourceLeads.Count > 0)
                {
                    allEvidence.AddRange(sourceLeads);
                    _evidenceBoard.AddRange(sourceLeads);
                    LogStageCounts("source-ner", media, sourceLeads.Count, allEvidence);
                    diag.StageEnd("SOURCE-NER", $"{sourceLeads.Count} item(s)");
                    diag.Sample("source-ner", string.Join(" | ", sourceLeads.Select(s => s.Summary)));
                }
                else
                {
                    diag.Skipped("SOURCE-NER", "No title/description/URL/filename leads extracted");
                }

                diag.StageStart("KEYFRAMES");
                Report(progress, session, "keyframes", baseFrac + 0.2 * span, "Keyframe extraction");
                var kfDir = Path.Combine(sessionDir, "keyframes", Sanitize(Path.GetFileNameWithoutExtension(media)));
                var frames = await _keyframes.ExtractAsync(media, kfDir, null, token).ConfigureAwait(false);
                var kfMethods = frames.Select(f => f.ExtractionMethod).Distinct().ToList();
                foreach (var kf in frames)
                {
                    var ev = new EvidenceItem
                    {
                        Id = Guid.NewGuid(),
                        AnalysisSessionId = session.Id,
                        Type = EvidenceType.Keyframe,
                        Summary = $"Keyframe @{kf.Timestamp:hh\\:mm\\:ss} (q={kf.QualityScore:F2}, {kf.ExtractionMethod})",
                        SourceMediaPath = media,
                        MediaTimestamp = kf.Timestamp,
                        FrameIndex = kf.FrameIndex,
                        PreviewAssetPath = kf.ImagePath,
                        Confidence = Confidence.From(Math.Clamp(kf.QualityScore, 0.2, 0.95)),
                        Notes = kf.ExtractionMethod == "plan-only-no-ffmpeg"
                            ? "Install ffmpeg for bitmap keyframes."
                            : null
                    };
                    allEvidence.Add(ev);
                    _evidenceBoard.Add(ev);
                }

                if (frames.Count == 0)
                    diag.Skipped("KEYFRAMES", "0 frames extracted");
                else if (kfMethods.Any(m => m.Contains("plan-only", StringComparison.OrdinalIgnoreCase) ||
                                            m.Contains("no-ffmpeg", StringComparison.OrdinalIgnoreCase)))
                    diag.Skipped("KEYFRAMES", $"FALLBACK method(s): {string.Join(", ", kfMethods)} — {frames.Count} plan/stub frame(s)");
                else
                    diag.StageEnd("KEYFRAMES", $"{frames.Count} extracted (methods: {string.Join(", ", kfMethods)})");

                var ocrTextCount = 0;
                var ocrSample = new StringBuilder();
                if (options.RunOcr)
                {
                    diag.StageStart("OCR");
                    Report(progress, session, "ocr", baseFrac + 0.45 * span, $"OCR ({_ocr.EngineName})");
                    diag.KeyValue("OCR engine", _ocr.EngineName);
                    foreach (var kf in frames.Where(f => f.ImagePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                                                        || f.ImagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                                                        || f.ImagePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                    {
                        token.ThrowIfCancellationRequested();
                        var ocrs = await _ocr.RecognizeAsync(kf.ImagePath, token).ConfigureAwait(false);
                        foreach (var o in ocrs)
                        {
                            ocrTextCount++;
                            if (ocrSample.Length < 120)
                                ocrSample.Append(o.Text).Append(' ');

                            var ev = new EvidenceItem
                            {
                                Id = Guid.NewGuid(),
                                AnalysisSessionId = session.Id,
                                Type = EvidenceType.OcrText,
                                Summary = Truncate(o.Text, 120),
                                RawContent = o.Text,
                                SourceMediaPath = media,
                                MediaTimestamp = kf.Timestamp,
                                FrameIndex = kf.FrameIndex,
                                PreviewAssetPath = kf.ImagePath,
                                Confidence = o.Confidence,
                                DetectedLanguage = o.DetectedLanguage,
                                Notes = o.Notes
                            };
                            allEvidence.Add(ev);
                            _evidenceBoard.Add(ev);
                            allEvidence.AddRange(_ner.ExtractFromText(session.Id, media, o.Text, kf.Timestamp, o.DetectedLanguage));
                        }
                    }

                    if (ocrTextCount == 0)
                    {
                        diag.Skipped("OCR",
                            $"Engine={_ocr.EngineName}; 0 text regions. " +
                            "App DI uses Windows.Media.Ocr then Sidecar — empty usually means no readable text in frames " +
                            "(or this process only registered Sidecar). Sidecar path expects frame.ocr.txt / .txt.");
                    }
                    else
                    {
                        diag.StageEnd("OCR", $"{ocrTextCount} text region(s)");
                        diag.Sample("OCR", ocrSample.ToString());
                    }
                }
                else
                {
                    diag.Skipped("OCR", "options.RunOcr=false");
                }

                if (options.RunTranscription)
                {
                    diag.StageStart("AUDIO");
                    Report(progress, session, "audio", baseFrac + 0.65 * span, $"Transcription ({_transcription.EngineName})");
                    diag.KeyValue("Transcription engine", _transcription.EngineName);
                    var transcript = await _transcription.TranscribeAsync(media, token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(transcript.FullText))
                    {
                        var wordCount = transcript.FullText
                            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
                        var audioEv = new EvidenceItem
                        {
                            Id = Guid.NewGuid(),
                            AnalysisSessionId = session.Id,
                            Type = EvidenceType.AudioTranscript,
                            Summary = Truncate(transcript.FullText, 160),
                            RawContent = transcript.FullText,
                            SourceMediaPath = media,
                            Confidence = transcript.Confidence,
                            DetectedLanguage = transcript.DetectedLanguage,
                            Notes = transcript.Notes
                        };
                        allEvidence.Add(audioEv);
                        _evidenceBoard.Add(audioEv);

                        foreach (var seg in transcript.Segments)
                        {
                            var ner = _ner.ExtractFromText(session.Id, media, seg.Text, seg.Start, transcript.DetectedLanguage);
                            allEvidence.AddRange(ner);
                            _evidenceBoard.AddRange(ner);
                        }

                        // Always NER FullText once — some engines return text with empty Segments.
                        var fullNer = _ner.ExtractFromText(
                            session.Id, media, transcript.FullText, timestamp: null, transcript.DetectedLanguage);
                        allEvidence.AddRange(fullNer);
                        _evidenceBoard.AddRange(fullNer);
                        LogStageCounts("audio", media, 1 + fullNer.Count, allEvidence);

                        diag.StageEnd("AUDIO", $"transcript {wordCount} words, segments={transcript.Segments.Count}, ner+={fullNer.Count}");
                        diag.Sample("transcript", transcript.FullText);
                        if (!string.IsNullOrWhiteSpace(transcript.Notes))
                            diag.Line($"  transcript notes: {transcript.Notes}");

                        // Background sound notes placeholder when transcript notes mention ambience
                        if (transcript.Notes?.Contains("ambience", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            allEvidence.Add(new EvidenceItem
                            {
                                Id = Guid.NewGuid(),
                                AnalysisSessionId = session.Id,
                                Type = EvidenceType.BackgroundSound,
                                Summary = "Background ambience noted in transcript metadata",
                                SourceMediaPath = media,
                                Confidence = Confidence.Low,
                                Notes = transcript.Notes
                            });
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(transcript.Notes))
                    {
                        allEvidence.Add(new EvidenceItem
                        {
                            Id = Guid.NewGuid(),
                            AnalysisSessionId = session.Id,
                            Type = EvidenceType.AudioTranscript,
                            Summary = "No transcript available",
                            SourceMediaPath = media,
                            Confidence = Confidence.None,
                            Notes = transcript.Notes
                        });
                        var whisper = WhisperCliTranscriptionEngine.ResolveWhisper();
                        diag.Skipped("AUDIO",
                            $"Engine={_transcription.EngineName}; empty transcript — {transcript.Notes} | " +
                            $"Whisper CLI on PATH: {(whisper is null ? "NO — install openai-whisper or whisper.cpp, or place media.srt" : $"{whisper.Kind} @ {whisper.Path}")}");
                    }
                    else
                    {
                        var whisper = WhisperCliTranscriptionEngine.ResolveWhisper();
                        diag.Skipped("AUDIO",
                            $"Engine={_transcription.EngineName}; empty transcript and no notes | " +
                            $"Whisper CLI: {(whisper is null ? "NOT FOUND" : $"{whisper.Kind} available")}");
                    }
                }
                else
                {
                    diag.Skipped("AUDIO", "options.RunTranscription=false");
                }

                if (options.RunSceneTags)
                {
                    diag.StageStart("VISION");
                    Report(progress, session, "vision", baseFrac + 0.8 * span, "Scene tags");
                    // Tag from media path / title sidecar once (keyframes alone rarely encode place keywords).
                    var mediaContextTags = await _sceneTagger.TagAsync(media, token).ConfigureAwait(false);
                    foreach (var tag in mediaContextTags)
                    {
                        var ev = new EvidenceItem
                        {
                            Id = Guid.NewGuid(),
                            AnalysisSessionId = session.Id,
                            Type = tag.Type,
                            Summary = tag.Label,
                            SourceMediaPath = media,
                            Confidence = tag.Confidence,
                            Notes = tag.Notes
                        };
                        allEvidence.Add(ev);
                        _evidenceBoard.Add(ev);
                    }

                    var kfTagCount = 0;
                    foreach (var kf in frames.Take(12))
                    {
                        var tags = await _sceneTagger.TagAsync(kf.ImagePath, token).ConfigureAwait(false);
                        kfTagCount += tags.Count;
                        foreach (var tag in tags)
                        {
                            var ev = new EvidenceItem
                            {
                                Id = Guid.NewGuid(),
                                AnalysisSessionId = session.Id,
                                Type = tag.Type,
                                Summary = tag.Label,
                                SourceMediaPath = media,
                                MediaTimestamp = kf.Timestamp,
                                FrameIndex = kf.FrameIndex,
                                PreviewAssetPath = kf.ImagePath,
                                Confidence = tag.Confidence,
                                Notes = tag.Notes
                            };
                            allEvidence.Add(ev);
                            _evidenceBoard.Add(ev);
                        }
                    }

                    LogStageCounts("vision", media,
                        mediaContextTags.Count, allEvidence);

                    var visionTotal = mediaContextTags.Count + kfTagCount;
                    if (visionTotal == 0)
                        diag.Skipped("VISION", "HeuristicSceneTagger produced 0 tags (no keyword hits in path/sidecar/keyframes)");
                    else
                    {
                        diag.StageEnd("VISION", $"{visionTotal} tag(s) (media-context={mediaContextTags.Count}, keyframe={kfTagCount})");
                        diag.Sample("vision", string.Join(", ", mediaContextTags.Select(t => t.Label).Take(8)));
                    }
                }
                else
                {
                    diag.Skipped("VISION", "options.RunSceneTags=false");
                }
            }

            var mediaPathSet = new HashSet<string>(mediaPaths, StringComparer.OrdinalIgnoreCase);
            var preservedShadow = boardBeforeClear
                .Where(e => e.Type == EvidenceType.ShadowMeasurement && !e.IsRejected)
                .Where(e => e.SourceMediaPath is { } sp && mediaPathSet.Contains(sp))
                .Select(e => RebindShadowEvidence(e, session.Id))
                .ToList();
            if (preservedShadow.Count > 0)
            {
                allEvidence.AddRange(preservedShadow);
                _evidenceBoard.AddRange(preservedShadow);
                diag.Line($"Preserved {preservedShadow.Count} shadow measurement(s) from prior board for matching media.");
            }

            diag.Section("SOLAR");
            SolarLocusResult? solarLocus = null;
            if (options.RunFusion)
            {
                solarLocus = SolarLocusFromEvidence.TryComputeLocus(
                    session.Id,
                    allEvidence,
                    defaultObservationUtc: session.CreatedAtUtc);
                if (solarLocus is null)
                {
                    diag.Skipped("SOLAR",
                        "No shadow-measurement evidence on board — locus computed when Solar page saves measurements");
                }
                else
                {
                    diag.StageEnd("SOLAR",
                        $"Locus from {solarLocus.MeasurementIds.Count} shadow measurement(s); " +
                        $"peak cell lat={solarLocus.PeakProbabilityCell?.LatitudeDegrees:F2} " +
                        $"lon={solarLocus.PeakProbabilityCell?.LongitudeDegrees:F2}");
                }
            }
            else
            {
                diag.Skipped("SOLAR", "options.RunFusion=false");
            }

            // Ensure NER evidence is on the board
            foreach (var e in allEvidence)
                _evidenceBoard.Add(e);

            var diskSessionIds = _store.ListSessionIds();
            var diskEvidenceTotal = 0;
            foreach (var sid in diskSessionIds)
            {
                try
                {
                    var doc = await _store.LoadAsync(sid, token).ConfigureAwait(false);
                    diskEvidenceTotal += doc?.Evidence.Count ?? 0;
                }
                catch
                {
                    // ignore corrupt
                }
            }

            diag.LogEvidenceInventory(allEvidence, _evidenceBoard.All(), session.Id);
            diag.KeyValue("Disk sessions (folders)", diskSessionIds.Count.ToString());
            diag.KeyValue("Disk evidence rows (all sessions)", diskEvidenceTotal.ToString());

            IReadOnlyList<LocationHypothesis> hypotheses = [];
            if (options.RunFusion)
            {
                Report(progress, session, "fusion", 0.92, "Hypothesis fusion");
                var sessionEvidenceIds = allEvidence.Select(e => e.AnalysisSessionId).Distinct().ToList();
                _logger?.LogInformation(
                    "Fusion input session={SessionId} evidence={EvidenceCount} sessionIds=[{SessionIds}] solar={Solar}",
                    session.Id, allEvidence.Count, string.Join(",", sessionEvidenceIds),
                    solarLocus is null ? "none" : "present");
                System.Diagnostics.Debug.WriteLine(
                    $"[AnalysisPipeline] Fusion session={session.Id:N} evidence={allEvidence.Count} " +
                    $"distinctEvidenceSessions={sessionEvidenceIds.Count} solar={(solarLocus is null ? "none" : "present")}");
                diag.Line($"[{DateTimeOffset.Now:HH:mm:ss.fff}] Calling HypothesisFusionEngine.Fuse (no fusion cache in this build)");
                hypotheses = _fusion.Fuse(session.Id, allEvidence, solarLocus);
                diag.LogFusion(
                    invoked: true,
                    optionsWouldSkip: false,
                    cacheStatus: "MISS (no fusion cache exists — Fuse always runs)",
                    hypotheses);
                var fuseable = CountFuseable(allEvidence);
                diag.KeyValue("Fuseable clue breakdown", fuseable.Summary);
                _logger?.LogInformation(
                    "Fusion output session={SessionId} hypotheses={HypCount} fuseable={Fuseable} ({Breakdown})",
                    session.Id, hypotheses.Count, fuseable.Total, fuseable.Summary);
                System.Diagnostics.Debug.WriteLine(
                    $"[AnalysisPipeline] Fusion output session={session.Id:N} hypotheses={hypotheses.Count} " +
                    $"fuseable={fuseable.Total} ({fuseable.Summary})");
                if (hypotheses.Count == 0)
                {
                    session.FusionEmptyReason = fuseable.Total == 0
                        ? "No place, mineral, GPS, road, or scene-region leads were found. " +
                          "OCR/ASR may be empty (install Whisper for speech; ffmpeg for OCR bitmaps). " +
                          "YouTube title/description NER requires a .gmt-source.json sidecar from download."
                        : $"Had {fuseable.Total} location-relevant clue(s) ({fuseable.Summary}) but fusion formed no ranked hypothesis.";
                    diag.KeyValue("FusionEmptyReason (set)", session.FusionEmptyReason);
                }
                else
                {
                    session.FusionEmptyReason = null;
                    System.Diagnostics.Debug.WriteLine(
                        $"[AnalysisPipeline] Top hypothesis: {hypotheses[0].Label} conf={hypotheses[0].Confidence.Value:F2}");
                }
            }
            else
            {
                diag.LogFusion(
                    invoked: false,
                    optionsWouldSkip: true,
                    cacheStatus: "N/A — fusion skipped by options",
                    hypotheses);
            }

            var nearby = new List<NearbyLocalityHit>();
            if (options.CrossLinkMinerals && _localities is not null)
            {
                Report(progress, session, "rockhounding", 0.96, "Mineral / locality cross-link");
                nearby.AddRange(await CrossLinkAsync(allEvidence, hypotheses, token).ConfigureAwait(false));
                diag.Line($"Rockhounding cross-link: {nearby.Count} nearby locality hit(s)");
            }

            session.Status = AnalysisStatus.Completed;
            session.ProgressFraction = 1;
            session.CurrentStage = "done";
            session.CompletedAtUtc = DateTimeOffset.UtcNow;
            session.EvidenceCount = allEvidence.Count;
            session.HypothesisCount = hypotheses.Count;
            Report(progress, session, "done", 1,
                hypotheses.Count > 0
                    ? $"Analysis complete — {hypotheses.Count} hypothesis(es)"
                    : "Analysis complete — no location hypotheses");

            await _store.SaveAsync(session, allEvidence, hypotheses, solarLocus, cancellationToken: token)
                .ConfigureAwait(false);

            diag.Section("PIPELINE COMPLETE");
            diag.KeyValue("Status", session.Status.ToString());
            diag.KeyValue("Diagnostic log path", diag.FilePath);
            diag.KeyValue("Awaiting UI handoff", "ApplyPipelineResult / HypothesesPage will append below");

            return new AnalysisPipelineResult
            {
                Session = session,
                Evidence = allEvidence,
                Hypotheses = hypotheses,
                SolarLocus = solarLocus,
                NearbyLocalities = nearby
            };
        }
        catch (OperationCanceledException)
        {
            diag.Section("CANCELLED");
            diag.Line("Analysis cancelled by user.");
            session.Status = AnalysisStatus.Cancelled;
            session.CurrentStage = "cancelled";
            session.ErrorMessage = "Cancelled by user.";
            session.EvidenceCount = allEvidence.Count;
            await _store.SaveAsync(session, allEvidence, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            diag.Section("FAILED");
            diag.Line($"Exception: {ex.GetType().Name}: {ex.Message}");
            diag.Line(ex.StackTrace ?? "(no stack)");
            _logger?.LogError(ex, "Analysis failed for session {SessionId}", session.Id);
            session.Status = AnalysisStatus.Failed;
            session.ErrorMessage = ex.Message;
            session.CurrentStage = "failed";
            session.EvidenceCount = allEvidence.Count;
            await _store.SaveAsync(session, allEvidence, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task<List<NearbyLocalityHit>> CrossLinkAsync(
        List<EvidenceItem> evidence,
        IReadOnlyList<LocationHypothesis> hypotheses,
        CancellationToken cancellationToken)
    {
        var hits = new List<NearbyLocalityHit>();
        if (_localities is null) return hits;

        var minerals = evidence
            .Where(e => e.Type == EvidenceType.MineralNameMention && !e.IsRejected)
            .Select(e => e.RawContent ?? e.Summary)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var mineral in minerals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mineralToken = mineral.Contains(':') ? mineral.Split(':').Last().Trim() : mineral.Trim();
            var found = await _localities.SearchByMineralAsync(mineralToken, cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var loc in found.Take(10))
            {
                hits.Add(ToNearbyHit(loc, distanceKm: null));
            }
        }

        foreach (var hyp in hypotheses.Where(h => h.Center is not null).Take(5))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var near = await _localities.FindNearAsync(hyp.Center!.Value, radiusKm: 75, cancellationToken)
                .ConfigureAwait(false);
            foreach (var loc in near.Take(8))
            {
                var dist = loc.Coordinates is { } c
                    ? LocalityStore.HaversineKm(hyp.Center.Value, c)
                    : (double?)null;
                hits.Add(ToNearbyHit(loc, dist));
            }
        }

        return hits
            .GroupBy(h => h.LocalityId)
            .Select(g => g.First())
            .ToList();
    }

    private static NearbyLocalityHit ToNearbyHit(Locality loc, double? distanceKm) =>
        new(
            loc.Id,
            loc.Name,
            loc.StateCode,
            distanceKm,
            loc.AccessStatus.ToString(),
            loc.SystemRating?.Overall,
            loc.ReportedMinerals,
            loc.Coordinates?.LatitudeDegrees,
            loc.Coordinates?.LongitudeDegrees,
            loc.SourceDataset,
            loc.SourceVintage,
            loc.HumanActivityDataMayBeOutdated,
            loc.SystemRating?.LegalClarity);

    private static void Report(
        IProgress<ProgressReport>? progress,
        AnalysisSession session,
        string stage,
        double fraction,
        string message)
    {
        session.CurrentStage = stage;
        session.ProgressFraction = fraction;
        progress?.Report(ProgressReport.Of(stage, fraction, message, session.Id, session.DisplayName));
    }

    private void LogStageCounts(string stage, string media, int added, List<EvidenceItem> all)
    {
        var byType = string.Join(", ",
            all.GroupBy(e => e.Type).OrderBy(g => g.Key.ToString()).Select(g => $"{g.Key}={g.Count()}"));
        _logger?.LogInformation(
            "Stage {Stage} media={Media} added={Added} totalEvidence={Total} [{Breakdown}]",
            stage, Path.GetFileName(media), added, all.Count, byType);
        System.Diagnostics.Debug.WriteLine(
            $"[AnalysisPipeline] stage={stage} media={Path.GetFileName(media)} +{added} total={all.Count} [{byType}]");
    }

    private async Task<List<EvidenceItem>> ExtractSourceLeadsAsync(
        Guid sessionId,
        string mediaPath,
        AnalysisPipelineOptions options,
        CancellationToken cancellationToken)
    {
        var items = new List<EvidenceItem>();
        var sidecar = await MediaSourceSidecar.TryReadAsync(mediaPath, cancellationToken).ConfigureAwait(false);

        var title = options.SourceTitle ?? sidecar?.Title;
        var description = options.SourceDescription ?? sidecar?.Description;
        var location = sidecar?.LocationLabel;
        var tags = sidecar?.Tags;

        // IMPORTANT: do NOT feed raw YouTube descriptions (related-video link farms) or the
        // watch URL into NER — that caused identical "Place-like phrase" spam across videos.
        var nerCorpus = RemoteDescriptionSanitizer.BuildNerCorpus(title, description, tags, location);
        if (!string.IsNullOrWhiteSpace(nerCorpus))
        {
            items.Add(new EvidenceItem
            {
                Id = Guid.NewGuid(),
                AnalysisSessionId = sessionId,
                Type = EvidenceType.Metadata,
                Summary = Truncate($"Source title/desc: {title ?? Path.GetFileName(mediaPath)}", 160),
                RawContent = nerCorpus,
                SourceMediaPath = mediaPath,
                Confidence = Confidence.High,
                Notes =
                    "SOURCE-NER corpus: this video's title + sanitized description + tags/location only " +
                    "(related-video / promo links stripped)."
            });
            items.AddRange(_ner.ExtractFromText(sessionId, mediaPath, nerCorpus));
        }

        // Filename often encodes place/mineral for rockhounding Shorts (e.g. Maury_Mountain_obsidian).
        var fileStem = Path.GetFileNameWithoutExtension(mediaPath).Replace('_', ' ').Replace('-', ' ');
        // Skip opaque YouTube/Instagram ids (typically 11-char video ids) — not place names.
        if (fileStem.Length >= 4 && !Regex.IsMatch(fileStem, @"^[A-Za-z0-9_-]{10,15}$"))
        {
            items.AddRange(_ner.ExtractFromText(sessionId, mediaPath, fileStem));
        }

        return items
            .GroupBy(i => (i.Type, (i.RawContent ?? i.Summary).ToLowerInvariant()))
            .Select(g => g.First())
            .ToList();
    }

    private static AnalysisSourceKind InferSourceKind(string mediaPath, string? sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            var p = YouTubeMediaFetcher.GetPlatform(sourceUrl);
            if (p == RemoteMediaPlatform.YouTube) return AnalysisSourceKind.YouTube;
            if (p == RemoteMediaPlatform.Instagram) return AnalysisSourceKind.Instagram;
        }

        if (mediaPath.Contains("captures", StringComparison.OrdinalIgnoreCase) ||
            mediaPath.Contains("clips", StringComparison.OrdinalIgnoreCase))
            return AnalysisSourceKind.ScreenCapture;

        return AnalysisSourceKind.LocalFile;
    }

    private static (int Total, string Summary) CountFuseable(IReadOnlyList<EvidenceItem> evidence)
    {
        var active = evidence.Where(e => !e.IsRejected && e.EffectiveWeight > 0).ToList();
        var place = active.Count(e => e.Type == EvidenceType.PlaceNameMention);
        var mineral = active.Count(e => e.Type == EvidenceType.MineralNameMention);
        var gps = active.Count(e => e.Type == EvidenceType.GpsEmbed);
        var road = active.Count(e =>
            e.Type == EvidenceType.OcrText &&
            (e.RawContent?.Contains("US", StringComparison.OrdinalIgnoreCase) == true ||
             e.Summary.Contains("Road", StringComparison.OrdinalIgnoreCase) ||
             e.Summary.Contains("Highway", StringComparison.OrdinalIgnoreCase)));
        var scene = active.Count(e => e.Type is EvidenceType.Terrain or EvidenceType.Vegetation
            or EvidenceType.MiningCue or EvidenceType.SoilRock);
        var total = place + mineral + gps + road + scene;
        return (total, $"place={place}, mineral={mineral}, gps={gps}, road={road}, scene={scene}");
    }

    private static EvidenceItem RebindShadowEvidence(EvidenceItem source, Guid sessionId) =>
        new()
        {
            Id = source.Id,
            AnalysisSessionId = sessionId,
            Type = source.Type,
            Summary = source.Summary,
            RawContent = source.RawContent,
            SourceMediaPath = source.SourceMediaPath,
            PreviewAssetPath = source.PreviewAssetPath,
            MediaTimestamp = source.MediaTimestamp,
            FrameIndex = source.FrameIndex,
            Confidence = source.Confidence,
            Notes = source.Notes,
            Attributes = source.Attributes is null
                ? null
                : new Dictionary<string, string>(source.Attributes)
        };

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
