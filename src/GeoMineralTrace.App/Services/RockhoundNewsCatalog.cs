using GeoMineralTrace.Core.News;

namespace GeoMineralTrace_App.Services;

/// <summary>
/// Curated rockhounding / gemology / mineral news &amp; community feeds.
/// All enabled by default; users can disable individuals in Settings.
/// </summary>
public static class RockhoundNewsCatalog
{
    public static IReadOnlyList<RockhoundNewsSource> All { get; } =
    [
        new()
        {
            Id = "rockngem",
            DisplayName = "Rock & Gem Magazine",
            FeedUrl = "https://www.rockngem.com/feed/",
            Category = "Magazine",
            HomepageUrl = "https://www.rockngem.com/"
        },
        new()
        {
            Id = "igs",
            DisplayName = "International Gem Society",
            FeedUrl = "https://www.gemsociety.org/feed/",
            Category = "Gemology",
            HomepageUrl = "https://www.gemsociety.org/"
        },
        new()
        {
            Id = "geologycom",
            DisplayName = "Geology.com",
            FeedUrl = "https://geology.com/feed/",
            Category = "Geology",
            HomepageUrl = "https://geology.com/"
        },
        new()
        {
            Id = "miningcom",
            DisplayName = "Mining.com",
            FeedUrl = "https://www.mining.com/feed/",
            Category = "Mining / minerals",
            FilterToGenreKeywords = true,
            HomepageUrl = "https://www.mining.com/"
        },
        new()
        {
            Id = "usgs",
            DisplayName = "USGS News",
            FeedUrl = "https://www.usgs.gov/news/feed",
            Category = "Science agency",
            FilterToGenreKeywords = true,
            HomepageUrl = "https://www.usgs.gov/news"
        },
        new()
        {
            Id = "reddit-rockhounding",
            DisplayName = "Reddit r/Rockhounding",
            FeedUrl = "https://www.reddit.com/r/Rockhounding/.rss",
            Category = "Community",
            HomepageUrl = "https://www.reddit.com/r/Rockhounding/"
        },
        new()
        {
            Id = "reddit-mineralcollecting",
            DisplayName = "Reddit r/mineralcollecting",
            FeedUrl = "https://www.reddit.com/r/mineralcollecting/.rss",
            Category = "Community",
            HomepageUrl = "https://www.reddit.com/r/mineralcollecting/"
        },
        new()
        {
            Id = "reddit-gemstones",
            DisplayName = "Reddit r/Gemstones",
            FeedUrl = "https://www.reddit.com/r/Gemstones/.rss",
            Category = "Community",
            HomepageUrl = "https://www.reddit.com/r/Gemstones/"
        },
        new()
        {
            Id = "reddit-geology",
            DisplayName = "Reddit r/geology",
            FeedUrl = "https://www.reddit.com/r/geology/.rss",
            Category = "Community",
            FilterToGenreKeywords = true,
            HomepageUrl = "https://www.reddit.com/r/geology/"
        },
        new()
        {
            Id = "reddit-fossils",
            DisplayName = "Reddit r/FossilPorn",
            FeedUrl = "https://www.reddit.com/r/FossilPorn/.rss",
            Category = "Community",
            HomepageUrl = "https://www.reddit.com/r/FossilPorn/"
        }
    ];

    public static RockhoundNewsSource? GetById(string id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Keywords used when a feed is broader than rockhounding alone.</summary>
    public static readonly string[] GenreKeywords =
    [
        "rockhound", "rockhounding", "gem", "gemstone", "gemology", "mineral", "minerals",
        "lapidary", "crystal", "crystals", "agate", "quartz", "opal", "jade", "turquoise",
        "collecting", "specimen", "quarry", "pegmatite", "fluorite", "garnet", "amethyst",
        "geology", "geologic", "mine", "mining claim", "fossils", "fossil", "lapis"
    ];
}
