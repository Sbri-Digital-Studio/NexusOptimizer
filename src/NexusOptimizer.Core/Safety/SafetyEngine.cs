using NexusOptimizer.Core.Cleaning;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Security;

namespace NexusOptimizer.Core.Safety;

/// <summary>
/// Coordina quarantena cifrata, undo e cronologia. Il motore fallisce chiuso:
/// se la copia sicura non riesce, CleanExecutor non elimina il file originale.
/// </summary>
public sealed class SafetyEngine
{
    public const long DefaultQuotaBytes = 1024L * 1024 * 1024;

    private readonly EncryptedQuarantineStore _store;
    private readonly SafetyTransactionLog _log;
    private readonly long _quotaBytes;
    private readonly object _quotaSync = new();
    private readonly PathGuard _restoreGuard = new();

    public SafetyEngine(string? dataDirectory = null, long quotaBytes = DefaultQuotaBytes)
        : this(dataDirectory, quotaBytes, new DpapiCurrentUserKeyProtector())
    {
    }

    internal SafetyEngine(string? dataDirectory, long quotaBytes, IQuarantineKeyProtector keyProtector)
    {
        dataDirectory ??= Path.Combine(ConfigStore.AppDataDirectory, "safety");
        _store = new EncryptedQuarantineStore(Path.Combine(dataDirectory, "quarantine"), keyProtector);
        _log = new SafetyTransactionLog(Path.Combine(dataDirectory, "history.json"));
        _quotaBytes = Math.Max(64L * 1024 * 1024, quotaBytes);
    }

    public IReadOnlyList<SafetyOperationRecord> GetHistory() => _log.GetAll();

    public SafetyOperationRecord BeginOperation(IEnumerable<string> categoryIds)
        => _log.Begin(categoryIds);

    /// <summary>Carica la chiave DPAPI nel thread chiamante prima del lavoro in background.</summary>
    public void EnsureReady() => _store.EnsureKeyAvailable();

    internal QuarantineCapture Capture(Guid operationId, string categoryId, CleanItem item, CancellationToken ct)
    {
        if (!EnsureCapacity(Math.Max(item.SizeBytes, 1) + 2L * 1024 * 1024, operationId))
            throw new IOException("La quarantena ha raggiunto la quota configurata.");
        return _store.Capture(operationId, categoryId, item.FullPath, item.IsDirectory, ct);
    }

    internal void RecordCapture(Guid operationId, QuarantineCapture capture)
        => _log.RecordCapture(operationId, capture.StoredBytes);

    internal void DiscardCapture(Guid operationId, QuarantineCapture capture)
    {
        _store.DeleteItem(operationId, capture.ItemId);
        _log.DiscardCapture(operationId, capture.StoredBytes);
    }

    public void CompleteOperation(Guid operationId, bool hadErrors)
        => _log.Complete(operationId, hadErrors);

    public Task<RestoreResult> RestoreAsync(Guid operationId, CancellationToken ct = default)
        => Task.Run(() => RestoreCore(operationId, ct), ct);

    private RestoreResult RestoreCore(Guid operationId, CancellationToken ct)
    {
        var result = new RestoreResult();
        var itemIds = _store.ListItemIds(operationId);
        foreach (var itemId in itemIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var metadata = _store.ReadMetadata(operationId, itemId);
                var category = CleanCatalog.GetById(metadata.CategoryId);
                if (category is null || !CanRestoreToOriginalPath(metadata, category))
                {
                    result.SkippedItems++;
                    continue;
                }

                if (metadata.IsDirectory)
                {
                    Directory.CreateDirectory(metadata.OriginalPath);
                }
                else
                {
                    var parent = Path.GetDirectoryName(metadata.OriginalPath);
                    if (string.IsNullOrWhiteSpace(parent))
                    {
                        result.SkippedItems++;
                        continue;
                    }
                    Directory.CreateDirectory(parent);
                    _store.RestoreFile(operationId, itemId, metadata.OriginalPath, ct);
                }
                _store.DeleteItem(operationId, itemId);
                result.RestoredItems++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // Non registriamo path o dettagli potenzialmente privati nella cronologia.
                result.SkippedItems++;
                result.Errors.Add("Un elemento non può essere ripristinato in sicurezza.");
            }
        }

        var fullyRestored = _store.ListItemIds(operationId).Count == 0;
        _log.MarkRestored(operationId, result.RestoredItems, fullyRestored);
        return result;
    }

    private bool CanRestoreToOriginalPath(QuarantineItemMetadata metadata, CleanCategoryDef category)
    {
        try
        {
            var destination = Path.GetFullPath(metadata.OriginalPath);
            // Riutilizziamo le stesse policy di perimetro usate in cancellazione:
            // il backup non può essere trasformato in una scrittura su un percorso critico.
            _restoreGuard.ValidateForDelete(destination, category.Roots);
            if (File.Exists(destination) || Directory.Exists(destination)) return false; // mai sovrascrivere
            return !HasReparsePointInExistingParents(destination, category.Roots);
        }
        catch (PathGuardException) { return false; }
        catch (Exception) { return false; }
    }

    private static bool HasReparsePointInExistingParents(string destination, IReadOnlyList<string> roots)
    {
        var root = roots.Select(TryFullPath)
            .FirstOrDefault(candidate => candidate is not null && ProtectedPaths.IsUnder(destination, candidate));
        if (root is null) return true;

        var current = Path.GetDirectoryName(destination);
        while (!string.IsNullOrWhiteSpace(current) && ProtectedPaths.IsUnder(current, root))
        {
            if (Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return true;
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current);
        }
        return false;
    }

    private bool EnsureCapacity(long expectedBytes, Guid activeOperationId)
    {
        lock (_quotaSync)
        {
            if (_store.GetUsedBytes() + expectedBytes <= _quotaBytes) return true;
            var candidates = _log.GetAll()
                .Where(record => record.Id != activeOperationId
                    && record.Status is not SafetyOperationStatus.InProgress
                    && record.Status is not SafetyOperationStatus.Undone
                    && record.Status is not SafetyOperationStatus.Expired)
                .OrderBy(record => record.CompletedUtc ?? record.StartedUtc);
            foreach (var candidate in candidates)
            {
                _store.DeleteOperation(candidate.Id);
                _log.MarkExpired(candidate.Id);
                if (_store.GetUsedBytes() + expectedBytes <= _quotaBytes) return true;
            }
            return false;
        }
    }

    private static string? TryFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception) { return null; }
    }
}
