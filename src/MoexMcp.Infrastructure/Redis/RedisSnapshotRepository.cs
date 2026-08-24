using System.Text.Json;
using Microsoft.Extensions.Logging;
using MoexMcp.Domain.Models;
using MoexMcp.Domain.Repositories;
using StackExchange.Redis;

namespace MoexMcp.Infrastructure.Redis;

/// <summary>
/// Снапшоты рынка в Redis:
///  - moexmcp:snap:{unixTs} — JSON с котировками;
///  - moexmcp:snap:index — sorted-set (score = unixTs) для поиска ближайшего по времени.
/// </summary>
public class RedisSnapshotRepository : ISnapshotRepository
{
    private const string KeyPrefix = "moexmcp:snap:";
    private const string IndexKey = "moexmcp:snap:index";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _db;
    private readonly ILogger<RedisSnapshotRepository> _logger;

    public RedisSnapshotRepository(IConnectionMultiplexer redis, ILogger<RedisSnapshotRepository> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task SaveSnapshotAsync(MarketSnapshot snapshot, CancellationToken ct = default)
    {
        var ts = new DateTimeOffset(snapshot.TakenAt, TimeSpan.Zero).ToUnixTimeSeconds();
        var json = JsonSerializer.Serialize(snapshot.Quotes, JsonOptions);

        await _db.StringSetAsync(KeyPrefix + ts, json);
        await _db.SortedSetAddAsync(IndexKey, ts, ts);
        _logger.LogDebug("Снапшот сохранён: {Count} котировок на {TakenAt}", snapshot.Quotes.Count, snapshot.TakenAt);
    }

    public async Task<MarketSnapshot?> GetNearestSnapshotAsync(DateTime moment, CancellationToken ct = default)
    {
        var ts = new DateTimeOffset(moment, TimeSpan.Zero).ToUnixTimeSeconds();

        // Ближайший снизу и сверху, выбираем ближний по времени
        var below = await _db.SortedSetRangeByScoreAsync(IndexKey, double.NegativeInfinity, ts, Exclude.None, Order.Descending, 0, 1);
        var above = await _db.SortedSetRangeByScoreAsync(IndexKey, ts, double.PositiveInfinity, Exclude.None, Order.Ascending, 0, 1);

        long? best = (below.Length > 0, above.Length > 0) switch
        {
            (true, true) => Math.Abs((long)below[0]! - ts) <= Math.Abs((long)above[0]! - ts) ? (long)below[0]! : (long)above[0]!,
            (true, false) => (long)below[0]!,
            (false, true) => (long)above[0]!,
            _ => null
        };

        if (best is null)
            return null;

        var json = await _db.StringGetAsync(KeyPrefix + best.Value);
        if (!json.HasValue)
            return null;

        var quotes = JsonSerializer.Deserialize<List<Quote>>((string)json!, JsonOptions) ?? [];
        var takenAt = DateTimeOffset.FromUnixTimeSeconds(best.Value).UtcDateTime;
        return new MarketSnapshot(takenAt, quotes);
    }

    public async Task CleanupOlderThanAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(retention).ToUnixTimeSeconds();

        // Сначала забираем членов индекса, чтобы удалить сами ключи
        var stale = await _db.SortedSetRangeByScoreAsync(IndexKey, double.NegativeInfinity, cutoff);
        if (stale.Length > 0)
        {
            var keys = stale.Select(m => (RedisKey)(KeyPrefix + (long)m!)).ToArray();
            await _db.KeyDeleteAsync(keys);
        }
        var removed = await _db.SortedSetRemoveRangeByScoreAsync(IndexKey, double.NegativeInfinity, cutoff);
        if (removed > 0)
            _logger.LogInformation("Удалено устаревших снапшотов: {Count}", removed);
    }
}
