namespace NexusOptimizer.Core.Security;

/// <summary>
/// Elenco centralizzato delle directory critiche che NON devono mai essere modificate
/// dal motore di pulizia, salvo eccezioni documentate (es. C:\Windows\Temp per la categoria Temp).
/// </summary>
public static class ProtectedPaths
{
    public static readonly IReadOnlyList<string> CriticalRoots = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),               // C:\Windows (System32, WinSxS...)
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // C:\ProgramData
    };

    /// <summary>Sottopercorsi esplicitamente consentiti dentro radici critiche, documentati.</summary>
    public static readonly IReadOnlyList<string> DocumentedExceptions = new[]
    {
        // C:\Windows\Temp: file temporanei di sistema, cancellabili in sicurezza.
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
        // C:\Windows\Prefetch è VOLONTARIAMENTE non incluso: cancellarlo degrada l'avvio.
    };

    /// <summary>Percorsi utente personali mai toccati dalla pulizia automatica.</summary>
    public static readonly IReadOnlyList<string> UserProtectedFolders = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
    };

    public static bool IsCriticalRoot(string fullPath)
    {
        foreach (var root in CriticalRoots)
        {
            if (!string.IsNullOrEmpty(root) && IsUnder(fullPath, root)) return true;
        }
        return false;
    }

    public static bool IsDocumentedException(string fullPath)
    {
        foreach (var exc in DocumentedExceptions)
        {
            if (!string.IsNullOrEmpty(exc) && IsUnder(fullPath, exc)) return true;
        }
        return false;
    }

    public static bool IsUserProtected(string fullPath)
    {
        foreach (var p in UserProtectedFolders)
        {
            if (!string.IsNullOrEmpty(p) && IsUnder(fullPath, p)) return true;
        }
        return false;
    }

    /// <summary>Verifica case-insensitive che 'path' coincida o discenda da 'baseDir'.</summary>
    public static bool IsUnder(string path, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(baseDir)) return false;
        var normPath = Normalize(path);
        var normBase = Normalize(baseDir);
        if (normBase.Length == 0 || !Path.IsPathFullyQualified(normBase)) return false;
        if (!normPath.StartsWith(normBase, StringComparison.OrdinalIgnoreCase)) return false;
        // Deve coincidere o essere separato da un delimitatore (evita C:\WindowsTemp vs C:\Windows).
        return normPath.Length == normBase.Length || normPath[normBase.Length] == Path.DirectorySeparatorChar;
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar);
    }
}
