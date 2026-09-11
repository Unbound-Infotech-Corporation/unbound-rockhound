namespace GeoMineralTrace.Core.Trips;

public sealed class Waypoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public required string Name { get; set; }
    public SiteType SiteType { get; set; } = SiteType.DigSite;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Notes { get; set; }
    /// <summary>When set, waypoint came from a completed video analysis.</summary>
    public Guid? SourceAnalysisSessionId { get; set; }
    public Guid? SourceHypothesisId { get; set; }
    public string? SourceVideoTitle { get; set; }
}
