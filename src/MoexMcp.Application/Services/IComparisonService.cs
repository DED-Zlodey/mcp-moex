namespace MoexMcp.Application.Services;

/// <summary>Доходность одного инструмента за период.</summary>
public record InstrumentPerformance(
    string Ticker,
    string Name,
    decimal StartPrice,
    decimal EndPrice,
    decimal ChangePercent,
    DateTime StartPriceTime,
    DateTime EndPriceTime,
    string PriceSource);

public interface IComparisonService
{
    /// <summary>Сравнить инструменты между собой по доходности за период (отсортированы по убыванию доходности).</summary>
    Task<IReadOnlyList<InstrumentPerformance>> CompareInstrumentsAsync(
        IReadOnlyList<string> tickers, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// Ранжировать все акции TQBR по доходности за период.
    /// Возвращает null, если подходящих снапшотов ещё нет (данные накапливаются воркером).
    /// </summary>
    Task<IReadOnlyList<InstrumentPerformance>?> RankByPerformanceAsync(
        DateTime from, DateTime to, int limit, CancellationToken ct = default);
}
