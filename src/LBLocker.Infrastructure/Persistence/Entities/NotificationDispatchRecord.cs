namespace LBLocker.Infrastructure.Persistence.Entities;

public enum NotificationChannel
{
    Sms,
    Email
}

public enum NotificationDispatchStatus
{
    Accepted,
    Rejected,
    Failed
}

/// <summary>
/// Stores delivery-provider metadata only. Do not persist recipient addresses,
/// phone numbers, message bodies or PIN codes in this record.
/// </summary>
public sealed class NotificationDispatchRecord
{
    public Guid Id { get; set; }
    public Guid AllocationId { get; set; }
    public NotificationChannel Channel { get; set; }
    public NotificationDispatchStatus Status { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public string? ExternalDispatchId { get; set; }
    public string? ErrorCode { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponseBody { get; set; }
}