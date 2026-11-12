using LBLocker.Domain.Parcels;
using LBLocker.Infrastructure.Persistence.Entities;
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
// Keep EF Core warnings/errors, but suppress routine SQL command and query logging.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Services.AddBusinessLogic();
builder.Services.AddLockerPersistence(databasePath);

using var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LockerDbContext>();
    await db.Database.EnsureCreatedAsync();
    await UpgradeSqliteSchemaAsync(db);
}

static async Task UpgradeSqliteSchemaAsync(LockerDbContext db)
{
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;
    if (shouldClose)
        await connection.OpenAsync();

    try
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('Parcels');";
        await using var reader = await command.ExecuteReaderAsync();

        var hasTargetLockerId = false;
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), "TargetLockerId", StringComparison.OrdinalIgnoreCase))
            {
                hasTargetLockerId = true;
                break;
            }
        }

        await reader.DisposeAsync();

        if (!hasTargetLockerId)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Parcels ADD COLUMN TargetLockerId TEXT NULL;");

        var notificationColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var notificationCommand = connection.CreateCommand())
        {
            notificationCommand.CommandText = "PRAGMA table_info('NotificationDispatches');";
            await using var notificationReader = await notificationCommand.ExecuteReaderAsync();
            while (await notificationReader.ReadAsync())
                notificationColumns.Add(notificationReader.GetString(1));
        }

        if (!notificationColumns.Contains("HttpStatusCode"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE NotificationDispatches ADD COLUMN HttpStatusCode INTEGER NULL;");
        if (!notificationColumns.Contains("ErrorMessage"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE NotificationDispatches ADD COLUMN ErrorMessage TEXT NULL;");
        if (!notificationColumns.Contains("ResponseBody"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE NotificationDispatches ADD COLUMN ResponseBody TEXT NULL;");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS ProcessAttempts (
                Id TEXT NOT NULL CONSTRAINT PK_ProcessAttempts PRIMARY KEY,
                ParcelId TEXT NOT NULL,
                CorrelationId TEXT NOT NULL,
                Step TEXT NOT NULL,
                AttemptNumber INTEGER NOT NULL,
                Status TEXT NOT NULL,
                OccurredAt TEXT NOT NULL,
                HttpStatusCode INTEGER NULL,
                ErrorCode TEXT NULL,
                ErrorMessage TEXT NULL,
                ResponseBody TEXT NULL,
                CONSTRAINT FK_ProcessAttempts_Parcels_ParcelId
                    FOREIGN KEY (ParcelId) REFERENCES Parcels (Id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS IX_ProcessAttempts_ParcelId_OccurredAt
                ON ProcessAttempts (ParcelId, OccurredAt);
            CREATE INDEX IF NOT EXISTS IX_ProcessAttempts_ParcelId_Step_AttemptNumber
                ON ProcessAttempts (ParcelId, Step, AttemptNumber);
            """);
    }
    finally
    {
        if (shouldClose)
            await connection.CloseAsync();
    }
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
            Console.WriteLine("4. Process / reprocess parcel");
            Console.WriteLine("5. Mark parcel physically ready at depot");
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
            else if (choice == "4")
                await ProcessParcelAsync();
            else if (choice == "5")
                await MarkDepotReadyAsync();
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
        var credentials = await db.AllocationCredentials.AsNoTracking().ToListAsync();
        var dispatches = await db.NotificationDispatches.AsNoTracking().ToListAsync();
        var invalidDestinationParcelIds = (await db.ParcelEvents.AsNoTracking()
            .Where(x => x.EventType == "ReturnedToSenderInvalidDestination")
            .Select(x => x.ParcelId)
            .Distinct()
            .ToListAsync()).ToHashSet();
        Console.WriteLine("\\nPARCEL OVERVIEW - ONE ROW PER PARCEL");
        Console.WriteLine(new string('=', 145));
        Console.WriteLine($"{"Parcel ID",-18} {"Recipient",-20} {"PIN",-12} {"Compartment",-28} {"SMS",-12} {"E-mail",-12} {"Final status",-25}");
        Console.WriteLine(new string('-', 145));

        foreach (var parcel in parcels)
        {
            recipients.TryGetValue(parcel.Id, out var recipient);
            var allocation = parcel.CurrentAllocation;
            var pinCreated = allocation is not null &&
                credentials.Any(x => x.AllocationId == allocation.Id);
            var pinStatus = allocation is null ? "—" : pinCreated ? "Created" : "Missing";
            var compartmentStatus = allocation is null
                ? "Not booked"
                : allocation.IsReleased ? "Released" : "Booked";
            List<NotificationDispatchRecord> allocationDispatches = allocation is null
                ? []
                : dispatches.Where(x => x.AllocationId == allocation.Id).ToList();
            var smsStatus = FormatChannelStatus(allocationDispatches, NotificationChannel.Sms);
            var emailStatus = FormatChannelStatus(allocationDispatches, NotificationChannel.Email);
            var finalStatus = invalidDestinationParcelIds.Contains(parcel.Id)
                ? "Returned - invalid destination"
                : GetFinalStatus(parcel.Status, allocation, pinCreated, allocationDispatches);
            var compartment = allocation is null
                ? "—"
                : $"{allocation.LockerId} / {allocation.CompartmentId}";

            Console.WriteLine($"{Clip(parcel.ExternalParcelId, 18),-18} {Clip(recipient?.Name ?? "Unknown", 20),-20} {pinStatus,-12} {Clip(compartmentStatus + ": " + compartment, 28),-28} {smsStatus,-12} {emailStatus,-12} {finalStatus,-25}");
        }

        Console.WriteLine(new string('-', 145));
        Console.WriteLine($"Total parcels: {parcels.Count}");
        Console.WriteLine("\\nEnter a Parcel ID to inspect details and exact failures, or press Enter to return:");
        var parcelId = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(parcelId))
            await ShowParcelDetailsAsync(parcelId);
    }

    private static string FormatChannelStatus(
        IReadOnlyCollection<NotificationDispatchRecord> dispatches,
        NotificationChannel channel)
    {
        var attempts = dispatches.Where(x => x.Channel == channel).ToList();
        if (attempts.Count == 0) return "Not sent";
        if (attempts.Any(x => x.Status == NotificationDispatchStatus.Accepted)) return "Accepted";
        if (attempts.Any(x => x.Status == NotificationDispatchStatus.Failed)) return "Failed";
        return "Rejected";
    }

    private static string GetFinalStatus(
        ParcelStatus parcelStatus,
        Allocation? allocation,
        bool pinCreated,
        IReadOnlyCollection<NotificationDispatchRecord> dispatches)
    {
        if (parcelStatus == ParcelStatus.PickedUp) return "Picked up";
        if (parcelStatus == ParcelStatus.ReturnedToSender) return "Returned to sender";
        if (parcelStatus == ParcelStatus.ReturnPending) return "Return pending";
        if (parcelStatus == ParcelStatus.AvailableForPickup) return "Ready for pickup";
        if (parcelStatus == ParcelStatus.Registered && allocation is null) return "Registered";
        if (allocation is null) return "Waiting for locker";
        if (!pinCreated) return "Process failed - PIN missing";
        if (dispatches.Count > 0 && !dispatches.Any(x => x.Status == NotificationDispatchStatus.Accepted))
            return "Process failed - notification";
        if (dispatches.Any(x => x.Status == NotificationDispatchStatus.Accepted))
            return "Processing - not pickup-ready";
        return "Compartment booked";
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
            .ToListAsync();
        history = history.OrderBy(x => x.OccurredAt).ToList();
        var dispatches = await db.NotificationDispatches.AsNoTracking()
            .Where(x => parcel.Allocations.Select(a => a.Id).Contains(x.AllocationId))
            .ToListAsync();
        dispatches = dispatches.OrderBy(x => x.AttemptedAt).ToList();
        var credentials = await db.AllocationCredentials.AsNoTracking()
            .Where(x => parcel.Allocations.Select(a => a.Id).Contains(x.AllocationId))
            .ToListAsync();
        var processAttempts = await db.ProcessAttempts.AsNoTracking()
            .Where(x => x.ParcelId == parcel.Id)
            .ToListAsync();
        processAttempts = processAttempts.OrderBy(x => x.OccurredAt).ToList();

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
                {
                    Console.WriteLine($"  {dispatch.AttemptedAt:O} | {dispatch.Channel} | {dispatch.Status} | {dispatch.ExternalDispatchId ?? "No external ID"}");
                    if (!string.IsNullOrWhiteSpace(dispatch.ErrorCode))
                        Console.WriteLine($"    Error code: {dispatch.ErrorCode}");
                    if (dispatch.HttpStatusCode.HasValue)
                        Console.WriteLine($"    HTTP: {dispatch.HttpStatusCode}");
                    if (!string.IsNullOrWhiteSpace(dispatch.ErrorMessage))
                        Console.WriteLine($"    Error: {dispatch.ErrorMessage}");
                    if (!string.IsNullOrWhiteSpace(dispatch.ResponseBody))
                        Console.WriteLine($"    API response: {dispatch.ResponseBody}");
                }
        }

        Console.WriteLine("\nPROCESS ATTEMPTS / DIAGNOSTICS");
        if (processAttempts.Count == 0)
            Console.WriteLine("  No process attempts have been recorded.");
        foreach (var attempt in processAttempts)
        {
            Console.WriteLine($"  {attempt.OccurredAt:O} | {attempt.Step} | Attempt {attempt.AttemptNumber} | {attempt.Status} | Correlation: {attempt.CorrelationId}");
            if (attempt.HttpStatusCode.HasValue)
                Console.WriteLine($"    HTTP: {attempt.HttpStatusCode}");
            if (!string.IsNullOrWhiteSpace(attempt.ErrorCode))
                Console.WriteLine($"    Error code: {attempt.ErrorCode}");
            if (!string.IsNullOrWhiteSpace(attempt.ErrorMessage))
                Console.WriteLine($"    Error: {attempt.ErrorMessage}");
            if (!string.IsNullOrWhiteSpace(attempt.ResponseBody))
                Console.WriteLine($"    API response: {attempt.ResponseBody}");
        }

        Console.WriteLine("\nEVENT HISTORY");
        foreach (var item in history)
            Console.WriteLine($"  {item.OccurredAt:O} | {item.EventType} | {item.Description ?? "—"} | Correlation: {item.CorrelationId?.ToString() ?? "—"}");

        Pause();
    }

    private async Task MarkDepotReadyAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LockerDbContext>();
        Console.Write("Enter Parcel ID physically received and ready at depot: ");
        var externalParcelId = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(externalParcelId))
            return;

        var parcel = await db.Parcels.SingleOrDefaultAsync(
            x => x.ExternalParcelId == externalParcelId);
        if (parcel is null)
        {
            WriteError($"Parcel '{externalParcelId}' was not found.");
            Pause();
            return;
        }

        try
        {
            parcel.AcceptAtDepot();
            await db.SaveChangesAsync();
            var repository = scope.ServiceProvider.GetRequiredService<
                LBLocker.BusinessLogic.Interfaces.IParcelRepository>();
            await repository.RecordEventAsync(
                parcel.Id,
                "ParcelPhysicallyReadyAtDepot",
                DateTimeOffset.UtcNow,
                "Operator explicitly confirmed that the parcel is physically present and ready for locker processing.");

            Console.WriteLine("Depot readiness recorded. Processing parcel...");
            var processor = scope.ServiceProvider.GetRequiredService<
                LBLocker.Infrastructure.Processing.ParcelProcessingService>();
            await processor.ProcessAsync(externalParcelId);
            Console.WriteLine("Processing run completed. Check parcel details for individual step results.");
        }
        catch (Exception exception)
        {
            WriteError($"Could not mark/process parcel: {exception.Message}");
        }

        Pause();
    }

    private async Task ProcessParcelAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LockerDbContext>();
        var parcels = await db.Parcels.AsNoTracking()
            .OrderBy(x => x.ExternalParcelId)
            .ToListAsync();

        Console.WriteLine("\\nPARCELS AVAILABLE FOR PROCESSING / REPROCESSING");
        Console.WriteLine(new string('-', 90));
        Console.WriteLine($"{"Parcel ID",-24} {"Status",-22} {"Requested locker",-24} {"Size",-8}");
        Console.WriteLine(new string('-', 90));
        foreach (var parcel in parcels)
            Console.WriteLine($"{Clip(parcel.ExternalParcelId, 24),-24} {parcel.Status,-22} {Clip(parcel.TargetLockerId ?? "MISSING", 24),-24} {parcel.Size,-8}");

        Console.Write("\\nEnter Parcel ID to process or reprocess (blank to cancel): ");
        var parcelId = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(parcelId))
            return;

        try
        {
            var processor = scope.ServiceProvider.GetRequiredService<
                LBLocker.Infrastructure.Processing.ParcelProcessingService>();
            await processor.ProcessAsync(parcelId);
            Console.WriteLine("Processing run completed. Check parcel details for individual step results.");
        }
        catch (Exception exception)
        {
            WriteError($"Processing stopped: {exception.Message}");
        }

        Pause();
    }

    private async Task ShowImportHistoryAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LockerDbContext>();
        var imports = await db.ImportedEvents.AsNoTracking().ToListAsync();
        imports = imports.OrderByDescending(x => x.ImportedAt).ToList();

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

            // Only newly created or updated parcels that were already
            // explicitly marked physically ready at the depot are auto-processed.
            var eligibleParcelIds = result.Items
                .Where(x => x.Disposition is
                    LBLocker.BusinessLogic.Contracts.ImportRecordDisposition.ParcelCreated or
                    LBLocker.BusinessLogic.Contracts.ImportRecordDisposition.ParcelUpdated)
                .Select(x => x.ExternalParcelId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var db = scope.ServiceProvider.GetRequiredService<LockerDbContext>();
            var eligibleParcels = await db.Parcels
                .Where(x => eligibleParcelIds.Contains(x.ExternalParcelId))
                .ToListAsync();

            // A CSV import represents parcels physically received and ready
            // for locker processing. Promote newly registered parcels to depot
            // readiness before attempting allocation and notification.
            var readyParcelIds = new List<string>();
            foreach (var parcel in eligibleParcels)
            {
                if (parcel.Status == ParcelStatus.Registered)
                {
                    parcel.AcceptAtDepot();
                    db.ParcelEvents.Add(new ParcelEventRecord
                    {
                        Id = Guid.NewGuid(),
                        ParcelId = parcel.Id,
                        EventType = "ParcelPhysicallyReadyAtDepot",
                        OccurredAt = DateTimeOffset.UtcNow,
                        RecordedAt = DateTimeOffset.UtcNow,
                        Description = "CSV import treated as confirmation that the parcel is physically present and ready for locker processing."
                    });
                    readyParcelIds.Add(parcel.ExternalParcelId);
                }
                else if (parcel.Status is ParcelStatus.AcceptedAtDepot or
                         ParcelStatus.InTransit or ParcelStatus.AvailableForPickup)
                {
                    // Updated source snapshots may also resume a prior run:
                    // e.g. retry capacity allocation or a failed notification channel.
                    readyParcelIds.Add(parcel.ExternalParcelId);
                }
            }
            await db.SaveChangesAsync();

            // Revisit only parcels already flagged for invalid-destination return.
            // This recovers older ReturnPending records without touching expiry returns.
            var invalidDestinationReturnIds = await db.ParcelEvents
                .Where(x => x.EventType == "ReturnToSenderRequiredInvalidDestination" ||
                            x.EventType == "ReturnedToSenderInvalidDestination")
                .Join(db.Parcels, e => e.ParcelId, p => p.Id, (e, p) => new { e.ParcelId, p.ExternalParcelId, p.Status })
                .Where(x => x.Status == ParcelStatus.ReturnPending)
                .Select(x => x.ExternalParcelId)
                .Distinct()
                .ToListAsync();
            readyParcelIds.AddRange(invalidDestinationReturnIds);
            readyParcelIds = readyParcelIds.Distinct(StringComparer.Ordinal).ToList();

            var processed = 0;
            var processingFailures = new List<string>();
            var processor = scope.ServiceProvider.GetRequiredService<
                LBLocker.Infrastructure.Processing.ParcelProcessingService>();
            foreach (var readyParcelId in readyParcelIds)
            {
                try
                {
                    await processor.ProcessAsync(readyParcelId);
                    processed++;
                }
                catch (Exception processingException)
                {
                    processingFailures.Add($"{readyParcelId}: {processingException.Message}");
                }
            }

            Console.WriteLine("\nIMPORT RESULT");
            Console.WriteLine(new string('-', 100));
            Console.WriteLine($"{"EventId",-24} {"ParcelId",-24} {"Timestamp",-22} Result");
            Console.WriteLine(new string('-', 100));
            foreach (var item in result.Items)
                Console.WriteLine($"{Clip(item.EventId, 24),-24} {Clip(item.ExternalParcelId, 24),-24} {item.Timestamp:yyyy-MM-dd HH:mm:ss} {Describe(item.Disposition)}");

            Console.WriteLine(new string('-', 100));
            Console.WriteLine($"Rows read:                  {result.RowsRead}");
            Console.WriteLine($"Parcels created:             {result.ParcelsCreated}");
            Console.WriteLine($"Parcels updated:              {result.ParcelsUpdated}");
            Console.WriteLine($"Rows with no changes skipped: {result.NoChangesSkipped}");
            Console.WriteLine($"Duplicate events skipped:    {result.DuplicateEventsSkipped}");
            Console.WriteLine($"Incomplete rows rejected:    {result.IncompleteRowsSkipped}");
            Console.WriteLine($"Auto-processed (depot-ready):  {processed}");
            Console.WriteLine($"Auto-processing failures:     {processingFailures.Count}");
            foreach (var failure in processingFailures)
                WriteError($"  {failure}");
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
            LBLocker.BusinessLogic.Contracts.ImportRecordDisposition.ParcelUpdated => "UPDATED",
            LBLocker.BusinessLogic.Contracts.ImportRecordDisposition.NoChangesSkipped => "NO CHANGES - SKIPPED",
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
