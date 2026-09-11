using GeoMineralTrace.Core.Claims;

namespace GeoMineralTrace.Claims;

/// <summary>
/// Derives user-facing claim status and honest legal copy.
/// Maintenance fees for unpatented claims are due on or before September 1 each year.
/// </summary>
public static class ClaimStatusClassifier
{
    public const int DefaultExpiringSoonWindowDays = 60;

    public static ClaimStatus Classify(
        string? blmDisposition,
        DateOnly? lastFeePaid,
        int? lastAssessmentYear,
        DateOnly asOf,
        int expiringSoonWindowDays = DefaultExpiringSoonWindowDays)
    {
        var disp = (blmDisposition ?? "").Trim();
        var closed = disp.Contains("closed", StringComparison.OrdinalIgnoreCase)
                     || disp.Contains("forfeit", StringComparison.OrdinalIgnoreCase)
                     || disp.Contains("abandon", StringComparison.OrdinalIgnoreCase)
                     || disp.Contains("cancel", StringComparison.OrdinalIgnoreCase);

        if (closed)
            return ClaimStatus.LapsedReopenable;

        var active = disp.Contains("active", StringComparison.OrdinalIgnoreCase)
                     || string.IsNullOrWhiteSpace(disp);

        if (!active)
            return ClaimStatus.Unknown;

        var nextDeadline = NextSeptemberFirst(asOf);
        var daysToDeadline = nextDeadline.DayNumber - asOf.DayNumber;
        var inWindow = daysToDeadline >= 0 && daysToDeadline <= expiringSoonWindowDays;

        var feeCoversCurrentYear = FeeCoversAssessmentYear(lastFeePaid, lastAssessmentYear, asOf, nextDeadline);
        var hasFeeEvidence = lastFeePaid is not null || lastAssessmentYear is not null;

        if (!feeCoversCurrentYear && hasFeeEvidence &&
            (inWindow || FeeClearlyOverdue(lastFeePaid, lastAssessmentYear, asOf, nextDeadline)))
            return ClaimStatus.ExpiringSoon;

        return ClaimStatus.Active;
    }

    /// <summary>
    /// True when Sept 1 is near and we lack fee-payment evidence — show as advisory, not ExpiringSoon.
    /// </summary>
    public static bool MaintenanceDeadlineApproaching(
        ClaimStatus status,
        DateOnly? lastFeePaid,
        int? lastAssessmentYear,
        DateOnly asOf,
        int expiringSoonWindowDays = DefaultExpiringSoonWindowDays)
    {
        if (status is ClaimStatus.LapsedReopenable or ClaimStatus.Closed)
            return false;

        var nextDeadline = NextSeptemberFirst(asOf);
        var daysToDeadline = nextDeadline.DayNumber - asOf.DayNumber;
        if (daysToDeadline < 0 || daysToDeadline > expiringSoonWindowDays)
            return false;

        return lastFeePaid is null && lastAssessmentYear is null;
    }

    public static DateOnly NextSeptemberFirst(DateOnly asOf) =>
        asOf.Month < 9 || (asOf.Month == 9 && asOf.Day <= 1)
            ? new DateOnly(asOf.Year, 9, 1)
            : new DateOnly(asOf.Year + 1, 9, 1);

    private static bool FeeCoversAssessmentYear(
        DateOnly? lastFeePaid,
        int? lastAssessmentYear,
        DateOnly asOf,
        DateOnly nextDeadline)
    {
        // Assessment year for the upcoming Sept 1 deadline is the calendar year of that deadline.
        var assessmentYear = nextDeadline.Year;
        if (lastAssessmentYear is { } y && y >= assessmentYear)
            return true;

        if (lastFeePaid is { } paid)
        {
            // Payment on/after Sept 1 of previous year typically covers the current assessment year cycle.
            var cycleStart = new DateOnly(assessmentYear - 1, 9, 1);
            return paid >= cycleStart;
        }

        // Unknown fee history — do not assume paid.
        return false;
    }

    private static bool FeeClearlyOverdue(
        DateOnly? lastFeePaid,
        int? lastAssessmentYear,
        DateOnly asOf,
        DateOnly nextDeadline)
    {
        var assessmentYear = nextDeadline.Year;
        if (lastAssessmentYear is { } y && y < assessmentYear - 1)
            return true;

        if (lastFeePaid is { } paid && paid < new DateOnly(assessmentYear - 1, 9, 1))
            return true;

        // Past Sept 1 with no evidence of current-year payment.
        if (asOf > nextDeadline && lastAssessmentYear is null && lastFeePaid is null)
            return false; // stay Active informational — we simply lack fee data

        return false;
    }

    public static string LegalExplainer(ClaimStatus status) => status switch
    {
        ClaimStatus.Active =>
            "ACTIVE — This is an existing claim of record. Federal mining claims are not sold by BLM. " +
            "The only way to acquire rights to an active claim is a private transfer negotiated directly with the claimant of record. " +
            "Do not prospect or remove minerals from claimed ground without the claimant’s permission.",

        ClaimStatus.ExpiringSoon =>
            "EXPIRING SOON — Annual maintenance fees for unpatented claims are due on or before September 1. " +
            "This record appears at risk of lapsing if the fee is not paid. Verify payment status in MLRS before relying on this flag. " +
            "If the claim later closes for non-payment, the ground may become open for a *new* claim to be staked (subject to land status). " +
            "This is not a purchase listing from BLM.",

        ClaimStatus.LapsedReopenable =>
            "LAPSED / CLOSED — BLM shows this claim as closed (often for missed maintenance). " +
            "Lapsed ground on open federal land may be available for a *new* claim to be staked by anyone qualified under the mining law — " +
            "not purchased from BLM. Always confirm current land status, withdrawals, and conflicts before staking.",

        ClaimStatus.Closed =>
            "CLOSED — This case is closed. Confirm whether the land remains open to location before any staking activity.",

        _ =>
            "UNKNOWN — Disposition is unclear in the imported extract. Verify the serial number in BLM MLRS before any field decision."
    };

    public static string StatusBadge(ClaimStatus status) => status switch
    {
        ClaimStatus.Active => "Active",
        ClaimStatus.ExpiringSoon => "Expiring Soon",
        ClaimStatus.LapsedReopenable => "Lapsed / Reopenable",
        ClaimStatus.Closed => "Closed",
        _ => "Unknown"
    };
}
