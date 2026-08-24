using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;
using ModelContextProtocol.Server;

namespace MoexMcp.Host.Tools;

/// <summary>Рыночные данные MOEX: котировки акций/облигаций/металлов, топы, поиск, новости, индексы, валюта.</summary>
[McpServerToolType]
public class MoexMarketTools
{
    private readonly IMarketDataService _market;
    private readonly ILogger<MoexMarketTools> _logger;

    public MoexMarketTools(IMarketDataService market, ILogger<MoexMarketTools> logger)
    {
        _market = market;
        _logger = logger;
    }

    [McpServerTool(Name = "get_stock_info"), Description("Получить текущую котировку акции на MOEX: цена, изменение, объём торгов")]
    public async Task<string> GetStockInfo(
        [Description("Тикер акции (например SBER, GAZP, LKOH)")] string ticker,
        CancellationToken ct = default)
    {
        var quote = await _market.GetStockInfoAsync(ticker, ct);
        if (quote is null)
            return $"Акция {ticker} не найдена на MOEX (основной режим TQBR).";

        var sb = new StringBuilder($"Информация об акции {quote.Ticker} ({quote.Name}):\n");
        sb.AppendLine($"Цена: {Format.Price(quote.Price, quote.PriceUnit)}");
        sb.AppendLine($"Изменение: {Format.Signed(quote.Change)} ({Format.Signed(quote.ChangePercent)}%)");
        sb.AppendLine($"Объём торгов: {quote.Volume?.ToString("N0") ?? "н/д"}");
        sb.AppendLine($"Время данных (МСК): {Format.Time(quote.Time)}");
        sb.AppendLine(MarketStatus(AssetClass.Share, quote.Time));
        return sb.ToString();
    }

    [McpServerTool(Name = "get_bond_info"), Description("Получить текущую котировку облигации на MOEX: цена в % от номинала, доходность (YTM), НКД, изменение, объём")]
    public async Task<string> GetBondInfo(
        [Description("Тикер облигации (например SU26243RMFS4 — ОФЗ)")] string ticker,
        CancellationToken ct = default)
    {
        var quote = await _market.GetBondInfoAsync(ticker, ct);
        if (quote is null)
            return $"Облигация {ticker} не найдена на MOEX.";

        var sb = new StringBuilder($"Информация об облигации {quote.Ticker} ({quote.Name}):\n");
        sb.AppendLine($"Цена: {Format.Price(quote.Price, quote.PriceUnit)}");
        sb.AppendLine($"Доходность (YTM): {(quote.Yield is null ? "н/д" : $"{quote.Yield:0.00}%")}");
        sb.AppendLine($"НКД: {Format.Price(quote.AccruedInterest)}");
        sb.AppendLine($"Изменение: {Format.Signed(quote.Change)} ({Format.Signed(quote.ChangePercent)}%)");
        sb.AppendLine($"Объём торгов: {quote.Volume?.ToString("N0") ?? "н/д"}");
        sb.AppendLine($"Время данных (МСК): {Format.Time(quote.Time)}");
        sb.AppendLine(MarketStatus(AssetClass.Bond, quote.Time));
        return sb.ToString();
    }

    [McpServerTool(Name = "get_metal_prices"), Description("Получить цены драгметаллов на MOEX (золото GLDRUB_TOM, серебро SLVRUB_TOM), ₽/грамм")]
    public async Task<string> GetMetalPrices(CancellationToken ct = default)
    {
        var metals = await _market.GetMetalPricesAsync(ct);
        if (metals.Count == 0)
            return "Не удалось получить цены металлов.";

        var sb = new StringBuilder("Цены металлов MOEX (₽/грамм):\n");
        foreach (var m in metals)
            sb.AppendLine($"- {m.Name} ({m.Ticker}): {Format.Price(m.Price, AssetClass.Metal.PriceUnit())} ({Format.Signed(m.Change)}) на {Format.Time(m.Time)}");
        sb.AppendLine(MarketStatus(AssetClass.Metal, metals.Select(m => m.Time).Where(t => t is not null).Max()));
        return sb.ToString();
    }

    [McpServerTool(Name = "get_top_gainers"), Description("Получить топ растущих акций MOEX за сегодня")]
    public async Task<string> GetTopGainers(
        [Description("Количество акций в списке (по умолчанию 10)")] int limit = 10,
        CancellationToken ct = default)
    {
        var top = await _market.GetTopGainersAsync(limit, ct);
        return FormatQuoteList(top, $"Топ {top.Count} растущих акций MOEX:", AssetClass.Share);
    }

    [McpServerTool(Name = "get_top_losers"), Description("Получить топ падающих акций MOEX за сегодня")]
    public async Task<string> GetTopLosers(
        [Description("Количество акций в списке (по умолчанию 10)")] int limit = 10,
        CancellationToken ct = default)
    {
        var top = await _market.GetTopLosersAsync(limit, ct);
        return FormatQuoteList(top, $"Топ {top.Count} падающих акций MOEX:", AssetClass.Share);
    }

    [McpServerTool(Name = "get_top_bond_gainers"), Description("Получить топ растущих облигаций MOEX за сегодня (цены в % от номинала)")]
    public async Task<string> GetTopBondGainers(
        [Description("Количество облигаций в списке (по умолчанию 10)")] int limit = 10,
        CancellationToken ct = default)
    {
        var top = await _market.GetTopBondGainersAsync(limit, ct);
        return FormatQuoteList(top, $"Топ {top.Count} растущих облигаций MOEX:", AssetClass.Bond);
    }

    [McpServerTool(Name = "get_top_bond_losers"), Description("Получить топ падающих облигаций MOEX за сегодня (цены в % от номинала)")]
    public async Task<string> GetTopBondLosers(
        [Description("Количество облигаций в списке (по умолчанию 10)")] int limit = 10,
        CancellationToken ct = default)
    {
        var top = await _market.GetTopBondLosersAsync(limit, ct);
        return FormatQuoteList(top, $"Топ {top.Count} падающих облигаций MOEX:", AssetClass.Bond);
    }

    [McpServerTool(Name = "search_stocks"), Description("Поиск ценных бумаг на MOEX по названию или тикеру")]
    public async Task<string> SearchStocks(
        [Description("Поисковый запрос (часть названия или тикера)")] string query,
        CancellationToken ct = default)
    {
        var found = await _market.SearchStocksAsync(query, ct);
        if (found.Count == 0)
            return $"По запросу «{query}» ничего не найдено.";

        var sb = new StringBuilder($"Найдено бумаг: {found.Count}\n");
        foreach (var s in found)
            sb.AppendLine($"- {s.Ticker}: {s.ShortName} ({s.Name})");
        return sb.ToString();
    }

    [McpServerTool(Name = "get_moex_news"), Description("Получить последние новости с сайта Московской биржи")]
    public async Task<string> GetMoexNews(
        [Description("Количество новостей (по умолчанию 20)")] int limit = 20,
        CancellationToken ct = default)
    {
        var news = await _market.GetNewsAsync(limit, ct);
        if (news.Count == 0)
            return "Новостей не найдено.";

        var sb = new StringBuilder($"Новости MOEX ({news.Count}):\n");
        foreach (var n in news)
            sb.AppendLine($"- [{Format.Time(n.PublishedAt)}] {n.Title}" + (n.Tag.Length > 0 ? $" (тег: {n.Tag})" : ""));
        return sb.ToString();
    }

    [McpServerTool(Name = "get_indices"), Description("Получить значения индексов MOEX (IMOEX, RTSI)")]
    public async Task<string> GetIndices(CancellationToken ct = default)
    {
        var indices = await _market.GetIndicesAsync(ct);
        if (indices.Count == 0)
            return "Не удалось получить значения индексов.";

        var sb = new StringBuilder("Индексы MOEX:\n");
        foreach (var i in indices)
            sb.AppendLine($"- {i.Ticker}: {Format.Number(i.Value)} ({Format.Signed(i.Change)}) на {Format.Time(i.Time)}");
        return sb.ToString();
    }

    [McpServerTool(Name = "get_currency_rates"), Description("Получить курсы валют на MOEX (USD/RUB, EUR/RUB)")]
    public async Task<string> GetCurrencyRates(CancellationToken ct = default)
    {
        var rates = await _market.GetCurrencyRatesAsync(ct);
        if (rates.Count == 0)
            return "Не удалось получить курсы валют.";

        var sb = new StringBuilder("Курсы валют MOEX:\n");
        foreach (var r in rates)
            sb.AppendLine($"- {r.Ticker}: {Format.Number(r.Price)} ({Format.Signed(r.Change)}) на {Format.Time(r.Time)}");
        sb.AppendLine(MarketStatus(AssetClass.Currency, rates.Select(r => r.Time).Where(t => t is not null).Max()));
        return sb.ToString();
    }

    /// <summary>Строка статуса рынка: торги идут или вне сессии (и на какое время данные).</summary>
    internal static string MarketStatus(AssetClass assetClass, DateTime? dataTime) =>
        TradingSchedule.Describe(assetClass, dataTime, TradingSchedule.MoscowNow);

    private static string FormatQuoteList(IReadOnlyList<Quote> quotes, string header, AssetClass assetClass)
    {
        if (quotes.Count == 0)
            return "Данных нет (возможно, торги ещё не начались).";

        var sb = new StringBuilder(header + "\n");
        for (var i = 0; i < quotes.Count; i++)
        {
            var q = quotes[i];
            sb.AppendLine($"{i + 1}. {q.Ticker} ({q.Name}): {Format.Price(q.Price, q.PriceUnit)} ({Format.Signed(q.ChangePercent)}%)");
        }
        sb.Append(MarketStatus(assetClass, quotes.Select(q => q.Time).Where(t => t is not null).Max()));
        return sb.ToString();
    }
}

/// <summary>Общие форматтеры для текстовых ответов инструментов.</summary>
internal static class Format
{
    public static string Price(decimal? value, string? unit = null) => value is null ? "н/д" : $"{value:0.00} {unit ?? "₽"}";
    public static string Number(decimal? value) => value is null ? "н/д" : $"{value:0.00}";
    public static string Signed(decimal? value) => value is null ? "н/д" : $"{(value >= 0 ? "+" : "")}{value:0.00}";
    public static string Time(DateTime? value) => value is null ? "н/д" : $"{value:dd.MM.yyyy HH:mm:ss}";

    /// <summary>Разобрать дату из строки (yyyy-MM-dd) или вернуть значение по умолчанию.</summary>
    public static DateTime ParseDate(string? s, DateTime fallback) =>
        !string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s, out var dt) ? dt : fallback;

    /// <summary>Разобрать тип актива (share|bond|currency|metal). null — невалидное значение.</summary>
    public static AssetClass? ParseAssetClass(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        null or "" or "share" => AssetClass.Share,
        "bond" => AssetClass.Bond,
        "currency" => AssetClass.Currency,
        "metal" => AssetClass.Metal,
        _ => null
    };

    /// <summary>Сообщение об ошибке для невалидного asset_type.</summary>
    public static string InvalidAssetClass(string? s) =>
        $"Неизвестный тип актива «{s}». Допустимые значения: share, bond, currency, metal.";
}
