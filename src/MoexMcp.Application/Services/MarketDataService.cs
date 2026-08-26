using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Application.Services;

/// <summary>
/// Рыночные данные с кэшированием. Слой не знает, что кэш — это Redis.
/// </summary>
public class MarketDataService : IMarketDataService
{
    /// <summary>
    /// Время жизни кэша для котировок отдельных ценных бумаг, курсов валют, индексов и цен на металлы.
    /// </summary>
    /// <remarks>
    /// Значение по умолчанию — 30 секунд.
    /// </remarks>
    private static readonly TimeSpan QuoteTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Время жизни кэша для полных списков акций и облигаций, используемых при построении топов и снапшотов.
    /// </summary>
    /// <remarks>
    /// Значение по умолчанию — 60 секунд.
    /// </remarks>
    private static readonly TimeSpan SharesListTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Время жизни кэша для новостей и результатов поиска.
    /// </summary>
    private static readonly TimeSpan NewsTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Репозиторий для получения рыночных данных от Московской биржи.
    /// </summary>
    private readonly IMoexRepository _moex;

    /// <summary>
    /// Репозиторий кэша, используемый для кратковременного хранения рыночных данных.
    /// Абстрагирует приложение от конкретной реализации кэша.
    /// </summary>
    private readonly ICacheRepository _cache;

    /// <summary>
    /// Сервис рыночных данных, предоставляющий доступ к котировкам акций, облигаций, металлов, индексов, валютных курсов и новостей с кэшированием результатов запросов.
    /// </summary>
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

    /// <summary>
    /// Асинхронно получает все акции режима TQBR с кэшированием результата.
    /// </summary>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <returns>Список котировок всех акций.</returns>
    private Task<IReadOnlyList<Quote>> GetAllSharesAsync(CancellationToken ct = default) =>
        Cached("shares:all", SharesListTtl, () => _moex.GetAllShareQuotesAsync(ct))!;

    /// <summary>
    /// Все облигации (TQCB + TQOB) — используется для формирования топов, поэтому кэшируется.
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача, результатом которой является доступный только для чтения список котировок облигаций.</returns>
    private Task<IReadOnlyList<Quote>> GetAllBondsAsync(CancellationToken ct = default) =>
        Cached("bonds:all", SharesListTtl, () => _moex.GetAllBondQuotesAsync(ct))!;

    /// <summary>
    /// Возвращает значение из кэша по заданному ключу. Если значение отсутствует,
    /// выполняет асинхронную загрузку из источника, сохраняет полученный результат
    /// в кэш с указанным временем жизни и возвращает его.
    /// </summary>
    /// <typeparam name="T">Тип кэшируемого значения.</typeparam>
    /// <param name="key">Ключ, по которому хранится значение в кэше.</param>
    /// <param name="ttl">Время жизни записи в кэше.</param>
    /// <param name="fetch">Функция, выполняющая асинхронную выборку значения из источника.</param>
    /// <returns>Задача, результат которой содержит значение из кэша или только что полученное из источника значение. Возвращает значение по умолчанию, если выборка не вернула результат.</returns>
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
