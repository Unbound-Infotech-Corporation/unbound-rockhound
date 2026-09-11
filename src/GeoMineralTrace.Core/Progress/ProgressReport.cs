namespace GeoMineralTrace.Core.Progress;

/// <summary>
/// Structured progress event for long-running pipelines.
/// </summary>
public sealed record ProgressReport(
    string Stage,
    double FractionComplete,
    string? Message = null,
    bool IsCancellable = true,
    Guid? SessionId = null,
    string? DisplayName = null)
{
    public static ProgressReport Of(
        string stage,
        double fraction,
        string? message = null,
        Guid? sessionId = null,
        string? displayName = null) =>
        new(stage, Math.Clamp(fraction, 0, 1), message, SessionId: sessionId, DisplayName: displayName);
}
