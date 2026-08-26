namespace MoexMcp.Domain.Models;

/// <summary>
/// Часы торговых сессий MOEX по классам активов (константы, пн–пт; праздники не зашиваем —
/// в праздник ISS отдаёт прежний SYSTIME, и дедуп по времени данных сам отсеивает дубли).
/// </summary>
public static class TradingSchedule
{
    /// <summary>
    /// Информация о московском часовом поясе, используемая для перевода UTC в московское время.
    /// </summary>
    private static readonly TimeZoneInfo MoscowTz = GetMoscowTimeZone();

    /// <summary>
    /// Текущее московское время.
    /// </summary>
    public static DateTime MoscowNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MoscowTz);

    /// <summary>
    /// Идут ли сейчас торги по классу актива (по московскому времени).
    /// </summary>
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
    /// Определяет, стоит ли воркеру опрашивать указанный класс активов в заданный момент московского времени.
    /// Проверка выполняется по торговому окну класса с запасом, без привязки к дню недели,
    /// чтобы не пропустить дополнительные сессии MOEX в часть выходных.
    /// Ночью (с 0:00 до 9:00 МСК) опрос не производится, так как данные неизменны.
    /// </summary>
    /// <param name="assetClass">Класс актива MOEX.</param>
    /// <param name="moscowNow">Текущее московское время.</param>
    /// <returns>true, если класс активов находится в окне опроса; в противном случае — false.</returns>
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

    /// <summary>
    /// Формирует строку статуса рынка для ответов инструментов:
    /// торги идут или вне сессии, с указанием времени данных.
    /// </summary>
    /// <param name="assetClass">Класс актива MOEX.</param>
    /// <param name="dataTime">Время данных. Если не задано, выводится «н/д».</param>
    /// <param name="moscowNow">Текущее московское время.</param>
    /// <return>Строка статуса рынка с указанием времени данных.</return>
    public static string Describe(AssetClass assetClass, DateTime? dataTime, DateTime moscowNow)
    {
        var data = dataTime is null ? "н/д" : $"{dataTime:HH:mm:ss dd.MM}";
        return IsSessionActive(assetClass, moscowNow)
            ? $"Торги идут (данные на {data})"
            : $"Вне сессии, данные на {data}";
    }

    /// <summary>
    /// Возвращает объект часового пояса для московского времени, выбирая идентификатор,
    /// доступный в текущей операционной системе.
    /// </summary>
    /// <returns>
    /// Информация о московском часовом поясе.
    /// </returns>
    private static TimeZoneInfo GetMoscowTimeZone()
    {
        // Windows и Linux используют разные идентификаторы зон
        try { return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"); }
    }
}
