using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexusOptimizer.Core.Updates;

/// <summary>
/// Manifest pubblicato dal canale di aggiornamento. Contiene solo cio' che serve
/// a dire "esiste una versione piu' recente" e dove leggerne le note: nessun
/// download viene avviato dall'applicazione.
/// </summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";

    /// <summary>Pagina della release (HTTPS) che l'utente apre nel browser.</summary>
    [JsonPropertyName("url")] public string Url { get; set; } = "";

    [JsonPropertyName("notes")] public string? Notes { get; set; }

    [JsonPropertyName("publishedUtc")] public DateTime? PublishedUtc { get; set; }
}

public enum UpdateCheckStatus
{
    /// <summary>Interruttore spento: nessuna chiamata di rete viene effettuata.</summary>
    Disabled,

    /// <summary>Nessun canale configurato: non c'e' niente da contattare.</summary>
    NotConfigured,

    UpToDate,
    UpdateAvailable,

    /// <summary>Canale irraggiungibile o risposta non valida: nessun downgrade silenzioso.</summary>
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string? LatestVersion = null, string? Url = null);

/// <summary>
/// Regole del canale di aggiornamento, senza rete: validazione dell'indirizzo,
/// lettura del manifest e confronto di versione. Sono la parte verificabile dai
/// test; la sola chiamata HTTPS vive nel servizio dell'applicazione.
/// </summary>
public static class UpdateChannel
{
    /// <summary>Tetto di lettura: un manifest legittimo sta in pochi kilobyte.</summary>
    public const int MaxManifestBytes = 64 * 1024;

    /// <summary>Intervallo minimo fra due controlli automatici all'avvio.</summary>
    public static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// Solo HTTPS assoluto: un canale in chiaro permetterebbe a un intermediario
    /// di annunciare una versione a piacere.
    /// </summary>
    public static bool IsSupportedFeed(string? url)
        => !string.IsNullOrWhiteSpace(url)
           && Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps;

    public static UpdateManifest? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version)) return null;
            if (ParseVersion(manifest.Version) is null) return null;
            // Un manifest che indicasse una pagina non HTTPS non e' utilizzabile.
            if (!string.IsNullOrWhiteSpace(manifest.Url) && !IsSupportedFeed(manifest.Url)) return null;
            return manifest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Versione dichiarata, tollerante ai suffissi di pre-release ("0.2.0-beta.1"):
    /// conta solo la parte numerica, il resto e' etichetta editoriale.
    /// </summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim().TrimStart('v', 'V');
        var cut = trimmed.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) trimmed = trimmed[..cut];
        if (trimmed.Length == 0) return null;
        return Version.TryParse(trimmed, out var version) ? version : null;
    }

    /// <summary>Confronto a tre cifre: la revisione di build non annuncia aggiornamenti.</summary>
    public static bool IsNewer(string? candidate, Version? current)
    {
        var parsed = ParseVersion(candidate);
        if (parsed is null || current is null) return false;
        return Normalize(parsed) > Normalize(current);
    }

    private static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
