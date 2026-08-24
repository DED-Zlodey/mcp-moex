using System.ComponentModel;
using System.Text;
using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;
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

    [McpServerTool(Name = "rank_by_performance"), Description("Ранжировать инструменты MOEX одного класса по доходности за период. Работает по накопленным снапшотам (хранятся 7 дней); снапшоты облигаций/металлов накапливаются с момента выкатки этой версии")]
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
            return "Нет подходящих снапшотов за этот период. Снапшоты рынка накапливаются каждые 5 минут и хранятся 7 дней — " +
                   "для более ранних дат используйте compare_instruments (берёт данные из дневной истории ISS).";
        if (result.Count == 0)
            return "Не удалось рассчитать доходность: нет цен за этот период.";

        return FormatRanking(result, $"Рейтинг ({asset_type}) по доходности ({fromDate:dd.MM.yyyy} — {toDate:dd.MM.yyyy}):");
    }

    private static string FormatRanking(IReadOnlyList<InstrumentPerformance> items, string header)
    {
        var sb = new StringBuilder(header + "\n");
        for (var i = 0; i < items.Count; i++)
        {
            var p = items[i];
            var unit = p.Class.PriceUnit();
            sb.AppendLine($"{i + 1}. {p.Ticker} ({p.Name}): {p.StartPrice:0.00} → {p.EndPrice:0.00} {unit}, {Format.Signed(p.ChangePercent)}%");
        }
        return sb.ToString();
    }
}
