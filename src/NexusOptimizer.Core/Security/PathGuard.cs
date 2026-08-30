namespace NexusOptimizer.Core.Security;

/// <summary>
/// Validazione difensiva dei percorsi prima di qualsiasi operazione di scansione/cancellazione.
/// Protegge da path traversal, percorsi vuoti, reparse point fuori dal perimetro consentito
/// e cancellazioni accidentali di directory critiche.
/// </summary>
public sealed class PathGuard
{
    private readonly IReadOnlyList<string> _exclusions;

    public PathGuard(IEnumerable<string>? exclusions = null)
    {
        var list = new List<string>();
        if (exclusions != null)
        {
            foreach (var ex in exclusions)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(ex) && Directory.Exists(ex))
                        list.Add(Path.GetFullPath(ex).TrimEnd(Path.DirectorySeparatorChar));
                    else if (!string.IsNullOrWhiteSpace(ex) && File.Exists(ex))
                        list.Add(Path.GetFullPath(ex));
                }
                catch (Exception) { /* percorso non valido: ignorato */ }
            }
        }
        _exclusions = list;
    }

    /// <summary>
    /// Normalizza e valida un percorso per l'analisi. Ritorna il percorso assoluto normalizzato.
    /// Solleva <see cref="PathGuardException"/> se il percorso è vuoto, inesistente o escluso.
    /// </summary>
    public string ValidateForScan(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PathGuardException("Percorso vuoto: operazione negata.");
        if (!Path.IsPathRooted(path))
            throw new PathGuardException($"Percorso relativo non ammesso: '{path}'.");

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception e) { throw new PathGuardException($"Percorso non valido: '{path}'.", e); }

        if (!Directory.Exists(full) && !File.Exists(full))
            throw new PathGuardException($"Percorso inesistente: '{full}'.");

        if (_exclusions.Any(ex => ProtectedPaths.IsUnder(full, ex)))
            throw new PathGuardException($"Percorso escluso dall'utente: '{full}'.");

        return full;
    }

    /// <summary>
    /// Valida un percorso per la CANCELLAZIONE. Regole:
    /// 1) percorso non vuolo e radicato; 2) mai una radice di drive o directory critica intera;
    /// 3) permessa solo se dentro una delle root consentite passate dal chiamante (categoria pulizia);
    /// 4) eccezioni documentate (C:\Windows\Temp) trattate come root consentite;
    /// 5) mai cartelle utente protette (Documenti, Desktop, Download...).
    /// </summary>
    public void ValidateForDelete(string path, IReadOnlyCollection<string> allowedRoots)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PathGuardException("Percorso vuoto: cancellazione negata.");
        if (!Path.IsPathRooted(path))
            throw new PathGuardException($"Percorso relativo non ammesso: '{path}'.");

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception e) { throw new PathGuardException($"Percorso non valido: '{path}'.", e); }

        // Mai cancellare radici di drive o percorsi troppo corti.
        var root = Path.GetPathRoot(full) ?? string.Empty;
        if (root.Length == 0 || full.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new PathGuardException($"Radice di drive: cancellazione negata ('{full}').");

        // Mai cartelle utente personali.
        if (ProtectedPaths.IsUserProtected(full))
            throw new PathGuardException($"Cartella utente protetta: '{full}'.");

        // Directory critiche: solo eccezioni documentate.
        if (ProtectedPaths.IsCriticalRoot(full) && !ProtectedPaths.IsDocumentedException(full))
            throw new PathGuardException($"Directory critica di sistema: '{full}'.");

        // Deve trovarsi dentro almeno una root autorizzata dalla categoria corrente.
        string? authorizedRoot = null;
        foreach (var r in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            string normRoot;
            try { normRoot = Path.GetFullPath(r); } catch (Exception) { continue; }
            // La root di categoria è solo un confine: non può essere cancellata interamente.
            if (PathsEqual(full, normRoot)) continue;
            if (ProtectedPaths.IsUnder(full, normRoot)) { authorizedRoot = normRoot; break; }
        }
        if (authorizedRoot is null)
            throw new PathGuardException($"Percorso fuori dalle directory autorizzate dalla categoria: '{full}'.");

        // Un percorso lessicalmente interno può attraversare una junction/symlink verso
        // l'esterno. Falliamo chiusi se il target o un genitore fino alla root è reparse.
        try
        {
            if (HasReparsePointInPath(full, authorizedRoot))
                throw new PathGuardException($"Reparse point non ammesso nel percorso: '{full}'.");
        }
        catch (PathGuardException) { throw; }
        catch (Exception e)
        {
            throw new PathGuardException($"Impossibile verificare in sicurezza il percorso: '{full}'.", e);
        }

        // Esclusioni utente hanno sempre priorità.
        if (_exclusions.Any(ex => ProtectedPaths.IsUnder(full, ex)))
            throw new PathGuardException($"Percorso escluso dall'utente: '{full}'.");
    }

    public bool IsExcluded(string fullPath)
        => _exclusions.Any(ex => ProtectedPaths.IsUnder(fullPath, ex));

    private static bool HasReparsePointInPath(string path, string allowedRoot)
    {
        string? current = path;
        while (!string.IsNullOrWhiteSpace(current) && ProtectedPaths.IsUnder(current, allowedRoot))
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return true;

            if (PathsEqual(current, allowedRoot)) break;
            current = Path.GetDirectoryName(current);
        }
        return false;
    }

    private static bool PathsEqual(string left, string right)
        => Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
}

public sealed class PathGuardException : Exception
{
    public PathGuardException(string message, Exception? inner = null) : base(message, inner) { }
}
