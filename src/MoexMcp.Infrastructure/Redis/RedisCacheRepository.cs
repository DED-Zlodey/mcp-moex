using System.Text.Json;
using Microsoft.Extensions.Logging;
using MoexMcp.Domain.Repositories;
using StackExchange.Redis;

namespace MoexMcp.Infrastructure.Redis;

/// <summary>
/// TTL-кэш на Redis. Все ключи с префиксом moexmcp:, чтобы не пересекаться с чужими данными.
/// </summary>
public class RedisCacheRepository : ICacheRepository
{
    /// <summary>
    /// Префикс, добавляемый ко всем ключам кэша в Redis.
    /// Все ключи формируются с префиксом <c>moexmcp:cache:</c>,
    /// чтобы изолировать данные приложения от чужих данных в общем хранилище
    /// и избежать случайных коллизий имён.
    /// </summary>
    private const string KeyPrefix = "moexmcp:cache:";

    /// <summary>
    /// Параметры сериализации и десериализации JSON, используемые при записи и чтении значений из кэша Redis.
    /// Инициализируются веб-умолчаниями для обеспечения согласованного формата ключей JSON.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Экземпляр базы данных Redis, используемый для выполнения операций чтения и записи кэша.
    /// </summary>
    private readonly IDatabase _db;

    /// <summary>
    /// Логгер для регистрации предупреждений и ошибок, возникающих при работе с кэшем Redis.
    /// </summary>
    private readonly ILogger<RedisCacheRepository> _logger;

    /// <summary>
    /// Репозиторий TTL-кэша на основе Redis.
    /// Все ключи автоматически дополняются префиксом moexmcp:, чтобы исключить
    /// пересечение с данными других приложений.
    /// При ошибках чтения или записи выполняется логирование, а операция
    /// продолжается как при отсутствии кэша.
    /// </summary>
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
