namespace PulpMXFantasy.Contracts.Events;

/// <summary>
/// Published when a command begins execution.
/// </summary>
/// <param name="CommandId">Unique identifier for this command execution.</param>
/// <param name="CommandType">Type name (e.g., "TrainModels", "ImportEvents", "SyncNextEvent").</param>
/// <param name="StartedAt">When execution began.</param>
public record CommandStartedEvent(
    Guid CommandId,
    string CommandType,
    DateTimeOffset StartedAt);
