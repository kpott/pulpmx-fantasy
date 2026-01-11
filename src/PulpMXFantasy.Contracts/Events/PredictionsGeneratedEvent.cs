namespace PulpMXFantasy.Contracts.Events;

/// <summary>
/// Published after predictions are written to the read model.
/// </summary>
/// <param name="EventId">Event for which predictions were generated</param>
/// <param name="GeneratedAt">When predictions were generated</param>
/// <param name="PredictionCount">Number of predictions generated</param>
/// <param name="ModelVersion">Version of the model used</param>
public record PredictionsGeneratedEvent(
    Guid EventId,
    DateTimeOffset GeneratedAt,
    int PredictionCount,
    string ModelVersion);
