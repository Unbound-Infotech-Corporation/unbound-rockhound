using System.Text.Json;
using GeoMineralTrace.Core.App;
using GeoMineralTrace.Core.Trips;

namespace GeoMineralTrace.Infrastructure.Services;

public sealed class TripStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path = AppDataPaths.Sub("trips.json");
    private readonly object _gate = new();

    public Task<IReadOnlyList<Trip>> ListTripsAsync(CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<Trip>>(ReadCatalogUnsafe().Trips.OrderByDescending(t => t.CreatedAtUtc).ToList());
    }

    public Task<Trip?> GetTripAsync(Guid tripId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(ReadCatalogUnsafe().Trips.FirstOrDefault(t => t.Id == tripId));
    }

    public Task<IReadOnlyList<Waypoint>> ListWaypointsAsync(Guid tripId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<Waypoint>>(
                ReadCatalogUnsafe().Waypoints.Where(w => w.TripId == tripId).ToList());
    }

    public Task<Trip> CreateTripAsync(string name, string? description = null, CancellationToken ct = default)
    {
        var trip = new Trip
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        };

        lock (_gate)
        {
            var catalog = ReadCatalogUnsafe();
            catalog.Trips.Add(trip);
            WriteCatalogUnsafe(catalog);
        }

        return Task.FromResult(trip);
    }

    public Task<Waypoint> AddWaypointAsync(Waypoint waypoint, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var catalog = ReadCatalogUnsafe();
            if (catalog.Trips.All(t => t.Id != waypoint.TripId))
                throw new InvalidOperationException("Trip not found.");

            catalog.Waypoints.Add(waypoint);
            WriteCatalogUnsafe(catalog);
        }

        return Task.FromResult(waypoint);
    }

    public Task RemoveWaypointAsync(Guid waypointId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var catalog = ReadCatalogUnsafe();
            catalog.Waypoints.RemoveAll(w => w.Id == waypointId);
            WriteCatalogUnsafe(catalog);
        }

        return Task.CompletedTask;
    }

    public Task DeleteTripAsync(Guid tripId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var catalog = ReadCatalogUnsafe();
            catalog.Trips.RemoveAll(t => t.Id == tripId);
            catalog.Waypoints.RemoveAll(w => w.TripId == tripId);
            WriteCatalogUnsafe(catalog);
        }

        return Task.CompletedTask;
    }

    private TripCatalog ReadCatalogUnsafe()
    {
        try
        {
            if (!File.Exists(_path))
                return new TripCatalog();

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<TripCatalog>(json, JsonOptions) ?? new TripCatalog();
        }
        catch
        {
            return new TripCatalog();
        }
    }

    private void WriteCatalogUnsafe(TripCatalog catalog)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(catalog, JsonOptions));
        File.Copy(tmp, _path, overwrite: true);
        File.Delete(tmp);
    }

    private sealed class TripCatalog
    {
        public List<Trip> Trips { get; set; } = [];
        public List<Waypoint> Waypoints { get; set; } = [];
    }
}
