using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Tests.Application;

/// <summary>
/// Фейк IMoexRepository: возвращает заготовленные данные, считает вызовы.
/// </summary>
internal class FakeMoexRepository : IMoexRepository
{
    /// <summary>
    /// Коллекция всех котировок акций, используемая в тестовом репозитории.
    /// </summary>
    public IReadOnlyList<Quote> AllQuotes { get; set; } = [];

    /// <summary>
    /// Все котировки облигаций.
    /// </summary>
    public IReadOnlyList<Quote> AllBondQuotes { get; set; } = [];

    /// <summary>
    /// Цены на металлы.
    /// </summary>
    public IReadOnlyList<MetalPrice> MetalPrices { get; set; } = [];

    /// <summary>
    /// Общая история дневных цен, используемая по умолчанию, когда для конкретного тикера не задана история в HistoryByTicker.
    /// </summary>
    public IReadOnlyList<DailyPrice> History { get; set; } = [];

    /// <summary>
    /// История по конкретному тикеру (приоритет над History).
    /// </summary>
    public Dictionary<string, IReadOnlyList<DailyPrice>> HistoryByTicker { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Дневные закрытия всего класса по датам (board-wide история).
    /// </summary>
    public Dictionary<(AssetClass Class, DateTime Day), IReadOnlyList<DailyPrice>> BoardCloses { get; } = [];

    /// <summary>
    /// Коллекция свечей, используемая в тестовом репозитории для имитации данных, возвращаемых методом <see cref="GetCandlesAsync"/>.
    /// </summary>
    public IReadOnlyList<Candle> Candles { get; set; } = [];

    /// <summary>
    /// Счётчик вызовов метода получения всех котировок акций в тестовом репозитории.
    /// Используется для проверки кэширования повторных обращений.
    /// </summary>
    public int AllQuotesCalls { get; private set; }

    /// <summary>
    /// Количество вызовов метода получения всех котировок облигаций.
    /// </summary>
    public int AllBondsCalls { get; private set; }

    /// <summary>
    /// Количество вызовов метода получения цен на металлы в фейковом репозитории.
    /// </summary>
    public int MetalsCalls { get; private set; }

    /// <summary>
    /// Счётчик вызовов метода получения котировки облигации.
    /// </summary>
    public int BondQuoteCalls { get; private set; }

    /// <summary>
    /// Количество вызовов метода получения исторических цен.
    /// </summary>
    public int HistoryCalls { get; private set; }

    /// <summary>
    /// Количество вызовов метода GetCandlesAsync.
    /// </summary>
    public int CandlesCalls { get; private set; }

    /// <summary>
    /// Счётчик вызовов метода получения ежедневных закрытий рынка.
    /// </summary>
    public int BoardClosesCalls { get; private set; }

    /// <summary>
    /// Класс актива, переданный в последнем вызове метода получения истории цен в фиктивном репозитории.
    /// </summary>
    public AssetClass? LastHistoryAssetClass { get; private set; }

    /// <summary>
    /// Класс актива, переданный при последнем вызове <see cref="GetCandlesAsync"/>.
    /// </summary>
    public AssetClass? LastCandlesAssetClass { get; private set; }

    /// <summary>
    /// Класс актива, переданный в последнем вызове метода получения дневных закрытий по рынку.
    /// </summary>
    public AssetClass? LastBoardClosesAssetClass { get; private set; }

    public Task<Quote?> GetQuoteAsync(string ticker, AssetClass assetClass = AssetClass.Share,
        CancellationToken ct = default) =>
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

    public Task<IReadOnlyList<SecurityInfo>> SearchSecuritiesAsync(string query, int limit = 20,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SecurityInfo>>([]);

    public Task<IReadOnlyList<SiteNewsItem>> GetSiteNewsAsync(int limit = 20, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SiteNewsItem>>([]);

    public Task<IReadOnlyList<Candle>> GetCandlesAsync(string ticker, int intervalMinutes, DateTime from, DateTime to,
        AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        CandlesCalls++;
        LastCandlesAssetClass = assetClass;
        return Task.FromResult(Candles);
    }

    public Task<IReadOnlyList<DailyPrice>> GetPriceHistoryAsync(string ticker, DateTime from, DateTime to,
        AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        HistoryCalls++;
        LastHistoryAssetClass = assetClass;
        // Эмулируем ISS: отдаём строки не позже границы to
        var rows = HistoryByTicker.TryGetValue(ticker, out var v) ? v : History;
        return Task.FromResult<IReadOnlyList<DailyPrice>>(rows.Where(d => d.Date <= to).ToList());
    }

    public Task<IReadOnlyList<DailyPrice>> GetMarketDailyClosesAsync(DateTime day,
        AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        BoardClosesCalls++;
        LastBoardClosesAssetClass = assetClass;
        return Task.FromResult(BoardCloses.TryGetValue((assetClass, day.Date), out var v)
            ? v
            : (IReadOnlyList<DailyPrice>)[]);
    }

    public Task<IReadOnlyList<IndexQuote>> GetIndicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IndexQuote>>([]);

    public Task<IReadOnlyList<CurrencyRate>> GetCurrencyRatesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CurrencyRate>>([]);
}

/// <summary>
/// In-memory фейк кэша.
/// </summary>
internal class FakeCacheRepository : ICacheRepository
{
    /// <summary>
    /// Внутреннее хранилище кэшированных данных в оперативной памяти.
    /// </summary>
    private readonly Dictionary<string, object> _data = new();

    /// <summary>
    /// Счетчик вызовов метода получения значения из кэша.
    /// </summary>
    private int GetCalls { get; set; }

    /// <summary>
    /// Количество вызовов метода SetAsync в фейковом репозитории кэша.
    /// </summary>
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

internal static class TestData
{
    /// <summary>
    /// Создает тестовую котировку инструмента с заданными параметрами и значениями по умолчанию.
    /// </summary>
    /// <param name="ticker">Тикер инструмента.</param>
    /// <param name="price">Цена инструмента.</param>
    /// <param name="changePercent">Процентное изменение цены.</param>
    /// <param name="time">Время котировки. Если не указано, используется значение 21.08.2026 10:00:00.</param>
    /// <param name="assetClass">Класс актива. Если не указан, используется <see cref="AssetClass.Share"/>.</param>
    /// <returns>Экземпляр <see cref="Quote"/> с заполненными полями.</returns>
    public static Quote Quote(string ticker, decimal? price, decimal? changePercent, DateTime? time = null,
        AssetClass assetClass = AssetClass.Share) =>
        new(ticker, $"Name {ticker}", price, null, changePercent, 1000, time ?? new DateTime(2026, 8, 21, 10, 0, 0),
            assetClass, assetClass.PriceUnit());
}