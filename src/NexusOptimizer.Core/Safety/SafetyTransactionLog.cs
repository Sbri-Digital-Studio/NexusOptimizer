using System.Text.Json;

namespace NexusOptimizer.Core.Safety;

/// <summary>
/// Registro atomico delle operazioni della Safety Engine. Il file conserva solo
/// date, categorie e contatori; i percorsi necessari all'undo sono cifrati a parte.
/// </summary>
internal sealed class SafetyTransactionLog
{
    private readonly string _path;
    private readonly object _sync = new();
    private List<SafetyOperationRecord>? _records;

    public SafetyTransactionLog(string path) => _path = path;

    public IReadOnlyList<SafetyOperationRecord> GetAll()
    {
        lock (_sync)
        {
            EnsureLoaded();
            return _records!
                .OrderByDescending(record => record.StartedUtc)
                .Select(Clone)
                .ToArray();
        }
    }

    public SafetyOperationRecord Begin(IEnumerable<string> categories)
    {
        lock (_sync)
        {
            EnsureLoaded();
            var record = new SafetyOperationRecord
            {
                Id = Guid.NewGuid(),
                StartedUtc = DateTime.UtcNow,
                Status = SafetyOperationStatus.InProgress,
                Categories = categories.Distinct(StringComparer.Ordinal).ToList(),
            };
            _records!.Add(record);
            Save();
            return Clone(record);
        }
    }

    public void RecordCapture(Guid id, long bytes)
    {
        lock (_sync)
        {
            var record = Find(id);
            record.ItemsQuarantined++;
            record.BytesQuarantined += Math.Max(0, bytes);
            Save();
        }
    }

    public void DiscardCapture(Guid id, long bytes)
    {
        lock (_sync)
        {
            var record = Find(id);
            record.ItemsQuarantined = Math.Max(0, record.ItemsQuarantined - 1);
            record.BytesQuarantined = Math.Max(0, record.BytesQuarantined - Math.Max(0, bytes));
            Save();
        }
    }

    public void Complete(Guid id, bool hadErrors)
    {
        lock (_sync)
        {
            var record = Find(id);
            record.Status = hadErrors ? SafetyOperationStatus.CompletedWithErrors : SafetyOperationStatus.Completed;
            record.CompletedUtc = DateTime.UtcNow;
            Save();
        }
    }

    public void MarkRestored(Guid id, int count, bool fullyRestored)
    {
        lock (_sync)
        {
            var record = Find(id);
            record.ItemsRestored = Math.Min(record.ItemsQuarantined, record.ItemsRestored + Math.Max(0, count));
            if (fullyRestored)
            {
                record.Status = SafetyOperationStatus.Undone;
                record.CompletedUtc ??= DateTime.UtcNow;
            }
            Save();
        }
    }

    public void MarkExpired(Guid id)
    {
        lock (_sync)
        {
            var record = Find(id);
            record.Status = SafetyOperationStatus.Expired;
            Save();
        }
    }

    private SafetyOperationRecord Find(Guid id)
    {
        EnsureLoaded();
        return _records!.FirstOrDefault(record => record.Id == id)
            ?? throw new InvalidOperationException("Operazione di sicurezza non trovata.");
    }

    private void EnsureLoaded()
    {
        if (_records is not null) return;
        try
        {
            if (File.Exists(_path))
                _records = JsonSerializer.Deserialize<List<SafetyOperationRecord>>(File.ReadAllText(_path)) ?? [];
            else
                _records = [];
        }
        catch (JsonException)
        {
            // Un registro danneggiato non deve mai autorizzare una cancellazione.
            // Lo isoliamo e ricominciamo con uno vuoto, mantenendo il file per diagnosi.
            try { File.Move(_path, _path + ".corrupt-" + DateTime.UtcNow.Ticks, overwrite: false); }
            catch (IOException) { }
            _records = [];
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_records, WriteOptions));
        File.Move(tmp, _path, overwrite: true);
    }

    private static SafetyOperationRecord Clone(SafetyOperationRecord record) => new()
    {
        Id = record.Id,
        StartedUtc = record.StartedUtc,
        CompletedUtc = record.CompletedUtc,
        Status = record.Status,
        Categories = [.. record.Categories],
        ItemsQuarantined = record.ItemsQuarantined,
        ItemsRestored = record.ItemsRestored,
        BytesQuarantined = record.BytesQuarantined,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
}
