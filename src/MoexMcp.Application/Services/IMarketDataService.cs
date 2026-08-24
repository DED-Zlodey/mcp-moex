using MoexMcp.Domain.Models;

namespace MoexMcp.Application.Services;

public interface IMarketDataService
{
    Task<Quote?> GetStockInfoAsync(string ticker, CancellationToken ct = default);
    Task<IReadOnlyList<Quote>> GetTopGainersAsync(int limit, CancellationToken ct = default);
    Task<IReadOnlyList<Quote>> GetTopLosersAsync(int limit, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityInfo>> SearchStocksAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<SiteNewsItem>> GetNewsAsync(int limit, CancellationToken ct = default);
    Task<IReadOnlyList<IndexQuote>> GetIndicesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CurrencyRate>> GetCurrencyRatesAsync(CancellationToken ct = default);
}
