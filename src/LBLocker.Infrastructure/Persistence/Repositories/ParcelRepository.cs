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

        // Rows without an EventId cannot be durably deduplicated. Treat them as
        // rejected input rather than inventing an identity.
        if (string.IsNullOrWhiteSpace(record.EventId))
            return ImportRecordDisposition.IncompleteRowRejected;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (await dbContext.ImportedEvents.AnyAsync(
                x => x.EventId == record.EventId, cancellationToken))
        {
            return ImportRecordDisposition.DuplicateEventSkipped;
        }

        var isValid = !string.IsNullOrWhiteSpace(record.ExternalParcelId)
            && !string.IsNullOrWhiteSpace(record.RecipientId)
            && record.Size.HasValue;

        var now = DateTimeOffset.UtcNow;
        var importedEvent = new ImportedEventRecord
        {
            Id = Guid.NewGuid(),
            EventId = record.EventId,
            ImportedAt = now,
            Status = isValid ? ImportedEventStatus.Processed : ImportedEventStatus.Rejected,
            ProcessedAt = isValid ? now : null,
            FailureReason = isValid ? null : "Missing parcel ID, recipient ID or parcel size.",
            ParcelExternalId = string.IsNullOrWhiteSpace(record.ExternalParcelId)
                ? null
                : record.ExternalParcelId
        };

        if (!isValid)
        {
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