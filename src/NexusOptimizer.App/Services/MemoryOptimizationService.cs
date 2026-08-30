using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace NexusOptimizer.App.Services;

/// <summary>Esito misurato della pulizia RAM richiesta esplicitamente dall'utente.</summary>
public sealed record RamOptimizationResult(
    long AvailableMemoryGainBytes,
    long TrimmedWorkingSetBytes,
    long NexusManagedBytesReleased,
    int TrimmedProcessCount)
{
    /// <summary>
    /// Il guadagno di memoria disponibile e' la misura primaria. Se Windows non lo
    /// riflette ancora, la riduzione reale dei working set resta una misura valida.
    /// </summary>
    public long RecoveredBytes => AvailableMemoryGainBytes > 0
        ? AvailableMemoryGainBytes
        : Math.Max(TrimmedWorkingSetBytes, NexusManagedBytesReleased);

    public bool Changed => RecoveredBytes > 0 || TrimmedProcessCount > 0;
}

/// <summary>
/// La VRAM degli altri processi non e' modificabile tramite un'API pubblica di
/// Windows. Questo risultato riguarda esclusivamente wrapper e risorse grafiche
/// non piu' raggiungibili appartenenti a Nexus.
/// </summary>
public sealed record VramOptimizationResult(long NexusManagedBytesReleased, bool RenderQueueFlushed);

/// <summary>
/// Contratto unico usato da RAM Manager, Optimizer e Modalita' Gaming: nessuna
/// sezione mantiene una propria variante dell'operazione di memoria.
/// </summary>
public interface IMemoryOptimizationService
{
    long? AvailableMemoryBytes();
    RamOptimizationResult OptimizeRam();
    VramOptimizationResult OptimizeVram();
}

/// <summary>
/// Ottimizzazioni memoria esplicite e non distruttive. La RAM viene recuperata
/// compattando l'heap di Nexus e riducendo i working set delle sole app della
/// sessione utente in background. Processo in primo piano, sistema, sicurezza,
/// driver e giochi protetti non vengono mai toccati.
///
/// Per la VRAM si possono rilasciare soltanto risorse grafiche possedute da Nexus:
/// Windows non espone un comando sicuro per svuotare allocazioni di giochi o app.
/// </summary>
public sealed class MemoryOptimizationService : IMemoryOptimizationService
{
    private const long MinimumWorkingSetBytes = 20L * 1024 * 1024;
    private readonly object _operationGate = new();

    public RamOptimizationResult OptimizeRam()
    {
        lock (_operationGate)
        {
            var availableBefore = AvailableMemoryBytes();
            var managedReleased = CollectUnusedNexusResources();
            var (trimmedBytes, trimmedCount) = TrimUserWorkingSets();
            var availableAfter = AvailableMemoryBytes();
            var availableGain = availableBefore is long before && availableAfter is long after
                ? Math.Max(0, after - before)
                : 0;

            return new RamOptimizationResult(
                availableGain,
                trimmedBytes,
                managedReleased,
                trimmedCount);
        }
    }

    public VramOptimizationResult OptimizeVram()
    {
        lock (_operationGate)
        {
            // I wrapper WPF/DirectX non piu' raggiungibili rilasciano qui le loro
            // risorse native; DwmFlush attende poi la coda di composizione.
            var managedReleased = CollectUnusedNexusResources();
            var flushed = false;
            try { flushed = DwmFlush() == 0; }
            catch (DllNotFoundException) { /* composizione non disponibile */ }
            catch (EntryPointNotFoundException) { /* versione Windows non supportata */ }

            return new VramOptimizationResult(managedReleased, flushed);
        }
    }

    public long? AvailableMemoryBytes()
    {
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? (long)status.AvailablePhysical : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static long CollectUnusedNexusResources()
    {
        var before = GC.GetTotalMemory(forceFullCollection: false);
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        catch (InvalidOperationException)
        {
            // Un altro GC puo' essersi avviato nel frattempo: nessuna azione rischiosa.
        }

        var after = GC.GetTotalMemory(forceFullCollection: false);
        return Math.Max(0, before - after);
    }

    private static (long Bytes, int Count) TrimUserWorkingSets()
    {
        long released = 0;
        var count = 0;
        var ownId = Environment.ProcessId;
        var ownSession = CurrentSessionId();
        var foreground = ForegroundProcessId();

        foreach (var process in SafeGetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == ownId || process.Id == foreground) continue;
                    if (process.SessionId != ownSession) continue;
                    if (ProtectedProcesses.IsProtected(process.ProcessName)) continue;

                    var before = process.WorkingSet64;
                    if (before < MinimumWorkingSetBytes) continue;
                    if (!EmptyWorkingSet(process.Handle)) continue;

                    process.Refresh();
                    var difference = Math.Max(0, before - process.WorkingSet64);
                    if (difference <= 0) continue;
                    released += difference;
                    count++;
                }
                catch (Exception)
                {
                    // Accesso negato o processo terminato: si passa oltre.
                }
            }
        }

        return (released, count);
    }

    private static Process[] SafeGetProcesses()
    {
        try { return Process.GetProcesses(); }
        catch (Exception) { return []; }
    }

    private static int CurrentSessionId()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.SessionId;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private static int ForegroundProcessId()
    {
        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return 0;
            _ = GetWindowThreadProcessId(handle, out var pid);
            return (int)pid;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr processHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmFlush();
}
