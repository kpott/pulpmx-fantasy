namespace PulpMXFantasy.Domain.ValueObjects;

/// <summary>
/// Value object representing fantasy points calculated from a finish position.
/// </summary>
/// <remarks>
/// Encapsulates the core fantasy scoring logic:
/// 1. Points table: 1st=25, 2nd=22, 3rd=20, 4th=18, 5th=17... down to 22nd=0
/// 2. Non-All-Star riders get DOUBLE points if handicap-adjusted position ≤ 10
/// 3. All-Star riders NEVER get doubled (they get single points only)
///
/// This is immutable (record struct) to ensure scoring consistency.
/// </remarks>
public readonly record struct FantasyPoints
{
    /// <summary>
    /// Official AMA Supercross/Motocross points table for finish positions 1-22
    /// </summary>
    /// <remarks>
    /// Index 0 = 1st place (25 pts), Index 1 = 2nd place (22 pts), etc.
    /// Positions 23+ receive 0 points
    /// </remarks>
    private static readonly int[] PointsTable =
    {
        25, 22, 20, 18, 17, 16, 15, 14, 13, 12,  // 1st-10th
        11, 10,  9,  8,  7,  6,  5,  4,  3,  2,  // 11th-20th
         1,  0   // 21st-22nd+
    };

    /// <summary>
    /// Base points before any doubling multiplier is applied
    /// </summary>
    public int BasePoints { get; init; }

    /// <summary>
    /// Whether these points are eligible for doubling (top 10, non-All-Star)
    /// </summary>
    public bool IsDoubled { get; init; }

    /// <summary>
    /// Final fantasy points after doubling (if applicable)
    /// </summary>
    public int TotalPoints => IsDoubled ? BasePoints * 2 : BasePoints;

    /// <summary>
    /// Calculates fantasy points based on a rider's handicap-adjusted finish position.
    /// </summary>
    /// <param name="adjustedPosition">Finish position after handicap applied (1-22+)</param>
    /// <param name="isAllStar">Whether the rider is an All-Star (no doubling)</param>
    /// <returns>FantasyPoints value object with base points and doubling information</returns>
    /// <remarks>
    /// Examples:
    /// - 3rd place, All-Star: 20 points (no double)
    /// - 3rd place, non-All-Star: 40 points (20 * 2)
    /// - 11th place, non-All-Star: 11 points (11 * 1, past 10th)
    /// - 1st place, non-All-Star: 50 points (25 * 2)
    /// </remarks>
    public static FantasyPoints Calculate(int adjustedPosition, bool isAllStar)
    {
        // Clamp position to valid range (1-22, anything beyond = 0 points)
        var clampedPosition = Math.Clamp(adjustedPosition, 1, PointsTable.Length + 1);

        // Get base points from table (0-indexed)
        var basePoints = clampedPosition <= PointsTable.Length
            ? PointsTable[clampedPosition - 1]
            : 0;

        // Double points if: (1) not All-Star AND (2) adjusted position <= 10
        var canDouble = !isAllStar && adjustedPosition <= 10;

        return new FantasyPoints
        {
            BasePoints = basePoints,
            IsDoubled = canDouble
        };
    }

    /// <summary>
    /// Gets the base points for a specific finish position without any doubling logic.
    /// </summary>
    /// <param name="position">Finish position (1-22+)</param>
    /// <returns>Base points from the official points table</returns>
    public static int GetBasePoints(int position)
    {
        if (position < 1 || position > PointsTable.Length)
            return 0;

        return PointsTable[position - 1];
    }
}
