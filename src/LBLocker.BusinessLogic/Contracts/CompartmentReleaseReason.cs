namespace LBLocker.BusinessLogic.Contracts;

/// <summary>
/// Business reasons supported by the Locker API when releasing a compartment.
/// </summary>
public enum CompartmentReleaseReason
{
    CustomerPickedUp,
    ExpiredReturnedToDepot,
    MaintenanceOverride
}
