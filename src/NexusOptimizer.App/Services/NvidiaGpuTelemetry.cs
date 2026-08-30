using System.IO;
using System.Runtime.InteropServices;

namespace NexusOptimizer.App.Services;

/// <summary>Campione GPU letto da NVML; ogni campo è null se il driver non lo espone.</summary>
public sealed record NvidiaGpuSample(
    double? TemperatureCelsius,
    double? CoreClockMhz,
    double? MemoryClockMhz,
    double? FanPercent,
    double? PowerWatts,
    double? UtilizationPercent,
    double? MemoryUsedBytes,
    double? MemoryTotalBytes);

/// <summary>
/// Telemetria GPU NVIDIA tramite NVML (NVIDIA Management Library), la stessa
/// libreria usata da nvidia-smi e installata dal driver: nessuna dipendenza
/// aggiunta al progetto, nessun binario di terze parti.
///
/// Windows non espone temperatura e frequenza della GPU tramite PDH o WMI, quindi
/// senza questa sorgente resterebbero legittimamente "n.d.". Se la libreria non
/// c'è (GPU AMD/Intel, driver assente) <see cref="TryCreate"/> restituisce null e
/// il resto dell'applicazione continua a dichiarare il dato non disponibile.
/// </summary>
internal sealed class NvidiaGpuTelemetry : IDisposable
{
    private const int NvmlSuccess = 0;
    private const uint TemperatureSensorGpu = 0;
    private const uint ClockGraphics = 0;
    private const uint ClockMemory = 2;

    private readonly IntPtr _library;
    private readonly IntPtr _device;
    private readonly NvmlShutdown _shutdown;
    private readonly NvmlDeviceGetTemperature? _getTemperature;
    private readonly NvmlDeviceGetClockInfo? _getClockInfo;
    private readonly NvmlGetUint? _getFanSpeed;
    private readonly NvmlGetUint? _getPowerUsage;
    private readonly NvmlDeviceGetUtilizationRates? _getUtilization;
    private readonly NvmlDeviceGetMemoryInfo? _getMemoryInfo;
    private bool _disposed;

    private NvidiaGpuTelemetry(IntPtr library, IntPtr device, NvmlShutdown shutdown)
    {
        _library = library;
        _device = device;
        _shutdown = shutdown;
        _getTemperature = Bind<NvmlDeviceGetTemperature>(library, "nvmlDeviceGetTemperature");
        _getClockInfo = Bind<NvmlDeviceGetClockInfo>(library, "nvmlDeviceGetClockInfo");
        _getFanSpeed = Bind<NvmlGetUint>(library, "nvmlDeviceGetFanSpeed");
        _getPowerUsage = Bind<NvmlGetUint>(library, "nvmlDeviceGetPowerUsage");
        _getUtilization = Bind<NvmlDeviceGetUtilizationRates>(library, "nvmlDeviceGetUtilizationRates");
        _getMemoryInfo = Bind<NvmlDeviceGetMemoryInfo>(library, "nvmlDeviceGetMemoryInfo");
    }

    /// <summary>Apre NVML e prende la prima GPU; null se la libreria non è disponibile.</summary>
    public static NvidiaGpuTelemetry? TryCreate()
    {
        var library = LoadNvml();
        if (library == IntPtr.Zero) return null;

        try
        {
            var init = Bind<NvmlInit>(library, "nvmlInit_v2") ?? Bind<NvmlInit>(library, "nvmlInit");
            var shutdown = Bind<NvmlShutdown>(library, "nvmlShutdown");
            var getHandle = Bind<NvmlDeviceGetHandleByIndex>(library, "nvmlDeviceGetHandleByIndex_v2")
                            ?? Bind<NvmlDeviceGetHandleByIndex>(library, "nvmlDeviceGetHandleByIndex");
            if (init is null || shutdown is null || getHandle is null)
            {
                NativeLibrary.Free(library);
                return null;
            }

            if (init() != NvmlSuccess || getHandle(0, out var device) != NvmlSuccess || device == IntPtr.Zero)
            {
                try { shutdown(); } catch (Exception) { /* chiusura best effort */ }
                NativeLibrary.Free(library);
                return null;
            }

            return new NvidiaGpuTelemetry(library, device, shutdown);
        }
        catch (Exception)
        {
            NativeLibrary.Free(library);
            return null;
        }
    }

    /// <summary>Lettura completa; i campi non supportati dalla scheda restano null.</summary>
    public NvidiaGpuSample? Read()
    {
        if (_disposed) return null;
        try
        {
            double? temperature = _getTemperature is not null
                                  && _getTemperature(_device, TemperatureSensorGpu, out var celsius) == NvmlSuccess
                ? celsius
                : null;

            double? coreClock = _getClockInfo is not null
                                && _getClockInfo(_device, ClockGraphics, out var core) == NvmlSuccess
                ? core
                : null;

            double? memoryClock = _getClockInfo is not null
                                  && _getClockInfo(_device, ClockMemory, out var memory) == NvmlSuccess
                ? memory
                : null;

            double? fan = _getFanSpeed is not null && _getFanSpeed(_device, out var fanPercent) == NvmlSuccess
                ? fanPercent
                : null;

            // La potenza è in milliwatt: la portiamo a watt come il resto della UI.
            double? power = _getPowerUsage is not null && _getPowerUsage(_device, out var milliwatts) == NvmlSuccess
                ? milliwatts / 1000d
                : null;

            double? utilization = null;
            if (_getUtilization is not null && _getUtilization(_device, out var rates) == NvmlSuccess)
                utilization = rates.Gpu;

            double? used = null, total = null;
            if (_getMemoryInfo is not null && _getMemoryInfo(_device, out var memoryInfo) == NvmlSuccess)
            {
                used = memoryInfo.Used;
                total = memoryInfo.Total;
            }

            return new NvidiaGpuSample(temperature, coreClock, memoryClock, fan, power, utilization, used, total);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _shutdown(); } catch (Exception) { /* chiusura best effort */ }
        try { NativeLibrary.Free(_library); } catch (Exception) { /* idem */ }
    }

    // ---------------------------------------------------------------- binding

    /// <summary>
    /// nvml.dll accompagna il driver: di norma è in System32, sulle installazioni
    /// più vecchie nella cartella NVSMI. Si prova nell'ordine e si rinuncia in
    /// silenzio: l'assenza della libreria è uno scenario normale, non un errore.
    /// </summary>
    private static IntPtr LoadNvml()
    {
        foreach (var candidate in CandidatePaths())
        {
            try
            {
                if (NativeLibrary.TryLoad(candidate, out var handle)) return handle;
            }
            catch (Exception) { /* percorso non valido: si prova il successivo */ }
        }
        return IntPtr.Zero;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        yield return "nvml.dll";
        foreach (var variable in new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)" })
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(root)) continue;
            yield return Path.Combine(root, "NVIDIA Corporation", "NVSMI", "nvml.dll");
        }
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (system.Length > 0) yield return Path.Combine(system, "nvml.dll");
    }

    private static T? Bind<T>(IntPtr library, string entryPoint) where T : Delegate
    {
        try
        {
            return NativeLibrary.TryGetExport(library, entryPoint, out var address)
                ? Marshal.GetDelegateForFunctionPointer<T>(address)
                : null;
        }
        catch (Exception) { return null; }
    }

    // --------------------------------------------------------------- P/Invoke

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlInit();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlShutdown();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetTemperature(IntPtr device, uint sensorType, out uint temperature);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetClockInfo(IntPtr device, uint clockType, out uint clockMhz);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlGetUint(IntPtr device, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }
}
