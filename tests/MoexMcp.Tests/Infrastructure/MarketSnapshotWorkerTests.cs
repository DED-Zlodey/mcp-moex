using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using MoexMcp.Domain.Models;
using MoexMcp.Infrastructure.Snapshots;
using MoexMcp.Tests.Application;

namespace MoexMcp.Tests.Infrastructure;

public class MarketSnapshotWorkerTests
{
    private static readonly DateTime DataTime = new(2026, 8, 24, 12, 0, 0);

    /// <summary>Воркер с ускоренным тиком и подменённым московским временем.</summary>
    private static MarketSnapshotWorker Worker(
        FakeMoexRepository moex, FakeSnapshotRepository snapshots, DateTime moscowNow) =>
        new(moex, snapshots, NullLogger<MarketSnapshotWorker>.Instance,
            interval: TimeSpan.FromMilliseconds(20),
            retention: TimeSpan.FromDays(7),
            moscowNow: () => moscowNow);

    private static FakeMoexRepository MoexWithAllClasses(DateTime time) => new()
    {
        AllQuotes = [TestData.Quote("SBER", 270, 1m, time)],
        AllBondQuotes = [TestData.Quote("SU26243RMFS4", 70, 0.1m, time, AssetClass.Bond)],
        MetalPrices = [new MetalPrice("GLDRUB_TOM", "Золото", 12000, 5, time)]
    };

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
                return true;
            await Task.Delay(20);
        }
        return false;
    }

    private static async Task RunWorkerAsync(MarketSnapshotWorker worker, Func<Task> body)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await worker.StartAsync(cts.Token);
        try
        {
            await body();
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Snapshot_ContainsAllClasses()
    {
        var moex = MoexWithAllClasses(DataTime);
        var snapshots = new FakeSnapshotRepository();
        var worker = Worker(moex, snapshots, new DateTime(2026, 8, 24, 12, 0, 0)); // пн, день

        await RunWorkerAsync(worker, async () =>
        {
            Assert.True(await WaitForAsync(() => snapshots.SavedCount >= 1));
        });

        var snapshot = snapshots.Last!;
        Assert.Contains(snapshot.Quotes, q => q.Class == AssetClass.Share);
        Assert.Contains(snapshot.Quotes, q => q.Class == AssetClass.Bond);
        Assert.Contains(snapshot.Quotes, q => q.Class == AssetClass.Metal);
    }

    [Fact]
    public async Task UnchangedDataTime_SnapshotNotDuplicated()
    {
        var moex = MoexWithAllClasses(DataTime);
        var snapshots = new FakeSnapshotRepository();
        var worker = Worker(moex, snapshots, new DateTime(2026, 8, 24, 12, 0, 0));

        await RunWorkerAsync(worker, async () =>
        {
            Assert.True(await WaitForAsync(() => snapshots.SavedCount >= 1));
            await Task.Delay(200); // несколько тиков с теми же SYSTIME
            Assert.Equal(1, snapshots.SavedCount);
            Assert.True(moex.AllQuotesCalls > 2); // опрос ISS при этом идёт каждый тик
        });
    }

    [Fact]
    public async Task ChangedDataTimeOfOneClass_SnapshotSavedAgain()
    {
        var moex = MoexWithAllClasses(DataTime);
        var snapshots = new FakeSnapshotRepository();
        var worker = Worker(moex, snapshots, new DateTime(2026, 8, 24, 12, 0, 0));

        await RunWorkerAsync(worker, async () =>
        {
            Assert.True(await WaitForAsync(() => snapshots.SavedCount >= 1));
            // Изменился SYSTIME только у акций — снапшот всё равно пишется
            moex.AllQuotes = [TestData.Quote("SBER", 271, 1m, DataTime.AddMinutes(5))];
            Assert.True(await WaitForAsync(() => snapshots.SavedCount >= 2));
        });
    }

    [Fact]
    public async Task AfterBondSession_BondsAreNotPolled()
    {
        var moex = MoexWithAllClasses(DataTime);
        var snapshots = new FakeSnapshotRepository();
        var worker = Worker(moex, snapshots, new DateTime(2026, 8, 24, 20, 0, 0)); // пн, вечер — облигации закрыты

        await RunWorkerAsync(worker, async () =>
        {
            Assert.True(await WaitForAsync(() => moex.AllQuotesCalls > 0 && moex.MetalsCalls > 0));
            await Task.Delay(100);
            Assert.Equal(0, moex.AllBondsCalls);
        });
    }

    [Fact]
    public async Task TradingWeekend_IsNotSkipped()
    {
        var moex = MoexWithAllClasses(DataTime);
        var snapshots = new FakeSnapshotRepository();
        var worker = Worker(moex, snapshots, new DateTime(2026, 8, 22, 12, 0, 0)); // суббота, день

        await RunWorkerAsync(worker, async () =>
        {
            // MOEX проводит доп. сессии в часть выходных — опрос идёт и в субботу
            Assert.True(await WaitForAsync(() => snapshots.SavedCount >= 1));
        });
    }

    [Fact]
    public async Task Night_IssIsNotPolled()
    {
        var moex = MoexWithAllClasses(DataTime);
        var snapshots = new FakeSnapshotRepository();
        var worker = Worker(moex, snapshots, new DateTime(2026, 8, 24, 2, 0, 0)); // ночь

        await RunWorkerAsync(worker, async () =>
        {
            await Task.Delay(200);
            Assert.Equal(0, moex.AllQuotesCalls);
            Assert.Equal(0, moex.AllBondsCalls);
            Assert.Equal(0, moex.MetalsCalls);
            Assert.Equal(0, snapshots.SavedCount);
        });
    }
}
