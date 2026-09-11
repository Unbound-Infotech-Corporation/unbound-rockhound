using System.Text.Json;
using GeoMineralTrace.Core.App;
using GeoMineralTrace_App.Services;

namespace GeoMineralTrace_App.Helpers;

/// <summary>Which home-screen news feeds are enabled. Default = all catalog sources.</summary>
public static class NewsSourcePreferences
{
    public const string EnabledSourcesKey = "NewsSourcesEnabled";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static IReadOnlySet<string> GetEnabledSourceIds()
    {
        var raw = AppPreferences.ReadSetting(EnabledSourcesKey);
        if (string.IsNullOrWhiteSpace(raw))
            return RockhoundNewsCatalog.All.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(raw, JsonOptions);
            if (list is null || list.Count == 0)
                return RockhoundNewsCatalog.All.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            return list
                .Where(id => RockhoundNewsCatalog.GetById(id) is not null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return RockhoundNewsCatalog.All.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static bool IsEnabled(string sourceId) =>
        GetEnabledSourceIds().Contains(sourceId);

    public static void SetEnabled(string sourceId, bool enabled)
    {
        var set = GetEnabledSourceIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (enabled)
            set.Add(sourceId);
        else
            set.Remove(sourceId);

        // Keep at least one source if user turns everything off — fall back to all on next read if empty.
        var payload = set.Count == 0
            ? "[]"
            : JsonSerializer.Serialize(set.OrderBy(x => x).ToList(), JsonOptions);
        AppPreferences.WriteSetting(EnabledSourcesKey, payload);
    }

    public static void EnableAll()
    {
        var ids = RockhoundNewsCatalog.All.Select(s => s.Id).ToList();
        AppPreferences.WriteSetting(EnabledSourcesKey, JsonSerializer.Serialize(ids, JsonOptions));
    }
}
