using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Pipeline.Abstractions;

namespace GeoMineralTrace.Pipeline.Audio;

/// <summary>
/// Tries transcription engines in order; returns the first non-empty transcript.
/// </summary>
public sealed class CompositeTranscriptionEngine : ITranscriptionEngine
{
    private readonly IReadOnlyList<ITranscriptionEngine> _engines;

    public CompositeTranscriptionEngine(string name, params ITranscriptionEngine[] engines)
    {
        EngineName = name;
        _engines = engines;
    }

    public string EngineName { get; }

    public async Task<TranscriptionResult> TranscribeAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        TranscriptionResult? bestEmpty = null;

        foreach (var engine in _engines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await engine.TranscribeAsync(mediaPath, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result.FullText))
                    return result with { Notes = $"{result.Notes} [{engine.EngineName}]".Trim() };

                bestEmpty ??= result;
            }
            catch
            {
                // Try next engine.
            }
        }

        return bestEmpty ?? new TranscriptionResult(
            string.Empty,
            null,
            [],
            Confidence.None,
            "No transcription engines produced text.");
    }
}