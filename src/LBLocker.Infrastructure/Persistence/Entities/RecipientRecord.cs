namespace LBLocker.Infrastructure.Persistence.Entities;

/// <summary>
/// Contact details for one concrete parcel. The external recipient ID is used
/// as the stable key because the source system supplies it on each import.
/// Personal fields are cleared by the retention process; the non-personal key
/// remains to preserve referential integrity.
/// </summary>
public sealed class RecipientRecord
{
    public string ExternalRecipientId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? PostalCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PersonalDataErasedAt { get; set; }
}