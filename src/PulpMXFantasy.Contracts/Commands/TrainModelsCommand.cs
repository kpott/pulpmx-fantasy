using MassTransit;

namespace PulpMXFantasy.Contracts.Commands;

/// <summary>
/// Command to train all 4 ML models (250/450 x Qualification/FinishPosition).
/// </summary>
/// <remarks>
/// Trains models sequentially and publishes ModelsTrainedEvent on completion.
/// Predictions are automatically generated after models are trained.
/// </remarks>
/// <param name="CommandId">Unique identifier for this command instance</param>
/// <param name="Timestamp">When the command was issued</param>
/// <param name="Force">If true, retrain even if recent models exist</param>
public record TrainModelsCommand(
    Guid CommandId,
    DateTimeOffset Timestamp,
    bool Force = false) : CorrelatedBy<Guid>
{
    /// <summary>
    /// MassTransit correlation ID for message flow tracking.
    /// </summary>
    public Guid CorrelationId => CommandId;
}
