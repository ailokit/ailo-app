using Ailo.Jobs;
using Ailo.ViewModels;

namespace Ailo.Tests;

public sealed class ScheduledJobItemTests
{
    [Fact]
    public void ParametersJson_DisplaysUnicodeAndEmojiCharacters()
    {
        var now = DateTimeOffset.UtcNow;
        var item = new ScheduledJobItem(new CronJob(
            1,
            "native_notification",
            "0 9 * * *",
            "{\"Title\":\"Hydration reminder \\uD83D\\uDCA7\",\"Body\":\"Keep going \\uD83D\\uDC95\"}",
            true,
            null,
            now.AddHours(1),
            now,
            now));

        Assert.Contains("Hydration reminder 💧", item.ParametersJson);
        Assert.Contains("Keep going 💕", item.ParametersJson);
        Assert.DoesNotContain("\\uD83D", item.ParametersJson);
    }
}
