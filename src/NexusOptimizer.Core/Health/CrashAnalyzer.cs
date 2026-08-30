namespace NexusOptimizer.Core.Health;

/// <summary>
/// Filtro puro e testabile per gli eventi che Windows associa a crash o blocchi delle
/// applicazioni. Non tratta ogni errore del registro come un crash.
/// </summary>
public static class CrashAnalyzer
{
    public static CrashAnalysis Analyze(IEnumerable<CrashIncident> entries, DateTime cutoffLocal)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var crashes = entries
            .Where(entry => entry.OccurredAt >= cutoffLocal && IsCrashEvent(entry.Source, entry.EventId))
            .OrderByDescending(entry => entry.OccurredAt)
            .ToArray();
        return new CrashAnalysis(true, crashes);
    }

    public static bool IsCrashEvent(string? source, int eventId)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        return source.Trim() switch
        {
            "Application Error" => eventId is 1000 or 1001,
            "Application Hang" => eventId == 1002,
            ".NET Runtime" => eventId == 1026,
            "Windows Error Reporting" => eventId == 1001,
            _ => false,
        };
    }
}
