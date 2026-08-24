using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Tests.Application;

/// <summary>Фейк IMoexRepository: возвращает заготовленные данные, считает вызовы.</summary>
internal class FakeMoexRepository : IMoexRepository
{
    public IReadOnlyList<Quote> AllQuotes { get; set; } = [];
    public IReadOnlyList<Quote> AllBondQuotes { get; set; } = [];
    public IReadOnlyList<MetalPrice> MetalPrices { get; set; } = [];
    public IReadOnlyList<DailyPrice> History { get; set; } = [];
    public IReadOnlyList<Candle> Candles { get; set; } = [];
    public int AllQuotesCalls { get; private set; }
    public int AllBondsCalls { get; private set; }
    public int MetalsCalls { get; private set; }
    public int BondQuoteCalls { get; private set; }
    public int HistoryCalls { get; private set; }
    public int CandlesCalls { get; private set; }
    public AssetClass? LastHistoryAssetClass { get; private set; }
    public AssetClass? LastCandlesAssetClass { get; private set; }

    public Task<Quote?> GetQuoteAsync(string ticker, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default) =>
        Task.FromResult(AllQuotes.FirstOrDefault(q => q.Ticker == ticker));

    public Task<Quote?> GetBondQuoteAsync(string ticker, CancellationToken ct = default)
    {
        BondQuoteCalls++;
        return Task.FromResult(AllBondQuotes.FirstOrDefault(q => q.Ticker == ticker));
    }

    public Task<IReadOnlyList<Quote>> GetAllShareQuotesAsync(CancellationToken ct = default)
    {
        AllQuotesCalls++;
        return Task.FromResult(AllQuotes);
    }

    public Task<IReadOnlyList<Quote>> GetAllBondQuotesAsync(CancellationToken ct = default)
    {
        AllBondsCalls++;
        return Task.FromResult(AllBondQuotes);
    }

    public Task<IReadOnlyList<MetalPrice>> GetMetalPricesAsync(CancellationToken ct = default)
    {
        MetalsCalls++;
        return Task.FromResult(MetalPrices);
    }

    public Task<IReadOnlyList<SecurityInfo>> SearchSecuritiesAsync(string query, int limit = 20, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SecurityInfo>>([]);

    public Task<IReadOnlyList<SiteNewsItem>> GetSiteNewsAsync(int limit = 20, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SiteNewsItem>>([]);

    public Task<IReadOnlyList<Candle>> GetCandlesAsync(string ticker, int intervalMinutes, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        CandlesCalls++;
        LastCandlesAssetClass = assetClass;
        return Task.FromResult(Candles);
    }

    public Task<IReadOnlyList<DailyPrice>> GetPriceHistoryAsync(string ticker, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        HistoryCalls++;
        LastHistoryAssetClass = assetClass;
        return Task.FromResult(History);
    }

    public Task<IReadOnlyList<IndexQuote>> GetIndicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IndexQuote>>([]);

    public Task<IReadOnlyList<CurrencyRate>> GetCurrencyRatesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CurrencyRate>>([]);
}

/// <summary>In-memory фейк кэша.</summary>
internal class FakeCacheRepository : ICacheRepository
{
    private readonly Dictionary<string, object> _data = new();
    public int GetCalls { get; private set; }
    public int SetCalls { get; private set; }

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        GetCalls++;
        return Task.FromResult(_data.TryGetValue(key, out var v) ? (T?)v : default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        SetCalls++;
        _data[key] = value!;
        return Task.CompletedTask;
    }
}

/// <summary>Фейк хранилища снапшотов.</summary>
internal class FakeSnapshotRepository : ISnapshotRepository
{
    private readonly List<MarketSnapshot> _snapshots = [];
    public int SavedCount => _snapshots.Count;
    public MarketSnapshot? Last => _snapshots.LastOrDefault();

    public void Add(MarketSnapshot snapshot) => _snapshots.Add(snapshot);

    public Task SaveSnapshotAsync(MarketSnapshot snapshot, CancellationToken ct = default)
    {
        _snapshots.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task<MarketSnapshot?> GetNearestSnapshotAsync(DateTime moment, CancellationToken ct = default)
    {
        var nearest = _snapshots
            .OrderBy(s => (s.TakenAt - moment).Duration())
            .FirstOrDefault();
        return Task.FromResult(nearest);
    }

    public Task CleanupOlderThanAsync(TimeSpan retention, CancellationToken ct = default) => Task.CompletedTask;
}

internal static class TestData
{
    public static Quote Quote(string ticker, decimal? price, decimal? changePercent, DateTime? time = null, AssetClass assetClass = AssetClass.Share) =>
        new(ticker, $"Name {ticker}", price, null, changePercent, 1000, time ?? new DateTime(2026, 8, 21, 10, 0, 0), assetClass, assetClass.PriceUnit());
}
