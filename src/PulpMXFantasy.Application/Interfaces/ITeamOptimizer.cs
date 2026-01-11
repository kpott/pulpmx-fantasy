using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Domain.Enums;

namespace PulpMXFantasy.Application.Interfaces;

/// <summary>
/// Service for finding optimal fantasy team selections using constraint programming.
/// </summary>
/// <remarks>
/// WHY THIS INTERFACE EXISTS:
/// ==========================
/// Team selection is a constrained optimization problem:
/// - Maximize expected fantasy points
/// - Subject to multiple constraints (roster composition, previous picks, etc.)
/// - Cannot be solved with simple greedy algorithms (constraints interact)
///
/// CONSTRAINT PROGRAMMING APPROACH:
/// ================================
/// Uses Google.OrTools CP-SAT solver:
/// - Exact optimization (finds provably optimal solution)
/// - Handles complex constraints efficiently
/// - Much better than greedy "pick top 8 riders" (which violates constraints)
///
/// FANTASY LEAGUE CONSTRAINTS:
/// ===========================
/// 1. Exactly 4 riders per class (250 and 450)
/// 2. Exactly 1 All-Star per class (scores single points only)
/// 3. Non All-Stars get DOUBLE points if handicap-adjusted position <= 10
/// 4. Cannot pick same rider in consecutive events (same series)
/// 5. Optional: Pick First-to-Finish-Line (FFL) rider (+15 if correct, -7 if wrong)
///
/// USAGE EXAMPLE:
/// <code>
/// // Get predictions from ML model
/// var predictions = await _predictionService.GetOrGeneratePredictionsAsync(eventId);
///
/// // Define constraints (exclude previous event picks)
/// var constraints = new TeamConstraints(
///     ExcludedRiders: previousEventRiderIds,
///     RequireAllStar450: true,
///     RequireAllStar250: true,
///     ConsiderFfl: false);
///
/// // Find optimal team
/// var optimalTeam = _teamOptimizer.FindOptimalTeam(predictions, constraints);
///
/// // Display team: optimalTeam.TotalExpectedPoints, optimalTeam.Riders450, etc.
/// </code>
/// </remarks>
public interface ITeamOptimizer
{
    /// <summary>
    /// Finds the optimal 8-rider fantasy team given predictions and constraints.
    /// </summary>
    /// <param name="predictions">ML predictions for all available riders</param>
    /// <param name="constraints">Team selection constraints</param>
    /// <returns>Optimal team maximizing expected fantasy points</returns>
    /// <remarks>
    /// Algorithm: Mixed Integer Programming with CP-SAT solver
    ///
    /// Decision variables:
    /// - x[i] = 1 if rider i is selected, 0 otherwise
    /// - One binary variable per eligible rider
    ///
    /// Objective function:
    /// Maximize: SUM(x[i] * ExpectedPoints[i])
    ///
    /// Constraints:
    /// - SUM(x[i] for 450 class) = 4
    /// - SUM(x[i] for 250 class) = 4
    /// - SUM(x[i] for 450 All-Stars) = 1
    /// - SUM(x[i] for 250 All-Stars) = 1
    /// - x[i] = 0 for all riders in ExcludedRiders
    ///
    /// Returns empty team if no feasible solution exists (over-constrained).
    /// </remarks>
    OptimalTeam FindOptimalTeam(
        IReadOnlyList<RiderPrediction> predictions,
        TeamConstraints constraints);
}

/// <summary>
/// Constraints for team optimization.
/// </summary>
/// <param name="ExcludedRiders">Rider IDs that cannot be selected (previous event picks)</param>
/// <param name="RequireAllStar450">Require exactly one 450 All-Star (default: true)</param>
/// <param name="RequireAllStar250">Require exactly one 250 All-Star (default: true)</param>
/// <param name="ConsiderFfl">Include First-to-Finish-Line optimization (future feature)</param>
/// <remarks>
/// EXCLUDED RIDERS:
/// Must track picks from previous event in same series to prevent consecutive selection.
/// Query from pick_history table WHERE series_id = current_series AND event_id = previous_event.
///
/// ALL-STAR REQUIREMENTS:
/// Fantasy league typically requires 1 All-Star per class.
/// All-Stars score single points (no doubling), but may be mandatory picks.
///
/// FFL (First-to-Finish-Line):
/// Optional bonus pick: +15 points if rider leads first lap, -7 if not.
/// Expected value = P(leads first lap) * 15 - (1 - P(leads first lap)) * 7
/// Requires separate ML model to predict first-lap leader probabilities.
/// Set to false for MVP (not yet implemented).
/// </remarks>
public record TeamConstraints(
    HashSet<Guid>? ExcludedRiders = null,
    bool RequireAllStar450 = true,
    bool RequireAllStar250 = true,
    bool ConsiderFfl = false)
{
    /// <summary>
    /// Gets excluded riders (empty set if null).
    /// </summary>
    public HashSet<Guid> GetExcludedRiders() => ExcludedRiders ?? new HashSet<Guid>();
}

/// <summary>
/// Optimal fantasy team result.
/// </summary>
/// <param name="Riders450">4 selected riders from 450 class</param>
/// <param name="Riders250">4 selected riders from 250 class</param>
/// <param name="TotalExpectedPoints">Sum of expected points for all 8 riders</param>
/// <param name="IsFeasible">True if optimization found valid solution, false if over-constrained</param>
/// <param name="SolveTimeMs">Time taken to solve optimization (milliseconds)</param>
/// <remarks>
/// TEAM COMPOSITION:
/// - 4 riders from 450 class (1 All-Star, 3 non-All-Stars)
/// - 4 riders from 250 class (1 All-Star, 3 non-All-Stars)
/// - Total: 8 riders
///
/// EXPECTED POINTS CALCULATION:
/// Sum of individual rider ExpectedPoints from ML predictions.
/// Accounts for doubling logic (non-All-Stars with adjusted position <= 10).
///
/// INFEASIBLE SOLUTIONS:
/// IsFeasible = false occurs when constraints cannot be satisfied:
/// - Not enough eligible riders after exclusions
/// - No All-Stars available in a class
/// - Over-constrained problem (conflicting requirements)
///
/// Example: If user picked all good riders in previous event, may not have
/// enough competitive options remaining.
/// </remarks>
public record OptimalTeam(
    IReadOnlyList<Guid> Riders450,
    IReadOnlyList<Guid> Riders250,
    float TotalExpectedPoints,
    bool IsFeasible,
    long SolveTimeMs);
