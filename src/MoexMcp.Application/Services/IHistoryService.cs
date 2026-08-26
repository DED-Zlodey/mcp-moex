using MoexMcp.Domain.Models;

namespace MoexMcp.Application.Services;

/// <summary>
/// Предоставляет методы для получения исторических торговых данных инструментов.
/// </summary>
/// <summary>
/// Асинхронно возвращает свечи указанного инструмента за заданный период.
/// </summary>
/// <param name="ticker">Тикер инструмента.</param>
/// <param name="intervalMinutes">Интервал свечи в минутах.</param>
/// <param name="from">Начало периода.</param>
/// <param name="to">Конец периода.</param>
/// <param name="assetClass">Класс актива инструмента.</param>
/// <param name="ct">Токен отмены операции.</param>
/// <returns>Коллекция свечей, доступная только для чтения.</returns>
/// <summary>
/// Асинхронно возвращает историю цен закрытия указанного инструмента за заданный период.
/// </summary>
/// <param name="ticker">Тикер инструмента.</param>
/// <param name="from">Начало периода.</param>
/// <param name="to">Конец периода.</param>
/// <param name="assetClass">Класс актива инструмента.</param>
/// <param name="ct">Токен отмены операции.</param>
/// <returns>Коллекция дневных цен закрытия, доступная только для чтения.</returns>
public interface IHistoryService
{
    /// <summary>
    /// Асинхронно возвращает исторические свечи (OHLCV) для указанного инструмента за заданный период.
    /// </summary>
    /// <param name="ticker">Тикер инструмента на MOEX.</param>
    /// <param name="intervalMinutes">Интервал свечи в минутах. Поддерживаются только значения 1, 10, 60 и 24.</param>
    /// <param name="from">Начальная дата и время запрашиваемого периода.</param>
    /// <param name="to">Конечная дата и время запрашиваемого периода.</param>
    /// <param name="assetClass">Класс актива инструмента.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача, результатом которой является доступный только для чтения список свечей.</returns>
    Task<IReadOnlyList<Candle>> GetCandlesAsync(string ticker, int intervalMinutes, DateTime from, DateTime to,
        AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);

    /// <summary>
    /// Асинхронно получает историю дневных цен закрытия инструмента за указанный период.
    /// </summary>
    /// <param name="ticker">Тикер инструмента на MOEX.</param>
    /// <param name="from">Начало периода.</param>
    /// <param name="to">Конец периода.</param>
    /// <param name="assetClass">Класс актива. По умолчанию <see cref="AssetClass.Share"/>.</param>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <returns>Задача, представляющая асинхронную операцию, результатом которой является коллекция дневных цен закрытия.</returns>
    Task<IReadOnlyList<DailyPrice>> GetPriceHistoryAsync(string ticker, DateTime from, DateTime to,
        AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);
}