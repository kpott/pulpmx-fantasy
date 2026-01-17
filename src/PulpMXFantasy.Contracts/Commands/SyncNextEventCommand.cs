using MassTransit;

namespace PulpMXFantasy.Contracts.Commands;

/// <summary>
/// Command to synchronize the next upcoming event from the PulpMX Fantasy API.
/// </summary>
/// <remarks>
/// Fetches the next upcoming event including rider lists, handicaps, All-Star designations,
/// and qualifying results (when available after day qualifying sessions conclude).
/// </remarks>
/// <param name="CommandId">Unique identifier for this command instance</param>
/// <param name="Timestamp">When the command was issued</param>
public record SyncNextEventCommand(
    Guid CommandId,
    DateTimeOffset Timestamp) : CorrelatedBy<Guid>
{
    /// <summary>
    /// MassTransit correlation ID for message flow tracking.
    /// </summary>
    public Guid CorrelationId => CommandId;
}
