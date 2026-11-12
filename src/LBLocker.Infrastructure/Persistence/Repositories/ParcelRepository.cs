using LBLocker.BusinessLogic.Contracts;
using LBLocker.BusinessLogic.Interfaces;
using LBLocker.Domain.Parcels;
using LBLocker.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LBLocker.Infrastructure.Persistence.Repositories;

public sealed class ParcelRepository(LockerDbContext dbContext) : IParcelRepository
{
    public Task<Parcel?> GetByExternalParcelIdAsync(
        string externalParcelId,
        CancellationToken cancellationToken = default) =>
        dbContext.Parcels
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.ExternalParcelId == externalParcelId, cancellationToken);

    public async Task<ImportRecordDisposition> ProcessImportRecordAsync(
        ParcelImportRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Without a source event ID, durable deduplication is impossible.
        if (string.IsNullOrWhiteSpace(record.EventId))
            return ImportRecordDisposition.IncompleteRowRejected;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var importedEvent = await dbContext.ImportedEvents
            .SingleOrDefaultAsync(x => x.EventId == record.EventId, cancellationToken);

        // Processed events are idempotent. Rejected events can be retried after
        // the source row has been corrected, retaining the same EventId.
        if (importedEvent?.Status == ImportedEventStatus.Processed)
            return ImportRecordDisposition.DuplicateEventSkipped;

        var isNewEvent = importedEvent is null;
        var isValid = !string.IsNullOrWhiteSpace(record.ExternalParcelId)
            && !string.IsNullOrWhiteSpace(record.RecipientId)
            && record.Size.HasValue
            && !string.IsNullOrWhiteSpace(record.RecipientName)
            && (!string.IsNullOrWhiteSpace(record.RecipientPhone)
                || !string.IsNullOrWhiteSpace(record.RecipientEmail));

        var now = DateTimeOffset.UtcNow;
        importedEvent ??= new ImportedEventRecord
        {
            Id = Guid.NewGuid(),
            EventId = record.EventId,
            ImportedAt = now
        };

        importedEvent.Status = isValid ? ImportedEventStatus.Processed : ImportedEventStatus.Rejected;
        importedEvent.ProcessedAt = isValid ? now : null;
        importedEvent.FailureReason = isValid
            ? null
            : "Missing parcel ID, recipient ID, recipient name, parcel size or contact channel.";
        importedEvent.ParcelExternalId = string.IsNullOrWhiteSpace(record.ExternalParcelId)
            ? null
            : record.ExternalParcelId;

        if (!isValid)
        {
            if (isNewEvent)
                dbContext.ImportedEvents.Add(importedEvent);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ImportRecordDisposition.IncompleteRowRejected;
        }

        var existingParcel = await dbContext.Parcels
            .SingleOrDefaultAsync(x => x.ExternalParcelId == record.ExternalParcelId, cancellationToken);

        if (existingParcel is not null)
        {
            var recipient = await dbContext.Recipients
                .SingleOrDefaultAsync(x => x.ParcelId == existingParcel.Id, cancellationToken);
            var changes = new List<string>();

            void TrackChange(string field, string? oldValue, string? newValue)
            {
                if (!string.IsNullOrWhiteSpace(newValue) &&
                    !string.Equals(oldValue, newValue, StringComparison.Ordinal))
                {
                    changes.Add($"{field}: '{oldValue ?? "(empty)"}' -> '{newValue}'");
                }
            }

            TrackChange("RecipientId", existingParcel.RecipientId, record.RecipientId);
            TrackChange("RecipientName", recipient?.Name, record.RecipientName);
            TrackChange("RecipientPhone", recipient?.PhoneNumber, record.RecipientPhone);
            TrackChange("RecipientEmail", recipient?.Email, record.RecipientEmail);
            TrackChange("RecipientPostalCode", recipient?.PostalCode, record.RecipientPostalCode);
            TrackChange("Size", existingParcel.Size.ToString(), record.Size?.ToString());
            TrackChange("RequestedLocker", existingParcel.TargetLockerId, record.TargetLockerId);

            if (changes.Count == 0)
            {
                importedEvent.ParcelExternalId = existingParcel.ExternalParcelId;
                if (isNewEvent)
                    dbContext.ImportedEvents.Add(importedEvent);

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ImportRecordDisposition.NoChangesSkipped;
            }

            existingParcel.ApplyImportedSnapshot(record.Size!.Value, record.TargetLockerId);
            if (recipient is not null)
            {
                if (!string.IsNullOrWhiteSpace(record.RecipientName)) recipient.Name = record.RecipientName;
                if (!string.IsNullOrWhiteSpace(record.RecipientPhone)) recipient.PhoneNumber = record.RecipientPhone;
                if (!string.IsNullOrWhiteSpace(record.RecipientEmail)) recipient.Email = record.RecipientEmail;
                if (!string.IsNullOrWhiteSpace(record.RecipientPostalCode)) recipient.PostalCode = record.RecipientPostalCode;
                recipient.ExternalRecipientId = record.RecipientId;
            }

            foreach (var change in changes)
            {
                dbContext.ParcelEvents.Add(new ParcelEventRecord
                {
                    Id = Guid.NewGuid(),
                    ParcelId = existingParcel.Id,
                    EventType = "ImportedDataChanged",
                    OccurredAt = record.Timestamp,
                    RecordedAt = now,
                    Description = $"Source EventId {record.EventId}: {change}"
                });
            }

            importedEvent.ParcelExternalId = existingParcel.ExternalParcelId;
            if (isNewEvent)
                dbContext.ImportedEvents.Add(importedEvent);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ImportRecordDisposition.ParcelUpdated;
        }

        var parcel = Parcel.Register(
            record.ExternalParcelId,
            record.RecipientId,
            record.Size!.Value,
            record.Timestamp,
            record.TargetLockerId);

        dbContext.Parcels.Add(parcel);
        dbContext.Recipients.Add(new RecipientRecord
        {
            ParcelId = parcel.Id,
            ExternalRecipientId = record.RecipientId,
            Name = record.RecipientName,
            PhoneNumber = record.RecipientPhone,
            Email = record.RecipientEmail,
            PostalCode = record.RecipientPostalCode,
            CreatedAt = now
        });

        // The first lifecycle event is committed atomically with the parcel,
        // recipient and imported source event.
        dbContext.ParcelEvents.Add(new ParcelEventRecord
        {
            Id = Guid.NewGuid(),
            ParcelId = parcel.Id,
            EventType = "ParcelRegistered",
            OccurredAt = record.Timestamp,
            RecordedAt = now,
            Description = "Parcel registration imported from source batch."
        });

        importedEvent.ParcelExternalId = parcel.ExternalParcelId;
        if (isNewEvent)
            dbContext.ImportedEvents.Add(importedEvent);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ImportRecordDisposition.ParcelCreated;
    }

    public async Task RecordEventAsync(
        Guid parcelId,
        string eventType,
        DateTimeOffset occurredAt,
        string? description = null,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (parcelId == Guid.Empty)
            throw new ArgumentException("Parcel ID cannot be empty.", nameof(parcelId));

        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));

        if (eventType.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(eventType), "Event type cannot exceed 100 characters.");

        if (description?.Length > 500)
            throw new ArgumentOutOfRangeException(nameof(description), "Description cannot exceed 500 characters.");

        var parcelExists = await dbContext.Parcels
            .AnyAsync(x => x.Id == parcelId, cancellationToken);

        if (!parcelExists)
            throw new InvalidOperationException($"Parcel '{parcelId}' does not exist.");

        dbContext.ParcelEvents.Add(new ParcelEventRecord
        {
            Id = Guid.NewGuid(),
            ParcelId = parcelId,
            EventType = eventType,
            OccurredAt = occurredAt,
            RecordedAt = DateTimeOffset.UtcNow,
            Description = description,
            CorrelationId = correlationId
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(Parcel parcel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parcel);
        dbContext.Parcels.Add(parcel);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Parcel parcel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parcel);
        dbContext.Parcels.Update(parcel);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
