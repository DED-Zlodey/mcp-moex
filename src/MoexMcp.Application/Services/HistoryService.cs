using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Application.Services;

public class HistoryService : IHistoryService
{
    private static readonly TimeSpan CandlesTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HistoryTtl = TimeSpan.FromHours(24); // дневная история неизменна

    private static readonly int[] AllowedIntervals = [1, 10, 60, 24];

    private readonly IMoexRepository _moex;
    private readonly ICacheRepository _cache;

    public HistoryService(IMoexRepository moex, ICacheRepository cache)
    {
        _moex = moex;
        _cache = cache;
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string ticker, int intervalMinutes, DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (!AllowedIntervals.Contains(intervalMinutes))
            throw new ArgumentException($"Интервал {intervalMinutes} не поддерживается. Допустимые: 1, 10, 60, 24.", nameof(intervalMinutes));

        var key = $"candles:{ticker.ToUpperInvariant()}:{intervalMinutes}:{from:yyyyMMdd}:{to:yyyyMMdd}";
        var cached = await _cache.GetAsync<List<Candle>>(key);
        if (cached is not null)
            return cached;

        var fresh = await _moex.GetCandlesAsync(ticker.ToUpperInvariant(), intervalMinutes, from, to, ct);
        await _cache.SetAsync(key, fresh.ToList(), CandlesTtl);
        return fresh;
    }

    public async Task<IReadOnlyList<DailyPrice>> GetPriceHistoryAsync(string ticker, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var key = $"history:{ticker.ToUpperInvariant()}:{from:yyyyMMdd}:{to:yyyyMMdd}";
        var cached = await _cache.GetAsync<List<DailyPrice>>(key);
        if (cached is not null)
            return cached;

        var fresh = await _moex.GetPriceHistoryAsync(ticker.ToUpperInvariant(), from, to, ct);
        await _cache.SetAsync(key, fresh.ToList(), HistoryTtl);
        return fresh;
    }
}
