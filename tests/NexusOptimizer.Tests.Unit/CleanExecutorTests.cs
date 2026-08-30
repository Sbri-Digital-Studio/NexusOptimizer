using NexusOptimizer.Core.Cleaning;

namespace NexusOptimizer.Tests;

public sealed class CleanExecutorTests
{
    [Fact]
    [Trait("Category", "DeletionSafety")]
    public async Task DryRun_ReportsItems_WithoutChangingFiles()
    {
        var root = CreateTempRoot();
        var file = Path.Combine(root, "cache.tmp");
        await File.WriteAllTextAsync(file, "cache");
        try
        {
            var scan = Scan(root, file);
            var result = await new CleanExecutor().RunAsync(scan,
                new CleanOptions { DryRun = true, UseRecycleBin = false }, null, CancellationToken.None);

            Assert.True(File.Exists(file));
            Assert.Equal(1, result.ItemsRemoved);
            Assert.Equal(5, result.BytesFreed);
            Assert.True(result.WasDryRun);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public async Task PermanentDelete_Stays_Inside_AllowedRoot()
    {
        var root = CreateTempRoot();
        var file = Path.Combine(root, "cache.tmp");
        await File.WriteAllTextAsync(file, "cache");
        try
        {
            var result = await new CleanExecutor().RunAsync(Scan(root, file),
                new CleanOptions { DryRun = false, UseRecycleBin = false }, null, CancellationToken.None);

            Assert.False(File.Exists(file));
            Assert.Equal(1, result.ItemsRemoved);
            Assert.Empty(result.ErrorMessages);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public async Task MixedScan_DeletesSafeItem_ButPreservesOutsideItem()
    {
        var root = CreateTempRoot();
        var outsideRoot = CreateTempRoot();
        var safeFile = Path.Combine(root, "safe.tmp");
        var outsideFile = Path.Combine(outsideRoot, "keep.tmp");
        await File.WriteAllTextAsync(safeFile, "safe");
        await File.WriteAllTextAsync(outsideFile, "keep");
        try
        {
            var category = new CleanCategoryDef("test", "test", SecurityLevel.Green, true, false, [root]);
            var entry = new CategoryScanResult { Category = category };
            entry.Items.Add(new CleanItem(safeFile, 4, false));
            entry.Items.Add(new CleanItem(outsideFile, 4, false));
            var scan = new ScanResult();
            scan.Categories.Add(entry);

            var result = await new CleanExecutor().RunAsync(scan,
                new CleanOptions { DryRun = false, UseRecycleBin = false }, null, CancellationToken.None);

            Assert.False(File.Exists(safeFile));
            Assert.True(File.Exists(outsideFile));
            Assert.Equal(1, result.ItemsRemoved);
            Assert.Equal(1, result.ItemsSkipped);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(outsideRoot)) Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public async Task Scan_Cannot_Delete_Category_Root()
    {
        var root = CreateTempRoot();
        try
        {
            var category = new CleanCategoryDef("test", "test", SecurityLevel.Green, true, false, [root]);
            var entry = new CategoryScanResult { Category = category };
            entry.Items.Add(new CleanItem(root, 0, true));
            var scan = new ScanResult();
            scan.Categories.Add(entry);

            var result = await new CleanExecutor().RunAsync(scan,
                new CleanOptions { DryRun = false, UseRecycleBin = false }, null, CancellationToken.None);

            Assert.True(Directory.Exists(root));
            Assert.Equal(0, result.ItemsRemoved);
            Assert.Equal(1, result.ItemsSkipped);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static ScanResult Scan(string root, string file)
    {
        var category = new CleanCategoryDef("test", "test", SecurityLevel.Green, true, false, [root]);
        var entry = new CategoryScanResult { Category = category };
        entry.Items.Add(new CleanItem(file, 5, false));
        entry.TotalBytes = 5;
        var result = new ScanResult();
        result.Categories.Add(entry);
        return result;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexusoptimizer-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
