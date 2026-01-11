using PulpMXFantasy.Domain.Entities;

namespace PulpMXFantasy.Application.Interfaces;

/// <summary>
/// Repository interface for Event entity queries.
/// </summary>
/// <remarks>
/// WHY THIS INTERFACE EXISTS:
/// ==========================
/// Following Clean Architecture / Dependency Inversion Principle:
/// - Application layer defines WHAT it needs (query events)
/// - Infrastructure layer implements HOW (using EF Core with ApplicationDbContext)
///
/// FOCUSED SCOPE:
/// ==============
/// This interface provides only the queries needed by application-layer handlers.
/// Full CRUD operations remain in the DbContext (write operations).
/// This follows the CQRS pattern where reads and writes are separated.
///
/// USAGE IN EVENT HANDLERS:
/// ========================
/// <code>
/// public async Task Consume(ConsumeContext&lt;ModelsTrainedEvent&gt; context)
/// {
///     var nextEvent = await _eventRepository.GetNextUpcomingEventAsync();
///     if (nextEvent != null)
///     {
///         await GeneratePredictionsForEvent(nextEvent.Id);
///     }
/// }
/// </code>
/// </remarks>
public interface IEventRepository
{
    /// <summary>
    /// Gets the next upcoming event that hasn't been completed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Next upcoming event, or null if no upcoming events exist</returns>
    Task<Event?> GetNextUpcomingEventAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an event by its ID with rider information loaded.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Event with riders, or null if not found</returns>
    Task<Event?> GetEventWithRidersAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets event riders for a specific event with rider details.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="riderIds">Optional filter for specific rider IDs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of event riders with rider details loaded</returns>
    Task<List<EventRider>> GetEventRidersWithDetailsAsync(
        Guid eventId,
        IEnumerable<Guid>? riderIds = null,
        CancellationToken cancellationToken = default);
}
