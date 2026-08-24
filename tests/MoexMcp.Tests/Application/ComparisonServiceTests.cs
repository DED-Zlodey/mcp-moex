using MoexMcp.Application.Services;
using MoexMcp.Domain.Models;

namespace MoexMcp.Tests.Application;

public class ComparisonServiceTests
{
    private static readonly DateTime From = new(2026, 8, 20, 10, 0, 0);
    private static readonly DateTime To = new(2026, 8, 21, 18, 0, 0);

    [Fact]
    public async Task Compare_FromSnapshots_ComputesChangeAndRanksDesc()
    {
        var snapshots = new FakeSnapshotRepository();
        snapshots.Add(new MarketSnapshot(From, [
            TestData.Quote("SBER", 100, 0, From),
            TestData.Quote("GAZP", 200, 0, From),
        ]));
        snapshots.Add(new MarketSnapshot(To, [
            TestData.Quote("SBER", 110, 1, To),   // +10%
            TestData.Quote("GAZP", 190, -1, To),  // -5%
        ]));
        var service = new ComparisonService(new FakeMoexRepository(), snapshots);

        var result = await service.CompareInstrumentsAsync(["SBER", "GAZP"], From, To);

        Assert.Equal(2, result.Count);
        Assert.Equal("SBER", result[0].Ticker);        // выше доходность — выше место
        Assert.Equal(10.0m, result[0].ChangePercent);
        Assert.Equal("GAZP", result[1].Ticker);
        Assert.Equal(-5.0m, result[1].ChangePercent);
    }

    [Fact]
    public async Task Compare_NoSnapshots_FallsBackToDailyHistory()
    {
        var moex = new FakeMoexRepository
        {
            History = [new DailyPrice("SBER", new DateTime(2026, 8, 15), 250m)]
        };
        var service = new ComparisonService(moex, new FakeSnapshotRepository());

        var result = await service.CompareInstrumentsAsync(["SBER"], From, To);

        // Оба конца взялись из одной и той же истории (последнее закрытие ≤ момента)
        var item = Assert.Single(result);
        Assert.Equal(250m, item.StartPrice);
        Assert.Equal(250m, item.EndPrice);
        Assert.Equal(0m, item.ChangePercent);
        Assert.Equal("history", item.PriceSource);
    }

    [Fact]
    public async Task Compare_TickerWithoutPrices_IsSkipped()
    {
        var snapshots = new FakeSnapshotRepository();
        snapshots.Add(new MarketSnapshot(From, [TestData.Quote("SBER", 100, 0, From)]));
        snapshots.Add(new MarketSnapshot(To, [TestData.Quote("SBER", 110, 1, To)]));
        var service = new ComparisonService(new FakeMoexRepository(), snapshots);

        var result = await service.CompareInstrumentsAsync(["SBER", "XXXX"], From, To);

        Assert.Single(result); // XXXX без цен не попадает в выдачу
    }

    [Fact]
    public async Task Compare_DuplicateTickers_AreCountedOnce()
    {
        var snapshots = new FakeSnapshotRepository();
        snapshots.Add(new MarketSnapshot(From, [TestData.Quote("SBER", 100, 0, From)]));
        snapshots.Add(new MarketSnapshot(To, [TestData.Quote("SBER", 110, 1, To)]));
        var service = new ComparisonService(new FakeMoexRepository(), snapshots);

        var result = await service.CompareInstrumentsAsync(["SBER", "sber", "SBER"], From, To);

        Assert.Single(result);
    }

    [Fact]
    public async Task Rank_NoSnapshots_ReturnsNull()
    {
        var service = new ComparisonService(new FakeMoexRepository(), new FakeSnapshotRepository());

        Assert.Null(await service.RankByPerformanceAsync(From, To, 10));
    }

    [Fact]
    public async Task Rank_SnapshotTooFar_ReturnsNull()
    {
        var snapshots = new FakeSnapshotRepository();
        // Ближайший снапшот дальше суток от запрошенного момента — не подходит
        snapshots.Add(new MarketSnapshot(From.AddDays(3), [TestData.Quote("SBER", 100, 0, From.AddDays(3))]));
        var service = new ComparisonService(new FakeMoexRepository(), snapshots);

        Assert.Null(await service.RankByPerformanceAsync(From, To, 10));
    }

    [Fact]
    public async Task Rank_SortsByYieldAndSkipsBrokenRows()
    {
        var snapshots = new FakeSnapshotRepository();
        snapshots.Add(new MarketSnapshot(From, [
            TestData.Quote("A", 100, 0, From),
            TestData.Quote("B", 100, 0, From),
            TestData.Quote("C", 100, 0, From),
        ]));
        snapshots.Add(new MarketSnapshot(To, [
            TestData.Quote("A", 105, 1, To),   // +5%
            TestData.Quote("B", 90, -1, To),   // -10%
            TestData.Quote("C", null, null, To), // без цены — пропуск
            TestData.Quote("D", 50, 1, To),      // не было в стартовом снапшоте — пропуск
        ]));
        var service = new ComparisonService(new FakeMoexRepository(), snapshots);

        var rank = await service.RankByPerformanceAsync(From, To, 10);

        Assert.NotNull(rank);
        Assert.Equal(2, rank.Count);
        Assert.Equal("A", rank[0].Ticker);
        Assert.Equal(5.0m, rank[0].ChangePercent);
        Assert.Equal("B", rank[1].Ticker);
    }

    [Fact]
    public async Task Rank_RespectsLimit()
    {
        var snapshots = new FakeSnapshotRepository();
        snapshots.Add(new MarketSnapshot(From,
            Enumerable.Range(1, 30).Select(i => TestData.Quote($"T{i}", 100, 0, From)).ToList()));
        snapshots.Add(new MarketSnapshot(To,
            Enumerable.Range(1, 30).Select(i => TestData.Quote($"T{i}", 100 + i, 1, To)).ToList()));
        var service = new ComparisonService(new FakeMoexRepository(), snapshots);

        var rank = await service.RankByPerformanceAsync(From, To, 5);

        Assert.NotNull(rank);
        Assert.Equal(5, rank.Count);
        Assert.Equal("T30", rank[0].Ticker); // максимальная доходность
    }

    [Fact]
    public async Task Rank_FiltersByAssetClass()
    {
        var snapshots = new FakeSnapshotRepository();
        snapshots.Add(new MarketSnapshot(From, [
            TestData.Quote("SBER", 100, 0, From),
            TestData.Quote("OFZ", 70, 0, From, AssetClass.Bond),
        ]));
        snapshots.Add(new MarketSnapshot(To, [
            TestData.Quote("SBER", 120, 1, To),  // +20%
            TestData.Quote("OFZ", 71, 1, To, AssetClass.Bond), // +1.43%
        ]));
        var service = new ComparisonService(new FakeMoexRepository(), snapshots);

        var bonds = await service.RankByPerformanceAsync(From, To, 10, AssetClass.Bond);
        var shares = await service.RankByPerformanceAsync(From, To, 10);

        var bond = Assert.Single(bonds!);
        Assert.Equal("OFZ", bond.Ticker);
        Assert.Equal(AssetClass.Bond, bond.Class);
        var share = Assert.Single(shares!);
        Assert.Equal("SBER", share.Ticker);
    }

    [Fact]
    public async Task Compare_Bond_PassesAssetClassToHistoryAndResult()
    {
        var moex = new FakeMoexRepository
        {
            History = [new DailyPrice("OFZ", new DateTime(2026, 8, 15), 70m)]
        };
        var service = new ComparisonService(moex, new FakeSnapshotRepository());

        var result = await service.CompareInstrumentsAsync(["OFZ"], From, To, AssetClass.Bond);

        var item = Assert.Single(result);
        Assert.Equal(AssetClass.Bond, item.Class);
        Assert.Equal(AssetClass.Bond, moex.LastHistoryAssetClass);
    }

    [Fact]
    public async Task Compare_Bond_DoesNotMatchShareQuotesFromSnapshot()
    {
        // В снапшоте есть акция с таким тикером, но compare вызван для облигаций — совпадения нет
        var snapshots = new FakeSnapshotRepository();
        snapshots.Add(new MarketSnapshot(From, [TestData.Quote("X", 100, 0, From)]));
        snapshots.Add(new MarketSnapshot(To, [TestData.Quote("X", 110, 1, To)]));
        var service = new ComparisonService(new FakeMoexRepository(), snapshots);

        var result = await service.CompareInstrumentsAsync(["X"], From, To, AssetClass.Bond);

        Assert.Empty(result); // истории тоже нет — тикер пропущен
    }
}
