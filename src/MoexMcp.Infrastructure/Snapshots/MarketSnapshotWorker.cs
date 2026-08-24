using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Infrastructure.Snapshots;

/// <summary>Периодически сохраняет снапшот всех акций TQBR в Redis для последующих сравнений.</summary>
public class MarketSnapshotWorker : BackgroundService
{
    private readonly IMoexRepository _moex;
    private readonly ISnapshotRepository _snapshots;
    private readonly ILogger<MarketSnapshotWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _retention;

    /// <summary>Время последних сохранённых рыночных данных (SYSTIME биржи). Защита от дублей.</summary>
    private DateTime? _lastDataTime;

    private static readonly TimeZoneInfo MoscowTz = GetMoscowTimeZone();

    public MarketSnapshotWorker(
        IMoexRepository moex,
        ISnapshotRepository snapshots,
        ILogger<MarketSnapshotWorker> logger,
        TimeSpan? interval = null,
        TimeSpan? retention = null)
    {
        _moex = moex;
        _snapshots = snapshots;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(5);
        _retention = retention ?? TimeSpan.FromDays(7);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MarketSnapshotWorker запущен: интервал {Interval}, ретеншн {Retention}", _interval, _retention);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (IsWeekendInMoscow())
                {
                    // В субботу и воскресенье торгов нет — в ISS не ходим, Redis не засоряем
                    _logger.LogDebug("Выходной день (МСК) — опрос ISS пропущен");
                }
                else
                {
                    var quotes = await _moex.GetAllShareQuotesAsync(stoppingToken);
                    if (quotes.Count == 0)
                    {
                        _logger.LogWarning("Снапшот пропущен: ISS вернул пустой список котировок");
                    }
                    else
                    {
                        // Если биржевое время данных не изменилось, это те же цены (праздник, ночь) — не пишем дубль
                        var dataTime = quotes.Select(q => q.Time).Where(t => t is not null).Max();
                        if (dataTime is not null && dataTime == _lastDataTime)
                        {
                            _logger.LogDebug("Данные ISS не изменились ({DataTime}) — снапшот пропущен", dataTime);
                        }
                        else
                        {
                            await _snapshots.SaveSnapshotAsync(new MarketSnapshot(DateTime.UtcNow, quotes), stoppingToken);
                            _lastDataTime = dataTime;
                            _logger.LogInformation("Снапшот рынка сохранён: {Count} бумаг, данные на {DataTime}", quotes.Count, dataTime);
                        }
                    }

                    await _snapshots.CleanupOlderThanAsync(_retention, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Воркер не должен падать из-за сетевых ошибок — ждём следующий тик
                _logger.LogError(ex, "Ошибка при сохранении снапшота рынка");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static bool IsWeekendInMoscow()
    {
        var dayOfWeek = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MoscowTz).DayOfWeek;
        return dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    private static TimeZoneInfo GetMoscowTimeZone()
    {
        // Windows и Linux используют разные идентификаторы зон
        try { return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"); }
    }
}
