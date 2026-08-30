namespace NexusOptimizer.Core.Health;

/// <summary>
/// Raccoglie soltanto evidenze locali, senza privilegi permanenti e senza traffico di rete.
/// Le fonti non disponibili restano tali e vengono escluse dalla normalizzazione del punteggio.
/// </summary>
public sealed class LocalDiagnosticsService(ICrashLogReader? crashLogReader = null)
{
    private static readonly TimeSpan CrashWindow = TimeSpan.FromDays(7);
    private readonly ICrashLogReader _crashLogReader = crashLogReader ?? new WindowsCrashLogReader();

    public HealthAssessment Assess(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (total, free) = ReadSystemDrive();
        var uptime = ReadUptime();
        var crashes = _crashLogReader.ReadRecent(CrashWindow, cancellationToken);
        return HealthAssessmentEngine.Assess(new HealthInput(total, free, uptime, crashes));
    }

    private static (long? Total, long? Free) ReadSystemDrive()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root)) return (null, null);
            var drive = new DriveInfo(root);
            return drive.IsReady ? (drive.TotalSize, drive.AvailableFreeSpace) : (null, null);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or System.Security.SecurityException
                                   or ArgumentException)
        {
            return (null, null);
        }
    }

    private static TimeSpan? ReadUptime()
    {
        try { return TimeSpan.FromMilliseconds(Environment.TickCount64); }
        catch (Exception) { return null; }
    }
}
