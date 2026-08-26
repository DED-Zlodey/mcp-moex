using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;

namespace MoexMcp.Tests.Application;

public class ComparisonServiceTests
{
    /// <summary>
    /// Начальная дата и время периода, используемого в тестах сравнения и ранжирования инструментов.
    /// </summary>
    private static readonly DateTime From = new(2026, 8, 20, 10, 0, 0);

    /// <summary>
    /// Дата и время окончания периода, используемые в тестах сравнения инструментов и расчёта доходности.
    /// </summary>
    private static readonly DateTime To = new(2026, 8, 21, 18, 0, 0);

    /// <summary>
    /// Создаёт экземпляр <see cref="ComparisonService"/> для использования в тестах.
    /// </summary>
    /// <param name="moex">Фейковый репозиторий данных Московской биржи.</param>
    /// <param name="cache">Фейковый репозиторий кэша. Если не указан, используется новый экземпляр <see cref="FakeCacheRepository"/>.</param>
    /// <returns>Настроенный экземпляр <see cref="ComparisonService"/>.</returns>
    private static ComparisonService Service(FakeMoexRepository moex, FakeCacheRepository? cache = null) =>
        new(moex, cache ?? new FakeCacheRepository());

    /// <summary>
    /// Проверяет, что метод <see cref="ComparisonService.CompareInstrumentsAsync"/> корректно вычисляет процентное изменение цены инструмента на основе дневной истории котировок и возвращает список инструментов, отсортированный по изменению по убыванию.
    /// </summary>
    /// <returns>
    /// Задача, представляющая результат выполнения асинхронного теста.
    /// </returns>
    [Fact]
    public async Task Compare_FromDailyHistory_ComputesChangeAndRanksDesc()
    {
        var moex = new FakeMoexRepository();
        moex.HistoryByTicker["SBER"] =
        [
            new DailyPrice("SBER", new DateTime(2026, 8, 20), 100m),
            new DailyPrice("SBER", new DateTime(2026, 8, 21), 110m), // +10%
        ];
        moex.HistoryByTicker["GAZP"] =
        [
            new DailyPrice("GAZP", new DateTime(2026, 8, 20), 200m),
            new DailyPrice("GAZP", new DateTime(2026, 8, 21), 190m), // -5%
        ];

        var result = await Service(moex).CompareInstrumentsAsync(["SBER", "GAZP"], From, To);

        Assert.Equal(2, result.Count);
        Assert.Equal("SBER", result[0].Ticker);        // выше доходность — выше место
        Assert.Equal(10.0m, result[0].ChangePercent);
        Assert.Equal("history", result[0].PriceSource);
        Assert.Equal("GAZP", result[1].Ticker);
        Assert.Equal(-5.0m, result[1].ChangePercent);
    }

    /// <summary>
    /// Проверяет, что при сравнении инструменты без исторических цен пропускаются и не попадают в результат.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию выполнения теста.</returns>
    [Fact]
    public async Task Compare_TickerWithoutPrices_IsSkipped()
    {
        var moex = new FakeMoexRepository();
        moex.HistoryByTicker["SBER"] =
        [
            new DailyPrice("SBER", new DateTime(2026, 8, 20), 100m),
            new DailyPrice("SBER", new DateTime(2026, 8, 21), 110m),
        ];

        var result = await Service(moex).CompareInstrumentsAsync(["SBER", "XXXX"], From, To);

        Assert.Single(result); // XXXX без истории не попадает в выдачу
    }

    /// <summary>
    /// Проверяет, что при сравнении инструментов дублирующиеся тикеры, включая варианты с разным регистром, учитываются только один раз.
    /// </summary>
    /// <returns>Задача, представляющая результат выполнения асинхронного теста.</returns>
    [Fact]
    public async Task Compare_DuplicateTickers_AreCountedOnce()
    {
        var moex = new FakeMoexRepository();
        moex.HistoryByTicker["SBER"] =
        [
            new DailyPrice("SBER", new DateTime(2026, 8, 20), 100m),
            new DailyPrice("SBER", new DateTime(2026, 8, 21), 110m),
        ];

        var result = await Service(moex).CompareInstrumentsAsync(["SBER", "sber", "SBER"], From, To);

        Assert.Single(result);
    }

    /// <summary>
    /// Проверяет, что при сравнении облигаций класс актива <see cref="AssetClass.Bond"/> передаётся в репозиторий исторических данных и сохраняется в результирующем элементе.
    /// </summary>
    /// <returns>
    /// Задача, представляющая результат асинхронного теста.
    /// </returns>
    [Fact]
    public async Task Compare_Bond_PassesAssetClassToHistoryAndResult()
    {
        var moex = new FakeMoexRepository();
        moex.HistoryByTicker["OFZ"] =
        [
            new DailyPrice("OFZ", new DateTime(2026, 8, 20), 69m),
            new DailyPrice("OFZ", new DateTime(2026, 8, 21), 70m),
        ];

        var result = await Service(moex).CompareInstrumentsAsync(["OFZ"], From, To, AssetClass.Bond);

        var item = Assert.Single(result);
        Assert.Equal(AssetClass.Bond, item.Class);
        Assert.Equal(AssetClass.Bond, moex.LastHistoryAssetClass);
    }

    /// <summary>
    /// Проверяет, что метод <see cref="ComparisonService.RankByPerformanceAsync"/> сортирует инструменты по проценту изменения цены по убыванию и исключает из рейтинга инструменты с неполными историческими данными.
    /// </summary>
    /// <returns>Не возвращает значение.</returns>
    [Fact]
    public async Task Rank_SortsByYieldAndSkipsBrokenRows()
    {
        var moex = new FakeMoexRepository();
        moex.BoardCloses[(AssetClass.Share, From.Date)] =
        [
            new DailyPrice("A", From.Date, 100m),
            new DailyPrice("B", From.Date, 100m),
            new DailyPrice("C", From.Date, 100m),
        ];
        moex.BoardCloses[(AssetClass.Share, To.Date)] =
        [
            new DailyPrice("A", To.Date, 105m), // +5%
            new DailyPrice("B", To.Date, 90m),  // -10%
            // C нет в конце периода — пропуск; D не было в начале — пропуск
            new DailyPrice("D", To.Date, 50m),
        ];

        var rank = await Service(moex).RankByPerformanceAsync(From, To, 10);

        Assert.NotNull(rank);
        Assert.Equal(2, rank.Count);
        Assert.Equal("A", rank[0].Ticker);
        Assert.Equal(5.0m, rank[0].ChangePercent);
        Assert.Equal("B", rank[1].Ticker);
    }

    /// <summary>
    /// Проверяет, что метод <see cref="ComparisonService.RankByPerformanceAsync"/> при ранжировании инструментов по доходности учитывает параметр <c>limit</c> и возвращает не более заданного количества результатов, отсортированных по убыванию доходности.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию тестирования.</returns>
    [Fact]
    public async Task Rank_RespectsLimit()
    {
        var moex = new FakeMoexRepository();
        moex.BoardCloses[(AssetClass.Share, From.Date)] =
            Enumerable.Range(1, 30).Select(i => new DailyPrice($"T{i}", From.Date, 100m)).ToList();
        moex.BoardCloses[(AssetClass.Share, To.Date)] =
            Enumerable.Range(1, 30).Select(i => new DailyPrice($"T{i}", To.Date, 100m + i)).ToList();

        var rank = await Service(moex).RankByPerformanceAsync(From, To, 5);

        Assert.NotNull(rank);
        Assert.Equal(5, rank.Count);
        Assert.Equal("T30", rank[0].Ticker); // максимальная доходность
    }

    /// <summary>
    /// Проверяет, что при запросе ранжирования за период, начинающийся в выходной день,
    /// стартовая цена берётся с последнего торгового дня, предшествующего выходным.
    /// </summary>
    /// <returns>Задача, представляющая результат выполнения асинхронного теста.</returns>
    [Fact]
    public async Task Rank_WeekendMoment_WalksBackToLastTradingDay()
    {
        var moex = new FakeMoexRepository();
        // 22–23.08.2026 — выходные, данных нет; последний торговый день — пятница 21.08
        var friday = new DateTime(2026, 8, 21);
        var monday = new DateTime(2026, 8, 24);
        moex.BoardCloses[(AssetClass.Share, friday)] = [new DailyPrice("SBER", friday, 100m)];
        moex.BoardCloses[(AssetClass.Share, monday)] = [new DailyPrice("SBER", monday, 110m)];

        var sunday = new DateTime(2026, 8, 23, 15, 0, 0);
        var rank = await Service(moex).RankByPerformanceAsync(sunday, monday, 10);

        var item = Assert.Single(rank!);
        Assert.Equal(friday, item.StartPriceTime); // старт взялся с пятницы, а не с пустых выходных
        Assert.Equal(10.0m, item.ChangePercent);
    }

    /// <summary>
    /// Проверяет, что метод <see cref="ComparisonService.RankByPerformanceAsync"/> возвращает <c>null</c>, когда в репозитории отсутствует история котировок.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию выполнения теста.</returns>
    [Fact]
    public async Task Rank_NoHistoryAtAll_ReturnsNull()
    {
        var rank = await Service(new FakeMoexRepository()).RankByPerformanceAsync(From, To, 10);

        Assert.Null(rank);
    }

    /// <summary>
    /// Проверяет, что при ранжировании по доходности запрашиваются данные о закрытии торговой доски только для указанного класса активов, а в результате остаются только инструменты этого класса.
    /// </summary>
    /// <returns>Задача, представляющая выполнение асинхронного теста.</returns>
    [Fact]
    public async Task Rank_FiltersByAssetClassViaBoard()
    {
        var moex = new FakeMoexRepository();
        moex.BoardCloses[(AssetClass.Share, From.Date)] = [new DailyPrice("SBER", From.Date, 100m)];
        moex.BoardCloses[(AssetClass.Share, To.Date)] = [new DailyPrice("SBER", To.Date, 120m)];
        moex.BoardCloses[(AssetClass.Bond, From.Date)] = [new DailyPrice("OFZ", From.Date, 70m)];
        moex.BoardCloses[(AssetClass.Bond, To.Date)] = [new DailyPrice("OFZ", To.Date, 71m)];

        var bonds = await Service(moex).RankByPerformanceAsync(From, To, 10, AssetClass.Bond);

        var bond = Assert.Single(bonds!);
        Assert.Equal("OFZ", bond.Ticker);
        Assert.Equal(AssetClass.Bond, bond.Class);
        Assert.Equal(AssetClass.Bond, moex.LastBoardClosesAssetClass);
    }

    /// <summary>
    /// Проверяет, что повторный вызов <see cref="ComparisonService.RankByPerformanceAsync"/> использует закэшированные данные закрытия торговых дней и не выполняет дополнительных обращений к репозиторию.
    /// </summary>
    /// <returns>Задача для выполнения теста.</returns>
    [Fact]
    public async Task Rank_SecondCall_UsesCachedBoardCloses()
    {
        var moex = new FakeMoexRepository();
        moex.BoardCloses[(AssetClass.Share, From.Date)] = [new DailyPrice("SBER", From.Date, 100m)];
        moex.BoardCloses[(AssetClass.Share, To.Date)] = [new DailyPrice("SBER", To.Date, 110m)];
        var cache = new FakeCacheRepository();
        var service = Service(moex, cache);

        await service.RankByPerformanceAsync(From, To, 10);
        var callsAfterFirst = moex.BoardClosesCalls;
        await service.RankByPerformanceAsync(From, To, 10);

        Assert.Equal(callsAfterFirst, moex.BoardClosesCalls); // повторный rank в ISS не пошёл
        Assert.True(cache.SetCalls > 0);
    }

    /// <summary>
    /// Проверяет, что <see cref="ComparisonService.RankByPerformanceAsync"/> не кэширует пустые данные за текущий торговый день до закрытия сессии.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию тестирования.</returns>
    [Fact]
    public async Task Rank_EmptyDay_IsNotCached()
    {
        // Сегодняшний день появляется в истории только после закрытия сессии — пустое нельзя кэшировать
        var moex = new FakeMoexRepository();
        var cache = new FakeCacheRepository();

        await Service(moex, cache).RankByPerformanceAsync(From, To, 10);

        Assert.Equal(0, cache.SetCalls);
    }
}
