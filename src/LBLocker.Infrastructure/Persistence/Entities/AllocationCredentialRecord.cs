namespace LBLocker.Infrastructure.Persistence.Entities;

public sealed class AllocationCredentialRecord
{
    public Guid Id { get; set; }
    public Guid AllocationId { get; set; }
    public string EncryptedPin { get; set; } = null!;
    public string KeyVersion { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}