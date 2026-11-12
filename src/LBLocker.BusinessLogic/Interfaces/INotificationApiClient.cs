using LBLocker.BusinessLogic.Contracts;

namespace LBLocker.BusinessLogic.Interfaces;

public interface INotificationApiClient
{
    Task<NotificationDispatchResult> SendNotificationAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default);
}
