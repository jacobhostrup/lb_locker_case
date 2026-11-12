namespace LBLocker.BusinessLogic.Contracts;

public enum ImportRecordDisposition
{
    ParcelCreated,
    ParcelUpdated,
    NoChangesSkipped,
    DuplicateEventSkipped,
    IncompleteRowRejected
}