namespace PulpMXFantasy.Contracts.Events;

/// <summary>
/// Published when a command completes successfully.
/// </summary>
/// <param name="CommandId">Command identifier.</param>
/// <param name="CompletedAt">When command finished.</param>
/// <param name="CompletionMessage">Summary message.</param>
/// <param name="ResultDataJson">JSON-serialized result data (optional).</param>
public record CommandCompletedEvent(
    Guid CommandId,
    DateTimeOffset CompletedAt,
    string CompletionMessage,
    string? ResultDataJson = null);
