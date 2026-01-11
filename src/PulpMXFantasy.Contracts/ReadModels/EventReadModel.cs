namespace PulpMXFantasy.Contracts.ReadModels;

/// <summary>
/// Read model for event data, optimized for UI display.
/// </summary>
/// <remarks>
/// Stored in read_model.events table.
/// Denormalized with series name to avoid joins.
/// Populated by Worker when events are synced.
/// </remarks>
public record EventReadModel
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public required string Venue { get; init; }
    public required string Location { get; init; }
    public required DateTimeOffset EventDate { get; init; }
    public required string SeriesName { get; init; }
    public required int SeasonYear { get; init; }
    public required bool IsCompleted { get; init; }
    public DateTimeOffset? LockoutTime { get; init; }
    public required int RiderCount { get; init; }
    public required DateTimeOffset SyncedAt { get; init; }
}
