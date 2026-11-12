using Xunit;
using LBLocker.Domain.Parcels;

namespace LBLocker.Domain.Tests;

public sealed class ParcelLifecycleTests
{
    private static readonly DateTimeOffset RegisteredAt =
        new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static Parcel CreateParcel()
    {
        var parcel = Parcel.Register("PARCEL-001", "RECIPIENT-001", ParcelSize.Medium, RegisteredAt);
        parcel.AcceptAtDepot();
        parcel.MarkInTransit();
        return parcel;
    }

    private static Allocation Allocate(Parcel parcel)
    {
        return parcel.AddAllocation(
            "LOCKER-CPH-01",
            "COMPARTMENT-001",
            RegisteredAt.AddHours(1),
            RegisteredAt.AddDays(3));
    }

    [Fact]
    public void Register_ShouldStartInRegisteredState()
    {
        var parcel = Parcel.Register("PARCEL-001", "RECIPIENT-001", ParcelSize.Small, RegisteredAt);

        Assert.Equal(ParcelStatus.Registered, parcel.Status);
        Assert.Empty(parcel.Allocations);
        Assert.Null(parcel.CurrentAllocation);
    }

    [Fact]
    public void Parcel_CannotMoveDirectlyFromRegisteredToInTransit()
    {
        var parcel = Parcel.Register("PARCEL-001", "RECIPIENT-001", ParcelSize.Small, RegisteredAt);

        Assert.Throws<InvalidOperationException>(() => parcel.MarkInTransit());
    }

    [Fact]
    public void AddAllocation_ShouldRequireParcelToBeInTransit()
    {
        var parcel = Parcel.Register("PARCEL-001", "RECIPIENT-001", ParcelSize.Small, RegisteredAt);

        Assert.Throws<InvalidOperationException>(() => Allocate(parcel));
    }

    [Fact]
    public void AddAllocation_ShouldPreventSecondUnreleasedAllocation()
    {
        var parcel = CreateParcel();
        Allocate(parcel);

        Assert.Throws<InvalidOperationException>(() => Allocate(parcel));
    }

    [Fact]
    public void ExpiredAllocation_ShouldRemainCurrentUntilReleaseIsConfirmed()
    {
        var parcel = CreateParcel();
        var allocation = Allocate(parcel);
        parcel.MarkAvailableForPickup(allocation.Id);

        parcel.MarkAllocationExpired(allocation.Id, allocation.ExpiresAt);

        Assert.Equal(ParcelStatus.ReturnPending, parcel.Status);
        Assert.Equal(AllocationStatus.Expired, allocation.Status);
        Assert.False(allocation.IsReleased);
        Assert.Same(allocation, parcel.CurrentAllocation);
    }

    [Fact]
    public void ReleasingExpiredAllocation_ShouldPreserveExpiredStatusAndAllowNewAllocation()
    {
        var parcel = CreateParcel();
        var allocation = Allocate(parcel);
        parcel.MarkAvailableForPickup(allocation.Id);
        parcel.MarkAllocationExpired(allocation.Id, allocation.ExpiresAt);

        parcel.MarkAllocationReleased(allocation.Id, allocation.ExpiresAt.AddMinutes(5));

        Assert.Equal(AllocationStatus.Expired, allocation.Status);
        Assert.True(allocation.IsReleased);
        Assert.Null(parcel.CurrentAllocation);
    }

    [Fact]
    public void Parcel_CannotBeMarkedReturnedBeforeAllocationIsReleased()
    {
        var parcel = CreateParcel();
        var allocation = Allocate(parcel);
        parcel.MarkAvailableForPickup(allocation.Id);
        parcel.MarkAllocationExpired(allocation.Id, allocation.ExpiresAt);

        Assert.Throws<InvalidOperationException>(() => parcel.MarkReturnedToSender());
    }

    [Fact]
    public void Parcel_CanBeMarkedReturnedAfterReleaseIsConfirmed()
    {
        var parcel = CreateParcel();
        var allocation = Allocate(parcel);
        parcel.MarkAvailableForPickup(allocation.Id);
        parcel.MarkAllocationExpired(allocation.Id, allocation.ExpiresAt);
        parcel.MarkAllocationReleased(allocation.Id, allocation.ExpiresAt.AddMinutes(5));

        parcel.MarkReturnedToSender();

        Assert.Equal(ParcelStatus.ReturnedToSender, parcel.Status);
    }

    [Fact]
    public void ActiveAllocationRelease_ShouldSetReleasedStatus()
    {
        var parcel = CreateParcel();
        var allocation = Allocate(parcel);

        parcel.MarkAllocationReleased(allocation.Id, allocation.AllocatedAt.AddMinutes(10));

        Assert.Equal(AllocationStatus.Released, allocation.Status);
        Assert.True(allocation.IsReleased);
    }

    [Fact]
    public void Allocation_CannotExpireBeforeExpiryTime()
    {
        var parcel = CreateParcel();
        var allocation = Allocate(parcel);

        Assert.Throws<InvalidOperationException>(
            () => allocation.MarkExpired(allocation.ExpiresAt.AddTicks(-1)));
    }
}
