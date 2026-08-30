using NexusOptimizer.App.Services;
using NexusOptimizer.App.ViewModels;
using NexusOptimizer.Core.Logging;

namespace NexusOptimizer.Tests;

public sealed class MaintenanceActionsTests
{
    [Fact]
    public void DiskCleanupCommand_RequestsProtectedSmartCleanPreview()
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), "NexusOptimizerTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var log = new FileLogService(logDirectory);
            var viewModel = new DiskManagerViewModel(log);
            var requested = false;
            viewModel.CleanRequested += () => requested = true;

            viewModel.CleanCommand.Execute(null);

            Assert.True(requested);
            Assert.Contains("Smart Clean", viewModel.Status, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RamAndVramCleanup_UseTheSharedMemoryService()
    {
        using var monitor = new SystemMonitor(intervalMs: 60_000);
        var memory = new StubMemoryOptimizationService();
        using var viewModel = new RamManagerViewModel(monitor, memory);

        viewModel.OptimizeCommand.Execute(null);
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (viewModel.IsOptimizing && DateTime.UtcNow < timeout)
            await Task.Delay(20);

        Assert.False(viewModel.IsOptimizing);
        Assert.Equal(1, memory.RamCalls);
        Assert.Equal(1, memory.VramCalls);
        Assert.Contains("3", viewModel.RamActionStatus, StringComparison.Ordinal);
        Assert.NotEqual(Formatter.Dash, viewModel.LastRamReleasedText);
        Assert.NotEqual(Formatter.Dash, viewModel.LastVramActionText);
    }

    private sealed class StubMemoryOptimizationService : IMemoryOptimizationService
    {
        public int RamCalls { get; private set; }
        public int VramCalls { get; private set; }

        public long? AvailableMemoryBytes() => 8L * 1024 * 1024 * 1024;

        public RamOptimizationResult OptimizeRam()
        {
            RamCalls++;
            return new RamOptimizationResult(
                AvailableMemoryGainBytes: 64L * 1024 * 1024,
                TrimmedWorkingSetBytes: 60L * 1024 * 1024,
                NexusManagedBytesReleased: 4L * 1024 * 1024,
                TrimmedProcessCount: 3);
        }

        public VramOptimizationResult OptimizeVram()
        {
            VramCalls++;
            return new VramOptimizationResult(2L * 1024 * 1024, RenderQueueFlushed: true);
        }
    }
}
