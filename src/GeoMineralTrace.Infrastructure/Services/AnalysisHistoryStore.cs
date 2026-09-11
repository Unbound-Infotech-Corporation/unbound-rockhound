using System.Text.Json;
using GeoMineralTrace.Core.Analysis;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Hypothesis;
using GeoMineralTrace.Pipeline;

namespace GeoMineralTrace.Infrastructure.Services;

/// <summary>
/// Index of completed analyses for the History view. Session payloads remain under sessions/.
/// </summary>
public sealed class AnalysisHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path = AppDataPaths.Sub("analysis-history.json");
    private readonly object _gate = new();

    public async Task RecordCompletedAnalysisAsync(AnalysisPipelineResult result, CancellationToken ct = default)
    {
        if (result.Session.Status != AnalysisStatus.Completed)
            return;

        var lead = HypothesisPresentation.SelectLead(result.Hypotheses);
        var entry = new AnalysisHistoryEntry
        {
            SessionId = result.Session.Id,
            Title = result.Session.DisplayName,
            SourceUrl = result.Session.SourceUrl,
            CompletedAtUtc = result.Session.CompletedAtUtc ?? DateTimeOffset.UtcNow,
            HypothesisCount = result.Hypotheses.Count,
            LeadHypothesisId = lead?.Id,
            LeadLabel = lead?.Label,
            LeadConfidence = lead?.Confidence.Value,
            LeadLatitude = lead?.Center?.LatitudeDegrees,
            LeadLongitude = lead?.Center?.LongitudeDegrees,
            LeadReasoningSummary = lead is null ? null : HypothesisPresentation.BuildReasoningSummary(lead)
        };

        lock (_gate)
        {
            var catalog = ReadCatalogUnsafe();
            catalog.RemoveAll(e => e.SessionId == entry.SessionId);
            catalog.Insert(0, entry);
            WriteCatalogUnsafe(catalog);
        }

        await Task.CompletedTask;
    }

    public Task<IReadOnlyList<AnalysisHistoryEntry>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AnalysisHistoryEntry>>(
                ReadCatalogUnsafe().OrderByDescending(e => e.CompletedAtUtc).ToList());
        }
    }

    /// <summary>Removes the history row only; session folder is untouched (delete via Cases).</summary>
    public Task RemoveAsync(Guid sessionId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var catalog = ReadCatalogUnsafe();
            var removed = catalog.RemoveAll(e => e.SessionId == sessionId);
            if (removed > 0)
                WriteCatalogUnsafe(catalog);
        }

        return Task.CompletedTask;
    }

    private List<AnalysisHistoryEntry> ReadCatalogUnsafe()
    {
        try
        {
            if (!File.Exists(_path))
                return [];

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<AnalysisHistoryEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void WriteCatalogUnsafe(List<AnalysisHistoryEntry> catalog)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(catalog, JsonOptions));
        File.Copy(tmp, _path, overwrite: true);
        File.Delete(tmp);
    }
}
