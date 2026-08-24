using Microsoft.Extensions.Logging.Abstractions;
using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;
using MoexMcp.Host.Tools;

namespace MoexMcp.Tests.Host;

internal class FakeMarketDataService : IMarketDataService
{
    public Quote? Quote { get; set; }
    public Quote? BondQuote { get; set; }
    public IReadOnlyList<Quote> Quotes { get; set; } = [];
    public IReadOnlyList<MetalPrice> Metals { get; set; } = [];
    public IReadOnlyList<SecurityInfo> Securities { get; set; } = [];
    public IReadOnlyList<SiteNewsItem> News { get; set; } = [];
    public IReadOnlyList<CurrencyRate> CurrencyRates { get; set; } = [];

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
    public IReadOnlyList<InstrumentPerformance> CompareResult { get; set; } = [];
    public IReadOnlyList<InstrumentPerformance>? RankResult { get; set; } = [];
    public IReadOnlyList<string>? LastTickers { get; private set; }
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
    public IReadOnlyList<Candle> Candles { get; set; } = [];
    public IReadOnlyList<DailyPrice> Prices { get; set; } = [];
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

    [Fact]
    public async Task GetBondInfo_UnknownTicker_FriendlyMessage()
    {
        var text = await Tools(new FakeMarketDataService()).GetBondInfo("XXXX");
        Assert.Contains("не найдена", text);
    }

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

    [Fact]
    public async Task Candles_InvalidAssetType_FriendlyError()
    {
        var tools = new MoexHistoryTools(new FakeHistoryService());

        var text = await tools.GetCandles("SBER", asset_type: "crypto");

        Assert.Contains("Неизвестный тип актива", text);
    }

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
