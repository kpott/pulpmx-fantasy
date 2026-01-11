using Microsoft.Extensions.Logging;
using NSubstitute;
using PulpMXFantasy.Contracts.Interfaces;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Domain.Enums;
using PulpMXFantasy.Infrastructure.Optimization;

namespace PulpMXFantasy.Infrastructure.Tests.Optimization;

/// <summary>
/// Unit tests for TeamOptimizerService constraint programming implementation.
/// </summary>
/// <remarks>
/// WHY THESE TESTS:
/// ================
/// Team optimizer implements complex constraint satisfaction logic using
/// Google.OrTools. Tests verify:
/// 1. Constraint enforcement (4 riders per class, 1 All-Star per class)
/// 2. Excluded riders are not selected
/// 3. Optimal team maximizes expected points
/// 4. Infeasible scenarios are detected correctly
/// </remarks>
public class TeamOptimizerServiceTests
{
    private readonly ITeamOptimizer _optimizer;
    private readonly ILogger<TeamOptimizerService> _logger;

    public TeamOptimizerServiceTests()
    {
        _logger = Substitute.For<ILogger<TeamOptimizerService>>();
        _optimizer = new TeamOptimizerService(_logger);
    }

    [Fact]
    public void FindOptimalTeam_WithValidPredictions_Returns8Riders()
    {
        // Arrange: Create predictions with sufficient riders
        var predictions = CreateSamplePredictions(
            riders450: 10,
            riders250: 10,
            allStars450: 2,
            allStars250: 2);

        var constraints = new TeamConstraints();

        // Act
        var result = _optimizer.FindOptimalTeam(predictions, constraints);

        // Assert
        Assert.True(result.IsFeasible);
        Assert.Equal(4, result.Riders450.Count);
        Assert.Equal(4, result.Riders250.Count);
        Assert.True(result.TotalExpectedPoints > 0);
    }

    [Fact]
    public void FindOptimalTeam_SelectsHighestScoringRiders()
    {
        // Arrange: Create riders with known scores
        var topRider450 = Guid.NewGuid();
        var topRider250 = Guid.NewGuid();

        var predictions = new List<RiderPrediction>
        {
            // 450 class - top rider with 100 points
            new(topRider450, BikeClass.Class450, false, 100f, 100f, 1, 90f, 110f, 0.9f),
            new(Guid.NewGuid(), BikeClass.Class450, false, 80f, 80f, 2, 70f, 90f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class450, false, 70f, 70f, 3, 60f, 80f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class450, false, 60f, 60f, 4, 50f, 70f, 0.7f),
            new(Guid.NewGuid(), BikeClass.Class450, true, 50f, 50f, 5, 40f, 60f, 0.7f),  // All-Star

            // 250 class - top rider with 90 points
            new(topRider250, BikeClass.Class250, false, 90f, 90f, 1, 80f, 100f, 0.9f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 75f, 75f, 2, 65f, 85f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 65f, 65f, 3, 55f, 75f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 55f, 55f, 4, 45f, 65f, 0.7f),
            new(Guid.NewGuid(), BikeClass.Class250, true, 45f, 45f, 5, 35f, 55f, 0.7f),  // All-Star
        };

        var constraints = new TeamConstraints();

        // Act
        var result = _optimizer.FindOptimalTeam(predictions, constraints);

        // Assert
        Assert.True(result.IsFeasible);
        Assert.Contains(topRider450, result.Riders450);  // Top 450 rider selected
        Assert.Contains(topRider250, result.Riders250);  // Top 250 rider selected
    }

    [Fact]
    public void FindOptimalTeam_RespectsExcludedRiders()
    {
        // Arrange: Create predictions and exclude specific riders
        var excludedRider1 = Guid.NewGuid();
        var excludedRider2 = Guid.NewGuid();

        var predictions = new List<RiderPrediction>
        {
            // 450 class
            new(excludedRider1, BikeClass.Class450, false, 100f, 100f, 1, 90f, 110f, 0.9f),  // Excluded but high scoring
            new(Guid.NewGuid(), BikeClass.Class450, false, 80f, 80f, 2, 70f, 90f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class450, false, 70f, 70f, 3, 60f, 80f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class450, false, 60f, 60f, 4, 50f, 70f, 0.7f),
            new(Guid.NewGuid(), BikeClass.Class450, true, 50f, 50f, 5, 40f, 60f, 0.7f),

            // 250 class
            new(excludedRider2, BikeClass.Class250, false, 90f, 90f, 1, 80f, 100f, 0.9f),  // Excluded but high scoring
            new(Guid.NewGuid(), BikeClass.Class250, false, 75f, 75f, 2, 65f, 85f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 65f, 65f, 3, 55f, 75f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 55f, 55f, 4, 45f, 65f, 0.7f),
            new(Guid.NewGuid(), BikeClass.Class250, true, 45f, 45f, 5, 35f, 55f, 0.7f),
        };

        var constraints = new TeamConstraints(
            ExcludedRiders: new HashSet<Guid> { excludedRider1, excludedRider2 });

        // Act
        var result = _optimizer.FindOptimalTeam(predictions, constraints);

        // Assert
        Assert.True(result.IsFeasible);
        Assert.DoesNotContain(excludedRider1, result.Riders450);  // Excluded rider NOT selected
        Assert.DoesNotContain(excludedRider2, result.Riders250);  // Excluded rider NOT selected
    }

    [Fact]
    public void FindOptimalTeam_SelectsExactlyOneAllStarPerClass()
    {
        // Arrange
        var allStar450_1 = Guid.NewGuid();
        var allStar450_2 = Guid.NewGuid();
        var allStar250_1 = Guid.NewGuid();
        var allStar250_2 = Guid.NewGuid();

        var predictions = new List<RiderPrediction>
        {
            // 450 class
            new(Guid.NewGuid(), BikeClass.Class450, false, 80f, 80f, 1, 70f, 90f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class450, false, 70f, 70f, 2, 60f, 80f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class450, false, 60f, 60f, 3, 50f, 70f, 0.7f),
            new(allStar450_1, BikeClass.Class450, true, 55f, 55f, 4, 45f, 65f, 0.7f),  // All-Star
            new(allStar450_2, BikeClass.Class450, true, 50f, 50f, 5, 40f, 60f, 0.7f),  // All-Star

            // 250 class
            new(Guid.NewGuid(), BikeClass.Class250, false, 75f, 75f, 1, 65f, 85f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 65f, 65f, 2, 55f, 75f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 55f, 55f, 3, 45f, 65f, 0.7f),
            new(allStar250_1, BikeClass.Class250, true, 50f, 50f, 4, 40f, 60f, 0.7f),  // All-Star
            new(allStar250_2, BikeClass.Class250, true, 45f, 45f, 5, 35f, 55f, 0.7f),  // All-Star
        };

        var constraints = new TeamConstraints();

        // Act
        var result = _optimizer.FindOptimalTeam(predictions, constraints);

        // Assert
        Assert.True(result.IsFeasible);

        // Count All-Stars in result
        var selectedAllStars450 = result.Riders450
            .Count(id => predictions.Any(p => p.RiderId == id && p.IsAllStar));
        var selectedAllStars250 = result.Riders250
            .Count(id => predictions.Any(p => p.RiderId == id && p.IsAllStar));

        Assert.Equal(1, selectedAllStars450);  // Exactly 1 All-Star from 450
        Assert.Equal(1, selectedAllStars250);  // Exactly 1 All-Star from 250
    }

    [Fact]
    public void FindOptimalTeam_InsufficientRiders450_ReturnsInfeasible()
    {
        // Arrange: Only 3 riders in 450 class (need 4)
        var predictions = new List<RiderPrediction>
        {
            // 450 class - ONLY 3 riders (need 4)
            new(Guid.NewGuid(), BikeClass.Class450, false, 80f, 80f, 1, 70f, 90f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class450, false, 70f, 70f, 2, 60f, 80f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class450, true, 50f, 50f, 3, 40f, 60f, 0.7f),

            // 250 class - sufficient riders
            new(Guid.NewGuid(), BikeClass.Class250, false, 75f, 75f, 1, 65f, 85f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 65f, 65f, 2, 55f, 75f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 55f, 55f, 3, 45f, 65f, 0.7f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 50f, 50f, 4, 40f, 60f, 0.7f),
            new(Guid.NewGuid(), BikeClass.Class250, true, 45f, 45f, 5, 35f, 55f, 0.7f),
        };

        var constraints = new TeamConstraints();

        // Act
        var result = _optimizer.FindOptimalTeam(predictions, constraints);

        // Assert
        Assert.False(result.IsFeasible);  // Should be infeasible
        Assert.Empty(result.Riders450);
        Assert.Empty(result.Riders250);
    }

    [Fact]
    public void FindOptimalTeam_NoAllStarsAvailable_ReturnsInfeasible()
    {
        // Arrange: No All-Stars in 450 class
        var predictions = new List<RiderPrediction>
        {
            // 450 class - NO All-Stars
            new(Guid.NewGuid(), BikeClass.Class450, false, 80f, 80f, 1, 70f, 90f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class450, false, 70f, 70f, 2, 60f, 80f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class450, false, 60f, 60f, 3, 50f, 70f, 0.7f),
            new(Guid.NewGuid(), BikeClass.Class450, false, 55f, 55f, 4, 45f, 65f, 0.7f),

            // 250 class - has All-Star
            new(Guid.NewGuid(), BikeClass.Class250, false, 75f, 75f, 1, 65f, 85f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 65f, 65f, 2, 55f, 75f, 0.8f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 55f, 55f, 3, 45f, 65f, 0.7f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 50f, 50f, 4, 40f, 60f, 0.7f),
            new(Guid.NewGuid(), BikeClass.Class250, true, 45f, 45f, 5, 35f, 55f, 0.7f),
        };

        var constraints = new TeamConstraints();

        // Act
        var result = _optimizer.FindOptimalTeam(predictions, constraints);

        // Assert
        Assert.False(result.IsFeasible);  // Should be infeasible
    }

    /// <summary>
    /// Helper: Creates sample predictions for testing.
    /// </summary>
    private List<RiderPrediction> CreateSamplePredictions(
        int riders450,
        int riders250,
        int allStars450,
        int allStars250)
    {
        var predictions = new List<RiderPrediction>();

        // Add 450 non-All-Stars
        for (int i = 0; i < riders450 - allStars450; i++)
        {
            predictions.Add(new RiderPrediction(
                Guid.NewGuid(),
                BikeClass.Class450,
                false,
                80f - i * 5,  // Decreasing expected points
                80f - i * 5,  // Points if qualifies (same for test simplicity)
                i + 1,        // Predicted finish position
                70f - i * 5,
                90f - i * 5,
                0.8f));
        }

        // Add 450 All-Stars
        for (int i = 0; i < allStars450; i++)
        {
            predictions.Add(new RiderPrediction(
                Guid.NewGuid(),
                BikeClass.Class450,
                true,
                50f - i * 5,
                50f - i * 5,  // Points if qualifies
                riders450 - allStars450 + i + 1,  // Predicted finish position
                40f - i * 5,
                60f - i * 5,
                0.7f));
        }

        // Add 250 non-All-Stars
        for (int i = 0; i < riders250 - allStars250; i++)
        {
            predictions.Add(new RiderPrediction(
                Guid.NewGuid(),
                BikeClass.Class250,
                false,
                75f - i * 5,
                75f - i * 5,  // Points if qualifies
                i + 1,        // Predicted finish position
                65f - i * 5,
                85f - i * 5,
                0.8f));
        }

        // Add 250 All-Stars
        for (int i = 0; i < allStars250; i++)
        {
            predictions.Add(new RiderPrediction(
                Guid.NewGuid(),
                BikeClass.Class250,
                true,
                45f - i * 5,
                45f - i * 5,  // Points if qualifies
                riders250 - allStars250 + i + 1,  // Predicted finish position
                35f - i * 5,
                55f - i * 5,
                0.7f));
        }

        return predictions;
    }
}
