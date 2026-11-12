namespace LBLocker.BusinessLogic.Contracts;

public enum NotificationChannel
{
    Sms,
    Email
}

public sealed record NotificationRequest(
    string RecipientId,
    string ParcelId,
    string LockerId,
    NotificationChannel Channel,
    string? PinCode,
    string? TextBody);
