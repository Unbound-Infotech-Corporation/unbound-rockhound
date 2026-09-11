using GeoMineralTrace.Core.App;
using GeoMineralTrace.Claims;
using GeoMineralTrace.Claims.Import;
using GeoMineralTrace.Claims.Storage;
using GeoMineralTrace.Core.DeepAnalysis;
using GeoMineralTrace.Evidence.Store;
using GeoMineralTrace.Hypothesis.Fusion;
using GeoMineralTrace.Infrastructure.Licensing;
using GeoMineralTrace.Infrastructure.Services;
using GeoMineralTrace.Infrastructure.Social;
using GeoMineralTrace.Pipeline;
using GeoMineralTrace.Pipeline.Abstractions;
using GeoMineralTrace.Pipeline.Audio;
using GeoMineralTrace.Pipeline.Ingest;
using GeoMineralTrace.Pipeline.Keyframes;
using GeoMineralTrace.Pipeline.Ocr;
using GeoMineralTrace.Pipeline.Persistence;
using GeoMineralTrace.Pipeline.Vision;
using GeoMineralTrace.Reporting;
using GeoMineralTrace.Hydrology;
using GeoMineralTrace.Hydrology.Import;
using GeoMineralTrace.Hydrology.Storage;
using GeoMineralTrace.Rockhounding.Import;
using GeoMineralTrace.Rockhounding.Storage;
using GeoMineralTrace.Solar;
using GeoMineralTrace.Solar.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace GeoMineralTrace.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGeoMineralTraceCore(this IServiceCollection services, string? localityDbPath = null)
    {
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddDebug();
        });

        services.AddSingleton<AnalysisSessionContext>();
        services.AddSingleton<SeedDataBootstrapper>();
        services.AddSingleton<SolarPositionCalculator>();
        services.AddSingleton<SolarLocusEngine>();
        services.AddSingleton<TechniquesKnowledgeBase>();
        services.AddSingleton<EvidenceBoardStore>();
        services.AddSingleton<HypothesisFusionEngine>();
        services.AddSingleton(_ => new DeepAnalysisOptions());
        services.AddSingleton<DeepAnalysisPass>(sp =>
            new DeepAnalysisPass(
                sp.GetRequiredService<DeepAnalysisOptions>(),
                sp.GetRequiredService<HypothesisFusionEngine>()));
        services.AddSingleton<AnalysisSessionStore>();
        services.AddSingleton<AnalysisHistoryStore>();
        services.AddSingleton<TripStore>();
        services.AddSingleton<SupabaseConfigStore>();
        services.AddSingleton<SocialSessionStore>();
        services.AddSingleton<LicenseLocalStore>();
        services.AddHttpClient("supabase");
        services.AddHttpClient("license");
        services.AddSingleton<SocialAuthService>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("supabase");
            return new SocialAuthService(
                sp.GetRequiredService<SupabaseConfigStore>(),
                sp.GetRequiredService<SocialSessionStore>(),
                http);
        });
        services.AddSingleton<ISocialAuthService>(sp => sp.GetRequiredService<SocialAuthService>());
        services.AddSingleton<ILicenseService>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("license");
            return new LicenseService(
                sp.GetRequiredService<SupabaseConfigStore>(),
                sp.GetRequiredService<LicenseLocalStore>(),
                http);
        });
        services.AddSingleton<ISocialProfileService>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("supabase");
            return new SocialProfileService(
                sp.GetRequiredService<ISocialAuthService>(),
                sp.GetRequiredService<SocialAuthService>(),
                http,
                sp.GetRequiredService<SupabaseConfigStore>());
        });
        services.AddSingleton<ISocialForumService>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("supabase");
            return new SocialForumService(
                sp.GetRequiredService<ISocialAuthService>(),
                sp.GetRequiredService<SocialAuthService>(),
                http,
                sp.GetRequiredService<SupabaseConfigStore>());
        });
        services.AddSingleton<IKeyframeExtractor, FfmpegKeyframeExtractor>();
        services.AddSingleton<IOcrEngine, SidecarOcrEngine>();
        services.AddSingleton<ITranscriptionEngine, SidecarTranscriptionEngine>();
        services.AddSingleton<ISceneTagger, HeuristicSceneTagger>();
        services.AddSingleton<YouTubeMediaFetcher>();
        services.AddSingleton<ResearchReportExporter>();
        services.AddSingleton<LocalityCsvImporter>();
        services.AddSingleton<UsgsMrdsImporter>();
        services.AddSingleton<RumouredSiteCsvImporter>();
        services.AddSingleton<BlmMlrsClaimImporter>();
        services.AddSingleton<NhdWatercourseImporter>();
        services.AddSingleton<RiverFindsCsvImporter>();
        services.AddSingleton<TrailsGeoJsonImporter>();
        services.AddSingleton<MineralGlossaryImporter>();

        var dataDir = AppDataPaths.LocalRoot;
        Directory.CreateDirectory(dataDir);

        var dbPath = localityDbPath ?? Path.Combine(dataDir, "localities.db");
        var claimsDbPath = Path.Combine(dataDir, "claims.db");
        var findsDbPath = Path.Combine(dataDir, "finds.db");
        var rumouredDbPath = Path.Combine(dataDir, "rumoured.db");
        var riversDbPath = Path.Combine(dataDir, "rivers.db");
        var trailsDbPath = Path.Combine(dataDir, "trails.db");
        var glossaryDbPath = Path.Combine(dataDir, "glossary.db");
        var glossaryImages = Path.Combine(dataDir, "glossary-images");

        services.AddSingleton(_ => new LocalityStore(dbPath));
        services.AddSingleton(_ => new ClaimStore(claimsDbPath));
        services.AddSingleton(_ => new PersonalFindStore(findsDbPath));
        services.AddSingleton(_ => new RumouredSiteStore(rumouredDbPath));
        services.AddSingleton(_ => new RiverStore(riversDbPath));
        services.AddSingleton(_ => new TrailStore(trailsDbPath));
        services.AddSingleton(_ => new MineralGlossaryStore(glossaryDbPath, glossaryImages));
        services.AddSingleton<UserRatingService>();
        services.AddSingleton<ClaimLocalityCrossLink>();
        services.AddSingleton<RiverLocalityCrossLink>();
        services.AddSingleton<MineOwnershipEnricher>();

        services.AddSingleton<AnalysisPipeline>(sp => new AnalysisPipeline(
            sp.GetRequiredService<EvidenceBoardStore>(),
            sp.GetRequiredService<AnalysisSessionStore>(),
            sp.GetRequiredService<HypothesisFusionEngine>(),
            sp.GetRequiredService<IKeyframeExtractor>(),
            sp.GetRequiredService<IOcrEngine>(),
            sp.GetRequiredService<ITranscriptionEngine>(),
            sp.GetRequiredService<ISceneTagger>(),
            sp.GetRequiredService<LocalityStore>(),
            sp.GetService<ILogger<AnalysisPipeline>>()));

        return services;
    }
}
