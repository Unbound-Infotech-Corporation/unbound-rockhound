namespace GeoMineralTrace.Core.Rockhounding;

/// <summary>
/// Transparent multi-factor rating. Each factor is 0–10; overall is a weighted mean.
/// </summary>
public sealed class LocalityRating
{
    public required double Accessibility { get; init; }
    public required double Productivity { get; init; }
    public required double LegalClarity { get; init; }
    public required double Recency { get; init; }
    public required double BeginnerFriendliness { get; init; }
    public required double Variety { get; init; }
    public double? Safety { get; init; }
    public string? Notes { get; init; }

    public double Overall => ComputeOverall(
        Accessibility, Productivity, LegalClarity, Recency,
        BeginnerFriendliness, Variety, Safety);

    public static double ComputeOverall(
        double accessibility,
        double productivity,
        double legalClarity,
        double recency,
        double beginnerFriendliness,
        double variety,
        double? safety = null)
    {
        // Weights sum to 1.0. Legal clarity and accessibility are prioritized
        // because closed/illegal sites must not rank highly regardless of finds.
        const double wAccess = 0.18;
        const double wProd = 0.18;
        const double wLegal = 0.22;
        const double wRecency = 0.12;
        const double wBeginner = 0.12;
        const double wVariety = 0.10;
        const double wSafety = 0.08;

        var safetyScore = safety ?? 5.0;
        var score =
            accessibility * wAccess +
            productivity * wProd +
            legalClarity * wLegal +
            recency * wRecency +
            beginnerFriendliness * wBeginner +
            variety * wVariety +
            safetyScore * wSafety;

        return Math.Round(Math.Clamp(score, 0, 10), 2);
    }

    public IReadOnlyDictionary<string, double> Breakdown => new Dictionary<string, double>
    {
        ["Accessibility"] = Accessibility,
        ["Productivity"] = Productivity,
        ["LegalClarity"] = LegalClarity,
        ["Recency"] = Recency,
        ["BeginnerFriendliness"] = BeginnerFriendliness,
        ["Variety"] = Variety,
        ["Safety"] = Safety ?? 5.0,
        ["Overall"] = Overall
    };
}
