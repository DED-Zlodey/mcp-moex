namespace MoexMcp.Domain.Repositories;

/// <summary>Простой TTL-кэш произвольных данных.</summary>
public interface ICacheRepository
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
}
