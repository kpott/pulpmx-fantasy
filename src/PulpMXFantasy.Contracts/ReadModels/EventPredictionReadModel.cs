namespace PulpMXFantasy.Contracts.ReadModels;

/// <summary>
/// Read model for ML predictions, optimized for UI display.
/// </summary>
/// <remarks>
/// Stored in read_model.event_predictions table.
/// Denormalized rider data avoids joins for fast queries.
/// </remarks>
public record EventPredictionReadModel
{
    public required Guid Id { get; init; }
    public required Guid EventId { get; init; }
    public required Guid RiderId { get; init; }
    public required string RiderName { get; init; }
    public required int RiderNumber { get; init; }
    public required string BikeClass { get; init; }
    public required bool IsAllStar { get; init; }
    public required int Handicap { get; init; }
    public required float ExpectedPoints { get; init; }
    public required float PointsIfQualifies { get; init; }
    public int? PredictedFinish { get; init; }
    public required float LowerBound { get; init; }
    public required float UpperBound { get; init; }
    public required float Confidence { get; init; }
    public required string ModelVersion { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
}
