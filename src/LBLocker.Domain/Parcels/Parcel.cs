namespace LBLocker.Domain.Parcels;

/// <summary>
/// A parcel represents one concrete shipment, not the physical item across repeated shipments.
/// </summary>
public sealed class Parcel
{
    private readonly List<Allocation> _allocations = [];

    private Parcel(
        Guid id,
        string externalParcelId,
        string recipientId,
        ParcelSize size,
        DateTimeOffset registeredAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Parcel ID cannot be empty.", nameof(id));

        if (string.IsNullOrWhiteSpace(externalParcelId))
            throw new ArgumentException("External parcel ID is required.", nameof(externalParcelId));

        if (string.IsNullOrWhiteSpace(recipientId))
            throw new ArgumentException("Recipient ID is required.", nameof(recipientId));

        Id = id;
        ExternalParcelId = externalParcelId;
        RecipientId = recipientId;
        Size = size;
        RegisteredAt = registeredAt;
        Status = ParcelStatus.Registered;
    }

    public Guid Id { get; }
    public string ExternalParcelId { get; }
    public string RecipientId { get; }
    public ParcelSize Size { get; private set; }
    public DateTimeOffset RegisteredAt { get; }
    public ParcelStatus Status { get; private set; }

    public IReadOnlyCollection<Allocation> Allocations => _allocations.AsReadOnly();

    public Allocation? CurrentAllocation =>
        _allocations.LastOrDefault(allocation => allocation.Status != AllocationStatus.Released);

    public static Parcel Register(
        string externalParcelId,
        string recipientId,
        ParcelSize size,
        DateTimeOffset registeredAt)
    {
        return new Parcel(
            Guid.NewGuid(),
            externalParcelId,
            recipientId,
            size,
            registeredAt);
    }

    public void AcceptAtDepot()
    {
        EnsureStatus(ParcelStatus.Registered);
        Status = ParcelStatus.AcceptedAtDepot;
    }

    public void MarkInTransit()
    {
        EnsureStatus(ParcelStatus.AcceptedAtDepot);
        Status = ParcelStatus.InTransit;
    }

    public Allocation AddAllocation(
        string lockerId,
        string compartmentId,
        DateTimeOffset allocatedAt,
        DateTimeOffset expiresAt)
    {
        if (Status != ParcelStatus.InTransit)
            throw new InvalidOperationException("An allocation can only be added while the parcel is in transit.");

        if (CurrentAllocation is not null)
            throw new InvalidOperationException("The parcel already has an unreleased allocation.");

        var allocation = new Allocation(
            Guid.NewGuid(),
            lockerId,
            compartmentId,
            allocatedAt,
            expiresAt);

        _allocations.Add(allocation);
        return allocation;
    }

    public void MarkAvailableForPickup(Guid allocationId)
    {
        EnsureStatus(ParcelStatus.InTransit);
        var allocation = GetAllocation(allocationId);

        if (allocation.Status != AllocationStatus.Active)
            throw new InvalidOperationException("The parcel requires an active allocation to become available for pickup.");

        Status = ParcelStatus.AvailableForPickup;
    }

    public void MarkPickedUp()
    {
        EnsureStatus(ParcelStatus.AvailableForPickup);
        Status = ParcelStatus.PickedUp;
    }

    public void MarkReturnPending()
    {
        EnsureStatus(ParcelStatus.AvailableForPickup);
        Status = ParcelStatus.ReturnPending;
    }

    public void MarkReturnedToSender()
    {
        EnsureStatus(ParcelStatus.ReturnPending);
        Status = ParcelStatus.ReturnedToSender;
    }

    public void ChangeSize(ParcelSize newSize)
    {
        if (Status is ParcelStatus.PickedUp or ParcelStatus.ReturnPending or ParcelStatus.ReturnedToSender)
            throw new InvalidOperationException("Parcel size cannot be changed in its current state.");

        if (CurrentAllocation is not null)
            throw new InvalidOperationException("Parcel size cannot be changed while an allocation has not been released.");

        Size = newSize;
    }

    private Allocation GetAllocation(Guid allocationId)
    {
        return _allocations.SingleOrDefault(allocation => allocation.Id == allocationId)
            ?? throw new InvalidOperationException($"Allocation '{allocationId}' does not belong to this parcel.");
    }

    private void EnsureStatus(ParcelStatus expectedStatus)
    {
        if (Status != expectedStatus)
            throw new InvalidOperationException(
                $"Operation is not valid when parcel status is '{Status}'. Expected '{expectedStatus}'.");
    }
}
