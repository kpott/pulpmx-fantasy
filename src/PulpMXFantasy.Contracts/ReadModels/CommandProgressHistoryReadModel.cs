namespace PulpMXFantasy.Contracts.ReadModels;

/// <summary>
/// Read model for command progress timeline entries.
/// </summary>
/// <remarks>
/// Stored in read_model.command_progress_history table.
/// Child of CommandStatusReadModel for tracking milestones.
/// </remarks>
public record CommandProgressHistoryReadModel
{
    public required Guid Id { get; init; }
    public required Guid CommandId { get; init; }
    public required string Message { get; init; }
    public required int ProgressPercentage { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? MilestoneName { get; init; }
}
