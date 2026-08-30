namespace NexusOptimizer.Core.Health;

/// <summary>Livello informativo usato da Health Score e raccomandazioni; non classifica malware.</summary>
public enum HealthSeverity
{
    Good,
    Attention,
    Critical,
    Info,
    Unavailable,
}

/// <summary>
/// Evidenza già filtrata dal registro eventi. Il messaggio completo non viene mai letto o
/// conservato: può includere percorsi e dati dell'utente.
/// </summary>
public sealed record CrashIncident(DateTime OccurredAt, string Source, int EventId);

public sealed record CrashAnalysis(
    bool IsAvailable,
    IReadOnlyList<CrashIncident> Incidents,
    string? UnavailabilityReason = null)
{
    public static CrashAnalysis Unavailable(string? reason = null) => new(false, [], reason);
}

public sealed record HealthInput(
    long? SystemDriveTotalBytes,
    long? SystemDriveFreeBytes,
    TimeSpan? Uptime,
    CrashAnalysis CrashAnalysis);

/// <summary>
/// Un fattore espone sia i punti sia l'evidenza numerica grezza: la UI può spiegare ogni
/// risultato senza nascondere la formula.
/// </summary>
public sealed record HealthFactor(
    string Id,
    int EarnedPoints,
    int MaximumPoints,
    HealthSeverity Severity,
    double? Evidence = null)
{
    public bool IsAvailable => MaximumPoints > 0;
}

public sealed record HealthRecommendation(string Id, HealthSeverity Severity);

public sealed record HealthAssessment(
    int? Score,
    bool IsPartial,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<HealthFactor> Factors,
    IReadOnlyList<HealthRecommendation> Recommendations,
    IReadOnlyList<CrashIncident> RecentCrashes);
