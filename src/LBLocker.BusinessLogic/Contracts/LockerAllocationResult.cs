namespace LBLocker.BusinessLogic.Contracts;

public sealed record LockerAllocationResult(
    string AllocationId,
    string LockerId,
    string CompartmentId,
    string PinCode,
    DateTimeOffset AllocatedAt,
    DateTimeOffset ExpiresAt);
