using MoexMcp.Domain.Models;

namespace MoexMcp.Application.Services;

/// <summary>
/// Доходность одного инструмента за период.
/// </summary>
public record InstrumentPerformance(
    string Ticker,
    string Name,
    decimal StartPrice,
    decimal EndPrice,
    decimal ChangePercent,
    DateTime StartPriceTime,
    DateTime EndPriceTime,
    string PriceSource,
    AssetClass Class = AssetClass.Share);

public interface IComparisonService
{
    /// <summary>
    /// Сравнивает инструменты по доходности за указанный период.
    /// Результат отсортирован по убыванию процента изменения цены.
    /// Все инструменты должны принадлежать к одному классу активов; смешанное сравнение не поддерживается.
    /// </summary>
    /// <param name="tickers">Список тикеров инструментов для сравнения.</param>
    /// <param name="from">Начальная дата периода.</param>
    /// <param name="to">Конечная дата периода.</param>
    /// <param name="assetClass">Класс активов сравниваемых инструментов. По умолчанию — акции.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <return>Список результатов сравнения, упорядоченный по убыванию доходности.</return>
    Task<IReadOnlyList<InstrumentPerformance>> CompareInstrumentsAsync(
        IReadOnlyList<string> tickers, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share,
        CancellationToken ct = default);

    /// <summary>
    /// Ранжирует инструменты одного класса активов по доходности за указанный период.
    /// Доходность рассчитывается по дневным ценам закрытия ISS. Используется последний торговый день, не позже границ периода.
    /// Возвращает null, если ISS не предоставил исторические данные.
    /// </summary>
    /// <param name="from">Начальная дата периода.</param>
    /// <param name="to">Конечная дата периода.</param>
    /// <param name="limit">Максимальное количество инструментов в результате.</param>
    /// <param name="assetClass">Класс активов инструментов. По умолчанию — акции.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <return>Список инструментов, упорядоченных по доходности, или null, если исторические данные недоступны.</return>
    Task<IReadOnlyList<InstrumentPerformance>?> RankByPerformanceAsync(
        DateTime from, DateTime to, int limit, AssetClass assetClass = AssetClass.Share,
        CancellationToken ct = default);
}
