using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.Application.Interfaces;

/// <summary>
/// Service for updating read models in the CQRS architecture.
/// </summary>
/// <remarks>
/// WHY THIS INTERFACE EXISTS:
/// ==========================
/// Following Clean Architecture / Dependency Inversion Principle:
/// - Application layer defines WHAT it needs (update read models)
/// - Infrastructure layer implements HOW (using EF Core with ReadDbContext)
/// - Event handlers can use this abstraction without depending on EF Core directly
///
/// BENEFITS:
/// 1. **Testability** - Mock read model updates in unit tests
/// 2. **Flexibility** - Swap implementation without changing application code
/// 3. **Separation** - Application layer doesn't depend on Infrastructure
///
/// USAGE IN EVENT HANDLERS:
/// ========================
/// <code>
/// public async Task Consume(ConsumeContext&lt;ModelsTrainedEvent&gt; context)
/// {
///     var predictions = await _predictionService.GeneratePredictionsForEventAsync(eventId);
///     var readModels = MapToReadModels(predictions);
///     await _readModelUpdater.UpdateEventPredictionsAsync(eventId, readModels);
/// }
/// </code>
/// </remarks>
public interface IReadModelUpdater
{
    /// <summary>
    /// Updates event predictions for a specific event.
    /// Uses upsert pattern: existing predictions are replaced, new ones are inserted.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="predictions">New predictions to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of predictions updated</returns>
    Task<int> UpdateEventPredictionsAsync(
        Guid eventId,
        IEnumerable<EventPredictionReadModel> predictions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all predictions for an event.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="bikeClass">Optional bike class filter (Class250 or Class450)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of predictions ordered by expected points descending</returns>
    Task<List<EventPredictionReadModel>> GetEventPredictionsAsync(
        Guid eventId,
        string? bikeClass = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all predictions for an event.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of predictions deleted</returns>
    Task<int> DeleteEventPredictionsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates model metadata after training.
    /// Deactivates previous models of the same type and activates the new one.
    /// </summary>
    /// <param name="metadata">New model metadata to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if update succeeded</returns>
    Task<bool> UpdateModelMetadataAsync(
        ModelMetadataReadModel metadata,
        CancellationToken cancellationToken = default);

    // ============================================================================
    // EVENT READ MODEL
    // ============================================================================

    /// <summary>
    /// Updates event read model data (upsert pattern).
    /// Called by Worker when events are synced from PulpMX API.
    /// </summary>
    /// <param name="eventReadModel">Event data to upsert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if update succeeded</returns>
    Task<bool> UpdateEventAsync(
        EventReadModel eventReadModel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an event by ID from the read model.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Event read model or null if not found</returns>
    Task<EventReadModel?> GetEventByIdAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next upcoming event from the read model.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Next event or null if no upcoming events</returns>
    Task<EventReadModel?> GetNextUpcomingEventAsync(
        CancellationToken cancellationToken = default);
}
