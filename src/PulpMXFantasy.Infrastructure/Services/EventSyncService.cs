using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.ReadModels;
using PulpMXFantasy.Domain.Entities;
using PulpMXFantasy.Domain.Enums;
using PulpMXFantasy.Infrastructure.Data;
using PulpMXFantasy.Infrastructure.ExternalApi;
using PulpMXFantasy.Infrastructure.Mappers;

namespace PulpMXFantasy.Infrastructure.Services;

/// <summary>
/// Service for synchronizing event and rider data from PulpMX API to database.
/// </summary>
/// <remarks>
/// WHY THIS SERVICE EXISTS:
/// ========================
/// Orchestrates the complex process of syncing data from external API to database:
/// 1. Fetch data from PulpMX API (via IPulpMXApiClient)
/// 2. Map API DTOs to domain entities (via ApiMapper)
/// 3. Determine what's new vs. what needs updating
/// 4. Save to database with proper relationships
/// 5. Handle errors gracefully with fallback strategies
///
/// This is a APPLICATION LAYER service because it:
/// - Coordinates between Infrastructure (API, Database)
/// - Implements business logic (when to sync, what to sync)
/// - Does NOT contain domain logic (that's in entities)
/// - Does NOT directly access infrastructure (uses abstractions)
///
/// SYNC STRATEGY:
/// ==============
/// 1. **Incremental Sync** - Only sync what changed
///    - Check if event exists by Slug
///    - Check if rider exists by PulpMxId
///    - Update existing records, insert new ones
///
/// 2. **Idempotent Operations** - Safe to run multiple times
///    - Same data synced twice = no duplicates
///    - Database constraints prevent duplicates (unique indexes)
///    - Can safely retry after failures
///
/// 3. **Transaction Boundaries** - All-or-nothing
///    - Event + riders + event_riders synced together
///    - If any part fails, entire sync rolls back
///    - Prevents partial/inconsistent data
///
/// ERROR HANDLING:
/// ===============
/// - API unavailable → Log error, return false, retry later
/// - Database error → Transaction rolls back, throw exception
/// - Mapping error → Log error with details, skip invalid records
/// - Partial success → Log warnings, continue with valid data
///
/// USAGE:
/// ======
/// Called by:
/// - Background service (scheduled sync every hour)
/// - Admin endpoint (manual sync trigger)
/// - Startup (ensure latest data on app start)
/// </remarks>
public class EventSyncService : IEventSyncService
{
    private readonly IPulpMXApiClient _apiClient;
    private readonly ApplicationDbContext _dbContext;
    private readonly IReadModelUpdater _readModelUpdater;
    private readonly ILogger<EventSyncService> _logger;

    public EventSyncService(
        IPulpMXApiClient apiClient,
        ApplicationDbContext dbContext,
        IReadModelUpdater readModelUpdater,
        ILogger<EventSyncService> logger)
    {
        _apiClient = apiClient;
        _dbContext = dbContext;
        _readModelUpdater = readModelUpdater;
        _logger = logger;
    }

    /// <summary>
    /// Syncs the next upcoming event from API to database.
    /// </summary>
    /// <returns>True if sync succeeded, false if API unavailable or no events</returns>
    /// <remarks>
    /// Steps:
    /// 1. Call API to get next event with riders
    /// 2. Find or create Series for event
    /// 3. Find or create Event
    /// 4. For each rider: find or create Rider, create/update EventRider
    /// 5. Save all changes in single transaction
    ///
    /// Example usage:
    /// <code>
    /// public class BackgroundSyncService : BackgroundService
    /// {
    ///     protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    ///     {
    ///         while (!stoppingToken.IsCancellationRequested)
    ///         {
    ///             await _eventSyncService.SyncNextEventAsync(stoppingToken);
    ///             await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
    ///         }
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public async Task<bool> SyncNextEventAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting sync of next event from PulpMX API");

        try
        {
            // Step 1: Fetch next event from API
            var response = await _apiClient.GetNextEventAsync(cancellationToken);

            if (response == null)
            {
                _logger.LogInformation("No upcoming events found");
                return false;
            }

            // Convert NextEventRidersResponse to ApiEvent format
            var apiEvent = ConvertNextEventToApiEvent(response);
            _logger.LogInformation(
                "Fetched event: {EventName} on {EventDate} with {RiderCount} riders",
                apiEvent.Name,
                apiEvent.EventDate,
                apiEvent.Riders.Count);

            // Step 2: Find or create Series
            var series = await FindOrCreateSeriesAsync(apiEvent, cancellationToken);

            // Step 3: Find or create Event
            var eventEntity = await FindOrCreateEventAsync(apiEvent, series.Id, cancellationToken);

            // Step 4: Sync all riders for this event
            await SyncEventRidersAsync(apiEvent, eventEntity, cancellationToken);

            // Step 5: Save all changes in single transaction
            var changeCount = await _dbContext.SaveChangesAsync(cancellationToken);

            // Step 6: Update EventReadModel for Web UI (CQRS read model)
            await UpdateEventReadModelAsync(eventEntity, series, apiEvent.Riders.Count, cancellationToken);

            _logger.LogInformation(
                "Successfully synced event {EventName}: {ChangeCount} changes saved",
                apiEvent.Name,
                changeCount);

            return true;
        }
        catch (PulpMXApiException ex)
        {
            _logger.LogError(ex, "API error during sync: {ApiMessage}", ex.ApiMessage);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during sync");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during sync");
            throw; // Re-throw unexpected errors
        }
    }

    /// <summary>
    /// Syncs a specific historical event by slug (for training data).
    /// </summary>
    /// <param name="eventSlug">Event slug (e.g., "anaheim-1-2025")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if sync succeeded</returns>
    /// <remarks>
    /// Used for:
    /// - Importing historical results for ML training
    /// - Backfilling missing events
    /// - Re-syncing event if data was incorrect
    ///
    /// Example usage:
    /// <code>
    /// // Import all 2024 Supercross events for training
    /// var events = await _apiClient.GetSeriesEventsAsync(2024, "Supercross");
    /// foreach (var evt in events.Where(e => e.IsCompleted))
    /// {
    ///     await _eventSyncService.SyncHistoricalEventAsync(evt.Slug);
    /// }
    /// </code>
    /// </remarks>
    public async Task<bool> SyncHistoricalEventAsync(
        string eventSlug,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Syncing historical event: {EventSlug}", eventSlug);

        try
        {
            // Fetch event with results from API
            var response = await _apiClient.GetEventWithResultsAsync(eventSlug, cancellationToken);

            // Convert new API structure to internal ApiEvent format
            var apiEvent = ConvertToApiEvent(response);

            // Same sync process as next event
            var series = await FindOrCreateSeriesAsync(apiEvent, cancellationToken);
            var eventEntity = await FindOrCreateEventAsync(apiEvent, series.Id, cancellationToken);
            await SyncEventRidersAsync(apiEvent, eventEntity, cancellationToken);

            // Calculate fantasy points for completed events
            if (apiEvent.IsCompleted)
            {
                await CalculateFantasyPointsAsync(eventEntity, cancellationToken);
            }

            var changeCount = await _dbContext.SaveChangesAsync(cancellationToken);

            // Update EventReadModel for Web UI (CQRS read model)
            await UpdateEventReadModelAsync(eventEntity, series, apiEvent.Riders.Count, cancellationToken);

            _logger.LogInformation(
                "Successfully synced historical event {EventSlug}: {ChangeCount} changes",
                eventSlug,
                changeCount);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing historical event {EventSlug}", eventSlug);
            return false;
        }
    }

    /// <summary>
    /// Converts NextEventRidersResponse to the internal ApiEvent format.
    /// </summary>
    /// <remarks>
    /// The next event endpoint returns basic event info and riders separated by class.
    /// We merge riders into a single collection for internal processing.
    /// </remarks>
    private ExternalApi.Models.ApiEvent ConvertNextEventToApiEvent(ExternalApi.Models.NextEventRidersResponse response)
    {
        var eventInfo = response.NextEventInfo;

        // Parse lockout timestamp
        var lockoutTimestamp = DateTimeOffset.FromUnixTimeSeconds(eventInfo.Lockout);

        // Determine series type from event type
        var seriesType = eventInfo.Type.ToLowerInvariant() switch
        {
            "sx" => "Supercross",
            "mx" => "Motocross",
            "smx" => "SuperMotocross",
            _ => "Supercross" // Default fallback
        };

        // Determine event format
        var eventFormat = eventInfo.Format.ToLowerInvariant() switch
        {
            "triple_crown" => "TripleCrown",
            "normal" => "Standard",
            _ => "Standard"
        };

        // Merge riders from both classes
        var allRiders = new List<ExternalApi.Models.ApiEventRider>();

        // Convert 250 riders
        foreach (var rider in response.Riders250)
        {
            allRiders.Add(ConvertToApiEventRider(rider, "250"));
        }

        // Convert 450 riders
        foreach (var rider in response.Riders450)
        {
            allRiders.Add(ConvertToApiEventRider(rider, "450"));
        }

        return new ExternalApi.Models.ApiEvent
        {
            Slug = eventInfo.Id,
            Name = eventInfo.Title,
            Venue = eventInfo.Title, // API doesn't separate venue from title
            Location = eventInfo.Title, // Use title as location for next event
            EventDate = lockoutTimestamp,
            RoundNumber = eventInfo.SeriesRound,
            SeriesType = seriesType,
            EventFormat = eventFormat,
            Division = "Combined", // Next event doesn't specify division
            IsCompleted = false, // Next event is always upcoming
            LockoutTime = lockoutTimestamp, // Store official lockout time
            Riders = allRiders
        };
    }

    /// <summary>
    /// Converts the new API response structure to the internal ApiEvent format.
    /// </summary>
    /// <remarks>
    /// The API returns riders separated by class (riders250, riders450).
    /// We merge them into a single collection for internal processing.
    /// </remarks>
    private ExternalApi.Models.ApiEvent ConvertToApiEvent(ExternalApi.Models.EventWithResultsResponse response)
    {
        var eventData = response.EventData;

        // Parse lockout timestamp (must be UTC for PostgreSQL)
        var lockoutTimestamp = long.TryParse(eventData.Lockout, out var lockoutSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(lockoutSeconds)
            : DateTimeOffset.UtcNow;

        // Determine series type from event type
        var seriesType = eventData.Type.ToLowerInvariant() switch
        {
            "sx" => "Supercross",
            "mx" => "Motocross",
            "smx" => "SuperMotocross",
            _ => "Supercross" // Default fallback
        };

        // Determine event format
        var eventFormat = eventData.Format.ToLowerInvariant() switch
        {
            "triple_crown" => "TripleCrown",
            "normal" => "Standard",
            _ => "Standard"
        };

        // Map division short codes to full names (API uses "w"/"e" for 2025 season)
        var division = (eventData.Region?.ToLowerInvariant() ?? "combined") switch
        {
            "w" or "west" => "West",
            "e" or "east" => "East",
            "showdown" => "Showdown",
            "" or null => "Combined",
            _ => "Combined" // Default fallback
        };

        // Merge riders from both classes
        var allRiders = new List<ExternalApi.Models.ApiEventRider>();

        // Convert 250 riders
        foreach (var rider in response.Riders250)
        {
            allRiders.Add(ConvertToApiEventRider(rider, "250"));
        }

        // Convert 450 riders
        foreach (var rider in response.Riders450)
        {
            allRiders.Add(ConvertToApiEventRider(rider, "450"));
        }

        return new ExternalApi.Models.ApiEvent
        {
            Slug = eventData.Id,
            Name = eventData.Title,
            Venue = eventData.Title, // API doesn't separate venue from title
            Location = eventData.Label,
            EventDate = lockoutTimestamp,
            RoundNumber = eventData.SeriesRound,
            SeriesType = seriesType,
            EventFormat = eventFormat,
            Division = division, // Mapped from short codes
            IsCompleted = eventData.Status.Equals("complete", StringComparison.OrdinalIgnoreCase),
            LockoutTime = lockoutTimestamp, // Store official lockout time
            Riders = allRiders
        };
    }

    /// <summary>
    /// Converts detailed API rider to simpler ApiEventRider format.
    /// </summary>
    /// <remarks>
    /// CRITICAL: Handles DNQ (Did Not Qualify) riders correctly.
    /// The API returns finishPosition=0 for riders who didn't make the main event.
    /// We must map this to NULL (not 0) so the ML qualification model can learn.
    ///
    /// Position meanings from API:
    /// - 0 = DNQ (did not qualify for main event)
    /// - 1-22 = Made main event (finished in this position)
    /// - 23+ = Made main but finished outside top 22 (rare)
    /// </remarks>
    private ExternalApi.Models.ApiEventRider ConvertToApiEventRider(
        ExternalApi.Models.ApiEventRiderDetailed detailed,
        string bikeClass)
    {
        // Handle finish position: 0 or null means DNQ (store as NULL)
        int? finishPosition = (detailed.FinishPosition ?? detailed.Position) switch
        {
            null => null,  // API returned null - DNQ
            0 => null,     // API returned 0 - DNQ
            int pos => pos // API returned 1-22 - made main event
        };

        // Handicap position is also 0 for DNQ riders
        int? handicapPosition = detailed.HandicapPosition == 0 ? null : detailed.HandicapPosition;

        return new ExternalApi.Models.ApiEventRider
        {
            PulpMxId = detailed.Slug,
            Name = detailed.Name,
            Number = detailed.NumberInt,
            PhotoUrl = detailed.ImageUrl,
            BikeClass = bikeClass,
            Handicap = detailed.Handicap,
            IsAllStar = detailed.AllStar,
            IsInjured = detailed.Injured,
            PickTrend = detailed.PickTrend,
            CombinedQualyPosition = detailed.CombinedQualyPosition,
            BestQualyLapSeconds = detailed.BestQualyLapSeconds,
            QualyGapToLeader = null, // Not provided in this endpoint
            FinishPosition = finishPosition,
            HandicapAdjustedPosition = handicapPosition,
            FantasyPoints = detailed.FantasyPoints
        };
    }

    /// <summary>
    /// Finds existing series or creates new one.
    /// </summary>
    private async Task<Series> FindOrCreateSeriesAsync(
        Infrastructure.ExternalApi.Models.ApiEvent apiEvent,
        CancellationToken cancellationToken)
    {
        var seriesType = ApiMapper.ParseEnum<SeriesType>(apiEvent.SeriesType, nameof(apiEvent.SeriesType));
        var year = apiEvent.EventDate.Year;

        // First, check local tracked entities
        var series = _dbContext.Series.Local
            .FirstOrDefault(s => s.Year == year && s.SeriesType == seriesType);

        if (series != null)
        {
            _logger.LogDebug("Found existing series in local cache: {SeriesName}", series.Name);
            return series;
        }

        // Not in local cache, check database
        series = await _dbContext.Series
            .FirstOrDefaultAsync(
                s => s.Year == year && s.SeriesType == seriesType,
                cancellationToken);

        if (series != null)
        {
            _logger.LogDebug("Found existing series: {SeriesName}", series.Name);
            return series;
        }

        // Create new series
        series = new Series
        {
            Id = Guid.NewGuid(),
            Name = $"{year} {seriesType}",
            SeriesType = seriesType,
            Year = year,
            StartDate = apiEvent.EventDate, // Will be updated as more events added
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Series.Add(series);
        _logger.LogInformation("Created new series: {SeriesName}", series.Name);

        return series;
    }

    /// <summary>
    /// Finds existing event or creates new one.
    /// </summary>
    private async Task<Event> FindOrCreateEventAsync(
        Infrastructure.ExternalApi.Models.ApiEvent apiEvent,
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        // First, check local tracked entities
        var eventEntity = _dbContext.Events.Local
            .FirstOrDefault(e => e.Slug == apiEvent.Slug);

        if (eventEntity != null)
        {
            _logger.LogDebug("Found existing event in local cache: {EventName}", eventEntity.Name);

            // Update event data (in case anything changed)
            eventEntity.Name = apiEvent.Name;
            eventEntity.Venue = apiEvent.Venue;
            eventEntity.Location = apiEvent.Location;
            eventEntity.EventDate = apiEvent.EventDate;
            eventEntity.IsCompleted = apiEvent.IsCompleted;
            eventEntity.LockoutTime = apiEvent.LockoutTime;
            eventEntity.UpdatedAt = DateTimeOffset.UtcNow;

            return eventEntity;
        }

        // Not in local cache, check database
        eventEntity = await _dbContext.Events
            .FirstOrDefaultAsync(e => e.Slug == apiEvent.Slug, cancellationToken);

        if (eventEntity != null)
        {
            _logger.LogDebug("Found existing event: {EventName}", eventEntity.Name);

            // Update event data (in case anything changed)
            eventEntity.Name = apiEvent.Name;
            eventEntity.Venue = apiEvent.Venue;
            eventEntity.Location = apiEvent.Location;
            eventEntity.EventDate = apiEvent.EventDate;
            eventEntity.IsCompleted = apiEvent.IsCompleted;
            eventEntity.LockoutTime = apiEvent.LockoutTime;
            eventEntity.UpdatedAt = DateTimeOffset.UtcNow;

            return eventEntity;
        }

        // Create new event
        eventEntity = ApiMapper.MapToEvent(apiEvent, seriesId);
        _dbContext.Events.Add(eventEntity);
        _logger.LogInformation("Created new event: {EventName}", eventEntity.Name);

        return eventEntity;
    }

    /// <summary>
    /// Syncs all riders for an event.
    /// </summary>
    private async Task SyncEventRidersAsync(
        Infrastructure.ExternalApi.Models.ApiEvent apiEvent,
        Event eventEntity,
        CancellationToken cancellationToken)
    {
        foreach (var apiRider in apiEvent.Riders)
        {
            try
            {
                // Find or create rider
                var rider = await FindOrCreateRiderAsync(apiRider, cancellationToken);

                // Find or create event rider
                await FindOrCreateEventRiderAsync(apiRider, eventEntity.Id, rider.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error syncing rider {RiderName} for event {EventName}, skipping",
                    apiRider.Name,
                    apiEvent.Name);
                // Continue with other riders
            }
        }
    }

    /// <summary>
    /// Finds existing rider or creates new one.
    /// </summary>
    private async Task<Rider> FindOrCreateRiderAsync(
        Infrastructure.ExternalApi.Models.ApiEventRider apiRider,
        CancellationToken cancellationToken)
    {
        // First, check local tracked entities (includes riders added but not yet saved in this transaction)
        var rider = _dbContext.Riders.Local
            .FirstOrDefault(r => r.PulpMxId == apiRider.PulpMxId);

        if (rider != null)
        {
            // Update rider data (name, number, photo may change)
            rider.Name = apiRider.Name;
            rider.Number = apiRider.Number;
            rider.PhotoUrl = apiRider.PhotoUrl;
            rider.UpdatedAt = DateTimeOffset.UtcNow;

            return rider;
        }

        // Not in local cache, check database
        rider = await _dbContext.Riders
            .FirstOrDefaultAsync(r => r.PulpMxId == apiRider.PulpMxId, cancellationToken);

        if (rider != null)
        {
            // Update rider data (name, number, photo may change)
            rider.Name = apiRider.Name;
            rider.Number = apiRider.Number;
            rider.PhotoUrl = apiRider.PhotoUrl;
            rider.UpdatedAt = DateTimeOffset.UtcNow;

            return rider;
        }

        // Create new rider
        rider = ApiMapper.MapToRider(apiRider);
        _dbContext.Riders.Add(rider);
        _logger.LogInformation("Created new rider: {RiderName}", rider.Name);

        return rider;
    }

    /// <summary>
    /// Finds existing event rider or creates new one.
    /// </summary>
    private async Task<EventRider> FindOrCreateEventRiderAsync(
        Infrastructure.ExternalApi.Models.ApiEventRider apiRider,
        Guid eventId,
        Guid riderId,
        CancellationToken cancellationToken)
    {
        // First, check local tracked entities (includes event riders added but not yet saved in this transaction)
        var eventRider = _dbContext.EventRiders.Local
            .FirstOrDefault(er => er.EventId == eventId && er.RiderId == riderId);

        if (eventRider != null)
        {
            // Update existing event rider
            ApiMapper.UpdateEventRider(eventRider, apiRider);
            return eventRider;
        }

        // Not in local cache, check database
        eventRider = await _dbContext.EventRiders
            .FirstOrDefaultAsync(
                er => er.EventId == eventId && er.RiderId == riderId,
                cancellationToken);

        if (eventRider != null)
        {
            // Update existing event rider
            ApiMapper.UpdateEventRider(eventRider, apiRider);
            return eventRider;
        }

        // Create new event rider
        eventRider = ApiMapper.MapToEventRider(apiRider, eventId, riderId);
        _dbContext.EventRiders.Add(eventRider);

        return eventRider;
    }

    /// <summary>
    /// Calculates fantasy points for all riders in a completed event.
    /// </summary>
    /// <remarks>
    /// CRITICAL: This method must process ALL riders, including DNQ (Did Not Qualify).
    /// - Riders with FinishPosition (made main event): Calculate fantasy points using domain logic
    /// - Riders without FinishPosition (DNQ): Set FantasyPoints = 0
    ///
    /// WHY THIS MATTERS:
    /// The ML qualification model REQUIRES DNQ data to learn patterns.
    /// Without DNQ examples, the model has 100% qualification rate and learns nothing (AUC=0.50).
    ///
    /// IMPORTANT: Uses Local collection to include tracked but unsaved entities.
    /// </remarks>
    private Task CalculateFantasyPointsAsync(
        Event eventEntity,
        CancellationToken cancellationToken)
    {
        // Get ALL event riders from tracked entities (includes unsaved entities)
        // Can't use async ToListAsync() here since Local is IEnumerable, not IQueryable
        var eventRiders = _dbContext.EventRiders.Local
            .Where(er => er.EventId == eventEntity.Id)
            .ToList();

        int madeMainCount = 0;
        int dnqCount = 0;

        foreach (var eventRider in eventRiders)
        {
            try
            {
                if (eventRider.FinishPosition.HasValue)
                {
                    // Rider made main event - calculate fantasy points using domain logic
                    eventRider.CalculateFantasyPoints();
                    madeMainCount++;
                }
                else
                {
                    // Rider DNQ - no fantasy points earned
                    eventRider.FantasyPoints = 0;
                    dnqCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error calculating fantasy points for rider {RiderId} in event {EventId}",
                    eventRider.RiderId,
                    eventRider.EventId);
            }
        }

        _logger.LogInformation(
            "Calculated fantasy points for event {EventName}: {MadeMain} made main, {DNQ} DNQ",
            eventEntity.Name,
            madeMainCount,
            dnqCount);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates the EventReadModel for the Web UI (CQRS read model).
    /// </summary>
    /// <remarks>
    /// CQRS Pattern: After syncing to write model (ApplicationDbContext),
    /// also update the read model so Web UI can display the event.
    ///
    /// This ensures Web UI has up-to-date event data without accessing
    /// the write model directly (enforced at assembly level).
    /// </remarks>
    private async Task UpdateEventReadModelAsync(
        Event eventEntity,
        Series series,
        int riderCount,
        CancellationToken cancellationToken)
    {
        try
        {
            var eventReadModel = new EventReadModel
            {
                Id = eventEntity.Id,
                Name = eventEntity.Name,
                Slug = eventEntity.Slug,
                Venue = eventEntity.Venue,
                Location = eventEntity.Location,
                EventDate = eventEntity.EventDate,
                SeriesName = series.Name,
                SeasonYear = series.Year,
                IsCompleted = eventEntity.IsCompleted,
                LockoutTime = eventEntity.LockoutTime,
                RiderCount = riderCount,
                SyncedAt = DateTimeOffset.UtcNow
            };

            await _readModelUpdater.UpdateEventAsync(eventReadModel, cancellationToken);

            _logger.LogDebug(
                "Updated EventReadModel for event: {EventName}",
                eventEntity.Name);
        }
        catch (Exception ex)
        {
            // Log but don't fail the sync - read model update is eventual consistency
            _logger.LogWarning(ex,
                "Failed to update EventReadModel for event {EventName}, will retry on next sync",
                eventEntity.Name);
        }
    }
}
