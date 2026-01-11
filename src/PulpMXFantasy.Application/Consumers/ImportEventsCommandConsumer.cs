using MassTransit;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.Commands;
using PulpMXFantasy.Contracts.Events;
using PulpMXFantasy.Contracts.Interfaces;

namespace PulpMXFantasy.Application.Consumers;

/// <summary>
/// Handles ImportEventsCommand by sequentially syncing historical events from PulpMX API.
/// </summary>
/// <remarks>
/// WHY SEQUENTIAL PROCESSING:
/// ==========================
/// The PulpMX API has rate limiting. Processing events in parallel would:
/// 1. Hit rate limits and fail
/// 2. Overload the database with concurrent writes
/// 3. Make progress tracking confusing
///
/// RESILIENCE STRATEGY:
/// ====================
/// - If one event fails, continue with the next
/// - Track successes and failures separately
/// - Complete the command even if some events fail
/// - Individual failures are logged but don't stop batch
///
/// EVENT PUBLISHING:
/// =================
/// For each successful sync, publishes EventSyncedEvent.
/// Downstream consumers (e.g., ML training) can react to new data.
///
/// PROGRESS TRACKING:
/// ==================
/// Updates ICommandStatusService with percentage progress:
/// - After each event: (completed / total) * 100
/// - Shows which event is currently being processed
/// </remarks>
public class ImportEventsCommandConsumer : IConsumer<ImportEventsCommand>
{
    private readonly IEventSyncService _eventSyncService;
    private readonly ICommandStatusService _commandStatusService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ImportEventsCommandConsumer> _logger;

    /// <summary>
    /// Command type identifier for status tracking.
    /// </summary>
    public const string CommandType = "ImportEvents";

    public ImportEventsCommandConsumer(
        IEventSyncService eventSyncService,
        ICommandStatusService commandStatusService,
        IPublishEndpoint publishEndpoint,
        ILogger<ImportEventsCommandConsumer> logger)
    {
        _eventSyncService = eventSyncService ?? throw new ArgumentNullException(nameof(eventSyncService));
        _commandStatusService = commandStatusService ?? throw new ArgumentNullException(nameof(commandStatusService));
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes the ImportEventsCommand by syncing each event sequentially.
    /// </summary>
    public async Task Consume(ConsumeContext<ImportEventsCommand> context)
    {
        var command = context.Message;
        var correlationId = context.CorrelationId ?? Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var cancellationToken = context.CancellationToken;

        _logger.LogInformation(
            "Starting ImportEventsCommand: {SlugCount} events to import, CorrelationId={CorrelationId}",
            command.EventSlugs.Count,
            correlationId);

        // Validate input
        if (command.EventSlugs == null || command.EventSlugs.Count == 0)
        {
            _logger.LogWarning("ImportEventsCommand received with empty slug list");
            return;
        }

        // Create command status record
        await _commandStatusService.CreateAsync(
            commandId,
            correlationId,
            CommandType,
            cancellationToken);

        var successCount = 0;
        var failedSlugs = new List<string>();
        var totalCount = command.EventSlugs.Count;

        // Process each event sequentially
        for (var i = 0; i < totalCount; i++)
        {
            var slug = command.EventSlugs[i];
            var eventNumber = i + 1;

            try
            {
                _logger.LogInformation(
                    "Processing event {EventNumber}/{TotalCount}: {EventSlug}",
                    eventNumber,
                    totalCount,
                    slug);

                // Update progress before processing
                await _commandStatusService.UpdateProgressAsync(
                    commandId,
                    $"Syncing event {eventNumber}/{totalCount}: {slug}",
                    CalculateProgressPercentage(i, totalCount),
                    cancellationToken);

                // Sync the event
                var success = await _eventSyncService.SyncHistoricalEventAsync(slug, cancellationToken);

                if (success)
                {
                    successCount++;

                    // Publish event for downstream consumers
                    await _publishEndpoint.Publish(
                        new EventSyncedEvent(
                            EventId: Guid.NewGuid(),
                            EventName: slug, // Will be replaced with actual name from sync
                            EventSlug: slug,
                            EventDate: DateTimeOffset.UtcNow, // Will be replaced with actual date
                            RiderCount: 0), // Will be replaced with actual count
                        cancellationToken);

                    _logger.LogInformation(
                        "Successfully synced event {EventSlug} ({EventNumber}/{TotalCount})",
                        slug,
                        eventNumber,
                        totalCount);
                }
                else
                {
                    failedSlugs.Add(slug);
                    _logger.LogWarning(
                        "Failed to sync event {EventSlug} ({EventNumber}/{TotalCount})",
                        slug,
                        eventNumber,
                        totalCount);
                }

                // Update progress after processing
                await _commandStatusService.UpdateProgressAsync(
                    commandId,
                    $"Completed {eventNumber}/{totalCount}: {slug}",
                    CalculateProgressPercentage(eventNumber, totalCount),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                failedSlugs.Add(slug);
                _logger.LogError(
                    ex,
                    "Error syncing event {EventSlug} ({EventNumber}/{TotalCount}): {ErrorMessage}",
                    slug,
                    eventNumber,
                    totalCount,
                    ex.Message);

                // Continue with next event - don't let one failure stop the batch
            }
        }

        // Complete the command with results
        var resultData = new
        {
            TotalRequested = totalCount,
            SuccessCount = successCount,
            FailedCount = failedSlugs.Count,
            FailedSlugs = failedSlugs
        };

        var completionMessage = $"Imported {successCount}/{totalCount} events" +
            (failedSlugs.Count > 0 ? $" ({failedSlugs.Count} failed)" : "");
        await _commandStatusService.CompleteAsync(commandId, resultData, completionMessage, cancellationToken);

        _logger.LogInformation(
            "ImportEventsCommand completed: {SuccessCount}/{TotalCount} succeeded, {FailedCount} failed",
            successCount,
            totalCount,
            failedSlugs.Count);
    }

    /// <summary>
    /// Calculates progress percentage based on completed events.
    /// </summary>
    /// <param name="completedCount">Number of events completed</param>
    /// <param name="totalCount">Total number of events</param>
    /// <returns>Progress percentage (0-100)</returns>
    private static int CalculateProgressPercentage(int completedCount, int totalCount)
    {
        if (totalCount == 0) return 100;
        return (int)Math.Round((double)completedCount / totalCount * 100);
    }
}
