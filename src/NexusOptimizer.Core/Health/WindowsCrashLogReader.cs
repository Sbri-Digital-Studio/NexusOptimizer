using System.Diagnostics;

namespace NexusOptimizer.Core.Health;

public interface ICrashLogReader
{
    CrashAnalysis ReadRecent(TimeSpan window, CancellationToken cancellationToken = default);
}

/// <summary>Reader in sola lettura dell'Application event log di Windows.</summary>
public sealed class WindowsCrashLogReader : ICrashLogReader
{
    public CrashAnalysis ReadRecent(TimeSpan window, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        try
        {
            var cutoff = DateTime.Now.Subtract(window);
            var candidates = new List<CrashIncident>();
            using var applicationLog = new EventLog("Application");

            // Il registro è ordinato cronologicamente: fermarsi alla finestra richiesta evita
            // di scandire tutta la sua storia e mantiene il refresh leggero.
            for (var index = applicationLog.Entries.Count - 1; index >= 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = applicationLog.Entries[index];
                if (entry.TimeGenerated < cutoff) break;
                if (entry.EntryType != EventLogEntryType.Error) continue;

                var eventId = unchecked((int)(entry.InstanceId & 0xFFFF));
                candidates.Add(new CrashIncident(entry.TimeGenerated, entry.Source, eventId));
            }

            return CrashAnalyzer.Analyze(candidates, cutoff);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or System.ComponentModel.Win32Exception
                                   or UnauthorizedAccessException
                                   or System.Security.SecurityException)
        {
            // Un log non disponibile non deve invalidare il resto della diagnostica.
            return CrashAnalysis.Unavailable(ex.GetType().Name);
        }
    }
}
