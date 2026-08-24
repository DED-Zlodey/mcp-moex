using MoexMcp.Domain.Models;

namespace MoexMcp.Application.Services;

public interface IHistoryService
{
    Task<IReadOnlyList<Candle>> GetCandlesAsync(string ticker, int intervalMinutes, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);
    Task<IReadOnlyList<DailyPrice>> GetPriceHistoryAsync(string ticker, DateTime from, DateTime to, AssetClass assetClass = AssetClass.Share, CancellationToken ct = default);
}
