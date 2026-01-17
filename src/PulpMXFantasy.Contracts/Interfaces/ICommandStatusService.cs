using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.Contracts.Interfaces;

/// <summary>
/// Read-only service for querying command status from the read model database.
/// </summary>
/// <remarks>
/// EVENT-DRIVEN ARCHITECTURE:
/// ==========================
/// Command status is now managed through events:
/// - Worker consumers publish CommandStartedEvent, CommandProgressUpdatedEvent,
///   CommandCompletedEvent, and CommandFailedEvent via ConsumeContext.Publish()
/// - Web's CommandStatusEventConsumer handles these events, writes to DB, and pushes to SignalR
///
/// This service is READ-ONLY - it provides queries for:
/// - Initial page load (get recent commands)
/// - Command details fetch (get command with history)
/// - Status polling (fallback when SignalR disconnected)
///
/// IMPLEMENTATIONS:
/// ================
/// - CommandStatusService (ReadModel): PostgreSQL-backed status queries
/// </remarks>
public interface ICommandStatusService
{
    /// <summary>
    /// Gets a command status by its ID.
    /// </summary>
    Task<CommandStatusReadModel?> GetByIdAsync(
        Guid commandId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a command status with its progress history.
    /// </summary>
    /// <param name="commandId">The command ID to query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of status and history, or null if not found</returns>
    Task<(CommandStatusReadModel Status, IReadOnlyList<CommandProgressHistoryReadModel> History)?> GetByIdWithHistoryAsync(
        Guid commandId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all command statuses by correlation ID.
    /// </summary>
    Task<IReadOnlyList<CommandStatusReadModel>> GetByCorrelationIdAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent command statuses.
    /// </summary>
    Task<IReadOnlyList<CommandStatusReadModel>> GetRecentAsync(
        int count = 20,
        string? commandType = null,
        CancellationToken cancellationToken = default);
}
