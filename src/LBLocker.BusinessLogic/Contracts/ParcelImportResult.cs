namespace LBLocker.BusinessLogic.Contracts;

/// <summary>
/// Outcome for one source row. Contains identifiers and processing outcome only;
/// it deliberately excludes recipient contact details.
/// </summary>
public sealed record ParcelImportItemResult(
    string EventId,
    string ExternalParcelId,
    DateTimeOffset Timestamp,
    ImportRecordDisposition Disposition);

/// <summary>
/// Summary and per-row outcomes for one import batch.
/// </summary>
public sealed record ParcelImportResult(
    int RowsRead,
    int ParcelsCreated,
    int DuplicateEventsSkipped,
    int ParcelsUpdated,
    int NoChangesSkipped,
    int IncompleteRowsSkipped,
    IReadOnlyList<ParcelImportItemResult> Items);
