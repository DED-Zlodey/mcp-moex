using System.Globalization;
using System.Text.Json;

namespace MoexMcp.Infrastructure.Moex;

/// <summary>
/// Разбор ответов ISS MOEX: блоки вида {"columns": [...], "data": [[...]]}.
/// Доступ к ячейкам строго по ИМЕНАМ колонок — порядок колонок в ISS не гарантирован.
/// </summary>
public static class IssParser
{
    /// <summary>
    /// Парсит блок данных ответа ISS, содержащий массивы "columns" и "data",
    /// и формирует список строк в виде словарей "имя столбца — значение".
    /// </summary>
    /// <param name="doc">Документ JSON с корневым элементом, содержащим искомый блок.</param>
    /// <param name="blockName">Имя блока на верхнем уровне документа.</param>
    /// <returns>
    /// Список строк, где каждая строка представлена словарём сопоставления имён столбцов
    /// и соответствующих элементов JSON. Возвращает пустой список, если блок, столбцы
    /// или данные отсутствуют.
    /// </returns>
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

    /// <summary>
    /// Возвращает строковое значение ячейки из строки ISS по имени колонки.
    /// Если колонка отсутствует или содержит значение не строкового типа, возвращает <c>null</c>.
    /// </summary>
    /// <param name="row">Строка ISS в виде словаря значений по именам колонок.</param>
    /// <param name="col">Имя колонки.</param>
    /// <returns>Строковое значение ячейки или <c>null</c>.</returns>
    public static string? GetString(this IReadOnlyDictionary<string, JsonElement> row, string col) =>
        row.TryGetValue(col, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    /// <summary>
    /// Извлекает из строки ISS значение указанной колонки как <see cref="decimal"/>.
    /// Числовые значения возвращаются напрямую, строковые — парсятся с помощью
    /// <see cref="CultureInfo.InvariantCulture"/>. Если колонка отсутствует или значение
    /// не удаётся преобразовать, возвращается <c>null</c>.
    /// </summary>
    /// <param name="row">Строка ISS в виде словаря «имя колонки — значение».</param>
    /// <param name="col">Имя колонки, значение которой нужно извлечь.</param>
    /// <returns>Значение колонки типа <see cref="decimal"/> или <c>null</c>, если колонка не найдена или значение нераспознаваемо.</returns>
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

    /// <summary>
    /// Извлекает из строки данных ISS значение указанного столбца в виде <see cref="long"/>.
    /// Поддерживает числовые и строковые JSON-значения.
    /// </summary>
    /// <param name="row">Строка данных ISS: словарь, ключом которого является имя столбца, а значением — JSON-элемент.</param>
    /// <param name="col">Имя столбца, значение которого требуется извлечь.</param>
    /// <returns>Значение столбца в виде <see cref="long"/>; <c>null</c>, если столбец отсутствует или его значение не удаётся преобразовать в <see cref="long"/>.</returns>
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

    /// <summary>
    /// Извлекает значение указанного столбца из строки данных ISS и преобразует его
    /// к типу <see cref="DateTime"/>, используя инвариантную культуру.
    /// </summary>
    /// <param name="row">Строка данных в виде словаря сопоставления имён столбцов и элементов JSON.</param>
    /// <param name="col">Имя столбца, содержащего значение даты и времени.</param>
    /// <returns>
    /// Значение <see cref="DateTime"/>, если строка найдена и успешно распознана;
    /// в противном случае — <c>null</c>.
    /// </returns>
    public static DateTime? GetDateTime(this IReadOnlyDictionary<string, JsonElement> row, string col)
    {
        var s = row.GetString(col);
        return s is not null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : null;
    }
}
