using MoexMcp.Domain.Models;

namespace MoexMcp.Domain.Repositories;

/// <summary>Хранилище снапшотов рынка для сравнений за прошлые периоды.</summary>
public interface ISnapshotRepository
{
    Task SaveSnapshotAsync(MarketSnapshot snapshot, CancellationToken ct = default);

    /// <summary>Ближайший по времени снапшот к указанному моменту (null, если снапшотов нет).</summary>
    Task<MarketSnapshot?> GetNearestSnapshotAsync(DateTime moment, CancellationToken ct = default);

    /// <summary>Удалить снапшоты старше указанного возраста.</summary>
    Task CleanupOlderThanAsync(TimeSpan retention, CancellationToken ct = default);
}
