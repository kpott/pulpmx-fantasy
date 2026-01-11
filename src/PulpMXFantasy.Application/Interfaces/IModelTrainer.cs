using PulpMXFantasy.Domain.Enums;

namespace PulpMXFantasy.Application.Interfaces;

/// <summary>
/// Interface for ML model training operations.
/// </summary>
/// <remarks>
/// WHY THIS INTERFACE EXISTS:
/// ==========================
/// Abstracts ML training operations for testability:
/// - Allows mocking in unit tests
/// - Decouples command handlers from concrete implementation
/// - Enables different training implementations (local, cloud, etc.)
///
/// TRAINING OPERATIONS:
/// ====================
/// 1. Qualification Model - Binary classification (makes main event?)
/// 2. Finish Position Model - Regression (where will they finish?)
///
/// Each operation trains for a specific bike class (250/450).
/// Total of 4 models trained per full training cycle.
/// </remarks>
public interface IModelTrainer
{
    /// <summary>
    /// Trains a qualification prediction model for a bike class.
    /// </summary>
    /// <param name="bikeClass">250 or 450 class</param>
    /// <param name="modelDirectory">Directory to save trained model</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Metadata about the trained model</returns>
    /// <remarks>
    /// Binary classification model predicting if rider makes main event (top 22).
    /// Expected metrics: Accuracy 75-80%, AUC 0.80-0.85
    /// </remarks>
    Task<TrainedModelResult> TrainQualificationModelAsync(
        BikeClass bikeClass,
        string modelDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trains a finish position prediction model for a bike class.
    /// </summary>
    /// <param name="bikeClass">250 or 450 class</param>
    /// <param name="modelDirectory">Directory to save trained model</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Metadata about the trained model</returns>
    /// <remarks>
    /// Regression model predicting finish position (1-22) for qualified riders.
    /// Expected metrics: R² 0.30-0.50, MAE 3-5 positions
    /// </remarks>
    Task<TrainedModelResult> TrainFinishPositionModelAsync(
        BikeClass bikeClass,
        string modelDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a model training operation.
/// </summary>
/// <remarks>
/// Captures all metadata about a trained model for:
/// - Persisting to read model database
/// - Publishing in ModelsTrainedEvent
/// - Displaying to admin users
/// </remarks>
public record TrainedModelResult(
    string Version,
    string BikeClass,
    string ModelType,
    DateTimeOffset TrainedAt,
    int TrainingSamples,
    double RSquared,
    double MeanAbsoluteError,
    double RootMeanSquaredError,
    string ModelPath,
    double ValidationAccuracy);
