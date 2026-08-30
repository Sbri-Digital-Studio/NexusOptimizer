namespace NexusOptimizer.Core.Cleaning;

public enum SecurityLevel
{
    Green = 0,   // sicuro da eliminare
    Yellow = 1,  // cache/dati ricreabili
    Red = 2      // potenzialmente importante: MAI selezionato di default
}

/// <summary>Definizione statica di una categoria di pulizia.</summary>
public sealed record CleanCategoryDef(
    string Id,
    string NameKey,                 // chiave localizzazione
    SecurityLevel Level,
    bool SelectedByDefault,
    bool RequiresAdmin,
    IReadOnlyList<string> Roots     // directory autorizzate per questa categoria (base della validazione)
);

/// <summary>Risultato della scansione di un singolo elemento (file o cartella).</summary>
public sealed record CleanItem(string FullPath, long SizeBytes, bool IsDirectory);

/// <summary>Esito scansione di una categoria.</summary>
public sealed class CategoryScanResult
{
    public CleanCategoryDef Category { get; init; } = null!;
    public List<CleanItem> Items { get; } = new();
    public long TotalBytes { get; set; }
    public int SkippedLocked { get; set; }
    public int Errors { get; set; }
}

/// <summary>Esito complessivo della scansione.</summary>
public sealed class ScanResult
{
    public List<CategoryScanResult> Categories { get; } = new();
    public long TotalBytes => Categories.Sum(c => c.TotalBytes);
    public int TotalFiles => Categories.Sum(c => c.Items.Count(i => !i.IsDirectory));
    public int Errors => Categories.Sum(c => c.Errors);
}

/// <summary>Opzioni del motore di pulizia.</summary>
public sealed class CleanOptions
{
    /// <summary>Quando true simula l'operazione senza cancellare nulla (default in sviluppo/test).</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Sposta nel cestino invece dell'eliminazione permanente quando possibile.</summary>
    public bool UseRecycleBin { get; set; } = true;

    /// <summary>
    /// Richiede una copia cifrata nella quarantena prima della rimozione. Se la
    /// quarantena non è disponibile il motore salta il file: mai fallback a delete.
    /// </summary>
    public bool UseQuarantine { get; set; }

    public IReadOnlyList<string> Exclusions { get; set; } = Array.Empty<string>();
}

/// <summary>Report finale della pulizia con soli dati reali misurati.</summary>
public sealed class CleanResult
{
    public bool WasDryRun { get; init; }
    public long BytesFreed { get; set; }
    public int ItemsRemoved { get; set; }
    public int ItemsSkipped { get; set; }
    public List<string> ErrorMessages { get; } = new();
}
