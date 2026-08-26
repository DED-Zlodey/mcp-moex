using System.Text.Json;
using MoexMcp.Infrastructure.Moex;

namespace MoexMcp.Tests.Infrastructure;

public class IssParserTests
{
    /// <summary>
    /// Парсит строку JSON в объект <see cref="JsonDocument"/>.
    /// </summary>
    /// <param name="json">Строка, содержащая данные в формате JSON.</param>
    /// <return>Разобранный документ JSON, готовый для чтения элементов.</return>
    private static JsonDocument Doc(string json) => JsonDocument.Parse(json);

    /// <summary>
    /// Проверяет, что метод <see cref="IssParser.ParseBlock"/> корректно сопоставляет
    /// значения ячеек данных с именами столбцов в блоке ответа ISS.
    /// </summary>
    [Fact]
    public void ParseBlock_MapsCellsByColumnNames()
    {
        using var doc = Doc("""
            { "securities": {
                "columns": ["SECID", "SHORTNAME", "PREVPRICE"],
                "data": [["SBER", "Сбербанк", 273.33]]
            } }
            """);

        var rows = IssParser.ParseBlock(doc, "securities");

        var row = Assert.Single(rows);
        Assert.Equal("SBER", row.GetString("SECID"));
        Assert.Equal("Сбербанк", row.GetString("SHORTNAME"));
        Assert.Equal(273.33m, row.GetDecimal("PREVPRICE"));
    }

    /// <summary>
    /// Проверяет, что метод <see cref="MoexMcp.Infrastructure.Moex.IssParser.ParseBlock"/> извлекает значения
    /// по именам столбцов независимо от их порядка в массиве columns.
    /// </summary>
    [Fact]
    public void ParseBlock_ColumnOrderDoesNotMatter()
    {
        // Тот же смысл, но колонки переставлены — парсер обязан вытащить по именам
        using var doc = Doc("""
            { "securities": {
                "columns": ["PREVPRICE", "SECID"],
                "data": [[273.33, "SBER"]]
            } }
            """);

        var row = Assert.Single(IssParser.ParseBlock(doc, "securities"));
        Assert.Equal("SBER", row.GetString("SECID"));
        Assert.Equal(273.33m, row.GetDecimal("PREVPRICE"));
    }

    /// <summary>
    /// Проверяет, что метод <see cref="IssParser.ParseBlock"/> возвращает пустой список,
    /// если запрашиваемый блок отсутствует в документе JSON.
    /// </summary>
    [Fact]
    public void ParseBlock_MissingBlock_ReturnsEmpty()
    {
        using var doc = Doc("""{"other": {"columns": [], "data": []}}""");
        Assert.Empty(IssParser.ParseBlock(doc, "securities"));
    }

    /// <summary>
    /// Проверяет, что метод <see cref="IssParser.GetDecimal(IReadOnlyDictionary{string, JsonElement}, string)"/>
    /// корректно обрабатывает отсутствующее значение, числовое значение и число, представленное строкой.
    /// </summary>
    [Fact]
    public void GetDecimal_HandlesNullNumberAndString()
    {
        using var doc = Doc("""
            { "b": { "columns": ["A", "B", "C"], "data": [[null, 12.5, "7.25"]] } }
            """);
        var row = Assert.Single(IssParser.ParseBlock(doc, "b"));

        Assert.Null(row.GetDecimal("A"));
        Assert.Equal(12.5m, row.GetDecimal("B"));
        Assert.Equal(7.25m, row.GetDecimal("C")); // ISS иногда отдаёт числа строками
        Assert.Null(row.GetDecimal("MISSING"));
    }

    /// <summary>
    /// Проверяет, что метод <see cref="IssParser.GetLong"/> корректно разбирает целочисленное значение объёма из блока данных ISS.
    /// </summary>
    [Fact]
    public void GetLong_ParsesVolume()
    {
        using var doc = Doc("""{ "b": { "columns": ["V"], "data": [[4167119]] } }""");
        var row = Assert.Single(IssParser.ParseBlock(doc, "b"));
        Assert.Equal(4167119L, row.GetLong("V"));
    }

    /// <summary>
    /// Проверяет, что метод <see cref="IssParser.GetDateTime"/> корректно разбирает дату и время, представленные в формате ISS.
    /// </summary>
    /// <remarks>
    /// Создаёт JSON-документ с блоком, содержащим столбец "T" и строку данных "2026-08-21 19:00:11",
    /// после чего убеждается, что возвращаемое значение соответствует 21 августа 2026 года 19:00:11.
    /// </remarks>
    [Fact]
    public void GetDateTime_ParsesIssFormat()
    {
        using var doc = Doc("""{ "b": { "columns": ["T"], "data": [["2026-08-21 19:00:11"]] } }""");
        var row = Assert.Single(IssParser.ParseBlock(doc, "b"));
        Assert.Equal(new DateTime(2026, 8, 21, 19, 0, 11), row.GetDateTime("T"));
    }
}
