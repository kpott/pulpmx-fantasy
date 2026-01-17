namespace PulpMXFantasy.Domain.Abstractions;

/// <summary>
/// Interface for entities that track creation and modification timestamps.
/// </summary>
/// <remarks>
/// Implemented by entities that need automatic timestamp updates:
/// - Series, Event, Rider, EventRider, Team
///
/// NOT implemented by TeamSelection (has only CreatedAt, no UpdatedAt).
///
/// ApplicationDbContext.SaveChangesAsync() uses this interface to automatically
/// update the UpdatedAt property when entities are modified.
/// </remarks>
public interface IHasTimestamps
{
    /// <summary>
    /// Timestamp when the entity was created.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Timestamp of the last modification.
    /// Automatically updated by ApplicationDbContext.SaveChangesAsync().
    /// </summary>
    DateTimeOffset UpdatedAt { get; set; }
}
