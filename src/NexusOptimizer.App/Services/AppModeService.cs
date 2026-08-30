using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;

namespace NexusOptimizer.App.Services;

/// <summary>
/// Livello operativo condiviso da titlebar, sidebar e Modalità Gaming.
/// Il valore è persistito in config.json e non cambia mai da solo.
/// </summary>
public sealed class AppModeService
{
    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly FileLogService _log;

    public AppModeService(AppConfig config, ConfigStore store, FileLogService log)
    {
        _config = config;
        _store = store;
        _log = log;
        Level = AppModeLevels.Parse(config.Mode);
    }

    /// <summary>Raised dopo un cambio livello: le viste rileggono etichette e default.</summary>
    public event Action? Changed;

    public AppModeLevel Level { get; private set; }

    public string DisplayName => Level.ToDisplayName();

    public void Set(AppModeLevel level)
    {
        if (Level == level) return;
        Level = level;
        _config.Mode = level.ToId();
        try { _store.Save(_config); }
        catch (Exception ex) { _log.Error("Salvataggio livello modalità fallito", ex); }
        _log.Info($"Livello modalità impostato su {level.ToDisplayName()}");
        Changed?.Invoke();
    }
}
