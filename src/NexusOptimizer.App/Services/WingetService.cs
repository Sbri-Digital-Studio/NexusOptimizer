using System.Diagnostics;
using System.Text;
using NexusOptimizer.Core.Logging;

namespace NexusOptimizer.App.Services;

/// <summary>Programma con una versione più recente disponibile.</summary>
public sealed record PackageUpdate(
    string Id,
    string Name,
    string CurrentVersion,
    string AvailableVersion,
    string Source)
{
    /// <summary>Identità dell'annuncio: la stessa versione non avvisa due volte.</summary>
    public string AnnounceKey => Id + "@" + AvailableVersion;
}

public enum PackageManagerStatus
{
    /// <summary>winget non è presente su questo PC: nessuna ipotesi al suo posto.</summary>
    NotAvailable,

    UpToDate,
    UpdatesAvailable,
    Failed,
}

public sealed record PackageScanResult(PackageManagerStatus Status, IReadOnlyList<PackageUpdate> Updates)
{
    public static readonly PackageScanResult NotAvailable = new(PackageManagerStatus.NotAvailable, []);
}

/// <summary>
/// Aggiornamenti dei programmi installati tramite <c>winget</c>, il gestore
/// pacchetti incluso in Windows.
///
/// Perché winget e non un catalogo nostro: le versioni disponibili arrivano dai
/// manifest ufficiali dei produttori, verificati con hash, e l'aggiornamento
/// esegue l'installer originale. Un catalogo compilato a mano invecchia, sbaglia
/// il confronto di versione e finisce per scaricare binari da fonti non
/// verificabili. Qui non si scarica nulla per conto proprio.
///
/// La ricerca è una chiamata di rete: parte su richiesta esplicita o con il
/// controllo automatico attivato dall'utente. L'aggiornamento avviene solo su
/// comando, un programma alla volta.
/// </summary>
public sealed class WingetService(FileLogService log)
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan UpgradeTimeout = TimeSpan.FromMinutes(15);

    private readonly FileLogService _log = log;
    private bool? _available;

    /// <summary>winget risponde su questo PC? Verificato una volta sola.</summary>
    public bool IsAvailable
    {
        get
        {
            _available ??= Probe();
            return _available.Value;
        }
    }

    private bool Probe()
    {
        try
        {
            var (exitCode, output) = Run("--version", TimeSpan.FromSeconds(15));
            var ok = exitCode == 0 && output.Contains('v', StringComparison.OrdinalIgnoreCase);
            _log.Info(ok ? $"winget disponibile: {output.Trim()}" : "winget non disponibile su questo PC.");
            return ok;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Task<PackageScanResult> ScanAsync(CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            if (!IsAvailable) return PackageScanResult.NotAvailable;
            try
            {
                var (exitCode, output) = Run(
                    "upgrade --include-unknown --disable-interactivity --accept-source-agreements",
                    ScanTimeout);

                // winget restituisce un codice diverso da zero anche quando dice
                // semplicemente "nessun aggiornamento": conta l'output, non l'esito.
                var updates = ParseUpgradeTable(output);
                if (updates.Count > 0)
                    return new PackageScanResult(PackageManagerStatus.UpdatesAvailable, updates);

                var recognised = output.Contains("---", StringComparison.Ordinal) || exitCode == 0;
                return recognised
                    ? new PackageScanResult(PackageManagerStatus.UpToDate, [])
                    : new PackageScanResult(PackageManagerStatus.Failed, []);
            }
            catch (Exception ex)
            {
                _log.Error("Ricerca aggiornamenti programmi non riuscita", ex);
                return new PackageScanResult(PackageManagerStatus.Failed, []);
            }
        }, cancellationToken);

    /// <summary>
    /// Aggiorna un singolo programma con l'installer del produttore. Restituisce
    /// il codice di uscita di winget: diverso da zero significa che l'operazione
    /// non è andata a buon fine (spesso servono privilegi di amministratore).
    /// </summary>
    public Task<(bool Ok, string Output)> UpgradeAsync(PackageUpdate package,
                                                       CancellationToken cancellationToken = default)
        => Task.Run<(bool, string)>(() =>
        {
            if (!IsAvailable) return (false, "");
            try
            {
                _log.Info($"Aggiornamento programma richiesto: {package.Id} -> {package.AvailableVersion}");
                var (exitCode, output) = Run(
                    $"upgrade --id \"{package.Id}\" --silent --disable-interactivity " +
                    "--accept-package-agreements --accept-source-agreements",
                    UpgradeTimeout);
                if (exitCode != 0) _log.Warning($"winget upgrade {package.Id} => codice {exitCode}");
                return (exitCode == 0, output);
            }
            catch (Exception ex)
            {
                _log.Error($"Aggiornamento di {package.Id} non riuscito", ex);
                return (false, "");
            }
        }, cancellationToken);

    private static (int ExitCode, string Output) Run(string arguments, TimeSpan timeout)
    {
        var info = new ProcessStartInfo("winget.exe", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(info);
        if (process is null) return (-1, "");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            return (-1, output);
        }
        return (process.ExitCode, output + error);
    }

    /// <summary>
    /// Legge la tabella di <c>winget upgrade</c>. Le intestazioni sono tradotte,
    /// quindi le colonne si ricavano dalla loro posizione nella riga di testata:
    /// nome, id, versione, disponibile, origine. Una riga che non rispetta lo
    /// schema viene ignorata invece di essere interpretata a caso.
    /// </summary>
    internal static IReadOnlyList<PackageUpdate> ParseUpgradeTable(string output)
    {
        var results = new List<PackageUpdate>();
        if (string.IsNullOrWhiteSpace(output)) return results;

        // CRLF prima dei \r isolati: invertendo l'ordine ogni "\r\n" diventerebbe
        // una riga vuota di troppo e la tabella si chiuderebbe subito (winget usa CRLF).
        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal)
                          .Replace("\r", "\n", StringComparison.Ordinal)
                          .Split('\n')
                          .Select(StripProgress)
                          .ToArray();

        var separator = Array.FindIndex(lines, IsSeparator);
        if (separator <= 0) return results;

        var header = lines[separator - 1];
        var columns = ColumnStarts(header);
        if (columns.Count < 4) return results;

        for (var i = separator + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            // La tabella finisce alla prima riga vuota: dopo ci sono il riepilogo
            // testuale ed eventualmente una seconda tabella, che non vanno letti
            // con queste colonne.
            if (line.Trim().Length == 0) break;
            if (IsSeparator(line)) continue;
            if (line.Length < columns[1]) continue;

            var name = Slice(line, columns[0], columns[1]);
            var id = Slice(line, columns[1], columns[2]);
            var current = Slice(line, columns[2], columns[3]);
            var available = columns.Count > 4 ? Slice(line, columns[3], columns[4]) : Slice(line, columns[3], line.Length);
            var source = columns.Count > 4 ? Slice(line, columns[4], line.Length) : "";

            if (id.Length == 0 || available.Length == 0) continue;
            results.Add(new PackageUpdate(id, name.Length > 0 ? name : id, current, available, source));
        }

        return results;
    }

    private static string Slice(string line, int start, int end)
    {
        if (start >= line.Length) return "";
        var stop = Math.Min(end, line.Length);
        return stop <= start ? "" : line[start..stop].Trim();
    }

    /// <summary>
    /// Inizio di ogni colonna: i titoli sono separati da due o piu' spazi, e un
    /// titolo puo' contenerne uno singolo. Le intestazioni sono tradotte, quindi
    /// conta la posizione, non il testo.
    /// </summary>
    internal static List<int> ColumnStarts(string header)
    {
        var starts = new List<int>();
        var cursor = 0;
        foreach (var part in System.Text.RegularExpressions.Regex.Split(header.TrimEnd(), @"\s{2,}"))
        {
            if (part.Length == 0) continue;
            var index = header.IndexOf(part, cursor, StringComparison.Ordinal);
            if (index < 0) continue;
            starts.Add(index);
            cursor = index + part.Length;
        }
        return starts;
    }

    private static bool IsSeparator(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 3 && trimmed.All(c => c == '-');
    }

    /// <summary>
    /// Toglie i caratteri di controllo lasciati dalla barra di avanzamento. I
    /// trattini NON si toccano: sono la riga che separa intestazione e dati.
    /// </summary>
    private static string StripProgress(string line)
    {
        var cleaned = new StringBuilder(line.Length);
        foreach (var c in line)
        {
            if (char.IsControl(c) && c != '\t') continue;
            cleaned.Append(c);
        }
        return cleaned.ToString().TrimEnd();
    }
}
