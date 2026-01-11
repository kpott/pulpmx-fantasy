using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.Contracts.Interfaces;

/// <summary>
/// Service for managing command status in the read model database.
/// </summary>
/// <remarks>
/// CQRS PATTERN:
/// =============
/// Commands are fire-and-forget (async). This interface provides
/// status tracking so UI can poll for completion.
///
/// COMMAND LIFECYCLE:
/// ==================
/// 1. CreateAsync - Command starts, status = "Pending"
/// 2. UpdateProgressAsync - In progress, status = "Running"
/// 3. CompleteAsync - Success, status = "Completed"
/// 4. FailAsync - Error, status = "Failed"
///
/// IMPLEMENTATIONS:
/// ================
/// - CommandStatusService (ReadModel): PostgreSQL-backed status tracking
/// </remarks>
public interface ICommandStatusService
{
    /// <summary>
    /// Creates a new command status record with "Pending" status.
    /// </summary>
    Task<CommandStatusReadModel> CreateAsync(
        Guid commandId,
        Guid correlationId,
        string commandType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a command status by its ID.
    /// </summary>
    Task<CommandStatusReadModel?> GetByIdAsync(
        Guid commandId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all command statuses by correlation ID.
    /// </summary>
    Task<List<CommandStatusReadModel>> GetByCorrelationIdAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent command statuses.
    /// </summary>
    Task<List<CommandStatusReadModel>> GetRecentAsync(
        int count = 20,
        string? commandType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the progress of a running command.
    /// </summary>
    Task UpdateProgressAsync(
        Guid commandId,
        string progressMessage,
        int progressPercentage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a command as completed with optional result data and completion message.
    /// </summary>
    Task CompleteAsync(
        Guid commandId,
        object? resultData = null,
        string? completionMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a command as failed with an error message.
    /// </summary>
    Task FailAsync(
        Guid commandId,
        string errorMessage,
        CancellationToken cancellationToken = default);
}
