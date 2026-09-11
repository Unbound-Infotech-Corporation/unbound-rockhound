namespace GeoMineralTrace.Core.Licensing;

/// <summary>Local entitlement state after activation or trial.</summary>
public sealed class LicenseEntitlement
{
    public required string ProductId { get; init; }
    public string? LicenseKey { get; init; }
    public string? Email { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? ValidatedAtUtc { get; init; }
    public DateTimeOffset? TrialStartedUtc { get; init; }
    public DateTimeOffset? TrialEndsUtc { get; init; }
    public int MaxActivations { get; init; } = 3;
}

public sealed class LicenseActivateResult
{
    public required bool Ok { get; init; }
    public string? Error { get; init; }
    public LicenseEntitlement? Entitlement { get; init; }
}

public static class LicenseStatuses
{
    public const string Active = "active";
    public const string Trial = "trial";
    public const string Missing = "missing";
    public const string Expired = "expired";
    public const string Revoked = "revoked";
    public const string Refunded = "refunded";
}
