using LBLocker.BusinessLogic.Contracts;
using LBLocker.BusinessLogic.Interfaces;

namespace LBLocker.BusinessLogic.Services;

/// <summary>
/// Imports parcel registrations from a parsed batch. The source contains
/// snapshots but no event type, so existing parcels are not mutated.
/// Persistence owns atomic event deduplication and parcel/recipient creation.
/// </summary>
public sealed class ParcelImportService(IParcelRepository parcelRepository)
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

        foreach (var record in records.OrderBy(record => record.Timestamp))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var disposition = await parcelRepository.ProcessImportRecordAsync(
                record, cancellationToken);

            switch (disposition)
            {
                case ImportRecordDisposition.ParcelCreated:
                    created++;
                    break;
                case ImportRecordDisposition.ExistingParcelSkipped:
                    existingParcelsSkipped++;
                    break;
                case ImportRecordDisposition.DuplicateEventSkipped:
                    duplicateEventsSkipped++;
                    break;
                case ImportRecordDisposition.IncompleteRowRejected:
                    incompleteRowsSkipped++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null);
            }
        }

        return new ParcelImportResult(
            records.Count,
            created,
            duplicateEventsSkipped,
            existingParcelsSkipped,
            incompleteRowsSkipped);
    }
}