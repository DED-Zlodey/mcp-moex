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
