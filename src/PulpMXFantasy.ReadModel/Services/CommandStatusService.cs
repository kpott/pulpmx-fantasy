using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Contracts.Interfaces;
using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.ReadModel.Services;

/// <summary>
/// Service for managing command status in the read model database.
/// </summary>
public class CommandStatusService : ICommandStatusService
{
    private readonly ReadDbContext _readDbContext;
    private readonly ILogger<CommandStatusService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public CommandStatusService(ReadDbContext readDbContext, ILogger<CommandStatusService> logger)
    {
        _readDbContext = readDbContext ?? throw new ArgumentNullException(nameof(readDbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CommandStatusReadModel> CreateAsync(
        Guid commandId,
        Guid correlationId,
        string commandType,
        CancellationToken cancellationToken = default)
    {
        var status = new CommandStatusReadModel
        {
            CommandId = commandId,
            CorrelationId = correlationId,
            CommandType = commandType,
            Status = CommandStatusValues.Pending,
            StartedAt = DateTimeOffset.UtcNow
        };

        _readDbContext.CommandStatus.Add(status);
        await _readDbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created command status: CommandId={CommandId}, Type={CommandType}, CorrelationId={CorrelationId}",
            commandId, commandType, correlationId);

        return status;
    }

    public async Task<CommandStatusReadModel?> GetByIdAsync(
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        return await _readDbContext.CommandStatus
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CommandId == commandId, cancellationToken);
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

    public async Task UpdateProgressAsync(
        Guid commandId,
        string progressMessage,
        int progressPercentage,
        CancellationToken cancellationToken = default)
    {
        var status = await _readDbContext.CommandStatus
            .FirstOrDefaultAsync(c => c.CommandId == commandId, cancellationToken);

        if (status == null)
        {
            _logger.LogWarning("Command status not found for update: CommandId={CommandId}", commandId);
            return;
        }

        var updatedStatus = status with
        {
            Status = CommandStatusValues.Running,
            ProgressMessage = progressMessage,
            ProgressPercentage = Math.Clamp(progressPercentage, 0, 100)
        };

        _readDbContext.Entry(status).CurrentValues.SetValues(updatedStatus);
        await _readDbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Updated command progress: CommandId={CommandId}, Message={Message}, Progress={Progress}%",
            commandId, progressMessage, progressPercentage);
    }

    public async Task CompleteAsync(
        Guid commandId,
        object? resultData = null,
        string? completionMessage = null,
        CancellationToken cancellationToken = default)
    {
        var status = await _readDbContext.CommandStatus
            .FirstOrDefaultAsync(c => c.CommandId == commandId, cancellationToken);

        if (status == null)
        {
            _logger.LogWarning("Command status not found for completion: CommandId={CommandId}", commandId);
            return;
        }

        string? resultJson = null;
        if (resultData != null)
        {
            resultJson = JsonSerializer.Serialize(resultData, JsonOptions);
        }

        var updatedStatus = status with
        {
            Status = CommandStatusValues.Completed,
            ProgressPercentage = 100,
            ProgressMessage = completionMessage ?? "Completed",
            CompletedAt = DateTimeOffset.UtcNow,
            ResultData = resultJson
        };

        _readDbContext.Entry(status).CurrentValues.SetValues(updatedStatus);
        await _readDbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Command completed: CommandId={CommandId}, Type={CommandType}, Duration={Duration}ms",
            commandId, status.CommandType, (updatedStatus.CompletedAt!.Value - status.StartedAt).TotalMilliseconds);
    }

    public async Task FailAsync(
        Guid commandId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var status = await _readDbContext.CommandStatus
            .FirstOrDefaultAsync(c => c.CommandId == commandId, cancellationToken);

        if (status == null)
        {
            _logger.LogWarning("Command status not found for failure: CommandId={CommandId}", commandId);
            return;
        }

        var updatedStatus = status with
        {
            Status = CommandStatusValues.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            ErrorMessage = errorMessage
        };

        _readDbContext.Entry(status).CurrentValues.SetValues(updatedStatus);
        await _readDbContext.SaveChangesAsync(cancellationToken);

        _logger.LogError(
            "Command failed: CommandId={CommandId}, Type={CommandType}, Error={Error}",
            commandId, status.CommandType, errorMessage);
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
