using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Contracts.Interfaces;
using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.ReadModel.Services;

/// <summary>
/// Read-only service for querying command status from the read model database.
/// </summary>
/// <remarks>
/// EVENT-DRIVEN ARCHITECTURE:
/// ==========================
/// This service is now READ-ONLY. Command status writes are handled by
/// CommandStatusEventConsumer in the Web project, which:
/// 1. Consumes command status events from the message broker
/// 2. Writes to the read model database
/// 3. Pushes updates to SignalR for real-time UI updates
/// </remarks>
public class CommandStatusService : ICommandStatusService
{
    private readonly ReadDbContext _readDbContext;
    private readonly ILogger<CommandStatusService> _logger;

    public CommandStatusService(ReadDbContext readDbContext, ILogger<CommandStatusService> logger)
    {
        _readDbContext = readDbContext ?? throw new ArgumentNullException(nameof(readDbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CommandStatusReadModel?> GetByIdAsync(
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        return await _readDbContext.CommandStatus
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CommandId == commandId, cancellationToken);
    }

    public async Task<(CommandStatusReadModel Status, IReadOnlyList<CommandProgressHistoryReadModel> History)?> GetByIdWithHistoryAsync(
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        var status = await _readDbContext.CommandStatus
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CommandId == commandId, cancellationToken);

        if (status == null)
        {
            return null;
        }

        var history = await _readDbContext.CommandProgressHistory
            .AsNoTracking()
            .Where(h => h.CommandId == commandId)
            .OrderBy(h => h.OccurredAt)
            .ToListAsync(cancellationToken);

        return (status, history);
    }

    public async Task<List<CommandStatusReadModel>> GetByCorrelationIdAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        return await _readDbContext.CommandStatus
            .AsNoTracking()
            .Where(c => c.CorrelationId == correlationId)
            .OrderByDescending(c => c.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<CommandStatusReadModel>> GetRecentAsync(
        int count = 20,
        string? commandType = null,
        CancellationToken cancellationToken = default)
    {
        var query = _readDbContext.CommandStatus.AsNoTracking();

        if (!string.IsNullOrEmpty(commandType))
        {
            query = query.Where(c => c.CommandType == commandType);
        }

        return await query
            .OrderByDescending(c => c.StartedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Constants for command status values.
/// </summary>
public static class CommandStatusValues
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
