using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PulpMXFantasy.Application.Consumers;
using PulpMXFantasy.Contracts.Interfaces;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.Events;
using PulpMXFantasy.Contracts.ReadModels;
using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Domain.Enums;

// Alias to avoid ambiguity with MassTransit.Event
using DomainEvent = PulpMXFantasy.Domain.Entities.Event;
using EventRider = PulpMXFantasy.Domain.Entities.EventRider;
using Rider = PulpMXFantasy.Domain.Entities.Rider;

namespace PulpMXFantasy.Application.Tests.Consumers;

/// <summary>
/// Unit tests for ModelsTrainedEventConsumer.
/// Tests the automatic prediction generation triggered by ModelsTrainedEvent.
/// </summary>
/// <remarks>
/// TDD-FIRST: These tests were written BEFORE the handler implementation.
///
/// TEST SCENARIOS:
/// 1. Handler reloads models into prediction service
/// 2. Handler finds next upcoming event without predictions
/// 3. Handler generates predictions via PredictionService
/// 4. Handler writes predictions to EventPredictionsReadModel
/// 5. Handler publishes PredictionsGeneratedEvent
/// 6. Error handling doesn't crash (logs and continues)
///
/// MOCKING STRATEGY:
/// - IPredictionService: Mocked to control prediction generation
/// - IReadModelUpdater: Mocked to verify predictions are written
/// - IEventRepository: Mocked to control event queries
/// - IPublishEndpoint: Mocked to verify event publication
/// </remarks>
public class ModelsTrainedEventConsumerTests
{
    private readonly IPredictionService _predictionService;
    private readonly IReadModelUpdater _readModelUpdater;
    private readonly IEventRepository _eventRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ModelsTrainedEventConsumer> _logger;
    private readonly ModelsTrainedEventConsumer _handler;

    public ModelsTrainedEventConsumerTests()
    {
        // Set up mocks
        _predictionService = Substitute.For<IPredictionService>();
        _readModelUpdater = Substitute.For<IReadModelUpdater>();
        _eventRepository = Substitute.For<IEventRepository>();
        _publishEndpoint = Substitute.For<IPublishEndpoint>();
        _logger = Substitute.For<ILogger<ModelsTrainedEventConsumer>>();

        // Create handler
        _handler = new ModelsTrainedEventConsumer(
            _predictionService,
            _readModelUpdater,
            _eventRepository,
            _publishEndpoint,
            _logger);
    }

    // =========================================================================
    // TEST 1: Handler calls prediction service to reload/refresh models
    // =========================================================================

    [Fact]
    public async Task Consume_WhenModelsTrainedEvent_RefreshesPredictionCache()
    {
        // Arrange
        var upcomingEvent = CreateUpcomingEvent();
        _eventRepository.GetNextUpcomingEventAsync(Arg.Any<CancellationToken>())
            .Returns(upcomingEvent);

        var modelsTrainedEvent = CreateModelsTrainedEvent();
        var context = CreateConsumeContext(modelsTrainedEvent);

        var predictions = CreateSamplePredictions();
        _predictionService.GeneratePredictionsForEventAsync(upcomingEvent.Id, Arg.Any<CancellationToken>())
            .Returns(predictions);

        SetupEventRidersLookup(upcomingEvent.Id, predictions);

        // Act
        await _handler.Consume(context);

        // Assert - Handler should invalidate cache to force fresh predictions
        await _predictionService.Received(1).InvalidatePredictionCacheAsync(upcomingEvent.Id);
    }

    // =========================================================================
    // TEST 2: Handler finds next upcoming event without predictions
    // =========================================================================

    [Fact]
    public async Task Consume_FindsNextUpcomingEvent_SkipsCompletedEvents()
    {
        // Arrange
        var upcomingEvent = CreateUpcomingEvent();
        _eventRepository.GetNextUpcomingEventAsync(Arg.Any<CancellationToken>())
            .Returns(upcomingEvent);

        var modelsTrainedEvent = CreateModelsTrainedEvent();
        var context = CreateConsumeContext(modelsTrainedEvent);

        var predictions = CreateSamplePredictions();
        _predictionService.GeneratePredictionsForEventAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(predictions);

        SetupEventRidersLookup(upcomingEvent.Id, predictions);

        // Act
        await _handler.Consume(context);

        // Assert - Should generate predictions for upcoming event
        await _predictionService.Received(1)
            .GeneratePredictionsForEventAsync(upcomingEvent.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_NoUpcomingEvents_DoesNotCrash_LogsAndSkips()
    {
        // Arrange - No events in database
        _eventRepository.GetNextUpcomingEventAsync(Arg.Any<CancellationToken>())
            .Returns((DomainEvent?)null);

        var modelsTrainedEvent = CreateModelsTrainedEvent();
        var context = CreateConsumeContext(modelsTrainedEvent);

        // Act - Should not throw
        var exception = await Record.ExceptionAsync(() => _handler.Consume(context));

        // Assert
        Assert.Null(exception);
        await _predictionService.DidNotReceive()
            .GeneratePredictionsForEventAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _publishEndpoint.DidNotReceive()
            .Publish(Arg.Any<PredictionsGeneratedEvent>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // TEST 3: Handler generates predictions via PredictionService
    // =========================================================================

    [Fact]
    public async Task Consume_GeneratesPredictionsForUpcomingEvent()
    {
        // Arrange
        var upcomingEvent = CreateUpcomingEvent();
        _eventRepository.GetNextUpcomingEventAsync(Arg.Any<CancellationToken>())
            .Returns(upcomingEvent);

        var modelsTrainedEvent = CreateModelsTrainedEvent();
        var context = CreateConsumeContext(modelsTrainedEvent);

        var predictions = CreateSamplePredictions();
        _predictionService.GeneratePredictionsForEventAsync(upcomingEvent.Id, Arg.Any<CancellationToken>())
            .Returns(predictions);

        SetupEventRidersLookup(upcomingEvent.Id, predictions);

        // Act
        await _handler.Consume(context);

        // Assert
        await _predictionService.Received(1)
            .GeneratePredictionsForEventAsync(upcomingEvent.Id, Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // TEST 4: Handler writes predictions to EventPredictionsReadModel
    // =========================================================================

    [Fact]
    public async Task Consume_WritesPredictionsToReadModel()
    {
        // Arrange
        var upcomingEvent = CreateUpcomingEvent();
        _eventRepository.GetNextUpcomingEventAsync(Arg.Any<CancellationToken>())
            .Returns(upcomingEvent);

        var modelsTrainedEvent = CreateModelsTrainedEvent();
        var context = CreateConsumeContext(modelsTrainedEvent);

        var predictions = CreateSamplePredictions();
        _predictionService.GeneratePredictionsForEventAsync(upcomingEvent.Id, Arg.Any<CancellationToken>())
            .Returns(predictions);

        SetupEventRidersLookup(upcomingEvent.Id, predictions);

        // Act
        await _handler.Consume(context);

        // Assert - Verify predictions were written to read model
        await _readModelUpdater.Received(1).UpdateEventPredictionsAsync(
            upcomingEvent.Id,
            Arg.Is<IEnumerable<EventPredictionReadModel>>(p => p.Count() == predictions.Count),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // TEST 5: Handler publishes PredictionsGeneratedEvent
    // =========================================================================

    [Fact]
    public async Task Consume_PublishesPredictionsGeneratedEvent()
    {
        // Arrange
        var upcomingEvent = CreateUpcomingEvent();
        _eventRepository.GetNextUpcomingEventAsync(Arg.Any<CancellationToken>())
            .Returns(upcomingEvent);

        var modelsTrainedEvent = CreateModelsTrainedEvent();
        var context = CreateConsumeContext(modelsTrainedEvent);

        var predictions = CreateSamplePredictions();
        _predictionService.GeneratePredictionsForEventAsync(upcomingEvent.Id, Arg.Any<CancellationToken>())
            .Returns(predictions);

        SetupEventRidersLookup(upcomingEvent.Id, predictions);

        // Act
        await _handler.Consume(context);

        // Assert
        await _publishEndpoint.Received(1).Publish(
            Arg.Is<PredictionsGeneratedEvent>(e =>
                e.EventId == upcomingEvent.Id &&
                e.PredictionCount == predictions.Count),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_IncludesModelVersionInPublishedEvent()
    {
        // Arrange
        var upcomingEvent = CreateUpcomingEvent();
        _eventRepository.GetNextUpcomingEventAsync(Arg.Any<CancellationToken>())
            .Returns(upcomingEvent);

        var modelVersion = "1.2.3";
        var modelsTrainedEvent = CreateModelsTrainedEvent(modelVersion);
        var context = CreateConsumeContext(modelsTrainedEvent);

        var predictions = CreateSamplePredictions();
        _predictionService.GeneratePredictionsForEventAsync(upcomingEvent.Id, Arg.Any<CancellationToken>())
            .Returns(predictions);

        SetupEventRidersLookup(upcomingEvent.Id, predictions);

        // Act
        await _handler.Consume(context);

        // Assert - Model version should be passed through
        await _publishEndpoint.Received(1).Publish(
            Arg.Is<PredictionsGeneratedEvent>(e => e.ModelVersion == modelVersion),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // TEST 6: Error handling doesn't crash (logs and continues)
    // =========================================================================

    [Fact]
    public async Task Consume_PredictionServiceThrows_LogsErrorAndDoesNotCrash()
    {
        // Arrange
        var upcomingEvent = CreateUpcomingEvent();
        _eventRepository.GetNextUpcomingEventAsync(Arg.Any<CancellationToken>())
            .Returns(upcomingEvent);

        var modelsTrainedEvent = CreateModelsTrainedEvent();
        var context = CreateConsumeContext(modelsTrainedEvent);

        _predictionService.GeneratePredictionsForEventAsync(upcomingEvent.Id, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("ML model failed"));

        // Act - Should not throw
        var exception = await Record.ExceptionAsync(() => _handler.Consume(context));

        // Assert
        Assert.Null(exception);
        // Event should NOT be published when prediction fails
        await _publishEndpoint.DidNotReceive()
            .Publish(Arg.Any<PredictionsGeneratedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_EmptyPredictions_DoesNotPublishEvent()
    {
        // Arrange
        var upcomingEvent = CreateUpcomingEvent();
        _eventRepository.GetNextUpcomingEventAsync(Arg.Any<CancellationToken>())
            .Returns(upcomingEvent);

        var modelsTrainedEvent = CreateModelsTrainedEvent();
        var context = CreateConsumeContext(modelsTrainedEvent);

        // Return empty predictions
        _predictionService.GeneratePredictionsForEventAsync(upcomingEvent.Id, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RiderPrediction>());

        // Act
        await _handler.Consume(context);

        // Assert - Should not publish event when no predictions generated
        await _publishEndpoint.DidNotReceive()
            .Publish(Arg.Any<PredictionsGeneratedEvent>(), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    private static DomainEvent CreateUpcomingEvent()
    {
        return new DomainEvent
        {
            Id = Guid.NewGuid(),
            SeriesId = Guid.NewGuid(),
            Slug = "anaheim-1-2025",
            Name = "Anaheim 1",
            Venue = "Angel Stadium",
            Location = "Anaheim, CA",
            EventDate = DateTimeOffset.UtcNow.AddDays(7),
            RoundNumber = 1,
            SeriesType = SeriesType.Supercross,
            EventFormat = EventFormat.Standard,
            Division = Division.West,
            IsCompleted = false
        };
    }

    private static ModelsTrainedEvent CreateModelsTrainedEvent(string modelVersion = "1.0.0")
    {
        return new ModelsTrainedEvent(
            TrainedAt: DateTimeOffset.UtcNow,
            Models: new List<ModelMetadata>
            {
                new("Class250", "FinishPosition", modelVersion, null, 0.85f, 2.5f, "/path/to/model"),
                new("Class450", "FinishPosition", modelVersion, null, 0.87f, 2.3f, "/path/to/model")
            },
            TotalTrainingSamples: 1000);
    }

    private static ConsumeContext<ModelsTrainedEvent> CreateConsumeContext(ModelsTrainedEvent message)
    {
        var context = Substitute.For<ConsumeContext<ModelsTrainedEvent>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private static IReadOnlyList<RiderPrediction> CreateSamplePredictions()
    {
        return new List<RiderPrediction>
        {
            new(Guid.NewGuid(), BikeClass.Class450, false, 45.5f, 54f, 1, 38.0f, 53.0f, 0.85f),
            new(Guid.NewGuid(), BikeClass.Class450, true, 35.0f, 42f, 3, 28.0f, 42.0f, 0.82f),
            new(Guid.NewGuid(), BikeClass.Class250, false, 40.0f, 50f, 2, 32.0f, 48.0f, 0.80f),
            new(Guid.NewGuid(), BikeClass.Class250, true, 32.0f, 40f, 4, 25.0f, 39.0f, 0.78f)
        };
    }

    private void SetupEventRidersLookup(Guid eventId, IReadOnlyList<RiderPrediction> predictions)
    {
        var eventRiders = predictions.Select(p => new EventRider
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            RiderId = p.RiderId,
            BikeClass = p.BikeClass,
            Handicap = 0,
            IsAllStar = p.IsAllStar,
            Rider = new Rider
            {
                Id = p.RiderId,
                PulpMxId = $"rider-{p.RiderId}",
                Name = $"Test Rider {p.RiderId}",
                Number = 1
            }
        }).ToList();

        _eventRepository.GetEventRidersWithDetailsAsync(
            eventId,
            Arg.Any<IEnumerable<Guid>?>(),
            Arg.Any<CancellationToken>())
            .Returns(eventRiders);
    }
}
