using NexusOptimizer.Core.Cleaning;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Safety;

namespace NexusOptimizer.Tests.Integration;

public sealed class SafetyEngineTests
{
    [Fact]
    [Trait("Category", "DeletionSafety")]
    public async Task Quarantine_Encrypts_Records_And_Restores_File()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "nexusoptimizer-safety-source-" + Guid.NewGuid().ToString("N"));
        var safetyRoot = Path.Combine(Path.GetTempPath(), "nexusoptimizer-safety-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceRoot);
        var file = Path.Combine(sourceRoot, "recover-me.tmp");
        await File.WriteAllTextAsync(file, "contenuto-da-ripristinare");

        try
        {
            var engine = new SafetyEngine(safetyRoot, SafetyEngine.DefaultQuotaBytes, new TestKeyProtector());
            var result = await new CleanExecutor(safety: engine).RunAsync(
                Scan(file),
                new CleanOptions { DryRun = false, UseRecycleBin = false, UseQuarantine = true },
                progress: null, CancellationToken.None);

            Assert.False(File.Exists(file), string.Join(" | ", result.ErrorMessages));
            Assert.Equal(1, result.ItemsRemoved);
            var operation = Assert.Single(engine.GetHistory());
            Assert.Equal(SafetyOperationStatus.Completed, operation.Status);
            Assert.Equal(1, operation.ItemsQuarantined);

            // Il registro rimane utile per la cronologia ma non può divulgare il percorso.
            var history = await File.ReadAllTextAsync(Path.Combine(safetyRoot, "history.json"));
            Assert.DoesNotContain(file, history, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recover-me.tmp", history, StringComparison.OrdinalIgnoreCase);

            var restore = await engine.RestoreAsync(operation.Id);
            Assert.Equal(1, restore.RestoredItems);
            Assert.True(File.Exists(file));
            Assert.Equal("contenuto-da-ripristinare", await File.ReadAllTextAsync(file));
            Assert.Equal(SafetyOperationStatus.Undone, Assert.Single(engine.GetHistory()).Status);
        }
        finally
        {
            if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, recursive: true);
            if (Directory.Exists(safetyRoot)) Directory.Delete(safetyRoot, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public void AutoSafeClean_Admits_Only_Restorable_Green_Categories()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexusoptimizer-auto-policy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new AppConfig { AutoCleanTempFiles = true };
            var service = new AutoSafeCleanService(config, new ConfigStore(root),
                new SafetyEngine(root, SafetyEngine.DefaultQuotaBytes, new TestKeyProtector()));

            Assert.NotEmpty(service.CertifiedCategories);
            Assert.All(service.CertifiedCategories, category =>
            {
                Assert.Equal(SecurityLevel.Green, category.Level);
                Assert.False(category.RequiresAdmin);
                Assert.NotEqual("recycle_bin", category.Id);
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public async Task Restore_Never_Overwrites_A_File_Created_After_Cleanup()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "nexusoptimizer-overwrite-source-" + Guid.NewGuid().ToString("N"));
        var safetyRoot = Path.Combine(Path.GetTempPath(), "nexusoptimizer-overwrite-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceRoot);
        var file = Path.Combine(sourceRoot, "cache.tmp");
        await File.WriteAllTextAsync(file, "old-cache");
        try
        {
            var engine = new SafetyEngine(safetyRoot, SafetyEngine.DefaultQuotaBytes, new TestKeyProtector());
            var clean = await new CleanExecutor(safety: engine).RunAsync(
                Scan(file), new CleanOptions { DryRun = false, UseRecycleBin = false, UseQuarantine = true }, null,
                CancellationToken.None);
            Assert.Equal(1, clean.ItemsRemoved);

            await File.WriteAllTextAsync(file, "new-user-content");
            var restore = await engine.RestoreAsync(Assert.Single(engine.GetHistory()).Id);

            Assert.Equal(0, restore.RestoredItems);
            Assert.Equal(1, restore.SkippedItems);
            Assert.Equal("new-user-content", await File.ReadAllTextAsync(file));
        }
        finally
        {
            if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, recursive: true);
            if (Directory.Exists(safetyRoot)) Directory.Delete(safetyRoot, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "DeletionSafety")]
    public async Task Restore_FailsClosed_When_Encrypted_Content_Is_Tampered()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "nexusoptimizer-tamper-source-" + Guid.NewGuid().ToString("N"));
        var safetyRoot = Path.Combine(Path.GetTempPath(), "nexusoptimizer-tamper-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceRoot);
        var file = Path.Combine(sourceRoot, "cache.tmp");
        await File.WriteAllTextAsync(file, "authentic-content");
        try
        {
            var engine = new SafetyEngine(safetyRoot, SafetyEngine.DefaultQuotaBytes, new TestKeyProtector());
            var clean = await new CleanExecutor(safety: engine).RunAsync(
                Scan(file), new CleanOptions { DryRun = false, UseRecycleBin = false, UseQuarantine = true }, null,
                CancellationToken.None);
            Assert.Equal(1, clean.ItemsRemoved);

            var encrypted = Assert.Single(Directory.EnumerateFiles(safetyRoot, "*.data", SearchOption.AllDirectories));
            var bytes = await File.ReadAllBytesAsync(encrypted);
            bytes[^1] ^= 0x5A;
            await File.WriteAllBytesAsync(encrypted, bytes);

            var restore = await engine.RestoreAsync(Assert.Single(engine.GetHistory()).Id);

            Assert.Equal(0, restore.RestoredItems);
            Assert.Equal(1, restore.SkippedItems);
            Assert.False(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, recursive: true);
            if (Directory.Exists(safetyRoot)) Directory.Delete(safetyRoot, recursive: true);
        }
    }

    private static ScanResult Scan(string file)
    {
        // user_temp è l'unica categoria GREEN disponibile di default e la root
        // di test è sotto %TEMP%, quindi l'undo passa le medesime policy di produzione.
        var category = new CleanCategoryDef("user_temp", "test", SecurityLevel.Green, true, false,
            [Path.GetTempPath()]);
        var entry = new CategoryScanResult { Category = category };
        entry.Items.Add(new CleanItem(file, new FileInfo(file).Length, IsDirectory: false));
        entry.TotalBytes = entry.Items[0].SizeBytes;
        var scan = new ScanResult();
        scan.Categories.Add(entry);
        return scan;
    }

    private sealed class TestKeyProtector : IQuarantineKeyProtector
    {
        public byte[] Protect(byte[] plaintextKey) => plaintextKey.ToArray();
        public byte[] Unprotect(byte[] protectedKey) => protectedKey.ToArray();
    }
}
