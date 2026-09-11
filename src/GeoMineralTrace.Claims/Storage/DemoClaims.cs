using GeoMineralTrace.Core.Claims;
using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Claims.Storage;

/// <summary>Small offline demo set illustrating Active / Expiring Soon / Lapsed states (never "for sale").</summary>
public static class DemoClaims
{
    public static IReadOnlyList<MiningClaim> Create()
    {
        var imported = DateTimeOffset.UtcNow;
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

        return
        [
            Build(
                "DEMO-ACTIVE-LODE-1",
                "Demo Active Lode (informational)",
                "NVDEMO000001",
                ClaimType.Lode,
                "Active",
                39.84, -117.01,
                "NV 21 0230N 0440E 022",
                "NV",
                "Demo Claimant LLC",
                new DateOnly(2019, 4, 12),
                new DateOnly(asOf.Year - 1, 8, 20),
                asOf.Year,
                "gold",
                imported,
                asOf),
            Build(
                "DEMO-EXPIRING-1",
                "Demo Expiring Soon Placer",
                "NVDEMO000002",
                ClaimType.Placer,
                "Active",
                39.85, -117.02,
                "NV 21 0230N 0440E 023",
                "NV",
                "Jane Q. Prospector",
                new DateOnly(2018, 6, 1),
                new DateOnly(2024, 8, 15),
                2024,
                "gold",
                imported,
                asOf),
            Build(
                "DEMO-LAPSED-1",
                "Demo Lapsed / Reopenable Lode",
                "NVDEMO000003",
                ClaimType.Lode,
                "Closed",
                38.26, -117.51,
                "NV 21 0050N 0400E 029",
                "NV",
                null,
                new DateOnly(2015, 3, 20),
                null,
                null,
                "silver",
                imported,
                asOf),
            Build(
                "DEMO-MILL-1",
                "Demo Mill Site (active)",
                "ORDEMO000001",
                ClaimType.MillSite,
                "Active",
                44.12, -120.45,
                "OR 31 0150S 0140E 010",
                "OR",
                "Cascade Minerals Co.",
                new DateOnly(2020, 9, 1),
                new DateOnly(asOf.Year - 1, 8, 28),
                asOf.Year,
                null,
                imported,
                asOf)
        ];
    }

    private static MiningClaim Build(
        string idSuffix,
        string name,
        string serial,
        ClaimType type,
        string disposition,
        double lat,
        double lon,
        string legal,
        string state,
        string? claimant,
        DateOnly? located,
        DateOnly? lastFee,
        int? assessmentYear,
        string? mineral,
        DateTimeOffset imported,
        DateOnly asOf)
    {
        var status = ClaimStatusClassifier.Classify(disposition, lastFee, assessmentYear, asOf);
        return new MiningClaim
        {
            Id = Guid.Parse(CreateGuidFromSuffix(idSuffix)),
            ExternalId = $"blm-mlrs:{serial}",
            ClaimName = name,
            SerialNumber = serial,
            ClaimType = type,
            Status = status,
            Coordinates = new GeoCoordinate(lat, lon),
            LegalDescription = legal,
            ClaimantOfRecord = claimant,
            LocationDate = located,
            LastMaintenanceFeePaid = lastFee,
            LastAssessmentYear = assessmentYear,
            StateCode = state,
            FieldOffice = state == "NV" ? "Battle Mountain District (demo)" : "Prineville Field Office (demo)",
            Minerals = string.IsNullOrWhiteSpace(mineral) ? [] : [mineral],
            Acres = 20.66,
            SourceDataset = "Demo / offline seed",
            SourceImportedUtc = imported,
            BlmCaseDisposition = disposition,
            MaintenanceDeadlineApproaching = ClaimStatusClassifier.MaintenanceDeadlineApproaching(
                status, lastFee, assessmentYear, asOf),
            LegalNotes = ClaimStatusClassifier.LegalExplainer(status)
        };
    }

    private static string CreateGuidFromSuffix(string suffix)
    {
        // Stable demo GUIDs for upsert identity across launches.
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(suffix));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
