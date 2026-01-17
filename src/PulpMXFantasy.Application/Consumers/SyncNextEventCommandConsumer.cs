using System.Net;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.Commands;
using PulpMXFantasy.Contracts.Events;

namespace PulpMXFantasy.Application.Consumers;

/// <summary>
/// Handles SyncNextEventCommand messages from the message bus.
/// </summary>
/// <remarks>
/// WHY THIS HANDLER EXISTS:
/// ========================
/// MassTransit consumer that orchestrates next event synchronization:
/// 1. Publishes status events (Started -> Progress -> Completed/Failed)
/// 2. Calls EventSyncService to sync from PulpMX API
/// 3. Publishes EventSyncedEvent on success (triggers downstream workflows)
///
/// DESIGN DECISIONS:
/// =================
///
/// 1. **Does NOT rethrow exceptions**
///    - Publishes CommandFailedEvent instead of rethrowing
///    - Prevents MassTransit retry storm
///    - Allows UI to display error to user via SignalR
///
/// 2. **Uses context.Publish() for status events**
///    - Preserves CorrelationId from incoming command
///    - Maintains message chain for tracing
///    - Web consumer receives events and pushes to SignalR
///
/// 3. **Event-driven status tracking**
///    - Publishes CommandStartedEvent immediately
///    - Publishes CommandProgressUpdatedEvent during work
///    - Publishes CommandCompletedEvent or CommandFailedEvent at end
///
/// TRIGGERED BY:
/// =============
/// - Admin UI "Sync Next Event" button
/// - Scheduled background job (hourly before race day)
/// - Manual CLI command for testing
///
/// PUBLISHES ON SUCCESS:
/// =====================
/// - EventSyncedEvent: Triggers prediction generation workflow
/// </remarks>
public class SyncNextEventCommandConsumer : IConsumer<SyncNextEventCommand>
{
    private readonly IEventSyncService _eventSyncService;
    private readonly ILogger<SyncNextEventCommandConsumer> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a new SyncNextEventCommandConsumer instance.
    /// </summary>
    /// <param name="eventSyncService">Service for syncing events from API</param>
    /// <param name="logger">Logger instance</param>
    public SyncNextEventCommandConsumer(
        IEventSyncService eventSyncService,
        ILogger<SyncNextEventCommandConsumer> logger)
    {
        _eventSyncService = eventSyncService ?? throw new ArgumentNullException(nameof(eventSyncService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the SyncNextEventCommand.
    /// </summary>
    /// <param name="context">MassTransit consume context containing the command</param>
    public async Task Consume(ConsumeContext<SyncNextEventCommand> context)
    {
        var command = context.Message;
        var commandId = context.MessageId ?? Guid.NewGuid();
        var cancellationToken = context.CancellationToken;

        _logger.LogInformation(
            "Processing SyncNextEventCommand: CommandId={CommandId}, CorrelationId={CorrelationId}",
            commandId,
            context.CorrelationId);

        // Publish CommandStartedEvent
        await context.Publish(new CommandStartedEvent(
            commandId,
            "SyncNextEvent",
            DateTimeOffset.UtcNow), cancellationToken);

        try
        {
            // Publish progress update
            await context.Publish(new CommandProgressUpdatedEvent(
                commandId,
                "Syncing next event from PulpMX API...",
                10,
                DateTimeOffset.UtcNow,
                "FetchingAPI"), cancellationToken);

            // Execute the sync
            var syncResult = await _eventSyncService.SyncNextEventAsync(cancellationToken);

            if (syncResult)
            {
                _logger.LogInformation(
                    "Successfully synced next event: CommandId={CommandId}",
                    commandId);

                // Publish EventSyncedEvent for downstream workflows
                // Note: We use placeholder values since EventSyncService doesn't return event details
                // In a real implementation, EventSyncService would return the synced event info
                var eventSyncedEvent = new EventSyncedEvent(
                    EventId: Guid.NewGuid(), // Would come from sync result
                    EventName: "Next Event", // Would come from sync result
                    EventSlug: "next-event", // Would come from sync result
                    EventDate: DateTimeOffset.UtcNow, // Would come from sync result
                    RiderCount: 0); // Would come from sync result

                await context.Publish(eventSyncedEvent, cancellationToken);

                // Publish CommandCompletedEvent
                var resultData = new { Synced = true, Message = "Next event synced successfully" };
                await context.Publish(new CommandCompletedEvent(
                    commandId,
                    DateTimeOffset.UtcNow,
                    "Event synced successfully",
                    JsonSerializer.Serialize(resultData, JsonOptions)), cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "No event to sync or API unavailable: CommandId={CommandId}",
                    commandId);

                // Publish CommandCompletedEvent with indication that nothing was synced
                var resultData = new { Synced = false, Message = "No upcoming event found or API unavailable" };
                await context.Publish(new CommandCompletedEvent(
                    commandId,
                    DateTimeOffset.UtcNow,
                    "No upcoming event found",
                    JsonSerializer.Serialize(resultData, JsonOptions)), cancellationToken);
            }
        }
        catch (HttpRequestException ex) when (IsTransient(ex))
        {
            _logger.LogWarning(
                ex,
                "Transient HTTP failure during sync, will retry: CommandId={CommandId}, StatusCode={StatusCode}",
                commandId,
                ex.StatusCode);

            // Rethrow to trigger MassTransit retry
            throw;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(
                ex,
                "Timeout during sync, will retry: CommandId={CommandId}",
                commandId);

            // Rethrow to trigger MassTransit retry
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing SyncNextEventCommand: CommandId={CommandId}, Error={Error}",
                commandId,
                ex.Message);

            // Publish CommandFailedEvent - do NOT rethrow for non-transient errors
            await context.Publish(new CommandFailedEvent(
                commandId,
                DateTimeOffset.UtcNow,
                ex.Message,
                ex.GetType().Name), cancellationToken);
        }
    }

    /// <summary>
    /// Determines if an HTTP exception is transient and should be retried.
    /// </summary>
    private static bool IsTransient(HttpRequestException ex) =>
        ex.StatusCode is HttpStatusCode.TooManyRequests // 429
            or HttpStatusCode.ServiceUnavailable // 503
            or HttpStatusCode.BadGateway // 502
            or HttpStatusCode.GatewayTimeout; // 504
}
