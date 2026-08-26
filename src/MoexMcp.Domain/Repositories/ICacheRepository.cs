namespace MoexMcp.Domain.Repositories;

/// <summary>Простой TTL-кэш произвольных данных.</summary>
public interface ICacheRepository
{
    /// <summary>
    /// Асинхронно получает значение из кэша по указанному ключу.
    /// </summary>
    /// <typeparam name="T">Тип ожидаемого значения.</typeparam>
    /// <param name="key">Ключ, по которому хранится значение в кэше.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>
    /// Задача, представляющая асинхронную операцию. Результат содержит значение из кэша,
    /// если оно найдено и может быть приведено к типу <typeparamref name="T"/>;
    /// в противном случае — <see langword="null"/>.
    /// </returns>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Асинхронно сохраняет значение в кэше по указанному ключу с заданным временем жизни.
    /// </summary>
    /// <typeparam name="T">Тип сохраняемого значения.</typeparam>
    /// <param name="key">Ключ, по которому значение будет сохранено в кэше.</param>
    /// <param name="value">Значение для сохранения.</param>
    /// <param name="ttl">Время жизни записи в кэше.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Задача, представляющая асинхронную операцию сохранения.</returns>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
}
