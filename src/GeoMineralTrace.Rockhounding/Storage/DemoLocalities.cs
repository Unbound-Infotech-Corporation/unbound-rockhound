using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Rockhounding.Scoring;

namespace GeoMineralTrace.Rockhounding.Storage;

/// <summary>
/// Small, attributed demo seed set for offline UI and tests.
/// Replace/extend via documented import pipelines (USGS, state surveys, etc.).
/// </summary>
public static class DemoLocalities
{
    public static IReadOnlyList<Locality> Create()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            Build(
                Guid.Parse("11111111-1111-1111-1111-111111111101"),
                "Maury Mountain Obsidian",
                "OR",
                "Crook",
                new GeoCoordinate(44.083, -120.433),
                ["obsidian"],
                LandType.PublicBlm,
                AccessStatus.Open,
                DifficultyLevel.Beginner,
                "Public BLM collecting area; verify current rules before travel.",
                "Obsidian nodules and flow material.",
                ["BLM public collecting guidance (verify locally)", "Public rockhounding compilations"],
                productivity: 8, variety: 4),
            Build(
                Guid.Parse("11111111-1111-1111-1111-111111111102"),
                "Emerald Creek Garnet Area",
                "ID",
                "Benewah",
                new GeoCoordinate(47.02, -116.33),
                ["garnet", "almandine"],
                LandType.PublicUsfs,
                AccessStatus.PermitRequired,
                DifficultyLevel.Intermediate,
                "USFS fee/permit area historically; check current Forest Service notices.",
                "Star garnets in creek gravels.",
                ["USFS recreation info (verify)", "Idaho state rockhounding guides"],
                productivity: 7.5, variety: 3, accessibility: 6),
            Build(
                Guid.Parse("11111111-1111-1111-1111-111111111103"),
                "Crater of Diamonds State Park",
                "AR",
                "Pike",
                new GeoCoordinate(34.032, -93.672),
                ["diamond"],
                LandType.StatePark,
                AccessStatus.Open,
                DifficultyLevel.Beginner,
                "Fee area; dig-your-own park. Follow park rules — commercial claims elsewhere may be closed.",
                "Diamonds in weathered lamproite soil.",
                ["Arkansas State Parks"],
                productivity: 5, variety: 2, accessibility: 9, beginner: 9),
            Build(
                Guid.Parse("11111111-1111-1111-1111-111111111199"),
                "Closed Example Claim (Demo)",
                "NV",
                "Nye",
                new GeoCoordinate(38.1, -116.5),
                ["turquoise"],
                LandType.Claim,
                AccessStatus.Closed,
                DifficultyLevel.Advanced,
                "DEMO: Marked closed/restricted for disclaimer UX testing. Do not visit.",
                "n/a",
                ["Synthetic demo record — not a collectable site"],
                productivity: 8, variety: 5, accessibility: 2)
        ];

        Locality Build(
            Guid id,
            string name,
            string state,
            string county,
            GeoCoordinate coords,
            string[] minerals,
            LandType land,
            AccessStatus access,
            DifficultyLevel difficulty,
            string accessNotes,
            string finds,
            string[] sources,
            double productivity = 6,
            double variety = 5,
            double accessibility = 7,
            double? beginner = null)
        {
            var legal = LocalityRatingCalculator.LegalClarityFromAccess(access);
            var beg = beginner ?? LocalityRatingCalculator.BeginnerScore(difficulty, access);
            var rating = LocalityRatingCalculator.Create(
                accessibility,
                productivity,
                legal,
                recency: 7,
                beginnerFriendliness: beg,
                variety,
                safety: access == AccessStatus.Closed ? 2 : 7,
                notes: access == AccessStatus.Closed
                    ? "FLAGGED CLOSED/RESTRICTED — excluded from beginner/top lists by legal clarity."
                    : null);

            return new Locality
            {
                Id = id,
                Name = name,
                StateCode = state,
                County = county,
                Coordinates = coords,
                ReportedMinerals = minerals,
                LandType = land,
                AccessStatus = access,
                Difficulty = difficulty,
                AccessNotes = accessNotes,
                TypicalFinds = finds,
                Sources = sources,
                SystemRating = rating,
                CollectingLimits = "Always verify land status, claims, and collecting limits before visiting.",
                HazardNotes = "Remote desert/mountain hazards possible; carry water, maps, and tell someone your plans.",
                LastVerifiedUtc = now,
                EnrichmentSummary = "Demo seed only. Replace with attributed public-source enrichment under user consent."
            };
        }
    }
}
