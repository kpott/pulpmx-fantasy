using PulpMXFantasy.Domain.Abstractions;

namespace PulpMXFantasy.Domain.Entities;

/// <summary>
/// Represents a professional supercross/motocross rider in the fantasy system.
/// </summary>
/// <remarks>
/// This is the master rider entity that persists across all events and seasons.
///
/// KEY DESIGN DECISIONS:
/// 1. Rider is separate from EventRider (one rider, many event participations)
/// 2. Rider data is relatively static (name, number rarely change mid-season)
/// 3. Event-specific data (handicap, results) lives in EventRider entity
/// 4. API synchronization updates this table from PulpMX API
///
/// EXAMPLE USAGE:
/// - Query all riders to populate selection UI
/// - Link to EventRider records for event-specific stats
/// - Track rider history across multiple seasons
///
/// DATABASE MAPPING:
/// - Primary key: Id (UUID)
/// - Unique constraint on PulpMxId (external API reference)
/// </remarks>
public class Rider : IHasTimestamps
{
    /// <summary>
    /// Internal unique identifier for the rider
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// External PulpMX API identifier for this rider
    /// </summary>
    /// <remarks>
    /// Used to synchronize data from the PulpMX Fantasy API.
    /// This is the stable reference across API calls and seasons.
    /// Example: "chase-sexton-2025" or similar slug format
    /// </remarks>
    public required string PulpMxId { get; init; }

    /// <summary>
    /// Rider's full name as displayed to users
    /// </summary>
    /// <remarks>
    /// Examples: "Chase Sexton", "Eli Tomac", "Jett Lawrence"
    /// </remarks>
    public required string Name { get; set; }

    /// <summary>
    /// Rider's racing number (typically their permanent career number)
    /// </summary>
    /// <remarks>
    /// Examples: 1 (often the defending champion), 3, 7, 23, etc.
    /// May change between seasons but usually stays consistent.
    /// Important for user recognition and display.
    /// </remarks>
    public required int Number { get; set; }

    /// <summary>
    /// URL to the rider's photo for display in the UI
    /// </summary>
    /// <remarks>
    /// Typically sourced from PulpMX API or a CDN.
    /// Optional field - may be null for new riders without photos.
    /// </remarks>
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Timestamp when this rider record was created in our database
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp of the last update to this rider record
    /// </summary>
    /// <remarks>
    /// Updated when API sync detects changes to name, number, or photo.
    /// Useful for tracking data freshness and debugging sync issues.
    /// </remarks>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Navigation property to all event participations for this rider
    /// </summary>
    /// <remarks>
    /// ONE rider -> MANY event participations
    /// Used for querying a rider's complete event history, results, and predictions.
    /// </remarks>
    public ICollection<EventRider> EventRiders { get; init; } = new List<EventRider>();
}
