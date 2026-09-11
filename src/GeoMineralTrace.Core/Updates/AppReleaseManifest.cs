namespace GeoMineralTrace.Core.Updates;

/// <summary>
/// Hosted release feed entry. Publish one JSON file per product at your update URL.
/// Schema is intentionally small so every Heirloom/sellable app can share the same format.
/// </summary>
public sealed class AppReleaseManifest
{
    /// <summary>Stable product id, e.g. "geomineral-trace".</summary>
    public string ProductId { get; set; } = "";

    /// <summary>SemVer-ish version string, e.g. "0.5.1" or "0.5.1.0".</summary>
    public string Version { get; set; } = "";

    /// <summary>UTC publish time (ISO-8601).</summary>
    public DateTimeOffset? ReleasedAtUtc { get; set; }

    /// <summary>Short headline for the in-app banner.</summary>
    public string? Title { get; set; }

    /// <summary>One-paragraph release notes (plain text).</summary>
    public string? Notes { get; set; }

    /// <summary>Direct download URL for the portable zip (or installer).</summary>
    public string? DownloadUrl { get; set; }

    /// <summary>Optional longer release notes / changelog page.</summary>
    public string? ReleaseNotesUrl { get; set; }

    /// <summary>When true, UI stresses that upgrading is strongly recommended.</summary>
    public bool Mandatory { get; set; }

    /// <summary>Minimum Windows build (optional); ignored when null.</summary>
    public int? MinOsBuild { get; set; }
}
