namespace NexusOptimizer.Core.Notifications;

/// <summary>
/// Raccolta in memoria degli avvisi mostrati nella campanella. Non persiste nulla
/// su disco: un avviso descrive una condizione del momento e resta valido finché
/// l'applicazione è aperta. Thread-safe: le regole vengono valutate su timer e
/// sul thread di campionamento del monitor.
/// </summary>
public sealed class NotificationCenter
{
    /// <summary>Tetto della cronologia: oltre questo numero le voci più vecchie escono.</summary>
    public const int MaxItems = 50;

    /// <summary>
    /// Finestra di sicurezza contro i doppioni: la stessa regola non viene
    /// ripubblicata entro questo intervallo anche se il chiamante insiste.
    /// </summary>
    public static readonly TimeSpan DeduplicationWindow = TimeSpan.FromMinutes(5);

    private readonly List<NotificationRecord> _items = [];
    private readonly object _lock = new();

    /// <summary>Nuovo avviso accettato (già inserito nella lista).</summary>
    public event Action<NotificationRecord>? Published;

    /// <summary>La lista o lo stato di lettura sono cambiati.</summary>
    public event Action? Changed;

    /// <summary>Copia immutabile, dal più recente al più vecchio.</summary>
    public IReadOnlyList<NotificationRecord> Items
    {
        get { lock (_lock) return [.. _items]; }
    }

    public int Count
    {
        get { lock (_lock) return _items.Count; }
    }

    public int UnreadCount
    {
        get { lock (_lock) return _items.Count(item => !item.IsRead); }
    }

    /// <summary>
    /// Registra un avviso. Restituisce false quando viene scartato come doppione:
    /// il chiamante può così evitare anche il fumetto della tray.
    /// </summary>
    public bool Publish(NotificationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_lock)
        {
            var duplicate = _items.FirstOrDefault(item =>
                string.Equals(item.Key, record.Key, StringComparison.Ordinal)
                && record.CreatedUtc - item.CreatedUtc < DeduplicationWindow);
            if (duplicate is not null) return false;

            _items.Insert(0, record);
            while (_items.Count > MaxItems) _items.RemoveAt(_items.Count - 1);
        }
        Published?.Invoke(record);
        Changed?.Invoke();
        return true;
    }

    public void MarkAllRead()
    {
        var changed = false;
        lock (_lock)
        {
            foreach (var item in _items.Where(item => !item.IsRead))
            {
                item.IsRead = true;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_items.Count == 0) return;
            _items.Clear();
        }
        Changed?.Invoke();
    }
}
