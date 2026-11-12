using LBLocker.Domain.Parcels;

namespace LBLocker.BusinessLogic.Contracts;

/// <summary>
/// One parsed row from the incoming parcel CSV.
/// This represents source data, not a confirmed parcel lifecycle event.
/// </summary>
public sealed record ParcelImportRecord(
    string EventId,
    DateTimeOffset Timestamp,
    string ExternalParcelId,
    string RecipientId,
    string RecipientName,
    string? RecipientPhone,
    string RecipientEmail,
    string? RecipientPostalCode,
    string RetailerId,
    string? TargetLockerId,
    ParcelSize? Size);
