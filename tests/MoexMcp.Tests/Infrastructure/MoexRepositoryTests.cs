using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MoexMcp.Domain.Models;
using MoexMcp.Infrastructure.Moex;

namespace MoexMcp.Tests.Infrastructure;

/// <summary>
/// Набор модульных тестов для <see cref="MoexRepository"/>, проверяющих разбор ответов ISS МосБиржи, маршрутизацию по классам активов, обработку нестандартных сценариев и постраничную выборку.
/// </summary>
/// <remarks>
/// Покрывает получение котировок акций, облигаций, валют и металлов, исторических свечей, дневных закрытий, новостей и поиска инструментов.
/// </remarks>
public class MoexRepositoryTests
{
    /// <summary>
    /// HttpClient с подменённым транспортом: отвечает заранее заданным JSON.
    /// </summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        /// <summary>
        /// Возвращает список URL-адресов HTTP-запросов, обработанных данным обработчиком.
        /// </summary>
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());
            return Task.FromResult(responder(request));
        }
    }

    /// <summary>
    /// Создаёт HTTP-ответ со статусом 200 OK и телом в формате JSON, закодированным в UTF-8.
    /// </summary>
    /// <param name="body">Строка с содержимым JSON.</param>
    /// <return>HTTP-ответ <see cref="HttpResponseMessage"/> со статусом OK и типом содержимого application/json.</return>
    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    /// <summary>
    /// Создаёт экземпляр <see cref="MoexRepository"/> с поддельным HTTP-обработчиком для использования в тестах.
    /// </summary>
    /// <param name="responder">Функция, формирующая HTTP-ответ на основе входящего запроса.</param>
    /// <param name="handler">Выходной параметр, возвращающий созданный поддельный обработчик сообщений.</param>
    /// <return>Настроенный экземпляр <see cref="MoexRepository"/> с подменённым <see cref="HttpClient"/>.</return>
    private static MoexRepository Repo(Func<HttpRequestMessage, HttpResponseMessage> responder, out FakeHandler handler)
    {
        handler = new FakeHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://iss.moex.com/iss") };
        return new MoexRepository(http, NullLogger<MoexRepository>.Instance);
    }

    /// <summary>
    /// JSON-тело ответа Московской биржи с данными котировки акций Сбербанка, используемое в тестах репозитория.
    /// </summary>
    private const string SberQuoteJson = """
                                         {
                                           "securities": { "columns": ["SECID","SHORTNAME","PREVPRICE","MARKETPRICE"],
                                                           "data": [["SBER","Сбербанк",273.33,271.5]] },
                                           "marketdata": { "columns": ["SECID","LAST","CHANGE","LASTTOPREVPRICE","VOLTODAY","SYSTIME"],
                                                           "data": [["SBER",271.44,-1.89,-0.69,4167119,"2026-08-23 16:10:15"]] }
                                         }
                                         """;

    /// <summary>
    /// Проверяет, что <see cref="MoexRepository.GetQuoteAsync"/> корректно разбирает типичный ответ ISS МосБиржи для акции и заполняет все поля котировки.
    /// </summary>
    /// <return>Задача, представляющая выполнение асинхронного теста.</return>
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

    /// <summary>
    /// Проверяет сценарий, когда по инструменту сегодня отсутствуют сделки: блок <c>marketdata</c> пуст, а поля <c>LAST</c> и <c>MARKETPRICE</c> не заполнены. Убеждается, что <see cref="MoexRepository.GetQuoteAsync(string, AssetClass, CancellationToken)"/> использует значение <c>PREVPRICE</c> в качестве цены, а изменение в процентах остаётся неопределённым.
    /// </summary>
    /// <return>Результат асинхронного теста. Утверждает, что возвращаемая котировка не равна <c>null</c>, её цена равна <c>273.33m</c>, а свойство <c>ChangePercent</c> равно <c>null</c>.</return>
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

    /// <summary>
    /// Проверяет, что метод <see cref="MoexRepository.GetQuoteAsync"/> возвращает <c>null</c>,
    /// если указанный тикер отсутствует в данных MOEX ISS.
    /// </summary>
    /// <return>Задача, представляющая результат проверки возврата <c>null</c> для неизвестного тикера.</return>
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

    /// <summary>
    /// Проверяет, что метод <see cref="MoexRepository.GetAllShareQuotesAsync"/> объединяет данные секций <c>securities</c> и <c>marketdata</c> по тикеру <c>SECID</c>, а не по порядку строк в ответе MOEX ISS.
    /// </summary>
    /// <return>Задача, представляющая выполнение асинхронного теста.</return>
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

    /// <summary>
    /// Проверяет, что <see cref="MoexRepository.GetAllShareQuotesAsync"/> выполняет постраничную загрузку акций до тех пор, пока не будет получена последняя страница с меньшим, чем размер страницы, числом строк.
    /// </summary>
    /// <return>Задача, представляющая асинхронную операцию выполнения теста.</return>
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

    /// <summary>
    /// Проверяет, что метод <see cref="MoexRepository.GetSiteNewsAsync(int, CancellationToken)"/>
    /// самостоятельно обрезает список новостей до запрошенного лимита,
    /// если внешний ISS API игнорирует параметр limit и возвращает больше записей.
    /// </summary>
    /// <return>Задача, представляющая результат выполнения асинхронного теста.</return>
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

    /// <summary>
    /// Проверяет, что поиск ценных бумаг по запросу корректно преобразует строки ответа ISS в список объектов <see cref="SecurityInfo"/> и формирует URL с поисковым параметром.
    /// </summary>
    /// <return>Задача <see cref="Task"/>, представляющая выполнение асинхронного теста.</return>
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

    /// <summary>
    /// Проверяет, что <see cref="MoexRepository.GetIndicesAsync"/> использует поле CURRENTVALUE
    /// для получения текущего значения индекса и поле LASTCHANGE для изменения, а не LAST.
    /// </summary>
    /// <return>Задача, представляющая асинхронную операцию выполнения теста.</return>
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

    /// <summary>
    /// Проверяет, что при получении курсов валют вне торговой сессии, когда поле LAST отсутствует, репозиторий использует значение MARKETPRICE.
    /// </summary>
    /// <return>Задача, представляющая результат выполнения асинхронного теста.</return>
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

    /// <summary>
    /// Проверяет, что метод <see cref="MoexRepository.GetCandlesAsync"/> корректно разбирает поля OHLCV из ответа ISS.
    /// </summary>
    /// <return>Задача, представляющая асинхронную операцию выполнения теста.</return>
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

    /// <summary>
    /// Проверяет, что метод <see cref="MoexRepository.GetPriceHistoryAsync"/>
    /// исключает из результата записи с нулевым или null значением цены закрытия.
    /// </summary>
    /// <return>Задача <see cref="Task"/>, представляющая асинхронно выполняемый модульный тест.</return>
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

    /// <summary>
    /// Проверяет, что при HTTP-сбое с кодом 500 Internal Server Error репозиторий возвращает null вместо генерации исключения.
    /// </summary>
    /// <return>Задача <see cref="Task"/>, представляющая асинхронную операцию выполнения теста.</return>
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

    /// <summary>
    /// Тестовый JSON-ответ Московской биржи, содержащий разделы securities и marketdata, используемый для проверки парсинга котировок облигаций.
    /// </summary>
    private const string BondQuoteJson = """
                                         {
                                           "securities": { "columns": ["SECID","BOARDID","SHORTNAME","PREVPRICE","ACCRUEDINT"],
                                                           "data": [["SU26243RMFS4","SPOB","ОФЗ 26243",null,22.29],
                                                                    ["SU26243RMFS4","TQOB","ОФЗ 26243",69.854,22.29]] },
                                           "marketdata": { "columns": ["SECID","BOARDID","LAST","CHANGE","LASTTOPREVPRICE","VOLTODAY","YIELD","MARKETPRICE","SYSTIME"],
                                                           "data": [["SU26243RMFS4","SPOB",null,null,0,0,0,69.593,"2026-08-24 09:15:12"],
                                                                    ["SU26243RMFS4","TQOB",69.527,-0.327,-0.47,77244,16.11,69.593,"2026-08-24 10:02:59"]] }
                                         }
                                         """;

    /// <summary>
    /// Проверяет, что метод <see cref="MoexRepository.GetBondQuoteAsync"/> при получении котировки облигации
    /// отдаёт предпочтение приоритетной торговой площадке, извлекает доходность из marketdata
    /// и накопленный купонный доход из securities.
    /// </summary>
    /// <return>Задача <see cref="Task"/>, представляющая асинхронную операцию выполнения теста.</return>
    [Fact]
    public async Task GetBondQuote_PicksPriorityBoardAndParsesYieldAndAccruedInt()
    {
        var repo = Repo(_ => Json(BondQuoteJson), out _);

        var q = await repo.GetBondQuoteAsync("SU26243RMFS4");

        Assert.NotNull(q);
        Assert.Equal("ОФЗ 26243", q.Name);
        Assert.Equal(69.527m, q.Price);              // LAST приоритетного board TQOB, а не SPOB
        Assert.Equal(16.11m, q.Yield);               // YIELD — из marketdata
        Assert.Equal(22.29m, q.AccruedInterest);     // НКД — из securities
        Assert.Equal(AssetClass.Bond, q.Class);
        Assert.Equal("% номинала", q.PriceUnit);
        Assert.Equal(77244L, q.Volume);
    }

    /// <summary>
    /// Проверяет, что при запросе котировки облигации вне торговой сессии,
    /// когда отсутствует значение LAST, метод <see cref="MoexRepository.GetBondQuoteAsync"/>
    /// корректно использует значение MARKETPRICE.
    /// </summary>
    /// <return>Задача, представляющая асинхронно выполняемый тест.</return>
    [Fact]
    public async Task GetBondQuote_OffSession_FallsBackToMarketPrice()
    {
        const string json = """
            {
              "securities": { "columns": ["SECID","BOARDID","SHORTNAME","PREVPRICE","ACCRUEDINT"],
                              "data": [["SU26243RMFS4","TQOB","ОФЗ 26243",69.854,22.29]] },
              "marketdata": { "columns": ["SECID","BOARDID","LAST","CHANGE","LASTTOPREVPRICE","VOLTODAY","YIELD","MARKETPRICE","SYSTIME"],
                              "data": [["SU26243RMFS4","TQOB",null,null,null,null,16.11,69.593,"2026-08-24 09:15:12"]] }
            }
            """;
        var repo = Repo(_ => Json(json), out _);

        var q = await repo.GetBondQuoteAsync("SU26243RMFS4");

        Assert.NotNull(q);
        Assert.Equal(69.593m, q.Price); // LAST пуст вне сессии → MARKETPRICE
    }

    /// <summary>
    /// Проверяет, что при передаче неизвестного тикера метод <see cref="GetBondQuote"/> возвращает <c>null</c>.
    /// </summary>
    /// <returns>Результат проверки, подтверждающий возврат <c>null</c> для неизвестного тикера.</returns>
    [Fact]
    public async Task GetBondQuote_UnknownTicker_ReturnsNull()
    {
        const string json = """
            {
              "securities": { "columns": ["SECID","BOARDID"], "data": [] },
              "marketdata": { "columns": ["SECID","BOARDID"], "data": [] }
            }
            """;
        var repo = Repo(_ => Json(json), out _);

        Assert.Null(await repo.GetBondQuoteAsync("XXXX"));
    }

    /// <summary>
    /// Получает все котировки облигаций, объединяя данные с режимов торгов TQCB и TQOB.
    /// </summary>
    /// <return>Коллекция котировок облигаций, сформированная в результате слияния данных с режимов TQCB и TQOB.</return>
    [Fact]
    public async Task GetAllBondQuotes_MergesTqcbAndTqob()
    {
        var repo = Repo(req =>
        {
            var url = req.RequestUri!.ToString();
            var ticker = url.Contains("TQCB") ? "RU000A10AAA1" : "SU26243RMFS4";
            var json = """{"securities":{"columns":["SECID","SHORTNAME","PREVPRICE","MARKETPRICE","ACCRUEDINT"],"data":[[""" +
                       $"\"{ticker}\",\"Bond\",70.0,69.9,10.5]" +
                       """]},"marketdata":{"columns":["SECID","LAST","CHANGE","LASTTOPREVPRICE","VOLTODAY","YIELD","MARKETPRICE","SYSTIME"],"data":[[""" +
                       $"\"{ticker}\",70.1,0.1,0.14,100,15.5,70.0,\"2026-08-24 10:00:00\"]" +
                       "]}}";
            return Json(json);
        }, out var handler);

        var bonds = await repo.GetAllBondQuotesAsync();

        Assert.Equal(2, bonds.Count);
        Assert.Contains(bonds, b => b.Ticker == "RU000A10AAA1");
        Assert.Contains(bonds, b => b.Ticker == "SU26243RMFS4");
        Assert.All(bonds, b => Assert.Equal(AssetClass.Bond, b.Class));
        Assert.Contains(handler.RequestedUrls, u => u.Contains("bonds/boards/TQCB"));
        Assert.Contains(handler.RequestedUrls, u => u.Contains("bonds/boards/TQOB"));
    }

    /// <summary>
    /// Проверяет, что при получении цен на металлы вне торговой сессии используется значение MARKETPRICE, если поле LAST пустое.
    /// </summary>
    /// <return>Задача, представляющая асинхронную операцию выполнения теста.</return>
    [Fact]
    public async Task GetMetalPrices_FallsBackToMarketPriceOffSession()
    {
        var repo = Repo(req =>
        {
            var url = req.RequestUri!.ToString();
            var ticker = url.Contains("GLDRUB") ? "GLDRUB_TOM" : "SLVRUB_TOM";
            return Json("""{"marketdata":{"columns":["SECID","LAST","MARKETPRICE","CHANGE","SYSTIME"],"data":[[""" +
                        $"\"{ticker}\",null,12138.56,null,\"2026-08-24 09:41:44\"]" +
                        "]}}");
        }, out _);

        var metals = await repo.GetMetalPricesAsync();

        Assert.Equal(2, metals.Count);
        Assert.Equal("GLDRUB_TOM", metals[0].Ticker);
        Assert.Equal("Золото", metals[0].Name);
        Assert.Equal(12138.56m, metals[0].Price); // LAST пуст до 10:00 МСК → MARKETPRICE
    }

    /// <summary>
    /// Проверяет, что метод <see cref="MoexRepository.GetCandlesAsync"/> формирует URL запроса свечей
    /// в зависимости от класса актива и не использует сегмент <c>/boards/</c>.
    /// </summary>
    /// <param name="assetClass">Класс актива, определяющий целевой рынок MOEX.</param>
    /// <param name="expectedRoute">Ожидаемый сегмент маршрута в сформированном URL.</param>
    /// <return>Задача, представляющая асинхронную операцию проверки.</return>
    [Theory]
    [InlineData(AssetClass.Share, "engines/stock/markets/shares")]
    [InlineData(AssetClass.Bond, "engines/stock/markets/bonds")]
    [InlineData(AssetClass.Currency, "engines/currency/markets/selt")]
    [InlineData(AssetClass.Metal, "engines/currency/markets/selt")]
    public async Task GetCandles_RoutesByAssetClass(AssetClass assetClass, string expectedRoute)
    {
        const string json = """
            { "candles": { "columns": ["open","close","high","low","volume","begin"], "data": [] } }
            """;
        var repo = Repo(_ => Json(json), out var handler);

        await repo.GetCandlesAsync("X", 60, new DateTime(2026, 8, 21), new DateTime(2026, 8, 21), assetClass);

        Assert.Single(handler.RequestedUrls);
        Assert.Contains(expectedRoute, handler.RequestedUrls[0]);
        Assert.DoesNotContain("/boards/", handler.RequestedUrls[0]); // свечи работают без board
    }

    /// <summary>
    /// Проверяет, что запрос истории цен облигации дедуплицирует записи по дате:
    /// при наличии нескольких строк с разными режимами торгов за один день
    /// выбирается приоритетная площадка (TQOB вместо SPOB),
    /// а в результате остаётся одна запись на дату.
    /// Также проверяется, что для облигаций используется рыночный endpoint истории без сегмента board.
    /// </summary>
    /// <return>Задача, представляющая выполненный асинхронный тест.</return>
    [Fact]
    public async Task GetPriceHistory_Bond_DedupesBoardsByDate()
    {
        const string json = """
            { "history": { "columns": ["SECID","BOARDID","TRADEDATE","CLOSE"],
                           "data": [["SU26243RMFS4","SPOB","2026-08-21",69.1],
                                    ["SU26243RMFS4","TQOB","2026-08-21",69.854],
                                    ["SU26243RMFS4","TQOB","2026-08-22",69.9]] } }
            """;
        var repo = Repo(_ => Json(json), out var handler);

        var history = await repo.GetPriceHistoryAsync("SU26243RMFS4", new DateTime(2026, 8, 21), new DateTime(2026, 8, 22), AssetClass.Bond);

        Assert.Equal(2, history.Count);
        Assert.Equal(69.854m, history[0].Close); // на дату одна запись — приоритетный TQOB
        Assert.Contains("history/engines/stock/markets/bonds", handler.RequestedUrls[0]);
        Assert.DoesNotContain("/boards/", handler.RequestedUrls[0]); // история облигаций — без board
    }

    /// <summary>
    /// Проверяет, что при запросе дневных закрытий для акций репозиторий обращается к доске TQBR с указанием даты в URL.
    /// </summary>
    /// <return>Задача, завершающая выполнение теста.</return>
    [Fact]
    public async Task GetMarketDailyCloses_Share_QueriesBoardWithDateParam()
    {
        const string json = """
            { "history": { "columns": ["SECID","TRADEDATE","CLOSE"],
                           "data": [["SBER","2026-08-21",271.11],["GAZP","2026-08-21",179.9],["DEAD","2026-08-21",null]] } }
            """;
        var repo = Repo(_ => Json(json), out var handler);

        var closes = await repo.GetMarketDailyClosesAsync(new DateTime(2026, 8, 21));

        Assert.Equal(2, closes.Count); // null-закрытие отброшено
        Assert.Equal(271.11m, closes.Single(c => c.Ticker == "SBER").Close);
        Assert.Contains("history/engines/stock/markets/shares/boards/TQBR/securities.json", handler.RequestedUrls[0]);
        Assert.Contains("date=2026-08-21", handler.RequestedUrls[0]);
    }

    /// <summary>
    /// Проверяет, что при запросе дневных цен закрытия облигаций репозиторий обращается к торговым площадкам TQCB и TQOB, а при совпадении инструмента на обеих площадках сохраняется значение с площадки TQOB.
    /// </summary>
    /// <return>Задача, завершающаяся проверкой наличия единственной записи облигации с ценой закрытия из TQOB и подтверждением запроса обоих URL площадок.</return>
    [Fact]
    public async Task GetMarketDailyCloses_Bond_QueriesBothBoardsAndTqobWinsOnDup()
    {
        var repo = Repo(req =>
        {
            var url = req.RequestUri!.ToString();
            // Тикер есть на обоих board'ах — должна победить запись TQOB (она опрашивается последней)
            var close = url.Contains("TQCB") ? "69.1" : "69.854";
            return Json("""{"history":{"columns":["SECID","TRADEDATE","CLOSE"],"data":[["SU26243RMFS4","2026-08-21",""" + close + "]]}}");
        }, out var handler);

        var closes = await repo.GetMarketDailyClosesAsync(new DateTime(2026, 8, 21), AssetClass.Bond);

        var bond = Assert.Single(closes);
        Assert.Equal(69.854m, bond.Close);
        Assert.Contains(handler.RequestedUrls, u => u.Contains("bonds/boards/TQCB"));
        Assert.Contains(handler.RequestedUrls, u => u.Contains("bonds/boards/TQOB"));
    }

    /// <summary>
    /// Проверяет, что при запросе дневных цен закрытия для металлов
    /// репозиторий возвращает только известные металлические тикеры,
    /// а валютные пары с доски CETS фильтруются.
    /// </summary>
    /// <return>Задача <see cref="Task"/>, представляющая асинхронную операцию теста.</return>
    [Fact]
    public async Task GetMarketDailyCloses_Metal_KeepsOnlyKnownMetalTickers()
    {
        const string json = """
            { "history": { "columns": ["SECID","TRADEDATE","CLOSE"],
                           "data": [["USD000UTSTOM","2026-08-21",80.1],["GLDRUB_TOM","2026-08-21",12138.56]] } }
            """;
        var repo = Repo(_ => Json(json), out var handler);

        var metals = await repo.GetMarketDailyClosesAsync(new DateTime(2026, 8, 21), AssetClass.Metal);

        Assert.Equal("GLDRUB_TOM", Assert.Single(metals).Ticker); // валютные пары CETS отфильтрованы
        Assert.Contains("currency/markets/selt/boards/CETS", handler.RequestedUrls[0]);
    }

    /// <summary>
    /// Проверяет, что метод <see cref="MoexRepository.GetMarketDailyClosesAsync"/> выполняет постраничную загрузку исторических цен закрытия до тех пор, пока не встретит неполную страницу.
    /// </summary>
    /// <return>Задача <see cref="Task"/>, представляющая результат выполнения асинхронного теста.</return>
    [Fact]
    public async Task GetMarketDailyCloses_PaginatesUntilShortPage()
    {
        var rows100 = string.Join(",", Enumerable.Range(1, 100).Select(i => $"""["T{i:000}","2026-08-21",10]"""));
        var repo = Repo(req =>
        {
            var url = req.RequestUri!.ToString();
            var data = url.Contains("start=100") ? """["T999","2026-08-21",5]""" : rows100;
            return Json("""{"history":{"columns":["SECID","TRADEDATE","CLOSE"],"data":[""" + data + "]}}");
        }, out var handler);

        var closes = await repo.GetMarketDailyClosesAsync(new DateTime(2026, 8, 21));

        Assert.Equal(101, closes.Count);
        Assert.Equal(2, handler.RequestedUrls.Count);
        Assert.Contains("start=100", handler.RequestedUrls[1]);
    }
}
