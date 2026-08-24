using System.Text.Json;
using Microsoft.Extensions.Logging;
using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Infrastructure.Moex;

/// <summary>Реализация доступа к ISS MOEX (https://iss.moex.com/iss). API-ключ не требуется.</summary>
public class MoexRepository : IMoexRepository
{
    private const string SharesBoard = "TQBR";
    private static readonly string[] IndexTickers = ["IMOEX", "RTSI"];
    private static readonly (string Ticker, string Board)[] CurrencyTickers = [("USD000UTSTOM", "CETS"), ("EUR_RUB__TOM", "CETS")];

    private readonly HttpClient _http;
    private readonly ILogger<MoexRepository> _logger;

    public MoexRepository(HttpClient http, ILogger<MoexRepository> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Quote?> GetQuoteAsync(string ticker, CancellationToken ct = default)
    {
        var url = $"/iss/engines/stock/markets/shares/boards/{SharesBoard}/securities/{Uri.EscapeDataString(ticker)}.json" +
                  "?iss.meta=off&iss.only=securities,marketdata" +
                  "&securities.columns=SECID,SHORTNAME,PREVPRICE,MARKETPRICE" +
                  "&marketdata.columns=SECID,LAST,CHANGE,LASTTOPREVPRICE,VOLTODAY,SYSTIME";

        using var doc = await GetJsonAsync(url, ct);
        if (doc is null)
            return null;

        var sec = IssParser.ParseBlock(doc, "securities").FirstOrDefault();
        if (sec is null)
            return null; // бумага не найдена на TQBR

        var md = IssParser.ParseBlock(doc, "marketdata").FirstOrDefault();
        return ToQuote(sec, md);
    }

    public async Task<IReadOnlyList<Quote>> GetAllShareQuotesAsync(CancellationToken ct = default)
    {
        // Материализуем котировки сразу внутри using — JsonElement не переживает Dispose JsonDocument
        var byTicker = new Dictionary<string, Quote>(StringComparer.OrdinalIgnoreCase);

        // Пагинация ISS: по 100 строк за запрос, листаем параметром start
        for (var start = 0; start < 2000; start += 100)
        {
            var url = $"/iss/engines/stock/markets/shares/boards/{SharesBoard}/securities.json" +
                      "?iss.meta=off&iss.only=securities,marketdata" +
                      "&securities.columns=SECID,SHORTNAME,PREVPRICE,MARKETPRICE" +
                      "&marketdata.columns=SECID,LAST,CHANGE,LASTTOPREVPRICE,VOLTODAY,SYSTIME" +
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
                var quote = ToQuote(sec, sec.GetString("SECID") is { } t && mdByTicker.TryGetValue(t, out var md) ? md : null);
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

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string ticker, int intervalMinutes, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var candles = new List<Candle>();
        // ISS отдаёт до 500 свечей за запрос, листаем start
        for (var start = 0; start < 5000; start += 500)
        {
            var url = $"/iss/engines/stock/markets/shares/securities/{Uri.EscapeDataString(ticker)}/candles.json" +
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

    public async Task<IReadOnlyList<DailyPrice>> GetPriceHistoryAsync(string ticker, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var prices = new List<DailyPrice>();
        for (var start = 0; start < 5000; start += 100)
        {
            var url = $"/iss/history/engines/stock/markets/shares/boards/{SharesBoard}/securities/{Uri.EscapeDataString(ticker)}.json" +
                      "?iss.meta=off&iss.only=history" +
                      "&history.columns=SECID,TRADEDATE,CLOSE" +
                      $"&from={from:yyyy-MM-dd}&till={to:yyyy-MM-dd}&start={start}";

            using var doc = await GetJsonAsync(url, ct);
            if (doc is null)
                break;

            var page = IssParser.ParseBlock(doc, "history");
            prices.AddRange(page
                .Select(r => new DailyPrice(
                    r.GetString("SECID") ?? ticker,
                    r.GetDateTime("TRADEDATE") ?? DateTime.MinValue,
                    r.GetDecimal("CLOSE") ?? 0))
                .Where(p => p.Close > 0));
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

    private static Quote ToQuote(IReadOnlyDictionary<string, JsonElement> sec, IReadOnlyDictionary<string, JsonElement>? md)
    {
        // Если торгов сегодня не было (LAST = null), показываем MARKETPRICE/PREVPRICE
        var price = md?.GetDecimal("LAST") ?? sec.GetDecimal("MARKETPRICE") ?? sec.GetDecimal("PREVPRICE");
        return new Quote(
            sec.GetString("SECID") ?? "",
            sec.GetString("SHORTNAME") ?? "",
            price,
            md?.GetDecimal("CHANGE"),
            md?.GetDecimal("LASTTOPREVPRICE"),
            md?.GetLong("VOLTODAY"),
            md?.GetDateTime("SYSTIME"));
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
