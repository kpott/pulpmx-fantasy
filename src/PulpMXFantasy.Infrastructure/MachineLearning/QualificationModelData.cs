using Microsoft.ML.Data;

namespace PulpMXFantasy.Infrastructure.MachineLearning;

/// <summary>
/// Training data for qualification prediction (binary classification).
/// Predicts whether a rider will make the main event (top 22 finishers).
/// </summary>
/// <remarks>
/// WHY THIS MODEL EXISTS:
/// ======================
/// The first stage in fantasy prediction is determining if a rider qualifies for the main event.
/// Only 22 of ~40 riders make it. This is fundamentally different from predicting their finish position.
///
/// KEY FEATURES (defined in MLConstants.QualificationFeatures):
/// - Handicap: Most important - higher handicap riders more likely to qualify
/// - AvgFinishLast5: Recent form indicator
/// - FinishRate: Consistency - riders who finish races vs DNF
/// - TrackHistory: Venue-specific performance
/// - IsAllStar: All-Stars have ~95%+ qualification rate
/// NOTE: PickTrend was removed - it's not available at prediction time
///
/// TARGET:
/// - MadeMain = true if FinishPosition ≤ 22
/// - MadeMain = false if DNQ or FinishPosition > 22
///
/// EXPECTED PERFORMANCE:
/// - Accuracy: 75-80% (much better than random 55%)
/// - AUC-ROC: 0.80-0.85 (good discrimination)
/// - Precision (made main): 85% (low false positives)
///
/// IMPORTANT: Column names must match MLConstants.QualificationFeatures exactly.
/// The order of properties here determines the feature vector order.
/// </remarks>
public class QualificationModelData
{
    /// <summary>
    /// Event handicap value (-6 to +19 typical range).
    /// Higher handicap = more likely to qualify.
    /// </summary>
    [ColumnName(MLConstants.QualificationFeatures.Handicap)]
    public float Handicap { get; set; }

    /// <summary>
    /// Average finish position from last 5 races (PRIMARY FEATURE).
    /// Lower = better recent form.
    /// -1 if no history (new rider).
    /// </summary>
    [ColumnName(MLConstants.QualificationFeatures.AvgFinishLast5)]
    public float AvgFinishLast5 { get; set; }

    /// <summary>
    /// Percentage of races finished (0-100).
    /// 100 = never DNF, 80 = finishes 4 of 5 races.
    /// </summary>
    [ColumnName(MLConstants.QualificationFeatures.FinishRate)]
    public float FinishRate { get; set; }

    /// <summary>
    /// Average finish position at this specific venue in past years.
    /// -1 if never raced here before.
    /// </summary>
    [ColumnName(MLConstants.QualificationFeatures.TrackHistory)]
    public float TrackHistory { get; set; }

    // NOTE: PickTrend was removed - it's not available at prediction time (only after event starts)

    /// <summary>
    /// All-Star status (1.0 = true, 0.0 = false).
    /// All-Stars have ~95%+ qualification rate.
    /// </summary>
    [ColumnName(MLConstants.QualificationFeatures.IsAllStar)]
    public float IsAllStar { get; set; }

    /// <summary>
    /// Target variable: Did the rider make the main event?
    /// true = FinishPosition ≤ 22 (qualified)
    /// false = DNQ or FinishPosition > 22
    /// </summary>
    [ColumnName(MLConstants.QualificationFeatures.Label)]
    public bool MadeMain { get; set; }
}

/// <summary>
/// Prediction result from qualification model.
/// </summary>
public class QualificationPrediction
{
    /// <summary>
    /// Predicted label (true = will make main, false = will not).
    /// </summary>
    [ColumnName(MLConstants.PredictionOutput.PredictedLabel)]
    public bool PredictedMadeMain { get; set; }

    /// <summary>
    /// Probability of making the main event (0.0 to 1.0).
    /// Example: 0.85 = 85% chance of qualifying.
    /// </summary>
    [ColumnName(MLConstants.PredictionOutput.Probability)]
    public float Probability { get; set; }

    /// <summary>
    /// Raw score from the model (log odds).
    /// Positive = likely to qualify, negative = unlikely.
    /// </summary>
    [ColumnName(MLConstants.PredictionOutput.Score)]
    public float Score { get; set; }
}
