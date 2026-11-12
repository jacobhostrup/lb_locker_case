namespace LBLocker.Infrastructure.Persistence.Entities;

public enum ImportedEventStatus
{
    Processed,
    Rejected
}

public sealed class ImportedEventRecord
{
    public Guid Id { get; set; }
    public string EventId { get; set; } = null!;
    public DateTimeOffset ImportedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public ImportedEventStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public string? ParcelExternalId { get; set; }
}