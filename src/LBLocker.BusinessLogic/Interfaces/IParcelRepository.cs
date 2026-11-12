using LBLocker.Domain.Parcels;

namespace LBLocker.BusinessLogic.Interfaces;

public interface IParcelRepository
{
    Task<Parcel?> GetByExternalParcelIdAsync(
        string externalParcelId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Parcel parcel,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Parcel parcel,
        CancellationToken cancellationToken = default);
}
