namespace LBLocker.Infrastructure.Persistence.Entities;

public enum ProcessStep
{
    GeneratePin,
    AllocateCompartment,
    SendSms,
    SendEmail,
    ReleaseCompartment
}

public enum ProcessAttemptStatus
{
    Started,
    Succeeded,
    Failed
}

/// <summary>
/// Diagnostic record for one attempt at one operational process step.
/// Never store PINs, recipient contact details or message bodies here.
/// </summary>
public sealed class ProcessAttemptRecord
{
    public Guid Id { get; set; }
    public Guid ParcelId { get; set; }
    public Guid CorrelationId { get; set; }
    public ProcessStep Step { get; set; }
    public int AttemptNumber { get; set; }
    public ProcessAttemptStatus Status { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponseBody { get; set; }
}
