using PulpMXFantasy.Domain.Enums;
using PulpMXFantasy.Domain.ValueObjects;

namespace PulpMXFantasy.Domain.Entities;

/// <summary>
/// Represents a rider's participation in a specific event.
/// This is the MOST CRITICAL entity in the system - it contains all event-specific data.
/// </summary>
/// <remarks>
/// WHY THIS IS THE CORE ENTITY:
/// 1. Contains handicap (changes per event) - the foundation of fantasy scoring
/// 2. Contains All-Star status (determines if points are doubled)
/// 3. Contains qualifying results (used for ML predictions)
/// 4. Contains finish position and calculated fantasy points (results)
/// 5. Links riders to events (many-to-many relationship)
///
/// RELATIONSHIP STRUCTURE:
/// - ONE event -> MANY event riders (40-80 riders per event)
/// - ONE rider -> MANY event riders (one per event they participate in)
/// - EventRider is the join entity with rich event-specific data
///
/// FANTASY SCORING EXAMPLE:
/// Chase Sexton finishes 5th with handicap +2, not All-Star:
/// - Adjusted position: 5 - 2 = 3rd
/// - Base points: 20 (from points table for 3rd)
/// - Doubled: Yes (3rd <= 10 and not All-Star)
/// - Total fantasy points: 20 * 2 = 40 points
///
/// If he were All-Star: 20 points (no doubling)
/// If he finished 12th: 11 points (12 - 2 = 10th adjusted, no doubling past 10th)
///
/// ML FEATURE IMPORTANCE:
/// This entity provides most of the ML input features:
/// - Handicap (strongest predictor)
/// - IsAllStar (affects doubling)
/// - PickTrend (crowd wisdom)
/// - QualifyingPosition, QualifyingLapTime (recent form)
/// - Historical results calculated from previous EventRider records
///
/// DATABASE MAPPING:
/// - Primary key: Id (UUID)
/// - Foreign keys: EventId, RiderId
/// - Unique constraint on (EventId, RiderId) - rider can't be in same event twice
/// - Indexed on BikeClass for filtering 250 vs 450
/// </remarks>
public class EventRider
{
    /// <summary>
    /// Internal unique identifier for this event participation
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Foreign key to the event this rider is participating in
    /// </summary>
    public required Guid EventId { get; init; }

    /// <summary>
    /// Foreign key to the rider participating in this event
    /// </summary>
    public required Guid RiderId { get; init; }

    /// <summary>
    /// Bike class the rider is competing in for this event
    /// </summary>
    /// <remarks>
    /// IMPORTANT: A rider can move between classes across seasons.
    /// Example: Rider may compete in 250 class in 2024, then move to 450 in 2025.
    /// Therefore, BikeClass is stored per event, not on the Rider entity.
    /// </remarks>
    public required BikeClass BikeClass { get; set; }

    /// <summary>
    /// Handicap value for this rider at this event (-5 to +10 typically)
    /// </summary>
    /// <remarks>
    /// THE MOST IMPORTANT FIELD FOR FANTASY SCORING!
    ///
    /// Handicap is PulpMX Fantasy's attempt to level the playing field:
    /// - Positive handicap: Helps lower-skill riders (e.g., +5 means 10th place = 5th for points)
    /// - Negative handicap: Penalizes top riders (e.g., -2 means 1st place = 3rd for points)
    /// - Zero handicap: No adjustment (common for mid-tier riders)
    ///
    /// Handicaps are set by PulpMX staff before each event and can change based on:
    /// - Recent performance trends
    /// - Injury recovery status
    /// - Equipment changes (factory vs. privateer)
    ///
    /// CRITICAL: Handicap changes EVERY event - it's not static!
    ///
    /// EXAMPLE IMPACT:
    /// Rider finishes 8th with +3 handicap:
    /// - Adjusted position: 8 - 3 = 5th
    /// - Points: 17 (from table) * 2 (doubled, 5th <= 10) = 34 points
    ///
    /// Same rider finishes 8th with 0 handicap:
    /// - Adjusted position: 8
    /// - Points: 14 (from table) * 2 = 28 points
    /// </remarks>
    public required int Handicap { get; set; }

    /// <summary>
    /// Whether the rider is designated as an All-Star for this event
    /// </summary>
    /// <remarks>
    /// CRITICAL FOR DOUBLING LOGIC!
    ///
    /// All-Stars are the top-tier riders who:
    /// 1. NEVER get their points doubled (even if adjusted position <= 10)
    /// 2. Typically have negative handicaps (e.g., -2 or -3)
    /// 3. Are still valuable picks due to high base points from top finishes
    ///
    /// Fantasy team constraint: MUST pick exactly 1 All-Star per class
    ///
    /// EXAMPLE:
    /// Eli Tomac (All-Star) finishes 1st with -2 handicap:
    /// - Adjusted: 1 - (-2) = 3rd
    /// - Base points: 20
    /// - Doubled: NO (All-Star never doubles)
    /// - Total: 20 points
    ///
    /// Non-All-Star finishes 1st with 0 handicap:
    /// - Adjusted: 1st
    /// - Base points: 25
    /// - Doubled: YES (1st <= 10, not All-Star)
    /// - Total: 50 points
    ///
    /// This creates interesting strategic tension: Pick All-Stars for consistency
    /// or gamble on non-All-Stars for huge point potential?
    /// </remarks>
    public required bool IsAllStar { get; set; }

    /// <summary>
    /// Whether the rider is currently injured and unlikely to finish well
    /// </summary>
    /// <remarks>
    /// Used for:
    /// - Filtering out riders from recommendations
    /// - ML model feature (injured riders rarely score well)
    /// - UI warnings to users
    ///
    /// Important: "Injured" doesn't mean DNF - rider may still race but at reduced capacity.
    /// Example: Rider racing with broken collarbone may finish 15th instead of 3rd.
    /// </remarks>
    public bool IsInjured { get; set; } = false;

    /// <summary>
    /// Percentage of fantasy players who have picked this rider (0-100)
    /// </summary>
    /// <remarks>
    /// "Crowd wisdom" metric from PulpMX API.
    ///
    /// ML FEATURE: Higher pick trend often correlates with better performance
    /// (crowds collectively identify good value picks).
    ///
    /// STRATEGIC USE:
    /// - High pick trend (>50%): Safe pick, but less differentiation vs. opponents
    /// - Low pick trend (<10%): Risky, but big advantage if rider outperforms
    ///
    /// Example: 72.5 means 72.5% of players picked this rider
    /// </remarks>
    public decimal? PickTrend { get; set; }

    /// <summary>
    /// Combined qualifying position (1 = fastest, higher = slower)
    /// </summary>
    /// <remarks>
    /// ML FEATURE: Strong predictor of race finish.
    /// Calculated from qualifying sessions (typically 2 sessions, best lap counts).
    ///
    /// Example: CombinedQualyPosition = 3 means 3rd fastest in qualifying.
    /// </remarks>
    public int? CombinedQualyPosition { get; set; }

    /// <summary>
    /// Best qualifying lap time in seconds
    /// </summary>
    /// <remarks>
    /// ML FEATURE: Absolute speed metric (faster lap = better chance of winning).
    ///
    /// Example: 48.327 seconds for a supercross lap.
    /// </remarks>
    public decimal? BestQualyLapSeconds { get; set; }

    /// <summary>
    /// Gap to the fastest qualifier in seconds
    /// </summary>
    /// <remarks>
    /// ML FEATURE: More informative than absolute lap time across different tracks.
    ///
    /// Example: QualyGapToLeader = 1.234 means 1.234 seconds slower than P1.
    /// A gap > 2 seconds typically indicates no chance of winning.
    /// </remarks>
    public decimal? QualyGapToLeader { get; set; }

    /// <summary>
    /// Actual finish position in the race (1 = winner, 22+ = DNF typically)
    /// </summary>
    /// <remarks>
    /// NULL until race completes.
    /// Used for:
    /// - Calculating fantasy points
    /// - Training ML models
    /// - Tracking prediction accuracy
    ///
    /// Special cases:
    /// - DNF (Did Not Finish): Assigned position 23+ or similar
    /// - DNS (Did Not Start): No finish position recorded
    /// </remarks>
    public int? FinishPosition { get; set; }

    /// <summary>
    /// Finish position after applying handicap adjustment
    /// </summary>
    /// <remarks>
    /// Calculated as: FinishPosition - Handicap
    /// Clamped to minimum of 1 (can't have 0th or negative position).
    ///
    /// THIS IS THE POSITION USED FOR FANTASY SCORING!
    ///
    /// Example:
    /// - FinishPosition = 10, Handicap = +3
    /// - HandicapAdjustedPosition = 10 - 3 = 7
    /// - Fantasy points based on 7th place in points table
    /// </remarks>
    public int? HandicapAdjustedPosition { get; set; }

    /// <summary>
    /// Calculated fantasy points for this event
    /// </summary>
    /// <remarks>
    /// NULL until race completes and fantasy points are calculated.
    ///
    /// Calculation flow:
    /// 1. Get HandicapAdjustedPosition
    /// 2. Look up base points from points table (1st=25, 2nd=22, etc.)
    /// 3. If (!IsAllStar AND HandicapAdjustedPosition <= 10): multiply by 2
    /// 4. Store result here
    ///
    /// This is the value summed across all team riders to get total team score.
    ///
    /// Example: 40 points (20 base * 2 for doubling)
    /// </remarks>
    public int? FantasyPoints { get; set; }

    /// <summary>
    /// Timestamp when this event rider record was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp of the last update to this record
    /// </summary>
    /// <remarks>
    /// Updated when:
    /// - Handicap changes (common before race day)
    /// - Qualifying results are published
    /// - Race results are finalized
    /// - Fantasy points are calculated
    /// </remarks>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Navigation property to the event this rider is participating in
    /// </summary>
    public Event Event { get; init; } = null!;

    /// <summary>
    /// Navigation property to the rider entity
    /// </summary>
    public Rider Rider { get; init; } = null!;

    /// <summary>
    /// Calculates fantasy points based on finish position, handicap, and All-Star status.
    /// Updates FantasyPoints, HandicapAdjustedPosition properties.
    /// </summary>
    /// <remarks>
    /// THIS METHOD IMPLEMENTS THE CORE FANTASY SCORING LOGIC!
    ///
    /// Call this after race completion when FinishPosition is set.
    /// Uses the FantasyPoints value object for calculation.
    ///
    /// Side effects:
    /// - Sets HandicapAdjustedPosition
    /// - Sets FantasyPoints
    /// - Updates UpdatedAt timestamp
    ///
    /// Example usage:
    /// <code>
    /// eventRider.FinishPosition = 8;
    /// eventRider.CalculateFantasyPoints();
    /// // Now eventRider.FantasyPoints contains the calculated value
    /// </code>
    /// </remarks>
    public void CalculateFantasyPoints()
    {
        if (!FinishPosition.HasValue)
        {
            throw new InvalidOperationException(
                "Cannot calculate fantasy points without a finish position. " +
                "Set FinishPosition before calling CalculateFantasyPoints().");
        }

        // Calculate handicap-adjusted position (minimum of 1)
        var adjustedPosition = Math.Max(1, FinishPosition.Value - Handicap);
        HandicapAdjustedPosition = adjustedPosition;

        // Use FantasyPoints value object for scoring logic
        var points = ValueObjects.FantasyPoints.Calculate(adjustedPosition, IsAllStar);
        FantasyPoints = points.TotalPoints;

        // Update timestamp
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets a breakdown of the fantasy points calculation for display/debugging.
    /// </summary>
    /// <returns>
    /// String describing the calculation, e.g.,
    /// "Finished 8th with +3 handicap = 5th adjusted. Base: 17 points × 2 (doubled) = 34 points"
    /// </returns>
    public string GetPointsBreakdown()
    {
        if (!FinishPosition.HasValue || !HandicapAdjustedPosition.HasValue || !FantasyPoints.HasValue)
        {
            return "Points not yet calculated (race not completed)";
        }

        var basePoints = ValueObjects.FantasyPoints.GetBasePoints(HandicapAdjustedPosition.Value);
        var isDoubled = FantasyPoints.Value == basePoints * 2;

        var handicapDescription = Handicap switch
        {
            > 0 => $"+{Handicap} handicap",
            < 0 => $"{Handicap} handicap",
            _ => "no handicap"
        };

        var doublingDescription = isDoubled
            ? " × 2 (doubled)"
            : IsAllStar
                ? " (All-Star, no doubling)"
                : " (no doubling past 10th)";

        return $"Finished {FinishPosition}{GetOrdinalSuffix(FinishPosition.Value)} with {handicapDescription} " +
               $"= {HandicapAdjustedPosition}{GetOrdinalSuffix(HandicapAdjustedPosition.Value)} adjusted. " +
               $"Base: {basePoints} points{doublingDescription} = {FantasyPoints} points";
    }

    /// <summary>
    /// Helper method to get ordinal suffix (1st, 2nd, 3rd, 4th, etc.)
    /// </summary>
    private static string GetOrdinalSuffix(int number)
    {
        if (number <= 0) return "th";

        return (number % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (number % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            }
        };
    }
}
