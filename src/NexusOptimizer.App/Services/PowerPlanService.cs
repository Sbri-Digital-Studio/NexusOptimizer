using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NexusOptimizer.App.Services;

/// <summary>Piano energetico di Windows descritto da GUID e nome leggibile.</summary>
public sealed record PowerPlan(string SchemeId, string Name);

/// <summary>
/// Accesso ai piani energetici tramite powercfg, lo strumento di Windows.
/// Nexus non crea piani propri e non modifica le soglie: si limita a leggere
/// quelli già presenti e ad attivarne uno, così il ripristino è sempre esatto.
///
/// Condiviso tra Modalità Gaming (cambio temporaneo, ripristinato all'uscita) e
/// Optimizer (cambio persistente, annullabile dalla riga).
/// </summary>
public static class PowerPlanService
{
    public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    private static readonly Regex GuidPattern =
        new("[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", RegexOptions.Compiled);

    /// <summary>Piano attivo in questo momento; null se powercfg non risponde.</summary>
    public static PowerPlan? ReadActive()
    {
        var output = Run("/getactivescheme");
        if (output is null) return null;

        var guid = GuidPattern.Match(output);
        if (!guid.Success) return null;

        var start = output.IndexOf('(', StringComparison.Ordinal);
        var end = output.LastIndexOf(')');
        var name = end > start && start >= 0 ? output[(start + 1)..end].Trim() : "";
        return new PowerPlan(guid.Value, name.Length == 0 ? "Piano corrente" : name);
    }

    /// <summary>Nome del piano attivo, per la striscia di stato della dashboard.</summary>
    public static string? ReadActiveName() => ReadActive()?.Name;

    /// <summary>
    /// Miglior piano prestazionale già presente sul PC: "Prestazioni max" se
    /// l'utente lo ha sbloccato, altrimenti "Prestazioni elevate". Null se la
    /// macchina espone solo il piano bilanciato (tipico dei portatili moderni).
    /// </summary>
    public static PowerPlan? FindPerformancePlan()
    {
        var list = Run("/list") ?? string.Empty;
        if (list.Contains(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase))
            return new PowerPlan(UltimatePerformanceGuid, "Prestazioni max");
        if (list.Contains(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase))
            return new PowerPlan(HighPerformanceGuid, "Prestazioni elevate");
        return null;
    }

    /// <summary>Attiva un piano esistente; false se powercfg rifiuta.</summary>
    public static bool Activate(string schemeId)
        => schemeId.Length > 0 && Run($"/setactive {schemeId}") is not null;

    private static string? Run(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powercfg.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(4000);
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception) { return null; }
    }
}
