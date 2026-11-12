using Xunit;
using LBLocker.BusinessLogic.Contracts;
using LBLocker.BusinessLogic.Interfaces;
using LBLocker.BusinessLogic.Services;
using LBLocker.Domain.Parcels;

namespace LBLocker.BusinessLogic.Tests;

public sealed class ParcelImportServiceTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ImportAsync_ShouldCreateParcelFromCompleteRecord()
    {
        var repository = new InMemoryParcelRepository();
        var service = new ParcelImportService(repository);

        var result = await service.ImportAsync(
            [Record("EVT-1", "PARCEL-1", BaseTime, ParcelSize.Medium)]);

        Assert.Equal(1, result.RowsRead);
        Assert.Equal(1, result.ParcelsCreated);
        Assert.Single(repository.Parcels);
        Assert.Equal("PARCEL-1", repository.Parcels[0].ExternalParcelId);
        Assert.Equal(ParcelStatus.Registered, repository.Parcels[0].Status);
    }

    [Fact]
    public async Task ImportAsync_ShouldSkipDuplicateEventIdsWithinBatch()
    {
        var repository = new InMemoryParcelRepository();
        var service = new ParcelImportService(repository);
        var records = new[]
        {
            Record("EVT-1", "PARCEL-1", BaseTime, ParcelSize.Small),
            Record("EVT-1", "PARCEL-2", BaseTime.AddMinutes(1), ParcelSize.Large)
        };

        var result = await service.ImportAsync(records);

        Assert.Equal(1, result.ParcelsCreated);
        Assert.Equal(1, result.DuplicateEventsSkipped);
        Assert.Single(repository.Parcels);
    }

    [Fact]
    public async Task ImportAsync_ShouldSkipRowsWithMissingRequiredImportData()
    {
        var repository = new InMemoryParcelRepository();
        var service = new ParcelImportService(repository);
        var incomplete = Record("EVT-1", "PARCEL-1", BaseTime, null);

        var result = await service.ImportAsync([incomplete]);

        Assert.Equal(1, result.IncompleteRowsSkipped);
        Assert.Empty(repository.Parcels);
    }

    [Fact]
    public async Task ImportAsync_ShouldNotOverwriteExistingParcelWithLaterSnapshot()
    {
        var repository = new InMemoryParcelRepository();
        var existing = Parcel.Register("PARCEL-1", "RECIPIENT-1", ParcelSize.Small, BaseTime);
        await repository.AddAsync(existing);
        var service = new ParcelImportService(repository);

        var result = await service.ImportAsync(
            [Record("EVT-2", "PARCEL-1", BaseTime.AddHours(1), ParcelSize.Large)]);

        Assert.Equal(1, result.ExistingParcelsSkipped);
        Assert.Single(repository.Parcels);
        Assert.Equal(ParcelSize.Small, existing.Size);
    }

    [Fact]
    public async Task ImportAsync_ShouldProcessRecordsInTimestampOrder()
    {
        var repository = new InMemoryParcelRepository();
        var service = new ParcelImportService(repository);
        var records = new[]
        {
            Record("EVT-LATER", "PARCEL-1", BaseTime.AddHours(2), ParcelSize.Large),
            Record("EVT-EARLIER", "PARCEL-1", BaseTime, ParcelSize.Small)
        };

        var result = await service.ImportAsync(records);

        Assert.Equal(1, result.ParcelsCreated);
        Assert.Equal(ParcelSize.Small, Assert.Single(repository.Parcels).Size);
    }

    [Fact]
    public async Task ImportAsync_ShouldReturnZeroCountsForEmptyBatch()
    {
        var service = new ParcelImportService(new InMemoryParcelRepository());

        var result = await service.ImportAsync([]);

        Assert.Equal(0, result.RowsRead);
        Assert.Equal(0, result.ParcelsCreated);
    }

    private static ParcelImportRecord Record(
        string eventId,
        string parcelId,
        DateTimeOffset timestamp,
        ParcelSize? size)
    {
        return new ParcelImportRecord(
            eventId,
            timestamp,
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

    private sealed class InMemoryParcelRepository : IParcelRepository
    {
        private readonly List<Parcel> _parcels = [];

        public IReadOnlyList<Parcel> Parcels => _parcels;

        public Task<Parcel?> GetByExternalParcelIdAsync(
            string externalParcelId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_parcels.SingleOrDefault(
                parcel => parcel.ExternalParcelId == externalParcelId));
        }

        public Task AddAsync(
            Parcel parcel,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _parcels.Add(parcel);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            Parcel parcel,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
