namespace PulpMXFantasy.Contracts.Events;

/// <summary>
/// Published when a command fails.
/// </summary>
/// <param name="CommandId">Command identifier.</param>
/// <param name="FailedAt">When failure occurred.</param>
/// <param name="ErrorMessage">Error description.</param>
/// <param name="ExceptionType">Optional exception type name.</param>
public record CommandFailedEvent(
    Guid CommandId,
    DateTimeOffset FailedAt,
    string ErrorMessage,
    string? ExceptionType = null);
