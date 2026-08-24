using Microsoft.Extensions.Logging.Abstractions;
using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;
using MoexMcp.Host.Tools;

namespace MoexMcp.Tests.Host;

internal class FakeMarketDataService : IMarketDataService
{
    public Quote? Quote { get; set; }
    public IReadOnlyList<Quote> Quotes { get; set; } = [];
    public IReadOnlyList<SecurityInfo> Securities { get; set; } = [];
    public IReadOnlyList<SiteNewsItem> News { get; set; } = [];

    public Task<Quote?> GetStockInfoAsync(string ticker, CancellationToken ct = default) => Task.FromResult(Quote);
    public Task<IReadOnlyList<Quote>> GetTopGainersAsync(int limit, CancellationToken ct = default) => Task.FromResult(Quotes);
    public Task<IReadOnlyList<Quote>> GetTopLosersAsync(int limit, CancellationToken ct = default) => Task.FromResult(Quotes);
    public Task<IReadOnlyList<SecurityInfo>> SearchStocksAsync(string query, CancellationToken ct = default) => Task.FromResult(Securities);
    public Task<IReadOnlyList<SiteNewsItem>> GetNewsAsync(int limit, CancellationToken ct = default) => Task.FromResult(News);
    public Task<IReadOnlyList<IndexQuote>> GetIndicesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IndexQuote>>([]);
    public Task<IReadOnlyList<CurrencyRate>> GetCurrencyRatesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CurrencyRate>>([]);
}

internal class FakeComparisonService : IComparisonService
{
    public IReadOnlyList<InstrumentPerformance> CompareResult { get; set; } = [];
    public IReadOnlyList<InstrumentPerformance>? RankResult { get; set; } = [];
    public IReadOnlyList<string>? LastTickers { get; private set; }

    public Task<IReadOnlyList<InstrumentPerformance>> CompareInstrumentsAsync(
        IReadOnlyList<string> tickers, DateTime from, DateTime to, CancellationToken ct = default)
    {
        LastTickers = tickers;
        return Task.FromResult(CompareResult);
    }

    public Task<IReadOnlyList<InstrumentPerformance>?> RankByPerformanceAsync(
        DateTime from, DateTime to, int limit, CancellationToken ct = default) =>
        Task.FromResult(RankResult);
}

public class MoexMarketToolsTests
{
    private static MoexMarketTools Tools(FakeMarketDataService market) =>
        new(market, NullLogger<MoexMarketTools>.Instance);

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

    [Fact]
    public async Task GetStockInfo_UnknownTicker_FriendlyMessage()
    {
        var text = await Tools(new FakeMarketDataService()).GetStockInfo("XXXX");
        Assert.Contains("не найдена", text);
    }

    [Fact]
    public async Task TopGainers_EmptyData_ExplainsWhy()
    {
        var text = await Tools(new FakeMarketDataService()).GetTopGainers();
        Assert.Contains("торги", text);
    }

    [Fact]
    public async Task SearchStocks_NoResults_SaysSo()
    {
        var text = await Tools(new FakeMarketDataService()).SearchStocks("несуществующее");
        Assert.Contains("ничего не найдено", text);
    }
}

public class MoexCompareToolsTests
{
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

    [Fact]
    public async Task Compare_EmptyTickers_AsksForInput()
    {
        var tools = new MoexCompareTools(new FakeComparisonService());
        var text = await tools.CompareInstruments("  , ,");
        Assert.Contains("тикер", text);
    }

    [Fact]
    public async Task Rank_NoSnapshots_ExplainsAccumulation()
    {
        var comparison = new FakeComparisonService { RankResult = null };
        var tools = new MoexCompareTools(comparison);

        var text = await tools.RankByPerformance();

        Assert.Contains("снапшот", text);
        Assert.Contains("compare_instruments", text);
    }
}
