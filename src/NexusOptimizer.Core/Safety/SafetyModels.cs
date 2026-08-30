using System.Text.Json.Serialization;

namespace NexusOptimizer.Core.Safety;

/// <summary>Stato persistito di un'operazione che ha modificato file locali.</summary>
public enum SafetyOperationStatus
{
    InProgress,
    Completed,
    CompletedWithErrors,
    Undone,
    Expired,
}

/// <summary>
/// Registro locale di un'operazione. Non contiene mai percorsi o nomi di file:
/// quei dati rimangono cifrati nella quarantena e servono esclusivamente al ripristino.
/// </summary>
public sealed class SafetyOperationRecord
{
    public Guid Id { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public SafetyOperationStatus Status { get; set; }
    public List<string> Categories { get; set; } = [];
    public int ItemsQuarantined { get; set; }
    public int ItemsRestored { get; set; }
    public long BytesQuarantined { get; set; }

    [JsonIgnore]
    public bool CanUndo => (Status is SafetyOperationStatus.InProgress
        or SafetyOperationStatus.Completed
        or SafetyOperationStatus.CompletedWithErrors)
        && ItemsQuarantined > ItemsRestored;
}

/// <summary>Riepilogo di un ripristino, senza esporre percorsi nel registro.</summary>
public sealed class RestoreResult
{
    public int RestoredItems { get; set; }
    public int SkippedItems { get; set; }
    public List<string> Errors { get; } = [];
}

internal sealed class QuarantineItemMetadata
{
    public string OriginalPath { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long OriginalSizeBytes { get; set; }
    public DateTime OriginalLastWriteUtc { get; set; }
}

internal sealed record QuarantineCapture(Guid ItemId, long StoredBytes);
