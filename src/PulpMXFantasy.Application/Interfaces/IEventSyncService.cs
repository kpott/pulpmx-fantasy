namespace PulpMXFantasy.Application.Interfaces;

/// <summary>
/// Service for synchronizing event and rider data from external APIs.
/// </summary>
/// <remarks>
/// WHY THIS INTERFACE EXISTS:
/// ==========================
/// Application layer interface for event synchronization operations:
/// - Abstracts API sync logic from controllers/handlers
/// - Enables TDD/unit testing with mocks
/// - Coordinates between external API and database
///
/// IMPLEMENTATIONS:
/// ================
/// - EventSyncService (Infrastructure): PulpMX API integration
/// </remarks>
public interface IEventSyncService
{
    /// <summary>
    /// Syncs the next upcoming event from API to database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if sync succeeded, false if API unavailable or no events</returns>
    /// <remarks>
    /// Steps:
    /// 1. Call API to get next event with riders
    /// 2. Find or create Series for event
    /// 3. Find or create Event
    /// 4. For each rider: find or create Rider, create/update EventRider
    /// 5. Save all changes in single transaction
    /// </remarks>
    Task<bool> SyncNextEventAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs a specific historical event by slug (for training data).
    /// </summary>
    /// <param name="eventSlug">Event slug (e.g., "anaheim-1-2025")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if sync succeeded</returns>
    Task<bool> SyncHistoricalEventAsync(string eventSlug, CancellationToken cancellationToken = default);
}
