namespace LBLocker.Infrastructure.Persistence.Entities;

public sealed class ParcelEventRecord
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public string EventType { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string? Description { get; set; }
    public Guid? CorrelationId { get; set; }
}