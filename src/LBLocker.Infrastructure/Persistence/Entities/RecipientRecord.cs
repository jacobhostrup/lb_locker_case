namespace LBLocker.Infrastructure.Persistence.Entities;

/// <summary>
/// Contact details belong to one concrete parcel, not to a reusable person
/// profile. Personal fields are cleared by the retention process while the
/// non-personal row identity preserves referential integrity.
/// </summary>
public sealed class RecipientRecord
{
    public Guid ParcelId { get; set; }
    public string ExternalRecipientId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? PostalCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PersonalDataErasedAt { get; set; }
}