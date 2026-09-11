using GeoMineralTrace.Core.Rockhounding;

namespace GeoMineralTrace.Rockhounding.Scoring;

/// <summary>
/// Builds transparent locality ratings. Legal clarity is derived in part from
/// access status so closed/restricted sites cannot rank as top overall picks.
/// </summary>
public static class LocalityRatingCalculator
{
    public static LocalityRating Create(
        double accessibility,
        double productivity,
        double legalClarity,
        double recency,
        double beginnerFriendliness,
        double variety,
        double? safety = null,
        string? notes = null) =>
        new()
        {
            Accessibility = Clamp10(accessibility),
            Productivity = Clamp10(productivity),
            LegalClarity = Clamp10(legalClarity),
            Recency = Clamp10(recency),
            BeginnerFriendliness = Clamp10(beginnerFriendliness),
            Variety = Clamp10(variety),
            Safety = safety is null ? null : Clamp10(safety.Value),
            Notes = notes
        };

    public static double LegalClarityFromAccess(AccessStatus status) => status switch
    {
        AccessStatus.Open => 9.0,
        AccessStatus.PermitRequired => 7.0,
        AccessStatus.Seasonal => 6.5,
        AccessStatus.Restricted => 3.0,
        AccessStatus.Closed => 0.5,
        _ => 4.0
    };

    /// <summary>
    /// LOW legal-clarity tier for USGS MRDS / critical-minerals imports.
    /// Those datasets do not reflect current land, claim, or collecting access.
    /// Always below verified seed/locality clarity (never use <see cref="LegalClarityFromAccess"/> alone for MRDS).
    /// </summary>
    public const double LegalClarityForUsgsOccurrence = 2.0;

    /// <summary>Recency score for MRDS (systematic updates ceased 2011).</summary>
    public const double RecencyForUsgsMrds = 2.0;

    /// <summary>Recency for USGS Critical Minerals US release (2023 vintage).</summary>
    public const double RecencyForUsgsCriticalMinerals = 5.0;

    public static double BeginnerScore(DifficultyLevel difficulty, AccessStatus access) =>
        (difficulty, access) switch
        {
            (DifficultyLevel.Beginner, AccessStatus.Open) => 9.0,
            (DifficultyLevel.Beginner, _) => 7.0,
            (DifficultyLevel.Intermediate, AccessStatus.Open) => 6.0,
            (DifficultyLevel.Intermediate, _) => 5.0,
            (DifficultyLevel.Advanced, _) => 3.0,
            (DifficultyLevel.Expert, _) => 1.5,
            _ => 4.0
        };

    private static double Clamp10(double v) => Math.Clamp(v, 0, 10);
}
