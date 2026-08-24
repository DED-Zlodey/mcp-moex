using System.Text.Json;
using MoexMcp.Infrastructure.Moex;

namespace MoexMcp.Tests.Infrastructure;

public class IssParserTests
{
    private static JsonDocument Doc(string json) => JsonDocument.Parse(json);

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

    [Fact]
    public void ParseBlock_MissingBlock_ReturnsEmpty()
    {
        using var doc = Doc("""{"other": {"columns": [], "data": []}}""");
        Assert.Empty(IssParser.ParseBlock(doc, "securities"));
    }

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

    [Fact]
    public void GetLong_ParsesVolume()
    {
        using var doc = Doc("""{ "b": { "columns": ["V"], "data": [[4167119]] } }""");
        var row = Assert.Single(IssParser.ParseBlock(doc, "b"));
        Assert.Equal(4167119L, row.GetLong("V"));
    }

    [Fact]
    public void GetDateTime_ParsesIssFormat()
    {
        using var doc = Doc("""{ "b": { "columns": ["T"], "data": [["2026-08-21 19:00:11"]] } }""");
        var row = Assert.Single(IssParser.ParseBlock(doc, "b"));
        Assert.Equal(new DateTime(2026, 8, 21, 19, 0, 11), row.GetDateTime("T"));
    }
}
