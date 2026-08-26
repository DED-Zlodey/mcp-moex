using MoexMcp.Domain.Models;

namespace MoexMcp.Domain.Repositories;

/// <summary>
/// Репозиторий для доступа к рыночным и историческим данным Московской биржи,
/// включая котировки, свечи, индексы, валютные курсы и новости.
/// </summary>
public interface IMoexRepository
{
    /// <summary>
    /// Получает текущую котировку инструмента (для акций — основной режим TQBR).
    /// </summary>
    /// <param name="ticker">Тикер инструмента.</param>
    /// <param name="assetClass">Класс актива.</param>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <returns>Текущая котировка инструмента или null, если котировка не найдена.</returns>
    Task<Quote?> GetQuoteAsync(string ticker, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);

    /// <summary>
    /// Котировка облигации: цена в % от номинала, доходность, НКД.
    /// </summary>
    /// <param name="ticker">Тикер облигации.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Котировка облигации или <c>null</c>, если инструмент не найден.</returns>
    Task<Quote?> GetBondQuoteAsync(string ticker, CancellationToken ct = default);

    /// <summary>
    /// Возвращает котировки всех акций основного режима TQBR одним запросом.
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список котировок акций основного режима TQBR.</returns>
    Task<IReadOnlyList<Quote>> GetAllShareQuotesAsync(CancellationToken ct = default);

    /// <summary>
    /// Возвращает котировки всех облигаций основных режимов торгов (TQCB + TQOB).
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список котировок облигаций.</returns>
    Task<IReadOnlyList<Quote>> GetAllBondQuotesAsync(CancellationToken ct = default);

    /// <summary>
    /// Цены драгметаллов (GLDRUB_TOM, SLVRUB_TOM), ₽/грамм.
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список цен драгметаллов.</returns>
    Task<IReadOnlyList<MetalPrice>> GetMetalPricesAsync(CancellationToken ct = default);

    /// <summary>
    /// Поиск ценных бумаг Московской биржи по названию или тикеру.
    /// </summary>
    /// <param name="query">Строка поиска: тикер или часть наименования бумаги.</param>
    /// <param name="limit">Максимальное количество возвращаемых результатов.</param>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <return>Список найденных бумаг в виде объектов <see cref="SecurityInfo"/>; пустой список, если совпадения отсутствуют.</return>
    Task<IReadOnlyList<SecurityInfo>> SearchSecuritiesAsync(string query, int limit = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Получает список новостей с сайта Московской биржи.
    /// </summary>
    /// <param name="limit">Максимальное количество возвращаемых новостей.</param>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <returns>Коллекция новостей с сайта MOEX.</returns>
    Task<IReadOnlyList<SiteNewsItem>> GetSiteNewsAsync(int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Возвращает OHLC-свечи по указанному инструменту за заданный период.
    /// </summary>
    /// <param name="ticker">Тикер инструмента.</param>
    /// <param name="intervalMinutes">Интервал свечи в минутах. Поддерживаются значения 1, 10, 60 (час) и 24 (день).</param>
    /// <param name="from">Начало запрашиваемого периода.</param>
    /// <param name="to">Конец запрашиваемого периода.</param>
    /// <param name="assetClass">Класс актива. По умолчанию используется акция.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Коллекция OHLC-свечей в указанном интервале.</returns>
    Task<IReadOnlyList<Candle>> GetCandlesAsync(string ticker, int intervalMinutes, DateTime from, DateTime to,
        AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);

    /// <summary>
    /// Возвращает дневные цены закрытия инструмента за указанный период.
    /// </summary>
    /// <param name="ticker">Тикер инструмента.</param>
    /// <param name="from">Начальная дата периода.</param>
    /// <param name="to">Конечная дата периода.</param>
    /// <param name="assetClass">Класс актива. По умолчанию — акции.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список дневных цен закрытия за запрошенный период.</returns>
    Task<IReadOnlyList<DailyPrice>> GetPriceHistoryAsync(string ticker, DateTime from, DateTime to,
        AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);

    /// <summary>
    /// Дневные закрытия всех инструментов класса за конкретный торговый день (board-wide история ISS).
    /// Если в этот день торгов не было, возвращает пустой список.
    /// </summary>
    /// <param name="day">Торговый день, за который запрашиваются закрытия.</param>
    /// <param name="assetClass">Класс активов, по инструментам которого выполняется запрос.</param>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <return>Задача, результатом которой является список дневных закрытий по инструментам.</return>
    Task<IReadOnlyList<DailyPrice>> GetMarketDailyClosesAsync(DateTime day, AssetClass assetClass = AssetClass.Share,
        CancellationToken ct = default);

    /// <summary>
    /// Получает актуальные значения индексов Московской биржи (IMOEX, RTSI и т.п.).
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список текущих котировок индексов.</returns>
    Task<IReadOnlyList<IndexQuote>> GetIndicesAsync(CancellationToken ct = default);

    /// <summary>
    /// Возвращает курсы валют (USD/RUB, EUR/RUB).
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список курсов валютных пар.</returns>
    Task<IReadOnlyList<CurrencyRate>> GetCurrencyRatesAsync(CancellationToken ct = default);
}