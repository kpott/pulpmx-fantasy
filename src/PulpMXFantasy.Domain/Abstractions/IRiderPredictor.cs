using PulpMXFantasy.Domain.Enums;

namespace PulpMXFantasy.Domain.Abstractions;

/// <summary>
/// Defines the contract for predicting rider fantasy points using machine learning.
/// </summary>
/// <remarks>
/// WHY THIS INTERFACE EXISTS:
/// ==========================
/// Following Clean Architecture / Dependency Inversion Principle:
/// - Domain layer defines WHAT it needs (predict fantasy points)
/// - Infrastructure layer implements HOW (using ML.NET, LightGBM, etc.)
/// - Application layer can use this abstraction without depending on ML.NET
///
/// BENEFITS:
/// 1. **Testability** - Mock predictions in unit tests without loading ML models
/// 2. **Flexibility** - Swap ML algorithms without changing domain/application code
/// 3. **Fallback strategies** - Implement simple handicap-based predictor for errors
/// 4. **Multiple implementations** - Could have LightGBM, FastTree, or API-based predictors
///
/// USAGE EXAMPLE:
/// <code>
/// // Application layer gets predictor via dependency injection
/// var predictions = _riderPredictor.PredictBatch(eventRiderFeatures);
///
/// // Team optimizer uses predictions with constraints
/// var optimalTeam = _teamOptimizer.FindOptimalTeam(predictions, constraints);
/// </code>
///
/// IMPLEMENTATION NOTES:
/// - Infrastructure will implement this using ML.NET PredictionEnginePool (thread-safe)
/// - Should handle model unavailability gracefully (return fallback predictions)
/// - Predictions include confidence intervals for uncertainty estimation
/// </remarks>
public interface IRiderPredictor
{
    /// <summary>
    /// Predicts fantasy points for a single rider.
    /// </summary>
    /// <param name="features">Rider features extracted from EventRider entity</param>
    /// <returns>Prediction with expected points and confidence bounds</returns>
    /// <remarks>
    /// Use PredictBatch() for multiple riders - it's more efficient than calling
    /// this method in a loop (batch predictions can optimize internal operations).
    /// </remarks>
    RiderPrediction PredictFantasyPoints(RiderFeatures features);

    /// <summary>
    /// Predicts fantasy points for multiple riders in a single batch (more efficient).
    /// </summary>
    /// <param name="features">Collection of rider features to predict</param>
    /// <returns>Read-only list of predictions matching input order</returns>
    /// <remarks>
    /// ALWAYS prefer batch prediction for multiple riders:
    /// - More efficient than loop of single predictions
    /// - ML.NET can optimize batch inference
    /// - Maintains consistent model state across predictions
    ///
    /// Typical usage:
    /// - Predict all 40-80 riders for an upcoming event
    /// - Generate predictions for team optimizer
    /// </remarks>
    IReadOnlyList<RiderPrediction> PredictBatch(IEnumerable<RiderFeatures> features);

    /// <summary>
    /// Gets information about the loaded ML model.
    /// </summary>
    /// <returns>Model metadata (version, training date, accuracy metrics)</returns>
    /// <remarks>
    /// Used for:
    /// - Displaying model version in UI
    /// - Logging which model was used for predictions
    /// - Comparing predictions across model versions
    /// - Debugging prediction issues
    /// </remarks>
    ModelInfo GetModelInfo();

    /// <summary>
    /// Checks if the ML model is loaded and ready for predictions.
    /// </summary>
    /// <returns>True if ready, false if model unavailable or loading failed</returns>
    /// <remarks>
    /// Use this to:
    /// - Display UI warnings if model unavailable
    /// - Fall back to handicap-based predictions
    /// - Trigger model loading retry logic
    /// </remarks>
    bool IsModelReady();

    /// <summary>
    /// Forces a reload of ML models from disk.
    /// </summary>
    /// <remarks>
    /// Call this after training new models to ensure the predictor uses
    /// the latest model files. This bypasses the PredictionEnginePool's
    /// file watching mechanism which can be unreliable in Docker.
    /// </remarks>
    void ReloadModels();
}

/// <summary>
/// Prediction result for a single rider, including expected points and uncertainty.
/// </summary>
/// <param name="RiderId">Rider identifier from EventRider entity</param>
/// <param name="BikeClass">Bike class (250 or 450)</param>
/// <param name="IsAllStar">True if rider is All-Star (affects team constraints)</param>
/// <param name="ExpectedPoints">Predicted fantasy points (mean of distribution), includes qualification probability</param>
/// <param name="PointsIfQualifies">Fantasy points if rider makes the main event (no qualification probability applied)</param>
/// <param name="PredictedFinish">Predicted finish position in main event (1-22), null if DNQ predicted</param>
/// <param name="LowerBound">Lower bound of 80% confidence interval</param>
/// <param name="UpperBound">Upper bound of 80% confidence interval</param>
/// <param name="Confidence">Model confidence in prediction (0-1, higher = more certain)</param>
/// <remarks>
/// EXPECTED POINTS:
/// This is the mean predicted value - the "most likely" fantasy points.
/// Example: 28.5 points
///
/// CONFIDENCE INTERVALS:
/// Represent prediction uncertainty using 80% confidence interval:
/// - LowerBound: 20th percentile prediction
/// - UpperBound: 80th percentile prediction
/// - Wider interval = more uncertainty
///
/// Example: ExpectedPoints=28.5, LowerBound=18.2, UpperBound=38.9
/// Interpretation: "80% chance rider scores between 18-39 points, most likely ~29"
///
/// CONFIDENCE SCORE:
/// Additional metric for prediction quality (0-1):
/// - 0.9+ = High confidence (rider with strong qualifying, consistent history)
/// - 0.5-0.9 = Medium confidence (typical prediction)
/// - <0.5 = Low confidence (new rider, injury return, data quality issues)
///
/// BIKE CLASS & ALL-STAR:
/// Included in prediction for team optimizer constraints:
/// - Team must have exactly 4 riders per class (250, 450)
/// - Team must have exactly 1 All-Star per class
/// - Optimizer needs this metadata to build valid teams
///
/// USAGE IN OPTIMIZATION:
/// Team optimizer can use uncertainty for strategy:
/// - Risk-averse: Prioritize riders with narrow confidence intervals
/// - Risk-seeking: Pick high-upside riders with wide intervals
/// - Expected value: Use ExpectedPoints for standard optimization
/// </remarks>
public record RiderPrediction(
    Guid RiderId,
    BikeClass BikeClass,
    bool IsAllStar,
    float ExpectedPoints,
    float PointsIfQualifies,
    int? PredictedFinish,
    float LowerBound,
    float UpperBound,
    float Confidence);

/// <summary>
/// Input features for ML model prediction, extracted from EventRider and historical data.
/// </summary>
/// <remarks>
/// FEATURE ENGINEERING:
/// These 16 features are carefully chosen based on fantasy point correlation:
///
/// STRONGEST PREDICTORS (Expert Review):
/// 1. Handicap - PulpMX's manual adjustment (most important!)
/// 2. QualifyingPosition - Recent form indicator
/// 3. AvgFantasyPointsLast5 - Rolling average points
/// 4. IsAllStar - Affects doubling logic
///
/// ADDITIONAL CONTEXT:
/// 5. PickTrend - Crowd wisdom
/// 6. QualifyingLapTime - Absolute speed
/// 7. QualyGapToLeader - Relative speed
/// 8. AvgFinishLast5 - Rolling average finish
/// 9. FinishRate - Consistency (DNF percentage)
/// 10. SeasonPoints - Championship standings
/// 11. TrackHistory - Performance at this venue
/// 12. TrackType - Indoor/outdoor, soil type
/// 13. DaysSinceInjury - Recovery status
/// 14. TeamQuality - Factory vs privateer equipment
/// 15. GatePick - Starting position quality
/// 16. RecentMomentum - Trend direction (improving/declining)
///
/// FEATURE NORMALIZATION:
/// Implementation should normalize features appropriately:
/// - Categorical: One-hot or ordinal encoding (TrackType, TeamQuality)
/// - Numeric: StandardScaler or MinMaxScaler (Handicap, QualyGap, etc.)
/// - Binary: Keep as 0/1 (IsAllStar, IsInjured)
///
/// HANDLING MISSING VALUES:
/// - New riders: Use series averages for historical features
/// - Missing qualifying: Use worst qualifying position + penalty
/// - Track history: Use 0 if rider never raced this venue
/// </remarks>
public record RiderFeatures(
    Guid RiderId,
    BikeClass BikeClass,
    int Handicap,
    bool IsAllStar,
    bool IsInjured,
    decimal? PickTrend,
    int? QualifyingPosition,
    decimal? QualifyingLapTime,
    decimal? QualyGapToLeader,
    decimal? AvgFinishLast5,
    decimal? AvgFantasyPointsLast5,
    decimal? FinishRate,
    int? SeasonPoints,
    decimal? TrackHistory,
    string? TrackType,
    int? DaysSinceInjury,
    string? TeamQuality,
    int? GatePick,
    decimal? RecentMomentum);

/// <summary>
/// Information about the loaded ML model.
/// </summary>
/// <param name="Version">Model version identifier (e.g., "v1.2.3_2025-01-09_r2")</param>
/// <param name="TrainedDate">When the model was trained</param>
/// <param name="TrainingSamples">Number of training samples used</param>
/// <param name="ValidationAccuracy">Top-3 prediction accuracy on validation set (0-1)</param>
/// <param name="MeanAbsoluteError">MAE in fantasy points (lower = better)</param>
/// <remarks>
/// MODEL VERSIONING FORMAT:
/// "v{major}.{minor}.{patch}_{date}_{retrain}"
///
/// Examples:
/// - "v1.0.0_2025-01-09_r1" - Initial model trained on 2025-01-09
/// - "v1.0.0_2025-01-15_r2" - Retrained after Round 2 with new data
/// - "v1.1.0_2025-02-01_r5" - Minor algorithm update at Round 5
///
/// ACCURACY METRICS:
/// - ValidationAccuracy: % of time predicted top-3 riders in actual top-3
///   * Target: >30% (significantly better than random)
///   * Excellent: >40%
/// - MeanAbsoluteError: Average difference between predicted and actual points
///   * Good: <8 points MAE
///   * Excellent: <5 points MAE
///
/// MINIMUM TRAINING SAMPLES:
/// Per expert review, require minimum 200 samples per class to train.
/// Early season: Use previous season's model until enough data collected.
/// </remarks>
public record ModelInfo(
    string Version,
    DateTimeOffset TrainedDate,
    int TrainingSamples,
    float ValidationAccuracy,
    float MeanAbsoluteError);
