using MassTransit;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.Commands;
using PulpMXFantasy.Contracts.Events;
using System.Text.Json;

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
/// PROGRESS TRACKING (EVENT-DRIVEN):
/// =================================
/// Publishes status events via ConsumeContext:
/// - CommandStartedEvent at start
/// - CommandProgressUpdatedEvent after each event
/// - CommandCompletedEvent or CommandFailedEvent at end
/// Web consumer receives these and pushes to SignalR.
/// </remarks>
public class ImportEventsCommandConsumer : IConsumer<ImportEventsCommand>
{
    private readonly IEventSyncService _eventSyncService;
    private readonly ILogger<ImportEventsCommandConsumer> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Command type identifier for status tracking.
    /// </summary>
    public const string CommandType = "ImportEvents";

    public ImportEventsCommandConsumer(
        IEventSyncService eventSyncService,
        ILogger<ImportEventsCommandConsumer> logger)
    {
        _eventSyncService = eventSyncService ?? throw new ArgumentNullException(nameof(eventSyncService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes the ImportEventsCommand by syncing each event sequentially.
    /// </summary>
    public async Task Consume(ConsumeContext<ImportEventsCommand> context)
    {
        var command = context.Message;
        var commandId = context.MessageId ?? Guid.NewGuid();
        var cancellationToken = context.CancellationToken;

        _logger.LogInformation(
            "Starting ImportEventsCommand: {SlugCount} events to import, CorrelationId={CorrelationId}",
            command.EventSlugs.Count,
            context.CorrelationId);

        // Validate input
        if (command.EventSlugs == null || command.EventSlugs.Count == 0)
        {
            _logger.LogWarning("ImportEventsCommand received with empty slug list");
            return;
        }

        // Publish CommandStartedEvent
        await context.Publish(new CommandStartedEvent(
            commandId,
            CommandType,
            DateTimeOffset.UtcNow), cancellationToken);

        var successCount = 0;
        var failedSlugs = new List<string>();
        var totalCount = command.EventSlugs.Count;

        try
        {
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

                    // Publish progress update before processing
                    await context.Publish(new CommandProgressUpdatedEvent(
                        commandId,
                        $"Syncing event {eventNumber}/{totalCount}: {slug}",
                        CalculateProgressPercentage(i, totalCount),
                        DateTimeOffset.UtcNow,
                        $"Processing_{slug}"), cancellationToken);

                    // Sync the event
                    var success = await _eventSyncService.SyncHistoricalEventAsync(slug, cancellationToken);

                    if (success)
                    {
                        successCount++;

                        // Publish event for downstream consumers
                        await context.Publish(
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

                    // Publish progress update after processing
                    await context.Publish(new CommandProgressUpdatedEvent(
                        commandId,
                        $"Completed {eventNumber}/{totalCount}: {slug}",
                        CalculateProgressPercentage(eventNumber, totalCount),
                        DateTimeOffset.UtcNow,
                        $"Completed_{slug}"), cancellationToken);
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

            // Publish CommandCompletedEvent with results
            var resultData = new
            {
                TotalRequested = totalCount,
                SuccessCount = successCount,
                FailedCount = failedSlugs.Count,
                FailedSlugs = failedSlugs
            };

            var completionMessage = $"Imported {successCount}/{totalCount} events" +
                (failedSlugs.Count > 0 ? $" ({failedSlugs.Count} failed)" : "");

            await context.Publish(new CommandCompletedEvent(
                commandId,
                DateTimeOffset.UtcNow,
                completionMessage,
                JsonSerializer.Serialize(resultData, JsonOptions)), cancellationToken);

            _logger.LogInformation(
                "ImportEventsCommand completed: {SuccessCount}/{TotalCount} succeeded, {FailedCount} failed",
                successCount,
                totalCount,
                failedSlugs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing ImportEventsCommand: CommandId={CommandId}, Error={Error}",
                commandId,
                ex.Message);

            // Publish CommandFailedEvent - do NOT rethrow to prevent MassTransit retry
            await context.Publish(new CommandFailedEvent(
                commandId,
                DateTimeOffset.UtcNow,
                ex.Message,
                ex.GetType().Name), cancellationToken);
        }
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
