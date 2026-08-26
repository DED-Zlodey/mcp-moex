using Microsoft.Extensions.Logging.Abstractions;
using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;
using MoexMcp.Host.Tools;

namespace MoexMcp.Tests.Host;

internal class FakeMarketDataService : IMarketDataService
{
    /// <summary>
    /// Котировка акции, используемая в тестовом имитационном сервисе рыночных данных для возврата результата запроса информации об акции.
    /// </summary>
    public Quote? Quote { get; set; }

    /// <summary>
    /// Котировка облигации, используемая тестовым фейковым сервисом рыночных данных для возврата результата запроса информации о заданной облигации.
    /// </summary>
    public Quote? BondQuote { get; set; }

    /// <summary>
    /// Коллекция котировок инструментов, используемая в тестовом имитационном сервисе рыночных данных для возврата результатов запросов топов растущих и падающих акций и облигаций.
    /// </summary>
    public IReadOnlyList<Quote> Quotes { get; set; } = [];

    /// <summary>
    /// Возвращает или задает коллекцию цен на драгоценные металлы (золото и серебро), торгуемые на MOEX, в рублях за грамм.
    /// </summary>
    public IReadOnlyList<MetalPrice> Metals { get; set; } = [];

    /// <summary>
    /// Список ценных бумаг, используемый для имитации результатов поиска акций.
    /// </summary>
    private IReadOnlyList<SecurityInfo> Securities { get; set; } = [];

    /// <summary>
    /// Коллекция новостей с сайта MOEX, используемая для имитации результатов вызова <see cref="IMarketDataService.GetNewsAsync"/>.
    /// </summary>
    private IReadOnlyList<SiteNewsItem> News { get; set; } = [];

    /// <summary>
    /// Коллекция валютных курсов, используемая для имитации данных сервиса рыночных данных.
    /// </summary>
    private IReadOnlyList<CurrencyRate> CurrencyRates { get; set; } = [];

    public Task<Quote?> GetStockInfoAsync(string ticker, CancellationToken ct = default) => Task.FromResult(Quote);
    public Task<Quote?> GetBondInfoAsync(string ticker, CancellationToken ct = default) => Task.FromResult(BondQuote);
    public Task<IReadOnlyList<MetalPrice>> GetMetalPricesAsync(CancellationToken ct = default) => Task.FromResult(Metals);
    public Task<IReadOnlyList<Quote>> GetTopGainersAsync(int limit, CancellationToken ct = default) => Task.FromResult(Quotes);
    public Task<IReadOnlyList<Quote>> GetTopLosersAsync(int limit, CancellationToken ct = default) => Task.FromResult(Quotes);
    public Task<IReadOnlyList<Quote>> GetTopBondGainersAsync(int limit, CancellationToken ct = default) => Task.FromResult(Quotes);
    public Task<IReadOnlyList<Quote>> GetTopBondLosersAsync(int limit, CancellationToken ct = default) => Task.FromResult(Quotes);
    public Task<IReadOnlyList<SecurityInfo>> SearchStocksAsync(string query, CancellationToken ct = default) => Task.FromResult(Securities);
    public Task<IReadOnlyList<SiteNewsItem>> GetNewsAsync(int limit, CancellationToken ct = default) => Task.FromResult(News);
    public Task<IReadOnlyList<IndexQuote>> GetIndicesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IndexQuote>>([]);
    public Task<IReadOnlyList<CurrencyRate>> GetCurrencyRatesAsync(CancellationToken ct = default) => Task.FromResult(CurrencyRates);
}

internal class FakeComparisonService : IComparisonService
{
    /// <summary>
    /// Результат сравнения доходности инструментов за период, который будет возвращён при вызове метода сравнения.
    /// </summary>
    public IReadOnlyList<InstrumentPerformance> CompareResult { get; set; } = [];

    /// <summary>
    /// Результат ранжирования инструментов по доходности, возвращаемый фиктивной реализацией сервиса сравнения.
    /// </summary>
    public IReadOnlyList<InstrumentPerformance>? RankResult { get; set; } = [];

    /// <summary>
    /// Последний набор тикеров, переданных в метод сравнения инструментов, используемый в тестовом имитационном сервисе для проверки обработки входных параметров.
    /// </summary>
    public IReadOnlyList<string>? LastTickers { get; private set; }

    /// <summary>
    /// Последний класс актива, переданный в методы сравнения или ранжирования инструментов.
    /// </summary>
    public AssetClass? LastAssetClass { get; private set; }

    public Task<IReadOnlyList<InstrumentPerformance>> CompareInstrumentsAsync(
        IReadOnlyList<string> tickers, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        LastTickers = tickers;
        LastAssetClass = assetClass;
        return Task.FromResult(CompareResult);
    }

    public Task<IReadOnlyList<InstrumentPerformance>?> RankByPerformanceAsync(
        DateTime from, DateTime to, int limit, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        LastAssetClass = assetClass;
        return Task.FromResult(RankResult);
    }
}

internal class FakeHistoryService : IHistoryService
{
    /// <summary>
    /// Коллекция OHLC-свечей, используемая в тестовом фейковом сервисе исторических данных для имитации результата запроса свечей.
    /// </summary>
    public IReadOnlyList<Candle> Candles { get; set; } = [];

    /// <summary>
    /// История дневных цен закрытия, используемая в тестовом имитационном сервисе исторических данных для возврата результата запроса ценовой истории.
    /// </summary>
    public IReadOnlyList<DailyPrice> Prices { get; set; } = [];

    /// <summary>
    /// Последний использованный класс актива, переданный в методы получения свечей или истории цен.
    /// </summary>
    public AssetClass? LastAssetClass { get; private set; }

    public Task<IReadOnlyList<Candle>> GetCandlesAsync(string ticker, int intervalMinutes, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        LastAssetClass = assetClass;
        return Task.FromResult(Candles);
    }

    public Task<IReadOnlyList<DailyPrice>> GetPriceHistoryAsync(string ticker, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        LastAssetClass = assetClass;
        return Task.FromResult(Prices);
    }
}

public class MoexMarketToolsTests
{
    /// <summary>
    /// Создаёт экземпляр <see cref="MoexMarketTools"/> для использования в тестах с подставным сервисом рыночных данных.
    /// </summary>
    /// <param name="market">Подставной сервис рыночных данных.</param>
    /// <returns>Новый экземпляр <see cref="MoexMarketTools"/>, сконфигурированный для тестового окружения.</returns>
    private static MoexMarketTools Tools(FakeMarketDataService market) =>
        new(market, NullLogger<MoexMarketTools>.Instance);

    /// <summary>
    /// Проверяет, что метод <see cref="MoexMarketTools.GetStockInfo"/> корректно форматирует данные котировки акции — тикер, название, текущую цену и процентное изменение.
    /// </summary>
    /// <return>
    /// Задача <see cref="Task"/>, представляющая завершение асинхронной проверки.
    /// </return>
    [Fact]
    public async Task GetStockInfo_FormatsQuote()
    {
        var market = new FakeMarketDataService
        {
            Quote = new Quote("SBER", "Сбербанк", 271.44m, -1.89m, -0.69m, 4167119, new DateTime(2026, 8, 21, 16, 10, 15))
        };

        var text = await Tools(market).GetStockInfo("SBER");

        Assert.Contains("SBER", text);
        Assert.Contains("Сбербанк", text);
        Assert.Contains("271,44", text);
        Assert.Contains("-0,69", text);
    }

    /// <summary>
    /// Проверяет, что при запросе несуществующего тикера метод <see cref="MoexMarketTools.GetStockInfo"/> возвращает понятное сообщение о том, что акция не найдена.
    /// </summary>
    /// <returns>Задача, представляющая результат выпо��нения теста.</returns>
    [Fact]
    public async Task GetStockInfo_UnknownTicker_FriendlyMessage()
    {
        var text = await Tools(new FakeMarketDataService()).GetStockInfo("XXXX");
        Assert.Contains("не найдена", text);
    }

    /// <summary>
    /// Проверяет, что при отсутствии данных о топе растущих акций метод <see cref="MoexMarketTools.GetTopGainers"/> возвращает поясняющий текст о торгах.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию теста.</returns>
    [Fact]
    public async Task TopGainers_EmptyData_ExplainsWhy()
    {
        var text = await Tools(new FakeMarketDataService()).GetTopGainers();
        Assert.Contains("торги", text);
    }

    /// <summary>
    /// Проверяет, что метод <see cref="MoexMarketTools.SearchStocks"/> возвращает сообщение о том, что ничего не найдено, если по запросу не найдено ценных бумаг.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию модульного теста.</returns>
    [Fact]
    public async Task SearchStocks_NoResults_SaysSo()
    {
        var text = await Tools(new FakeMarketDataService()).SearchStocks("несуществующее");
        Assert.Contains("ничего не найдено", text);
    }

    /// <summary>
    /// Проверяет, что информация об акции, возвращаемая инструментом <see cref="MoexMarketTools.GetStockInfo"/>, содержит сведения о времени или статусе торгов для запрошенного тикера.
    /// </summary>
    /// <returns>Задача, представляющая выполнение теста.</returns>
    [Fact]
    public async Task GetStockInfo_ContainsMarketStatus()
    {
        var market = new FakeMarketDataService
        {
            Quote = new Quote("SBER", "Сбербанк", 271.44m, -1.89m, -0.69m, 4167119, new DateTime(2026, 8, 21, 16, 10, 15))
        };

        var text = await Tools(market).GetStockInfo("SBER");

        Assert.Contains("данные на", text); // «Торги идут (данные на …)» или «Вне сессии, данные на …»
    }

    /// <summary>
    /// Проверяет, что метод <see cref="MoexMarketTools.GetBondInfo"/> корректно форматирует информацию об облигации: цену в процентах от номинала, доходность (YTM), накопленный купонный доход и статус рынка.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию проверки.</returns>
    [Fact]
    public async Task GetBondInfo_FormatsYieldAccruedAndStatus()
    {
        var market = new FakeMarketDataService
        {
            BondQuote = new Quote("SU26243RMFS4", "ОФЗ 26243", 69.527m, -0.327m, -0.47m, 77244,
                new DateTime(2026, 8, 24, 10, 2, 59), AssetClass.Bond, "% номинала", 16.11m, 22.29m)
        };

        var text = await Tools(market).GetBondInfo("SU26243RMFS4");

        Assert.Contains("ОФЗ 26243", text);
        Assert.Contains("69,53 % номинала", text);
        Assert.Contains("16,11%", text);       // доходность
        Assert.Contains("22,29", text);        // НКД
        Assert.Contains("данные на", text);    // статус рынка
    }

    /// <summary>
    /// Проверяет, что при передаче неизвестного тикера облигации метод <see cref="MoexMarketTools.GetBondInfo"/> возвращает понятное сообщение об ошибке.
    /// </summary>
    /// <returns>Задача, представляющая результат выполнения теста.</returns>
    [Fact]
    public async Task GetBondInfo_UnknownTicker_FriendlyMessage()
    {
        var text = await Tools(new FakeMarketDataService()).GetBondInfo("XXXX");
        Assert.Contains("не найдена", text);
    }

    /// <summary>
    /// Проверяет, что метод <see cref="MoexMarketTools.GetMetalPrices"/> корректно форматирует цены драгоценных металлов с указанием стоимости за грамм и времени предоставленных данных.
    /// </summary>
    /// <returns>Задача, представляющая выполнение асинхронного теста.</returns>
    [Fact]
    public async Task GetMetalPrices_FormatsGramPriceAndStatus()
    {
        var market = new FakeMarketDataService
        {
            Metals =
            [
                new MetalPrice("GLDRUB_TOM", "Золото", 12138.56m, null, new DateTime(2026, 8, 24, 9, 41, 44)),
                new MetalPrice("SLVRUB_TOM", "Серебро", 180.85m, 0.5m, new DateTime(2026, 8, 24, 9, 41, 44)),
            ]
        };

        var text = await Tools(market).GetMetalPrices();

        Assert.Contains("Золото", text);
        Assert.Contains("12138,56 ₽/г", text);
        Assert.Contains("Серебро", text);
        Assert.Contains("данные на", text);
    }

    /// <summary>
    /// Проверяет, что метод <see cref="MoexMarketTools.GetTopBondGainers"/> корректно форматирует цены облигаций в процентах от номинала.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию проверки.</returns>
    [Fact]
    public async Task TopBondGainers_FormatsPercentOfFaceValue()
    {
        var market = new FakeMarketDataService
        {
            Quotes = [new Quote("SU26243RMFS4", "ОФЗ 26243", 70.1m, 0.1m, 0.14m, 100,
                new DateTime(2026, 8, 24, 10, 0, 0), AssetClass.Bond, "% номинала")]
        };

        var text = await Tools(market).GetTopBondGainers();

        Assert.Contains("облигаций", text);
        Assert.Contains("70,10 % номинала", text);
        Assert.Contains("+0,14", text);
    }
}

public class MoexCompareToolsTests
{
    /// <summary>
    /// Проверяет, что метод <see cref="MoexCompareTools.CompareInstruments"/> корректно разбивает строку тикеров по запятым, удаляет лишние пробелы и дубли, передаёт очищенный список в сервис сравнения и формирует результат, содержащий информацию о доходности инструмента.
    /// </summary>
    /// <returns>Задача, представляющая результат выполнения теста.</returns>
    [Fact]
    public async Task Compare_SplitsAndDeduplicatesTickers()
    {
        var comparison = new FakeComparisonService
        {
            CompareResult =
            [
                new InstrumentPerformance("SBER", "Сбербанк", 100, 110, 10m,
                    new DateTime(2026, 8, 20), new DateTime(2026, 8, 21), "snapshot")
            ]
        };
        var tools = new MoexCompareTools(comparison);

        var text = await tools.CompareInstruments("SBER, GAZP ,LKOH");

        Assert.Equal(["SBER", "GAZP", "LKOH"], comparison.LastTickers);
        Assert.Contains("SBER", text);
        Assert.Contains("+10", text);
    }

    /// <summary>
    /// Проверяет, что при передаче пустого или состоящего только из разделителей списка тикеров
    /// метод <see cref="MoexMcp.Host.Tools.MoexCompareTools.CompareInstruments"/> возвращает сообщение,
    /// запрашивающее ввод тикера.
    /// </summary>
    /// <returns>Задача, представляющая асинхронный результат выполнения теста.</returns>
    [Fact]
    public async Task Compare_EmptyTickers_AsksForInput()
    {
        var tools = new MoexCompareTools(new FakeComparisonService());
        var text = await tools.CompareInstruments("  , ,");
        Assert.Contains("тикер", text);
    }

    /// <summary>
    /// Проверяет, что метод <see cref="MoexCompareTools.RankByPerformance"/> при отсутствии исторических данных возвращает сообщение с пояснением ошибки, содержащее подстроку "истор".
    /// </summary>
    /// <returns>Задача, представляющая результат выполнения теста.</returns>
    [Fact]
    public async Task Rank_NoHistory_ExplainsFailure()
    {
        var comparison = new FakeComparisonService { RankResult = null };
        var tools = new MoexCompareTools(comparison);

        var text = await tools.RankByPerformance();

        Assert.Contains("истор", text);
    }

    /// <summary>
    /// Проверяет, что при вызове <see cref="MoexCompareTools.CompareInstruments"/> с параметром asset_type равным "bond" в сервис сравнения передаётся класс актива <see cref="AssetClass.Bond"/>.
    /// </summary>
    /// <returns>Задача, представляющая выполнение теста.</returns>
    [Fact]
    public async Task Compare_AssetTypeBond_IsPassedToService()
    {
        var comparison = new FakeComparisonService
        {
            CompareResult =
            [
                new InstrumentPerformance("OFZ", "ОФЗ", 70, 71, 1.43m,
                    new DateTime(2026, 8, 20), new DateTime(2026, 8, 21), "history", AssetClass.Bond)
            ]
        };
        var tools = new MoexCompareTools(comparison);

        var text = await tools.CompareInstruments("OFZ", asset_type: "bond");

        Assert.Equal(AssetClass.Bond, comparison.LastAssetClass);
        Assert.Contains("% номинала", text); // единицы цены облигации
    }

    /// <summary>
    /// Проверяет, что при указании неподдерживаемого типа актива методы сравнения и ранжирования возвращают пользователю понятное сообщение об ошибке «Неизвестный тип актива».
    /// </summary>
    /// <param name="assetType">Неподдерживаемый тип актива, передаваемый в инструменты сравнения и ранжирования.</param>
    /// <returns>Объект <see cref="Task"/>, представляющий асинхронную операцию выполнения теста.</returns>
    [Theory]
    [InlineData("crypto")]
    [InlineData("stocks")]
    public async Task InvalidAssetType_FriendlyError(string assetType)
    {
        var tools = new MoexCompareTools(new FakeComparisonService());

        var compareText = await tools.CompareInstruments("SBER", asset_type: assetType);
        var rankText = await tools.RankByPerformance(asset_type: assetType);

        Assert.Contains("Неизвестный тип актива", compareText);
        Assert.Contains("Неизвестный тип актива", rankText);
    }

    /// <summary>
    /// Проверяет, что при вызове <see cref="MoexCompareTools.RankByPerformance"/> с типом актива "bond" значение корректно преобразуется в <see cref="AssetClass.Bond"/> и передаётся в сервис сравнения.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию теста.</returns>
    [Fact]
    public async Task Rank_AssetTypeBond_IsPassedToService()
    {
        var comparison = new FakeComparisonService { RankResult = [] };
        var tools = new MoexCompareTools(comparison);

        await tools.RankByPerformance(asset_type: "bond");

        Assert.Equal(AssetClass.Bond, comparison.LastAssetClass);
    }
}

public class MoexHistoryToolsTests
{
    /// <summary>
    /// Проверяет, что при получении OHLC-свечей с параметром <c>asset_type</c>, равным <c>"bond"</c>, в сервис истории передаётся значение <see cref="AssetClass.Bond"/>.
    /// </summary>
    /// <returns>Задача, представляющая результат выполнения теста.</returns>
    [Fact]
    public async Task Candles_AssetTypeBond_IsPassedToService()
    {
        var history = new FakeHistoryService
        {
            Candles = [new Candle(new DateTime(2026, 8, 21, 10, 0, 0), 70, 70.1m, 70.2m, 69.9m, 100)]
        };
        var tools = new MoexHistoryTools(history);

        var text = await tools.GetCandles("SU26243RMFS4", asset_type: "bond");

        Assert.Equal(AssetClass.Bond, history.LastAssetClass);
        Assert.Contains("70,10", text);
    }

    /// <summary>
    /// Проверяет, что при передаче недопустимого типа актива в инструмент получения свечей возвращается понятное сообщение об ошибке, содержащее текст "Неизвестный тип актива".
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию проверки.</returns>
    [Fact]
    public async Task Candles_InvalidAssetType_FriendlyError()
    {
        var tools = new MoexHistoryTools(new FakeHistoryService());

        var text = await tools.GetCandles("SBER", asset_type: "crypto");

        Assert.Contains("Неизвестный тип актива", text);
    }

    /// <summary>
    /// Проверяет, что при запросе истории цен облигации через <see cref="MoexHistoryTools.GetPriceHistory"/>
    /// с параметром asset_type, равным bond, в сервис истории передаётся <see cref="AssetClass.Bond"/>,
    /// а цена закрытия отображается в результате как процент от номинала.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию проверки.</returns>
    [Fact]
    public async Task PriceHistory_Bond_FormatsPercentOfFaceValue()
    {
        var history = new FakeHistoryService
        {
            Prices = [new DailyPrice("SU26243RMFS4", new DateTime(2026, 8, 21), 69.85m)]
        };
        var tools = new MoexHistoryTools(history);

        var text = await tools.GetPriceHistory("SU26243RMFS4", asset_type: "bond");

        Assert.Equal(AssetClass.Bond, history.LastAssetClass);
        Assert.Contains("69,85 % номинала", text);
    }

    /// <summary>
    /// Проверяет, что при вызове <see cref="MoexHistoryTools.GetPriceHistory(string, string?, string?, string, CancellationToken)"/>
    /// только с тикером по умолчанию используется тип актива <see cref="AssetClass.Share"/>,
    /// а возвращаемый текст содержит цену закрытия в формате с символом рубля.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию теста.</returns>
    [Fact]
    public async Task PriceHistory_DefaultAssetType_IsShare()
    {
        var history = new FakeHistoryService
        {
            Prices = [new DailyPrice("SBER", new DateTime(2026, 8, 21), 271.11m)]
        };
        var tools = new MoexHistoryTools(history);

        var text = await tools.GetPriceHistory("SBER");

        Assert.Equal(AssetClass.Share, history.LastAssetClass);
        Assert.Contains("271,11 ₽", text); // обратная совместимость формата
    }
}
