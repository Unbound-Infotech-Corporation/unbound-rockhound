using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Core.Claims;

/// <summary>
/// Federal mining claim / site record (BLM MLRS). Not a marketplace listing —
/// claims are not purchased from BLM.
/// </summary>
public sealed class MiningClaim
{
    public required Guid Id { get; init; }

    /// <summary>Stable external key, e.g. blm-mlrs:NV105221812.</summary>
    public string? ExternalId { get; init; }

    public required string ClaimName { get; init; }
    public required string SerialNumber { get; init; }
    public string? LegacySerialNumber { get; init; }
    public ClaimType ClaimType { get; init; } = ClaimType.Unknown;

    /// <summary>Operational status derived from BLM disposition + maintenance-fee dates.</summary>
    public ClaimStatus Status { get; init; } = ClaimStatus.Unknown;

    public GeoCoordinate? Coordinates { get; init; }
    public string? LegalDescription { get; init; }
    public string? Township { get; init; }
    public string? Range { get; init; }
    public string? Section { get; init; }
    public string? Meridian { get; init; }

    public string? ClaimantOfRecord { get; init; }
    public DateOnly? LocationDate { get; init; }
    public DateOnly? LastMaintenanceFeePaid { get; init; }
    public int? LastAssessmentYear { get; init; }

    public required string StateCode { get; init; }
    public string? County { get; init; }
    public string? FieldOffice { get; init; }

    public IReadOnlyList<string> Minerals { get; init; } = [];
    public double? Acres { get; init; }

    public string SourceDataset { get; init; } = "BLM MLRS";
    public DateTimeOffset? SourceImportedUtc { get; init; }
    public string? BlmCaseDisposition { get; init; }

    /// <summary>
    /// Seasonal advisory when Sept 1 is near but fee-payment evidence is missing from the extract.
    /// Distinct from <see cref="ClaimStatus.ExpiringSoon"/> (which requires fee/assessment evidence).
    /// </summary>
    public bool MaintenanceDeadlineApproaching { get; init; }

    /// <summary>Plain-language note for UI (never "for sale").</summary>
    public string? LegalNotes { get; init; }
}

public enum ClaimType
{
    Unknown = 0,
    Lode = 1,
    Placer = 2,
    MillSite = 3,
    TunnelSite = 4,
    Other = 99
}

/// <summary>
/// User-facing claim states. Never model "available to buy."
/// </summary>
public enum ClaimStatus
{
    Unknown = 0,

    /// <summary>Active claim of record — informational only; rights transfer only via private negotiation with the claimant.</summary>
    Active = 1,

    /// <summary>Active claim approaching the annual Sept 1 maintenance-fee deadline (or fee unpaid for the assessment year).</summary>
    ExpiringSoon = 2,

    /// <summary>Closed / forfeited / expired — ground may be open for a *new* claim to be staked (subject to land status).</summary>
    LapsedReopenable = 3,

    /// <summary>Closed without reopen implication (withdrawn land, etc.).</summary>
    Closed = 4
}
