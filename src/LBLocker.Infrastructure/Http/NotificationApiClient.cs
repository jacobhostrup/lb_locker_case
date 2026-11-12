using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LBLocker.BusinessLogic.Contracts;
using LBLocker.BusinessLogic.Interfaces;

namespace LBLocker.Infrastructure.Http;

public sealed class NotificationApiClient(HttpClient httpClient) : INotificationApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<NotificationDispatchResult> SendNotificationAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new NotificationRequestDto
        {
            RecipientId = request.RecipientId,
            ParcelId = request.ParcelId,
            LockerId = request.LockerId,
            Channel = request.Channel == NotificationChannel.Sms ? "SMS" : "EMAIL",
            PinCode = request.PinCode,
            TextBody = request.TextBody
        };

        using var response = await httpClient.PostAsJsonAsync(
            "notifications", payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<NotificationResponseDto>(
            JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Notification API returned an empty response.");

        var channel = result.DeliveredChannel.ToUpperInvariant() switch
        {
            "SMS" => NotificationChannel.Sms,
            "EMAIL" => NotificationChannel.Email,
            _ => throw new FormatException(
                $"Notification API returned unknown channel '{result.DeliveredChannel}'.")
        };

        return new NotificationDispatchResult(
            result.NotificationId,
            result.RecipientId,
            result.ParcelId,
            channel,
            result.DispatchedAt);
    }

    private sealed class NotificationRequestDto
    {
        [JsonPropertyName("recipient_id")] public string RecipientId { get; set; } = "";
        [JsonPropertyName("parcel_id")] public string ParcelId { get; set; } = "";
        [JsonPropertyName("locker_id")] public string LockerId { get; set; } = "";
        [JsonPropertyName("channel")] public string Channel { get; set; } = "";
        [JsonPropertyName("pin_code")] public string? PinCode { get; set; }
        [JsonPropertyName("text_body")] public string? TextBody { get; set; }
    }

    private sealed class NotificationResponseDto
    {
        [JsonPropertyName("notification_id")] public string NotificationId { get; set; } = "";
        [JsonPropertyName("recipient_id")] public string RecipientId { get; set; } = "";
        [JsonPropertyName("parcel_id")] public string ParcelId { get; set; } = "";
        [JsonPropertyName("delivered_channel")] public string DeliveredChannel { get; set; } = "";
        [JsonPropertyName("dispatched_at")] public DateTimeOffset DispatchedAt { get; set; }
    }
}
