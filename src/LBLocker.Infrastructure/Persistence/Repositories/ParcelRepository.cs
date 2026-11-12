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
            importedEvent.ParcelExternalId = existingParcel.ExternalParcelId;
            if (isNewEvent)
                dbContext.ImportedEvents.Add(importedEvent);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ImportRecordDisposition.ExistingParcelSkipped;
        }

        var parcel = Parcel.Register(
            record.ExternalParcelId,
            record.RecipientId,
            record.Size!.Value,
            record.Timestamp);

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

        importedEvent.ParcelExternalId = parcel.ExternalParcelId;
        if (isNewEvent)
            dbContext.ImportedEvents.Add(importedEvent);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ImportRecordDisposition.ParcelCreated;
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