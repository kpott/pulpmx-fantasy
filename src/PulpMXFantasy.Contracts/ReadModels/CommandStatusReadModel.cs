namespace PulpMXFantasy.Contracts.ReadModels;

/// <summary>
/// Read model for tracking command execution progress.
/// </summary>
/// <remarks>
/// Stored in read_model.command_status table.
/// Used for UI polling to show command progress.
/// </remarks>
public record CommandStatusReadModel
{
    public required Guid CommandId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required string CommandType { get; init; }
    public required string Status { get; init; }
    public string? ProgressMessage { get; init; }
    public int? ProgressPercentage { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ResultData { get; init; }
    public long? DurationMs { get; init; }
}
