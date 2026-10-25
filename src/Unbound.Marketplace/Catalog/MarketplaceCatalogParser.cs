using System.Globalization;
using System.Text.Json;

namespace Unbound.Marketplace.Catalog;

public sealed class MarketplaceCatalogException : Exception
{
    public MarketplaceCatalogException(string message) : base(message)
    {
    }
}

public static class MarketplaceCatalogParser
{
    public const int ShortDescriptionLimit = 180;

    public static IReadOnlyList<MarketplaceProduct> Parse(string json, MarketplaceOptions? options = null)
    {
        options ??= new MarketplaceOptions();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new MarketplaceCatalogException("Catalog JSON could not be read.");
        }

        using (doc)
        {
            var array = ResolveArray(doc.RootElement);
            var list = new List<MarketplaceProduct>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (ReadBool(item, "is_sample", "isSample") == true)
                    continue;

                var id = ReadString(item, "id", "Id");
                var name = ReadString(item, "name", "Name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    continue;

                var tagline = ReadString(item, "tagline", "Tagline");
                var description = ReadString(item, "description", "Description");
                var licenseId = ResolveLicenseProductId(item, id, name, options);
                var icon = ReadString(item, "image_url", "imageUrl", "icon_url", "iconUrl");
                var store = ReadString(item, "store_url", "storeUrl");
                if (string.IsNullOrWhiteSpace(store))
                    store = MarketplaceDefaults.StorePage(id);

                var status = ReadString(item, "status", "Status");
                if (string.IsNullOrWhiteSpace(status))
                    status = "active";

                var price = ReadPrice(item);
                list.Add(new MarketplaceProduct
                {
                    Id = id.Trim(),
                    Name = name.Trim(),
                    ShortDescription = BuildShortDescription(tagline, description),
                    Price = price,
                    PriceLabel = FormatPrice(price),
                    IconUrl = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim(),
                    Status = status.Trim(),
                    LicenseProductId = string.IsNullOrWhiteSpace(licenseId) ? null : licenseId.Trim(),
                    StoreUrl = store,
                    DownloadUrl = ReadString(item, "download_url", "downloadUrl"),
                    Featured = ReadBool(item, "featured", "Featured") == true,
                    SortOrder = ReadDouble(item, "sort_order", "sortOrder") ?? 0,
                    Monogram = BuildMonogram(name)
                });
            }

            return list
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public static string BuildShortDescription(string? tagline, string? description)
    {
        var source = string.IsNullOrWhiteSpace(tagline) ? description : tagline;
        if (string.IsNullOrWhiteSpace(source))
            return "";

        var collapsed = string.Join(' ', source
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= ShortDescriptionLimit)
            return collapsed;
        return collapsed[..(ShortDescriptionLimit - 1)].TrimEnd() + "…";
    }

    public static string FormatPrice(decimal? price) =>
        price is null
            ? ""
            : price.Value.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

    public static string BuildMonogram(string name)
    {
        var words = name.Split([' ', '—', '–', '-', '·', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0)
            .Take(2)
            .ToArray();
        if (words.Length == 0)
            return "?";
        if (words.Length == 1)
            return char.ToUpperInvariant(words[0][0]).ToString();
        return string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[1][0]));
    }

    private static string? ResolveLicenseProductId(JsonElement item, string id, string name, MarketplaceOptions options)
    {
        var fromRow = ReadString(item, "license_product_id", "licenseProductId", "stripe_product_id", "stripeProductId");
        if (!string.IsNullOrWhiteSpace(fromRow))
            return fromRow;
        if (options.LicenseProductIdByCatalogId.TryGetValue(id.Trim(), out var byId) && !string.IsNullOrWhiteSpace(byId))
            return byId;
        if (options.LicenseProductIdByName.TryGetValue(name.Trim(), out var byName) && !string.IsNullOrWhiteSpace(byName))
            return byName;
        return null;
    }

    private static JsonElement ResolveArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryGet(root, out var products, "products", "Products") && products.ValueKind == JsonValueKind.Array)
                return products;
            if (TryGet(root, out var data, "data", "Data") && data.ValueKind == JsonValueKind.Array)
                return data;
        }

        throw new MarketplaceCatalogException("Catalog JSON must be an array or an object with a products array.");
    }

    private static bool TryGet(JsonElement item, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out value))
                return true;
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement item, params string[] names)
    {
        if (!TryGet(item, out var value, names) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool? ReadBool(JsonElement item, params string[] names)
    {
        if (!TryGet(item, out var value, names))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static double? ReadDouble(JsonElement item, params string[] names)
    {
        if (!TryGet(item, out var value, names))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static decimal? ReadPrice(JsonElement item)
    {
        if (!TryGet(item, out var value, "price", "Price") || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.Number
            && decimal.TryParse(value.GetRawText(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()?.Trim().TrimStart('$');
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        return null;
    }
}
