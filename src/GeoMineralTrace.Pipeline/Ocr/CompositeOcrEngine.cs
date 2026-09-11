using GeoMineralTrace.Pipeline.Abstractions;

namespace GeoMineralTrace.Pipeline.Ocr;

/// <summary>
/// Tries engines in order; returns the first non-empty recognition result set.
/// </summary>
public sealed class CompositeOcrEngine : IOcrEngine
{
    private readonly IReadOnlyList<IOcrEngine> _engines;

    public CompositeOcrEngine(string name, params IOcrEngine[] engines)
    {
        EngineName = name;
        _engines = engines;
    }

    public string EngineName { get; }

    public async Task<IReadOnlyList<OcrResult>> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        foreach (var engine in _engines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var results = await engine.RecognizeAsync(imagePath, cancellationToken).ConfigureAwait(false);
                if (results.Count > 0)
                    return results;
            }
            catch
            {
                // Try next engine.
            }
        }

        return [];
    }
}
