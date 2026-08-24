using MoexMcp.Domain.Models;

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
    string PriceSource,
    AssetClass Class = AssetClass.Share);

public interface IComparisonService
{
    /// <summary>
    /// Сравнить инструменты между собой по доходности за период (отсортированы по убыванию доходности).
    /// Класс актива единый на вызов — смешанное сравнение «акция vs облигация» не поддерживается
    /// (у классов разные единицы цены).
    /// </summary>
    Task<IReadOnlyList<InstrumentPerformance>> CompareInstrumentsAsync(
        IReadOnlyList<string> tickers, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);

    /// <summary>
    /// Ранжировать инструменты одного класса по доходности за период.
    /// Возвращает null, если подходящих снапшотов ещё нет (данные накапливаются воркером).
    /// </summary>
    Task<IReadOnlyList<InstrumentPerformance>?> RankByPerformanceAsync(
        DateTime from, DateTime to, int limit, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);
}
