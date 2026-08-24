using System.Text.Json;
using Microsoft.Extensions.Logging;
using MoexMcp.Domain.Repositories;
using StackExchange.Redis;

namespace MoexMcp.Infrastructure.Redis;

/// <summary>TTL-кэш на Redis. Все ключи с префиксом moexmcp:, чтобы не пересекаться с чужими данными.</summary>
public class RedisCacheRepository : ICacheRepository
{
    private const string KeyPrefix = "moexmcp:cache:";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _db;
    private readonly ILogger<RedisCacheRepository> _logger;

    public RedisCacheRepository(IConnectionMultiplexer redis, ILogger<RedisCacheRepository> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await _db.StringGetAsync(KeyPrefix + key);
            return value.HasValue ? JsonSerializer.Deserialize<T>((string)value!, JsonOptions) : default;
        }
        catch (Exception ex) when (ex is RedisException or JsonException)
        {
            _logger.LogWarning(ex, "Ошибка чтения кэша Redis, ключ {Key}", key);
            return default; // кэш не должен ронять запрос — сходим за данными напрямую
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            await _db.StringSetAsync(KeyPrefix + key, JsonSerializer.Serialize(value, JsonOptions), ttl);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Ошибка записи в кэш Redis, ключ {Key}", key);
        }
    }
}
