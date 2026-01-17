using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Domain.Enums;

namespace PulpMXFantasy.Domain.Entities;

/// <summary>
/// Represents a single racing event (round) in a series.
/// </summary>
/// <remarks>
/// This entity captures all metadata about a specific race event.
///
/// KEY DESIGN DECISIONS:
/// 1. Event is the container for all event-specific data (riders, results, predictions)
/// 2. SeriesType determines consecutive pick restrictions (can't pick same rider in SX rounds N and N+1)
/// 3. EventFormat is CRITICAL - determines scoring logic branching:
///    - Standard: Single main event → fantasy points
///    - TripleCrown: 3 races → overall position → fantasy points
///    - Motocross: 2 motos → fantasy points PER MOTO (summed)
/// 4. Division is CRITICAL for 250 class - filters which riders are available
///
/// EXAMPLE EVENTS:
/// - Anaheim 1 (Supercross, Standard, West 250)
/// - Daytona (Supercross, TripleCrown, East 250)
/// - Hangtown (Motocross, Motocross, Combined 250)
/// - SuperMotocross Round 1 (SuperMotocross, Standard, Combined)
///
/// API SYNCHRONIZATION:
/// - Event data fetched from PulpMX API /v2/events endpoints
/// - Slug is the stable identifier from API
///
/// DATABASE MAPPING:
/// - Primary key: Id (UUID)
/// - Unique constraint on Slug (external API reference)
/// - Indexed on EventDate for chronological queries
/// </remarks>
public class Event : IHasTimestamps
{
    /// <summary>
    /// Internal unique identifier for the event
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Foreign key to the series this event belongs to
    /// </summary>
    public required Guid SeriesId { get; init; }

    /// <summary>
    /// External PulpMX API slug identifier for this event
    /// </summary>
    /// <remarks>
    /// Examples: "anaheim-1-2025", "daytona-2025", "hangtown-2025"
    /// Used as the stable reference when calling PulpMX API endpoints.
    /// Must be unique across all events.
    /// </remarks>
    public required string Slug { get; init; }

    /// <summary>
    /// Human-readable event name displayed to users
    /// </summary>
    /// <remarks>
    /// Examples: "Anaheim 1", "Daytona Supercross", "Hangtown National"
    /// </remarks>
    public required string Name { get; set; }

    /// <summary>
    /// Racing venue/stadium name
    /// </summary>
    /// <remarks>
    /// Examples: "Angel Stadium", "Daytona International Speedway", "Hangtown MX Park"
    /// </remarks>
    public required string Venue { get; set; }

    /// <summary>
    /// City and state where the event takes place
    /// </summary>
    /// <remarks>
    /// Examples: "Anaheim, CA", "Daytona Beach, FL", "Sacramento, CA"
    /// </remarks>
    public required string Location { get; set; }

    /// <summary>
    /// Date when the event takes place
    /// </summary>
    /// <remarks>
    /// Used for:
    /// - Determining "next event" for predictions
    /// - Chronological sorting of events
    /// - Consecutive pick restriction validation (events N and N+1)
    /// </remarks>
    public required DateTimeOffset EventDate { get; set; }

    /// <summary>
    /// Round number within the series (1-based)
    /// </summary>
    /// <remarks>
    /// Examples: 1 for "Anaheim 1", 17 for "Las Vegas Finale"
    /// Used for consecutive pick restrictions and season progression tracking.
    /// </remarks>
    public required int RoundNumber { get; set; }

    /// <summary>
    /// Series type this event belongs to
    /// </summary>
    /// <remarks>
    /// CRITICAL for consecutive pick restrictions!
    /// You can't pick the same rider in consecutive rounds of the SAME series.
    /// Supercross round 17 → Motocross round 1 = different series, picks reset.
    /// </remarks>
    public required SeriesType SeriesType { get; set; }

    /// <summary>
    /// Event format determining scoring logic
    /// </summary>
    /// <remarks>
    /// CRITICAL - System will fail without handling all formats!
    ///
    /// Standard: Most common, single main event
    /// TripleCrown: 3 shorter races, scored by overall position across all 3
    /// Motocross: 2 motos, fantasy points awarded PER MOTO and summed
    /// SuperMotocross: Playoff format with special rules
    ///
    /// Scoring logic MUST branch based on this value!
    /// ~40% of events are NOT standard format.
    /// </remarks>
    public required EventFormat EventFormat { get; set; }

    /// <summary>
    /// Division for 250 class filtering (East/West/Combined)
    /// </summary>
    /// <remarks>
    /// CRITICAL for 250 class rider availability!
    ///
    /// In Supercross, 250 class splits into East and West divisions:
    /// - East riders only race at eastern venues
    /// - West riders only race at western venues
    /// - "Showdown" events have Combined (both divisions race)
    ///
    /// Database queries MUST filter:
    /// WHERE (division = @EventDivision OR division = 'Combined')
    ///
    /// 450 class always uses Combined (no division split).
    ///
    /// Example: Anaheim 1 is West, so only show West + Combined riders.
    /// </remarks>
    public required Division Division { get; set; }

    /// <summary>
    /// Fantasy pick lockout time from PulpMX API.
    /// </summary>
    /// <remarks>
    /// After this time, fantasy picks are locked and predictions should NOT be regenerated.
    /// This protects existing predictions from being overwritten after the race starts.
    /// Populated from NextEventInfo.Lockout (Unix epoch seconds) during API sync.
    /// </remarks>
    public DateTimeOffset? LockoutTime { get; set; }

    /// <summary>
    /// Whether the event has already occurred
    /// </summary>
    /// <remarks>
    /// Used to determine if we should:
    /// - Generate predictions (IsCompleted = false)
    /// - Display results and accuracy metrics (IsCompleted = true)
    /// - Train ML models (only use completed events)
    /// </remarks>
    public bool IsCompleted { get; set; } = false;

    /// <summary>
    /// Timestamp when this event record was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp of the last update to this event record
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Navigation property to the series this event belongs to
    /// </summary>
    public Series Series { get; init; } = null!;

    /// <summary>
    /// Navigation property to all riders participating in this event
    /// </summary>
    /// <remarks>
    /// ONE event -> MANY event riders (typically 40-80 riders across 250 and 450 classes)
    /// Used for querying entry lists, generating predictions, calculating results.
    /// </remarks>
    public ICollection<EventRider> EventRiders { get; init; } = new List<EventRider>();
}
