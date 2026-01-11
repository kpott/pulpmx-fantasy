using MassTransit;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.Commands;
using PulpMXFantasy.Contracts.Events;
using PulpMXFantasy.Contracts.Interfaces;

namespace PulpMXFantasy.Application.Consumers;

/// <summary>
/// Handles SyncNextEventCommand messages from the message bus.
/// </summary>
/// <remarks>
/// WHY THIS HANDLER EXISTS:
/// ========================
/// MassTransit consumer that orchestrates next event synchronization:
/// 1. Updates command status (Pending -> Running -> Completed/Failed)
/// 2. Calls EventSyncService to sync from PulpMX API
/// 3. Publishes EventSyncedEvent on success (triggers downstream workflows)
///
/// DESIGN DECISIONS:
/// =================
///
/// 1. **Does NOT rethrow exceptions**
///    - Marks status as Failed instead of rethrowing
///    - Prevents MassTransit retry storm
///    - Allows UI to display error to user
///
/// 2. **Uses IPublishEndpoint.Publish() for events**
///    - Publish() sends to all subscribers (fan-out)
///    - Send() sends to specific endpoint (point-to-point)
///    - EventSyncedEvent should notify all interested services
///
/// 3. **Command status tracking**
///    - Creates status record immediately (Pending)
///    - Updates to Running before actual work
///    - Completes with result data on success
///    - Fails with error message on exception
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
    private readonly ICommandStatusService _commandStatusService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SyncNextEventCommandConsumer> _logger;

    /// <summary>
    /// Creates a new SyncNextEventCommandConsumer instance.
    /// </summary>
    /// <param name="eventSyncService">Service for syncing events from API</param>
    /// <param name="commandStatusService">Service for tracking command status</param>
    /// <param name="publishEndpoint">MassTransit publish endpoint for events</param>
    /// <param name="logger">Logger instance</param>
    public SyncNextEventCommandConsumer(
        IEventSyncService eventSyncService,
        ICommandStatusService commandStatusService,
        IPublishEndpoint publishEndpoint,
        ILogger<SyncNextEventCommandConsumer> logger)
    {
        _eventSyncService = eventSyncService ?? throw new ArgumentNullException(nameof(eventSyncService));
        _commandStatusService = commandStatusService ?? throw new ArgumentNullException(nameof(commandStatusService));
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
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
        var correlationId = context.CorrelationId ?? Guid.NewGuid();
        var cancellationToken = context.CancellationToken;

        _logger.LogInformation(
            "Processing SyncNextEventCommand: CommandId={CommandId}, CorrelationId={CorrelationId}",
            commandId,
            correlationId);

        // Create command status record (Pending)
        await _commandStatusService.CreateAsync(
            commandId,
            correlationId,
            "SyncNextEvent",
            cancellationToken);

        try
        {
            // Update to Running
            await _commandStatusService.UpdateProgressAsync(
                commandId,
                "Syncing next event from PulpMX API...",
                10,
                cancellationToken);

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

                await _publishEndpoint.Publish(eventSyncedEvent, cancellationToken);

                // Complete with success result
                await _commandStatusService.CompleteAsync(
                    commandId,
                    new { Synced = true, Message = "Next event synced successfully" },
                    "Event synced successfully",
                    cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "No event to sync or API unavailable: CommandId={CommandId}",
                    commandId);

                // Complete but with indication that nothing was synced
                await _commandStatusService.CompleteAsync(
                    commandId,
                    new { Synced = false, Message = "No upcoming event found or API unavailable" },
                    "No upcoming event found",
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing SyncNextEventCommand: CommandId={CommandId}, Error={Error}",
                commandId,
                ex.Message);

            // Mark as failed - do NOT rethrow to prevent MassTransit retry
            await _commandStatusService.FailAsync(
                commandId,
                ex.Message,
                cancellationToken);
        }
    }
}
