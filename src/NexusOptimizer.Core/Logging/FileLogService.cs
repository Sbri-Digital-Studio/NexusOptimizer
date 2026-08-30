namespace NexusOptimizer.Core.Logging;

public enum LogLevel { Debug = 0, Info = 1, Warning = 2, Error = 3 }

/// <summary>Logger thread-safe su file con purge dei log vecchi.</summary>
public sealed class FileLogService : IDisposable
{
    /// <summary>
    /// Tetto per file di log. Un errore ripetuto (es. un binding rotto che si
    /// ripresenta a ogni frame) non deve poter riempire il disco dell'utente:
    /// oltre questa soglia il file viene ruotato e la copia precedente sostituita.
    /// </summary>
    private const long MaxLogBytes = 8L * 1024 * 1024;

    private readonly string _logDirectory;
    private readonly object _writeLock = new();
    private LogLevel _minLevel;
    private StreamWriter? _writer;
    private string? _currentPath;
    private long _writtenBytes;

    public FileLogService(string logDirectory, LogLevel minLevel = LogLevel.Info)
    {
        _logDirectory = logDirectory;
        _minLevel = minLevel;
        Directory.CreateDirectory(_logDirectory);
        OpenWriter();
    }

    public void SetLevel(LogLevel level) => _minLevel = level;

    public void Debug(string message) => Write(LogLevel.Debug, message);
    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warning(string message) => Write(LogLevel.Warning, message);
    public void Error(string message, Exception? ex = null)
        => Write(LogLevel.Error, ex is null ? message : $"{message} :: {ex}");

    private void Write(LogLevel level, string message)
    {
        if (level < _minLevel) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {message}";
        lock (_writeLock)
        {
            try
            {
                _writer?.WriteLine(line);
                _writtenBytes += line.Length + Environment.NewLine.Length;
                if (_writtenBytes >= MaxLogBytes) Roll();
            }
            catch (IOException) { /* l'app continua */ }
        }
    }

    /// <summary>Elimina i log (tutti se days &lt;= 0). Ritorna il numero di file rimossi.</summary>
    public int Purge(int olderThanDays)
    {
        var removed = 0;
        try
        {
            lock (_writeLock)
            {
                _writer?.Dispose();
                _writer = null;
            }
            foreach (var f in Directory.GetFiles(_logDirectory, "*.log"))
            {
                if (olderThanDays <= 0 || File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-olderThanDays))
                {
                    File.Delete(f);
                    removed++;
                }
            }
        }
        catch (Exception) { /* best effort */ }
        finally
        {
            lock (_writeLock)
            {
                if (_writer is null) OpenWriter();
            }
        }
        return removed;
    }

    /// <summary>Ruota il file corrente conservando una sola copia precedente.</summary>
    private void Roll()
    {
        var path = _currentPath;
        try
        {
            _writer?.Dispose();
            _writer = null;
            if (path is not null)
            {
                var previous = Path.ChangeExtension(path, ".1.log");
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(path, previous);
            }
        }
        catch (Exception) { /* la rotazione non deve mai fermare l'applicazione */ }
        finally { OpenWriter(); }
    }

    private void OpenWriter()
    {
        var baseName = $"nexus-optimizer-{DateTime.Now:yyyy-MM-dd}";
        try
        {
            var path = Path.Combine(_logDirectory, baseName + ".log");
            _writer = new StreamWriter(path, append: true) { AutoFlush = true };
            _currentPath = path;
            _writtenBytes = FileLength(path);
        }
        catch (IOException)
        {
            // Due copie dell'app possono essere aperte per errore. Non perdiamo
            // il log della seconda: usiamo un file per processo invece di tacere.
            try
            {
                var path = Path.Combine(_logDirectory, $"{baseName}-{Environment.ProcessId}.log");
                _writer = new StreamWriter(path, append: true) { AutoFlush = true };
                _currentPath = path;
                _writtenBytes = FileLength(path);
            }
            catch (IOException) { _writer = null; _currentPath = null; }
        }
    }

    private static long FileLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (Exception) { return 0; }
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
