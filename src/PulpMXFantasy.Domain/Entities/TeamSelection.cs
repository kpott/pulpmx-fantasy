namespace PulpMXFantasy.Domain.Entities;

/// <summary>
/// Represents a single rider selection on a fantasy team.
/// </summary>
/// <remarks>
/// WHY TEAMSELECTION EXISTS:
/// =========================
/// This is the join entity between Team and EventRider, answering:
/// "Which 8 riders did the user pick for their team?"
///
/// RELATIONSHIP STRUCTURE:
/// Team (1) ←→ (8) TeamSelection (8) ←→ (1) EventRider
///
/// - ONE team has EXACTLY 8 team selections (4 from 250, 4 from 450)
/// - ONE team selection links to ONE event rider
/// - ONE event rider can be selected by MANY teams (multiple users pick same rider)
///
/// TEAM SELECTION EXAMPLE:
/// User creates team for Anaheim 1:
/// - TeamSelection 1: Haiden Deegan (250, All-Star)
/// - TeamSelection 2: Tom Vialle (250)
/// - TeamSelection 3: Chance Hymas (250)
/// - TeamSelection 4: RJ Hampshire (250)
/// - TeamSelection 5: Eli Tomac (450, All-Star)
/// - TeamSelection 6: Chase Sexton (450)
/// - TeamSelection 7: Jason Anderson (450)
/// - TeamSelection 8: Justin Barcia (450)
///
/// DATA FLOW:
/// 1. ML optimizer predicts expected fantasy points for all EventRiders
/// 2. Constraint programming selects optimal 8 EventRiders
/// 3. Create Team with 8 TeamSelection records pointing to chosen EventRiders
/// 4. After race: EventRider.FantasyPoints calculated
/// 5. Team.TotalPoints = Sum(TeamSelections.FantasyPoints)
///
/// ANALYTICS USAGE:
/// - Track which riders are most commonly selected (pick percentage)
/// - Compare ML-optimized picks vs human selections
/// - Identify "sleeper picks" (low ownership, high points)
/// </remarks>
public class TeamSelection
{
    /// <summary>
    /// Internal unique identifier for this team selection
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Foreign key to the team this selection belongs to
    /// </summary>
    public required Guid TeamId { get; init; }

    /// <summary>
    /// Foreign key to the event rider being selected
    /// </summary>
    public required Guid EventRiderId { get; init; }

    /// <summary>
    /// Position/slot on the team (1-8)
    /// </summary>
    /// <remarks>
    /// Optional field for ordering riders in UI display.
    /// Could be used for position-based strategies in future
    /// (e.g., "captain" rider gets bonus points).
    ///
    /// Typical ordering:
    /// - Slots 1-4: 250 class riders
    /// - Slots 5-8: 450 class riders
    /// </remarks>
    public int? SelectionOrder { get; set; }

    /// <summary>
    /// Timestamp when this selection was made
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Navigation property to the team this selection belongs to
    /// </summary>
    public Team Team { get; init; } = null!;

    /// <summary>
    /// Navigation property to the event rider being selected
    /// </summary>
    /// <remarks>
    /// This is where we get all the important data:
    /// - EventRider.Rider.Name - Display rider name
    /// - EventRider.Handicap - Show handicap value
    /// - EventRider.IsAllStar - Mark All-Stars in UI
    /// - EventRider.FantasyPoints - Get points after race
    /// - EventRider.BikeClass - Group by 250/450
    /// </remarks>
    public EventRider EventRider { get; init; } = null!;

    /// <summary>
    /// Gets a display string for this selection.
    /// </summary>
    /// <returns>Human-readable selection summary</returns>
    public string GetDisplayString()
    {
        var rider = EventRider.Rider;
        var allStarBadge = EventRider.IsAllStar ? " ⭐" : "";
        var handicapText = EventRider.Handicap switch
        {
            > 0 => $" (+{EventRider.Handicap})",
            < 0 => $" ({EventRider.Handicap})",
            _ => ""
        };

        var pointsText = EventRider.FantasyPoints.HasValue
            ? $" - {EventRider.FantasyPoints} pts"
            : "";

        return $"#{rider.Number} {rider.Name}{allStarBadge}{handicapText}{pointsText}";
    }
}
