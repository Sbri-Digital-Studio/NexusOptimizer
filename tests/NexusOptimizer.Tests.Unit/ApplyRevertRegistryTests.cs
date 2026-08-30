using Microsoft.Win32;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;
using NexusOptimizer.Core.Safety;

namespace NexusOptimizer.Tests;

/// <summary>
/// Apply/Revert delle preferenze utente su una chiave di prova creata sotto
/// HKCU\Software\NexusOptimizer\TestSandbox ed eliminata al termine: nessuna
/// impostazione reale di Windows viene toccata dalla suite, ma il meccanismo
/// verificato e' esattamente quello usato dall'Optimizer e dalla Modalita' Gaming.
/// </summary>
public sealed class ApplyRevertRegistryTests : IDisposable
{
    private const string SandboxRoot = @"Software\NexusOptimizer\TestSandbox";

    private readonly string _subKey = SandboxRoot + "\\" + Guid.NewGuid().ToString("N");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nexus-apply-" + Guid.NewGuid().ToString("N"));
    private readonly AppConfig _config = AppConfig.Default;
    private ConfigStore _store = null!;
    private FileLogService _log = null!;

    private OptimizerEngine CreateEngine()
    {
        Directory.CreateDirectory(_root);
        _store = new ConfigStore(_root);
        _log = new FileLogService(Path.Combine(_root, "logs"));
        var startup = new StartupService(_config, _store, _log);
        var safety = new SafetyEngine(Path.Combine(_root, "safety"));
        var mode = new AppModeService(_config, _store, _log);
        return new OptimizerEngine(_config, _store, _log, startup, safety, mode);
    }

    private object? ReadRaw(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey);
        return key?.GetValue(name);
    }

    private static void WriteRaw(string subKey, string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        key!.SetValue(name, value, RegistryValueKind.DWord);
    }

    // ------------------------------------------------------------- Optimizer

    [Fact]
    public void Apply_WritesValue_AndRemembersThatItDidNotExist()
    {
        var engine = CreateEngine();

        Assert.True(engine.WriteUserValue("test.action", _subKey, "Flag", 1, RegistryValueKind.DWord));

        Assert.Equal(1, ReadRaw("Flag"));
        var state = Assert.Single(_config.OptimizerState);
        Assert.Equal("test.action", state.ActionId);
        Assert.Null(state.PreviousValue); // la voce non esisteva prima
    }

    [Fact]
    public void Revert_RemovesValueThatDidNotExistBefore()
    {
        var engine = CreateEngine();
        engine.WriteUserValue("test.action", _subKey, "Flag", 1, RegistryValueKind.DWord);

        var restored = engine.RestoreUserValues("test.action");

        Assert.Equal(1, restored);
        Assert.Null(ReadRaw("Flag"));
        Assert.Empty(_config.OptimizerState);
    }

    [Fact]
    public void Revert_PutsBackThePreviousValue_WithItsOriginalKind()
    {
        WriteRaw(_subKey, "Flag", 3);
        var engine = CreateEngine();
        engine.WriteUserValue("test.action", _subKey, "Flag", 0, RegistryValueKind.DWord);
        Assert.Equal(0, ReadRaw("Flag"));

        engine.RestoreUserValues("test.action");

        Assert.Equal(3, ReadRaw("Flag"));
        using var key = Registry.CurrentUser.OpenSubKey(_subKey);
        Assert.Equal(RegistryValueKind.DWord, key!.GetValueKind("Flag"));
    }

    [Fact]
    public void Apply_Twice_KeepsTheOriginalValueAsRestorePoint()
    {
        WriteRaw(_subKey, "Flag", 7);
        var engine = CreateEngine();

        engine.WriteUserValue("test.action", _subKey, "Flag", 1, RegistryValueKind.DWord);
        engine.WriteUserValue("test.action", _subKey, "Flag", 2, RegistryValueKind.DWord);

        Assert.Equal("7", Assert.Single(_config.OptimizerState).PreviousValue);
        engine.RestoreUserValues("test.action");
        Assert.Equal(7, ReadRaw("Flag"));
    }

    [Fact]
    public void Revert_WithoutApply_ChangesNothing()
    {
        WriteRaw(_subKey, "Flag", 5);
        var engine = CreateEngine();

        Assert.Equal(0, engine.RestoreUserValues("test.action"));
        Assert.Equal(5, ReadRaw("Flag"));
    }

    [Fact]
    public void RestoreState_SurvivesAnApplicationRestart()
    {
        // L'annullamento deve funzionare anche in una sessione nuova: lo stato
        // vive in config.json, non nella memoria del processo.
        var engine = CreateEngine();
        engine.WriteUserValue("test.action", _subKey, "Flag", 1, RegistryValueKind.DWord);

        var reloaded = _store.Load();
        Assert.Single(reloaded.OptimizerState);
        var log = new FileLogService(Path.Combine(_root, "logs"));
        var restarted = new OptimizerEngine(reloaded, _store, log,
            new StartupService(reloaded, _store, log),
            new SafetyEngine(Path.Combine(_root, "safety2")),
            new AppModeService(reloaded, _store, log));

        Assert.Equal(1, restarted.RestoreUserValues("test.action"));
        Assert.Null(ReadRaw("Flag"));
    }

    // --------------------------------------------------------- Modalita' Gaming

    [Fact]
    public void GamingBoost_RestoresAbsentValue()
    {
        var gaming = new GamingModeService(new FileLogService(Path.Combine(_root, "logs")));
        object? previous = null;

        Assert.True(gaming.WriteUserDword(_subKey, "GameDVR_Enabled", 0, ref previous));
        Assert.Equal(0, ReadRaw("GameDVR_Enabled"));

        gaming.RestoreValue(Registry.CurrentUser, _subKey, "GameDVR_Enabled", ref previous);

        Assert.Null(ReadRaw("GameDVR_Enabled"));
        Assert.Null(previous); // il ripristino consuma la memoria del valore
    }

    [Fact]
    public void GamingBoost_RestoresPreviousValue()
    {
        WriteRaw(_subKey, "AutoGameModeEnabled", 1);
        var gaming = new GamingModeService(new FileLogService(Path.Combine(_root, "logs")));
        object? previous = null;

        gaming.WriteUserDword(_subKey, "AutoGameModeEnabled", 0, ref previous);
        Assert.Equal(0, ReadRaw("AutoGameModeEnabled"));

        gaming.RestoreValue(Registry.CurrentUser, _subKey, "AutoGameModeEnabled", ref previous);

        Assert.Equal(1, ReadRaw("AutoGameModeEnabled"));
    }

    [Fact]
    public void GamingBoost_AppliedTwice_KeepsTheOriginalRestorePoint()
    {
        WriteRaw(_subKey, "GameDVR_Enabled", 1);
        var gaming = new GamingModeService(new FileLogService(Path.Combine(_root, "logs")));
        object? previous = null;

        gaming.WriteUserDword(_subKey, "GameDVR_Enabled", 0, ref previous);
        gaming.WriteUserDword(_subKey, "GameDVR_Enabled", 0, ref previous);
        gaming.RestoreValue(Registry.CurrentUser, _subKey, "GameDVR_Enabled", ref previous);

        Assert.Equal(1, ReadRaw("GameDVR_Enabled"));
    }

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_subKey, throwOnMissingSubKey: false); }
        catch (Exception) { /* la chiave di prova puo' essere gia' sparita */ }
        try
        {
            // La radice sandbox resta solo se contiene ancora prove di altri test.
            using var root = Registry.CurrentUser.OpenSubKey(SandboxRoot);
            if (root is not null && root.SubKeyCount == 0 && root.ValueCount == 0)
                Registry.CurrentUser.DeleteSubKey(SandboxRoot, throwOnMissingSubKey: false);
        }
        catch (Exception) { }
        _log?.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* file di log ancora aperto: la cartella e' in %TEMP% */ }
    }
}
