using System.Text.Json;
using Microsoft.Extensions.Logging;
using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Infrastructure.Moex;

/// <summary>Реализация доступа к ISS MOEX (https://iss.moex.com/iss). API-ключ не требуется.</summary>
public class MoexRepository : IMoexRepository
{
    private static readonly string[] IndexTickers = ["IMOEX", "RTSI"];
    private static readonly (string Ticker, string Board)[] CurrencyTickers = [("USD000UTSTOM", "CETS"), ("EUR_RUB__TOM", "CETS")];
    private static readonly (string Ticker, string Name)[] MetalTickers = [("GLDRUB_TOM", "Золото"), ("SLVRUB_TOM", "Серебро")];
    private static readonly string[] BondBoards = ["TQCB", "TQOB"];

    private readonly HttpClient _http;
    private readonly ILogger<MoexRepository> _logger;

    public MoexRepository(HttpClient http, ILogger<MoexRepository> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Маршрут ISS: engine/market/board для класса актива. Board = null — сегмент не вставляется.</summary>
    private sealed record MarketRoute(string Engine, string Market, string? Board);

    private static MarketRoute RouteFor(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Bond => new MarketRoute("stock", "bonds", null),
        // Валюта и драгметаллы (GLDRUB_TOM/SLVRUB_TOM) торгуются на валютном рынке в режиме CETS
        AssetClass.Currency or AssetClass.Metal => new MarketRoute("currency", "selt", "CETS"),
        _ => new MarketRoute("stock", "shares", "TQBR")
    };

    private static string BoardSegment(MarketRoute route) => route.Board is null ? "" : $"/boards/{route.Board}";

    public async Task<Quote?> GetQuoteAsync(string ticker, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        if (assetClass == AssetClass.Bond)
            return await GetBondQuoteAsync(ticker, ct);

        var route = RouteFor(assetClass);
        var url = $"/iss/engines/{route.Engine}/markets/{route.Market}{BoardSegment(route)}/securities/{Uri.EscapeDataString(ticker)}.json" +
                  "?iss.meta=off&iss.only=securities,marketdata" +
                  "&securities.columns=SECID,SHORTNAME,PREVPRICE,MARKETPRICE" +
                  "&marketdata.columns=SECID,LAST,CHANGE,LASTTOPREVPRICE,VOLTODAY,MARKETPRICE,SYSTIME";

        using var doc = await GetJsonAsync(url, ct);
        if (doc is null)
            return null;

        var sec = IssParser.ParseBlock(doc, "securities").FirstOrDefault();
        if (sec is null)
            return null; // бумага не найдена в этом режиме

        var md = IssParser.ParseBlock(doc, "marketdata").FirstOrDefault();
        return ToQuote(sec, md, assetClass);
    }

    public async Task<Quote?> GetBondQuoteAsync(string ticker, CancellationToken ct = default)
    {
        // Без board: ISS отдаёт строки по всем режимам (TQOB, TQCB, SPOB...) — выбираем приоритетный.
        // YIELD лежит в marketdata, НКД (ACCRUEDINT) — в securities (в marketdata его ISS молча отбрасывает).
        var url = $"/iss/engines/stock/markets/bonds/securities/{Uri.EscapeDataString(ticker)}.json" +
                  "?iss.meta=off&iss.only=securities,marketdata" +
                  "&securities.columns=SECID,BOARDID,SHORTNAME,PREVPRICE,ACCRUEDINT" +
                  "&marketdata.columns=SECID,BOARDID,LAST,CHANGE,LASTTOPREVPRICE,VOLTODAY,YIELD,MARKETPRICE,SYSTIME";

        using var doc = await GetJsonAsync(url, ct);
        if (doc is null)
            return null;

        var sec = PreferBoard(IssParser.ParseBlock(doc, "securities"));
        if (sec is null)
            return null; // облигация не найдена

        var md = PreferBoard(IssParser.ParseBlock(doc, "marketdata"));

        // Цена облигации — % от номинала. Вне сессии (после 18:40 и до 9:50) LAST пуст: MARKETPRICE → PREVPRICE
        var price = md?.GetDecimal("LAST") ?? md?.GetDecimal("MARKETPRICE") ?? sec.GetDecimal("PREVPRICE");
        return new Quote(
            sec.GetString("SECID") ?? "",
            sec.GetString("SHORTNAME") ?? "",
            price,
            md?.GetDecimal("CHANGE"),
            md?.GetDecimal("LASTTOPREVPRICE"),
            md?.GetLong("VOLTODAY"),
            md?.GetDateTime("SYSTIME"),
            AssetClass.Bond,
            AssetClass.Bond.PriceUnit(),
            md?.GetDecimal("YIELD"),
            sec.GetDecimal("ACCRUEDINT"));
    }

    /// <summary>Приоритет режима для облигации: TQOB (ОФЗ), затем TQCB (корпоративные), иначе первая строка.</summary>
    private static IReadOnlyDictionary<string, JsonElement>? PreferBoard(IReadOnlyList<Dictionary<string, JsonElement>> rows)
    {
        if (rows.Count == 0)
            return null;
        return rows.FirstOrDefault(r => r.GetString("BOARDID") == "TQOB")
               ?? rows.FirstOrDefault(r => r.GetString("BOARDID") == "TQCB")
               ?? rows[0];
    }

    public async Task<IReadOnlyList<Quote>> GetAllShareQuotesAsync(CancellationToken ct = default)
    {
        var route = RouteFor(AssetClass.Share);
        return await GetAllBoardQuotesAsync(route, AssetClass.Share, ct);
    }

    public async Task<IReadOnlyList<Quote>> GetAllBondQuotesAsync(CancellationToken ct = default)
    {
        // ОФЗ и корпоративные облигации живут на разных board'ах — опрашиваем оба и объединяем
        var byTicker = new Dictionary<string, Quote>(StringComparer.OrdinalIgnoreCase);
        foreach (var board in BondBoards)
        {
            var route = new MarketRoute("stock", "bonds", board);
            foreach (var quote in await GetAllBoardQuotesAsync(route, AssetClass.Bond, ct))
                byTicker[quote.Ticker] = quote;
        }
        return byTicker.Values.ToList();
    }

    public async Task<IReadOnlyList<MetalPrice>> GetMetalPricesAsync(CancellationToken ct = default)
    {
        var result = new List<MetalPrice>();
        foreach (var (ticker, name) in MetalTickers)
        {
            var url = $"/iss/engines/currency/markets/selt/boards/CETS/securities/{ticker}.json" +
                      "?iss.meta=off&iss.only=marketdata" +
                      "&marketdata.columns=SECID,LAST,MARKETPRICE,CHANGE,SYSTIME";

            using var doc = await GetJsonAsync(url, ct);
            var md = doc is null ? null : IssParser.ParseBlock(doc, "marketdata").FirstOrDefault();
            if (md is not null)
            {
                // До 10:00 МСК торгов нет, LAST пуст — берём MARKETPRICE (цена прошлой сессии)
                result.Add(new MetalPrice(
                    ticker,
                    name,
                    md.GetDecimal("LAST") ?? md.GetDecimal("MARKETPRICE"),
                    md.GetDecimal("CHANGE"),
                    md.GetDateTime("SYSTIME")));
            }
        }
        return result;
    }

    /// <summary>Все котировки одного board'а. Пагинация ISS: по 100 строк за запрос, листаем параметром start.</summary>
    private async Task<IReadOnlyList<Quote>> GetAllBoardQuotesAsync(MarketRoute route, AssetClass assetClass, CancellationToken ct)
    {
        // У облигаций дополнительно забираем YIELD и НКД
        var secColumns = assetClass == AssetClass.Bond
            ? "SECID,SHORTNAME,PREVPRICE,MARKETPRICE,ACCRUEDINT"
            : "SECID,SHORTNAME,PREVPRICE,MARKETPRICE";
        var mdColumns = assetClass == AssetClass.Bond
            ? "SECID,LAST,CHANGE,LASTTOPREVPRICE,VOLTODAY,YIELD,MARKETPRICE,SYSTIME"
            : "SECID,LAST,CHANGE,LASTTOPREVPRICE,VOLTODAY,MARKETPRICE,SYSTIME";

        // Материализуем котировки сразу внутри using — JsonElement не переживает Dispose JsonDocument
        var byTicker = new Dictionary<string, Quote>(StringComparer.OrdinalIgnoreCase);

        for (var start = 0; start < 2000; start += 100)
        {
            var url = $"/iss/engines/{route.Engine}/markets/{route.Market}{BoardSegment(route)}/securities.json" +
                      "?iss.meta=off&iss.only=securities,marketdata" +
                      $"&securities.columns={secColumns}" +
                      $"&marketdata.columns={mdColumns}" +
                      $"&start={start}";

            using var doc = await GetJsonAsync(url, ct);
            if (doc is null)
                break;

            var secPage = IssParser.ParseBlock(doc, "securities");

            // marketdata может содержать несколько строк на бумагу — берём последнюю (актуальную)
            var mdByTicker = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.OrdinalIgnoreCase);
            foreach (var md in IssParser.ParseBlock(doc, "marketdata"))
            {
                var t = md.GetString("SECID");
                if (t is not null)
                    mdByTicker[t] = md;
            }

            foreach (var sec in secPage)
            {
                var quote = ToQuote(sec, sec.GetString("SECID") is { } t && mdByTicker.TryGetValue(t, out var md) ? md : null, assetClass);
                if (quote.Ticker.Length > 0)
                    byTicker[quote.Ticker] = quote;
            }

            if (secPage.Count < 100)
                break;
        }

        return byTicker.Values.ToList();
    }

    public async Task<IReadOnlyList<SecurityInfo>> SearchSecuritiesAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        var url = $"/iss/securities.json?q={Uri.EscapeDataString(query)}&iss.meta=off&iss.only=securities" +
                  $"&securities.columns=secid,shortname,name&limit={limit}";

        using var doc = await GetJsonAsync(url, ct);
        if (doc is null)
            return [];

        return IssParser.ParseBlock(doc, "securities")
            .Select(r => new SecurityInfo(
                r.GetString("secid") ?? "",
                r.GetString("shortname") ?? "",
                r.GetString("name") ?? ""))
            .Where(s => s.Ticker.Length > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<SiteNewsItem>> GetSiteNewsAsync(int limit = 20, CancellationToken ct = default)
    {
        var url = $"/iss/sitenews.json?iss.meta=off&iss.only=sitenews&sitenews.columns=id,title,tag,modified_at&limit={limit}";

        using var doc = await GetJsonAsync(url, ct);
        if (doc is null)
            return [];

        return IssParser.ParseBlock(doc, "sitenews")
            .Select(r => new SiteNewsItem(
                r.GetLong("id") ?? 0,
                r.GetString("title") ?? "",
                r.GetString("tag") ?? "",
                r.GetDateTime("modified_at")))
            .Take(limit) // ISS игнорирует limit у sitenews — режем на клиенте
            .ToList();
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string ticker, int intervalMinutes, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        var route = RouteFor(assetClass);
        var candles = new List<Candle>();
        // ISS отдаёт до 500 свечей за запрос, листаем start. Board не нужен — candles работает без него для всех классов
        for (var start = 0; start < 5000; start += 500)
        {
            var url = $"/iss/engines/{route.Engine}/markets/{route.Market}/securities/{Uri.EscapeDataString(ticker)}/candles.json" +
                      $"?iss.meta=off&interval={intervalMinutes}" +
                      $"&from={from:yyyy-MM-dd}&till={to:yyyy-MM-dd}" +
                      "&candles.columns=open,close,high,low,volume,begin" +
                      $"&start={start}";

            using var doc = await GetJsonAsync(url, ct);
            if (doc is null)
                break;

            var page = IssParser.ParseBlock(doc, "candles");
            candles.AddRange(page.Select(r => new Candle(
                r.GetDateTime("begin") ?? DateTime.MinValue,
                r.GetDecimal("open") ?? 0,
                r.GetDecimal("close") ?? 0,
                r.GetDecimal("high") ?? 0,
                r.GetDecimal("low") ?? 0,
                r.GetLong("volume") ?? 0)));
            if (page.Count < 500)
                break;
        }
        return candles;
    }

    public async Task<IReadOnlyList<DailyPrice>> GetPriceHistoryAsync(string ticker, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        var route = RouteFor(assetClass);
        var prices = new List<DailyPrice>();
        for (var start = 0; start < 5000; start += 100)
        {
            var url = $"/iss/history/engines/{route.Engine}/markets/{route.Market}{BoardSegment(route)}/securities/{Uri.EscapeDataString(ticker)}.json" +
                      "?iss.meta=off&iss.only=history" +
                      "&history.columns=SECID,BOARDID,TRADEDATE,CLOSE" +
                      $"&from={from:yyyy-MM-dd}&till={to:yyyy-MM-dd}&start={start}";

            using var doc = await GetJsonAsync(url, ct);
            if (doc is null)
                break;

            var page = IssParser.ParseBlock(doc, "history");
            // Без board (облигации) на одну дату может быть несколько строк — берём приоритетный режим
            foreach (var day in page.GroupBy(r => r.GetDateTime("TRADEDATE")))
            {
                var row = day.FirstOrDefault(r => r.GetString("BOARDID") == "TQOB")
                          ?? day.FirstOrDefault(r => r.GetString("BOARDID") == "TQCB")
                          ?? day.First();
                var close = row.GetDecimal("CLOSE");
                if (close is > 0)
                    prices.Add(new DailyPrice(row.GetString("SECID") ?? ticker, day.Key ?? DateTime.MinValue, close.Value));
            }
            if (page.Count < 100)
                break;
        }
        return prices;
    }

    public async Task<IReadOnlyList<IndexQuote>> GetIndicesAsync(CancellationToken ct = default)
    {
        var result = new List<IndexQuote>();
        foreach (var ticker in IndexTickers)
        {
            // У индексов значение лежит в CURRENTVALUE, а не в LAST
            var url = $"/iss/engines/stock/markets/index/securities/{ticker}.json" +
                      "?iss.meta=off&iss.only=marketdata" +
                      "&marketdata.columns=SECID,CURRENTVALUE,LASTCHANGE,SYSTIME";

            using var doc = await GetJsonAsync(url, ct);
            var md = doc is null ? null : IssParser.ParseBlock(doc, "marketdata").FirstOrDefault();
            if (md is not null)
            {
                result.Add(new IndexQuote(
                    ticker,
                    md.GetDecimal("CURRENTVALUE"),
                    md.GetDecimal("LASTCHANGE"),
                    md.GetDateTime("SYSTIME")));
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<CurrencyRate>> GetCurrencyRatesAsync(CancellationToken ct = default)
    {
        var result = new List<CurrencyRate>();
        foreach (var (ticker, board) in CurrencyTickers)
        {
            var url = $"/iss/engines/currency/markets/selt/boards/{board}/securities/{ticker}.json" +
                      "?iss.meta=off&iss.only=marketdata" +
                      "&marketdata.columns=SECID,LAST,MARKETPRICE,CHANGE,SYSTIME";

            using var doc = await GetJsonAsync(url, ct);
            var md = doc is null ? null : IssParser.ParseBlock(doc, "marketdata").FirstOrDefault();
            if (md is not null)
            {
                // Вне торговой сессии LAST пуст — берём MARKETPRICE
                result.Add(new CurrencyRate(
                    ticker,
                    md.GetDecimal("LAST") ?? md.GetDecimal("MARKETPRICE"),
                    md.GetDecimal("CHANGE"),
                    md.GetDateTime("SYSTIME")));
            }
        }
        return result;
    }

    private static Quote ToQuote(IReadOnlyDictionary<string, JsonElement> sec, IReadOnlyDictionary<string, JsonElement>? md, AssetClass assetClass)
    {
        // Если торгов сегодня не было (LAST = null), показываем MARKETPRICE/PREVPRICE
        var price = md?.GetDecimal("LAST") ?? md?.GetDecimal("MARKETPRICE") ?? sec.GetDecimal("MARKETPRICE") ?? sec.GetDecimal("PREVPRICE");
        return new Quote(
            sec.GetString("SECID") ?? "",
            sec.GetString("SHORTNAME") ?? "",
            price,
            md?.GetDecimal("CHANGE"),
            md?.GetDecimal("LASTTOPREVPRICE"),
            md?.GetLong("VOLTODAY"),
            md?.GetDateTime("SYSTIME"),
            assetClass,
            assetClass.PriceUnit(),
            assetClass == AssetClass.Bond ? md?.GetDecimal("YIELD") : null,
            assetClass == AssetClass.Bond ? sec.GetDecimal("ACCRUEDINT") : null);
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            await using var stream = await _http.GetStreamAsync(url, ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Ошибка запроса к ISS MOEX: {Url}", url);
            return null;
        }
    }
}
