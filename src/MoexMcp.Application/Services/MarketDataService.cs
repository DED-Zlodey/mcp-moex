using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Application.Services;

/// <summary>Рыночные данные с кэшированием. Слой не знает, что кэш — это Redis.</summary>
public class MarketDataService : IMarketDataService
{
    private static readonly TimeSpan QuoteTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SharesListTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NewsTtl = TimeSpan.FromMinutes(5);

    private readonly IMoexRepository _moex;
    private readonly ICacheRepository _cache;

    public MarketDataService(IMoexRepository moex, ICacheRepository cache)
    {
        _moex = moex;
        _cache = cache;
    }

    public Task<Quote?> GetStockInfoAsync(string ticker, CancellationToken ct = default) =>
        Cached($"quote:{ticker.ToUpperInvariant()}", QuoteTtl,
            () => _moex.GetQuoteAsync(ticker.ToUpperInvariant(), AssetClass.Share, ct));

    public Task<Quote?> GetBondInfoAsync(string ticker, CancellationToken ct = default) =>
        Cached($"bondquote:{ticker.ToUpperInvariant()}", QuoteTtl,
            () => _moex.GetBondQuoteAsync(ticker.ToUpperInvariant(), ct));

    public Task<IReadOnlyList<MetalPrice>> GetMetalPricesAsync(CancellationToken ct = default) =>
        Cached("metals", QuoteTtl, () => _moex.GetMetalPricesAsync(ct))!;

    public async Task<IReadOnlyList<Quote>> GetTopGainersAsync(int limit, CancellationToken ct = default) =>
        (await GetAllSharesAsync(ct))
            .Where(q => q.ChangePercent is not null)
            .OrderByDescending(q => q.ChangePercent)
            .Take(limit)
            .ToList();

    public async Task<IReadOnlyList<Quote>> GetTopLosersAsync(int limit, CancellationToken ct = default) =>
        (await GetAllSharesAsync(ct))
            .Where(q => q.ChangePercent is not null)
            .OrderBy(q => q.ChangePercent)
            .Take(limit)
            .ToList();

    public async Task<IReadOnlyList<Quote>> GetTopBondGainersAsync(int limit, CancellationToken ct = default) =>
        (await GetAllBondsAsync(ct))
            .Where(q => q.ChangePercent is not null)
            .OrderByDescending(q => q.ChangePercent)
            .Take(limit)
            .ToList();

    public async Task<IReadOnlyList<Quote>> GetTopBondLosersAsync(int limit, CancellationToken ct = default) =>
        (await GetAllBondsAsync(ct))
            .Where(q => q.ChangePercent is not null)
            .OrderBy(q => q.ChangePercent)
            .Take(limit)
            .ToList();

    public Task<IReadOnlyList<SecurityInfo>> SearchStocksAsync(string query, CancellationToken ct = default) =>
        Cached($"search:{query.ToUpperInvariant()}", NewsTtl,
            () => _moex.SearchSecuritiesAsync(query, 20, ct))!;

    public Task<IReadOnlyList<SiteNewsItem>> GetNewsAsync(int limit, CancellationToken ct = default) =>
        Cached($"news:{limit}", NewsTtl,
            () => _moex.GetSiteNewsAsync(limit, ct))!;

    public Task<IReadOnlyList<IndexQuote>> GetIndicesAsync(CancellationToken ct = default) =>
        Cached("indices", QuoteTtl, () => _moex.GetIndicesAsync(ct))!;

    public Task<IReadOnlyList<CurrencyRate>> GetCurrencyRatesAsync(CancellationToken ct = default) =>
        Cached("currency", QuoteTtl, () => _moex.GetCurrencyRatesAsync(ct))!;

    /// <summary>Все акции TQBR — используется и топами, и снапшотами, поэтому кэшируем.</summary>
    public Task<IReadOnlyList<Quote>> GetAllSharesAsync(CancellationToken ct = default) =>
        Cached("shares:all", SharesListTtl, () => _moex.GetAllShareQuotesAsync(ct))!;

    /// <summary>Все облигации (TQCB + TQOB) — для топов.</summary>
    private Task<IReadOnlyList<Quote>> GetAllBondsAsync(CancellationToken ct = default) =>
        Cached("bonds:all", SharesListTtl, () => _moex.GetAllBondQuotesAsync(ct))!;

    private async Task<T?> Cached<T>(string key, TimeSpan ttl, Func<Task<T>> fetch)
    {
        var cached = await _cache.GetAsync<T>(key);
        if (cached is not null)
            return cached;

        var fresh = await fetch();
        if (fresh is not null)
            await _cache.SetAsync(key, fresh, ttl);
        return fresh;
    }
}
