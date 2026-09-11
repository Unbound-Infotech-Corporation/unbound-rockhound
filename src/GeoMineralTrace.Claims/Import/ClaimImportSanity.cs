namespace GeoMineralTrace.Claims.Import;

/// <summary>
/// Guards against silently truncated BLM national imports or demo-only production databases.
/// </summary>
public static class ClaimImportSanity
{
    /// <summary>
    /// Minimum upserted rows expected when importing a file whose name suggests a US-wide extract.
    /// Real MLRS national pulls are tens of thousands of rows; 50–100 means something failed quietly.
    /// </summary>
    public const int MinimumNationalImportUpserted = 5_000;

    /// <summary>
    /// Below this total, the in-app claims DB is almost certainly demo seed + partial state extract only.
    /// </summary>
    public const int MinimumProductionDatabaseTotal = 1_000;

    public const int DemoSeedCount = 4;

    public static void ValidateBulkImport(string filePath, int totalFeatures, int upserted)
    {
        var fileName = Path.GetFileName(filePath);
        var looksNational = fileName.Contains("us-", StringComparison.OrdinalIgnoreCase)
                            || fileName.Contains("national", StringComparison.OrdinalIgnoreCase);

        if (looksNational && upserted < MinimumNationalImportUpserted)
        {
            throw new InvalidOperationException(
                $"BLM import from '{fileName}' upserted only {upserted:N0} claims " +
                $"(parsed {totalFeatures:N0} features). Expected at least {MinimumNationalImportUpserted:N0} " +
                "for a national extract. Re-run BlmClaimsImport with --download-national and check network/disk.");
        }

        if (totalFeatures >= 500 && upserted < totalFeatures / 10)
        {
            throw new InvalidOperationException(
                $"BLM import from '{fileName}' upserted {upserted:N0} of {totalFeatures:N0} parsed features " +
                "(<10%). Most rows were skipped — check serial/coordinate mapping.");
        }
    }

    public static bool IsLikelyDemoOnlyDatabase(int totalCount) =>
        totalCount > 0 && totalCount < MinimumProductionDatabaseTotal;

    public static string DemoOnlyWarning(int totalCount) =>
        $"Claims database has only {totalCount:N0} records (expected ≥{MinimumProductionDatabaseTotal:N0} " +
        "after a national BLM import). You are viewing demo seed and/or a partial state extract. " +
        "Run: dotnet run --project artifacts/BlmClaimsImport -- --download-national";
}
