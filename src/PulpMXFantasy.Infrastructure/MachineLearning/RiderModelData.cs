using Microsoft.ML.Data;

namespace PulpMXFantasy.Infrastructure.MachineLearning;

/// <summary>
/// ML.NET model input data for rider fantasy point prediction.
/// </summary>
/// <remarks>
/// WHY THIS CLASS EXISTS:
/// ======================
/// ML.NET requires specific attribute decorations for model training/inference:
/// - [LoadColumn] - Maps CSV columns during training
/// - [ColumnName] - Specifies feature names for model
/// - Value types (float, bool) - Required by ML.NET (not nullable types)
///
/// This is separate from Domain's RiderFeatures record because:
/// - Domain layer should not reference ML.NET
/// - ML.NET needs specific attribute decorations
/// - Training pipeline expects specific types (float, not decimal)
///
/// FEATURE ENGINEERING:
/// ====================
/// 16 features designed with ML expert guidance:
///
/// **Event-Specific Features (immediate context):**
/// - Handicap: Official event handicap (-5 to +10 typical)
/// - IsAllStar: Cannot double points (0/1 binary)
/// - IsInjured: Recently injured (0/1 binary) - should predict 0 points
/// - PickTrend: % of players selecting rider (crowd wisdom)
/// - QualifyingPosition: Combined qualifying position
/// - QualifyingLapTime: Best qualifying lap in seconds
/// - QualyGapToLeader: Gap to fastest qualifier
///
/// **Historical Performance Features:**
/// - AvgFinishLast5: Average finish position last 5 races (lower = better)
/// - AvgFantasyPointsLast5: Average fantasy points last 5 races
/// - FinishRate: % of races finished (100 - DNF%)
/// - SeasonPoints: Accumulated fantasy points this season
///
/// **Track/Environment Features:**
/// - TrackHistory: Average finish at this venue (venue-specific skill)
/// - TrackTypeIndoor: Indoor/Outdoor affects setup (0/1 binary)
///
/// **Rider Context Features:**
/// - DaysSinceInjury: Recovery time (0 if not injured, >0 if recovering)
/// - TeamQualityFactory: Factory team equipment advantage (0/1 binary)
/// - RecentMomentum: Trend direction (+ improving, - declining, 0 stable)
///
/// HANDLING MISSING DATA:
/// ======================
/// For new riders or insufficient history:
/// - Use -1 as "missing data" indicator
/// - LightGBM handles missing data gracefully (built-in support)
/// - Alternative: Use series averages as fallback
/// </remarks>
public class RiderModelData
{
    /// <summary>
    /// Official event handicap value.
    /// </summary>
    [LoadColumn(0)]
    [ColumnName("Handicap")]
    public float Handicap { get; set; }

    /// <summary>
    /// All-Star status (1 = true, 0 = false).
    /// All-Stars cannot double points.
    /// </summary>
    [LoadColumn(1)]
    [ColumnName("IsAllStar")]
    public bool IsAllStar { get; set; }

    /// <summary>
    /// Injury status (1 = injured, 0 = healthy).
    /// Injured riders typically score 0 points.
    /// </summary>
    [LoadColumn(2)]
    [ColumnName("IsInjured")]
    public bool IsInjured { get; set; }

    /// <summary>
    /// Pick trend - percentage of players selecting this rider.
    /// Crowd wisdom indicator (0-100).
    /// </summary>
    [LoadColumn(3)]
    [ColumnName("PickTrend")]
    public float PickTrend { get; set; }

    /// <summary>
    /// Combined qualifying position.
    /// Lower = better starting position.
    /// </summary>
    [LoadColumn(4)]
    [ColumnName("QualifyingPosition")]
    public float QualifyingPosition { get; set; }

    /// <summary>
    /// Best qualifying lap time in seconds.
    /// Lower = faster.
    /// </summary>
    [LoadColumn(5)]
    [ColumnName("QualifyingLapTime")]
    public float QualifyingLapTime { get; set; }

    /// <summary>
    /// Gap to leader in qualifying (seconds).
    /// 0 = pole position, positive = slower.
    /// </summary>
    [LoadColumn(6)]
    [ColumnName("QualyGapToLeader")]
    public float QualyGapToLeader { get; set; }

    /// <summary>
    /// Average finish position last 5 races.
    /// Lower = better historical performance.
    /// -1 if insufficient data (< 5 races).
    /// </summary>
    [LoadColumn(7)]
    [ColumnName("AvgFinishLast5")]
    public float AvgFinishLast5 { get; set; }

    /// <summary>
    /// Average fantasy points last 5 races.
    /// Higher = better historical scoring.
    /// -1 if insufficient data.
    /// </summary>
    [LoadColumn(8)]
    [ColumnName("AvgFantasyPointsLast5")]
    public float AvgFantasyPointsLast5 { get; set; }

    /// <summary>
    /// Finish rate - percentage of races finished.
    /// 100 = always finishes, lower = more DNFs.
    /// </summary>
    [LoadColumn(9)]
    [ColumnName("FinishRate")]
    public float FinishRate { get; set; }

    /// <summary>
    /// Total fantasy points accumulated this season.
    /// Higher = better season performance.
    /// </summary>
    [LoadColumn(10)]
    [ColumnName("SeasonPoints")]
    public float SeasonPoints { get; set; }

    /// <summary>
    /// Historical average finish at this venue.
    /// Lower = better track-specific performance.
    /// -1 if no history at venue.
    /// </summary>
    [LoadColumn(11)]
    [ColumnName("TrackHistory")]
    public float TrackHistory { get; set; }

    /// <summary>
    /// Track type (1 = Indoor/Supercross, 0 = Outdoor/Motocross).
    /// Different surfaces and setup requirements.
    /// </summary>
    [LoadColumn(12)]
    [ColumnName("TrackTypeIndoor")]
    public bool TrackTypeIndoor { get; set; }

    /// <summary>
    /// Days since last injury.
    /// 0 = no recent injury, positive = recovering.
    /// Used to model recovery curves.
    /// </summary>
    [LoadColumn(13)]
    [ColumnName("DaysSinceInjury")]
    public float DaysSinceInjury { get; set; }

    /// <summary>
    /// Team quality (1 = Factory team, 0 = Privateer/Satellite).
    /// Factory teams have better equipment and support.
    /// </summary>
    [LoadColumn(14)]
    [ColumnName("TeamQualityFactory")]
    public bool TeamQualityFactory { get; set; }

    /// <summary>
    /// Recent momentum indicator.
    /// Positive = improving trend, negative = declining, 0 = stable.
    /// Calculated as (avg last 3 races) - (avg previous races).
    /// </summary>
    [LoadColumn(15)]
    [ColumnName("RecentMomentum")]
    public float RecentMomentum { get; set; }

    /// <summary>
    /// Target variable: Actual fantasy points scored.
    /// This is what the model learns to predict.
    /// </summary>
    [LoadColumn(16)]
    [ColumnName("Label")]
    public float FantasyPoints { get; set; }
}

/// <summary>
/// ML.NET model output (prediction result).
/// </summary>
/// <remarks>
/// Score = Predicted fantasy points (regression output).
/// LightGBM outputs single float for regression tasks.
/// </remarks>
public class RiderPredictionResult
{
    /// <summary>
    /// Predicted fantasy points.
    /// </summary>
    [ColumnName("Score")]
    public float PredictedFantasyPoints { get; set; }
}
