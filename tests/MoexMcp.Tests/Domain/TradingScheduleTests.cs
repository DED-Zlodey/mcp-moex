using MoexMcp.Domain.Models;

namespace MoexMcp.Tests.Domain;

public class TradingScheduleTests
{
    /// <summary>
    /// Базовая дата, соответствующая понедельнику 24 августа 2026 года, используемая в тестах торгового расписания для формирования моментов времени внутри торговой сессии.
    /// </summary>
    private static readonly DateTime Monday = new(2026, 8, 24); // понедельник

    /// <summary>
    /// Дата субботы, используемая в тестах для проверки поведения торгового расписания в выходные дни.
    /// </summary>
    private static readonly DateTime Saturday = new(2026, 8, 22); // суббота

    /// <summary>
    /// Вспомогательный метод для формирования значения даты и времени на основе заданного дня с добавлением указанного количества часов и минут.
    /// </summary>
    /// <param name="day">Базовая дата, к которой добавляются часы и минуты.</param>
    /// <param name="h">Количество часов, добавляемых к базовой дате.</param>
    /// <param name="m">Количество минут, добавляемых к базовой дате.</param>
    /// <returns>Значение даты и времени, полученное добавлением к базовой дате указанных часов и минут.</returns>
    private static DateTime At(DateTime day, int h, int m) => day.AddHours(h).AddMinutes(m);

    /// <summary>
    /// Проверяет, что метод <see cref="TradingSchedule.IsSessionActive(AssetClass, DateTime)"/> корректно определяет активность торговой сессии в зависимости от класса актива и временного окна.
    /// </summary>
    /// <param name="assetClass">Класс актива, для которого проверяется состояние сессии.</param>
    /// <param name="h">Часы, добавляемые к базовой дате понедельника для формирования проверяемого момента времени.</param>
    /// <param name="m">Минуты, добавляемые к базовой дате понедельника для формирования проверяемого момента времени.</param>
    /// <param name="expected">Ожидаемое значение активности сессии в указанный момент времени.</param>
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

    /// <summary>
    /// Проверяет, что метод <see cref="TradingSchedule.IsSessionActive"/> возвращает <c>false</c> для акций и облигаций в субботу, подтверждая закрытие торговой сессии в выходные дни.
    /// </summary>
    [Fact]
    public void IsSessionActive_Weekend_IsClosed()
    {
        Assert.False(TradingSchedule.IsSessionActive(AssetClass.Share, At(Saturday, 12, 0)));
        Assert.False(TradingSchedule.IsSessionActive(AssetClass.Bond, At(Saturday, 12, 0)));
    }

    /// <summary>
    /// Проверяет, что метод ShouldPoll корректно определяет необходимость опроса в зависимости от торгового окна заданного класса актива.
    /// </summary>
    /// <param name="assetClass">Класс актива, для которого выполняется проверка.</param>
    /// <param name="h">Часы московского времени проверяемого момента.</param>
    /// <param name="m">Минуты московского времени проверяемого момента.</param>
    /// <param name="expected">Ожидаемый результат вызова ShouldPoll для указанного класса актива и времени.</param>
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

    /// <summary>
    /// Проверяет, что проверка необходимости опроса на выходном дне возвращает положительный результат для акций, поскольку Московская биржа проводит дополнительные торговые сессии в часть выходных.
    /// </summary>
    [Fact]
    public void ShouldPoll_Weekend_StillPolls()
    {
        // MOEX проводит доп. сессии в часть выходных — пропускать нельзя
        Assert.True(TradingSchedule.ShouldPoll(AssetClass.Share, At(Saturday, 12, 0)));
    }

    /// <summary>
    /// Проверяет, что в ночное время опрос не выполняется ни для одного из поддерживаемых классов активов.
    /// </summary>
    [Fact]
    public void ShouldPoll_Night_NothingIsPolled()
    {
        Assert.False(TradingSchedule.ShouldPoll(AssetClass.Share, At(Monday, 2, 0)));
        Assert.False(TradingSchedule.ShouldPoll(AssetClass.Bond, At(Monday, 2, 0)));
        Assert.False(TradingSchedule.ShouldPoll(AssetClass.Metal, At(Monday, 2, 0)));
    }

    /// <summary>
    /// Проверяет, что метод <see cref="TradingSchedule.Describe"/> возвращает строку с указанием на активные торги и текущим временем, когда торговая сессия открыта.
    /// </summary>
    [Fact]
    public void Describe_ActiveSession_SaysTrading()
    {
        var text = TradingSchedule.Describe(AssetClass.Share, At(Monday, 12, 0), At(Monday, 12, 0));
        Assert.Contains("Торги идут", text);
        Assert.Contains("12:00:00", text);
    }

    /// <summary>
    /// Проверяет, что метод <see cref="TradingSchedule.Describe"/> формирует сообщение о закрытой торговой сессии с указанием времени последних данных, если текущее московское время находится вне торгового окна для заданного класса инструмента.
    /// </summary>
    [Fact]
    public void Describe_OffSession_SaysClosed()
    {
        var text = TradingSchedule.Describe(AssetClass.Bond, At(Monday, 18, 40), At(Monday, 20, 0));
        Assert.Contains("Вне сессии", text);
        Assert.Contains("18:40:00", text);
    }
}
