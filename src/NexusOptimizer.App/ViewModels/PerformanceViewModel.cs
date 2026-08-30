using System.Windows.Threading;
using NexusOptimizer.App.Services;

namespace NexusOptimizer.App.ViewModels;

/// <summary>Opzione intervallo dei grafici.</summary>
public sealed record PerfRange(string Id, string LabelKey);

/// <summary>
/// Grafici storici basati SOLO sui campioni reali del SystemMonitor.
/// Le finestre lunghe usano medie a blocchi, senza interpolazioni o valori stimati.
/// </summary>
public sealed class PerformanceViewModel : ObservableBase, IDisposable
{
    public IReadOnlyList<PerfRange> Ranges { get; } =
    [
        new("30s", "perf.range.30s"),
        new("1m", "perf.range.1m"),
        new("5m", "perf.range.5m"),
        new("30m", "perf.range.30m"),
        new("session", "perf.range.session"),
    ];

    private const int RawCapacity = 7200; // due ore a un campione al secondo
    private readonly SystemMonitor _monitor;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly SeriesPointBuffer _cpu = new(RawCapacity);
    private readonly SeriesPointBuffer _ram = new(RawCapacity);
    private readonly SeriesPointBuffer _disk = new(RawCapacity);
    private readonly SeriesPointBuffer _net = new(RawCapacity); // byte/s
    private readonly Dictionary<(string range, string metric), SeriesPointBuffer> _view = [];
    private string _rangeId = "30s";
    private bool _paused;
    private double? _cpuCurrent;
    private double? _ramCurrent;
    private double? _diskCurrent;
    private double? _netCurrent;

    public PerformanceViewModel(SystemMonitor monitor)
    {
        _monitor = monitor;
        _monitor.Snapshot += OnSnapshot;
        _monitor.IntervalChanged += OnIntervalChanged;
        Locale.Changed += RaiseStaticLabels;
    }

    private void RaiseStaticLabels()
    {
        Raise(nameof(RangeOptions));
        Raise(nameof(NoteText));
        Raise(nameof(ScaleNote));
    }

    public IReadOnlyList<PerfRange> RangeOptions =>
        [.. Ranges.Select(r => new PerfRange(r.Id, r.LabelKey))];

    public string RangeLabel(PerfRange r) => Locale.T(r.LabelKey);

    public string RangeId
    {
        get => _rangeId;
        set
        {
            if (_rangeId == value || value is null) return;
            _rangeId = value;
            Raise();
            Raise(nameof(RangeWindowSeconds));
            RebindAll();
        }
    }

    /// <summary>Congela le serie mostrate, continuando a raccogliere campioni reali.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            Raise();
            if (!value) RebindAll();
        }
    }

    /// <summary>
    /// Campioni al secondo prodotti dal monitor: le finestre sono espresse in
    /// secondi, i buffer in campioni. Con una cadenza diversa da 1 Hz senza questa
    /// conversione l'asse dei tempi mentirebbe.
    /// </summary>
    private double SamplesPerSecond => Math.Max(0.1, 1000.0 / _monitor.IntervalMs);

    /// <summary>Finestra temporale usata per le tacche dell'asse X.</summary>
    public int RangeWindowSeconds => _rangeId == "session"
        ? (int)Math.Round(RawCapacity / SamplesPerSecond)
        : RangeSpecFor(_rangeId).WindowSeconds;

    public SeriesPointBuffer CpuSeries => ViewBuf(_cpu, "cpu");
    public SeriesPointBuffer RamSeries => ViewBuf(_ram, "ram");
    public SeriesPointBuffer DiskSeries => ViewBuf(_disk, "disk");
    public SeriesPointBuffer NetSeries => ViewBuf(_net, "net");

    /// <summary>Valori correnti mostrati nell'intestazione di ogni grafico.</summary>
    public string CpuCurrentText => Formatter.Percent(_cpuCurrent);
    public string RamCurrentText => Formatter.Percent(_ramCurrent);
    public string DiskCurrentText => Formatter.Percent(_diskCurrent);
    public string NetCurrentText => _netCurrent is double value
        ? Formatter.RatePerSec(value)
        : Formatter.Dash;
    public double? CpuCurrent => _cpuCurrent;
    public double? RamCurrent => _ramCurrent;
    public double? DiskCurrent => _diskCurrent;

    private SeriesPointBuffer ViewBuf(SeriesPointBuffer source, string metric)
    {
        var key = (_rangeId, metric);
        if (!_view.TryGetValue(key, out var buffer))
        {
            var spec = RangeSpecFor(_rangeId);
            buffer = new SeriesPointBuffer(PointsFor(spec));
            ReplaceView(buffer, source, spec, SamplesPerSecond);
            _view[key] = buffer;
        }
        return buffer;
    }

    private void OnSnapshot(SystemSnapshot snapshot)
    {
        BeginUi(() =>
        {
            _cpuCurrent = snapshot.CpuPercent;
            _ramCurrent = snapshot.RamUsedPercent;
            _diskCurrent = snapshot.DiskActivePercent;
            _netCurrent = snapshot.NetDownKBytesPerSecond;
            Raise(nameof(CpuCurrentText));
            Raise(nameof(RamCurrentText));
            Raise(nameof(DiskCurrentText));
            Raise(nameof(NetCurrentText));
            Raise(nameof(CpuCurrent));
            Raise(nameof(RamCurrent));
            Raise(nameof(DiskCurrent));

            _cpu.Push(snapshot.CpuPercent ?? 0);
            _ram.Push(snapshot.RamUsedPercent ?? 0);
            _disk.Push(snapshot.DiskActivePercent ?? 0);
            // Il feed del monitor è in KB/s; i grafici conservano l'unità base byte/s
            // e la UI sceglie automaticamente KB/s, MB/s o GB/s.
            _net.Push(Math.Max(0, snapshot.NetDownKBytesPerSecond ?? 0) * 1024d);
            if (!_paused) RebindAll();
        });
    }

    /// <summary>
    /// Punti effettivamente mostrati per una finestra: dipende dalla cadenza, non
    /// dal numero fisso pensato per un campione al secondo.
    /// </summary>
    private int PointsFor(RangeSpec spec)
    {
        var rate = SamplesPerSecond;
        var windowSamples = Math.Max(1, (int)Math.Round(spec.WindowSeconds * rate));
        var bucketSamples = Math.Max(1, (int)Math.Round(spec.BucketSeconds * rate));
        return Math.Max(1, (int)Math.Ceiling(windowSamples / (double)bucketSamples));
    }

    /// <summary>
    /// Cambiando cadenza cambia quanti campioni entrano in una finestra: le viste
    /// memorizzate sono dimensionate per la vecchia e vanno ricostruite.
    /// </summary>
    private void OnIntervalChanged() => BeginUi(() =>
    {
        _view.Clear();
        Raise(nameof(RangeWindowSeconds));
        Raise(nameof(CpuSeries));
        Raise(nameof(RamSeries));
        Raise(nameof(DiskSeries));
        Raise(nameof(NetSeries));
    });

    private void RebindAll()
    {
        var rate = SamplesPerSecond;
        ReplaceView(ViewBuf(_cpu, "cpu"), _cpu, RangeSpecFor(_rangeId), rate);
        ReplaceView(ViewBuf(_ram, "ram"), _ram, RangeSpecFor(_rangeId), rate);
        ReplaceView(ViewBuf(_disk, "disk"), _disk, RangeSpecFor(_rangeId), rate);
        ReplaceView(ViewBuf(_net, "net"), _net, RangeSpecFor(_rangeId), rate);
    }

    private static void ReplaceView(SeriesPointBuffer target, SeriesPointBuffer source,
                                   RangeSpec spec, double samplesPerSecond)
    {
        target.Clear();
        var values = source.ToArray();
        // Secondi dichiarati dalla finestra -> campioni effettivamente raccolti.
        var windowSamples = Math.Max(1, (int)Math.Round(spec.WindowSeconds * samplesPerSecond));
        var bucketSamples = Math.Max(1, (int)Math.Round(spec.BucketSeconds * samplesPerSecond));
        var start = Math.Max(0, values.Length - windowSamples);
        var sliceLength = values.Length - start;
        if (sliceLength <= 0) return;

        if (bucketSamples <= 1)
        {
            for (var i = start; i < values.Length; i++) target.Push(values[i]);
            return;
        }

        for (var offset = start; offset < values.Length; offset += bucketSamples)
        {
            var count = Math.Min(bucketSamples, values.Length - offset);
            var sum = 0d;
            for (var i = 0; i < count; i++) sum += values[offset + i];
            target.Push(sum / count);
        }
    }

    private static RangeSpec RangeSpecFor(string id) => id switch
    {
        "1m" => new(60, 1, 60),
        "5m" => new(300, 5, 60),
        "30m" => new(1800, 30, 60),
        "session" => new(RawCapacity, 1, RawCapacity),
        _ => new(30, 1, 30),
    };

    private void BeginUi(Action action)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    public static string NoteText => Locale.T("perf.note");
    public static string ScaleNote => Locale.T("perf.cpu.max");

    public void Dispose()
    {
        _monitor.Snapshot -= OnSnapshot;
        _monitor.IntervalChanged -= OnIntervalChanged;
        Locale.Changed -= RaiseStaticLabels;
    }

    private readonly record struct RangeSpec(int WindowSeconds, int BucketSeconds, int Capacity);
}
