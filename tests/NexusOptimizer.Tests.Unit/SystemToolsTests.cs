using NexusOptimizer.App.Services;

namespace NexusOptimizer.Tests;

public sealed class SystemToolsTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Example\\app.exe\" --background", @"C:\Program Files\Example\app.exe")]
    [InlineData(@"C:\Tools\agent.exe --silent", @"C:\Tools\agent.exe")]
    [InlineData(@"%LOCALAPPDATA%\Example\app.exe /minimized", "EXPANDED_LOCAL_APP")]
    public void StartupCommand_ExtractsExecutable(string command, string expected)
    {
        var actual = StartupService.ExtractExecutablePath(command);
        if (expected == "EXPANDED_LOCAL_APP")
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Example", "app.exe"), actual, ignoreCase: true);
        else
            Assert.Equal(expected, actual, ignoreCase: true);
    }

    [Fact]
    public void ProcessSnapshot_IncludesCurrentProcess()
    {
        var rows = new ProcessService().Collect();
        Assert.Contains(rows, row => row.Pid == Environment.ProcessId);
    }

    [Theory]
    [InlineData(1024d, "1,0 MB/s")]
    [InlineData(1024d * 1024d, "1,0 GB/s")]
    public void NetworkRate_UsesReadableUnits(double kilobytesPerSecond, string expected)
    {
        Assert.Equal(expected, Formatter.RatePerSec(kilobytesPerSecond));
    }
}
