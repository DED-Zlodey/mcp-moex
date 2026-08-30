using System.ComponentModel;
using System.Globalization;
using System.Text;
using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;
using ModelContextProtocol.Server;

namespace MoexMcp.Host.Tools;

/// <summary>
/// Сравнение инструментов и ранжирование по доходности (дневная история ISS).
/// </summary>
[McpServerToolType]
public class MoexCompareTools
{
    private readonly IComparisonService _comparison;

    /// <summary>
    /// Набор инструментов MCP-сервера для сравнения финансовых инструментов Московской биржи.
    /// </summary>
    /// <remarks>
    /// Позволяет сравнивать инструменты одного класса активов по доходности за выбранный период.
    /// </remarks>
    public MoexCompareTools(IComparisonService comparison)
    {
        _comparison = comparison;
    }

    /// <summary>
    /// Сравнивает финансовые инструменты одного класса активов по доходности за указанный период.
    /// </summary>
    /// <param name="tickers">Тикеры инструментов через запятую, например SBER,GAZP,LKOH.</param>
    /// <param name="from">Начало периода в формате yyyy-MM-dd. По умолчанию — 7 дней назад.</param>
    /// <param name="to">Конец периода в формате yyyy-MM-dd. По умолчанию — текущая дата.</param>
    /// <param name="asset_type">Тип актива: share, bond, currency, metal. Должен быть одинаковым для всех переданных тикеров. По умолчанию — share.</param>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <returns>
    /// Строка с таблицей сравнения инструментов, содержащей цены на начало и конец периода,
    /// изменение в процентах и место каждого инструмента в рейтинге доходности.
    /// Возвращает сообщение об ошибке, если указан неподдерживаемый класс активов,
    /// отсутствуют тикеры или не удалось получить цены за выбранный период.
    /// </returns>
    /// <remarks>
    /// Смешанное сравнение инструментов разных классов активов не поддерживается.
    /// </remarks>
    [McpServerTool(Name = "compare_instruments"), Description("Сравнить инструменты одного класса между собой по доходности за период: цена на начало и конец, изменение в %, место в рейтинге. Смешанное сравнение разных классов (акция vs облигация) не поддерживается — сравнивайте отдельными вызовами")]
    public async Task<string> CompareInstruments(
        [Description("Тикеры через запятую (например SBER,GAZP,LKOH)")] string tickers,
        [Description("Начало периода, yyyy-MM-dd. По умолчанию 7 дней назад")] string? from = null,
        [Description("Конец периода, yyyy-MM-dd. По умолчанию сейчас")] string? to = null,
        [Description("Тип актива: share, bond, currency, metal. По умолчанию share. Единый для всех тикеров вызова")] string asset_type = "share",
        CancellationToken ct = default)
    {
        var assetClass = Format.ParseAssetClass(asset_type);
        if (assetClass is null)
            return Format.InvalidAssetClass(asset_type);

        var list = tickers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (list.Length == 0)
            return "Укажите хотя бы один тикер.";

        var fromDate = Format.ParseDate(from, DateTime.UtcNow.AddDays(-7));
        var toDate = Format.ParseDate(to, DateTime.UtcNow);

        var result = await _comparison.CompareInstrumentsAsync(list, fromDate, toDate, assetClass.Value, ct);
        if (result.Count == 0)
            return "Не удалось получить цены ни по одному из указанных тикеров за этот период.";

        return FormatRanking(result, $"Сравнение инструментов ({asset_type}, {fromDate:dd.MM.yyyy} — {toDate:dd.MM.yyyy}):");
    }

    /// <summary>
    /// Ранжирует инструменты Московской биржи одного класса активов по доходности за указанный период.
    /// Расчёт выполняется по дневным ценам закрытия ISS.
    /// </summary>
    /// <param name="from">Начало периода в формате yyyy-MM-dd. По умолчанию используется начало текущего дня.</param>
    /// <param name="to">Конец периода в формате yyyy-MM-dd. По умолчанию используется текущий момент времени.</param>
    /// <param name="limit">Максимальное количество позиций в рейтинге. По умолчанию 20.</param>
    /// <param name="asset_type">Тип актива: share, bond, currency или metal. По умолчанию share.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Строка с рейтингом инструментов по доходности или сообщение об ошибке.</returns>
    [McpServerTool(Name = "rank_by_performance"), Description("Ранжировать инструменты MOEX одного класса по доходности за период. Считается по дневным закрытиям ISS: берётся последний торговый день не позже границ периода, глубина истории — годы")]
    public async Task<string> RankByPerformance(
        [Description("Начало периода, yyyy-MM-dd. По умолчанию начало сегодняшнего дня")] string? from = null,
        [Description("Конец периода, yyyy-MM-dd. По умолчанию сейчас")] string? to = null,
        [Description("Сколько позиций показать (по умолчанию 20)")] int limit = 20,
        [Description("Тип актива: share, bond, currency, metal. По умолчанию share")] string asset_type = "share",
        CancellationToken ct = default)
    {
        var assetClass = Format.ParseAssetClass(asset_type);
        if (assetClass is null)
            return Format.InvalidAssetClass(asset_type);

        var fromDate = Format.ParseDate(from, DateTime.UtcNow.Date);
        var toDate = Format.ParseDate(to, DateTime.UtcNow);

        var result = await _comparison.RankByPerformanceAsync(fromDate, toDate, limit, assetClass.Value, ct);
        if (result is null)
            return "Не удалось получить дневную историю ISS за этот период — попробуйте позже или другие даты.";
        if (result.Count == 0)
            return "Не удалось рассчитать доходность: нет цен за этот период.";

        return FormatRanking(result, $"Рейтинг ({asset_type}) по доходности ({fromDate:dd.MM.yyyy} — {toDate:dd.MM.yyyy}):");
    }

    /// <summary>
    /// Формирует текстовый рейтинг инструментов по доходности на основе переданных данных.
    /// </summary>
    /// <param name="items">Список объектов <see cref="InstrumentPerformance"/>, отсортированных по доходности, для форматирования.</param>
    /// <param name="header">Заголовок, который добавляется в начало результирующей строки.</param>
    /// <returns>Строка с заголовком и пронумерованным списком инструментов, включающим начальную и конечную цены, единицу измерения и процент изменения.</returns>
    private static string FormatRanking(IReadOnlyList<InstrumentPerformance> items, string header)
    {
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        var sb = new StringBuilder(header + "\n");
        for (var i = 0; i < items.Count; i++)
        {
            var p = items[i];
            var unit = p.Class.PriceUnit();
            sb.AppendLine(string.Format(ru, "{0}. {1} ({2}): {3:0.00} → {4:0.00} {5}, {6}%",
                i + 1, p.Ticker, p.Name, p.StartPrice, p.EndPrice, unit, Format.Signed(p.ChangePercent)));
        }
        return sb.ToString();
    }
}
