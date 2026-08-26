using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;

namespace MoexMcp.Tests.Application;

public class HistoryServiceTests
{
    /// <summary>
    /// Проверяет, что метод <see cref="HistoryService.GetCandlesAsync"/> выбрасывает исключение <see cref="ArgumentException"/>
    /// при передаче неподдерживаемого значения интервала свечи.
    /// </summary>
    /// <param name="interval">Интервал свечи в минутах, который не входит в множество допустимых значений.</param>
    /// <returns>Задача, представляющая результат выполнения асинхронного теста.</returns>
    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(0)]
    public async Task GetCandles_UnsupportedInterval_Throws(int interval)
    {
        var service = new HistoryService(new FakeMoexRepository(), new FakeCacheRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetCandlesAsync("SBER", interval, DateTime.Today.AddDays(-1), DateTime.Today));
    }

    /// <summary>
    /// Проверяет, что запрос свечей с поддерживаемым интервалом корректно проходит через сервис истории и возвращает данные.
    /// </summary>
    /// <param name="interval">Интервал свечи в минутах, который должен поддерживаться сервисом.</param>
    /// <returns>Задача, представляющая асинхронную операцию выполнения теста.</returns>
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(24)]
    public async Task GetCandles_SupportedIntervals_PassThrough(int interval)
    {
        var moex = new FakeMoexRepository
        {
            Candles = [new Candle(new DateTime(2026, 8, 21, 10, 0, 0), 1, 2, 3, 0.5m, 100)]
        };
        var service = new HistoryService(moex, new FakeCacheRepository());

        var candles = await service.GetCandlesAsync("SBER", interval, DateTime.Today.AddDays(-1), DateTime.Today);

        Assert.Single(candles);
    }

    /// <summary>
    /// Проверяет, что история цен кэшируется: повторный вызов получения истории по тому же тикеру и диапазону дат не приводит к повторному обращению к репозиторию MOEX.
    /// </summary>
    /// <return>Задача, представляющая асинхронную операцию выполнения теста.</return>
    [Fact]
    public async Task PriceHistory_IsCached()
    {
        var moex = new FakeMoexRepository
        {
            History = [new DailyPrice("SBER", new DateTime(2026, 8, 21), 273.33m)]
        };
        var service = new HistoryService(moex, new FakeCacheRepository());
        var from = new DateTime(2026, 8, 1);
        var to = new DateTime(2026, 8, 21);

        await service.GetPriceHistoryAsync("SBER", from, to);
        await service.GetPriceHistoryAsync("SBER", from, to);

        Assert.Equal(1, moex.HistoryCalls);
    }

    /// <summary>
    /// Проверяет, что ключ кэширования истории цен включает класс актива, поэтому запросы с разными классами активов не попадают в один и тот же кэш.
    /// </summary>
    /// <return>
    /// Задача, представляющая результат выполнения асинхронного теста.
    /// </return>
    [Fact]
    public async Task CacheKeys_IncludeAssetClass()
    {
        var moex = new FakeMoexRepository
        {
            History = [new DailyPrice("X", new DateTime(2026, 8, 21), 100m)]
        };
        var service = new HistoryService(moex, new FakeCacheRepository());
        var from = new DateTime(2026, 8, 1);
        var to = new DateTime(2026, 8, 21);

        await service.GetPriceHistoryAsync("X", from, to);                          // share
        await service.GetPriceHistoryAsync("X", from, to);                          // share — из кэша
        await service.GetPriceHistoryAsync("X", from, to, AssetClass.Bond);         // bond — другой ключ

        Assert.Equal(2, moex.HistoryCalls);
    }

    /// <summary>
    /// Проверяет, что значение <see cref="AssetClass"/> корректно передаётся в репозиторий
    /// при вызове методов получения свечей и истории цен.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию выполнения теста.</returns>
    [Fact]
    public async Task AssetClass_IsPassedToRepository()
    {
        var moex = new FakeMoexRepository();
        var service = new HistoryService(moex, new FakeCacheRepository());

        await service.GetCandlesAsync("SU26243RMFS4", 60, DateTime.Today.AddDays(-1), DateTime.Today, AssetClass.Bond);
        await service.GetPriceHistoryAsync("GLDRUB_TOM", DateTime.Today.AddDays(-1), DateTime.Today, AssetClass.Metal);

        Assert.Equal(AssetClass.Bond, moex.LastCandlesAssetClass);
        Assert.Equal(AssetClass.Metal, moex.LastHistoryAssetClass);
    }
}
