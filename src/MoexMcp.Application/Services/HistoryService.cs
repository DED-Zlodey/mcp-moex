using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Application.Services;

public class HistoryService : IHistoryService
{
    /// <summary>
    /// Время жизни кэшированных свечей в кэше.
    /// </summary>
    private static readonly TimeSpan CandlesTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Время жизни кэшированных исторических данных по дневным ценам.
    /// Установлено в 24 часа, поскольку дневная история считается неизменной.
    /// </summary>
    private static readonly TimeSpan HistoryTtl = TimeSpan.FromHours(24); // дневная история неизменна

    /// <summary>
    /// Массив допустимых значений интервала свечей: 1, 10, 60 минут и 24 часа (дневной интервал).
    /// </summary>
    private static readonly int[] AllowedIntervals = [1, 10, 60, 24];

    /// <summary>
    /// Репозиторий для получения исторических и рыночных данных с Московской биржи.
    /// </summary>
    private readonly IMoexRepository _moex;

    /// <summary>
    /// Хранилище TTL-кэша для сохранения и получения кэшированных свечей и истории цен.
    /// </summary>
    private readonly ICacheRepository _cache;

    /// <summary>
    /// Сервис исторических рыночных данных.
    /// Предоставляет методы получения свечей и истории цен, проверяет допустимость интервала свечей
    /// и применяет кэширование с учётом класса актива.
    /// </summary>
    /// <summary>
    /// Инициализирует новый экземпляр сервиса исторических данных.
    /// </summary>
    /// <param name="moex">Репозиторий данных Московской биржи.</param>
    /// <param name="cache">Репозиторий кэша.</param>
    public HistoryService(IMoexRepository moex, ICacheRepository cache)
    {
        _moex = moex;
        _cache = cache;
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string ticker, int intervalMinutes, DateTime from,
        DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        if (!AllowedIntervals.Contains(intervalMinutes))
            throw new ArgumentException($"Интервал {intervalMinutes} не поддерживается. Допустимые: 1, 10, 60, 24.",
                nameof(intervalMinutes));

        // Класс актива в ключе — чтобы кэш свечей акции и облигации с одним тикером не пересекался
        var key =
            $"candles:{assetClass.ToString().ToLowerInvariant()}:{ticker.ToUpperInvariant()}:{intervalMinutes}:{from:yyyyMMdd}:{to:yyyyMMdd}";
        var cached = await _cache.GetAsync<List<Candle>>(key);
        if (cached is not null)
            return cached;

        var fresh = await _moex.GetCandlesAsync(ticker.ToUpperInvariant(), intervalMinutes, from, to, assetClass, ct);
        await _cache.SetAsync(key, fresh.ToList(), CandlesTtl);
        return fresh;
    }

    public async Task<IReadOnlyList<DailyPrice>> GetPriceHistoryAsync(string ticker, DateTime from, DateTime to,
        AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        var key =
            $"history:{assetClass.ToString().ToLowerInvariant()}:{ticker.ToUpperInvariant()}:{from:yyyyMMdd}:{to:yyyyMMdd}";
        var cached = await _cache.GetAsync<List<DailyPrice>>(key);
        if (cached is not null)
            return cached;

        var fresh = await _moex.GetPriceHistoryAsync(ticker.ToUpperInvariant(), from, to, assetClass, ct);
        await _cache.SetAsync(key, fresh.ToList(), HistoryTtl);
        return fresh;
    }
}