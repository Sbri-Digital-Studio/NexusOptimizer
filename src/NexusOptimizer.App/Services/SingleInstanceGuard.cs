namespace NexusOptimizer.App.Services;

/// <summary>
/// Una sola copia in esecuzione per sessione utente. Senza questo vincolo due
/// finestre scriverebbero lo stesso config.json e monitorerebbero lo stesso PC
/// due volte: la seconda copia si limita quindi a riportare in primo piano
/// quella gia' aperta e a chiudersi.
///
/// Gli oggetti di sincronizzazione hanno prefisso "Local\\": restano confinati
/// alla sessione dell'utente, senza toccare le altre sessioni della macchina.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\NexusOptimizer.Instance";
    private const string SignalName = @"Local\NexusOptimizer.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle signal)
    {
        _mutex = mutex;
        _signal = signal;
    }

    /// <summary>
    /// Restituisce il presidio quando questa e' la prima copia, altrimenti null.
    /// In caso di errore imprevisto si preferisce lasciar partire l'applicazione:
    /// un problema di sincronizzazione non deve impedire l'uso del programma.
    /// </summary>
    public static SingleInstanceGuard? Acquire()
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                mutex.Dispose();
                return null;
            }

            var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            return new SingleInstanceGuard(mutex, signal);
        }
        catch (Exception)
        {
            mutex?.Dispose();
            return null;
        }
    }

    /// <summary>Sveglia la copia gia' in esecuzione. False se non risponde.</summary>
    public static bool ActivateExistingInstance()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(SignalName, out var handle)) return false;
            using (handle) return handle.Set();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Attende in background la richiesta di attivazione inviata da un secondo
    /// avvio. Il callback viene invocato su un thread di lavoro: il chiamante
    /// effettua il marshalling verso l'interfaccia.
    /// </summary>
    public void ListenForActivation(Action onActivate)
    {
        ArgumentNullException.ThrowIfNull(onActivate);
        var token = _stop.Token;
        _ = Task.Factory.StartNew(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Attesa con scadenza: permette di chiudere il thread all'uscita.
                    if (_signal.WaitOne(TimeSpan.FromMilliseconds(500))) onActivate();
                }
                catch (Exception)
                {
                    return; // handle chiuso durante la chiusura dell'applicazione
                }
            }
        }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Dispose()
    {
        try
        {
            _stop.Cancel();
            _signal.Dispose();
            _mutex.ReleaseMutex();
        }
        catch (Exception)
        {
            /* chiusura: nessun errore di sincronizzazione deve emergere qui */
        }
        finally
        {
            _mutex.Dispose();
            _stop.Dispose();
        }
    }
}
