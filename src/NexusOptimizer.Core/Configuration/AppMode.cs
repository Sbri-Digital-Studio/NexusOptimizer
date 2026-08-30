namespace NexusOptimizer.Core.Configuration;

/// <summary>
/// Livello operativo scelto dall'utente. Non è un'etichetta cosmetica: definisce
/// quali azioni il programma può proporre e quali restano fuori dal perimetro.
/// SAFE      → Optimizer: solo azioni che non scrivono preferenze di sistema
///             (avvii automatici, pulizia nel Cestino, compattazione memoria).
///             Gaming: pre-seleziona solo le app che si riaprono da sole.
/// BALANCED  → Optimizer: sblocca le preferenze utente in HKCU (effetti visivi,
///             opzioni di Windows). Gaming: aggiunge browser, musica e chat.
/// EXPERT    → Optimizer: sblocca le modifiche che restano attive anche a Nexus
///             chiuso (piano energetico). Gaming: chiusura forzata e app non
///             catalogate.
/// </summary>
public enum AppModeLevel
{
    Safe = 0,
    Balanced = 1,
    Expert = 2,
}

public static class AppModeLevels
{
    public const string SafeId = "safe";
    public const string BalancedId = "balanced";
    public const string ExpertId = "expert";

    public static AppModeLevel Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        BalancedId => AppModeLevel.Balanced,
        ExpertId => AppModeLevel.Expert,
        _ => AppModeLevel.Safe,
    };

    public static string ToId(this AppModeLevel level) => level switch
    {
        AppModeLevel.Balanced => BalancedId,
        AppModeLevel.Expert => ExpertId,
        _ => SafeId,
    };

    /// <summary>Etichetta breve mostrata nella titlebar e nella sidebar.</summary>
    public static string ToDisplayName(this AppModeLevel level) => level switch
    {
        AppModeLevel.Balanced => "BALANCED",
        AppModeLevel.Expert => "EXPERT",
        _ => "SAFE",
    };
}
