using NexusOptimizer.Core.Configuration;

namespace NexusOptimizer.Tests;

public sealed class ConfigStoreTests
{
    [Fact]
    public void DisabledStartupEntry_RoundTrips_WithoutLosingCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ConfigStore(root);
            var expected = new DisabledStartupEntry
            {
                RegistryView = "Registry64",
                KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
                Name = "Example App",
                Command = "\"C:\\Program Files\\Example\\app.exe\" --background",
                ValueKind = "ExpandString",
                DisabledAtUtc = new DateTime(2026, 8, 27, 1, 2, 3, DateTimeKind.Utc),
            };
            var config = AppConfig.Default;
            config.DisabledStartupEntries.Add(expected);

            store.Save(config);
            var loaded = store.Load();

            var actual = Assert.Single(loaded.DisabledStartupEntries);
            Assert.Equal(expected.RegistryView, actual.RegistryView);
            Assert.Equal(expected.KeyPath, actual.KeyPath);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Command, actual.Command);
            Assert.Equal(expected.ValueKind, actual.ValueKind);
            Assert.Equal(expected.DisabledAtUtc, actual.DisabledAtUtc);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
