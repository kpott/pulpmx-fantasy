namespace PulpMXFantasy.Contracts.ReadModels;

/// <summary>
/// Read model for ML model training metadata.
/// </summary>
/// <remarks>
/// Stored in read_model.model_metadata table.
/// Displays latest model training results in the UI.
/// </remarks>
public record ModelMetadataReadModel
{
    public required Guid Id { get; init; }
    public required string BikeClass { get; init; }
    public required string ModelType { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset TrainedAt { get; init; }
    public required int TrainingSamples { get; init; }
    public float? ValidationAccuracy { get; init; }
    public float? RSquared { get; init; }
    public float? MeanAbsoluteError { get; init; }
    public required string ModelPath { get; init; }
    public required bool IsActive { get; init; }
}
