using System.Net;
using LBLocker.BusinessLogic.Contracts;
using LBLocker.BusinessLogic.Interfaces;
using LBLocker.Domain.Parcels;
using LBLocker.Infrastructure.Http;
using LBLocker.Infrastructure.Persistence;
using LBLocker.Infrastructure.Persistence.Entities;
using LBLocker.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using ApiNotificationChannel = LBLocker.BusinessLogic.Contracts.NotificationChannel;
using EntityNotificationChannel = LBLocker.Infrastructure.Persistence.Entities.NotificationChannel;

namespace LBLocker.Infrastructure.Processing;

/// <summary>
/// Executes or resumes the depot-to-locker workflow for one existing parcel.
/// Successful steps are persisted and are not repeated on subsequent runs.
/// </summary>
public sealed class ParcelProcessingService(
    LockerDbContext db,
    ILockerApiClient lockerApi,
    INotificationApiClient notificationApi,
    PinGenerator pinGenerator,
    PinCredentialProtector pinProtector)
{
    public async Task ProcessAsync(string externalParcelId, CancellationToken cancellationToken = default)
    {
        var parcel = await db.Parcels
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.ExternalParcelId == externalParcelId, cancellationToken)
            ?? throw new InvalidOperationException($"Parcel '{externalParcelId}' was not found.");

        var recipient = await db.Recipients.SingleOrDefaultAsync(
            x => x.ParcelId == parcel.Id, cancellationToken)
            ?? throw new InvalidOperationException("Recipient record is missing.");

        if (parcel.Status is ParcelStatus.PickedUp or ParcelStatus.ReturnedToSender)
            throw new InvalidOperationException($"Parcel is already in terminal status '{parcel.Status}'.");

        if (string.IsNullOrWhiteSpace(parcel.TargetLockerId))
            throw new InvalidOperationException("Requested locker is missing. The parcel cannot be allocated to another locker.");

        if (parcel.Status == ParcelStatus.Registered)
        {
            parcel.AcceptAtDepot();
        }
        if (parcel.Status == ParcelStatus.AcceptedAtDepot)
        {
            parcel.MarkInTransit();
            await db.SaveChangesAsync(cancellationToken);
            await AddEventAsync(parcel.Id, "ParcelReadyForLockerProcessing",
                "Parcel explicitly submitted for locker processing from depot.", null, cancellationToken);
        }

        var allocation = parcel.CurrentAllocation;
        if (allocation is null)
        {
            var previousStarted = await db.ProcessAttempts.AnyAsync(x =>
                x.ParcelId == parcel.Id &&
                x.Step == ProcessStep.AllocateCompartment &&
                x.Status == ProcessAttemptStatus.Started, cancellationToken);

            if (previousStarted)
                throw new InvalidOperationException(
                    "A previous allocation attempt has an unknown outcome. Reconcile the locker API before retrying; automatic retry could create a duplicate booking.");

            var correlationId = Guid.NewGuid();
            var pin = pinGenerator.Generate();
            var attempt = await StartAttemptAsync(parcel.Id, correlationId,
                ProcessStep.AllocateCompartment, cancellationToken);

            try
            {
                var result = await lockerApi.AllocateCompartmentAsync(
                    parcel.TargetLockerId,
                    parcel.ExternalParcelId,
                    parcel.Size,
                    parcel.RecipientId,
                    pin,
                    cancellationToken);

                if (!string.Equals(result.PinCode, pin, StringComparison.Ordinal))
                    throw new InvalidOperationException("Locker API did not echo the client-generated PIN unchanged.");

                allocation = parcel.AddAllocation(
                    result.LockerId,
                    result.CompartmentId,
                    result.AllocatedAt,
                    result.ExpiresAt);

                db.AllocationCredentials.Add(new AllocationCredentialRecord
                {
                    Id = Guid.NewGuid(),
                    AllocationId = allocation.Id,
                    EncryptedPin = pinProtector.Protect(pin),
                    KeyVersion = pinProtector.KeyVersion,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                await CompleteAttemptAsync(attempt, ProcessAttemptStatus.Succeeded, null, null, null, cancellationToken);
                await AddEventAsync(parcel.Id, "CompartmentBooked",
                    $"Locker {result.LockerId}, compartment {result.CompartmentId}, allocation {result.AllocationId}.",
                    correlationId, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                await FailAttemptAsync(attempt, exception, cancellationToken);
                if (exception is LockerApiException { StatusCode: HttpStatusCode.Conflict })
                    await AddEventAsync(parcel.Id, "LockerCapacityUnavailable",
                        exception.Message, correlationId, cancellationToken);
                throw;
            }
        }

        var credential = await db.AllocationCredentials.SingleOrDefaultAsync(
            x => x.AllocationId == allocation.Id, cancellationToken)
            ?? throw new InvalidOperationException("The active allocation has no stored PIN credential.");

        var storedPin = pinProtector.Unprotect(credential.EncryptedPin);
        var lockerList = await lockerApi.GetLockersAsync(cancellationToken);
        var locker = lockerList.SingleOrDefault(x => x.LockerId == allocation.LockerId)
            ?? throw new InvalidOperationException($"Locker '{allocation.LockerId}' was not returned by Locker API.");

        if (!string.IsNullOrWhiteSpace(recipient.PhoneNumber))
            await SendChannelAsync(parcel, recipient, allocation, storedPin, locker.Address,
                ApiNotificationChannel.Sms, cancellationToken);

        if (!string.IsNullOrWhiteSpace(recipient.Email))
            await SendChannelAsync(parcel, recipient, allocation, storedPin, locker.Address,
                ApiNotificationChannel.Email, cancellationToken);

        var dispatches = await db.NotificationDispatches
            .Where(x => x.AllocationId == allocation.Id).ToListAsync(cancellationToken);
        var smsAccepted = dispatches.Any(x => x.Channel == EntityNotificationChannel.Sms &&
            x.Status == NotificationDispatchStatus.Accepted);
        var emailAccepted = dispatches.Any(x => x.Channel == EntityNotificationChannel.Email &&
            x.Status == NotificationDispatchStatus.Accepted);

        if (smsAccepted || emailAccepted)
        {
            if (parcel.Status == ParcelStatus.InTransit)
                parcel.MarkAvailableForPickup(allocation.Id);
            await db.SaveChangesAsync(cancellationToken);
            await AddEventAsync(parcel.Id, "ParcelReadyForPickup",
                $"Notification accepted by {(smsAccepted && emailAccepted ? "SMS and email" : smsAccepted ? "SMS" : "email")} provider. Acceptance is not proof of recipient delivery.",
                null, cancellationToken);
        }
        else
        {
            await AddEventAsync(parcel.Id, "NotificationChannelsUnsuccessful",
                "No notification channel was accepted. Parcel remains allocated and requires operational handling.",
                null, cancellationToken);
        }
    }

    private async Task SendChannelAsync(
        Parcel parcel, RecipientRecord recipient, Allocation allocation, string pin,
        string lockerAddress, ApiNotificationChannel channel, CancellationToken cancellationToken)
    {
        var persistedChannel = channel == NotificationChannel.Sms
            ? Persistence.Entities.NotificationChannel.Sms
            : Persistence.Entities.NotificationChannel.Email;

        var accepted = await db.NotificationDispatches.AnyAsync(x =>
            x.AllocationId == allocation.Id &&
            x.Channel == persistedChannel &&
            x.Status == NotificationDispatchStatus.Accepted, cancellationToken);
        if (accepted) return;

        var correlationId = Guid.NewGuid();
        var attempt = await StartAttemptAsync(parcel.Id, correlationId,
            channel == ApiNotificationChannel.Sms ? ProcessStep.SendSms : ProcessStep.SendEmail,
            cancellationToken);
        var attemptRecord = new NotificationDispatchRecord
        {
            Id = Guid.NewGuid(),
            AllocationId = allocation.Id,
            Channel = persistedChannel,
            Status = NotificationDispatchStatus.Failed,
            AttemptedAt = DateTimeOffset.UtcNow
        };
        db.NotificationDispatches.Add(attemptRecord);
        await db.SaveChangesAsync(cancellationToken);

        var message = $"Your parcel {parcel.ExternalParcelId} is ready for pickup at {lockerAddress}. " +
            $"Locker: {allocation.LockerId}, compartment: {allocation.CompartmentId}. " +
            $"Pickup PIN: {pin}. Parcel reference: {parcel.ExternalParcelId}.";

        try
        {
            var result = await notificationApi.SendNotificationAsync(new NotificationRequest(
                recipient.ExternalRecipientId,
                parcel.ExternalParcelId,
                allocation.LockerId,
                channel,
                pin,
                message), cancellationToken);

            attemptRecord.Status = NotificationDispatchStatus.Accepted;
            attemptRecord.ExternalDispatchId = result.NotificationId;
            attemptRecord.AttemptedAt = result.DispatchedAt;
            await CompleteAttemptAsync(attempt, ProcessAttemptStatus.Succeeded, null, null, null, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            attemptRecord.Status = exception is LockerApiException api &&
                (int?)api.StatusCode is >= 400 and < 500
                    ? NotificationDispatchStatus.Rejected
                    : NotificationDispatchStatus.Failed;
            attemptRecord.HttpStatusCode = (int?)(exception as HttpRequestException)?.StatusCode;
            attemptRecord.ErrorCode = ExtractErrorCode(exception);
            attemptRecord.ErrorMessage = exception.Message;
            attemptRecord.ResponseBody = (exception as LockerApiException)?.ResponseBody;
            await FailAttemptAsync(attempt, exception, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<ProcessAttemptRecord> StartAttemptAsync(
        Guid parcelId, Guid correlationId, ProcessStep step, CancellationToken cancellationToken)
    {
        var number = await db.ProcessAttempts.CountAsync(
            x => x.ParcelId == parcelId && x.Step == step, cancellationToken) + 1;
        var attempt = new ProcessAttemptRecord
        {
            Id = Guid.NewGuid(), ParcelId = parcelId, CorrelationId = correlationId,
            Step = step, AttemptNumber = number, Status = ProcessAttemptStatus.Started,
            OccurredAt = DateTimeOffset.UtcNow
        };
        db.ProcessAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
        return attempt;
    }

    private async Task CompleteAttemptAsync(
        ProcessAttemptRecord attempt, ProcessAttemptStatus status,
        int? httpStatus, string? errorCode, string? errorMessage,
        CancellationToken cancellationToken)
    {
        attempt.Status = status;
        attempt.HttpStatusCode = httpStatus;
        attempt.ErrorCode = errorCode;
        attempt.ErrorMessage = errorMessage;
        attempt.OccurredAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task FailAttemptAsync(
        ProcessAttemptRecord attempt, Exception exception, CancellationToken cancellationToken)
    {
        var api = exception as LockerApiException;
        attempt.Status = ProcessAttemptStatus.Failed;
        attempt.HttpStatusCode = (int?)api?.StatusCode;
        attempt.ErrorCode = ExtractErrorCode(exception);
        attempt.ErrorMessage = exception.Message;
        attempt.ResponseBody = api?.ResponseBody;
        attempt.OccurredAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AddEventAsync(
        Guid parcelId, string eventType, string description, Guid? correlationId,
        CancellationToken cancellationToken)
    {
        db.ParcelEvents.Add(new ParcelEventRecord
        {
            Id = Guid.NewGuid(), ParcelId = parcelId, EventType = eventType,
            OccurredAt = DateTimeOffset.UtcNow, RecordedAt = DateTimeOffset.UtcNow,
            Description = description, CorrelationId = correlationId
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? ExtractErrorCode(Exception exception)
    {
        var body = (exception as LockerApiException)?.ResponseBody;
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
