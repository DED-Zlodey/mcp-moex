using System.Text.Json;
using MoexMcp.Domain.Models;

namespace MoexMcp.Tests.Domain;

public class QuoteSerializationTests
{
    /// <summary>
    /// Параметры сериализации JSON, используемые в тестах для проверки десериализации котировок
    /// и круговой сериализации объектов <see cref="Quote"/>.
    /// Настроены с применением веб-умолчаний (<see cref="JsonSerializerDefaults.Web"/>),
    /// что обеспечивает согласованное поведение при разборе данных в формате camelCase,
    /// поддержку устаревших снапшотов без полей Class, PriceUnit, Yield и AccruedInterest
    /// и сохранение полноценных bond-полей при сериализации и десериализации.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Проверяет десериализацию старого формата JSON котировок, который не содержит полей Class, PriceUnit, Yield и AccruedInterest.
    /// Убеждается, что создаётся объект <see cref="Quote"/> с классом актива <see cref="AssetClass.Share"/> по умолчанию,
    /// а отсутствующие поля PriceUnit, Yield и AccruedInterest имеют значение null, что обеспечивает обратную совместимость.
    /// </summary>
    [Fact]
    public void OldQuoteJson_DeserializesAsShare()
    {
        // Формат котировок до появления классов активов — без Class/PriceUnit/Yield/AccruedInterest
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

    /// <summary>
    /// Проверяет корректную круговую сериализацию котировки облигации
    /// с сохранением специфичных полей: класса актива <see cref="AssetClass.Bond"/>,
    /// единицы измерения цены, доходности и накопленного купонного дохода.
    /// </summary>
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
