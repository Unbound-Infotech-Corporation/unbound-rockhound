using System.Collections.Concurrent;
using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Core.DeepAnalysis;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Core.Progress;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Core.Solar;
using GeoMineralTrace.Evidence.Store;
using GeoMineralTrace.Hypothesis.Fusion;
using GeoMineralTrace.Pipeline;
using GeoMineralTrace.Pipeline.Diagnostics;
using GeoMineralTrace.Pipeline.Persistence;
using GeoMineralTrace.Rockhounding.Storage;
using GeoMineralTrace.Solar;

namespace GeoMineralTrace.Infrastructure.Services;

/// <summary>
/// Shared analysis state between Analyze, Evidence Board, Solar, Map, Hypotheses, and Cases pages.
/// The currently viewed case is always <see cref="CurrentSessionId"/> / <see cref="LastPipelineResult"/>.
/// </summary>
public sealed class AnalysisSessionContext
{
    private readonly EvidenceBoardStore _board;
    private readonly HypothesisFusionEngine _fusion;
    private readonly LocalityStore _localities;
    private readonly AnalysisSessionStore _sessions;
    private readonly AnalysisHistoryStore _history;
    private readonly DeepAnalysisOptions _deepOptions;
    private readonly ConcurrentDictionary<Guid, LiveRunProgress> _liveRuns = new();

    public AnalysisSessionContext(
        EvidenceBoardStore board,
        HypothesisFusionEngine fusion,
        LocalityStore localities,
        AnalysisSessionStore sessions,
        AnalysisHistoryStore history,
        DeepAnalysisOptions? deepOptions = null)
    {
        _board = board;
        _fusion = fusion;
        _localities = localities;
        _sessions = sessions;
        _history = history;
        _deepOptions = deepOptions ?? new DeepAnalysisOptions();
    }

    public Guid? CurrentSessionId { get; set; }
    public EvidenceItem? PendingKeyframe { get; set; }
    public SolarLocusResult? LastSolarLocus { get; set; }
    public AnalysisPipelineResult? LastPipelineResult { get; set; }
    public List<ShadowMeasurement> Trajectory { get; } = [];

    /// <summary>Raised when the actively viewed case changes (open, apply, clear).</summary>
    public event EventHandler? ActiveCaseChanged;

    /// <summary>Raised when a live run reports progress (Cases list can refresh).</summary>
    public event EventHandler? LiveProgressChanged;

    /// <summary>
    /// When false, Evidence Board must not silently rehydrate the latest on-disk session
    /// (e.g. after Clear or BeginNewAnalysis). Explicit Load Session / successful Apply turn it back on.
    /// </summary>
    public bool AllowDiskHydrate { get; private set; } = true;

    public string ActiveCaseCaption
    {
        get
        {
            if (LastPipelineResult?.Session is { } s)
            {
                var when = s.CreatedAtUtc.ToLocalTime().ToString("g");
                return $"{s.DisplayName} · {when} · {s.Status}";
            }

            if (CurrentSessionId is { } id)
                return $"Case {id.ToString("N")[..8]}…";

            return "No case open";
        }
    }

    public void ReportLiveProgress(Guid sessionId, ProgressReport report, string displayName)
    {
        _liveRuns[sessionId] = new LiveRunProgress(
            sessionId, displayName, report.Stage, report.FractionComplete, report.Message, DateTimeOffset.UtcNow);
        LiveProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearLiveProgress(Guid sessionId)
    {
        _liveRuns.TryRemove(sessionId, out _);
        LiveProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyDictionary<Guid, LiveRunProgress> LiveRuns => _liveRuns;

    public void ApplyPipelineResult(AnalysisPipelineResult result)
    {
        // Replace prior session state entirely — do not retain solar/trajectory from an older video.
        LastPipelineResult = result;
        CurrentSessionId = result.Session.Id;
        LastSolarLocus = result.SolarLocus;
        Trajectory.Clear();
        PendingKeyframe = null;
        AllowDiskHydrate = true;
        ClearLiveProgress(result.Session.Id);

        // Keep the in-memory board aligned with this pipeline result (scoped to this session only).
        _board.Clear();
        _board.AddRange(result.Evidence);

        System.Diagnostics.Debug.WriteLine(
            $"[AnalysisSession] ApplyPipelineResult session={result.Session.Id:N} " +
            $"evidence={result.Evidence.Count} hypotheses={result.Hypotheses.Count} " +
            $"name={result.Session.DisplayName}");

        AnalysisDiagnosticLog.AppendSection(result.Session.Id, "UI HANDOFF — ApplyPipelineResult",
        [
            $"[{DateTimeOffset.Now:HH:mm:ss.fff}] AnalysisSessionContext received pipeline result",
            $"Bound CurrentSessionId:     {result.Session.Id:N}",
            $"Evidence count applied:     {result.Evidence.Count}",
            $"Hypothesis count applied:   {result.Hypotheses.Count}",
            $"ActiveCaseCaption:          {ActiveCaseCaption}",
            $"Raising ActiveCaseChanged:  YES"
        ]);

        ActiveCaseChanged?.Invoke(this, EventArgs.Empty);

        if (result.Session.Status == AnalysisStatus.Completed)
            _ = _history.RecordCompletedAnalysisAsync(result);
    }

    public void ClearPendingKeyframe() => PendingKeyframe = null;

    public void Reset()
    {
        _board.Clear();
        CurrentSessionId = null;
        PendingKeyframe = null;
        LastSolarLocus = null;
        LastPipelineResult = null;
        Trajectory.Clear();
        // User (or new-run prep) cleared live state — do not revive prior disk session until Load/Apply.
        AllowDiskHydrate = false;
        ActiveCaseChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Prepare a clean board for a synthetic / sample fuse without reviving disk sessions.
    /// </summary>
    public void BeginSyntheticBoard(Guid sessionId)
    {
        _board.Clear();
        CurrentSessionId = sessionId;
        PendingKeyframe = null;
        LastSolarLocus = null;
        LastPipelineResult = null;
        Trajectory.Clear();
        AllowDiskHydrate = false;
        ActiveCaseChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Drop prior Analyze/Evidence/Hypotheses state before starting a new media analysis.
    /// </summary>
    public void BeginNewAnalysis(string reason)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[AnalysisSession] BeginNewAnalysis reason={reason} " +
            $"priorSession={CurrentSessionId?.ToString("N") ?? "(none)"}");
        Reset();
    }

    public async Task OpenCaseAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var doc = await _sessions.LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (doc is null)
            throw new InvalidOperationException("Case not found on disk.");

        await HydrateFromDocumentAsync(doc, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCaseAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await _sessions.DeleteAsync(sessionId, cancellationToken).ConfigureAwait(false);
        ClearLiveProgress(sessionId);
        if (CurrentSessionId == sessionId)
            Reset();
    }

    public Task<IReadOnlyList<AnalysisCaseSummary>> ListCasesAsync(
        CancellationToken cancellationToken = default) =>
        _sessions.ListSummariesAsync(cancellationToken);

    public DeepAnalysisOptions DeepAnalysisOptions => _deepOptions;

    public async Task<DeepAnalysisResult> RunDeepAnalysisAsync(
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = CurrentSessionId
                        ?? LastPipelineResult?.Session.Id
                        ?? throw new InvalidOperationException("No analysis session open — run analysis first.");

        var boardAll = _board.All();
        var evidence = boardAll.Where(e => e.AnalysisSessionId == sessionId).ToList();
        if (evidence.Count == 0 && LastPipelineResult?.Evidence is { Count: > 0 } fromResult)
            evidence = fromResult.Where(e => e.AnalysisSessionId == sessionId || e.AnalysisSessionId == Guid.Empty).ToList();

        if (evidence.Count == 0)
            throw new InvalidOperationException("No evidence on the board for Deep Analysis.");

        Report(progress, sessionId, "deep-score", 0.15, "Scoring evidence");
        cancellationToken.ThrowIfCancellationRequested();

        if (LastSolarLocus is null)
        {
            var defaultObs = LastPipelineResult?.Session.CreatedAtUtc ?? DateTimeOffset.UtcNow;
            LastSolarLocus = SolarLocusFromEvidence.TryComputeLocus(sessionId, evidence, defaultObservationUtc: defaultObs);
        }

        Report(progress, sessionId, "deep-select", 0.45, "Selecting corroborating clusters");
        var pass = new DeepAnalysisPass(_deepOptions, _fusion);
        var deep = await Task.Run(
            () => pass.Run(sessionId, evidence, LastSolarLocus),
            cancellationToken).ConfigureAwait(false);

        Report(progress, sessionId, "deep-fuse", 0.75, "Generating ranked hypotheses");
        cancellationToken.ThrowIfCancellationRequested();

        // Sync annotated evidence onto the board (same instances mutated by ApplyAnnotations).
        foreach (var item in evidence)
            _board.Add(item);

        var nearby = await CrossLinkQuickAsync(evidence, deep.Hypotheses, cancellationToken)
            .ConfigureAwait(false);

        var session = LastPipelineResult?.Session;
        if (session is null || session.Id != sessionId)
        {
            session = new AnalysisSession
            {
                Id = sessionId,
                DisplayName = LastPipelineResult?.Session.DisplayName ?? "Deep analysis session",
                Status = AnalysisStatus.Completed,
                SourceMediaPaths = LastPipelineResult?.Session.SourceMediaPaths.ToList() ?? []
            };
        }

        session.EvidenceCount = evidence.Count;
        session.HypothesisCount = deep.Hypotheses.Count;
        if (deep.Hypotheses.Count == 0)
        {
            session.FusionEmptyReason =
                $"Deep Analysis found no cluster above threshold {_deepOptions.MinimumClusterConfidence:F2}. " +
                "Check Evidence Board for per-item scores (considered, not selected).";
        }
        else
        {
            session.FusionEmptyReason = null;
        }

        LastPipelineResult = new AnalysisPipelineResult
        {
            Session = session,
            Evidence = evidence,
            Hypotheses = deep.Hypotheses,
            SolarLocus = LastSolarLocus,
            NearbyLocalities = nearby
        };

        await PersistAsync(cancellationToken).ConfigureAwait(false);
        Report(progress, sessionId, "done", 1.0, $"Deep Analysis complete — {deep.Hypotheses.Count} hypothesis(es)");
        ActiveCaseChanged?.Invoke(this, EventArgs.Empty);
        return deep;
    }

    private void Report(
        IProgress<ProgressReport>? progress,
        Guid sessionId,
        string stage,
        double fraction,
        string message)
    {
        var report = ProgressReport.Of(stage, fraction, message, sessionId, LastPipelineResult?.Session.DisplayName);
        progress?.Report(report);
        ReportLiveProgress(sessionId, report, LastPipelineResult?.Session.DisplayName ?? "Deep Analysis");
    }

    public async Task<IReadOnlyList<LocationHypothesis>> RefuseAsync(
        CancellationToken cancellationToken = default)
    {
        var sessionId = CurrentSessionId
                        ?? LastPipelineResult?.Session.Id
                        ?? Guid.NewGuid();
        CurrentSessionId = sessionId;

        // Scope fusion to the current session only — never merge leftover board rows from another video.
        var boardAll = _board.All();
        var evidence = boardAll
            .Where(e => e.AnalysisSessionId == sessionId)
            .ToList();
        if (evidence.Count == 0 && boardAll.Count > 0)
        {
            var distinctSessions = boardAll.Select(e => e.AnalysisSessionId).Distinct().ToList();
            if (distinctSessions.Count == 1)
            {
                // Board-only / sample fuse: adopt the single board session id instead of inventing one.
                sessionId = distinctSessions[0];
                CurrentSessionId = sessionId;
                evidence = boardAll.ToList();
            }
            // Otherwise leave evidence empty — mixed foreign rows must not be fused under the wrong id.
        }

        var foreign = boardAll.Count(e => e.AnalysisSessionId != sessionId);
        if (LastSolarLocus is null)
        {
            var defaultObs = LastPipelineResult?.Session.CreatedAtUtc ?? DateTimeOffset.UtcNow;
            LastSolarLocus = SolarLocusFromEvidence.TryComputeLocus(sessionId, evidence, defaultObservationUtc: defaultObs);
        }

        System.Diagnostics.Debug.WriteLine(
            $"[AnalysisSession] RefuseAsync session={sessionId:N} " +
            $"board={boardAll.Count} scopedEvidence={evidence.Count} foreignExcluded={foreign} " +
            $"solar={(LastSolarLocus is null ? "none" : "present")}");

        var hypotheses = _fusion.Fuse(sessionId, evidence, LastSolarLocus);

        var nearby = await CrossLinkQuickAsync(evidence, hypotheses, cancellationToken)
            .ConfigureAwait(false);

        var session = LastPipelineResult?.Session;
        if (session is null || session.Id != sessionId)
        {
            session = new AnalysisSession
            {
                Id = sessionId,
                DisplayName = LastPipelineResult?.Session.DisplayName ?? "Re-fused session",
                Status = AnalysisStatus.Completed,
                SourceMediaPaths = LastPipelineResult?.Session.SourceMediaPaths.ToList() ?? []
            };
        }

        LastPipelineResult = new AnalysisPipelineResult
        {
            Session = session,
            Evidence = evidence,
            Hypotheses = hypotheses,
            SolarLocus = LastSolarLocus ?? (session.Id == LastPipelineResult?.Session.Id
                ? LastPipelineResult.SolarLocus
                : null),
            NearbyLocalities = nearby
        };

        await PersistAsync(cancellationToken).ConfigureAwait(false);
        return hypotheses;
    }

    public async Task HydrateFromDocumentAsync(
        SessionDocument doc,
        CancellationToken cancellationToken = default)
    {
        _board.Clear();
        _board.AddRange(doc.Evidence);
        CurrentSessionId = doc.Session.Id;
        LastSolarLocus = doc.SolarLocus;
        Trajectory.Clear();
        AllowDiskHydrate = true;

        LastPipelineResult = new AnalysisPipelineResult
        {
            Session = doc.Session.ToSession(),
            Evidence = doc.Evidence,
            Hypotheses = doc.Hypotheses,
            SolarLocus = doc.SolarLocus,
            NearbyLocalities = []
        };

        await RefuseAsync(cancellationToken).ConfigureAwait(false);
        ActiveCaseChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task PersistAsync(CancellationToken cancellationToken = default)
    {
        if (LastPipelineResult is null)
            return;

        var session = LastPipelineResult.Session;
        session.EvidenceCount = LastPipelineResult.Evidence.Count;
        session.HypothesisCount = LastPipelineResult.Hypotheses.Count;
        if (LastPipelineResult.Hypotheses.Count == 0 && string.IsNullOrWhiteSpace(session.FusionEmptyReason))
        {
            session.FusionEmptyReason =
                "No ranked location hypotheses for this case. Check Evidence Board for OCR/ASR/place leads.";
        }

        await _sessions.SaveAsync(
            session,
            LastPipelineResult.Evidence,
            LastPipelineResult.Hypotheses,
            LastPipelineResult.SolarLocus ?? LastSolarLocus,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<NearbyLocalityHit>> CrossLinkQuickAsync(
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<LocationHypothesis> hypotheses,
        CancellationToken ct)
    {
        var hits = new List<NearbyLocalityHit>();
        var minerals = evidence
            .Where(e => e.Type == EvidenceType.MineralNameMention && !e.IsRejected)
            .Select(e => e.RawContent ?? e.Summary)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8);

        foreach (var mineral in minerals)
        {
            ct.ThrowIfCancellationRequested();
            var mineralToken = mineral.Contains(':') ? mineral.Split(':').Last().Trim() : mineral.Trim();
            var found = await _localities.SearchByMineralAsync(mineralToken, cancellationToken: ct).ConfigureAwait(false);
            foreach (var loc in found.Take(8))
                hits.Add(ToNearbyHit(loc, null));
        }

        foreach (var hyp in hypotheses.Where(h => h.Center is not null).Take(5))
        {
            ct.ThrowIfCancellationRequested();
            var near = await _localities.FindNearAsync(hyp.Center!.Value, radiusKm: 75, ct)
                .ConfigureAwait(false);
            foreach (var loc in near.Take(6))
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
}

public sealed record LiveRunProgress(
    Guid SessionId,
    string DisplayName,
    string Stage,
    double Fraction,
    string? Message,
    DateTimeOffset UpdatedAtUtc);