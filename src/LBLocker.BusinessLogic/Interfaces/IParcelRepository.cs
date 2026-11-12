using LBLocker.BusinessLogic.Contracts;
using LBLocker.Domain.Parcels;

namespace LBLocker.BusinessLogic.Interfaces;

public interface IParcelRepository
{
    Task<Parcel?> GetByExternalParcelIdAsync(
        string externalParcelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically records the source event and, where valid and new, creates
    /// the parcel and its parcel-specific recipient contact record.
    /// </summary>
    Task<ImportRecordDisposition> ProcessImportRecordAsync(
        ParcelImportRecord record,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Parcel parcel,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Parcel parcel,
        CancellationToken cancellationToken = default);
}