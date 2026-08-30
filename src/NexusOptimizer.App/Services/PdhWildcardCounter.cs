using System.Runtime.InteropServices;

namespace NexusOptimizer.App.Services;

/// <summary>
/// Contatore PDH con percorso jolly, letto in una sola chiamata nativa.
///
/// Serve per le categorie molto popolate: "GPU Engine" espone un'istanza per
/// processo/motore (oltre un migliaio su un PC normale) e aprirne una per ciascuna
/// con PerformanceCounter costerebbe più della metrica stessa. Qui la query resta
/// una, il filtro lo applica PDH e il risultato è la somma reale delle istanze.
///
/// Usa PdhAddEnglishCounter: i nomi dei contatori restano quelli inglesi anche su
/// Windows localizzato, quindi la lettura funziona su qualsiasi lingua di sistema.
/// </summary>
internal sealed class PdhWildcardCounter : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhCstatusValidData = 0x00000000;
    private const uint PdhCstatusNewData = 0x00000001;

    private IntPtr _query;
    private IntPtr _counter;
    private byte[] _buffer = [];

    private PdhWildcardCounter(IntPtr query, IntPtr counter)
    {
        _query = query;
        _counter = counter;
    }

    /// <summary>Apre la query; ritorna null se il contatore non esiste su questa macchina.</summary>
    public static PdhWildcardCounter? Create(string englishPath)
    {
        var query = IntPtr.Zero;
        try
        {
            if (PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out query) != 0) return null;
            if (PdhAddEnglishCounter(query, englishPath, IntPtr.Zero, out var counter) != 0)
            {
                CloseQuery(query);
                return null;
            }

            // I contatori di tipo rate richiedono due raccolte: la prima è il
            // riferimento e viene scartata, esattamente come per PerformanceCounter.
            _ = PdhCollectQueryData(query);
            return new PdhWildcardCounter(query, counter);
        }
        catch (DllNotFoundException) { CloseQuery(query); return null; }
        catch (EntryPointNotFoundException) { CloseQuery(query); return null; }
    }

    /// <summary>Somma delle istanze correnti; null se la lettura non è disponibile.</summary>
    public double? ReadSum()
    {
        if (_query == IntPtr.Zero) return null;
        try
        {
            if (PdhCollectQueryData(_query) != 0) return null;

            var size = (uint)_buffer.Length;
            var status = _buffer.Length == 0
                ? PdhMoreData
                : PdhGetFormattedCounterArray(_counter, PdhFmtDouble, ref size, out _, _buffer);

            if (status == PdhMoreData)
            {
                // Il numero di istanze cambia nel tempo: si rialloca solo quando serve.
                _buffer = new byte[Math.Max(size, 1024)];
                size = (uint)_buffer.Length;
                status = PdhGetFormattedCounterArray(_counter, PdhFmtDouble, ref size, out _, _buffer);
            }
            if (status != 0) return null;

            var itemSize = Marshal.SizeOf<PdhFmtCounterValueItemDouble>();
            var handle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            try
            {
                var basePointer = handle.AddrOfPinnedObject();
                var count = (int)(size / itemSize);
                double total = 0;
                var read = false;
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<PdhFmtCounterValueItemDouble>(basePointer + i * itemSize);
                    if (item.Value.CStatus is not (PdhCstatusValidData or PdhCstatusNewData)) continue;
                    if (!double.IsFinite(item.Value.DoubleValue)) continue;
                    total += item.Value.DoubleValue;
                    read = true;
                }
                return read ? total : null;
            }
            finally { handle.Free(); }
        }
        catch (Exception) { return null; }
    }

    public void Dispose()
    {
        if (_query == IntPtr.Zero) return;
        CloseQuery(_query);
        _query = IntPtr.Zero;
        _counter = IntPtr.Zero;
        _buffer = [];
    }

    /// <summary>Chiusura tollerante: l'esito non cambia il comportamento dell'app.</summary>
    private static void CloseQuery(IntPtr query)
    {
        if (query == IntPtr.Zero) return;
        try { _ = PdhCloseQuery(query); }
        catch (Exception) { /* chiusura best effort */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueDouble
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItemDouble
    {
        public IntPtr Name;
        public PdhFmtCounterValueDouble Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = false)]
    private static extern uint PdhOpenQuery(IntPtr dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath,
        IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format,
        ref uint bufferSize, out uint itemCount, byte[] itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
