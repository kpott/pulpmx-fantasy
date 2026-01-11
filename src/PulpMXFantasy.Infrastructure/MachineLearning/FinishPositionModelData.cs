using Microsoft.ML.Data;

namespace PulpMXFantasy.Infrastructure.MachineLearning;

/// <summary>
/// Training data for finish position prediction (regression).
/// Predicts where a rider will finish (1-22) if they make the main event.
/// </summary>
/// <remarks>
/// WHY THIS MODEL EXISTS:
/// ======================
/// Second stage of prediction. Given that a rider makes the main event,
/// where will they finish? This is pure regression on qualified riders only.
///
/// KEY FEATURES (defined in MLConstants.FinishPositionFeatures):
/// - Handicap: Most important - directly correlates with expected finish
/// - AvgFinishLast5: Recent performance
/// - AvgFantasyPointsLast5: Accounts for consistency and doubling
/// - TrackHistory: Venue-specific performance
/// - RecentMomentum: Improving vs declining form
/// - IsAllStar: May affect race strategy
/// - TrackTypeIndoor: Different rider strengths for indoor vs outdoor
///
/// TARGET:
/// - FinishPosition: Actual finish (1-22)
/// - Only trained on riders who made the main event
///
/// EXPECTED PERFORMANCE:
/// - R²: 0.30-0.50 (captures 30-50% of variance - good for motorsports)
/// - MAE: 3-5 positions (predict within 3-5 spots on average)
/// - RMSE: 4-6 positions
///
/// IMPORTANT: Column names must match MLConstants.FinishPositionFeatures exactly.
/// </remarks>
public class FinishPositionModelData
{
    /// <summary>
    /// Event handicap value (-6 to +19 typical range).
    /// Strongest predictor - lower handicap = better expected finish.
    /// </summary>
    [ColumnName(MLConstants.FinishPositionFeatures.Handicap)]
    public float Handicap { get; set; }

    /// <summary>
    /// Average finish position from last 5 races.
    /// Lower = better recent form.
    /// -1 if no history.
    /// </summary>
    [ColumnName(MLConstants.FinishPositionFeatures.AvgFinishLast5)]
    public float AvgFinishLast5 { get; set; }

    /// <summary>
    /// Average fantasy points from last 5 races.
    /// Accounts for consistency, doubling, and finish position.
    /// -1 if no history.
    /// </summary>
    [ColumnName(MLConstants.FinishPositionFeatures.AvgFantasyPointsLast5)]
    public float AvgFantasyPointsLast5 { get; set; }

    /// <summary>
    /// Average finish position at this specific venue.
    /// Some riders excel at certain tracks.
    /// -1 if no history at this venue.
    /// </summary>
    [ColumnName(MLConstants.FinishPositionFeatures.TrackHistory)]
    public float TrackHistory { get; set; }

    /// <summary>
    /// Recent momentum (last 3 races vs previous races).
    /// Positive = improving, negative = declining, 0 = stable.
    /// </summary>
    [ColumnName(MLConstants.FinishPositionFeatures.RecentMomentum)]
    public float RecentMomentum { get; set; }

    /// <summary>
    /// Total championship points this season.
    /// Championship leaders tend to finish better.
    /// </summary>
    [ColumnName(MLConstants.FinishPositionFeatures.SeasonPoints)]
    public float SeasonPoints { get; set; }

    /// <summary>
    /// All-Star status.
    /// May affect race strategy and tire choice.
    /// </summary>
    [ColumnName("IsAllStar")]
    public bool IsAllStar { get; set; }

    /// <summary>
    /// Track type: true = indoor (Supercross), false = outdoor (Motocross).
    /// Different rider strengths for indoor vs outdoor.
    /// </summary>
    [ColumnName("TrackTypeIndoor")]
    public bool TrackTypeIndoor { get; set; }

    /// <summary>
    /// Target variable: Actual finish position (1-22).
    /// Only includes riders who made the main event.
    /// </summary>
    [ColumnName(MLConstants.FinishPositionFeatures.Label)]
    public float FinishPosition { get; set; }
}

/// <summary>
/// Prediction result from finish position model.
/// </summary>
public class FinishPositionPrediction
{
    /// <summary>
    /// Predicted finish position (1-22).
    /// Will be clamped to valid range and rounded to integer.
    /// </summary>
    [ColumnName(MLConstants.PredictionOutput.PredictedFinish)]
    public float PredictedFinish { get; set; }
}
