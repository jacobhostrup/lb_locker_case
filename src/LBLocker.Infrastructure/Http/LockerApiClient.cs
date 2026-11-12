using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LBLocker.BusinessLogic.Contracts;
using LBLocker.BusinessLogic.Interfaces;
using LBLocker.Domain.Parcels;

namespace LBLocker.Infrastructure.Http;

public sealed class LockerApiClient(HttpClient httpClient) : ILockerApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<LockerInfo>> GetLockersAsync(
        CancellationToken cancellationToken = default)
    {
        var items = await httpClient.GetFromJsonAsync<List<LockerDto>>(
            "lockers", JsonOptions, cancellationToken) ?? [];
        return items.Select(x => new LockerInfo(x.LockerId, x.Address)).ToArray();
    }

    public async Task<IReadOnlyCollection<CompartmentInfo>> GetCompartmentsAsync(
        string lockerId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(lockerId, nameof(lockerId));
        var items = await httpClient.GetFromJsonAsync<List<CompartmentDto>>(
            $"lockers/{Uri.EscapeDataString(lockerId)}/compartments",
            JsonOptions, cancellationToken) ?? [];
        return items.Select(Map).ToArray();
    }

    public async Task<CompartmentInfo> GetCompartmentAsync(
        string lockerId,
        string compartmentId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(lockerId, nameof(lockerId));
        ValidateId(compartmentId, nameof(compartmentId));
        var item = await httpClient.GetFromJsonAsync<CompartmentDto>(
            $"lockers/{Uri.EscapeDataString(lockerId)}/compartments/{Uri.EscapeDataString(compartmentId)}",
            JsonOptions, cancellationToken);
        return item is null
            ? throw new InvalidOperationException("Locker API returned an empty compartment response.")
            : Map(item);
    }

    public async Task<LockerAllocationResult> AllocateCompartmentAsync(
        string lockerId,
        string externalParcelId,
        ParcelSize sizeRequired,
        string recipientId,
        string? pinCode = null,
        CancellationToken cancellationToken = default)
    {
        ValidateId(lockerId, nameof(lockerId));
        ValidateId(externalParcelId, nameof(externalParcelId));
        ValidateId(recipientId, nameof(recipientId));

        var request = new AllocationRequestDto
        {
            ParcelId = externalParcelId,
            SizeRequired = ToApiSize(sizeRequired),
            RecipientId = recipientId,
            PinCode = pinCode
        };

        using var response = await httpClient.PostAsJsonAsync(
            $"lockers/{Uri.EscapeDataString(lockerId)}/allocations",
            request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "Allocate compartment", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<AllocationResponseDto>(
            JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Locker API returned an empty allocation response.");

        return new LockerAllocationResult(
            result.AllocationId,
            result.LockerId,
            result.CompartmentId,
            result.PinCode,
            result.AllocatedAt,
            result.ExpiresAt);
    }

    public async Task ReleaseCompartmentAsync(
        string lockerId,
        string compartmentId,
        string externalParcelId,
        CompartmentReleaseReason reason,
        CancellationToken cancellationToken = default)
    {
        ValidateId(lockerId, nameof(lockerId));
        ValidateId(compartmentId, nameof(compartmentId));
        ValidateId(externalParcelId, nameof(externalParcelId));

        var request = new ReleaseRequestDto
        {
            ParcelId = externalParcelId,
            Reason = reason switch
            {
                CompartmentReleaseReason.CustomerPickedUp => "CUSTOMER_PICKED_UP",
                CompartmentReleaseReason.ExpiredReturnedToDepot => "EXPIRED_RETURNED_TO_DEPOT",
                CompartmentReleaseReason.MaintenanceOverride => "MAINTENANCE_OVERRIDE",
                _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
            }
        };

        using var response = await httpClient.PostAsJsonAsync(
            $"lockers/{Uri.EscapeDataString(lockerId)}/compartments/{Uri.EscapeDataString(compartmentId)}/release",
            request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "Release compartment", cancellationToken);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new LockerApiException(operation, response.StatusCode, response.ReasonPhrase, body);
    }

    private static CompartmentInfo Map(CompartmentDto item) =>
        new(item.CompartmentId, ParseSize(item.Size), item.OccupancyStatus,
            item.DoorState, item.SensorStatus);

    private static ParcelSize ParseSize(string value) => value.ToUpperInvariant() switch
    {
        "S" => ParcelSize.Small,
        "M" => ParcelSize.Medium,
        "L" => ParcelSize.Large,
        _ => throw new FormatException($"Locker API returned unknown compartment size '{value}'.")
    };

    private static string ToApiSize(ParcelSize value) => value switch
    {
        ParcelSize.Small => "S",
        ParcelSize.Medium => "M",
        ParcelSize.Large => "L",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static void ValidateId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A value is required.", parameterName);
    }

    private sealed class LockerDto
    {
        [JsonPropertyName("locker_id")] public string LockerId { get; set; } = "";
        [JsonPropertyName("address")] public string Address { get; set; } = "";
    }

    private sealed class CompartmentDto
    {
        [JsonPropertyName("compartment_id")] public string CompartmentId { get; set; } = "";
        [JsonPropertyName("size")] public string Size { get; set; } = "";
        [JsonPropertyName("occupancy_status")] public string OccupancyStatus { get; set; } = "";
        [JsonPropertyName("door_state")] public string DoorState { get; set; } = "";
        [JsonPropertyName("sensor_status")] public string SensorStatus { get; set; } = "";
    }

    private sealed class AllocationRequestDto
    {
        [JsonPropertyName("parcel_id")] public string ParcelId { get; set; } = "";
        [JsonPropertyName("size_required")] public string SizeRequired { get; set; } = "";
        [JsonPropertyName("recipient_id")] public string RecipientId { get; set; } = "";
        [JsonPropertyName("pin_code")] public string? PinCode { get; set; }
    }

    private sealed class AllocationResponseDto
    {
        [JsonPropertyName("allocation_id")] public string AllocationId { get; set; } = "";
        [JsonPropertyName("locker_id")] public string LockerId { get; set; } = "";
        [JsonPropertyName("compartment_id")] public string CompartmentId { get; set; } = "";
        [JsonPropertyName("pin_code")] public string? PinCode { get; set; }
        [JsonPropertyName("allocated_at")] public DateTimeOffset AllocatedAt { get; set; }
        [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed class ReleaseRequestDto
    {
        [JsonPropertyName("parcel_id")] public string ParcelId { get; set; } = "";
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    }
}