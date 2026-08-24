using System.Globalization;
using System.Text.Json;

namespace MoexMcp.Infrastructure.Moex;

/// <summary>
/// Разбор ответов ISS MOEX: блоки вида {"columns": [...], "data": [[...]]}.
/// Доступ к ячейкам строго по ИМЕНАМ колонок — порядок колонок в ISS не гарантирован.
/// </summary>
public static class IssParser
{
    public static IReadOnlyList<Dictionary<string, JsonElement>> ParseBlock(JsonDocument doc, string blockName)
    {
        if (!doc.RootElement.TryGetProperty(blockName, out var block))
            return [];
        if (!block.TryGetProperty("columns", out var cols) || !block.TryGetProperty("data", out var data))
            return [];

        var names = cols.EnumerateArray().Select(c => c.GetString()!).ToArray();
        var rows = new List<Dictionary<string, JsonElement>>();
        foreach (var row in data.EnumerateArray())
        {
            var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            var i = 0;
            foreach (var cell in row.EnumerateArray())
            {
                if (i < names.Length)
                    dict[names[i]] = cell;
                i++;
            }
            rows.Add(dict);
        }
        return rows;
    }

    public static string? GetString(this IReadOnlyDictionary<string, JsonElement> row, string col) =>
        row.TryGetValue(col, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    public static decimal? GetDecimal(this IReadOnlyDictionary<string, JsonElement> row, string col)
    {
        if (!row.TryGetValue(col, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDecimal(out var d) => d,
            JsonValueKind.String when decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    public static long? GetLong(this IReadOnlyDictionary<string, JsonElement> row, string col)
    {
        if (!row.TryGetValue(col, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt64(out var l) => l,
            JsonValueKind.String when long.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l) => l,
            _ => null
        };
    }

    /// <summary>ISS отдаёт время как "2026-08-21 19:00:11" (московское).</summary>
    public static DateTime? GetDateTime(this IReadOnlyDictionary<string, JsonElement> row, string col)
    {
        var s = row.GetString(col);
        return s is not null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : null;
    }
}
