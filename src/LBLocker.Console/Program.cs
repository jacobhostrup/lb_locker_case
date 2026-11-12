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
            Console.WriteLine("1. Parcel overview");
            Console.WriteLine("2. Import parcels from CSV");
            Console.WriteLine("3. Import history");
            Console.WriteLine("0. Exit");
            Console.Write("\nSelect: ");

            var choice = Console.ReadLine()?.Trim();
            if (choice == "0")
                return;

            if (choice == "1")
                await ShowParcelOverviewAsync();
            else if (choice == "2")
                await ImportCsvAsync();
            else if (choice == "3")
                await ShowImportHistoryAsync();
        }
    }

    private async Task ShowParcelOverviewAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LockerDbContext>();
        var parcels = await db.Parcels
            .AsNoTracking()
            .Include(x => x.Allocations)
            .OrderBy(x => x.ExternalParcelId)
            .ToListAsync();
        var recipients = await db.Recipients.AsNoTracking().ToDictionaryAsync(x => x.ParcelId);
        var events = await db.ParcelEvents.AsNoTracking()
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync();

        Console.WriteLine("\nPARCEL OVERVIEW");
        Console.WriteLine(new string('=', 150));
        Console.WriteLine($"{"Parcel ID",-18} {"Recipient",-24} {"Size",-8} {"Parcel status",-22} {"Requested locker",-20} {"Allocated locker / box",-28} {"Allocation",-14}");
        Console.WriteLine(new string('-', 150));

        foreach (var parcel in parcels)
        {
            recipients.TryGetValue(parcel.Id, out var recipient);
            var allocation = parcel.CurrentAllocation;
            var requestedLocker = events
                .Where(x => x.ParcelId == parcel.Id && x.EventType == "RequestedLockerObserved")
                .OrderByDescending(x => x.OccurredAt)
                .FirstOrDefault()?.Description ?? "Not recorded";
            var allocated = allocation is null
                ? "—"
                : $"{allocation.LockerId} / {allocation.CompartmentId}";
            var allocationStatus = allocation?.Status.ToString() ?? "Not allocated";

            Console.WriteLine($"{Clip(parcel.ExternalParcelId, 18),-18} {Clip(recipient?.Name ?? "Unknown", 24),-24} {parcel.Size,-8} {parcel.Status,-22} {Clip(requestedLocker, 20),-20} {Clip(allocated, 28),-28} {allocationStatus,-14}");
        }

        Console.WriteLine(new string('-', 150));
        Console.WriteLine($"Total parcels: {parcels.Count}");
        Console.WriteLine("\nEnter a Parcel ID to inspect its history, or press Enter to return:");
        var parcelId = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(parcelId))
            await ShowParcelDetailsAsync(parcelId);
    }

    private async Task ShowParcelDetailsAsync(string externalParcelId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LockerDbContext>();
        var parcel = await db.Parcels.AsNoTracking()
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.ExternalParcelId == externalParcelId);

        if (parcel is null)
        {
            WriteError($"Parcel '{externalParcelId}' was not found.");
            Pause();
            return;
        }

        var recipient = await db.Recipients.AsNoTracking().SingleOrDefaultAsync(x => x.ParcelId == parcel.Id);
        var history = await db.ParcelEvents.AsNoTracking()
            .Where(x => x.ParcelId == parcel.Id)
            .OrderBy(x => x.OccurredAt)
            .ToListAsync();
        var dispatches = await db.NotificationDispatches.AsNoTracking()
            .Where(x => parcel.Allocations.Select(a => a.Id).Contains(x.AllocationId))
            .OrderBy(x => x.AttemptedAt)
            .ToListAsync();
        var credentials = await db.AllocationCredentials.AsNoTracking()
            .Where(x => parcel.Allocations.Select(a => a.Id).Contains(x.AllocationId))
            .ToListAsync();

        Console.WriteLine($"\nPARCEL: {parcel.ExternalParcelId}");
        Console.WriteLine(new string('=', 90));
        Console.WriteLine($"Recipient:          {recipient?.Name ?? "Unknown"}");
        Console.WriteLine($"Recipient ID:       {recipient?.ExternalRecipientId ?? parcel.RecipientId}");
        Console.WriteLine($"Phone:              {Mask(recipient?.PhoneNumber)}");
        Console.WriteLine($"Email:              {MaskEmail(recipient?.Email)}");
        Console.WriteLine($"Size:               {parcel.Size}");
        Console.WriteLine($"Parcel status:      {parcel.Status}");
        Console.WriteLine($"PIN status:         {(credentials.Count == 0 ? "Not generated / not stored" : "Encrypted credential stored")}");
        Console.WriteLine($"Notification status:{(dispatches.Count == 0 ? " No attempts recorded" : "")}");

        foreach (var allocation in parcel.Allocations.OrderBy(x => x.AllocatedAt))
        {
            Console.WriteLine($"\nAllocation {allocation.Id}");
            Console.WriteLine($"  Locker / box: {allocation.LockerId} / {allocation.CompartmentId}");
            Console.WriteLine($"  Status:       {allocation.Status}");
            Console.WriteLine($"  Allocated:    {allocation.AllocatedAt:O}");
            Console.WriteLine($"  Expires:      {allocation.ExpiresAt:O}");
            Console.WriteLine($"  Released:     {allocation.ReleasedAt?.ToString("O") ?? "Not released"}");
        }

        if (dispatches.Count > 0)
        {
            Console.WriteLine("\nNOTIFICATION ATTEMPTS");
            foreach (var dispatch in dispatches)
                Console.WriteLine($"  {dispatch.AttemptedAt:O} | {dispatch.Channel} | {dispatch.Status} | {dispatch.ExternalDispatchId ?? dispatch.ErrorCode ?? "No external result"}");
        }

        Console.WriteLine("\nEVENT HISTORY");
        foreach (var item in history)
            Console.WriteLine($"  {item.OccurredAt:O} | {item.EventType} | {item.Description ?? "—"} | Correlation: {item.CorrelationId?.ToString() ?? "—"}");

        Pause();
    }

    private async Task ShowImportHistoryAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LockerDbContext>();
        var imports = await db.ImportedEvents.AsNoTracking()
            .OrderByDescending(x => x.ImportedAt)
            .ToListAsync();

        Console.WriteLine("\nIMPORTED SOURCE EVENTS");
        Console.WriteLine(new string('-', 120));
        Console.WriteLine($"{"Event ID",-30} {"Parcel ID",-20} {"Imported",-28} {"Status",-12} Failure");
        Console.WriteLine(new string('-', 120));
        foreach (var item in imports)
            Console.WriteLine($"{Clip(item.EventId, 30),-30} {Clip(item.ParcelExternalId ?? "—", 20),-20} {item.ImportedAt:O} {item.Status,-12} {item.FailureReason ?? "—"}");
        Console.WriteLine($"\nTotal source events: {imports.Count}");
        Pause();
    }

    private async Task ImportCsvAsync()
    {
        Console.Write("Path to CSV file: ");
        var path = Console.ReadLine()?.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            WriteError("File not found.");
            Pause();
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

            Console.WriteLine("\nIMPORT RESULT");
            Console.WriteLine(new string('-', 100));
            Console.WriteLine($"{"EventId",-24} {"ParcelId",-24} {"Timestamp",-22} Result");
            Console.WriteLine(new string('-', 100));
            foreach (var item in result.Items)
                Console.WriteLine($"{Clip(item.EventId, 24),-24} {Clip(item.ExternalParcelId, 24),-24} {item.Timestamp:yyyy-MM-dd HH:mm:ss} {Describe(item.Disposition)}");

            Console.WriteLine(new string('-', 100));
            Console.WriteLine($"Rows read:                  {result.RowsRead}");
            Console.WriteLine($"Parcels created:             {result.ParcelsCreated}");
            Console.WriteLine($"Existing parcels skipped:    {result.ExistingParcelsSkipped}");
            Console.WriteLine($"Duplicate events skipped:    {result.DuplicateEventsSkipped}");
            Console.WriteLine($"Incomplete rows rejected:    {result.IncompleteRowsSkipped}");
            await WriteImportReportAsync(result);
        }
        catch (Exception exception)
        {
            WriteError($"Import failed: {exception.Message}");
        }

        Pause();
    }

    private static async Task WriteImportReportAsync(LBLocker.BusinessLogic.Contracts.ParcelImportResult result)
    {
        var reportDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LBLocker", "ImportReports");
        Directory.CreateDirectory(reportDirectory);
        var reportPath = Path.Combine(reportDirectory, $"import-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.csv");

        await using var writer = new StreamWriter(reportPath);
        await writer.WriteLineAsync("EventId;ParcelId;TimestampUtc;Disposition");
        foreach (var item in result.Items)
        {
            var line = string.Join(";",
                Csv(item.EventId), Csv(item.ExternalParcelId),
                item.Timestamp.ToString("O"), item.Disposition);
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

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
    private static string Mask(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Length < 5 ? "***" : value[..2] + "***" + value[^2..];
    private static string MaskEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        var at = value.IndexOf('@');
        return at <= 1 ? "***" + value[Math.Max(at, 0)..] : value[..1] + "***" + value[at..];
    }
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static void Pause() { Console.WriteLine("\nPress Enter to continue..."); Console.ReadLine(); }
    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
