using System.Text.Json;
using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Evidence;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Core.Solar;

namespace GeoMineralTrace.Pipeline.Persistence;

/// <summary>
/// JSON file persistence for analysis sessions, evidence, hypotheses, and solar results.
/// Each session directory is one Cases/History record.
/// </summary>
public sealed class AnalysisSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _root;

    public AnalysisSessionStore(string? rootDirectory = null)
    {
        _root = rootDirectory ?? AppDataPaths.Sub("sessions");
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public string GetSessionDirectory(Guid sessionId)
    {
        var dir = Path.Combine(_root, sessionId.ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "keyframes"));
        return dir;
    }

    public async Task SaveAsync(
        AnalysisSession session,
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<LocationHypothesis>? hypotheses = null,
        SolarLocusResult? solarLocus = null,
        CancellationToken cancellationToken = default)
    {
        var dir = GetSessionDirectory(session.Id);
        var dto = new SessionDocument
        {
            Session = SessionDto.FromSession(session),
            Evidence = evidence.ToList(),
            Hypotheses = hypotheses?.ToList() ?? [],
            SolarLocus = solarLocus
        };

        var path = Path.Combine(dir, "session.json");
        var tmp = path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, dto, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
    }

    public async Task<SessionDocument?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetSessionDirectory(sessionId), "session.json");
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SessionDocument>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public IReadOnlyList<Guid> ListSessionIds() =>
        Directory.GetDirectories(_root)
            .Select(d => Guid.TryParse(Path.GetFileName(d), out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();

    public async Task<IReadOnlyList<AnalysisCaseSummary>> ListSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var list = new List<AnalysisCaseSummary>();
        foreach (var id in ListSessionIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var doc = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
                if (doc?.Session is null) continue;
                var s = doc.Session;
                list.Add(new AnalysisCaseSummary(
                    s.Id,
                    s.DisplayName,
                    s.SourceUrl,
                    s.SourceKind,
                    s.Status,
                    s.ProgressFraction,
                    s.CurrentStage,
                    s.EvidenceCount > 0 ? s.EvidenceCount : doc.Evidence.Count,
                    s.HypothesisCount > 0 ? s.HypothesisCount : doc.Hypotheses.Count,
                    s.FusionEmptyReason,
                    s.ErrorMessage,
                    s.CreatedAtUtc,
                    s.CompletedAtUtc,
                    s.SourceMediaPaths.FirstOrDefault()));
            }
            catch
            {
                // Skip corrupt session folders.
            }
        }

        return list
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dir = Path.Combine(_root, sessionId.ToString("N"));
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        return Task.CompletedTask;
    }
}

public sealed record AnalysisCaseSummary(
    Guid Id,
    string DisplayName,
    string? SourceUrl,
    AnalysisSourceKind SourceKind,
    AnalysisStatus Status,
    double ProgressFraction,
    string? CurrentStage,
    int EvidenceCount,
    int HypothesisCount,
    string? FusionEmptyReason,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? PrimaryMediaPath);

public sealed class SessionDocument
{
    public SessionDto Session { get; set; } = new();
    public List<EvidenceItem> Evidence { get; set; } = [];
    public List<LocationHypothesis> Hypotheses { get; set; } = [];
    public SolarLocusResult? SolarLocus { get; set; }
}

public sealed class SessionDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = "";
    public List<string> SourceMediaPaths { get; set; } = [];
    public string? SourceUrl { get; set; }
    public AnalysisSourceKind SourceKind { get; set; } = AnalysisSourceKind.LocalFile;
    public AnalysisStatus Status { get; set; }
    public double ProgressFraction { get; set; }
    public string? CurrentStage { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FusionEmptyReason { get; set; }
    public int EvidenceCount { get; set; }
    public int HypothesisCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public AnalysisSession ToSession() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        SourceMediaPaths = SourceMediaPaths,
        SourceUrl = SourceUrl,
        SourceKind = SourceKind,
        Status = Status,
        ProgressFraction = ProgressFraction,
        CurrentStage = CurrentStage,
        ErrorMessage = ErrorMessage,
        FusionEmptyReason = FusionEmptyReason,
        EvidenceCount = EvidenceCount,
        HypothesisCount = HypothesisCount,
        CreatedAtUtc = CreatedAtUtc,
        CompletedAtUtc = CompletedAtUtc
    };

    public static SessionDto FromSession(AnalysisSession session) => new()
    {
        Id = session.Id,
        DisplayName = session.DisplayName,
        SourceMediaPaths = session.SourceMediaPaths.ToList(),
        SourceUrl = session.SourceUrl,
        SourceKind = session.SourceKind,
        Status = session.Status,
        ProgressFraction = session.ProgressFraction,
        CurrentStage = session.CurrentStage,
        ErrorMessage = session.ErrorMessage,
        FusionEmptyReason = session.FusionEmptyReason,
        EvidenceCount = session.EvidenceCount,
        HypothesisCount = session.HypothesisCount,
        CreatedAtUtc = session.CreatedAtUtc,
        CompletedAtUtc = session.CompletedAtUtc
    };
}
