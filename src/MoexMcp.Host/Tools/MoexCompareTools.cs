using System.ComponentModel;
using System.Text;
using MoexMcp.Application.Services;
using ModelContextProtocol.Server;

namespace MoexMcp.Host.Tools;

/// <summary>Сравнение инструментов и ранжирование по доходности (на основе снапшотов в Redis и истории ISS).</summary>
[McpServerToolType]
public class MoexCompareTools
{
    private readonly IComparisonService _comparison;

    public MoexCompareTools(IComparisonService comparison)
    {
        _comparison = comparison;
    }

    [McpServerTool(Name = "compare_instruments"), Description("Сравнить акции между собой по доходности за период: цена на начало и конец, изменение в %, место в рейтинге")]
    public async Task<string> CompareInstruments(
        [Description("Тикеры через запятую (например SBER,GAZP,LKOH)")] string tickers,
        [Description("Начало периода, yyyy-MM-dd. По умолчанию 7 дней назад")] string? from = null,
        [Description("Конец периода, yyyy-MM-dd. По умолчанию сейчас")] string? to = null,
        CancellationToken ct = default)
    {
        var list = tickers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (list.Length == 0)
            return "Укажите хотя бы один тикер.";

        var fromDate = Format.ParseDate(from, DateTime.UtcNow.AddDays(-7));
        var toDate = Format.ParseDate(to, DateTime.UtcNow);

        var result = await _comparison.CompareInstrumentsAsync(list, fromDate, toDate, ct);
        if (result.Count == 0)
            return "Не удалось получить цены ни по одному из указанных тикеров за этот период.";

        return FormatRanking(result, $"Сравнение инструментов ({fromDate:dd.MM.yyyy} — {toDate:dd.MM.yyyy}):");
    }

    [McpServerTool(Name = "rank_by_performance"), Description("Ранжировать все акции MOEX (TQBR) по доходности за период. Работает по накопленным снапшотам (хранятся 7 дней)")]
    public async Task<string> RankByPerformance(
        [Description("Начало периода, yyyy-MM-dd. По умолчанию начало сегодняшнего дня")] string? from = null,
        [Description("Конец периода, yyyy-MM-dd. По умолчанию сейчас")] string? to = null,
        [Description("Сколько позиций показать (по умолчанию 20)")] int limit = 20,
        CancellationToken ct = default)
    {
        var fromDate = Format.ParseDate(from, DateTime.UtcNow.Date);
        var toDate = Format.ParseDate(to, DateTime.UtcNow);

        var result = await _comparison.RankByPerformanceAsync(fromDate, toDate, limit, ct);
        if (result is null)
            return "Нет подходящих снапшотов за этот период. Снапшоты рынка накапливаются каждые 5 минут и хранятся 7 дней — " +
                   "для более ранних дат используйте compare_instruments (берёт данные из дневной истории ISS).";
        if (result.Count == 0)
            return "Не удалось рассчитать доходность: нет цен за этот период.";

        return FormatRanking(result, $"Рейтинг акций MOEX по доходности ({fromDate:dd.MM.yyyy} — {toDate:dd.MM.yyyy}):");
    }

    private static string FormatRanking(IReadOnlyList<InstrumentPerformance> items, string header)
    {
        var sb = new StringBuilder(header + "\n");
        for (var i = 0; i < items.Count; i++)
        {
            var p = items[i];
            sb.AppendLine($"{i + 1}. {p.Ticker} ({p.Name}): {p.StartPrice:0.00} → {p.EndPrice:0.00} ₽, {Format.Signed(p.ChangePercent)}%");
        }
        return sb.ToString();
    }
}
