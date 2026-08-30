namespace NexusOptimizer.Core.Notifications;

/// <summary>Gravità di un avviso. Determina colore, icona e priorità di lettura.</summary>
public enum NotificationSeverity
{
    /// <summary>Informazione utile, nessuna azione urgente (es. spazio recuperabile).</summary>
    Info,

    /// <summary>Soglia superata: vale la pena intervenire (es. disco quasi pieno).</summary>
    Warning,

    /// <summary>Condizione critica misurata (es. temperatura oltre il limite di sicurezza).</summary>
    Critical,
}

/// <summary>
/// Avviso generato da una misura reale. Il testo NON è memorizzato tradotto: si
/// conservano le chiavi di localizzazione e gli argomenti, così un cambio lingua
/// riscrive anche la cronologia già raccolta.
/// </summary>
public sealed class NotificationRecord
{
    /// <summary>Identità della regola (usata per deduplica e cooldown), es. "disk.low:C:".</summary>
    public required string Key { get; init; }

    public required string TitleKey { get; init; }
    public required string MessageKey { get; init; }

    /// <summary>Argomenti già formattati (percentuali, byte, nomi) per i segnaposto {0}, {1}…</summary>
    public IReadOnlyList<string> MessageArgs { get; init; } = [];

    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Info;

    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Sezione da aprire al clic sull'avviso (es. "nav.diskmanager"); null se non applicabile.</summary>
    public string? TargetSectionId { get; init; }

    /// <summary>URL da aprire nel browser: usato solo dall'avviso di aggiornamento disponibile.</summary>
    public string? TargetUrl { get; init; }

    public bool IsRead { get; set; }
}
