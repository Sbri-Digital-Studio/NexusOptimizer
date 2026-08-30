using System.Security.Cryptography;
using System.Text.Json;

namespace NexusOptimizer.Core.Safety;

/// <summary>
/// Quarantena locale cifrata: AES-GCM per i contenuti, chiave protetta con DPAPI
/// CurrentUser. I file sono cifrati a blocchi per non caricare cache grandi in RAM.
/// </summary>
internal sealed class EncryptedQuarantineStore
{
    private const int ChunkSize = 1024 * 1024;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int FormatMagic = 0x3151464E; // "NQF1"
    private const byte FormatVersion = 1;
    private readonly string _root;
    private readonly string _keyPath;
    private readonly IQuarantineKeyProtector _keyProtector;
    private readonly object _keySync = new();
    private byte[]? _key;

    public EncryptedQuarantineStore(string root, IQuarantineKeyProtector keyProtector)
    {
        _root = root;
        _keyPath = Path.Combine(root, "key.dpapi");
        _keyProtector = keyProtector;
        Directory.CreateDirectory(_root);
    }

    public void EnsureKeyAvailable() => _ = GetKey();

    public string OperationDirectory(Guid operationId) => Path.Combine(_root, operationId.ToString("N"));

    public long GetUsedBytes()
    {
        try
        {
            return Directory.Exists(_root)
                ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                    .Sum(path => new FileInfo(path).Length)
                : 0;
        }
        catch (Exception) { return long.MaxValue; }
    }

    public QuarantineCapture Capture(Guid operationId, string categoryId, string path, bool isDirectory,
        CancellationToken ct)
    {
        var itemId = Guid.NewGuid();
        var operationDirectory = OperationDirectory(operationId);
        Directory.CreateDirectory(operationDirectory);
        var dataPath = DataPath(operationId, itemId);
        var metaPath = MetadataPath(operationId, itemId);
        var tempDataPath = dataPath + ".tmp";
        var tempMetaPath = metaPath + ".tmp";

        try
        {
            var info = new FileInfo(path);
            var metadata = new QuarantineItemMetadata
            {
                OriginalPath = Path.GetFullPath(path),
                CategoryId = categoryId,
                IsDirectory = isDirectory,
                OriginalSizeBytes = isDirectory ? 0 : info.Length,
                OriginalLastWriteUtc = isDirectory ? Directory.GetLastWriteTimeUtc(path) : info.LastWriteTimeUtc,
            };

            if (!isDirectory)
                EncryptFile(path, tempDataPath, ct);
            else
                EncryptBytes([], tempDataPath);

            var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata);
            EncryptBytes(metadataBytes, tempMetaPath);

            File.Move(tempDataPath, dataPath, overwrite: false);
            File.Move(tempMetaPath, metaPath, overwrite: false);
            return new QuarantineCapture(itemId, new FileInfo(dataPath).Length + new FileInfo(metaPath).Length);
        }
        catch
        {
            TryDelete(tempDataPath);
            TryDelete(tempMetaPath);
            TryDelete(dataPath);
            TryDelete(metaPath);
            throw;
        }
    }

    public QuarantineItemMetadata ReadMetadata(Guid operationId, Guid itemId)
    {
        var plaintext = DecryptBytes(MetadataPath(operationId, itemId));
        return JsonSerializer.Deserialize<QuarantineItemMetadata>(plaintext)
            ?? throw new CryptographicException("Metadati della quarantena non validi.");
    }

    public IReadOnlyList<Guid> ListItemIds(Guid operationId)
    {
        var dir = OperationDirectory(operationId);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.meta")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(text => Guid.TryParseExact(text, "N", out _))
            .Select(text => Guid.ParseExact(text, "N"))
            .ToArray();
    }

    public void RestoreFile(Guid operationId, Guid itemId, string destination, CancellationToken ct)
    {
        var tempPath = destination + ".nexus-restore-" + itemId.ToString("N") + ".tmp";
        try
        {
            DecryptFile(DataPath(operationId, itemId), tempPath, ct);
            File.Move(tempPath, destination, overwrite: false);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public void DeleteItem(Guid operationId, Guid itemId)
    {
        TryDelete(DataPath(operationId, itemId));
        TryDelete(MetadataPath(operationId, itemId));
        TryDeleteEmptyDirectory(OperationDirectory(operationId));
    }

    public void DeleteOperation(Guid operationId)
    {
        var path = OperationDirectory(operationId);
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void EncryptFile(string source, string destination, CancellationToken ct)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: ChunkSize, FileOptions.SequentialScan);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatMagic);
        writer.Write(FormatVersion);
        writer.Write(input.Length);
        writer.Write(ChunkSize);

        var plain = new byte[ChunkSize];
        var cipher = new byte[ChunkSize];
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(GetKey(), TagSize);

        int read;
        while ((read = input.Read(plain, 0, plain.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            RandomNumberGenerator.Fill(nonce);
            aes.Encrypt(nonce, plain.AsSpan(0, read), cipher.AsSpan(0, read), tag);
            writer.Write(read);
            writer.Write(nonce);
            writer.Write(cipher, 0, read);
            writer.Write(tag);
        }
        writer.Write(0);
    }

    private void DecryptFile(string source, string destination, CancellationToken ct)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != FormatMagic || reader.ReadByte() != FormatVersion)
            throw new CryptographicException("Formato della quarantena non riconosciuto.");
        var originalLength = reader.ReadInt64();
        var storedChunkSize = reader.ReadInt32();
        if (originalLength < 0 || storedChunkSize is <= 0 or > ChunkSize)
            throw new CryptographicException("Intestazione della quarantena non valida.");

        var plain = new byte[storedChunkSize];
        var cipher = new byte[storedChunkSize];
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        long written = 0;
        using var aes = new AesGcm(GetKey(), TagSize);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var length = reader.ReadInt32();
            if (length == 0) break;
            if (length < 0 || length > storedChunkSize || written + length > originalLength)
                throw new CryptographicException("Blocco della quarantena non valido.");

            ReadExactly(reader, nonce, NonceSize);
            ReadExactly(reader, cipher, length);
            ReadExactly(reader, tag, TagSize);
            aes.Decrypt(nonce, cipher.AsSpan(0, length), tag, plain.AsSpan(0, length));
            output.Write(plain, 0, length);
            written += length;
        }

        if (written != originalLength || input.Position != input.Length)
            throw new CryptographicException("Contenuto della quarantena incompleto.");
    }

    private void EncryptBytes(byte[] input, string destination)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[input.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(GetKey(), TagSize);
        aes.Encrypt(nonce, input, cipher, tag);
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatMagic);
        writer.Write(FormatVersion);
        writer.Write(input.Length);
        writer.Write(nonce);
        writer.Write(cipher);
        writer.Write(tag);
    }

    private byte[] DecryptBytes(string source)
    {
        using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != FormatMagic || reader.ReadByte() != FormatVersion)
            throw new CryptographicException("Formato della quarantena non riconosciuto.");
        var length = reader.ReadInt32();
        if (length < 0 || length > 1024 * 1024)
            throw new CryptographicException("Metadati della quarantena non validi.");
        var nonce = reader.ReadBytes(NonceSize);
        var cipher = reader.ReadBytes(length);
        var tag = reader.ReadBytes(TagSize);
        if (nonce.Length != NonceSize || cipher.Length != length || tag.Length != TagSize || stream.Position != stream.Length)
            throw new CryptographicException("Metadati della quarantena incompleti.");
        var plain = new byte[length];
        using var aes = new AesGcm(GetKey(), TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private byte[] GetKey()
    {
        lock (_keySync)
        {
            if (_key is not null) return _key;
            if (File.Exists(_keyPath))
            {
                _key = _keyProtector.Unprotect(File.ReadAllBytes(_keyPath));
            }
            else
            {
                var key = RandomNumberGenerator.GetBytes(32);
                var protectedKey = _keyProtector.Protect(key);
                var tmp = _keyPath + ".tmp";
                File.WriteAllBytes(tmp, protectedKey);
                File.Move(tmp, _keyPath, overwrite: false);
                _key = key;
            }
            if (_key.Length != 32) throw new CryptographicException("Chiave della quarantena non valida.");
            return _key;
        }
    }

    private static void ReadExactly(BinaryReader reader, byte[] buffer, int length)
    {
        var read = reader.Read(buffer, 0, length);
        if (read != length) throw new EndOfStreamException();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try { if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string DataPath(Guid operationId, Guid itemId)
        => Path.Combine(OperationDirectory(operationId), itemId.ToString("N") + ".data");

    private string MetadataPath(Guid operationId, Guid itemId)
        => Path.Combine(OperationDirectory(operationId), itemId.ToString("N") + ".meta");
}
