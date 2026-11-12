namespace LBLocker.BusinessLogic.Contracts;

/// <summary>
/// Summary of one import batch. Skipped rows are counted so that incomplete
/// or repeated source data is visible instead of silently discarded.
/// </summary>
public sealed record ParcelImportResult(
    int RowsRead,
    int ParcelsCreated,
    int DuplicateEventsSkipped,
    int ExistingParcelsSkipped,
    int IncompleteRowsSkipped);
