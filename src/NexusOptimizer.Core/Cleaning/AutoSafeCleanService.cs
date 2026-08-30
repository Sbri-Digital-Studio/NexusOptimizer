using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Safety;

namespace NexusOptimizer.Core.Cleaning;

/// <summary>
/// Pulizia automatica opt-in. La policy vive nel Core: soltanto categorie GREEN,
/// non amministrative e ripristinabili dalla quarantena sono ammesse.
/// </summary>
public sealed class AutoSafeCleanService
{
    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly SafetyEngine _safety;

    public AutoSafeCleanService(AppConfig config, ConfigStore store, SafetyEngine safety)
    {
        _config = config;
        _store = store;
        _safety = safety;
    }

    public IReadOnlyList<CleanCategoryDef> CertifiedCategories => CleanCatalog.Categories
        .Where(category => category.Level == SecurityLevel.Green
            && !category.RequiresAdmin
            // Lo svuotamento del Cestino non offre un backup cifrato per file.
            && category.Id != "recycle_bin"
            && IsEnabledByUser(category))
        .ToArray();

    /// <summary>Anteprima non mutante delle sole categorie ammesse dalla policy.</summary>
    public Task<ScanResult> PreviewAsync(CancellationToken ct = default)
    {
        var scanner = new CleanScanner(_config.Exclusions);
        return scanner.ScanAsync(CertifiedCategories, progress: null, ct);
    }

    /// <summary>
    /// Esegue solo quando attivo e scaduto l'intervallo; la scansione è sempre il
    /// primo passo, così ogni esecuzione conserva una preview misurata nel report.
    /// </summary>
    public async Task<CleanResult?> RunIfDueAsync(CancellationToken ct = default)
    {
        if (!_config.AutoCleanEnabled || !IsDue()) return null;
        var preview = await PreviewAsync(ct);
        var executor = new CleanExecutor(_config.Exclusions, _safety);
        var result = await executor.RunAsync(preview, new CleanOptions
        {
            DryRun = false,
            UseRecycleBin = false,
            UseQuarantine = true,
            Exclusions = _config.Exclusions,
        }, progress: null, ct);

        _config.LastAutoCleanUtc = DateTime.UtcNow;
        _store.Save(_config);
        return result;
    }

    private bool IsDue()
    {
        var interval = Math.Clamp(_config.AutoCleanIntervalDays, 1, 90);
        return _config.LastAutoCleanUtc is null
            || _config.LastAutoCleanUtc.Value <= DateTime.UtcNow.AddDays(-interval);
    }

    private bool IsEnabledByUser(CleanCategoryDef category)
        => category.Id != "user_temp" || _config.AutoCleanTempFiles;
}
