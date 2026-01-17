using MassTransit;

namespace PulpMXFantasy.Contracts.Commands;

/// <summary>
/// Command to import multiple historical events by their API slugs.
/// </summary>
/// <remarks>
/// Imports events sequentially to prevent API rate limiting.
/// Progress is tracked via CommandStatusReadModel.
/// </remarks>
/// <param name="CommandId">Unique identifier for this command instance</param>
/// <param name="EventSlugs">List of event slugs to import (e.g., ["anaheim-1-2025-sx", "san-diego-2025-sx"])</param>
/// <param name="Timestamp">When the command was issued</param>
public record ImportEventsCommand(
    Guid CommandId,
    IReadOnlyList<string> EventSlugs,
    DateTimeOffset Timestamp) : CorrelatedBy<Guid>
{
    /// <summary>
    /// MassTransit correlation ID for message flow tracking.
    /// </summary>
    public Guid CorrelationId => CommandId;
}
