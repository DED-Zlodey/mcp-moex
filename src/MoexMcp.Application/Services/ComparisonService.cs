using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Application.Services;

/// <summary>
/// Сравнения и ранжирование по доходности. Локальной истории нет:
/// цены — дневные закрытия ISS (per-ticker для compare, board-wide для rank),
/// board-wide история кэшируется (прошлые дни неизменны).
/// </summary>
public class ComparisonService : IComparisonService
{
    /// <summary>
    /// Сколько дней назад ищем последний торговый день (выходные, длинные праздники).
    /// </summary>
    private const int MaxLookbackDays = 10;

    /// <summary>
    /// Прошлые дни неизменны — кэшируем надолго; сегодняшний может «дозреть» после закрытия сессии.
    /// </summary>
    private static readonly TimeSpan PastDayCacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Время жизни кэша для данных текущего торгового дня: цены могут обновляться в течение сессии и появляются в истории только после её закрытия.
    /// </summary>
    private static readonly TimeSpan TodayCacheTtl = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Репозиторий данных Московской биржи (ISS) для получения исторических цен и рыночных закрытий.
    /// </summary>
    private readonly IMoexRepository _moex;

    /// <summary>
    /// TTL-кэш для хранения дневных закрытий по всем инструментам сектора.
    /// </summary>
    private readonly ICacheRepository _cache;

    /// <summary>
    /// Сравнения и ранжирование по доходности. Локальной истории нет:
    /// цены — дневные закрытия ISS (per-ticker для compare, board-wide для rank),
    /// board-wide история кэшируется (прошлые дни неизменны).
    /// </summary>
    public ComparisonService(IMoexRepository moex, ICacheRepository cache)
    {
        _moex = moex;
        _cache = cache;
    }

    public async Task<IReadOnlyList<InstrumentPerformance>> CompareInstrumentsAsync(
        IReadOnlyList<string> tickers, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        var result = new List<InstrumentPerformance>();

        foreach (var raw in tickers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var ticker = raw.ToUpperInvariant();
            var start = await GetPriceAtAsync(ticker, from, assetClass, ct);
            var end = await GetPriceAtAsync(ticker, to, assetClass, ct);
            if (start is null || end is null || start.Value.Price <= 0)
                continue;

            var changePercent = (end.Value.Price - start.Value.Price) / start.Value.Price * 100m;
            result.Add(new InstrumentPerformance(
                ticker,
                ticker,
                start.Value.Price,
                end.Value.Price,
                Math.Round(changePercent, 2),
                start.Value.Time,
                end.Value.Time,
                "history",
                assetClass));
        }

        return result.OrderByDescending(p => p.ChangePercent).ToList();
    }

    public async Task<IReadOnlyList<InstrumentPerformance>?> RankByPerformanceAsync(
        DateTime from, DateTime to, int limit, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        var start = await GetBoardClosesAsync(from, assetClass, ct);
        var end = await GetBoardClosesAsync(to, assetClass, ct);
        if (start is null || end is null)
            return null;

        var startPrices = start.Value.Closes.ToDictionary(p => p.Ticker, p => p.Close, StringComparer.OrdinalIgnoreCase);

        var result = new List<InstrumentPerformance>();
        foreach (var endPrice in end.Value.Closes)
        {
            if (!startPrices.TryGetValue(endPrice.Ticker, out var startClose) || startClose <= 0)
                continue; // не торговался на начало периода

            var changePercent = (endPrice.Close - startClose) / startClose * 100m;
            result.Add(new InstrumentPerformance(
                endPrice.Ticker,
                endPrice.Ticker,
                startClose,
                endPrice.Close,
                Math.Round(changePercent, 2),
                start.Value.Day,
                end.Value.Day,
                "history",
                assetClass));
        }

        return result.OrderByDescending(p => p.ChangePercent).Take(limit).ToList();
    }

    /// <summary>
    /// Возвращает последнее дневное закрытие тикера, датированное не позже указанного момента.
    /// Поиск выполняется в окне до <c>MaxLookbackDays</c> дней до момента.
    /// </summary>
    /// <param name="ticker">Код тикера.</param>
    /// <param name="moment">Максимальная дата и время, на которые требуется цена.</param>
    /// <param name="assetClass">Класс актива.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>
    /// Кортеж с ценой закрытия и её датой, либо <c>null</c>, если история цен отсутствует.
    /// </returns>
    private async Task<(decimal Price, DateTime Time)?> GetPriceAtAsync(
        string ticker, DateTime moment, AssetClass assetClass, CancellationToken ct)
    {
        var history = await _moex.GetPriceHistoryAsync(ticker, moment.AddDays(-MaxLookbackDays), moment, assetClass, ct);
        var last = history.LastOrDefault();
        return last is null ? null : (last.Close, last.Date);
    }

    /// <summary>
    /// Получает дневные закрытия всех инструментов указанного класса за последний торговый день,
    /// не позже заданного момента. Выполняет обратный поиск в пределах максимального периода lookback.
    /// </summary>
    /// <param name="moment">Момент времени, не позже которого ищется торговый день.</param>
    /// <param name="assetClass">Класс актива для получения закрытий.</param>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <return>Кортеж с датой найденного торгового дня и списком дневных закрытий;
    /// или null, если данные отсутствуют в пределах допустимого периода lookback.</return>
    private async Task<(DateTime Day, IReadOnlyList<DailyPrice> Closes)?> GetBoardClosesAsync(
        DateTime moment, AssetClass assetClass, CancellationToken ct)
    {
        for (var day = moment.Date; day >= moment.Date.AddDays(-MaxLookbackDays); day = day.AddDays(-1))
        {
            var closes = await GetCachedBoardClosesAsync(day, assetClass, ct);
            if (closes.Count > 0)
                return (day, closes);
        }
        return null;
    }

    /// <summary>
    /// Возвращает дневные закрытия по доске для указанного дня и класса активов с кэшированием.
    /// Результаты для прошедших дней сохраняются в кэше на длительный срок, за текущий день — на короткий.
    /// Пустые результаты не кэшируются, чтобы после закрытия торговой сессии загрузить актуальные данные.
    /// </summary>
    /// <param name="day">Торговый день, для которого запрашиваются закрытия.</param>
    /// <param name="assetClass">Класс актива MOEX.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список дневных цен закрытия инструментов.</returns>
    private async Task<IReadOnlyList<DailyPrice>> GetCachedBoardClosesAsync(
        DateTime day, AssetClass assetClass, CancellationToken ct)
    {
        var key = $"boardcloses:{assetClass}:{day:yyyy-MM-dd}";
        var cached = await _cache.GetAsync<List<DailyPrice>>(key, ct);
        if (cached is not null)
            return cached;

        var closes = await _moex.GetMarketDailyClosesAsync(day, assetClass, ct);
        if (closes.Count == 0)
            return closes; // пустое не кэшируем: сегодняшний день появится в истории после закрытия сессии

        var ttl = day < DateTime.UtcNow.Date ? PastDayCacheTtl : TodayCacheTtl;
        await _cache.SetAsync(key, closes.ToList(), ttl, ct);
        return closes;
    }
}
