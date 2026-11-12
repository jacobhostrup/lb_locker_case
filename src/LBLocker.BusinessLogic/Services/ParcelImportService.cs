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
        var updated = 0;
        var noChanges = 0;
        var incompleteRowsSkipped = 0;
        var items = new List<ParcelImportItemResult>(records.Count);

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
                case ImportRecordDisposition.ParcelUpdated:
                    updated++;
                    break;
                case ImportRecordDisposition.NoChangesSkipped:
                    noChanges++;
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

            items.Add(new ParcelImportItemResult(
                record.EventId,
                record.ExternalParcelId,
                record.Timestamp,
                disposition));
        }

        return new ParcelImportResult(
            records.Count,
            created,
            duplicateEventsSkipped,
            updated,
            noChanges,
            incompleteRowsSkipped,
            items);
    }
}
