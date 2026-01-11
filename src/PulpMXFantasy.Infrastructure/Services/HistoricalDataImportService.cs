using Microsoft.Extensions.Logging;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Infrastructure.ExternalApi;

namespace PulpMXFantasy.Infrastructure.Services;

/// <summary>
/// Service for importing historical race data for ML model training.
/// </summary>
/// <remarks>
/// WHY THIS SERVICE EXISTS:
/// ========================
/// ML models require historical data to train. This service:
/// - Imports past event results from PulpMX API
/// - Populates database with rider performance history
/// - Provides training data for LightGBM predictor
///
/// IMPORT STRATEGY:
/// ================
/// 1. Accept list of event slugs (e.g., "anaheim1-2024", "oakland-2024")
/// 2. For each event, call EventSyncService to sync riders and results
/// 3. Track success/failure for each event
/// 4. Return import summary with statistics
///
/// EVENT SLUG FORMAT:
/// ==================
/// PulpMX API uses slugs like:
/// - "anaheim1-2024" (Supercross)
/// - "hangtown-2024" (Motocross)
/// - "lasvegas-smx-25" (SuperMotocross)
///
/// You can find event slugs by:
/// 1. Browsing https://www.pulpmxfantasy.com
/// 2. Calling GET /v2/events API endpoint
/// 3. Checking past season schedules
///
/// EXAMPLE USAGE:
/// ==============
/// <code>
/// // Import 2024 Supercross season
/// var eventSlugs = new[] {
///     "anaheim1-2024", "san-diego-2024", "anaheim2-2024",
///     "glendale-2024", "oakland-2024", // ... etc
/// };
///
/// var result = await _importService.ImportHistoricalEventsAsync(eventSlugs);
/// // Result: 17 succeeded, 0 failed, 425 riders imported
/// </code>
/// </remarks>
public class HistoricalDataImportService
{
    private readonly IEventSyncService _eventSyncService;
    private readonly IPulpMXApiClient _apiClient;
    private readonly ILogger<HistoricalDataImportService> _logger;

    public HistoricalDataImportService(
        IEventSyncService eventSyncService,
        IPulpMXApiClient apiClient,
        ILogger<HistoricalDataImportService> logger)
    {
        _eventSyncService = eventSyncService;
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <summary>
    /// Imports multiple historical events in sequence.
    /// </summary>
    /// <param name="eventSlugs">List of event slugs to import</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result summary</returns>
    /// <remarks>
    /// This method processes events sequentially to avoid overwhelming the API.
    /// For large imports (50+ events), consider adding delay between requests.
    ///
    /// Each event import is independent - if one fails, others continue.
    /// Check the result object for detailed success/failure breakdown.
    /// </remarks>
    public async Task<ImportResult> ImportHistoricalEventsAsync(
        IEnumerable<string> eventSlugs,
        CancellationToken cancellationToken = default)
    {
        var slugList = eventSlugs.ToList();
        var result = new ImportResult();

        _logger.LogInformation(
            "Starting historical data import for {EventCount} events",
            slugList.Count);

        foreach (var slug in slugList)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Import cancelled by user at event {Slug}", slug);
                break;
            }

            try
            {
                _logger.LogInformation("Importing event: {Slug}", slug);

                // Sync the event using existing service
                var success = await _eventSyncService.SyncHistoricalEventAsync(
                    slug,
                    cancellationToken);

                if (success)
                {
                    result.SuccessfulEvents.Add(slug);
                    _logger.LogInformation("✓ Successfully imported: {Slug}", slug);
                }
                else
                {
                    result.FailedEvents.Add(slug, "Sync returned false (possibly no data)");
                    _logger.LogWarning("✗ Failed to import: {Slug}", slug);
                }

                // Small delay to be nice to the API (avoid rate limiting)
                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex)
            {
                result.FailedEvents.Add(slug, ex.Message);
                _logger.LogError(
                    ex,
                    "✗ Error importing event {Slug}: {Message}",
                    slug,
                    ex.Message);

                // Continue with next event even if this one failed
            }
        }

        _logger.LogInformation(
            "Historical import complete: {Success} succeeded, {Failed} failed",
            result.SuccessfulEvents.Count,
            result.FailedEvents.Count);

        return result;
    }

    /// <summary>
    /// Imports a complete season by discovering and syncing all events.
    /// </summary>
    /// <param name="seriesType">Series type (Supercross, Motocross, SuperMotocross)</param>
    /// <param name="year">Year (e.g., 2024)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result summary</returns>
    /// <remarks>
    /// NOTE: This requires an API endpoint to list events by series/year.
    /// Current PulpMX API may not support this - check API documentation.
    ///
    /// Alternative: Manually provide event slugs from season schedule.
    /// </remarks>
    public async Task<ImportResult> ImportSeasonAsync(
        string seriesType,
        int year,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement season discovery via API
        // For now, this requires manual event slug list
        _logger.LogWarning(
            "Automatic season import not yet implemented. " +
            "Use ImportHistoricalEventsAsync with manual event slug list.");

        return new ImportResult
        {
            FailedEvents = new Dictionary<string, string>
            {
                ["season-import"] = "Not implemented - use manual event list"
            }
        };
    }

    /// <summary>
    /// Gets a list of completed events from the PulpMX API.
    /// </summary>
    /// <param name="limit">Maximum number of events to fetch (default: 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of completed event slugs ordered by most recent first</returns>
    /// <remarks>
    /// This method dynamically fetches completed events from the API,
    /// eliminating the need for hardcoded event lists.
    ///
    /// The API returns events ordered by lockout date descending (most recent first).
    /// This is useful for:
    /// - Displaying available events to users
    /// - Importing recent historical data
    /// - Building training datasets
    ///
    /// Example usage:
    /// <code>
    /// var recentEvents = await _importService.GetCompletedEventsAsync(limit: 50);
    /// // Returns: ["lasvegas-smx-24", "milwaukee-smx-24", "concord-smx-24", ...]
    /// </code>
    /// </remarks>
    public async Task<List<string>> GetCompletedEventsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching up to {Limit} completed events from API", limit);

            var events = await _apiClient.GetEventsAsync(
                status: "complete",
                orderBy: "lockout",
                orderDir: "DESC",
                limit: limit,
                cancellationToken: cancellationToken);

            var eventSlugs = events
                .Where(e => !string.IsNullOrEmpty(e.Slug))
                .Select(e => e.Slug!)
                .ToList();

            _logger.LogInformation(
                "Successfully fetched {Count} completed events",
                eventSlugs.Count);

            return eventSlugs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching completed events from API");

            // Return empty list on error - caller can handle as needed
            return new List<string>();
        }
    }

    /// <summary>
    /// Gets a list of recommended event slugs for common seasons.
    /// </summary>
    /// <param name="season">Season identifier (e.g., "2024-supercross")</param>
    /// <returns>List of event slugs</returns>
    /// <remarks>
    /// HARDCODED EVENT LISTS:
    /// These are manually curated from PulpMX Fantasy website.
    /// Update these lists as new seasons are added.
    ///
    /// NOTE: Consider using GetCompletedEventsAsync() for dynamic event discovery
    /// instead of relying on these hardcoded lists.
    /// </remarks>
    public static IEnumerable<string> GetSeasonEventSlugs(string season)
    {
        return season.ToLowerInvariant() switch
        {
            "2024-supercross" => new[]
            {
                "anaheim1-2024",
                "san-diego-2024",
                "anaheim2-2024",
                "glendale-2024",
                "oakland-2024",
                "san-francisco-2024",
                "arlington-2024",
                "tampa-2024",
                "detroit-2024",
                "indianapolis-2024",
                "seattle-2024",
                "st-louis-2024",
                "birmingham-2024",
                "daytona-2024",
                "philadelphia-2024",
                "denver-2024",
                "foxborough-2024",
                "nashville-2024",
                "dallas-2024",
                "detroit2-2024",
                "salt-lake-city-2024"
            },

            "2024-motocross" => new[]
            {
                "hangtown-2024",
                "thunder-valley-2024",
                "high-point-2024",
                "southwick-2024",
                "red-bud-2024",
                "spring-creek-2024",
                "washougal-2024",
                "unadilla-2024",
                "budds-creek-2024",
                "ironman-2024"
            },

            "2024-smx" => new[]
            {
                "concord-smx-24",
                "milwaukee-smx-24",
                "lasvegas-smx-24"
            },

            _ => Array.Empty<string>()
        };
    }
}

/// <summary>
/// Result of historical data import operation.
/// </summary>
public class ImportResult
{
    /// <summary>
    /// List of event slugs that were successfully imported.
    /// </summary>
    public List<string> SuccessfulEvents { get; set; } = new();

    /// <summary>
    /// Dictionary of event slugs that failed, with error messages.
    /// </summary>
    public Dictionary<string, string> FailedEvents { get; set; } = new();

    /// <summary>
    /// Total number of events processed.
    /// </summary>
    public int TotalEvents => SuccessfulEvents.Count + FailedEvents.Count;

    /// <summary>
    /// Success rate as percentage (0-100).
    /// </summary>
    public int SuccessRate => TotalEvents == 0
        ? 0
        : (int)((SuccessfulEvents.Count / (double)TotalEvents) * 100);
}
