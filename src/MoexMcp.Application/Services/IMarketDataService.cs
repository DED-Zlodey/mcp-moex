using MoexMcp.Domain.Models;

namespace MoexMcp.Application.Services;

/// <summary>
/// Предоставляет методы для получения биржевой информации, новостей и справочных данных.
/// </summary>
public interface IMarketDataService
{
    /// <summary>
    /// Асинхронно получает текущую котировку указанной акции на MOEX.
    /// Реализация сервиса может кэшировать результат на ограниченное время.
    /// </summary>
    /// <param name="ticker">Тикер акции, для которой запрашивается котировка.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>
    /// Объект <see cref="Quote"/> с текущей котировкой акции
    /// или <c>null</c>, если инструмент не найден.
    /// </returns>
    Task<Quote?> GetStockInfoAsync(string ticker, CancellationToken ct = default);

    /// <summary>
    /// Асинхронно получает текущую котировку облигации по её тикеру на Московской бирже, включая цену в процентах от номинала, доходность (YTM), НКД, изменение цены, объём торгов и время обновления данных.
    /// </summary>
    /// <param name="ticker">Тикер облигации, для которого запрашивается информация, например SU26243RMFS4.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Объект <see cref="Quote"/>, содержащий данные об облигации, или <c>null</c>, если инструмент не найден.</returns>
    Task<Quote?> GetBondInfoAsync(string ticker, CancellationToken ct = default);

    /// <summary>
    /// Асинхронно получает текущие цены драгоценных металлов на MOEX.
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <return>Задача, результатом которой является список цен металлов.</return>
    Task<IReadOnlyList<MetalPrice>> GetMetalPricesAsync(CancellationToken ct = default);

    /// <summary>
    /// Возвращает список акций с наибольшим ростом за текущий день.
    /// </summary>
    /// <param name="limit">Максимальное количество акций в возвращаемом списке.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список котировок акций с наибольшим положительным изменением цены, упорядоченных по убыванию процента изменения.</returns>
    Task<IReadOnlyList<Quote>> GetTopGainersAsync(int limit, CancellationToken ct = default);

    /// <summary>
    /// Возвращает список акций MOEX с наибольшим падением цены за текущий торговый день.
    /// </summary>
    /// <param name="limit">Максимальное количество акций в возвращаемом списке.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Коллекция котировок акций, отсортированных по возрастанию изменения цены в процентах.</returns>
    Task<IReadOnlyList<Quote>> GetTopLosersAsync(int limit, CancellationToken ct = default);

    /// <summary>
    /// Возвращает список облигаций MOEX с наибольшим ростом цены за текущий торговый день.
    /// </summary>
    /// <param name="limit">Максимальное количество облигаций, возвращаемых в результате.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список котировок облигаций, отсортированных по убыванию процента изменения цены.</returns>
    Task<IReadOnlyList<Quote>> GetTopBondGainersAsync(int limit, CancellationToken ct = default);

    /// <summary>
    /// Возвращает список облигаций MOEX с наибольшим отрицательным изменением цены за текущий день.
    /// Облигации отбираются и сортируются по процентному изменению цены в порядке возрастания.
    /// </summary>
    /// <param name="limit">Максимальное количество облигаций, возвращаемых в результате.</param>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <returns>Список котировок облигаций, показывающих наибольшее падение цены в процентах.</returns>
    Task<IReadOnlyList<Quote>> GetTopBondLosersAsync(int limit, CancellationToken ct = default);

    /// <summary>
    /// Выполняет поиск ценных бумаг на Московской бирже по фрагменту названия или тикера.
    /// </summary>
    /// <param name="query">Поисковый запрос — часть названия или тикера ценной бумаги.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список найденных ценных бумаг в виде объектов <see cref="SecurityInfo"/>.</returns>
    Task<IReadOnlyList<SecurityInfo>> SearchStocksAsync(string query, CancellationToken ct = default);

    /// <summary>Возвращает список последних новостей с сайта Московской биржи.</summary>
    /// <param name="limit">Максимальное количество новостей для получения.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача, результатом выполнения которой является коллекция новостей <see cref="SiteNewsItem"/>.</returns>
    Task<IReadOnlyList<SiteNewsItem>> GetNewsAsync(int limit, CancellationToken ct = default);

    /// <summary>
    /// Асинхронно получает текущие значения биржевых индексов, таких как IMOEX, RTSI и т.п.
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список котировок индексов.</returns>
    Task<IReadOnlyList<IndexQuote>> GetIndicesAsync(CancellationToken ct = default);

    /// <summary>Асинхронно получает курсы валютных пар с Московской биржи.</summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Список курсов валютных пар.</returns>
    Task<IReadOnlyList<CurrencyRate>> GetCurrencyRatesAsync(CancellationToken ct = default);
}
