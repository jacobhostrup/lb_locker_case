using LBLocker.BusinessLogic.Contracts;
using LBLocker.Domain.Parcels;
using LBLocker.Infrastructure.Persistence;
using LBLocker.Infrastructure.Persistence.Repositories;
using LBLocker.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LBLocker.Infrastructure.Tests;

public sealed class ParcelRepositoryTests
{
    [Fact]
    public async Task ProcessImportRecordAsync_ShouldPersistParcelRecipientAndEventAtomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<LockerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new LockerDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var repository = new ParcelRepository(db);
        var record = Record("EVT-1", "PARCEL-1", ParcelSize.Medium);

        var result = await repository.ProcessImportRecordAsync(record);

        Assert.Equal(ImportRecordDisposition.ParcelCreated, result);
        Assert.Equal(1, await db.Parcels.CountAsync());
        Assert.Equal(1, await db.Recipients.CountAsync());
        Assert.Equal(1, await db.ImportedEvents.CountAsync());

        var recipient = await db.Recipients.SingleAsync();
        Assert.Equal("RECIPIENT-1", recipient.ExternalRecipientId);
        Assert.Equal("Test Recipient", recipient.Name);
        Assert.Equal("12345678", recipient.PhoneNumber);
    }

    [Fact]
    public async Task ProcessImportRecordAsync_ShouldSkipEventAlreadyProcessed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<LockerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new LockerDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var repository = new ParcelRepository(db);
        var record = Record("EVT-1", "PARCEL-1", ParcelSize.Medium);

        Assert.Equal(ImportRecordDisposition.ParcelCreated,
            await repository.ProcessImportRecordAsync(record));
        Assert.Equal(ImportRecordDisposition.DuplicateEventSkipped,
            await repository.ProcessImportRecordAsync(record));

        Assert.Single(await db.Parcels.ToListAsync());
        Assert.Single(await db.Recipients.ToListAsync());
        Assert.Single(await db.ImportedEvents.ToListAsync());
    }

    [Fact]
    public async Task ProcessImportRecordAsync_ShouldAllowRejectedEventToBeRetriedAfterCorrection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<LockerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new LockerDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var repository = new ParcelRepository(db);

        var incomplete = Record("EVT-1", "PARCEL-1", null);
        Assert.Equal(ImportRecordDisposition.IncompleteRowRejected,
            await repository.ProcessImportRecordAsync(incomplete));

        var corrected = Record("EVT-1", "PARCEL-1", ParcelSize.Small);
        Assert.Equal(ImportRecordDisposition.ParcelCreated,
            await repository.ProcessImportRecordAsync(corrected));

        Assert.Single(await db.Parcels.ToListAsync());
        Assert.Single(await db.Recipients.ToListAsync());
        var importedEvent = await db.ImportedEvents.SingleAsync();
        Assert.Equal(ImportedEventStatus.Processed, importedEvent.Status);
    }

    private static ParcelImportRecord Record(string eventId, string parcelId, ParcelSize? size) =>
        new(
            eventId,
            new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
            parcelId,
            "RECIPIENT-1",
            "Test Recipient",
            "12345678",
            "recipient@example.test",
            "1000",
            "RETAILER-1",
            "LOCKER-1",
            size);
}