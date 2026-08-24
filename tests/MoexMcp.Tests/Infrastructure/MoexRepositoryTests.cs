using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MoexMcp.Infrastructure.Moex;

namespace MoexMcp.Tests.Infrastructure;

public class MoexRepositoryTests
{
    /// <summary>HttpClient с подменённым транспортом: отвечает заранее заданным JSON.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static MoexRepository Repo(Func<HttpRequestMessage, HttpResponseMessage> responder, out FakeHandler handler)
    {
        handler = new FakeHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://iss.moex.com/iss") };
        return new MoexRepository(http, NullLogger<MoexRepository>.Instance);
    }

    private const string SberQuoteJson = """
        {
          "securities": { "columns": ["SECID","SHORTNAME","PREVPRICE","MARKETPRICE"],
                          "data": [["SBER","Сбербанк",273.33,271.5]] },
          "marketdata": { "columns": ["SECID","LAST","CHANGE","LASTTOPREVPRICE","VOLTODAY","SYSTIME"],
                          "data": [["SBER",271.44,-1.89,-0.69,4167119,"2026-08-23 16:10:15"]] }
        }
        """;

    [Fact]
    public async Task GetQuote_ParsesRealIssShape()
    {
        var repo = Repo(_ => Json(SberQuoteJson), out _);

        var q = await repo.GetQuoteAsync("SBER");

        Assert.NotNull(q);
        Assert.Equal("SBER", q.Ticker);
        Assert.Equal("Сбербанк", q.Name);
        Assert.Equal(271.44m, q.Price);
        Assert.Equal(-1.89m, q.Change);
        Assert.Equal(-0.69m, q.ChangePercent);
        Assert.Equal(4167119L, q.Volume);
        Assert.Equal(new DateTime(2026, 8, 23, 16, 10, 15), q.Time);
    }

    [Fact]
    public async Task GetQuote_NoTradesToday_FallsBackToPrevPrice()
    {
        const string json = """
            {
              "securities": { "columns": ["SECID","SHORTNAME","PREVPRICE","MARKETPRICE"],
                              "data": [["SBER","Сбербанк",273.33,null]] },
              "marketdata": { "columns": ["SECID","LAST","CHANGE","LASTTOPREVPRICE","VOLTODAY","SYSTIME"],
                              "data": [] }
            }
            """;
        var repo = Repo(_ => Json(json), out _);

        var q = await repo.GetQuoteAsync("SBER");

        Assert.NotNull(q);
        Assert.Equal(273.33m, q.Price); // LAST и MARKETPRICE пусты → PREVPRICE
        Assert.Null(q.ChangePercent);
    }

    [Fact]
    public async Task GetQuote_UnknownTicker_ReturnsNull()
    {
        const string json = """
            {
              "securities": { "columns": ["SECID","SHORTNAME","PREVPRICE","MARKETPRICE"], "data": [] },
              "marketdata": { "columns": ["SECID"], "data": [] }
            }
            """;
        var repo = Repo(_ => Json(json), out _);

        Assert.Null(await repo.GetQuoteAsync("XXXX"));
    }

    [Fact]
    public async Task GetAllShareQuotes_JoinsMarketdataByTicker()
    {
        const string json = """
            {
              "securities": { "columns": ["SECID","SHORTNAME","PREVPRICE","MARKETPRICE"],
                              "data": [["SBER","Сбербанк",273.33,271.5],["GAZP","ГАЗПРОМ",180.0,179.5]] },
              "marketdata": { "columns": ["SECID","LAST","CHANGE","LASTTOPREVPRICE","VOLTODAY","SYSTIME"],
                              "data": [["GAZP",179.9,-0.1,-0.06,1000,"2026-08-21 10:00:00"],
                                       ["SBER",271.44,-1.89,-0.69,4167119,"2026-08-21 10:00:00"]] }
            }
            """;
        var repo = Repo(_ => Json(json), out _);

        var quotes = await repo.GetAllShareQuotesAsync();

        Assert.Equal(2, quotes.Count);
        var sber = quotes.Single(q => q.Ticker == "SBER");
        var gazp = quotes.Single(q => q.Ticker == "GAZP");
        Assert.Equal(271.44m, sber.Price);
        Assert.Equal(179.9m, gazp.Price); // джойн по тикеру, а не по порядку строк
    }

    [Fact]
    public async Task GetAllShareQuotes_PaginatesUntilShortPage()
    {
        // Первая страница ровно 100 строк → должен быть второй запрос со start=100
        var rows100 = string.Join(",", Enumerable.Range(1, 100).Select(i => $"""["T{i:000}","Name{i}",10,10]"""));
        var handler = new FakeHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            var data = url.Contains("start=100") ? """["T999","Last",5,5]""" : rows100;
            var page = """{"securities":{"columns":["SECID","SHORTNAME","PREVPRICE","MARKETPRICE"],"data":[""" + data +
                       """]},"marketdata":{"columns":["SECID","LAST","CHANGE","LASTTOPREVPRICE","VOLTODAY","SYSTIME"],"data":[]}}""";
            return Json(page);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://iss.moex.com/iss") };
        var repo = new MoexRepository(http, NullLogger<MoexRepository>.Instance);

        var quotes = await repo.GetAllShareQuotesAsync();

        Assert.Equal(101, quotes.Count);
        Assert.Equal(2, handler.RequestedUrls.Count);
        Assert.Contains("start=100", handler.RequestedUrls[1]);
    }

    [Fact]
    public async Task GetSiteNews_TruncatesToLimit()
    {
        var newsRows = string.Join(",", Enumerable.Range(1, 50).Select(i =>
            $"""[{i},"Новость {i}","site","2026-08-21 21:15:01"]"""));
        var repo = Repo(_ => Json("""{"sitenews":{"columns":["id","title","tag","modified_at"],"data":[""" + newsRows + "]}}"), out _);

        var news = await repo.GetSiteNewsAsync(3);

        Assert.Equal(3, news.Count); // ISS игнорирует limit — режем сами
        Assert.Equal("Новость 1", news[0].Title);
    }

    [Fact]
    public async Task SearchSecurities_MapsRows()
    {
        const string json = """
            { "securities": { "columns": ["secid","shortname","name"],
                              "data": [["GAZP","ГАЗПРОМ ао","\"Газпром\" (ПАО) ао"],["SIBN","Газпрнефть","Газпром нефть"]] } }
            """;
        var repo = Repo(_ => Json(json), out var handler);

        var found = await repo.SearchSecuritiesAsync("газпром");

        Assert.Equal(2, found.Count);
        Assert.Equal("GAZP", found[0].Ticker);
        Assert.Contains("q=", handler.RequestedUrls[0]);
    }

    [Fact]
    public async Task GetIndices_UsesCurrentValueNotLast()
    {
        const string json = """
            { "marketdata": { "columns": ["SECID","CURRENTVALUE","LASTCHANGE","SYSTIME"],
                              "data": [["IMOEX",2134.97,13.44,"2026-08-21 19:00:11"]] } }
            """;
        var repo = Repo(_ => Json(json), out _);

        var indices = await repo.GetIndicesAsync();

        Assert.Equal(2, indices.Count); // IMOEX + RTSI
        Assert.Equal(2134.97m, indices[0].Value);
        Assert.Equal(13.44m, indices[0].Change);
    }

    [Fact]
    public async Task GetCurrencyRates_FallsBackToMarketPriceOffSession()
    {
        const string json = """
            { "marketdata": { "columns": ["SECID","LAST","MARKETPRICE","CHANGE","SYSTIME"],
                              "data": [["EUR_RUB__TOM",null,96.7335,null,"2026-08-21 23:55:01"]] } }
            """;
        var repo = Repo(_ => Json(json), out _);

        var rates = await repo.GetCurrencyRatesAsync();

        Assert.Equal(2, rates.Count);
        Assert.Equal(96.7335m, rates[0].Price); // LAST пуст вне сессии → MARKETPRICE
    }

    [Fact]
    public async Task GetCandles_ParsesOhlcv()
    {
        const string json = """
            { "candles": { "columns": ["open","close","high","low","volume","begin"],
                           "data": [[271.48,271.48,271.48,271.48,2441,"2026-08-21 06:00:00"]] } }
            """;
        var repo = Repo(_ => Json(json), out _);

        var candles = await repo.GetCandlesAsync("SBER", 60, new DateTime(2026, 8, 21), new DateTime(2026, 8, 21));

        var c = Assert.Single(candles);
        Assert.Equal(271.48m, c.Open);
        Assert.Equal(2441L, c.Volume);
        Assert.Equal(new DateTime(2026, 8, 21, 6, 0, 0), c.Begin);
    }

    [Fact]
    public async Task GetPriceHistory_SkipsZeroCloses()
    {
        const string json = """
            { "history": { "columns": ["SECID","TRADEDATE","CLOSE"],
                           "data": [["SBER","2026-08-20",271.11],["SBER","2026-08-21",null]] } }
            """;
        var repo = Repo(_ => Json(json), out _);

        var history = await repo.GetPriceHistoryAsync("SBER", new DateTime(2026, 8, 20), new DateTime(2026, 8, 21));

        var p = Assert.Single(history); // запись с null-закрытием отброшена
        Assert.Equal(271.11m, p.Close);
    }

    [Fact]
    public async Task HttpFailure_ReturnsNullInsteadOfThrowing()
    {
        var repo = Repo(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("oops")
        }, out _);

        // GetStreamAsync на 500 бросает HttpRequestException — репозиторий должен вернуть null
        Assert.Null(await repo.GetQuoteAsync("SBER"));
    }
}
