using LBLocker.BusinessLogic.Services;
using LBLocker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
var dataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "LBLocker");
Directory.CreateDirectory(dataDirectory);

var databasePath = Path.Combine(dataDirectory, "lb-locker.db");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddBusinessLogic();
builder.Services.AddLockerPersistence(databasePath);

using var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LockerDbContext>();
    await db.Database.EnsureCreatedAsync();
}

var application = new ConsoleApplication(host.Services);
await application.RunAsync();

internal sealed class ConsoleApplication(IServiceProvider services)
{
    public async Task RunAsync()
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("LB Locker - Parcel Management");
            Console.WriteLine("==============================");
            Console.WriteLine("1. Import parcels from CSV");
            Console.WriteLine("0. Exit");
            Console.Write("\nSelect: ");

            var choice = Console.ReadLine()?.Trim();
            if (choice == "0")
                return;

            if (choice == "1")
                await ImportCsvAsync();
        }
    }

    private async Task ImportCsvAsync()
    {
        Console.Write("\nPath to CSV file: ");
        var path = Console.ReadLine()?.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            WriteError("File not found.");
            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            await using var scope = services.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<LBLocker.BusinessLogic.Interfaces.IParcelFileReader>();
            var importer = scope.ServiceProvider.GetRequiredService<ParcelImportService>();

            var records = await reader.ReadAsync(stream);
            var result = await importer.ImportAsync(records);

            Console.WriteLine("\nImport details");
            Console.WriteLine(new string('-', 100));
            Console.WriteLine($"{"EventId",-24} {"ParcelId",-24} {"Timestamp",-22} Result");
            Console.WriteLine(new string('-', 100));

            foreach (var item in result.Items)
            {
                Console.WriteLine(
                    $"{Display(item.EventId),-24} {Display(item.ExternalParcelId),-24} " +
                    $"{item.Timestamp:yyyy-MM-dd HH:mm:ss} {Describe(item.Disposition)}");
            }

            Console.WriteLine(new string('-', 100));
            Console.WriteLine($"Rows read:                 {result.RowsRead}");
            Console.WriteLine($"Parcels created:            {result.ParcelsCreated}");
            Console.WriteLine($"Existing parcels skipped:   {result.ExistingParcelsSkipped}");
            Console.WriteLine($"Duplicate events skipped:   {result.DuplicateEventsSkipped}");
            Console.WriteLine($"Incomplete rows rejected:   {result.IncompleteRowsSkipped}");

            await WriteImportReportAsync(result);
        }
        catch (Exception exception)
        {
            WriteError($"Import failed: {exception.Message}");
        }

        Console.WriteLine("\nPress Enter to continue...");
        Console.ReadLine();
    }

    private static async Task WriteImportReportAsync(
        LBLocker.BusinessLogic.Contracts.ParcelImportResult result)
    {
        var reportDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LBLocker",
            "ImportReports");
        Directory.CreateDirectory(reportDirectory);

        var reportPath = Path.Combine(
            reportDirectory,
            $"import-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.csv");

        await using var writer = new StreamWriter(reportPath);
        await writer.WriteLineAsync("EventId;ParcelId;TimestampUtc;Disposition");

        foreach (var item in result.Items)
        {
            var line = string.Join(";",
                Csv(item.EventId),
                Csv(item.ExternalParcelId),
                item.Timestamp.ToString("O"),
                item.Disposition);
            await writer.WriteLineAsync(line);
        }

        Console.WriteLine($"\nImport report saved: {reportPath}");
    }

    private static string Describe(LBLocker.BusinessLogic.Contracts.ImportRecordDisposition disposition) =>
        disposition switch
        {
            LBLocker.BusinessLogic.Contracts.ImportRecordDisposition.ParcelCreated => "CREATED",
            LBLocker.BusinessLogic.Contracts.ImportRecordDisposition.ExistingParcelSkipped => "EXISTING PARCEL",
            LBLocker.BusinessLogic.Contracts.ImportRecordDisposition.DuplicateEventSkipped => "DUPLICATE EVENT",
            LBLocker.BusinessLogic.Contracts.ImportRecordDisposition.IncompleteRowRejected => "REJECTED - INCOMPLETE",
            _ => disposition.ToString()
        };

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(empty)" : value;

    private static string Csv(string value) =>
        $""{value.Replace(""", """")}"";

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
