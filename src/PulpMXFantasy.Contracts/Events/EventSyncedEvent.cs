namespace PulpMXFantasy.Contracts.Events;

/// <summary>
/// Published after an event is successfully synchronized from the PulpMX API.
/// </summary>
/// <param name="EventId">Internal event identifier</param>
/// <param name="EventName">Human-readable event name (e.g., "Anaheim 1")</param>
/// <param name="EventSlug">API slug identifier (e.g., "anaheim-1-2025-sx")</param>
/// <param name="EventDate">Date of the event</param>
/// <param name="RiderCount">Number of riders in the event</param>
public record EventSyncedEvent(
    Guid EventId,
    string EventName,
    string EventSlug,
    DateTimeOffset EventDate,
    int RiderCount);
