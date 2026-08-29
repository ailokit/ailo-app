using Cronos;

namespace Ailo.Tests;

public sealed class CronExpressionTests
{
    [Fact]
    public void ParseAndFindsNextWeekdayOccurrence()
    {
        var expression = CronExpression.Parse("30 9 * * 1-5", CronFormat.Standard);
        var after = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero); // Friday

        var next = expression.GetNextOccurrence(after, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 8, 31, 9, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void SupportsListsStepsNamesAndSundaySeven()
    {
        var expression = CronExpression.Parse("*/15 8,10 * JAN,MAR MON,SUN", CronFormat.Standard);
        var after = new DateTimeOffset(2027, 1, 3, 8, 0, 0, TimeSpan.Zero); // Sunday

        var next = expression.GetNextOccurrence(after, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2027, 1, 3, 8, 15, 0, TimeSpan.Zero), next);
    }

    [Theory]
    [InlineData("0 9 * *")]
    [InlineData("60 9 * * *")]
    [InlineData("0 9 * * MONDAY")]
    [InlineData("0 9 32 * *")]
    [InlineData("0 9 * * 1/0")]
    public void RejectsInvalidExpressions(string expression)
    {
        Assert.Throws<CronFormatException>(() => CronExpression.Parse(expression, CronFormat.Standard));
    }

    [Fact]
    public void ReturnsOccurrenceInTheProvidedTimeZone()
    {
        var expression = CronExpression.Parse("0 9 * * *", CronFormat.Standard);
        var after = new DateTimeOffset(2026, 6, 1, 8, 30, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(after, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), next);
    }
}
