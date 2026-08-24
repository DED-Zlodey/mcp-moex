using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;

namespace MoexMcp.Tests.Application;

public class MarketDataServiceTests
{
    private static MarketDataService Service(FakeMoexRepository moex, FakeCacheRepository cache) =>
        new(moex, cache);

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
}
