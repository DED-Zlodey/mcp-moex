using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Application.Services;

/// <summary>
/// Сравнения и ранжирование по доходности.
/// Цены: ближайший снапшот из Redis (внутри ретеншна), для старых дат — дневная история ISS.
/// </summary>
public class ComparisonService : IComparisonService
{
    /// <summary>Снапшот считается подходящим, если отстоит от запрошенного момента не более чем на сутки.</summary>
    private static readonly TimeSpan MaxSnapshotDistance = TimeSpan.FromDays(1);

    private readonly IMoexRepository _moex;
    private readonly ISnapshotRepository _snapshots;

    public ComparisonService(IMoexRepository moex, ISnapshotRepository snapshots)
    {
        _moex = moex;
        _snapshots = snapshots;
    }

    public async Task<IReadOnlyList<InstrumentPerformance>> CompareInstrumentsAsync(
        IReadOnlyList<string> tickers, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        var result = new List<InstrumentPerformance>();

        foreach (var raw in tickers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var ticker = raw.ToUpperInvariant();
            var start = await GetPriceAtAsync(ticker, from, assetClass, ct);
            var end = await GetPriceAtAsync(ticker, to, assetClass, ct);
            if (start is null || end is null || start.Value.Price <= 0)
                continue;

            var changePercent = (end.Value.Price - start.Value.Price) / start.Value.Price * 100m;
            result.Add(new InstrumentPerformance(
                ticker,
                end.Value.Name,
                start.Value.Price,
                end.Value.Price,
                Math.Round(changePercent, 2),
                start.Value.Time,
                end.Value.Time,
                end.Value.Source,
                assetClass));
        }

        return result.OrderByDescending(p => p.ChangePercent).ToList();
    }

    public async Task<IReadOnlyList<InstrumentPerformance>?> RankByPerformanceAsync(
        DateTime from, DateTime to, int limit, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default)
    {
        // Для ранжирования всего рынка по одному тикеру в историю не ходим — только снапшоты
        var startSnap = await _snapshots.GetNearestSnapshotAsync(from, ct);
        var endSnap = await _snapshots.GetNearestSnapshotAsync(to, ct);
        if (startSnap is null || endSnap is null)
            return null;
        if ((startSnap.TakenAt - from).Duration() > MaxSnapshotDistance ||
            (endSnap.TakenAt - to).Duration() > MaxSnapshotDistance)
            return null;

        var startPrices = startSnap.Quotes
            .Where(q => q.Class == assetClass && q.Price is > 0)
            .ToDictionary(q => q.Ticker, q => q, StringComparer.OrdinalIgnoreCase);

        var result = new List<InstrumentPerformance>();
        foreach (var endQuote in endSnap.Quotes.Where(q => q.Class == assetClass && q.Price is > 0))
        {
            if (!startPrices.TryGetValue(endQuote.Ticker, out var startQuote))
                continue;

            var changePercent = (endQuote.Price!.Value - startQuote.Price!.Value) / startQuote.Price.Value * 100m;
            result.Add(new InstrumentPerformance(
                endQuote.Ticker,
                endQuote.Name,
                startQuote.Price.Value,
                endQuote.Price.Value,
                Math.Round(changePercent, 2),
                startSnap.TakenAt,
                endSnap.TakenAt,
                "snapshot",
                assetClass));
        }

        return result.OrderByDescending(p => p.ChangePercent).Take(limit).ToList();
    }

    /// <summary>Цена тикера на момент: снапшот (±1 сутки) либо последнее дневное закрытие не позже момента.</summary>
    private async Task<(decimal Price, DateTime Time, string Source, string Name)?> GetPriceAtAsync(
        string ticker, DateTime moment, AssetClass assetClass, CancellationToken ct)
    {
        var snap = await _snapshots.GetNearestSnapshotAsync(moment, ct);
        if (snap is not null && (snap.TakenAt - moment).Duration() <= MaxSnapshotDistance)
        {
            var quote = snap.Quotes.FirstOrDefault(q =>
                q.Class == assetClass && q.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase));
            if (quote?.Price is > 0)
                return (quote.Price.Value, snap.TakenAt, "snapshot", quote.Name);
        }

        var history = await _moex.GetPriceHistoryAsync(ticker, moment.AddDays(-10), moment, assetClass, ct);
        var last = history.LastOrDefault();
        if (last is not null)
            return (last.Close, last.Date, "history", ticker);

        return null;
    }
}
