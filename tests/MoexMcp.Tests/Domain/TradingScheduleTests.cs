using MoexMcp.Domain.Models;

namespace MoexMcp.Tests.Domain;

public class TradingScheduleTests
{
    private static readonly DateTime Monday = new(2026, 8, 24); // понедельник
    private static readonly DateTime Saturday = new(2026, 8, 22); // суббота

    private static DateTime At(DateTime day, int h, int m) => day.AddHours(h).AddMinutes(m);

    [Theory]
    // Акции: 9:50–23:50 (основная + вечерняя сессия)
    [InlineData(AssetClass.Share, 9, 49, false)]
    [InlineData(AssetClass.Share, 9, 50, true)]
    [InlineData(AssetClass.Share, 18, 40, true)]  // перерыв на клиринг не учитываем
    [InlineData(AssetClass.Share, 23, 49, true)]
    [InlineData(AssetClass.Share, 23, 50, false)]
    // Облигации: только основная сессия 9:50–18:40
    [InlineData(AssetClass.Bond, 9, 49, false)]
    [InlineData(AssetClass.Bond, 9, 50, true)]
    [InlineData(AssetClass.Bond, 18, 39, true)]
    [InlineData(AssetClass.Bond, 18, 40, false)]
    [InlineData(AssetClass.Bond, 20, 0, false)]
    // Валюта и металлы: 10:00–23:50
    [InlineData(AssetClass.Currency, 9, 59, false)]
    [InlineData(AssetClass.Currency, 10, 0, true)]
    [InlineData(AssetClass.Currency, 23, 50, false)]
    [InlineData(AssetClass.Metal, 9, 59, false)]
    [InlineData(AssetClass.Metal, 10, 0, true)]
    [InlineData(AssetClass.Metal, 23, 49, true)]
    public void IsSessionActive_RespectsClassWindows(AssetClass assetClass, int h, int m, bool expected)
    {
        Assert.Equal(expected, TradingSchedule.IsSessionActive(assetClass, At(Monday, h, m)));
    }

    [Fact]
    public void IsSessionActive_Weekend_IsClosed()
    {
        Assert.False(TradingSchedule.IsSessionActive(AssetClass.Share, At(Saturday, 12, 0)));
        Assert.False(TradingSchedule.IsSessionActive(AssetClass.Bond, At(Saturday, 12, 0)));
    }

    [Theory]
    // Окна опроса воркера с запасом; день недели не учитывается (бывают торговые выходные)
    [InlineData(AssetClass.Share, 8, 59, false)]
    [InlineData(AssetClass.Share, 9, 0, true)]
    [InlineData(AssetClass.Share, 23, 59, true)]
    [InlineData(AssetClass.Bond, 9, 0, true)]
    [InlineData(AssetClass.Bond, 18, 59, true)]
    [InlineData(AssetClass.Bond, 19, 0, false)]
    [InlineData(AssetClass.Metal, 9, 29, false)]
    [InlineData(AssetClass.Metal, 9, 30, true)]
    public void ShouldPoll_RespectsClassWindows(AssetClass assetClass, int h, int m, bool expected)
    {
        Assert.Equal(expected, TradingSchedule.ShouldPoll(assetClass, At(Monday, h, m)));
    }

    [Fact]
    public void ShouldPoll_Weekend_StillPolls()
    {
        // MOEX проводит доп. сессии в часть выходных — пропускать нельзя
        Assert.True(TradingSchedule.ShouldPoll(AssetClass.Share, At(Saturday, 12, 0)));
    }

    [Fact]
    public void ShouldPoll_Night_NothingIsPolled()
    {
        Assert.False(TradingSchedule.ShouldPoll(AssetClass.Share, At(Monday, 2, 0)));
        Assert.False(TradingSchedule.ShouldPoll(AssetClass.Bond, At(Monday, 2, 0)));
        Assert.False(TradingSchedule.ShouldPoll(AssetClass.Metal, At(Monday, 2, 0)));
    }

    [Fact]
    public void Describe_ActiveSession_SaysTrading()
    {
        var text = TradingSchedule.Describe(AssetClass.Share, At(Monday, 12, 0), At(Monday, 12, 0));
        Assert.Contains("Торги идут", text);
        Assert.Contains("12:00:00", text);
    }

    [Fact]
    public void Describe_OffSession_SaysClosed()
    {
        var text = TradingSchedule.Describe(AssetClass.Bond, At(Monday, 18, 40), At(Monday, 20, 0));
        Assert.Contains("Вне сессии", text);
        Assert.Contains("18:40:00", text);
    }
}
