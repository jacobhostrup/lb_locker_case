namespace LBLocker.Domain.Parcels;

/// <summary>
/// Represents a reservation of one compartment for one parcel.
/// The allocation has its own lifecycle, separate from the parcel lifecycle.
/// </summary>
public sealed class Allocation
{
    internal Allocation(
        Guid id,
        string lockerId,
        string compartmentId,
        DateTimeOffset allocatedAt,
        DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Allocation ID cannot be empty.", nameof(id));

        if (string.IsNullOrWhiteSpace(lockerId))
            throw new ArgumentException("Locker ID is required.", nameof(lockerId));

        if (string.IsNullOrWhiteSpace(compartmentId))
            throw new ArgumentException("Compartment ID is required.", nameof(compartmentId));

        if (expiresAt <= allocatedAt)
            throw new ArgumentException("Expiry must be later than allocation time.", nameof(expiresAt));

        Id = id;
        LockerId = lockerId;
        CompartmentId = compartmentId;
        AllocatedAt = allocatedAt;
        ExpiresAt = expiresAt;
        Status = AllocationStatus.Active;
    }

    public Guid Id { get; }
    public string LockerId { get; }
    public string CompartmentId { get; }
    public DateTimeOffset AllocatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public AllocationStatus Status { get; private set; }

    public void MarkExpired(DateTimeOffset occurredAt)
    {
        if (Status != AllocationStatus.Active)
            throw new InvalidOperationException("Only an active allocation can expire.");

        if (occurredAt < ExpiresAt)
            throw new InvalidOperationException("An allocation cannot expire before its expiry time.");

        Status = AllocationStatus.Expired;
    }

    public void MarkReleased(DateTimeOffset releasedAt)
    {
        if (Status == AllocationStatus.Released)
            throw new InvalidOperationException("Allocation has already been released.");

        if (releasedAt < AllocatedAt)
            throw new ArgumentException("Release time cannot be earlier than allocation time.", nameof(releasedAt));

        ReleasedAt = releasedAt;
        Status = AllocationStatus.Released;
    }
}
