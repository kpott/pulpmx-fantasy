using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PulpMXFantasy.Contracts.Events;
using PulpMXFantasy.Contracts.ReadModels;
using PulpMXFantasy.ReadModel;
using PulpMXFantasy.Web.Hubs;

namespace PulpMXFantasy.Web.Consumers;

/// <summary>
/// Consumes command status events and updates the read model + pushes to SignalR.
/// </summary>
/// <remarks>
/// EVENT-DRIVEN STATUS TRACKING:
/// =============================
/// This consumer receives events from Worker consumers and:
/// 1. Writes status to the read model database
/// 2. Pushes real-time updates to connected SignalR clients
///
/// WHY IN WEB PROJECT:
/// ===================
/// Running this consumer in the Web project allows direct access to
/// IHubContext for pushing to SignalR clients without additional
/// infrastructure (Redis backplane, etc.).
/// </remarks>
public class CommandStatusEventConsumer :
    IConsumer<CommandStartedEvent>,
    IConsumer<CommandProgressUpdatedEvent>,
    IConsumer<CommandCompletedEvent>,
    IConsumer<CommandFailedEvent>
{
    private readonly ReadDbContext _readDbContext;
    private readonly IHubContext<AdminHub> _hubContext;
    private readonly ILogger<CommandStatusEventConsumer> _logger;

    public CommandStatusEventConsumer(
        ReadDbContext readDbContext,
        IHubContext<AdminHub> hubContext,
        ILogger<CommandStatusEventConsumer> logger)
    {
        _readDbContext = readDbContext;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CommandStartedEvent> context)
    {
        var evt = context.Message;
        var correlationId = context.CorrelationId ?? Guid.NewGuid();

        _logger.LogInformation(
            "Command started: CommandId={CommandId}, Type={CommandType}",
            evt.CommandId, evt.CommandType);

        try
        {
            // Create status record
            var status = new CommandStatusReadModel
            {
                CommandId = evt.CommandId,
                CorrelationId = correlationId,
                CommandType = evt.CommandType,
                Status = "Pending",
                ProgressMessage = "Starting...",
                ProgressPercentage = 0,
                StartedAt = evt.StartedAt
            };

            _readDbContext.CommandStatus.Add(status);

            // Add initial history entry
            var historyEntry = new CommandProgressHistoryReadModel
            {
                Id = Guid.NewGuid(),
                CommandId = evt.CommandId,
                Message = "Command started",
                ProgressPercentage = 0,
                OccurredAt = evt.StartedAt,
                MilestoneName = "Started"
            };
            _readDbContext.CommandProgressHistory.Add(historyEntry);

            await _readDbContext.SaveChangesAsync(context.CancellationToken);

            // Push to SignalR
            await _hubContext.Clients.Group("Admins").SendAsync("CommandStarted", new
            {
                evt.CommandId,
                evt.CommandType,
                evt.StartedAt,
                Status = "Pending",
                ProgressMessage = "Starting...",
                ProgressPercentage = 0
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process CommandStartedEvent for {CommandId}", evt.CommandId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<CommandProgressUpdatedEvent> context)
    {
        var evt = context.Message;

        _logger.LogDebug(
            "Command progress: CommandId={CommandId}, Progress={Progress}%",
            evt.CommandId, evt.ProgressPercentage);

        try
        {
            var status = await _readDbContext.CommandStatus
                .FirstOrDefaultAsync(c => c.CommandId == evt.CommandId, context.CancellationToken);

            if (status == null)
            {
                _logger.LogWarning("Command status not found: {CommandId}", evt.CommandId);
                return;
            }

            // Update status
            var updatedStatus = status with
            {
                Status = "Running",
                ProgressMessage = evt.ProgressMessage,
                ProgressPercentage = Math.Clamp(evt.ProgressPercentage, 0, 100)
            };
            _readDbContext.Entry(status).CurrentValues.SetValues(updatedStatus);

            // Add progress history entry
            var historyEntry = new CommandProgressHistoryReadModel
            {
                Id = Guid.NewGuid(),
                CommandId = evt.CommandId,
                Message = evt.ProgressMessage,
                ProgressPercentage = evt.ProgressPercentage,
                OccurredAt = evt.UpdatedAt,
                MilestoneName = evt.MilestoneName
            };
            _readDbContext.CommandProgressHistory.Add(historyEntry);

            await _readDbContext.SaveChangesAsync(context.CancellationToken);

            // Push to SignalR
            await _hubContext.Clients.Group("Admins").SendAsync("CommandProgress", new
            {
                evt.CommandId,
                evt.ProgressMessage,
                evt.ProgressPercentage,
                evt.MilestoneName,
                evt.UpdatedAt
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process CommandProgressUpdatedEvent for {CommandId}", evt.CommandId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<CommandCompletedEvent> context)
    {
        var evt = context.Message;

        _logger.LogInformation(
            "Command completed: CommandId={CommandId}",
            evt.CommandId);

        try
        {
            var status = await _readDbContext.CommandStatus
                .FirstOrDefaultAsync(c => c.CommandId == evt.CommandId, context.CancellationToken);

            if (status == null)
            {
                _logger.LogWarning("Command status not found: {CommandId}", evt.CommandId);
                return;
            }

            var durationMs = (long)(evt.CompletedAt - status.StartedAt).TotalMilliseconds;

            // Update status
            var updatedStatus = status with
            {
                Status = "Completed",
                ProgressPercentage = 100,
                ProgressMessage = evt.CompletionMessage,
                CompletedAt = evt.CompletedAt,
                ResultData = evt.ResultDataJson,
                DurationMs = durationMs
            };
            _readDbContext.Entry(status).CurrentValues.SetValues(updatedStatus);

            // Add completion history entry
            var historyEntry = new CommandProgressHistoryReadModel
            {
                Id = Guid.NewGuid(),
                CommandId = evt.CommandId,
                Message = evt.CompletionMessage,
                ProgressPercentage = 100,
                OccurredAt = evt.CompletedAt,
                MilestoneName = "Completed"
            };
            _readDbContext.CommandProgressHistory.Add(historyEntry);

            await _readDbContext.SaveChangesAsync(context.CancellationToken);

            // Push to SignalR
            await _hubContext.Clients.Group("Admins").SendAsync("CommandCompleted", new
            {
                evt.CommandId,
                evt.CompletedAt,
                evt.CompletionMessage,
                DurationMs = durationMs,
                ResultData = evt.ResultDataJson
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process CommandCompletedEvent for {CommandId}", evt.CommandId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<CommandFailedEvent> context)
    {
        var evt = context.Message;

        _logger.LogError(
            "Command failed: CommandId={CommandId}, Error={Error}",
            evt.CommandId, evt.ErrorMessage);

        try
        {
            var status = await _readDbContext.CommandStatus
                .FirstOrDefaultAsync(c => c.CommandId == evt.CommandId, context.CancellationToken);

            if (status == null)
            {
                _logger.LogWarning("Command status not found: {CommandId}", evt.CommandId);
                return;
            }

            var durationMs = (long)(evt.FailedAt - status.StartedAt).TotalMilliseconds;

            // Update status
            var updatedStatus = status with
            {
                Status = "Failed",
                CompletedAt = evt.FailedAt,
                ErrorMessage = evt.ErrorMessage,
                DurationMs = durationMs
            };
            _readDbContext.Entry(status).CurrentValues.SetValues(updatedStatus);

            // Add failure history entry
            var historyEntry = new CommandProgressHistoryReadModel
            {
                Id = Guid.NewGuid(),
                CommandId = evt.CommandId,
                Message = $"Failed: {evt.ErrorMessage}",
                ProgressPercentage = status.ProgressPercentage ?? 0,
                OccurredAt = evt.FailedAt,
                MilestoneName = "Failed"
            };
            _readDbContext.CommandProgressHistory.Add(historyEntry);

            await _readDbContext.SaveChangesAsync(context.CancellationToken);

            // Push to SignalR
            await _hubContext.Clients.Group("Admins").SendAsync("CommandFailed", new
            {
                evt.CommandId,
                evt.FailedAt,
                evt.ErrorMessage,
                evt.ExceptionType,
                DurationMs = durationMs
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process CommandFailedEvent for {CommandId}", evt.CommandId);
            throw;
        }
    }
}
