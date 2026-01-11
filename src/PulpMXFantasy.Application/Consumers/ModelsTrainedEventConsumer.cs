using MassTransit;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.Events;
using PulpMXFantasy.Contracts.ReadModels;
using PulpMXFantasy.Domain.Abstractions;

namespace PulpMXFantasy.Application.Consumers;

/// <summary>
/// Handles ModelsTrainedEvent by generating predictions for the next upcoming event.
/// </summary>
/// <remarks>
/// WHY THIS HANDLER EXISTS:
/// ========================
/// Automatic prediction generation pipeline:
/// 1. ML models are trained (manually triggered or scheduled)
/// 2. ModelsTrainedEvent is published
/// 3. This handler receives the event
/// 4. Generates predictions for the next upcoming event
/// 5. Writes predictions to read model for UI display
/// 6. Publishes PredictionsGeneratedEvent for downstream consumers
///
/// EVENT FLOW:
/// ===========
/// TrainModelsCommand
///    -> ModelTrainer trains all 4 models
///    -> Publishes ModelsTrainedEvent
///    -> THIS HANDLER CONSUMES
///    -> Publishes PredictionsGeneratedEvent
///    -> (Future: Team optimizer can consume to suggest teams)
///
/// ERROR HANDLING STRATEGY:
/// ========================
/// This handler uses defensive error handling:
/// - Log errors but don't throw (prevents message retry loops)
/// - Skip operations if prerequisites aren't met (no upcoming event)
/// - Continue gracefully if prediction service fails
///
/// This is an EVENT handler (pub/sub), not a command handler.
/// Failed events are logged but not retried since the system
/// will naturally recover when the next training occurs.
///
/// CLEAN ARCHITECTURE:
/// ===================
/// This handler lives in the Application layer and uses interfaces:
/// - IPredictionService: Generates predictions using ML models
/// - IReadModelUpdater: Writes predictions to read model database
/// - IEventRepository: Queries for upcoming events
/// - IPublishEndpoint: Publishes PredictionsGeneratedEvent (MassTransit)
///
/// Concrete implementations are in Infrastructure layer.
/// </remarks>
public class ModelsTrainedEventConsumer : IConsumer<ModelsTrainedEvent>
{
    private readonly IPredictionService _predictionService;
    private readonly IReadModelUpdater _readModelUpdater;
    private readonly IEventRepository _eventRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ModelsTrainedEventConsumer> _logger;

    /// <summary>
    /// Creates a new ModelsTrainedEventConsumer instance.
    /// </summary>
    public ModelsTrainedEventConsumer(
        IPredictionService predictionService,
        IReadModelUpdater readModelUpdater,
        IEventRepository eventRepository,
        IPublishEndpoint publishEndpoint,
        ILogger<ModelsTrainedEventConsumer> logger)
    {
        _predictionService = predictionService ?? throw new ArgumentNullException(nameof(predictionService));
        _readModelUpdater = readModelUpdater ?? throw new ArgumentNullException(nameof(readModelUpdater));
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the ModelsTrainedEvent by generating predictions for the next upcoming event.
    /// </summary>
    /// <param name="context">MassTransit consume context containing the event</param>
    /// <returns>Task representing the async operation</returns>
    public async Task Consume(ConsumeContext<ModelsTrainedEvent> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        _logger.LogInformation(
            "Received ModelsTrainedEvent: TrainedAt={TrainedAt}, ModelCount={ModelCount}, TotalSamples={TotalSamples}",
            message.TrainedAt,
            message.Models.Count,
            message.TotalTrainingSamples);

        try
        {
            // Step 1: Find the next upcoming event
            var nextEvent = await _eventRepository.GetNextUpcomingEventAsync(cancellationToken);
            if (nextEvent == null)
            {
                _logger.LogInformation("No upcoming events found. Skipping prediction generation.");
                return;
            }

            _logger.LogInformation(
                "Found next upcoming event: EventId={EventId}, Name={EventName}, Date={EventDate}, LockoutTime={LockoutTime}",
                nextEvent.Id,
                nextEvent.Name,
                nextEvent.EventDate,
                nextEvent.LockoutTime);

            // Step 1.5: Check if predictions are locked (race has started)
            if (nextEvent.LockoutTime.HasValue && nextEvent.LockoutTime.Value <= DateTimeOffset.UtcNow)
            {
                _logger.LogWarning(
                    "Predictions are locked for event {EventId} (lockout time {LockoutTime} has passed). " +
                    "Skipping prediction regeneration to protect existing predictions.",
                    nextEvent.Id,
                    nextEvent.LockoutTime.Value);
                return;
            }

            // Step 2: Invalidate prediction cache to force fresh predictions with new models
            await _predictionService.InvalidatePredictionCacheAsync(nextEvent.Id);
            _logger.LogDebug("Invalidated prediction cache for event {EventId}", nextEvent.Id);

            // Step 3: Generate predictions for the event
            var predictions = await GeneratePredictionsAsync(nextEvent.Id, cancellationToken);
            if (predictions.Count == 0)
            {
                _logger.LogWarning(
                    "No predictions generated for event {EventId}. Skipping read model update and event publication.",
                    nextEvent.Id);
                return;
            }

            _logger.LogInformation(
                "Generated {PredictionCount} predictions for event {EventId}",
                predictions.Count,
                nextEvent.Id);

            // Step 4: Get model version for read models
            var modelVersion = GetModelVersion(message);

            // Step 5: Write predictions to read model
            await WritePredictionsToReadModelAsync(nextEvent.Id, predictions, modelVersion, cancellationToken);

            // Step 6: Publish PredictionsGeneratedEvent
            await PublishPredictionsGeneratedEventAsync(
                nextEvent.Id,
                predictions.Count,
                modelVersion,
                cancellationToken);

            _logger.LogInformation(
                "Successfully processed ModelsTrainedEvent: Generated and stored {PredictionCount} predictions for event {EventId}",
                predictions.Count,
                nextEvent.Id);
        }
        catch (Exception ex)
        {
            // Log error but don't rethrow - this is an event handler, not a command handler
            // The system will naturally recover on the next training cycle
            _logger.LogError(
                ex,
                "Error processing ModelsTrainedEvent: {ErrorMessage}",
                ex.Message);
        }
    }

    /// <summary>
    /// Generates predictions for the specified event.
    /// </summary>
    private async Task<IReadOnlyList<RiderPrediction>> GeneratePredictionsAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _predictionService.GeneratePredictionsForEventAsync(eventId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to generate predictions for event {EventId}: {ErrorMessage}",
                eventId,
                ex.Message);
            return Array.Empty<RiderPrediction>();
        }
    }

    /// <summary>
    /// Extracts the model version from the ModelsTrainedEvent.
    /// </summary>
    private static string GetModelVersion(ModelsTrainedEvent message)
    {
        // Use the first model's version, or a default if no models
        return message.Models.FirstOrDefault()?.Version ?? "unknown";
    }

    /// <summary>
    /// Writes predictions to the read model database.
    /// </summary>
    private async Task WritePredictionsToReadModelAsync(
        Guid eventId,
        IReadOnlyList<RiderPrediction> predictions,
        string modelVersion,
        CancellationToken cancellationToken)
    {
        // Load rider information for denormalization
        var riderIds = predictions.Select(p => p.RiderId).ToList();
        var eventRiders = await _eventRepository.GetEventRidersWithDetailsAsync(eventId, riderIds, cancellationToken);

        var eventRiderLookup = eventRiders.ToDictionary(er => er.RiderId);

        // Map predictions to read models
        var readModels = predictions
            .Where(p => eventRiderLookup.ContainsKey(p.RiderId))
            .Select(p =>
            {
                var eventRider = eventRiderLookup[p.RiderId];
                return new EventPredictionReadModel
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    RiderId = p.RiderId,
                    RiderName = eventRider.Rider.Name,
                    RiderNumber = eventRider.Rider.Number,
                    BikeClass = p.BikeClass.ToString(),
                    IsAllStar = p.IsAllStar,
                    Handicap = eventRider.Handicap,
                    ExpectedPoints = p.ExpectedPoints,
                    PointsIfQualifies = p.PointsIfQualifies,
                    PredictedFinish = p.PredictedFinish,
                    LowerBound = p.LowerBound,
                    UpperBound = p.UpperBound,
                    Confidence = p.Confidence,
                    ModelVersion = modelVersion,
                    GeneratedAt = DateTimeOffset.UtcNow
                };
            })
            .ToList();

        if (readModels.Count > 0)
        {
            var updatedCount = await _readModelUpdater.UpdateEventPredictionsAsync(
                eventId,
                readModels,
                cancellationToken);

            _logger.LogInformation(
                "Wrote {Count} predictions to read model for event {EventId}",
                updatedCount,
                eventId);
        }
    }

    /// <summary>
    /// Publishes the PredictionsGeneratedEvent to the message bus.
    /// </summary>
    private async Task PublishPredictionsGeneratedEventAsync(
        Guid eventId,
        int predictionCount,
        string modelVersion,
        CancellationToken cancellationToken)
    {
        var predictionsGeneratedEvent = new PredictionsGeneratedEvent(
            EventId: eventId,
            GeneratedAt: DateTimeOffset.UtcNow,
            PredictionCount: predictionCount,
            ModelVersion: modelVersion);

        await _publishEndpoint.Publish(predictionsGeneratedEvent, cancellationToken);

        _logger.LogInformation(
            "Published PredictionsGeneratedEvent: EventId={EventId}, PredictionCount={PredictionCount}, ModelVersion={ModelVersion}",
            eventId,
            predictionCount,
            modelVersion);
    }
}
