using MoexMcp.Domain.Models;

namespace MoexMcp.Domain.Repositories;

/// <summary>Доступ к данным Московской биржи (ISS MOEX).</summary>
public interface IMoexRepository
{
    /// <summary>Текущая котировка инструмента (для акций — основной режим TQBR).</summary>
    Task<Quote?> GetQuoteAsync(string ticker, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);

    /// <summary>Котировка облигации: цена в % от номинала, доходность, НКД.</summary>
    Task<Quote?> GetBondQuoteAsync(string ticker, CancellationToken ct = default);

    /// <summary>Котировки всех акций основного режима TQBR одним запросом.</summary>
    Task<IReadOnlyList<Quote>> GetAllShareQuotesAsync(CancellationToken ct = default);

    /// <summary>Котировки всех облигаций основных режимов (TQCB + TQOB).</summary>
    Task<IReadOnlyList<Quote>> GetAllBondQuotesAsync(CancellationToken ct = default);

    /// <summary>Цены драгметаллов (GLDRUB_TOM, SLVRUB_TOM), ₽/грамм.</summary>
    Task<IReadOnlyList<MetalPrice>> GetMetalPricesAsync(CancellationToken ct = default);

    /// <summary>Поиск бумаг по названию или тикеру.</summary>
    Task<IReadOnlyList<SecurityInfo>> SearchSecuritiesAsync(string query, int limit = 20, CancellationToken ct = default);

    /// <summary>Новости с сайта MOEX.</summary>
    Task<IReadOnlyList<SiteNewsItem>> GetSiteNewsAsync(int limit = 20, CancellationToken ct = default);

    /// <summary>OHLC-свечи. intervalMinutes: 1, 10, 60 (час), 24 (день).</summary>
    Task<IReadOnlyList<Candle>> GetCandlesAsync(string ticker, int intervalMinutes, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);

    /// <summary>Дневные цены закрытия за период.</summary>
    Task<IReadOnlyList<DailyPrice>> GetPriceHistoryAsync(string ticker, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);

    /// <summary>Индексы (IMOEX, RTSI).</summary>
    Task<IReadOnlyList<IndexQuote>> GetIndicesAsync(CancellationToken ct = default);

    /// <summary>Курсы валют (USD/RUB, EUR/RUB).</summary>
    Task<IReadOnlyList<CurrencyRate>> GetCurrencyRatesAsync(CancellationToken ct = default);
}
