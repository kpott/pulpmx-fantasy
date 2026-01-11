using PulpMXFantasy.Domain.Entities;
using PulpMXFantasy.Domain.Enums;

namespace PulpMXFantasy.Domain.Tests.Entities;

/// <summary>
/// Unit tests for EventRider entity fantasy points calculation.
/// </summary>
/// <remarks>
/// WHY THESE TESTS:
/// ================
/// CalculateFantasyPoints() contains critical business logic that determines
/// user scores. These tests verify the scoring rules are correctly implemented:
///
/// 1. Non-All-Stars get DOUBLE points if adjusted position <= 10
/// 2. All-Stars never get double points
/// 3. Handicap adjusts finish position (can be negative or positive)
/// 4. Points table: 1st=25, 2nd=22, 3rd=20, 4th=18, 5th=17, etc.
/// 5. Positions > 21 score 0 points
/// </remarks>
public class EventRiderTests
{
    [Theory]
    [InlineData(1, 0, false, 50)]      // 1st place, no handicap, not All-Star = 25 * 2 = 50 (doubling applies!)
    [InlineData(5, 0, false, 34)]      // 5th place, no handicap, not All-Star = 17 * 2 = 34 (doubles)
    [InlineData(5, 2, false, 40)]      // 5th place, +2 handicap = 3rd adjusted, not All-Star = 20 * 2 = 40
    [InlineData(10, 0, false, 24)]     // 10th place, no handicap, not All-Star = 12 * 2 = 24
    [InlineData(11, 0, false, 11)]     // 11th place, no handicap, not All-Star = 11 (no doubling, > 10th)
    [InlineData(5, 0, true, 17)]       // 5th place, All-Star = 17 (All-Stars never double)
    [InlineData(1, 0, true, 25)]       // 1st place, All-Star = 25
    [InlineData(15, -5, false, 2)]     // 15th place, -5 handicap = 20th adjusted = 2 (no doubling, beyond 10th)
    [InlineData(22, 0, false, 0)]      // 22nd place = 0 points
    [InlineData(25, 0, false, 0)]      // 25th place (DNF) = 0 points
    public void CalculateFantasyPoints_ReturnsCorrectPoints(
        int finishPosition,
        int handicap,
        bool isAllStar,
        int expectedPoints)
    {
        // Arrange
        var eventRider = CreateTestEventRider(finishPosition, handicap, isAllStar);

        // Act
        eventRider.CalculateFantasyPoints();

        // Assert
        Assert.Equal(expectedPoints, eventRider.FantasyPoints);
    }

    [Fact]
    public void CalculateFantasyPoints_HandicapAdjustment_CanImprovePosition()
    {
        // Arrange: 10th place with +5 handicap should become 5th adjusted
        var eventRider = CreateTestEventRider(
            finishPosition: 10,
            handicap: 5,
            isAllStar: false);

        // Act
        eventRider.CalculateFantasyPoints();

        // Assert
        // 5th adjusted position = 17 points, doubled = 34
        Assert.Equal(34, eventRider.FantasyPoints);
    }

    [Fact]
    public void CalculateFantasyPoints_HandicapAdjustment_CanWorsenPosition()
    {
        // Arrange: 5th place with -3 handicap should become 8th adjusted
        var eventRider = CreateTestEventRider(
            finishPosition: 5,
            handicap: -3,
            isAllStar: false);

        // Act
        eventRider.CalculateFantasyPoints();

        // Assert
        // 8th adjusted position = 14 points, doubled = 28
        Assert.Equal(28, eventRider.FantasyPoints);
    }

    [Fact]
    public void CalculateFantasyPoints_Handicap_CannotGoBelowFirst()
    {
        // Arrange: 3rd place with +10 handicap should cap at 1st
        var eventRider = CreateTestEventRider(
            finishPosition: 3,
            handicap: 10,
            isAllStar: false);

        // Act
        eventRider.CalculateFantasyPoints();

        // Assert
        // Capped at 1st adjusted = 25 * 2 = 50 points (1st place doubles for non-All-Stars!)
        Assert.Equal(50, eventRider.FantasyPoints);
    }

    [Fact]
    public void CalculateFantasyPoints_TenthPlaceCutoff_ExactlyTenth()
    {
        // Arrange: Exactly 10th adjusted position should get doubling
        var eventRider = CreateTestEventRider(
            finishPosition: 10,
            handicap: 0,
            isAllStar: false);

        // Act
        eventRider.CalculateFantasyPoints();

        // Assert
        // 10th position = 12 points, doubled = 24
        Assert.Equal(24, eventRider.FantasyPoints);
    }

    [Fact]
    public void CalculateFantasyPoints_EleventhPlace_NoDoubling()
    {
        // Arrange: 11th adjusted position should NOT get doubling
        var eventRider = CreateTestEventRider(
            finishPosition: 11,
            handicap: 0,
            isAllStar: false);

        // Act
        eventRider.CalculateFantasyPoints();

        // Assert
        // 11th position = 11 points, NOT doubled
        Assert.Equal(11, eventRider.FantasyPoints);
    }

    [Fact]
    public void CalculateFantasyPoints_AllStar_NeverDoubles()
    {
        // Arrange: All-Star finishing 1st adjusted should not double
        var eventRider = CreateTestEventRider(
            finishPosition: 1,
            handicap: 0,
            isAllStar: true);

        // Act
        eventRider.CalculateFantasyPoints();

        // Assert
        Assert.Equal(25, eventRider.FantasyPoints); // Not doubled
    }

    /// <summary>
    /// Helper method to create EventRider for testing.
    /// </summary>
    private EventRider CreateTestEventRider(
        int finishPosition,
        int handicap,
        bool isAllStar)
    {
        var eventId = Guid.NewGuid();
        var riderId = Guid.NewGuid();

        return new EventRider
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            RiderId = riderId,
            BikeClass = BikeClass.Class450,
            Handicap = handicap,
            IsAllStar = isAllStar,
            IsInjured = false,
            FinishPosition = finishPosition
        };
    }
}
