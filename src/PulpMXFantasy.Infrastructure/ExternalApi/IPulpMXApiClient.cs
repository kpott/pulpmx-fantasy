using PulpMXFantasy.Infrastructure.ExternalApi.Models;

namespace PulpMXFantasy.Infrastructure.ExternalApi;

/// <summary>
/// Client for interacting with the PulpMX Fantasy API.
/// </summary>
/// <remarks>
/// WHY THIS INTERFACE EXISTS:
/// ==========================
/// Following Clean Architecture principles:
/// - Infrastructure defines the interface for external dependencies
/// - Allows mocking for tests (no real API calls needed)
/// - Decouples API implementation from usage
///
/// PULPMX FANTASY API:
/// ===================
/// Base URL: https://api.pulpmxfantasy.com
/// Authentication: API token in Authorization header
///
/// Key Endpoints:
/// - GET /v2/events/next/riders - Get upcoming event with rider data
/// - GET /v2/events/{slug}/riders-with-sessions - Get event results
/// - GET /v2/series/{year}/{type} - Get series information
///
/// RETRY LOGIC:
/// ============
/// Implementation includes:
/// - 3 retries with exponential backoff (2s, 4s, 8s)
/// - Circuit breaker (breaks after 5 consecutive failures)
/// - Timeout per request (30 seconds)
///
/// This ensures resilience against temporary API outages or network issues.
///
/// ERROR HANDLING:
/// ===============
/// Methods throw specific exceptions:
/// - HttpRequestException: Network/HTTP errors
/// - PulpMXApiException: API returned error (success = false)
/// - TimeoutException: Request exceeded timeout
/// - JsonException: Invalid JSON response
///
/// Callers should handle these exceptions and implement fallback strategies.
/// </remarks>
public interface IPulpMXApiClient
{
    /// <summary>
    /// Gets the next upcoming event with all rider data.
    /// </summary>
    /// <returns>Next event with riders, qualifying data, handicaps</returns>
    /// <remarks>
    /// Used for:
    /// - Generating predictions for upcoming event
    /// - Displaying event information to users
    /// - Populating database with event and rider data
    ///
    /// Example usage:
    /// <code>
    /// var response = await _apiClient.GetNextEventAsync();
    /// var event = response.Event;
    /// var riders250 = event.Riders.Where(r => r.BikeClass == "250");
    /// </code>
    ///
    /// Returns null if no upcoming events scheduled.
    /// </remarks>
    Task<NextEventRidersResponse?> GetNextEventAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific event by slug with results and session data.
    /// </summary>
    /// <param name="eventSlug">Event slug (e.g., "anaheim-1-2025")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Event with results and qualifying sessions</returns>
    /// <remarks>
    /// Used for:
    /// - Training ML models (need historical results)
    /// - Displaying past event results
    /// - Calculating prediction accuracy
    ///
    /// Example usage:
    /// <code>
    /// var response = await _apiClient.GetEventWithResultsAsync("anaheim-1-2025");
    /// var completedRiders = response.Event.Riders.Where(r => r.FinishPosition.HasValue);
    /// </code>
    ///
    /// Throws exception if event slug not found.
    /// </remarks>
    Task<EventWithResultsResponse> GetEventWithResultsAsync(
        string eventSlug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all events for a specific series (e.g., "2025 Supercross").
    /// </summary>
    /// <param name="year">Year (e.g., 2025)</param>
    /// <param name="seriesType">Series type ("Supercross", "Motocross", "SuperMotocross")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of events in the series</returns>
    /// <remarks>
    /// Used for:
    /// - Displaying series schedule
    /// - Bulk importing event data
    /// - Training ML models across entire series
    ///
    /// Example usage:
    /// <code>
    /// var events = await _apiClient.GetSeriesEventsAsync(2025, "Supercross");
    /// var completedEvents = events.Where(e => e.IsCompleted).ToList();
    /// </code>
    ///
    /// Returns empty list if no events found for series.
    /// </remarks>
    Task<List<ApiEvent>> GetSeriesEventsAsync(
        int year,
        string seriesType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of events with optional filtering and ordering.
    /// </summary>
    /// <param name="status">Filter by status ("complete", "upcoming", etc.)</param>
    /// <param name="orderBy">Field to order by ("lockout", "event_date", etc.)</param>
    /// <param name="orderDir">Order direction ("ASC" or "DESC")</param>
    /// <param name="limit">Maximum number of events to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of events matching the criteria</returns>
    /// <remarks>
    /// Used for:
    /// - Discovering completed events for historical data import
    /// - Displaying event lists to users
    /// - Finding events to train ML models
    ///
    /// Example usage:
    /// <code>
    /// // Get last 100 completed events
    /// var events = await _apiClient.GetEventsAsync(
    ///     status: "complete",
    ///     orderBy: "lockout",
    ///     orderDir: "DESC",
    ///     limit: 100);
    /// </code>
    ///
    /// Returns empty list if no events match criteria.
    /// </remarks>
    Task<List<ApiEvent>> GetEventsAsync(
        string? status = null,
        string? orderBy = null,
        string? orderDir = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the API is accessible and healthy.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if API is healthy, false otherwise</returns>
    /// <remarks>
    /// Used for:
    /// - Health checks endpoint
    /// - Verifying API connectivity before sync operations
    /// - Alerting if API is down
    ///
    /// Example usage:
    /// <code>
    /// if (!await _apiClient.IsHealthyAsync())
    /// {
    ///     _logger.LogWarning("PulpMX API is unavailable, using cached data");
    ///     return _cachedData;
    /// }
    /// </code>
    ///
    /// Does not throw exceptions - returns false on any error.
    /// </remarks>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Exception thrown when PulpMX API returns an error response.
/// </summary>
public class PulpMXApiException : Exception
{
    /// <summary>
    /// HTTP status code from API response
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Error message from API
    /// </summary>
    public string? ApiMessage { get; }

    public PulpMXApiException(string message, int? statusCode = null, string? apiMessage = null)
        : base(message)
    {
        StatusCode = statusCode;
        ApiMessage = apiMessage;
    }

    public PulpMXApiException(string message, Exception innerException, int? statusCode = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
