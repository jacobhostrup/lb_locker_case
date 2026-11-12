using LBLocker.BusinessLogic.Contracts;
using LBLocker.Domain.Parcels;

namespace LBLocker.BusinessLogic.Interfaces;

public interface ILockerApiClient
{
    Task<IReadOnlyCollection<LockerInfo>> GetLockersAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CompartmentInfo>> GetCompartmentsAsync(
        string lockerId,
        CancellationToken cancellationToken = default);

    Task<CompartmentInfo> GetCompartmentAsync(
        string lockerId,
        string compartmentId,
        CancellationToken cancellationToken = default);

    Task<LockerAllocationResult> AllocateCompartmentAsync(
        string lockerId,
        string externalParcelId,
        ParcelSize sizeRequired,
        string recipientId,
        string pinCode,
        CancellationToken cancellationToken = default);

    Task ReleaseCompartmentAsync(
        string lockerId,
        string compartmentId,
        string externalParcelId,
        CompartmentReleaseReason reason,
        CancellationToken cancellationToken = default);
}
