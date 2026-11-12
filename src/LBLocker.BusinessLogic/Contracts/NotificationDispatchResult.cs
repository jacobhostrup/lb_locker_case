namespace LBLocker.BusinessLogic.Contracts;

public sealed record NotificationDispatchResult(
    string NotificationId,
    string RecipientId,
    string ParcelId,
    NotificationChannel DeliveredChannel,
    DateTimeOffset DispatchedAt);
