using System.ComponentModel;
using System.Text;
using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;
using ModelContextProtocol.Server;

namespace MoexMcp.Host.Tools;

/// <summary>
/// Исторические данные MOEX: свечи и дневная история (доступны круглосуточно, не зависят от сессии).
/// </summary>
[McpServerToolType]
public class MoexHistoryTools
{
    private readonly IHistoryService _history;

    /// <summary>
    /// Инструменты MCP-сервера для получения исторических биржевых данных.
    /// </summary>
    public MoexHistoryTools(IHistoryService history)
    {
        _history = history;
    }

    /// <summary>
    /// Получает OHLC-свечи указанного инструмента за заданный период.
    /// </summary>
    /// <param name="ticker">Тикер инструмента (например SBER или SU26243RMFS4).</param>
    /// <param name="interval">Интервал свечи в минутах: 1, 10, 60 (час) или 24 (день). По умолчанию 60.</param>
    /// <param name="from">Начало периода в формате yyyy-MM-dd. По умолчанию 7 дней назад.</param>
    /// <param name="to">Конец периода в формате yyyy-MM-dd. По умолчанию сегодня.</param>
    /// <param name="asset_type">Тип актива: share, bond, currency, metal. По умолчанию share.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <return>Строка с таблицей свечей или сообщение об ошибке.</return>
    [McpServerTool(Name = "get_candles"), Description("Получить OHLC-свечи инструмента: открытие, закрытие, максимум, минимум, объём")]
    public async Task<string> GetCandles(
        [Description("Тикер инструмента (например SBER или SU26243RMFS4)")] string ticker,
        [Description("Интервал свечи в минутах: 1, 10, 60 (час) или 24 (день). По умолчанию 60")] int interval = 60,
        [Description("Начало периода, yyyy-MM-dd. По умолчанию 7 дней назад")] string? from = null,
        [Description("Конец периода, yyyy-MM-dd. По умолчанию сегодня")] string? to = null,
        [Description("Тип актива: share, bond, currency, metal. По умолчанию share")] string asset_type = "share",
        CancellationToken ct = default)
    {
        var assetClass = Format.ParseAssetClass(asset_type);
        if (assetClass is null)
            return Format.InvalidAssetClass(asset_type);

        var fromDate = Format.ParseDate(from, DateTime.Today.AddDays(-7));
        var toDate = Format.ParseDate(to, DateTime.Today);

        IReadOnlyList<Candle> candles;
        try
        {
            candles = await _history.GetCandlesAsync(ticker, interval, fromDate, toDate, assetClass.Value, ct);
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }

        if (candles.Count == 0)
            return $"Свечей по {ticker} за {fromDate:dd.MM.yyyy}—{toDate:dd.MM.yyyy} не найдено.";

        var sb = new StringBuilder($"Свечи {ticker.ToUpperInvariant()} ({asset_type}), интервал {interval} мин, {fromDate:dd.MM.yyyy}—{toDate:dd.MM.yyyy} ({candles.Count} шт.):\n");
        sb.AppendLine("Время | O | C | H | L | Объём");
        foreach (var c in candles)
            sb.AppendLine($"{c.Begin:dd.MM HH:mm} | {c.Open:0.00} | {c.Close:0.00} | {c.High:0.00} | {c.Low:0.00} | {c.Volume}");
        return sb.ToString();
    }

    /// <summary>
    /// Возвращает дневные цены закрытия указанного инструмента за заданный период.
    /// </summary>
    /// <param name="ticker">Тикер инструмента (например SBER или SU26243RMFS4).</param>
    /// <param name="from">Начало периода в формате yyyy-MM-dd. По умолчанию используется дата 30 дней назад.</param>
    /// <param name="to">Конец периода в формате yyyy-MM-dd. По умолчанию используется сегодняшняя дата.</param>
    /// <param name="asset_type">Тип актива: share, bond, currency, metal. По умолчанию share.</param>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <returns>Строка с историей цен закрытия инструмента или сообщение об отсутствии данных или некорректном типе актива.</returns>
    [McpServerTool(Name = "get_price_history"), Description("Получить дневные цены закрытия инструмента за период")]
    public async Task<string> GetPriceHistory(
        [Description("Тикер инструмента (например SBER или SU26243RMFS4)")] string ticker,
        [Description("Начало периода, yyyy-MM-dd. По умолчанию 30 дней назад")] string? from = null,
        [Description("Конец периода, yyyy-MM-dd. По умолчанию сегодня")] string? to = null,
        [Description("Тип актива: share, bond, currency, metal. По умолчанию share")] string asset_type = "share",
        CancellationToken ct = default)
    {
        var assetClass = Format.ParseAssetClass(asset_type);
        if (assetClass is null)
            return Format.InvalidAssetClass(asset_type);

        var fromDate = Format.ParseDate(from, DateTime.Today.AddDays(-30));
        var toDate = Format.ParseDate(to, DateTime.Today);

        var prices = await _history.GetPriceHistoryAsync(ticker, fromDate, toDate, assetClass.Value, ct);
        if (prices.Count == 0)
            return $"Истории по {ticker} за {fromDate:dd.MM.yyyy}—{toDate:dd.MM.yyyy} не найдено.";

        var unit = assetClass.Value.PriceUnit();
        var sb = new StringBuilder($"История закрытий {ticker.ToUpperInvariant()} ({asset_type}), {fromDate:dd.MM.yyyy}—{toDate:dd.MM.yyyy} ({prices.Count} дней):\n");
        foreach (var p in prices)
            sb.AppendLine($"{p.Date:dd.MM.yyyy}: {p.Close:0.00} {unit}");
        return sb.ToString();
    }
}
