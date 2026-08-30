using System.Globalization;

namespace NexusOptimizer.App.Services;

/// <summary>
/// Formattazione numerica coerente in tutta l'applicazione. La cultura segue la
/// lingua scelta dall'utente, non quella del sistema operativo: cosi' il formato
/// dei numeri resta prevedibile e coerente con i testi mostrati accanto.
/// </summary>
public static class Formatter
{
    private static CultureInfo It => Locale.Culture;

    /// <summary>Dimensioni leggibili: "2,3 GB", "480 MB", "12 KB", "0 B".</summary>
    public static string Bytes(double? b)
    {
        if (b is null || b < 0) return Dash;
        const double K = 1024;
        if (b >= K * K * K) return (b.Value / (K * K * K)).ToString("#,##0.0", It) + " GB";
        if (b >= K * K) return (b.Value / (K * K)).ToString("#,##0.0", It) + " MB";
        if (b >= K) return (b.Value / K).ToString("#,##0", It) + " KB";
        return ((int)b).ToString(It) + " B";
    }

    /// <summary>Tasso rete leggibile: KB/s, MB/s o GB/s in base al valore reale.</summary>
    public static string RatePerSec(double kbPerSecond)
    {
        if (kbPerSecond <= 0.001) return $"0{kSp}{It.NumberFormat.NumberDecimalSeparator}0 KB/s";
        if (kbPerSecond < 1024)
            return $"{kbPerSecond.ToString("#,##0.##", It)} KB/s";
        if (kbPerSecond < 1024 * 1024)
            return $"{(kbPerSecond / 1024).ToString("#,##0.0", It)} MB/s";
        return $"{(kbPerSecond / (1024 * 1024)).ToString("#,##0.0", It)} GB/s";
    }

    /// <summary>Valore percentuale; unknown => "n.d." (mai dati inventati).</summary>
    public static string Percent(double? v, int decimals = 0)
        => v is null or < 0 ? Unavailable : $"{v.Value.ToString($"#,##0.{new string('0', Math.Max(decimals, 0))}", It)}%";

    /// <summary>Uptime compatto.</summary>
    public static string Uptime(TimeSpan t)
        => t.TotalDays >= 1
            ? $"{(int)t.TotalDays}g {t.Hours}h {t.Minutes}m"
            : $"{t.Hours}h {t.Minutes}m";

    public static string Pluralize(int n, string singular, string plural)
        => n == 1 ? $"{n} {singular}" : $"{n} {plural}";

    /// <summary>Riga superiore dell'uptime: "2 Giorni" oppure "Oggi".</summary>
    public static string UptimeDays(TimeSpan t)
    {
        var days = (int)t.TotalDays;
        return days <= 0
            ? Locale.T("fmt.uptime.today")
            : Locale.P(days, "fmt.uptime.day.one", "fmt.uptime.day.many");
    }

    /// <summary>Riga inferiore dell'uptime: "04:15:32".</summary>
    public static string UptimeClock(TimeSpan t)
        => $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>Velocità di rete in Mbps, l'unità con cui vengono dichiarate le linee.</summary>
    public static string Mbps(double? kbPerSecond)
    {
        if (kbPerSecond is null || kbPerSecond < 0) return Unavailable;
        if (kbPerSecond == 0) return $"0{kSp}{It.NumberFormat.NumberDecimalSeparator}0 Mbps";

        var mbps = kbPerSecond.Value * 1024d * 8d / 1_000_000d;
        // Sotto 0,1 Mbps la conversione con una sola cifra decimale nasconde
        // il traffico reale (es. 0,5 KB/s diventava 0,0 Mbps). In quel caso
        // mostriamo l'unità più leggibile, senza arrotondare a zero.
        return mbps < 0.1
            ? $"{kbPerSecond.Value.ToString("#,##0.##", It)} KB/s"
            : mbps.ToString("#,##0.0", It) + " Mbps";
    }

    /// <summary>Temperatura reale; n.d. se il firmware non espone la zona termica.</summary>
    public static string Celsius(double? value)
        => value is null ? Unavailable : value.Value.ToString("#,##0", It) + " °C";

    /// <summary>Frequenza: GHz sopra 1000 MHz, altrimenti MHz.</summary>
    public static string Clock(double? megahertz)
        => megahertz is null or <= 0
            ? Unavailable
            : megahertz >= 1000
                ? (megahertz.Value / 1000d).ToString("#,##0.00", It) + " GHz"
                : megahertz.Value.ToString("#,##0", It) + " MHz";

    /// <summary>Dimensioni in GB con una cifra: usato per VRAM e memoria.</summary>
    public static string Gigabytes(double? bytes)
        => bytes is null || bytes < 0
            ? Unavailable
            : (bytes.Value / (1024d * 1024d * 1024d)).ToString("#,##0.0", It) + " GB";

    public static string Count(int n) => n.ToString("N0", It);

    public static string Count(int? n) => n is null ? Unavailable : n.Value.ToString("N0", It);

    /// <summary>Trattino: simbolo identico in ogni lingua.</summary>
    public const string Dash = "—";

    /// <summary>
    /// Marcatore "dato non esposto da Windows". E' testo, non un simbolo: in
    /// inglese diventa "n/a", altrimenti resterebbe un'abbreviazione italiana
    /// in mezzo a un'interfaccia tradotta.
    /// </summary>
    public static string Unavailable => Locale.T("fmt.unavailable");
    private const string kSp = "";
}
