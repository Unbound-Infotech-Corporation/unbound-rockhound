namespace GeoMineralTrace.Core.News;

public sealed class RockhoundNewsSource
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string FeedUrl { get; init; }
    public string Category { get; init; } = "General";
    public bool FilterToGenreKeywords { get; init; }
    public string HomepageUrl { get; init; } = "";
}

public sealed class RockhoundNewsHeadline
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required string SourceId { get; init; }
    public required string SourceName { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string? Summary { get; init; }
}
