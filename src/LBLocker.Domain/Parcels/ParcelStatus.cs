namespace LBLocker.Domain.Parcels;

public enum ParcelStatus
{
    Registered,
    AcceptedAtDepot,
    InTransit,
    AvailableForPickup,
    PickedUp,
    ReturnPending,
    ReturnedToSender
}
