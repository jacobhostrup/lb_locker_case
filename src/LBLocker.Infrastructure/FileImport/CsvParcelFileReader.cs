using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using LBLocker.BusinessLogic.Contracts;
using LBLocker.BusinessLogic.Interfaces;
using LBLocker.Domain.Parcels;

namespace LBLocker.Infrastructure.FileImport;

public sealed class CsvParcelFileReader : IParcelFileReader
{
    private static readonly string[] RequiredHeaders =
    [
        "event_id",
        "timestamp",
        "parcel_id",
        "recipient_id",
        "recipient_name",
        "recipient_phone",
        "recipient_email",
        "recipient_postal_code",
        "retailer_id",
        "target_locker_id",
        "size"
    ];

    public async Task<IReadOnlyCollection<ParcelImportRecord>> ReadAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.CanRead)
            throw new ArgumentException("The input stream must be readable.", nameof(input));

        using var textReader = new StreamReader(
            input,
            System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";",
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim
        };

        using var csv = new CsvReader(textReader, configuration);

        if (!await csv.ReadAsync())
            return [];

        csv.ReadHeader();
        ValidateHeaders(csv.HeaderRecord);

        var records = new List<ParcelImportRecord>();

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var timestampText = GetField(csv, "timestamp");
            if (!DateTimeOffset.TryParse(
                    timestampText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var timestamp))
            {
                throw new FormatException(
                    $"Invalid timestamp '{timestampText}' at CSV row {csv.Parser.Row}.");
            }

            records.Add(new ParcelImportRecord(
                GetField(csv, "event_id"),
                timestamp,
                GetField(csv, "parcel_id"),
                GetField(csv, "recipient_id"),
                GetField(csv, "recipient_name"),
                GetOptionalField(csv, "recipient_phone"),
                GetField(csv, "recipient_email"),
                GetOptionalField(csv, "recipient_postal_code"),
                GetField(csv, "retailer_id"),
                GetOptionalField(csv, "target_locker_id"),
                ParseSize(GetOptionalField(csv, "size"), csv.Parser.Row)));
        }

        return records;
    }

    private static void ValidateHeaders(string[]? headers)
    {
        if (headers is null)
            throw new FormatException("The CSV file does not contain a header row.");

        var available = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
        var missing = RequiredHeaders
            .Where(header => !available.Contains(header))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new FormatException(
                $"The CSV file is missing required columns: {string.Join(", ", missing)}.");
        }
    }

    private static string GetField(CsvReader csv, string name) =>
        csv.GetField(name) ?? string.Empty;

    private static string? GetOptionalField(CsvReader csv, string name)
    {
        var value = GetField(csv, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static ParcelSize? ParseSize(string? value, int row)
    {
        if (value is null)
            return null;

        return value.Trim().ToUpperInvariant() switch
        {
            "S" => ParcelSize.Small,
            "M" => ParcelSize.Medium,
            "L" => ParcelSize.Large,
            _ => throw new FormatException(
                $"Unknown parcel size '{value}' at CSV row {row}. Expected S, M or L.")
        };
    }
}
