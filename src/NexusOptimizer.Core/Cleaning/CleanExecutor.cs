using System.IO;
using NexusOptimizer.Core.Safety;
using NexusOptimizer.Core.Security;

namespace NexusOptimizer.Core.Cleaning;

/// <summary>
/// Esegue la pulizia PREVISTA da un <see cref="ScanResult"/>. Ogni elemento viene
/// ri-validato con <see cref="PathGuard.ValidateForDelete"/> immediatamente prima
/// della rimozione (contromisura TOCTOU). Mai reparse point, mai fuori perimetro.
/// Dry Run e cestino opzionali per lentirezza controllata.
/// </summary>
public sealed class CleanExecutor
{
    private readonly PathGuard _guard;
    private readonly SafetyEngine? _safety;

    public CleanExecutor(IEnumerable<string>? exclusions = null, SafetyEngine? safety = null)
    {
        _guard = new PathGuard(exclusions);
        _safety = safety;
    }

    public sealed record CleanProgress(int Processed, int Skipped, long BytesFreed);

    /// <summary>
    /// Filtra gli elementi di un risultato di scansione: restituisce quelli che
    /// superano la validazione per la cancellazione nella relativa categoria.
    /// (Dry-run in <see cref="ScanResult"/>, mai qui).
    /// </summary>
    public Task<CleanResult> RunAsync(
        ScanResult scan,
        CleanOptions options,
        IProgress<CleanProgress>? progress,
        CancellationToken ct)
    {
        // DPAPI dipende dal profilo del chiamante: la chiave viene aperta prima
        // di entrare nel worker thread, poi resta solo in memoria del processo.
        if (!options.DryRun && options.UseQuarantine) _safety?.EnsureReady();
        return Task.Run(() => RunCore(scan, options, progress, ct), ct);
    }

    private CleanResult RunCore(
        ScanResult scan,
        CleanOptions options,
        IProgress<CleanProgress>? progress,
        CancellationToken ct)
    {
        if (!options.DryRun && options.UseQuarantine && _safety is null)
            throw new InvalidOperationException("Quarantena richiesta ma Safety Engine non disponibile.");

        var result = new CleanResult { WasDryRun = options.DryRun };
        int processed = 0, skipped = 0;
        long bytes = 0;
        SafetyOperationRecord? operation = null;
        var hadErrors = false;

        if (!options.DryRun && options.UseQuarantine)
            operation = _safety!.BeginOperation(scan.Categories.Select(category => category.Category.Id));

        try
        {
            foreach (var categoryResult in scan.Categories)
            {
                ct.ThrowIfCancellationRequested();
                var cat = categoryResult.Category;
                var roots = cat.Roots;

                // Il Cestino Windows non espone i file originali in modo sicuro
                // all'app: non può quindi essere incluso in un undo cifrato.
                if (cat.Id == RecycleBin)
                {
                    if (categoryResult.Items.Count == 0) continue;
                    if (options.DryRun)
                    {
                        result.ItemsRemoved += categoryResult.Items.Count;
                        result.BytesFreed += categoryResult.TotalBytes;
                    }
                    else if (options.UseQuarantine)
                    {
                        result.ItemsSkipped += categoryResult.Items.Count;
                        skipped += categoryResult.Items.Count;
                        hadErrors = true;
                        result.ErrorMessages.Add("Cestino ignorato: non è ripristinabile dalla quarantena cifrata.");
                    }
                    else if (RecycleBinHelper.Empty())
                    {
                        result.ItemsRemoved += categoryResult.Items.Count;
                        result.BytesFreed += categoryResult.TotalBytes;
                    }
                    else
                    {
                        result.ItemsSkipped += categoryResult.Items.Count;
                        hadErrors = true;
                        result.ErrorMessages.Add("Cestino: svuotamento negato dal sistema.");
                    }
                    continue;
                }

                foreach (var item in categoryResult.Items)
                {
                    ct.ThrowIfCancellationRequested();
                    processed++;

                    // Veto su qualsiasi directory che rappresenti un reparse point.
                    if (IsReparsePoint(item.FullPath))
                    {
                        skipped++;
                        result.ItemsSkipped++;
                        hadErrors = true;
                        result.ErrorMessages.Add("Elemento ignorato: reparse point o attributi non verificabili.");
                        continue;
                    }

                    // Ri-validazione user-exclusions + perimetro categoria + cartelle protette.
                    try
                    {
                        _guard.ValidateForDelete(item.FullPath, roots);
                    }
                    catch (PathGuardException)
                    {
                        skipped++;
                        result.ItemsSkipped++;
                        hadErrors = true;
                        result.ErrorMessages.Add("Elemento ignorato: non rispetta la policy dei percorsi protetti.");
                        continue;
                    }

                    if (options.DryRun)
                    {
                        bytes += item.SizeBytes;
                        result.BytesFreed += item.SizeBytes;
                        result.ItemsRemoved++;
                        progress?.Report(new CleanProgress(processed, skipped, bytes));
                        continue;
                    }

                    QuarantineCapture? capture = null;
                    try
                    {
                        if (operation is not null)
                        {
                            capture = _safety!.Capture(operation.Id, cat.Id, item, ct);
                            // Registra il backup prima della delete: dopo un crash il
                            // file è ancora disponibile per l'undo, mai il contrario.
                            _safety.RecordCapture(operation.Id, capture);
                        }

                        // TOCTOU: seconda validazione subito prima dell'operazione mutante.
                        _guard.ValidateForDelete(item.FullPath, roots);
                        if (IsReparsePoint(item.FullPath)) throw new PathGuardException("Reparse point rilevato prima della rimozione.");

                        // Con quarantena il file è già salvato cifrato: la delete
                        // fisica evita di dipendere dalla retention del Cestino Windows.
                        var removed = DeletePathSafe(item.FullPath,
                            useRecycleBin: operation is null && options.UseRecycleBin);
                        if (!removed) throw new IOException("Rimozione negata dal sistema.");

                        bytes += item.SizeBytes;
                        result.BytesFreed += item.SizeBytes;
                        result.ItemsRemoved++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        if (capture is not null && operation is not null)
                            _safety!.DiscardCapture(operation.Id, capture);
                        skipped++;
                        result.ItemsSkipped++;
                        hadErrors = true;
                        result.ErrorMessages.Add($"Elemento ignorato: {ex.GetType().Name} durante quarantena o rimozione.");
                    }
                    finally
                    {
                        progress?.Report(new CleanProgress(processed, skipped, bytes));
                    }
                }
            }
        }
        finally
        {
            if (operation is not null)
                _safety!.CompleteOperation(operation.Id, hadErrors);
        }

        ct.ThrowIfCancellationRequested();
        return result;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var a = File.GetAttributes(path);
            return (a & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception) { return true; } // non determinabile: trattato come non sicuro
    }

    private static bool DeletePathSafe(string path, bool useRecycleBin)
    {
        if (useRecycleBin)
        {
            if (File.Exists(path) || Directory.Exists(path))
                return RecycleBinHelper.SendToRecycleBin(path);
            return false;
        }

        try
        {
            if (File.Exists(path)) { File.Delete(path); return true; }
            if (Directory.Exists(path)) { Directory.Delete(path, recursive: false); return true; }
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
        return false;
    }

    private const string RecycleBin = "recycle_bin";
}
