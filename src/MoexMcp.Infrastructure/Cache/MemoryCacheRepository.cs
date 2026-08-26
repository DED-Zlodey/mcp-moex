using System.Collections.Concurrent;
using System.Text.Json;
using MoexMcp.Domain.Repositories;

namespace MoexMcp.Infrastructure.Cache;

/// <summary>
/// Реализация репозитория кэша, хранящего записи в памяти текущего процесса с использованием абсолютного TTL.
/// Используется как локальная альтернатива Redis в случаях, когда распределённое кэширование не настроено.
/// Данные не сохраняются между перезапусками приложения, поэтому подходит для временного кэширования внешних ответов.
/// </summary>
public class MemoryCacheRepository : ICacheRepository
{
    /// <summary>
    /// Параметры сериализатора JSON, используемые для преобразования кэшируемых объектов в строку JSON и обратной десериализации при чтении из кэша.
    /// Инициализированы веб-умолчаниями для единообразного именования свойств и корректной обработки данных при TTL-кэшировании в памяти.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Хранилище записей кэша, сопоставляющее ключ с сериализованным JSON-представлением значения и абсолютным временем истечения срока действия.
    /// </summary>
    private readonly ConcurrentDictionary<string, (string Json, DateTimeOffset ExpiresAt)> _store = new();
    
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
                return Task.FromResult(JsonSerializer.Deserialize<T>(entry.Json, JsonOptions));

            _store.TryRemove(key, out _);
        }

        return Task.FromResult(default(T));
    }
    
    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        _store[key] = (JsonSerializer.Serialize(value, JsonOptions), DateTimeOffset.UtcNow.Add(ttl));
        return Task.CompletedTask;
    }
}
