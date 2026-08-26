namespace MoexMcp.Domain.Models;

/// <summary>
/// Класс актива на MOEX.
/// </summary>
public enum AssetClass
{
    /// <summary>
    /// Акция.
    /// </summary>
    Share,

    /// <summary>
    /// Облигация.
    /// </summary>
    Bond,

    /// <summary>
    /// Валюта.
    /// </summary>
    Currency,

    /// <summary>
    /// Металлы.
    /// </summary>
    Metal
}

public static class AssetClassExtensions
{
    /// <summary>
    /// Единица измерения цены: акции/валюта — ₽, облигации — % от номинала, металлы — ₽/грамм.
    /// </summary>
    public static string PriceUnit(this AssetClass assetClass) => assetClass switch
    {
        AssetClass.Bond => "% номинала",
        AssetClass.Metal => "₽/г",
        _ => "₽"
    };
}

/// <summary>
/// Котировка инструмента. Class по умолчанию Share — снапшоты старого формата
/// (без Class/PriceUnit/Yield/AccruedInterest) десериализуются как акции (обратная совместимость).
/// </summary>
public record Quote(
    string Ticker,
    string Name,
    decimal? Price,
    decimal? Change,
    decimal? ChangePercent,
    long? Volume,
    DateTime? Time,
    AssetClass Class = AssetClass.Share,
    string? PriceUnit = null,
    decimal? Yield = null,
    decimal? AccruedInterest = null);

/// <summary>
/// OHLC-свеча.
/// </summary>
public record Candle(
    DateTime Begin,
    decimal Open,
    decimal Close,
    decimal High,
    decimal Low,
    long Volume);

/// <summary>
/// Значение индекса (IMOEX, RTSI и т.п.).
/// </summary>
public record IndexQuote(
    string Ticker,
    decimal? Value,
    decimal? Change,
    DateTime? Time);

/// <summary>
/// Курс валютной пары.
/// </summary>
public record CurrencyRate(
    string Ticker,
    decimal? Price,
    decimal? Change,
    DateTime? Time);

/// <summary>
/// Цена драгметалла (GLDRUB_TOM, SLVRUB_TOM), ₽/грамм.
/// </summary>
public record MetalPrice(
    string Ticker,
    string Name,
    decimal? Price,
    decimal? Change,
    DateTime? Time);

/// <summary>
/// Новость с сайта MOEX.
/// </summary>
public record SiteNewsItem(
    long Id,
    string Title,
    string Tag,
    DateTime? PublishedAt);

/// <summary>
/// Краткое описание ценной бумаги (для поиска).
/// </summary>
public record SecurityInfo(
    string Ticker,
    string ShortName,
    string Name);

/// <summary>
/// Дневная цена закрытия.
/// </summary>
public record DailyPrice(
    string Ticker,
    DateTime Date,
    decimal Close);
