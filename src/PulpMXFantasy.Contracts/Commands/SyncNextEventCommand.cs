namespace PulpMXFantasy.Contracts.Commands;

/// <summary>
/// Command to synchronize the next upcoming event from the PulpMX Fantasy API.
/// </summary>
/// <remarks>
/// Fetches the next upcoming event including rider lists, handicaps, All-Star designations,
/// and qualifying results (when available after day qualifying sessions conclude).
/// </remarks>
/// <param name="Timestamp">When the command was issued</param>
public record SyncNextEventCommand(
    DateTimeOffset Timestamp);
