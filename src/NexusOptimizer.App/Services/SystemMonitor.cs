using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace NexusOptimizer.App.Services;

/// <summary>
/// Campione misurato del sistema (nessun dato simulato). I campi nullable valgono
/// null quando Windows non espone la metrica su questa macchina: la UI mostra "n.d.".
/// </summary>
public sealed record SystemSnapshot(
    double? CpuPercent,
    double? RamUsedPercent,
    double? RamTotalBytes,
    double? RamAvailableBytes,
    double? DiskActivePercent,
    double? DiskKBytesPerSecond,
    double? NetDownKBytesPerSecond,
    double? NetUpKBytesPerSecond,
    int ProcessCount,
    TimeSpan Uptime)
{
    /// <summary>Temperatura da "Thermal Zone Information" (ACPI); null se il firmware non la espone.</summary>
    public double? CpuTemperatureCelsius { get; init; }

    /// <summary>Frequenza effettiva = % Processor Performance × frequenza massima nominale.</summary>
    public double? CpuClockMhz { get; init; }

    public int? CpuCores { get; init; }
    public int? CpuThreads { get; init; }

    /// <summary>Cache di sistema (contatore "Memory\Cache Bytes").</summary>
    public double? RamCachedBytes { get; init; }

    /// <summary>Utilizzo motori 3D della GPU (categoria PDH "GPU Engine").</summary>
    public double? GpuPercent { get; init; }

    /// <summary>Memoria video dedicata in uso (NVML se disponibile, altrimenti PDH).</summary>
    public double? GpuMemoryUsedBytes { get; init; }

    /// <summary>Memoria video totale della scheda; disponibile solo da NVML.</summary>
    public double? GpuMemoryTotalBytes { get; init; }

    /// <summary>Temperatura GPU da NVML; Windows non la espone da PDH o WMI.</summary>
    public double? GpuTemperatureCelsius { get; init; }

    /// <summary>Frequenza del core grafico (NVML).</summary>
    public double? GpuClockMhz { get; init; }

    /// <summary>Frequenza della memoria video (NVML).</summary>
    public double? GpuMemoryClockMhz { get; init; }

    /// <summary>Ventola GPU in percentuale (NVML).</summary>
    public double? GpuFanPercent { get; init; }

    /// <summary>Consumo istantaneo della GPU in watt (NVML).</summary>
    public double? GpuPowerWatts { get; init; }

    public double? DiskReadBytesPerSecond { get; init; }
    public double? DiskWriteBytesPerSecond { get; init; }

    public bool NetworkAvailable { get; init; }

    public int UserProcessCount { get; init; }
    public int SystemProcessCount { get; init; }

    /// <summary>Servizi Windows in esecuzione (aggiornato ogni 10 s per restare leggeri).</summary>
    public int? ServiceCount { get; init; }
}

/// <summary>
/// Monitor hardware reale tramite API Windows documentate: PDH (PerformanceCounter)
/// per CPU/disco/GPU, GlobalMemoryStatusEx per la RAM, statistiche NetworkInterface
/// (differenziali) per upload/download. Refresh adattivo: Pause() a finestra nascosta.
/// Ogni sorgente opzionale viene disattivata da sola se assente o troppo costosa,
/// così il monitor resta leggero anche su macchine senza i contatori estesi.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Tempi cumulativi di sistema. E' la stessa base usata da Gestione attività:
    /// contatori monotoni, quindi la percentuale si calcola sulla differenza fra
    /// due letture qualsiasi, senza dipendere dalla granularità di aggiornamento
    /// dei contatori PDH (che a cadenza inferiore al secondo restituiscono 0).
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    // Contatori opzionali: se la categoria non esiste sulla macchina resta null
    // e l'UI mostra "n.d." invece di un numero inventato.
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _diskIdleCounter;
    private PerformanceCounter? _diskBytesCounter;
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;
    private PerformanceCounter? _cpuPerformanceCounter;
    private PerformanceCounter? _memoryCacheCounter;
    private PdhWildcardCounter? _gpuUtilization;
    private PdhWildcardCounter? _gpuMemory;
    private NvidiaGpuTelemetry? _nvidia;
    private List<PerformanceCounter> _thermalCounters = [];

    private readonly System.Timers.Timer _timer;
    private readonly System.Timers.Timer _inventoryTimer;
    private readonly Stopwatch _netClock = Stopwatch.StartNew();
    private readonly List<double> _cpuRing = new(180);
    private readonly List<double> _ramRing = new(180);
    private readonly List<double> _diskRing = new(180);
    private readonly List<double> _downRing = new(180);
    private readonly List<double> _upRing = new(180);
    private long _lastRecvBytes;
    private long _lastSentBytes;
    private bool _primedNet;
    private int _tick;

    /// <summary>Inventario processi/servizi: riferimento immutabile, letto senza lock.</summary>
    private volatile Inventory _inventory = Inventory.Empty;

    /// <summary>Guardie di ri-entranza: un giro non deve mai sovrapporsi al successivo.</summary>
    private long _prevIdleTime;
    private long _prevKernelTime;
    private long _prevUserTime;
    private bool _cpuPrimed;
    private double? _lastCpuPercent;

    private int _sampling;
    private int _inventoryRunning;
    private int _inventoryTick;
    private double? _maxClockMhz;
    private int? _cores;
    private int? _threads;
    private readonly int _sessionId;

    public const int RingCapacity = 180;

    /// <summary>Raised sul thread di campionamento; il destinatario effettua il marshalling.</summary>
    public event Action<SystemSnapshot>? Snapshot;

    /// <summary>La cadenza è cambiata: le viste che traducono campioni in secondi si riallineano.</summary>
    public event Action? IntervalChanged;

    public IReadOnlyList<double> CpuRing => _cpuRing;
    public IReadOnlyList<double> RamRing => _ramRing;
    public IReadOnlyList<double> DiskRing => _diskRing;
    public IReadOnlyList<double> DownRing => _downRing;
    public IReadOnlyList<double> UpRing => _upRing;

    public bool IsRunning => _timer.Enabled;

    public SystemMonitor(double intervalMs = 1000)
    {
        _sessionId = ReadSessionId();
        CreateCounters();
        PrimeNetwork();
        _ = Task.Run(ReadStaticCpuFacts);
        _timer = new System.Timers.Timer(intervalMs) { AutoReset = true };
        _timer.Elapsed += (_, _) =>
        {
            // Senza questa guardia un campione piu' lento dell'intervallo farebbe
            // partire il giro successivo in parallelo: due letture PDH ravvicinate
            // restituiscono ~0 e il valore mostrato alterna zero e numero reale.
            if (Interlocked.Exchange(ref _sampling, 1) == 1) return;
            try
            {
                var snap = Sample();
                Push(snap);
                Snapshot?.Invoke(snap);
            }
            finally { Interlocked.Exchange(ref _sampling, 0); }
        };

        // L'inventario di processi e servizi e' la lettura piu' costosa: vive su un
        // timer proprio, cosi' il campione di CPU/RAM/disco/rete resta regolare.
        _inventoryTimer = new System.Timers.Timer(2000) { AutoReset = true };
        _inventoryTimer.Elapsed += (_, _) => RefreshInventory();
    }

    /// <summary>Conteggi di processi e servizi, aggiornati fuori dal giro veloce.</summary>
    private sealed record Inventory(int Total, int User, int System, int? Services)
    {
        public static readonly Inventory Empty = new(0, 0, 0, null);
    }

    /// <summary>Cadenza del campionamento, modificabile dalle Impostazioni.</summary>
    public double IntervalMs
    {
        get => _timer.Interval;
        set
        {
            var interval = Math.Clamp(value, MinIntervalMs, MaxIntervalMs);
            if (Math.Abs(_timer.Interval - interval) < 1) return;
            _timer.Interval = interval;
            IntervalChanged?.Invoke();
        }
    }

    public const double MinIntervalMs = 500;
    public const double MaxIntervalMs = 5000;

    private void RefreshInventory()
    {
        if (Interlocked.Exchange(ref _inventoryRunning, 1) == 1) return;
        try
        {
            var (total, user, system) = CountProcesses();
            // I servizi cambiano di rado: bastano dieci secondi fra due letture.
            var services = ++_inventoryTick % 5 == 1 ? CountRunningServices() : _inventory.Services;
            _inventory = new Inventory(total, user, system, services);
        }
        catch (Exception) { /* un inventario mancato non deve fermare il monitor */ }
        finally { Interlocked.Exchange(ref _inventoryRunning, 0); }
    }

    public void Start()
    {
        Warmup();
        _ = Task.Run(RefreshInventory);
        _timer.Start();
        _inventoryTimer.Start();
    }

    /// <summary>Sospende il polling (finestra minimizzata/tray; requisito di leggerezza).</summary>
    public void Pause()
    {
        // Risincronizza i differenziali di rete per evitare picchi fittizi alla ripresa.
        PrimeNetwork();
        _timer.Stop();
        _inventoryTimer.Stop();
    }

    public void Resume()
    {
        PrimeNetwork();
        _timer.Start();
        _inventoryTimer.Start();
    }

    private void CreateCounters()
    {
        _cpuCounter = TryCreate("Processor", "% Processor Time", "_Total");
        _diskIdleCounter = TryCreate("PhysicalDisk", "% Idle Time", "_Total");
        _diskBytesCounter = TryCreate("PhysicalDisk", "Disk Bytes/sec", "_Total");
        _diskReadCounter = TryCreate("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
        _diskWriteCounter = TryCreate("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
        _cpuPerformanceCounter = TryCreate("Processor Information", "% Processor Performance", "_Total");
        _memoryCacheCounter = TryCreate("Memory", "Cache Bytes", instance: null);
        _thermalCounters = CreateCategoryCounters("Thermal Zone Information", "Temperature", _ => true);

        // GPU: una sola query PDH con jolly. La categoria "GPU Engine" ha
        // un'istanza per processo/motore (oltre un migliaio su un PC normale):
        // aprirle una a una costerebbe più della metrica che si vuole misurare.
        _gpuUtilization = PdhWildcardCounter.Create(@"\GPU Engine(*engtype_3D)\Utilization Percentage");
        _gpuMemory = PdhWildcardCounter.Create(@"\GPU Adapter Memory(*)\Dedicated Usage");

        // Temperatura, frequenze, ventola e consumo GPU non passano da PDH: sulle
        // schede NVIDIA arrivano da NVML, la libreria installata con il driver.
        _nvidia = NvidiaGpuTelemetry.TryCreate();
    }

    private static PerformanceCounter? TryCreate(string category, string counter, string? instance)
    {
        try
        {
            return instance is null
                ? new PerformanceCounter(category, counter, readOnly: true)
                : new PerformanceCounter(category, counter, instance, readOnly: true);
        }
        catch (Exception) { return null; } // categoria assente: metrica dichiarata non disponibile
    }

    /// <summary>
    /// Crea un contatore per ogni istanza selezionata di una categoria multi-istanza.
    /// Usato da GPU e zone termiche, dove il totale è la somma delle istanze attive.
    /// </summary>
    private static List<PerformanceCounter> CreateCategoryCounters(
        string categoryName, string counterName, Func<string, bool> instanceFilter)
    {
        var result = new List<PerformanceCounter>();
        try
        {
            if (!PerformanceCounterCategory.Exists(categoryName)) return result;
            var category = new PerformanceCounterCategory(categoryName);
            foreach (var instance in category.GetInstanceNames())
            {
                if (!instanceFilter(instance)) continue;
                try { result.Add(new PerformanceCounter(categoryName, counterName, instance, readOnly: true)); }
                catch (Exception) { /* istanza sparita tra enumerazione e apertura */ }
            }
        }
        catch (Exception) { /* categoria non disponibile su questa macchina */ }
        return result;
    }

    private void PrimeNetwork()
    {
        (_lastRecvBytes, _lastSentBytes) = SumNetworkBytes();
        _primedNet = true;
    }

    private static (long recv, long sent) SumNetworkBytes()
    {
        long recv = 0, sent = 0;
        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            var s = ni.GetIPv4Statistics();
            recv += s.BytesReceived;
            sent += s.BytesSent;
        }
        return (recv, sent);
    }

    private void Warmup()
    {
        // Prima lettura dei tempi di sistema: la successiva avrà un delta valido.
        ReadCpuPercent();

        // PDH richiede due letture distanti nel tempo: la prima viene scartata.
        foreach (var c in new[] { _cpuCounter, _diskIdleCounter, _diskBytesCounter, _diskReadCounter,
                                  _diskWriteCounter, _cpuPerformanceCounter, _memoryCacheCounter })
        {
            try { c?.NextValue(); } catch (Exception) { /* tollerato */ }
        }
    }

    /// <summary>Core/thread e frequenza nominale: dati statici, letti una sola volta.</summary>
    private void ReadStaticCpuFacts()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (var mo in searcher.Get().Cast<ManagementBaseObject>().Take(1))
            {
                if (mo["MaxClockSpeed"] is not null) _maxClockMhz = Convert.ToDouble(mo["MaxClockSpeed"], CultureInfo.InvariantCulture);
                if (mo["NumberOfCores"] is not null) _cores = Convert.ToInt32(mo["NumberOfCores"], CultureInfo.InvariantCulture);
                if (mo["NumberOfLogicalProcessors"] is not null) _threads = Convert.ToInt32(mo["NumberOfLogicalProcessors"], CultureInfo.InvariantCulture);
            }
        }
        catch (Exception) { /* WMI non disponibile: i campi restano null */ }
        _threads ??= Environment.ProcessorCount;
    }

    private SystemSnapshot Sample()
    {
        _tick++;
        double? cpu = null, diskActive = null, diskKbs = null;

        cpu = ReadCpuPercent();
        if (cpu is null)
        {
            // Riserva: se GetSystemTimes non risponde si torna al contatore PDH.
            try { if (_cpuCounter != null) cpu = Math.Clamp(_cpuCounter.NextValue(), 0, 100); }
            catch (Exception) { }
        }

        try
        {
            if (_diskIdleCounter != null)
                diskActive = Math.Clamp(100.0 - _diskIdleCounter.NextValue(), 0, 100);
        }
        catch (Exception) { }

        try { if (_diskBytesCounter != null) diskKbs = Math.Max(0, _diskBytesCounter.NextValue() / 1024.0); }
        catch (Exception) { }

        double? diskRead = Read(_diskReadCounter);
        double? diskWrite = Read(_diskWriteCounter);
        double? cached = Read(_memoryCacheCounter);

        double? clock = null;
        var performance = Read(_cpuPerformanceCounter);
        if (performance is double perf && _maxClockMhz is double maxClock && maxClock > 0)
            clock = maxClock * perf / 100d;

        double? ramUsedPct = null, ramTotal = null, ramAvail = null;
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            ramTotal = mem.ullTotalPhys;
            ramAvail = mem.ullAvailPhys;
            ramUsedPct = mem.ullTotalPhys == 0
                ? null
                : 100.0 * (mem.ullTotalPhys - mem.ullAvailPhys) / mem.ullTotalPhys;
        }

        double? downKbs = null, upKbs = null;
        if (_primedNet)
        {
            var (recv, sent) = SumNetworkBytes();
            var secs = Math.Max(_netClock.Elapsed.TotalSeconds, 0.001);
            if (recv >= _lastRecvBytes) downKbs = (recv - _lastRecvBytes) / secs / 1024.0;
            if (sent >= _lastSentBytes) upKbs = (sent - _lastSentBytes) / secs / 1024.0;
            _lastRecvBytes = recv;
            _lastSentBytes = sent;
            _netClock.Restart();
        }

        // Conteggi già pronti: qui si legge soltanto l'ultimo inventario.
        var inventory = _inventory;

        var (gpuLoad, gpuMemory) = SampleGpu();
        var nvidia = _nvidia?.Read();
        var temperature = SampleTemperature();

        var networkUp = false;
        try { networkUp = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); }
        catch (Exception) { }

        return new SystemSnapshot(cpu, ramUsedPct, ramTotal, ramAvail, diskActive, diskKbs,
                                  downKbs, upKbs, inventory.Total,
                                  TimeSpan.FromMilliseconds(Environment.TickCount64))
        {
            CpuTemperatureCelsius = temperature,
            CpuClockMhz = clock,
            CpuCores = _cores,
            CpuThreads = _threads,
            RamCachedBytes = cached,
            GpuPercent = gpuLoad ?? nvidia?.UtilizationPercent,
            // NVML misura la memoria della scheda: quando c'è si usa quella per
            // entrambi i valori, così usato e totale restano coerenti fra loro.
            GpuMemoryUsedBytes = nvidia?.MemoryUsedBytes ?? gpuMemory,
            GpuMemoryTotalBytes = nvidia?.MemoryTotalBytes,
            GpuTemperatureCelsius = nvidia?.TemperatureCelsius,
            GpuClockMhz = nvidia?.CoreClockMhz,
            GpuMemoryClockMhz = nvidia?.MemoryClockMhz,
            GpuFanPercent = nvidia?.FanPercent,
            GpuPowerWatts = nvidia?.PowerWatts,
            DiskReadBytesPerSecond = diskRead,
            DiskWriteBytesPerSecond = diskWrite,
            NetworkAvailable = networkUp,
            UserProcessCount = inventory.User,
            SystemProcessCount = inventory.System,
            ServiceCount = inventory.Services,
        };
    }

    /// <summary>
    /// Utilizzo CPU dalla differenza dei tempi di sistema. Se fra due letture non
    /// è trascorso tempo misurabile si ripete l'ultimo valore invece di dichiarare
    /// 0%: un salto a zero sarebbe una misura falsa, non un PC improvvisamente fermo.
    /// </summary>
    private double? ReadCpuPercent()
    {
        try
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
            if (!_cpuPrimed)
            {
                (_prevIdleTime, _prevKernelTime, _prevUserTime, _cpuPrimed) = (idle, kernel, user, true);
                return null;
            }

            var idleDelta = idle - _prevIdleTime;
            // Il tempo "kernel" include già quello di inattività: il totale è kernel + user.
            var totalDelta = (kernel - _prevKernelTime) + (user - _prevUserTime);
            (_prevIdleTime, _prevKernelTime, _prevUserTime) = (idle, kernel, user);

            if (totalDelta <= 0) return _lastCpuPercent;
            _lastCpuPercent = Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
            return _lastCpuPercent;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double? Read(PerformanceCounter? counter)
    {
        try { return counter is null ? null : Math.Max(0, counter.NextValue()); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Somma dell'utilizzo dei motori 3D: è la stessa base mostrata dal Task
    /// Manager. Il valore viene limitato a 100% perché più motori attivi in
    /// parallelo possono superare il 100 sommati fra loro.
    /// </summary>
    private (double? Load, double? Memory) SampleGpu()
    {
        var load = _gpuUtilization?.ReadSum();
        var memory = _gpuMemory?.ReadSum();
        return (load is double value ? Math.Clamp(value, 0, 100) : null, memory);
    }

    /// <summary>Kelvin → Celsius; si prende la zona più calda tra quelle esposte dall'ACPI.</summary>
    private double? SampleTemperature()
    {
        if (_thermalCounters.Count == 0) return null;
        double? hottest = null;
        foreach (var counter in _thermalCounters)
        {
            try
            {
                var kelvin = counter.NextValue();
                if (kelvin < 200 || kelvin > 400) continue; // valore fuori scala: firmware non affidabile
                var celsius = kelvin - 273.15;
                if (hottest is null || celsius > hottest) hottest = celsius;
            }
            catch (Exception) { }
        }
        return hottest;
    }

    private (int Total, int User, int System) CountProcesses()
    {
        var total = 0;
        var user = 0;
        var system = 0;
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    total++;
                    try
                    {
                        if (process.SessionId == _sessionId) user++;
                        else system++;
                    }
                    catch (Exception) { system++; }
                }
            }
        }
        catch (Exception) { }
        return (total, user, system);
    }

    private static int? CountRunningServices()
    {
        try
        {
            var services = ServiceController.GetServices();
            var running = services.Count(s =>
            {
                try { return s.Status == ServiceControllerStatus.Running; }
                catch (Exception) { return false; }
                finally { s.Dispose(); }
            });
            return running;
        }
        catch (Exception) { return null; }
    }

    private static int ReadSessionId()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.SessionId;
        }
        catch (Exception) { return 1; }
    }

    private void Push(SystemSnapshot s)
    {
        PushRing(_cpuRing, s.CpuPercent ?? 0);
        PushRing(_ramRing, s.RamUsedPercent ?? 0);
        PushRing(_diskRing, s.DiskActivePercent ?? 0);
        PushRing(_downRing, s.NetDownKBytesPerSecond ?? 0);
        PushRing(_upRing, s.NetUpKBytesPerSecond ?? 0);
    }

    private static void PushRing(List<double> ring, double value)
    {
        ring.Add(value);
        while (ring.Count > RingCapacity) ring.RemoveAt(0);
    }

    private static void DisposeAll(List<PerformanceCounter> counters)
    {
        foreach (var counter in counters)
        {
            try { counter.Dispose(); } catch (Exception) { }
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _inventoryTimer.Stop();
        _inventoryTimer.Dispose();
        foreach (var c in new[] { _cpuCounter, _diskIdleCounter, _diskBytesCounter, _diskReadCounter,
                                  _diskWriteCounter, _cpuPerformanceCounter, _memoryCacheCounter })
            c?.Dispose();
        _gpuUtilization?.Dispose();
        _gpuMemory?.Dispose();
        _nvidia?.Dispose();
        DisposeAll(_thermalCounters);
    }
}
