using Ailo.AI.Tools;
using System.Runtime.InteropServices;

namespace Ailo.Tests;

public sealed class SystemInformationToolTests
{
    private readonly SystemInformationTool _tool = new();

    [Fact]
    public async Task GetsCurrentTimeWithTimeZone()
    {
        var result = await _tool.GetSystemInformationAsync(SystemInformationType.CurrentTime);

        Assert.Contains("Local time", result);
        Assert.Contains(TimeZoneInfo.Local.Id, result);
    }

    [Fact]
    public async Task GetsOperatingSystemDetails()
    {
        var result = await _tool.GetSystemInformationAsync(SystemInformationType.OperatingSystem);

        Assert.Contains(RuntimeInformation.OSDescription.Trim(), result);
        Assert.Contains(RuntimeInformation.OSArchitecture.ToString(), result);
    }

    [Fact]
    public async Task GetsCurrentSystemUser()
    {
        var result = await _tool.GetSystemInformationAsync(SystemInformationType.CurrentUser);

        Assert.Contains(Environment.UserName, result);
    }
}
