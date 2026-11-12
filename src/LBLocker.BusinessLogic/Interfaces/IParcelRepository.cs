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

    /// <summary>
    /// Appends an immutable business-process event to the parcel history.
    /// Event types should describe a completed action or a failed attempt.
    /// </summary>
    Task RecordEventAsync(
        Guid parcelId,
        string eventType,
        DateTimeOffset occurredAt,
        string? description = null,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Parcel parcel,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Parcel parcel,
        CancellationToken cancellationToken = default);
}
