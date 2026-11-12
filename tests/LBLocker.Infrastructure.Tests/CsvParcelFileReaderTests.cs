using System.Text;
using LBLocker.Domain.Parcels;
using LBLocker.Infrastructure.FileImport;

namespace LBLocker.Infrastructure.Tests;

public sealed class CsvParcelFileReaderTests
{
    private const string Header =
        "event_id;timestamp;parcel_id;recipient_id;recipient_name;recipient_phone;recipient_email;recipient_postal_code;retailer_id;target_locker_id;size";

    [Fact]
    public async Task ReadAsync_ShouldMapCsvColumnsAndSize()
    {
        const string row =
            "EVT-1;2026-10-27T06:00:00Z;PARCEL-1;RCP-1;Mette Jensen;+4520123456;mette@example.dk;2200;SHOP;LOCKER-1;M";

        await using var stream = CreateStream($"{Header}\n{row}");
        var reader = new CsvParcelFileReader();

        var records = await reader.ReadAsync(stream);

        var record = Assert.Single(records);
        Assert.Equal("EVT-1", record.EventId);
        Assert.Equal("PARCEL-1", record.ExternalParcelId);
        Assert.Equal(ParcelSize.Medium, record.Size);
        Assert.Equal("LOCKER-1", record.TargetLockerId);
    }

    [Fact]
    public async Task ReadAsync_ShouldMapEmptyOptionalValuesToNull()
    {
        const string row =
            "EVT-2;2026-10-27T06:00:00Z;PARCEL-2;RCP-2;Test Recipient;;;1000;SHOP;;";

        await using var stream = CreateStream($"{Header}\n{row}");
        var reader = new CsvParcelFileReader();

        var record = Assert.Single(await reader.ReadAsync(stream));

        Assert.Null(record.RecipientPhone);
        Assert.Null(record.TargetLockerId);
        Assert.Null(record.Size);
    }

    [Fact]
    public async Task ReadAsync_ShouldSupportQuotedSemicolonInTextField()
    {
        const string row =
            "EVT-3;2026-10-27T06:00:00Z;PARCEL-3;RCP-3;\"Jensen; Hansen\";+4512345678;test@example.dk;1000;SHOP;LOCKER-1;S";

        await using var stream = CreateStream($"{Header}\n{row}");
        var reader = new CsvParcelFileReader();

        var record = Assert.Single(await reader.ReadAsync(stream));

        Assert.Equal("Jensen; Hansen", record.RecipientName);
    }

    [Fact]
    public async Task ReadAsync_ShouldRejectUnknownSize()
    {
        const string row =
            "EVT-4;2026-10-27T06:00:00Z;PARCEL-4;RCP-4;Test;+4512345678;test@example.dk;1000;SHOP;LOCKER-1;XL";

        await using var stream = CreateStream($"{Header}\n{row}");
        var reader = new CsvParcelFileReader();

        var exception = await Assert.ThrowsAsync<FormatException>(
            () => reader.ReadAsync(stream));

        Assert.Contains("Unknown parcel size", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_ShouldRejectMissingRequiredHeader()
    {
        await using var stream = CreateStream("event_id;timestamp;parcel_id");
        var reader = new CsvParcelFileReader();

        var exception = await Assert.ThrowsAsync<FormatException>(
            () => reader.ReadAsync(stream));

        Assert.Contains("missing required columns", exception.Message);
    }

    private static MemoryStream CreateStream(string content) =>
        new(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray());
}
