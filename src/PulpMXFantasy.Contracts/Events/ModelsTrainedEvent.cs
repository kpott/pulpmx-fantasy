namespace PulpMXFantasy.Contracts.Events;

/// <summary>
/// Published after all ML models have been successfully trained.
/// </summary>
/// <remarks>
/// Triggers automatic prediction generation via ModelsTrainedEventHandler.
/// </remarks>
/// <param name="TrainedAt">When training completed</param>
/// <param name="Models">Metadata for each trained model (4 total: 250/450 x Qualification/FinishPosition)</param>
/// <param name="TotalTrainingSamples">Total number of samples used across all models</param>
public record ModelsTrainedEvent(
    DateTimeOffset TrainedAt,
    List<ModelMetadata> Models,
    int TotalTrainingSamples);

/// <summary>
/// Metadata about a single trained ML model.
/// </summary>
/// <param name="BikeClass">Class250 or Class450</param>
/// <param name="ModelType">Qualification or FinishPosition</param>
/// <param name="Version">Semantic version (e.g., "1.0.0")</param>
/// <param name="ValidationAccuracy">Accuracy metric for classification models</param>
/// <param name="RSquared">R-squared metric for regression models</param>
/// <param name="MeanAbsoluteError">MAE metric</param>
/// <param name="ModelPath">File path to the trained model</param>
public record ModelMetadata(
    string BikeClass,
    string ModelType,
    string Version,
    float? ValidationAccuracy,
    float? RSquared,
    float? MeanAbsoluteError,
    string ModelPath);
