using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.ReadModels;
using PulpMXFantasy.ReadModel;

namespace PulpMXFantasy.Infrastructure.Services;

/// <summary>
/// Service for updating read models in the CQRS architecture.
/// </summary>
/// <remarks>
/// WHY THIS SERVICE EXISTS:
/// ========================
/// In CQRS, read models are projections of domain events.
/// This service handles the "projection" side of event processing:
/// 1. Receive domain events (EventSynced, ModelsTrainedEvent, etc.)
/// 2. Update denormalized read models accordingly
/// 3. Maintain consistency between write and read sides
///
/// READ MODEL UPDATE PATTERNS:
/// ===========================
///
/// 1. **Event Predictions**
///    - Updated after PredictionsGeneratedEvent
///    - Upsert pattern: Replace existing or insert new
///    - Bulk update for efficiency
///
/// 2. **Model Metadata**
///    - Updated after ModelsTrainedEvent
///    - Deactivate old models, activate new
///    - Track training history
///
/// EVENTUAL CONSISTENCY:
/// =====================
/// Read models may be slightly behind write models (milliseconds).
/// This is acceptable in CQRS - eventual consistency is the tradeoff
/// for query performance and scalability.
///
/// ERROR HANDLING:
/// ===============
/// - Failed updates logged but don't throw (eventual consistency)
/// - Retry logic handled by message broker (MassTransit)
/// - Dead letter queue for persistent failures
///
/// THREAD SAFETY:
/// ==============
/// This service should be registered as Scoped (one per request/message).
/// Each message handler gets its own DbContext and ReadModelUpdater instance.
///
/// USAGE EXAMPLE:
/// ==============
/// <code>
/// // In event consumer
/// public async Task Consume(ConsumeContext&lt;PredictionsGeneratedEvent&gt; context)
/// {
///     var predictions = context.Message.Predictions.Select(p => new EventPredictionReadModel
///     {
///         // Map from event to read model
///     }).ToList();
///
///     await _readModelUpdater.UpdateEventPredictionsAsync(
///         context.Message.EventId,
///         predictions);
/// }
/// </code>
/// </remarks>
public class ReadModelUpdater : IReadModelUpdater
{
    private readonly ReadDbContext _readDbContext;
    private readonly ILogger<ReadModelUpdater> _logger;

    /// <summary>
    /// Creates a new ReadModelUpdater instance.
    /// </summary>
    /// <param name="readDbContext">Read model database context</param>
    /// <param name="logger">Logger instance</param>
    public ReadModelUpdater(ReadDbContext readDbContext, ILogger<ReadModelUpdater> logger)
    {
        _readDbContext = readDbContext ?? throw new ArgumentNullException(nameof(readDbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ============================================================================
    // EVENT PREDICTIONS
    // ============================================================================

    /// <summary>
    /// Updates event predictions for a specific event.
    /// Uses upsert pattern: existing predictions are replaced, new ones are inserted.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="predictions">New predictions to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of predictions updated</returns>
    public async Task<int> UpdateEventPredictionsAsync(
        Guid eventId,
        IEnumerable<EventPredictionReadModel> predictions,
        CancellationToken cancellationToken = default)
    {
        var predictionsList = predictions.ToList();

        if (predictionsList.Count == 0)
        {
            _logger.LogWarning("No predictions provided for event: EventId={EventId}", eventId);
            return 0;
        }

        try
        {
            // Remove existing predictions for this event
            var existingPredictions = await _readDbContext.EventPredictions
                .Where(p => p.EventId == eventId)
                .ToListAsync(cancellationToken);

            if (existingPredictions.Count > 0)
            {
                _readDbContext.EventPredictions.RemoveRange(existingPredictions);
                _logger.LogDebug(
                    "Removed {Count} existing predictions for event: EventId={EventId}",
                    existingPredictions.Count, eventId);
            }

            // Add new predictions
            _readDbContext.EventPredictions.AddRange(predictionsList);
            await _readDbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated event predictions: EventId={EventId}, Count={Count}",
                eventId, predictionsList.Count);

            return predictionsList.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update event predictions: EventId={EventId}, Error={Error}",
                eventId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Gets all predictions for an event.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="bikeClass">Optional bike class filter (Class250 or Class450)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of predictions ordered by expected points descending</returns>
    public async Task<List<EventPredictionReadModel>> GetEventPredictionsAsync(
        Guid eventId,
        string? bikeClass = null,
        CancellationToken cancellationToken = default)
    {
        var query = _readDbContext.EventPredictions
            .AsNoTracking()
            .Where(p => p.EventId == eventId);

        if (!string.IsNullOrEmpty(bikeClass))
        {
            query = query.Where(p => p.BikeClass == bikeClass);
        }

        return await query
            .OrderByDescending(p => p.ExpectedPoints)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes all predictions for an event.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of predictions deleted</returns>
    public async Task<int> DeleteEventPredictionsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var predictions = await _readDbContext.EventPredictions
            .Where(p => p.EventId == eventId)
            .ToListAsync(cancellationToken);

        if (predictions.Count == 0)
        {
            return 0;
        }

        _readDbContext.EventPredictions.RemoveRange(predictions);
        await _readDbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Deleted event predictions: EventId={EventId}, Count={Count}",
            eventId, predictions.Count);

        return predictions.Count;
    }

    // ============================================================================
    // MODEL METADATA
    // ============================================================================

    /// <summary>
    /// Updates model metadata after training.
    /// Deactivates previous models of the same type and activates the new one.
    /// </summary>
    /// <param name="metadata">New model metadata to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if update succeeded</returns>
    public async Task<bool> UpdateModelMetadataAsync(
        ModelMetadataReadModel metadata,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Deactivate previous models of the same type and class
            var previousModels = await _readDbContext.ModelMetadata
                .Where(m => m.BikeClass == metadata.BikeClass
                         && m.ModelType == metadata.ModelType
                         && m.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var model in previousModels)
            {
                var deactivated = model with { IsActive = false };
                _readDbContext.Entry(model).CurrentValues.SetValues(deactivated);
            }

            // Check if model with same version exists (upsert pattern)
            var existingModel = await _readDbContext.ModelMetadata
                .FirstOrDefaultAsync(m => m.BikeClass == metadata.BikeClass
                                       && m.ModelType == metadata.ModelType
                                       && m.Version == metadata.Version,
                    cancellationToken);

            if (existingModel != null)
            {
                // Update existing model - set properties individually (can't change Id)
                var entry = _readDbContext.Entry(existingModel);
                entry.Property(m => m.TrainedAt).CurrentValue = metadata.TrainedAt;
                entry.Property(m => m.TrainingSamples).CurrentValue = metadata.TrainingSamples;
                entry.Property(m => m.ValidationAccuracy).CurrentValue = metadata.ValidationAccuracy;
                entry.Property(m => m.RSquared).CurrentValue = metadata.RSquared;
                entry.Property(m => m.MeanAbsoluteError).CurrentValue = metadata.MeanAbsoluteError;
                entry.Property(m => m.ModelPath).CurrentValue = metadata.ModelPath;
                entry.Property(m => m.IsActive).CurrentValue = metadata.IsActive;
            }
            else
            {
                // Add new model metadata
                _readDbContext.ModelMetadata.Add(metadata);
            }

            await _readDbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated model metadata: BikeClass={BikeClass}, ModelType={ModelType}, Version={Version}",
                metadata.BikeClass, metadata.ModelType, metadata.Version);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update model metadata: BikeClass={BikeClass}, ModelType={ModelType}, Error={Error}",
                metadata.BikeClass, metadata.ModelType, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Gets the active model metadata for a specific bike class and model type.
    /// </summary>
    /// <param name="bikeClass">Bike class (Class250 or Class450)</param>
    /// <param name="modelType">Model type (Qualification or FinishPosition)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Active model metadata or null if not found</returns>
    public async Task<ModelMetadataReadModel?> GetActiveModelAsync(
        string bikeClass,
        string modelType,
        CancellationToken cancellationToken = default)
    {
        return await _readDbContext.ModelMetadata
            .AsNoTracking()
            .Where(m => m.BikeClass == bikeClass
                     && m.ModelType == modelType
                     && m.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Gets all model metadata, optionally filtered and ordered.
    /// </summary>
    /// <param name="bikeClass">Optional bike class filter</param>
    /// <param name="modelType">Optional model type filter</param>
    /// <param name="activeOnly">If true, only return active models</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of model metadata ordered by trained date descending</returns>
    public async Task<List<ModelMetadataReadModel>> GetAllModelsAsync(
        string? bikeClass = null,
        string? modelType = null,
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _readDbContext.ModelMetadata.AsNoTracking();

        if (!string.IsNullOrEmpty(bikeClass))
        {
            query = query.Where(m => m.BikeClass == bikeClass);
        }

        if (!string.IsNullOrEmpty(modelType))
        {
            query = query.Where(m => m.ModelType == modelType);
        }

        if (activeOnly)
        {
            query = query.Where(m => m.IsActive);
        }

        return await query
            .OrderByDescending(m => m.TrainedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Gets model metadata by ID.
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Model metadata or null if not found</returns>
    public async Task<ModelMetadataReadModel?> GetModelByIdAsync(
        Guid modelId,
        CancellationToken cancellationToken = default)
    {
        return await _readDbContext.ModelMetadata
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == modelId, cancellationToken);
    }

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
    public async Task<bool> UpdateEventAsync(
        EventReadModel eventReadModel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if event already exists
            var existingEvent = await _readDbContext.Events
                .FirstOrDefaultAsync(e => e.Id == eventReadModel.Id, cancellationToken);

            if (existingEvent != null)
            {
                // Update existing event
                _readDbContext.Entry(existingEvent).CurrentValues.SetValues(eventReadModel);
                _logger.LogDebug(
                    "Updating existing event in read model: EventId={EventId}, Name={Name}",
                    eventReadModel.Id, eventReadModel.Name);
            }
            else
            {
                // Insert new event
                _readDbContext.Events.Add(eventReadModel);
                _logger.LogDebug(
                    "Adding new event to read model: EventId={EventId}, Name={Name}",
                    eventReadModel.Id, eventReadModel.Name);
            }

            await _readDbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Event read model updated: EventId={EventId}, Name={Name}",
                eventReadModel.Id, eventReadModel.Name);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update event read model: EventId={EventId}, Error={Error}",
                eventReadModel.Id, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Gets an event by ID from the read model.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Event read model or null if not found</returns>
    public async Task<EventReadModel?> GetEventByIdAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        return await _readDbContext.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
    }

    /// <summary>
    /// Gets the next upcoming event from the read model.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Next event or null if no upcoming events</returns>
    public async Task<EventReadModel?> GetNextUpcomingEventAsync(
        CancellationToken cancellationToken = default)
    {
        return await _readDbContext.Events
            .AsNoTracking()
            .Where(e => !e.IsCompleted && e.EventDate >= DateTimeOffset.UtcNow)
            .OrderBy(e => e.EventDate)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
