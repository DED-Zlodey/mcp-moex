using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Infrastructure.Snapshots;

/// <summary>
/// Периодически сохраняет объединённый снапшот рынка (акции + облигации + металлы) в Redis
/// для последующих сравнений. Классы опрашиваются в окнах своих торговых сессий;
/// от дублей защищает дедуп по биржевому времени данных (SYSTIME) — отдельно для каждого класса.
/// </summary>
public class MarketSnapshotWorker : BackgroundService
{
    private readonly IMoexRepository _moex;
    private readonly ISnapshotRepository _snapshots;
    private readonly ILogger<MarketSnapshotWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _retention;
    private readonly Func<DateTime> _moscowNow;

    /// <summary>Время последних сохранённых рыночных данных (SYSTIME биржи) по каждому классу. Защита от дублей.</summary>
    private readonly Dictionary<AssetClass, DateTime> _lastDataTime = new();

    public MarketSnapshotWorker(
        IMoexRepository moex,
        ISnapshotRepository snapshots,
        ILogger<MarketSnapshotWorker> logger,
        TimeSpan? interval = null,
        TimeSpan? retention = null,
        Func<DateTime>? moscowNow = null)
    {
        _moex = moex;
        _snapshots = snapshots;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(5);
        _retention = retention ?? TimeSpan.FromDays(7);
        _moscowNow = moscowNow ?? (() => TradingSchedule.MoscowNow);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MarketSnapshotWorker запущен: интервал {Interval}, ретеншн {Retention}", _interval, _retention);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
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

    private async Task TickAsync(CancellationToken ct)
    {
        var moscowNow = _moscowNow();

        // Ночью (0:00–9:00 МСК) не опрашивается ни один класс — данные всё равно неизменны.
        // Выходные специально не пропускаем: MOEX проводит доп. сессии в часть выходных дней,
        // а от дублей в неторговый день защищает дедуп по SYSTIME.
        if (!TradingSchedule.ShouldPoll(AssetClass.Share, moscowNow))
        {
            _logger.LogDebug("Вне окон торговых сессий (МСК {Time:HH:mm}) — опрос ISS пропущен", moscowNow);
            return;
        }

        var shares = await _moex.GetAllShareQuotesAsync(ct);
        if (shares.Count == 0)
        {
            _logger.LogWarning("Снапшот пропущен: ISS вернул пустой список котировок акций");
            return;
        }

        var quotes = new List<Quote>(shares);
        var changed = MarkDataTime(AssetClass.Share, MaxDataTime(shares));

        // Облигации торгуются только в основную сессию — после 19:00 МСК не опрашиваем
        if (TradingSchedule.ShouldPoll(AssetClass.Bond, moscowNow))
        {
            var bonds = await _moex.GetAllBondQuotesAsync(ct);
            if (bonds.Count == 0)
            {
                _logger.LogWarning("Не удалось получить котировки облигаций — в снапшот не попадут");
            }
            else
            {
                quotes.AddRange(bonds);
                changed |= MarkDataTime(AssetClass.Bond, MaxDataTime(bonds));
            }
        }

        if (TradingSchedule.ShouldPoll(AssetClass.Metal, moscowNow))
        {
            var metals = await _moex.GetMetalPricesAsync(ct);
            if (metals.Count == 0)
            {
                _logger.LogWarning("Не удалось получить цены металлов — в снапшот не попадут");
            }
            else
            {
                var metalQuotes = metals
                    .Select(m => new Quote(m.Ticker, m.Name, m.Price, m.Change, null, null, m.Time,
                        AssetClass.Metal, AssetClass.Metal.PriceUnit()))
                    .ToList();
                quotes.AddRange(metalQuotes);
                changed |= MarkDataTime(AssetClass.Metal, MaxDataTime(metalQuotes));
            }
        }

        // Если биржевое время данных не изменилось ни у одного класса (праздник, нет торгов) — не пишем дубль
        if (!changed)
        {
            _logger.LogDebug("Данные ISS не изменились ни по одному классу — снапшот пропущен");
        }
        else
        {
            await _snapshots.SaveSnapshotAsync(new MarketSnapshot(DateTime.UtcNow, quotes), ct);
            _logger.LogInformation("Снапшот рынка сохранён: {Count} инструментов", quotes.Count);
        }

        await _snapshots.CleanupOlderThanAsync(_retention, ct);
    }

    /// <summary>Обновить время данных класса. Возвращает true, если данные изменились и снапшот надо писать.</summary>
    private bool MarkDataTime(AssetClass assetClass, DateTime? dataTime)
    {
        // Без SYSTIME дедуп невозможен — пишем (старое поведение)
        if (dataTime is null)
            return true;
        if (_lastDataTime.TryGetValue(assetClass, out var last) && last == dataTime)
            return false;
        _lastDataTime[assetClass] = dataTime.Value;
        return true;
    }

    private static DateTime? MaxDataTime(IReadOnlyList<Quote> quotes) =>
        quotes.Select(q => q.Time).Where(t => t is not null).Max();
}
