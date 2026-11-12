using LBLocker.BusinessLogic.Contracts;
using LBLocker.BusinessLogic.Interfaces;
using LBLocker.Domain.Parcels;

namespace LBLocker.BusinessLogic.Services;

/// <summary>
/// Imports parcel registrations from a parsed batch.
/// The current source does not define event types, so repeated rows are not
/// interpreted as parcel lifecycle transitions and existing parcels are not overwritten.
/// </summary>
public sealed class ParcelImportService(
    IParcelRepository parcelRepository)
{
    public async Task<ParcelImportResult> ImportAsync(
        IReadOnlyCollection<ParcelImportRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var created = 0;
        var duplicateEventsSkipped = 0;
        var existingParcelsSkipped = 0;
        var incompleteRowsSkipped = 0;
        var seenEventIds = new HashSet<string>(StringComparer.Ordinal);

        // Process deterministically by source timestamp. The first complete row
        // for a parcel establishes its initial imported data; later snapshots
        // do not implicitly mutate the domain entity.
        foreach (var record in records.OrderBy(record => record.Timestamp))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(record.EventId) ||
                !seenEventIds.Add(record.EventId))
            {
                duplicateEventsSkipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.ExternalParcelId) ||
                string.IsNullOrWhiteSpace(record.RecipientId) ||
                record.Size is null)
            {
                incompleteRowsSkipped++;
                continue;
            }

            var existing = await parcelRepository.GetByExternalParcelIdAsync(
                record.ExternalParcelId,
                cancellationToken);

            if (existing is not null)
            {
                existingParcelsSkipped++;
                continue;
            }

            var parcel = Parcel.Register(
                record.ExternalParcelId,
                record.RecipientId,
                record.Size.Value,
                record.Timestamp);

            await parcelRepository.AddAsync(parcel, cancellationToken);
            created++;
        }

        return new ParcelImportResult(
            records.Count,
            created,
            duplicateEventsSkipped,
            existingParcelsSkipped,
            incompleteRowsSkipped);
    }
}
