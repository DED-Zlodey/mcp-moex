using System.Text.Json;
using MoexMcp.Domain.Models;

namespace MoexMcp.Tests.Domain;

public class QuoteSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void OldSnapshotJson_DeserializesAsShare()
    {
        // Формат снапшотов Redis до появления классов активов — без Class/PriceUnit/Yield/AccruedInterest
        const string oldJson = """
            [{"ticker":"SBER","name":"Сбербанк","price":271.44,"change":-1.89,"changePercent":-0.69,"volume":4167119,"time":"2026-08-21T16:10:15"}]
            """;

        var quotes = JsonSerializer.Deserialize<List<Quote>>(oldJson, JsonOptions);

        var q = Assert.Single(quotes!);
        Assert.Equal("SBER", q.Ticker);
        Assert.Equal(271.44m, q.Price);
        Assert.Equal(AssetClass.Share, q.Class); // default — обратная совместимость
        Assert.Null(q.PriceUnit);
        Assert.Null(q.Yield);
        Assert.Null(q.AccruedInterest);
    }

    [Fact]
    public void NewQuote_RoundTrips_BondFields()
    {
        var bond = new Quote("SU26243RMFS4", "ОФЗ 26243", 69.5m, -0.3m, -0.47m, 77244,
            new DateTime(2026, 8, 24, 10, 0, 0), AssetClass.Bond, "% номинала", 16.11m, 22.29m);

        var json = JsonSerializer.Serialize(new[] { bond }, JsonOptions);
        var quotes = JsonSerializer.Deserialize<List<Quote>>(json, JsonOptions);

        var q = Assert.Single(quotes!);
        Assert.Equal(AssetClass.Bond, q.Class);
        Assert.Equal("% номинала", q.PriceUnit);
        Assert.Equal(16.11m, q.Yield);
        Assert.Equal(22.29m, q.AccruedInterest);
    }
}
