using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;
using ModelContextProtocol.Server;

namespace MoexMcp.Host.Tools;

/// <summary>
/// Рыночные данные MOEX: котировки акций/облигаций/металлов, топы, поиск, новости, индексы, валюта.
/// </summary>
[McpServerToolType]
public class MoexMarketTools
{
    /// <summary>
    /// Сервис рыночных данных MOEX, используемый для получения котировок акций, облигаций и драгоценных металлов.
    /// </summary>
    private readonly IMarketDataService _market;

    /// <summary>
    /// Логгер для регистрации диагностических сообщений и событий при выполнении рыночных операций.
    /// </summary>
    private readonly ILogger<MoexMarketTools> _logger;

    /// <summary>
    /// Набор MCP-инструментов для получения рыночных данных Московской биржи: акции, облигации, драгоценные металлы, валюты, индексы, топы бумаг и новости.
    /// </summary>
    public MoexMarketTools(IMarketDataService market, ILogger<MoexMarketTools> logger)
    {
        _market = market;
        _logger = logger;
    }

    /// <summary>
    /// Получает текущую котировку акции на Московской бирже (MOEX), включая цену, изменение, объём торгов и статус торговой сессии.
    /// </summary>
    /// <param name="ticker">Тикер акции, например SBER, GAZP или LKOH.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Строка с информацией об акции: цена, изменение и изменение в процентах, объём торгов, время данных по московскому времени и статус рынка; если акция не найдена, возвращается соответствующее сообщение.</returns>
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

    /// <summary>
    /// Возвращает текущую котировку облигации на Московской бирже: цену в процентах от номинала, доходность до погашения (YTM), НКД, изменение цены, объём торгов и состояние рынка.
    /// </summary>
    /// <param name="ticker">Тикер облигации (например SU26243RMFS4 — ОФЗ).</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Строка с информацией об облигации. Если облигация не найдена, возвращается сообщение об ошибке.</returns>
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

    /// <summary>
    /// Возвращает текущие цены драгоценных металлов на Московской бирже в виде отформатированной строки.
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <return>
    /// Строка с ценами металлов в рублях за грамм, изменениями, временем обновления и статусом торговой сессии;
    /// если данные отсутствуют, возвращается сообщение об ошибке.
    /// </return>
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

    /// <summary>
    /// Возвращает топ растущих акций Московской биржи за текущий торговый день.
    /// </summary>
    /// <param name="limit">Максимальное количество акций в списке.</param>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <returns>Строка с отформатированным списком топ растущих акций.</returns>
    [McpServerTool(Name = "get_top_gainers"), Description("Получить топ растущих акций MOEX за сегодня")]
    public async Task<string> GetTopGainers(
        [Description("Количество акций в списке (по умолчанию 10)")] int limit = 10,
        CancellationToken ct = default)
    {
        var top = await _market.GetTopGainersAsync(limit, ct);
        return FormatQuoteList(top, $"Топ {top.Count} растущих акций MOEX:", AssetClass.Share);
    }

    /// <summary>
    /// Возвращает топ падающих акций Московской биржи за текущий торговый день.
    /// </summary>
    /// <param name="limit">Количество акций в возвращаемом списке. Значение по умолчанию — 10.</param>
    /// <param name="ct">Токен отмены для асинхронной операции.</param>
    /// <returns>Строка с отформатированным списком топа падающих акций MOEX.</returns>
    [McpServerTool(Name = "get_top_losers"), Description("Получить топ падающих акций MOEX за сегодня")]
    public async Task<string> GetTopLosers(
        [Description("Количество акций в списке (по умолчанию 10)")] int limit = 10,
        CancellationToken ct = default)
    {
        var top = await _market.GetTopLosersAsync(limit, ct);
        return FormatQuoteList(top, $"Топ {top.Count} падающих акций MOEX:", AssetClass.Share);
    }

    /// <summary>
    /// Получает топ растущих облигаций MOEX за сегодня с текущими котировками (цены в процентах от номинала).
    /// </summary>
    /// <param name="limit">Количество облигаций в списке. Значение по умолчанию — 10.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Строка с отформатированным списком топа растущих облигаций, включая цену в % от номинала, изменение и объём торгов.</returns>
    [McpServerTool(Name = "get_top_bond_gainers"), Description("Получить топ растущих облигаций MOEX за сегодня (цены в % от номинала)")]
    public async Task<string> GetTopBondGainers(
        [Description("Количество облигаций в списке (по умолчанию 10)")] int limit = 10,
        CancellationToken ct = default)
    {
        var top = await _market.GetTopBondGainersAsync(limit, ct);
        return FormatQuoteList(top, $"Топ {top.Count} растущих облигаций MOEX:", AssetClass.Bond);
    }

    /// <summary>
    /// Возвращает топ падающих облигаций Московской биржи за текущий торговый день с указанием цен в процентах от номинала.
    /// </summary>
    /// <param name="limit">Количество облигаций в возвращаемом списке. Значение по умолчанию — 10.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Строка с отформатированным списком топа падающих облигаций MOEX.</returns>
    [McpServerTool(Name = "get_top_bond_losers"), Description("Получить топ падающих облигаций MOEX за сегодня (цены в % от номинала)")]
    public async Task<string> GetTopBondLosers(
        [Description("Количество облигаций в списке (по умолчанию 10)")] int limit = 10,
        CancellationToken ct = default)
    {
        var top = await _market.GetTopBondLosersAsync(limit, ct);
        return FormatQuoteList(top, $"Топ {top.Count} падающих облигаций MOEX:", AssetClass.Bond);
    }

    /// <summary>
    /// Выполняет поиск ценных бумаг на Московской бирже по названию или тикеру.
    /// </summary>
    /// <param name="query">Поисковый запрос: часть названия или тикера бумаги.</param>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <returns>Строка с результатами поиска: список найденных бумаг с тикерами и наименованиями, либо сообщение о том, что ничего не найдено.</returns>
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

    /// <summary>
    /// Возвращает последние новости с сайта Московской биржи в отформатированном виде.
    /// </summary>
    /// <param name="limit">Максимальное количество возвращаемых новостей. По умолчанию 20.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Строка с новостями MOEX, содержащая дату публикации, заголовок и тег каждой новости; если новости отсутствуют, возвращает соответствующее сообщение.</returns>
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

    /// <summary>
    /// Получает текущие значения основных индексов Московской биржи (IMOEX, RTSI), включая величину индекса, его изменение и время обновления.
    /// </summary>
    /// <param name="ct">Токен отмены асинхронной операции.</param>
    /// <return>Строка со значениями индексов MOEX, их изменениями и временем обновления; или сообщение о том, что данные получить не удалось.</return>
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

    /// <summary>
    /// Возвращает актуальные курсы валют на Московской бирже (валютные пары, такие как USD/RUB и EUR/RUB) в виде форматированной строки.
    /// </summary>
    /// <param name="ct">Токен отмены для асинхронной операции.</param>
    /// <returns>
    /// Строка с текущими котировками валют, изменениями, временем обновления и статусом торговой сессии;
    /// или сообщение о том, что получить курсы не удалось.
    /// </returns>
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

    /// <summary>
    /// Возвращает строку статуса рынка: идут торги или вне сессии, а также актуальность полученных данных.
    /// </summary>
    /// <param name="assetClass">Класс актива, для которого определяется статус торгов.</param>
    /// <param name="dataTime">Время полученных данных, используемое для оценки актуальности.</param>
    /// <returns>Строка с описанием текущего статуса торговой сессии и временем данных.</returns>
    private static string MarketStatus(AssetClass assetClass, DateTime? dataTime) =>
        TradingSchedule.Describe(assetClass, dataTime, TradingSchedule.MoscowNow);

    /// <summary>
    /// Форматирует список котировок в читаемый текст с заголовком и статусом торгов.
    /// </summary>
    /// <param name="quotes">Список котировок для форматирования.</param>
    /// <param name="header">Заголовок, отображаемый перед списком.</param>
    /// <param name="assetClass">Класс актива, используемый для определения статуса торгов.</param>
    /// <returns>Строка с отформатированным списком котировок либо сообщение об отсутствии данных.</returns>
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

/// <summary>
/// Общие форматтеры для текстовых ответов инструментов.
/// </summary>
internal static class Format
{
    /// <summary>
    /// Форматирует ценовое значение в виде строки с указанием единицы измерения.
    /// </summary>
    /// <param name="value">Ценовое значение для форматирования.</param>
    /// <param name="unit">Единица измерения цены (например, валюта); если не указана, используется рубль.</param>
    /// <returns>Строковое представление цены с единицей измерения или "н/д", если значение отсутствует.</returns>
    public static string Price(decimal? value, string? unit = null) => value is null ? "н/д" : $"{value:0.00} {unit ?? "₽"}";

    /// <summary>
    /// Форматирует числовое значение в строковое представление с двумя знаками после запятой.
    /// </summary>
    /// <param name="value">Числовое значение для форматирования.</param>
    /// <returns>Строка с числом, округлённым до двух десятичных знаков; если значение равно null, возвращается «н/д».</returns>
    public static string Number(decimal? value) => value is null ? "н/д" : $"{value:0.00}";

    /// <summary>
    /// Форматирует числовое значение как строку со знаком: «+» для неотрицательных значений и «−» для отрицательных.
    /// </summary>
    /// <param name="value">Числовое значение для форматирования. Если значение равно null, возвращается строка «н/д».</param>
    /// <returns>Строковое представление числа со знаком или строка «н/д», если значение отсутствует.</returns>
    public static string Signed(decimal? value) => value is null ? "н/д" : $"{(value >= 0 ? "+" : "")}{value:0.00}";

    /// <summary>
    /// Форматирует значение даты и времени в строку.
    /// </summary>
    /// <param name="value">Значение даты и времени для форматирования.</param>
    /// <returns>Строковое представление даты и времени в формате "dd.MM.yyyy HH:mm:ss" или "н/д", если значение не задано.</returns>
    public static string Time(DateTime? value) => value is null ? "н/д" : $"{value:dd.MM.yyyy HH:mm:ss}";

    /// <summary>
    /// Разбирает дату из строки или возвращает значение по умолчанию, если строка пустая либо не может быть распознана как дата.
    /// </summary>
    /// <param name="s">Строка с датой, которую необходимо разобрать.</param>
    /// <param name="fallback">Значение, возвращаемое при пустой строке или ошибке разбора.</param>
    /// <returns>Распознанную дату, если разбор успешен; иначе переданное значение по умолчанию.</returns>
    public static DateTime ParseDate(string? s, DateTime fallback) =>
        !string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s, out var dt) ? dt : fallback;

    /// <summary>
    /// Разбирает строковое представление типа актива в значение перечисления <see cref="AssetClass"/>.
    /// </summary>
    /// <param name="s">Строка, содержащая тип актива (например, share, bond, currency или metal). Может быть <c>null</c> или пустой строкой.</param>
    /// <return>Возвращает соответствующее значение <see cref="AssetClass"/>, если строка распознана; иначе <c>null</c>.</return>
    public static AssetClass? ParseAssetClass(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        null or "" or "share" => AssetClass.Share,
        "bond" => AssetClass.Bond,
        "currency" => AssetClass.Currency,
        "metal" => AssetClass.Metal,
        _ => null
    };

    /// <summary>
    /// Формирует сообщение об ошибке для невалидного типа актива.
    /// </summary>
    /// <param name="s">Недопустимое значение типа актива.</param>
    /// <returns>Сообщение об ошибке с указанием допустимых значений типа актива.</returns>
    public static string InvalidAssetClass(string? s) =>
        $"Неизвестный тип актива «{s}». Допустимые значения: share, bond, currency, metal.";
}
