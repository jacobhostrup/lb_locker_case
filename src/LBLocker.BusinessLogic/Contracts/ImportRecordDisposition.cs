namespace LBLocker.BusinessLogic.Contracts;

public enum ImportRecordDisposition
{
    ParcelCreated,
    ExistingParcelSkipped,
    DuplicateEventSkipped,
    IncompleteRowRejected
}