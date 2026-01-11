using Google.OrTools.Sat;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Domain.Enums;

namespace PulpMXFantasy.Infrastructure.Optimization;

/// <summary>
/// Constraint programming implementation for finding optimal fantasy team selections.
/// </summary>
/// <remarks>
/// WHY CONSTRAINT PROGRAMMING (NOT GREEDY):
/// ========================================
/// Simple greedy approach fails because constraints interact:
/// - Can't just pick top 8 riders (might get 8 from same class)
/// - Can't pick top 4 per class (might not satisfy All-Star requirement)
/// - Previous picks constraint affects available riders
///
/// Constraint programming gives us:
/// - PROVABLY OPTIMAL solution (not heuristic)
/// - Handles complex constraint interactions
/// - Fast for this problem size (40-80 riders)
///
/// GOOGLE OR-TOOLS CP-SAT SOLVER:
/// ===============================
/// Uses Conflict-Driven Clause Learning (similar to modern SAT solvers):
/// - Binary decision variables: x[i] = 1 if rider i selected, 0 otherwise
/// - Linear constraints: SUM(x[i] where condition) = target
/// - Objective: Maximize SUM(x[i] * ExpectedPoints[i])
///
/// For 80 riders with 5 constraints, solver typically finds optimal in <10ms.
///
/// FANTASY LEAGUE CONSTRAINTS ENCODED:
/// ===================================
/// 1. Exactly 4 riders from 450 class
/// 2. Exactly 4 riders from 250 class
/// 3. Exactly 1 All-Star from 450 class
/// 4. Exactly 1 All-Star from 250 class
/// 5. Cannot select excluded riders (previous event picks)
///
/// HANDLING INFEASIBILITY:
/// =======================
/// If no solution exists (over-constrained problem):
/// - Returns IsFeasible = false
/// - Empty rider lists
/// - Common causes: Not enough eligible riders, no All-Stars available
///
/// Example: User picked all top riders in previous event, leaving only injured/weak options.
/// </remarks>
public class TeamOptimizerService : ITeamOptimizer
{
    private readonly ILogger<TeamOptimizerService> _logger;

    public TeamOptimizerService(ILogger<TeamOptimizerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Finds the optimal 8-rider fantasy team using constraint programming.
    /// </summary>
    public OptimalTeam FindOptimalTeam(
        IReadOnlyList<RiderPrediction> predictions,
        TeamConstraints constraints)
    {
        var startTime = DateTimeOffset.UtcNow;

        try
        {
            // Validate input
            if (predictions == null || predictions.Count == 0)
            {
                _logger.LogWarning("No predictions provided to optimizer");
                return CreateInfeasibleTeam(0);
            }

            // Create CP-SAT model
            var model = new CpModel();

            // Create decision variables (one per rider)
            var riderVars = new Dictionary<Guid, IntVar>();
            var riderMap = new Dictionary<Guid, RiderPrediction>();

            foreach (var prediction in predictions)
            {
                // Binary variable: 1 if selected, 0 if not
                var varName = $"rider_{prediction.RiderId}";
                riderVars[prediction.RiderId] = model.NewBoolVar(varName);
                riderMap[prediction.RiderId] = prediction;
            }

            // Get excluded riders (previous event picks)
            var excludedRiders = constraints.GetExcludedRiders();

            // Constraint: Excluded riders cannot be selected
            foreach (var riderId in excludedRiders)
            {
                if (riderVars.ContainsKey(riderId))
                {
                    model.Add(riderVars[riderId] == 0);
                }
            }

            // Group riders by class
            var riders450 = predictions
                .Where(p => p.BikeClass == BikeClass.Class450)
                .ToList();

            var riders250 = predictions
                .Where(p => p.BikeClass == BikeClass.Class250)
                .ToList();

            // Validate sufficient riders available
            if (riders450.Count < 4)
            {
                _logger.LogWarning(
                    "Insufficient 450 riders: {Count} available, need 4",
                    riders450.Count);
                return CreateInfeasibleTeam((DateTimeOffset.UtcNow - startTime).Milliseconds);
            }

            if (riders250.Count < 4)
            {
                _logger.LogWarning(
                    "Insufficient 250 riders: {Count} available, need 4",
                    riders250.Count);
                return CreateInfeasibleTeam((DateTimeOffset.UtcNow - startTime).Milliseconds);
            }

            // Count All-Stars per class
            var allStars450 = riders450.Count(p => p.IsAllStar);
            var allStars250 = riders250.Count(p => p.IsAllStar);

            if (constraints.RequireAllStar450 && allStars450 == 0)
            {
                _logger.LogWarning("No 450 All-Stars available, but required by constraints");
                return CreateInfeasibleTeam((DateTimeOffset.UtcNow - startTime).Milliseconds);
            }

            if (constraints.RequireAllStar250 && allStars250 == 0)
            {
                _logger.LogWarning("No 250 All-Stars available, but required by constraints");
                return CreateInfeasibleTeam((DateTimeOffset.UtcNow - startTime).Milliseconds);
            }

            // Build constraint expressions for each class
            // 450 Class Constraints
            var selected450 = riders450
                .Select(p => riderVars[p.RiderId])
                .ToArray();

            // Constraint: Exactly 4 riders from 450 class
            model.Add(LinearExpr.Sum(selected450) == 4);

            // Constraint: Exactly 1 All-Star from 450 class (if required)
            if (constraints.RequireAllStar450)
            {
                var allStar450Vars = riders450
                    .Where(p => p.IsAllStar)
                    .Select(p => riderVars[p.RiderId])
                    .ToArray();

                if (allStar450Vars.Length > 0)
                {
                    model.Add(LinearExpr.Sum(allStar450Vars) == 1);
                }
            }

            // 250 Class Constraints
            var selected250 = riders250
                .Select(p => riderVars[p.RiderId])
                .ToArray();

            // Constraint: Exactly 4 riders from 250 class
            model.Add(LinearExpr.Sum(selected250) == 4);

            // Constraint: Exactly 1 All-Star from 250 class (if required)
            if (constraints.RequireAllStar250)
            {
                var allStar250Vars = riders250
                    .Where(p => p.IsAllStar)
                    .Select(p => riderVars[p.RiderId])
                    .ToArray();

                if (allStar250Vars.Length > 0)
                {
                    model.Add(LinearExpr.Sum(allStar250Vars) == 1);
                }
            }

            // Objective: Maximize total expected fantasy points
            // Convert float expected points to scaled integers (multiply by 1000 for precision)
            var objectiveTerms = predictions
                .Select(p => riderVars[p.RiderId] * (long)(p.ExpectedPoints * 1000))
                .ToArray();

            model.Maximize(LinearExpr.Sum(objectiveTerms));

            // Solve the model
            var solver = new CpSolver();
            solver.StringParameters = "max_time_in_seconds:10.0"; // 10 second timeout

            _logger.LogInformation(
                "Solving team optimization with {RiderCount} riders " +
                "({Count450} in 450 class, {Count250} in 250 class)",
                predictions.Count,
                riders450.Count,
                riders250.Count);

            var status = solver.Solve(model);
            var solveTime = (DateTimeOffset.UtcNow - startTime).Milliseconds;

            // Check solution status
            if (status != CpSolverStatus.Optimal && status != CpSolverStatus.Feasible)
            {
                _logger.LogWarning(
                    "No feasible solution found. Solver status: {Status}",
                    status);
                return CreateInfeasibleTeam(solveTime);
            }

            // Extract selected riders
            var selectedRiders450 = riders450
                .Where(p => solver.Value(riderVars[p.RiderId]) == 1)
                .Select(p => p.RiderId)
                .ToList();

            var selectedRiders250 = riders250
                .Where(p => solver.Value(riderVars[p.RiderId]) == 1)
                .Select(p => p.RiderId)
                .ToList();

            // Calculate total expected points (from actual float values, not scaled integers)
            var totalPoints = predictions
                .Where(p => solver.Value(riderVars[p.RiderId]) == 1)
                .Sum(p => p.ExpectedPoints);

            _logger.LogInformation(
                "Optimal team found: {Count450} from 450 class, {Count250} from 250 class, " +
                "{TotalPoints:F1} expected points (solved in {SolveTime}ms)",
                selectedRiders450.Count,
                selectedRiders250.Count,
                totalPoints,
                solveTime);

            return new OptimalTeam(
                Riders450: selectedRiders450,
                Riders250: selectedRiders250,
                TotalExpectedPoints: totalPoints,
                IsFeasible: true,
                SolveTimeMs: solveTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in team optimization");
            return CreateInfeasibleTeam((DateTimeOffset.UtcNow - startTime).Milliseconds);
        }
    }

    /// <summary>
    /// Creates an infeasible team result.
    /// </summary>
    private OptimalTeam CreateInfeasibleTeam(long solveTimeMs)
    {
        return new OptimalTeam(
            Riders450: Array.Empty<Guid>(),
            Riders250: Array.Empty<Guid>(),
            TotalExpectedPoints: 0,
            IsFeasible: false,
            SolveTimeMs: solveTimeMs);
    }
}
