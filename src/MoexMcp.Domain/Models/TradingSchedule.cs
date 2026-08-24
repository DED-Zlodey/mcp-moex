namespace MoexMcp.Domain.Models;

/// <summary>
/// Часы торговых сессий MOEX по классам активов (константы, пн–пт; праздники не зашиваем —
/// в праздник ISS отдаёт прежний SYSTIME, и дедуп по времени данных сам отсеивает дубли).
/// </summary>
public static class TradingSchedule
{
    private static readonly TimeZoneInfo MoscowTz = GetMoscowTimeZone();

    /// <summary>Текущее московское время.</summary>
    public static DateTime MoscowNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MoscowTz);

    /// <summary>Идут ли сейчас торги по классу актива (по московскому времени).</summary>
    public static bool IsSessionActive(AssetClass assetClass, DateTime moscowNow)
    {
        if (moscowNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        var t = moscowNow.TimeOfDay;
        return assetClass switch
        {
            // Акции: основная сессия 9:50–18:40 + вечерняя 19:00–23:50 (перерыв на клиринг не учитываем)
            AssetClass.Share => t >= new TimeSpan(9, 50, 0) && t < new TimeSpan(23, 50, 0),
            // Облигации: только основная сессия 9:50–18:40
            AssetClass.Bond => t >= new TimeSpan(9, 50, 0) && t < new TimeSpan(18, 40, 0),
            // Валюта и драгметаллы (TOM): 10:00–23:50
            _ => t >= new TimeSpan(10, 0, 0) && t < new TimeSpan(23, 50, 0)
        };
    }

    /// <summary>
    /// Стоит ли воркеру опрашивать класс: окно сессии с запасом, без привязки к дню недели
    /// (MOEX проводит доп. сессии в часть выходных — пропускать их нельзя).
    /// Ночью (0:00–9:00 МСК) не опрашивается ничего: данные всё равно неизменны.
    /// </summary>
    public static bool ShouldPoll(AssetClass assetClass, DateTime moscowNow)
    {
        var t = moscowNow.TimeOfDay;
        return assetClass switch
        {
            AssetClass.Share => t >= new TimeSpan(9, 0, 0),
            AssetClass.Bond => t >= new TimeSpan(9, 0, 0) && t < new TimeSpan(19, 0, 0),
            _ => t >= new TimeSpan(9, 30, 0)
        };
    }

    /// <summary>Строка статуса рынка для ответов инструментов.</summary>
    public static string Describe(AssetClass assetClass, DateTime? dataTime, DateTime moscowNow)
    {
        var data = dataTime is null ? "н/д" : $"{dataTime:HH:mm:ss dd.MM}";
        return IsSessionActive(assetClass, moscowNow)
            ? $"Торги идут (данные на {data})"
            : $"Вне сессии, данные на {data}";
    }

    private static TimeZoneInfo GetMoscowTimeZone()
    {
        // Windows и Linux используют разные идентификаторы зон
        try { return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"); }
    }
}
