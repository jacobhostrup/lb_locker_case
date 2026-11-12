using LBLocker.Domain.Parcels;

namespace LBLocker.BusinessLogic.Contracts;

/// <summary>
/// Snapshot of a compartment as reported by the Locker API.
/// API-specific status values are kept as strings at this boundary.
/// </summary>
public sealed record CompartmentInfo(
    string CompartmentId,
    ParcelSize Size,
    string Status,
    string DoorState,
    string SensorState);
