using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;

namespace MoexMcp.Tests.Application;

public class HistoryServiceTests
{
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
}
