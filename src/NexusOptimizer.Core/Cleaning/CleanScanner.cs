using System.IO;

namespace NexusOptimizer.Core.Cleaning;

/// <summary>
/// Motore di scansione: analizza prima di cancellare, con esclusioni,
/// attraversamento protetto (no reparse point esterni) e annullamento reale.
/// </summary>
public sealed class CleanScanner
{
    private readonly Security.PathGuard _guard;

    public CleanScanner(IEnumerable<string>? exclusions = null)
        => _guard = new Security.PathGuard(exclusions);

    public sealed record ScanProgress(string CurrentPath, int FilesFound, long BytesFound);

    public Task<ScanResult> ScanAsync(
        IEnumerable<CleanCategoryDef> categories,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var result = new ScanResult();
            foreach (var category in categories)
            {
                ct.ThrowIfCancellationRequested();
                result.Categories.Add(ScanCategory(category, progress, ct));
            }
            return result;
        }, ct);
    }

    internal CategoryScanResult ScanCategory(CleanCategoryDef category, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var res = new CategoryScanResult { Category = category };

        if (category.Id == "recycle_bin")
        {
            var q = RecycleBinHelper.Query();
            if (q.HasValue)
            {
                res.TotalBytes = q.Value.Bytes;
                res.Items.Add(new CleanItem("recycle_bin://", q.Value.Bytes, IsDirectory: false));
            }
            return res;
        }

        foreach (var root in category.Roots.Where(Directory.Exists))
        {
            string validatedRoot;
            try { validatedRoot = _guard.ValidateForScan(root); }
            catch (Security.PathGuardException) { continue; }

            Enumerate(validatedRoot, res, progress, ct);
        }
        return res;
    }

    private void Enumerate(string directory, CategoryScanResult res, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_guard.IsExcluded(directory)) return;

        // Non attraversare junction/symlink (protezione da percorsi fuori perimetro).
        try
        {
            var attr = File.GetAttributes(directory);
            if ((attr & FileAttributes.ReparsePoint) != 0) return;
        }
        catch (Exception) { res.Errors++; return; }

        IEnumerable<string> files;
        IEnumerable<string> dirs;
        try
        {
            files = Directory.EnumerateFiles(directory);
            dirs = Directory.EnumerateDirectories(directory);
        }
        catch (UnauthorizedAccessException) { res.Errors++; return; }
        catch (DirectoryNotFoundException) { return; }
        catch (IOException) { res.Errors++; return; }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (_guard.IsExcluded(file)) continue;
            try
            {
                var fi = new FileInfo(file);
                if (!fi.Exists) continue; // race: file sparito
                res.Items.Add(new CleanItem(fi.FullName, fi.Length, IsDirectory: false));
                res.TotalBytes += fi.Length;
                progress?.Report(new ScanProgress(fi.FullName, res.Items.Count, res.TotalBytes));
            }
            catch (Exception) { res.Errors++; }
        }

        foreach (var dir in dirs)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var di = new DirectoryInfo(dir);
                if (!di.Exists) continue;
                // Le sottodirectory vuote contano come elemento rimovibile.
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    res.Items.Add(new CleanItem(di.FullName, 0, IsDirectory: true));
                    progress?.Report(new ScanProgress(di.FullName, res.Items.Count, res.TotalBytes));
                    continue;
                }
            }
            catch (Exception) { res.Errors++; continue; }
            Enumerate(dir, res, progress, ct);
        }
    }
}
