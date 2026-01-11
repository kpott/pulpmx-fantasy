using PulpMXFantasy.Domain.Enums;

namespace PulpMXFantasy.Domain.Entities;

/// <summary>
/// Represents a complete racing series (season) containing multiple events.
/// </summary>
/// <remarks>
/// WHY SERIES EXISTS AS A SEPARATE ENTITY:
/// ========================================
/// Series is the container for a season's worth of events and enforces the
/// consecutive pick restriction rule across events.
///
/// CONSECUTIVE PICK RESTRICTION:
/// You cannot pick the same rider in consecutive rounds of the SAME series.
/// Example:
/// - Supercross 2025, Round 5 (Anaheim 2) - Pick Jett Lawrence
/// - Supercross 2025, Round 6 (San Diego) - CANNOT pick Jett Lawrence
/// - Supercross 2025, Round 7 (Arlington) - CAN pick Jett Lawrence again
///
/// SERIES BOUNDARIES:
/// When a series ends, pick restrictions reset for the next series:
/// - Supercross 2025, Round 17 (Las Vegas) - Pick Jett Lawrence
/// - Motocross 2025, Round 1 (Hangtown) - CAN pick Jett Lawrence (new series!)
///
/// REAL-WORLD SERIES EXAMPLES:
/// - "2025 Monster Energy AMA Supercross Championship" (17 rounds, Jan-May)
/// - "2025 AMA Pro Motocross Championship" (12 rounds, May-Aug)
/// - "2025 SuperMotocross World Championship" (3 playoff rounds, Sep)
///
/// DATABASE USAGE:
/// - Query all events in a series: WHERE series_id = @SeriesId ORDER BY round_number
/// - Validate consecutive picks: Check previous event in same series
/// - Display season standings and statistics
///
/// API SYNCHRONIZATION:
/// Series data is relatively static (created once per season) but may need
/// updates for schedule changes or playoff structures.
/// </remarks>
public class Series
{
    /// <summary>
    /// Internal unique identifier for the series
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Human-readable series name displayed to users
    /// </summary>
    /// <remarks>
    /// Examples:
    /// - "2025 Monster Energy AMA Supercross Championship"
    /// - "2025 AMA Pro Motocross Championship"
    /// - "2025 SuperMotocross World Championship"
    /// </remarks>
    public required string Name { get; set; }

    /// <summary>
    /// Series type (Supercross, Motocross, or SuperMotocross)
    /// </summary>
    /// <remarks>
    /// CRITICAL for consecutive pick restrictions!
    /// Picks are restricted within the same series type only.
    ///
    /// Example: Can't pick same rider in consecutive Supercross rounds,
    /// but CAN pick them in next Motocross round (different series).
    /// </remarks>
    public required SeriesType SeriesType { get; set; }

    /// <summary>
    /// Calendar year the series takes place
    /// </summary>
    /// <remarks>
    /// Used for:
    /// - Displaying "current season" vs historical data
    /// - Filtering events for training ML models
    /// - Archiving old series data
    ///
    /// Example: 2025 for the 2025 racing season
    /// </remarks>
    public required int Year { get; set; }

    /// <summary>
    /// First event date in the series
    /// </summary>
    /// <remarks>
    /// Example: January 11, 2025 for Anaheim 1 (Supercross season opener)
    /// </remarks>
    public required DateTimeOffset StartDate { get; set; }

    /// <summary>
    /// Last event date in the series
    /// </summary>
    /// <remarks>
    /// May be tentative if later rounds aren't fully scheduled yet.
    /// Example: May 10, 2025 for Las Vegas (Supercross finale)
    /// </remarks>
    public DateTimeOffset? EndDate { get; set; }

    /// <summary>
    /// Whether this series is currently active (ongoing races)
    /// </summary>
    /// <remarks>
    /// Used for:
    /// - Highlighting "current season" in UI
    /// - Filtering which series to generate predictions for
    /// - Determining which series to sync data for
    ///
    /// Typically only one series of each type is active at a time.
    /// Example: "2025 Supercross" is active Jan-May, then "2025 Motocross" May-Aug
    /// </remarks>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Timestamp when this series record was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp of the last update to this series record
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Navigation property to all events in this series
    /// </summary>
    /// <remarks>
    /// ONE series -> MANY events (typically 12-17 events per series)
    /// Used for:
    /// - Displaying series schedule
    /// - Validating consecutive pick restrictions
    /// - Calculating series standings
    /// </remarks>
    public ICollection<Event> Events { get; init; } = new List<Event>();

    /// <summary>
    /// Gets the next upcoming event in this series (not yet completed).
    /// </summary>
    /// <returns>Next event, or null if series is completed</returns>
    public Event? GetNextEvent()
    {
        return Events
            .Where(e => !e.IsCompleted && e.EventDate >= DateTimeOffset.UtcNow)
            .OrderBy(e => e.EventDate)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets the previous event in this series (most recent completed event).
    /// </summary>
    /// <returns>Previous event, or null if no events completed yet</returns>
    public Event? GetPreviousEvent()
    {
        return Events
            .Where(e => e.IsCompleted)
            .OrderByDescending(e => e.EventDate)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets all events ordered by round number for chronological display.
    /// </summary>
    /// <returns>Events in round order (1, 2, 3, ...)</returns>
    public IEnumerable<Event> GetEventsInOrder()
    {
        return Events.OrderBy(e => e.RoundNumber);
    }
}
