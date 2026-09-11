using System.Collections.Concurrent;
using GeoMineralTrace.Core.Evidence;

namespace GeoMineralTrace.Evidence.Store;

/// <summary>
/// In-memory evidence board store for the current session (filterable/searchable).
/// Persistent project storage is a later milestone.
/// </summary>
public sealed class EvidenceBoardStore
{
    private readonly ConcurrentDictionary<Guid, EvidenceItem> _items = new();

    public void Add(EvidenceItem item) => _items[item.Id] = item;

    public void AddRange(IEnumerable<EvidenceItem> items)
    {
        foreach (var item in items)
            Add(item);
    }

    public bool TryGet(Guid id, out EvidenceItem? item) => _items.TryGetValue(id, out item);

    public IReadOnlyList<EvidenceItem> All() =>
        _items.Values.OrderBy(e => e.MediaTimestamp).ThenBy(e => e.ExtractedAtUtc).ToList();

    public IReadOnlyList<EvidenceItem> Filter(
        EvidenceType? type = null,
        string? search = null,
        bool includeRejected = false)
    {
        IEnumerable<EvidenceItem> q = _items.Values;
        if (!includeRejected)
            q = q.Where(e => !e.IsRejected);
        if (type is { } t)
            q = q.Where(e => e.Type == t);
        if (!string.IsNullOrWhiteSpace(search))
        {
            q = q.Where(e =>
                e.Summary.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (e.RawContent?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return q.OrderBy(e => e.MediaTimestamp).ThenBy(e => e.ExtractedAtUtc).ToList();
    }

    public void SetRejected(Guid id, bool rejected)
    {
        if (_items.TryGetValue(id, out var item))
            item.IsRejected = rejected;
    }

    public void SetWeight(Guid id, EvidenceWeight weight)
    {
        if (_items.TryGetValue(id, out var item))
            item.UserWeight = weight;
    }

    public void Clear() => _items.Clear();
}
