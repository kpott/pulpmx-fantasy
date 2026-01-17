namespace PulpMXFantasy.Contracts.Events;

/// <summary>
/// Published when command progress changes.
/// </summary>
/// <param name="CommandId">Command identifier.</param>
/// <param name="ProgressMessage">Human-readable progress description.</param>
/// <param name="ProgressPercentage">0-100 percentage complete.</param>
/// <param name="UpdatedAt">When this update occurred.</param>
/// <param name="MilestoneName">Optional name for this milestone (for timeline tracking).</param>
public record CommandProgressUpdatedEvent(
    Guid CommandId,
    string ProgressMessage,
    int ProgressPercentage,
    DateTimeOffset UpdatedAt,
    string? MilestoneName = null);
