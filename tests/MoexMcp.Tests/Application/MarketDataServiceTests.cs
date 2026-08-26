using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;

namespace MoexMcp.Tests.Application;

public class MarketDataServiceTests
{
    /// <summary>
    /// Создаёт и возвращает экземпляр MarketDataService, инициализированный переданными фейковыми репозиториями.
    /// </summary>
    /// <param name="moex">Фейковый репозиторий данных Московской биржи.</param>
    /// <param name="cache">Фейковый репозиторий кэша.</param>
    /// <returns>Экземпляр сервиса рыночных данных, настроенный для использования в тестах.</returns>
    private static MarketDataService Service(FakeMoexRepository moex, FakeCacheRepository cache) =>
        new(moex, cache);

    /// <summary>
    /// Проверяет, что метод GetTopGainersAsync возвращает топ акций с наибольшим ростом,
    /// отсортированных по убыванию процента изменения и ограниченных заданным количеством.
    /// </summary>
    /// <returns>Задача, представляющая выполнение теста.</returns>
    [Fact]
    public async Task TopGainers_SortedDescAndLimited()
    {
        var moex = new FakeMoexRepository
        {
            AllQuotes =
            [
                TestData.Quote("A", 100, 1.5m),
                TestData.Quote("B", 100, 5.0m),
                TestData.Quote("C", 100, 3.0m),
                TestData.Quote("D", 100, -2.0m),
            ]
        };

        var top = await Service(moex, new FakeCacheRepository()).GetTopGainersAsync(2);

        Assert.Equal(2, top.Count);
        Assert.Equal("B", top[0].Ticker);
        Assert.Equal("C", top[1].Ticker);
    }

    /// <summary>
    /// Возвращает заданное количество акций с наибольшим падением стоимости.
    /// Результат отсортирован по процентному изменению цены по возрастанию,
    /// начиная с инструмента с самым большим отрицательным изменением.
    /// Инструменты, для которых не задано процентное изменение, исключаются.
    /// </summary>
    /// <param name="limit">Максимальное количество возвращаемых инструментов.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список акций с наибольшим падением цены, отсортированных по возрастанию значения изменения в процентах.</returns>
    [Fact]
    public async Task TopLosers_SortedAsc()
    {
        var moex = new FakeMoexRepository
        {
            AllQuotes =
            [
                TestData.Quote("A", 100, 1.5m),
                TestData.Quote("B", 100, -5.0m),
                TestData.Quote("C", 100, -3.0m),
            ]
        };

        var top = await Service(moex, new FakeCacheRepository()).GetTopLosersAsync(2);

        Assert.Equal("B", top[0].Ticker);
        Assert.Equal("C", top[1].Ticker);
    }

    /// <summary>
    /// Проверяет, что котировки без заданного процентного изменения цены пропускаются
    /// и не участвуют в формировании топа растущих активов.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию выполнения теста.</returns>
    [Fact]
    public async Task Tops_SkipQuotesWithoutChangePercent()
    {
        var moex = new FakeMoexRepository
        {
            AllQuotes =
            [
                TestData.Quote("A", 100, null), // нет торгов — не участвует в топе
                TestData.Quote("B", 100, 2.0m),
            ]
        };

        var top = await Service(moex, new FakeCacheRepository()).GetTopGainersAsync(10);

        Assert.Single(top);
        Assert.Equal("B", top[0].Ticker);
    }

    /// <summary>
    /// Проверяет, что список всех акций кэшируется при последовательных вызовах
    /// <see cref="MarketDataService.GetTopGainersAsync"/> и
    /// <see cref="MarketDataService.GetTopLosersAsync"/>,
    /// благодаря чему данные из хранилища запрашиваются только один раз.
    /// </summary>
    /// <return>Задача, представляющая асинхронно выполняемый тест.</return>
    [Fact]
    public async Task AllSharesList_IsCached()
    {
        var moex = new FakeMoexRepository { AllQuotes = [TestData.Quote("A", 100, 1m)] };
        var cache = new FakeCacheRepository();
        var service = Service(moex, cache);

        await service.GetTopGainersAsync(5);
        await service.GetTopLosersAsync(5);

        Assert.Equal(1, moex.AllQuotesCalls); // второй вызов ушёл в кэш
    }

    /// <summary>
    /// Проверяет, что информация об облигации кэшируется при повторном запросе:
    /// второй вызов <see cref="MarketDataService.GetBondInfoAsync"/> с тем же тикером
    /// не приводит к повторному обращению к репозиторию.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию теста.</returns>
    [Fact]
    public async Task BondInfo_IsCached()
    {
        var moex = new FakeMoexRepository
        {
            AllBondQuotes = [TestData.Quote("SU26243RMFS4", 70, 0.1m, assetClass: AssetClass.Bond)]
        };
        var service = Service(moex, new FakeCacheRepository());

        var first = await service.GetBondInfoAsync("SU26243RMFS4");
        var second = await service.GetBondInfoAsync("SU26243RMFS4");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, moex.BondQuoteCalls); // второй вызов ушёл в кэш
    }

    /// <summary>
    /// Проверяет, что результаты вызова <see cref="MarketDataService.GetMetalPricesAsync"/> кэшируются,
    /// и повторный вызов метода не приводит к повторному обращению к источнику данных.
    /// </summary>
    /// <returns>Задача, завершающаяся после проверки корректности кэширования цен на металлы.</returns>
    [Fact]
    public async Task MetalPrices_AreCached()
    {
        var moex = new FakeMoexRepository
        {
            MetalPrices = [new MetalPrice("GLDRUB_TOM", "Золото", 12000, 5, new DateTime(2026, 8, 21, 10, 0, 0))]
        };
        var service = Service(moex, new FakeCacheRepository());

        await service.GetMetalPricesAsync();
        var metals = await service.GetMetalPricesAsync();

        Assert.Single(metals);
        Assert.Equal(1, moex.MetalsCalls);
    }

    /// <summary>
    /// Возвращает заданное количество облигаций с наибольшим ростом стоимости.
    /// Результат отсортирован по процентному изменению цены по убыванию,
    /// начиная с инструмента с самым большим положительным изменением.
    /// Облигации, для которых не задано процентное изменение, исключаются.
    /// </summary>
    /// <param name="limit">Максимальное количество возвращаемых облигаций.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список облигаций с наибольшим ростом цены, отсортированных по убыванию значения изменения в процентах.</returns>
    [Fact]
    public async Task TopBondGainers_SortedDescAndLimited()
    {
        var moex = new FakeMoexRepository
        {
            AllBondQuotes =
            [
                TestData.Quote("A", 100, 1.5m, assetClass: AssetClass.Bond),
                TestData.Quote("B", 100, 5.0m, assetClass: AssetClass.Bond),
                TestData.Quote("C", 100, 3.0m, assetClass: AssetClass.Bond),
                TestData.Quote("D", 100, null, assetClass: AssetClass.Bond), // без торгов — не участвует
            ]
        };
        var service = Service(moex, new FakeCacheRepository());

        var gainers = await service.GetTopBondGainersAsync(2);
        var losers = await service.GetTopBondLosersAsync(2);

        Assert.Equal(["B", "C"], gainers.Select(q => q.Ticker).ToArray());
        Assert.Equal(["A", "C"], losers.Select(q => q.Ticker).ToArray());
        Assert.Equal(1, moex.AllBondsCalls); // список облигаций кэшируется между топами
    }
}
