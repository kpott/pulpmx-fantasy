namespace PulpMXFantasy.Infrastructure.MachineLearning;

/// <summary>
/// Constants for ML model configuration to ensure consistency between training and inference.
/// </summary>
/// <remarks>
/// WHY THIS FILE EXISTS:
/// =====================
/// ML.NET models require exact schema matching between training and inference:
/// - Feature names must match exactly
/// - Feature order in concatenation must match
/// - Column names must be consistent
///
/// By centralizing these constants, we:
/// 1. Prevent typos causing schema mismatches
/// 2. Make feature additions/removals explicit
/// 3. Document the feature engineering decisions
///
/// CRITICAL: Any changes here affect both ModelTrainer and MultiStagePredictor.
/// After changes, models must be retrained and predictions regenerated.
/// </remarks>
public static class MLConstants
{
    // ============================================================================
    // MODEL NAMES (used by PredictionEnginePool)
    // ============================================================================

    /// <summary>
    /// Model name for 250cc qualification prediction.
    /// </summary>
    public const string Class250QualificationModel = "Class250_Qualification";

    /// <summary>
    /// Model name for 250cc finish position prediction.
    /// </summary>
    public const string Class250FinishPositionModel = "Class250_FinishPosition";

    /// <summary>
    /// Model name for 450cc qualification prediction.
    /// </summary>
    public const string Class450QualificationModel = "Class450_Qualification";

    /// <summary>
    /// Model name for 450cc finish position prediction.
    /// </summary>
    public const string Class450FinishPositionModel = "Class450_FinishPosition";

    // ============================================================================
    // MODEL FILE NAMES (consistent names for watchForChanges to work)
    // ============================================================================

    /// <summary>
    /// File name for 250cc qualification model.
    /// Using consistent names (not date-stamped) so PredictionEnginePool can watch for changes.
    /// </summary>
    public const string Class250QualificationFileName = "Class250_Qualification.zip";

    /// <summary>
    /// File name for 250cc finish position model.
    /// </summary>
    public const string Class250FinishPositionFileName = "Class250_FinishPosition.zip";

    /// <summary>
    /// File name for 450cc qualification model.
    /// </summary>
    public const string Class450QualificationFileName = "Class450_Qualification.zip";

    /// <summary>
    /// File name for 450cc finish position model.
    /// </summary>
    public const string Class450FinishPositionFileName = "Class450_FinishPosition.zip";

    // ============================================================================
    // QUALIFICATION MODEL FEATURES
    // ============================================================================
    // Binary classification: Does rider make main event (top 22)?
    // Features chosen based on correlation analysis with qualification outcome.

    public static class QualificationFeatures
    {
        /// <summary>
        /// Event handicap value (-6 to +19 typical range).
        /// Higher handicap = more likely to qualify.
        /// PRIMARY FEATURE for qualification prediction.
        /// </summary>
        public const string Handicap = "Handicap";

        /// <summary>
        /// Average finish position from last 5 races.
        /// Lower = better recent form.
        /// -1 if no history (new rider).
        /// </summary>
        public const string AvgFinishLast5 = "AvgFinishLast5";

        /// <summary>
        /// Percentage of races finished (0-100).
        /// 100 = never DNF, 80 = finishes 4 of 5 races.
        /// </summary>
        public const string FinishRate = "FinishRate";

        /// <summary>
        /// Average finish position at this specific venue in past years.
        /// -1 if never raced here before.
        /// </summary>
        public const string TrackHistory = "TrackHistory";

        // NOTE: PickTrend was removed - it's not available at prediction time (only after event starts)

        /// <summary>
        /// All-Star status (1.0 = true, 0.0 = false).
        /// All-Stars have ~95%+ qualification rate.
        /// </summary>
        public const string IsAllStar = "IsAllStar";

        /// <summary>
        /// Target variable: Did the rider make the main event?
        /// true = FinishPosition ≤ 22 (qualified)
        /// false = DNQ or FinishPosition > 22
        /// </summary>
        public const string Label = "Label";

        /// <summary>
        /// Combined features column name for ML pipeline.
        /// </summary>
        public const string Features = "Features";

        /// <summary>
        /// All feature names in the order they should be concatenated.
        /// CRITICAL: This order must match between training and inference!
        /// NOTE: PickTrend was removed - it's not available at prediction time.
        /// </summary>
        public static readonly string[] FeatureOrder = new[]
        {
            Handicap,
            AvgFinishLast5,
            FinishRate,
            TrackHistory,
            IsAllStar
        };
    }

    // ============================================================================
    // FINISH POSITION MODEL FEATURES
    // ============================================================================
    // Regression: Predicted finish position (1-22) for riders who make main.

    public static class FinishPositionFeatures
    {
        /// <summary>
        /// Event handicap value.
        /// </summary>
        public const string Handicap = "Handicap";

        /// <summary>
        /// Average finish position from last 5 races.
        /// </summary>
        public const string AvgFinishLast5 = "AvgFinishLast5";

        /// <summary>
        /// Average fantasy points from last 5 races.
        /// </summary>
        public const string AvgFantasyPointsLast5 = "AvgFantasyPointsLast5";

        /// <summary>
        /// Average finish position at this specific venue.
        /// </summary>
        public const string TrackHistory = "TrackHistory";

        /// <summary>
        /// Recent momentum (improving/declining trend).
        /// Positive = improving, negative = declining.
        /// </summary>
        public const string RecentMomentum = "RecentMomentum";

        /// <summary>
        /// Season points accumulated.
        /// </summary>
        public const string SeasonPoints = "SeasonPoints";

        /// <summary>
        /// All-Star status (bool input column name).
        /// </summary>
        public const string IsAllStar = "IsAllStar";

        /// <summary>
        /// All-Star status converted to float for concatenation.
        /// </summary>
        public const string IsAllStarFloat = "IsAllStarFloat";

        /// <summary>
        /// Track type indoor (bool input column name).
        /// </summary>
        public const string TrackTypeIndoor = "TrackTypeIndoor";

        /// <summary>
        /// Track type (indoor) converted to float for concatenation.
        /// </summary>
        public const string TrackTypeIndoorFloat = "TrackTypeIndoorFloat";

        /// <summary>
        /// Target variable: Actual finish position (1-22).
        /// </summary>
        public const string Label = "Label";

        /// <summary>
        /// Combined features column name for ML pipeline.
        /// </summary>
        public const string Features = "Features";

        /// <summary>
        /// All feature names in the order they should be concatenated.
        /// Note: IsAllStar and TrackTypeIndoor are converted to Float versions first.
        /// </summary>
        public static readonly string[] FeatureOrder = new[]
        {
            Handicap,
            AvgFinishLast5,
            AvgFantasyPointsLast5,
            TrackHistory,
            RecentMomentum,
            IsAllStarFloat,
            TrackTypeIndoorFloat
        };
    }

    // ============================================================================
    // PREDICTION OUTPUT COLUMNS
    // ============================================================================

    public static class PredictionOutput
    {
        /// <summary>
        /// Binary classification predicted label.
        /// </summary>
        public const string PredictedLabel = "PredictedLabel";

        /// <summary>
        /// Binary classification probability.
        /// </summary>
        public const string Probability = "Probability";

        /// <summary>
        /// Binary classification raw score (log-odds).
        /// </summary>
        public const string Score = "Score";

        /// <summary>
        /// Regression predicted value.
        /// </summary>
        public const string PredictedFinish = "Score"; // Regression output uses "Score" column
    }

    // ============================================================================
    // TRAINING PARAMETERS
    // ============================================================================

    public static class TrainingParams
    {
        /// <summary>
        /// Minimum number of training samples required per model.
        /// Expert recommendation: 200+ for reliable model.
        /// </summary>
        public const int MinimumTrainingSamples = 200;

        /// <summary>
        /// Test/validation split fraction (0.2 = 20% for validation).
        /// </summary>
        public const double TestFraction = 0.2;

        /// <summary>
        /// Random seed for reproducibility.
        /// </summary>
        public const int RandomSeed = 42;

        /// <summary>
        /// Number of tree leaves in FastTree/LightGBM.
        /// </summary>
        public const int NumberOfLeaves = 20;

        /// <summary>
        /// Number of boosting iterations.
        /// </summary>
        public const int NumberOfTrees = 100;

        /// <summary>
        /// Minimum examples required per leaf node.
        /// </summary>
        public const int MinimumExampleCountPerLeaf = 10;

        /// <summary>
        /// Learning rate for gradient boosting.
        /// </summary>
        public const double LearningRate = 0.1;
    }

    // ============================================================================
    // HELPER METHODS
    // ============================================================================

    /// <summary>
    /// Gets the model name for a given bike class and model type.
    /// </summary>
    /// <param name="bikeClass">250 or 450</param>
    /// <param name="modelType">"Qualification" or "FinishPosition"</param>
    /// <returns>Model name for PredictionEnginePool lookup</returns>
    public static string GetModelName(Domain.Enums.BikeClass bikeClass, string modelType)
    {
        return $"{bikeClass}_{modelType}";
    }

    /// <summary>
    /// Gets the model file name for a given bike class and model type.
    /// </summary>
    /// <param name="bikeClass">250 or 450</param>
    /// <param name="modelType">"Qualification" or "FinishPosition"</param>
    /// <returns>File name for model storage</returns>
    public static string GetModelFileName(Domain.Enums.BikeClass bikeClass, string modelType)
    {
        return $"{bikeClass}_{modelType}.zip";
    }

    /// <summary>
    /// Parses model info from a file name.
    /// </summary>
    /// <param name="fileName">Model file name</param>
    /// <param name="bikeClass">Extracted bike class (null if parse failed)</param>
    /// <param name="modelType">Extracted model type (null if parse failed)</param>
    /// <returns>True if parse succeeded</returns>
    public static bool TryParseModelFileName(string fileName, out string? bikeClass, out string? modelType)
    {
        bikeClass = null;
        modelType = null;

        if (string.IsNullOrEmpty(fileName))
            return false;

        // Parse names like "Class250_Qualification.zip" or "Class450_FinishPosition.zip"
        if (fileName.Contains("Class250", StringComparison.OrdinalIgnoreCase))
            bikeClass = "Class250";
        else if (fileName.Contains("Class450", StringComparison.OrdinalIgnoreCase))
            bikeClass = "Class450";
        else
            return false;

        if (fileName.Contains("Qualification", StringComparison.OrdinalIgnoreCase))
            modelType = "Qualification";
        else if (fileName.Contains("FinishPosition", StringComparison.OrdinalIgnoreCase))
            modelType = "FinishPosition";
        else
            return false;

        return true;
    }
}
