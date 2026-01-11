namespace PulpMXFantasy.Contracts.Commands;

/// <summary>
/// Command to import multiple historical events by their API slugs.
/// </summary>
/// <remarks>
/// Imports events sequentially to prevent API rate limiting.
/// Progress is tracked via CommandStatusReadModel.
/// </remarks>
/// <param name="EventSlugs">List of event slugs to import (e.g., ["anaheim-1-2025-sx", "san-diego-2025-sx"])</param>
/// <param name="Timestamp">When the command was issued</param>
public record ImportEventsCommand(
    List<string> EventSlugs,
    DateTimeOffset Timestamp);
