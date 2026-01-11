namespace PulpMXFantasy.Contracts.Commands;

/// <summary>
/// Command to train all 4 ML models (250/450 x Qualification/FinishPosition).
/// </summary>
/// <remarks>
/// Trains models sequentially and publishes ModelsTrainedEvent on completion.
/// Predictions are automatically generated after models are trained.
/// </remarks>
/// <param name="Timestamp">When the command was issued</param>
/// <param name="Force">If true, retrain even if recent models exist</param>
public record TrainModelsCommand(
    DateTimeOffset Timestamp,
    bool Force = false);
