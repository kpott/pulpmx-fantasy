using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PulpMXFantasy.Contracts.ReadModels;
using PulpMXFantasy.Infrastructure.Services;
using PulpMXFantasy.ReadModel;

namespace PulpMXFantasy.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for ReadModelUpdater service.
/// </summary>
/// <remarks>
/// Uses EF Core InMemory provider for fast, isolated tests.
/// Each test gets a fresh database to avoid cross-test contamination.
/// </remarks>
public class ReadModelUpdaterTests : IDisposable
{
    private readonly ReadDbContext _readDbContext;
    private readonly ILogger<ReadModelUpdater> _logger;
    private readonly ReadModelUpdater _updater;

    public ReadModelUpdaterTests()
    {
        // Create unique database name for test isolation
        var options = new DbContextOptionsBuilder<ReadDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _readDbContext = new ReadDbContext(options);
        _logger = Substitute.For<ILogger<ReadModelUpdater>>();
        _updater = new ReadModelUpdater(_readDbContext, _logger);
    }

    public void Dispose()
    {
        _readDbContext.Dispose();
    }

    // ============================================================================
    // EVENT PREDICTIONS TESTS
    // ============================================================================

    [Fact]
    public async Task UpdateEventPredictionsAsync_WithEmptyList_ReturnsZero()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var predictions = Enumerable.Empty<EventPredictionReadModel>();

        // Act
        var result = await _updater.UpdateEventPredictionsAsync(eventId, predictions);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task UpdateEventPredictionsAsync_WithValidPredictions_InsertsRecords()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var predictions = CreateSamplePredictions(eventId, count: 3);

        // Act
        var result = await _updater.UpdateEventPredictionsAsync(eventId, predictions);

        // Assert
        Assert.Equal(3, result);
        var stored = await _readDbContext.EventPredictions.Where(p => p.EventId == eventId).ToListAsync();
        Assert.Equal(3, stored.Count);
    }

    [Fact]
    public async Task UpdateEventPredictionsAsync_RemovesExistingAndAddsNew()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var oldPredictions = CreateSamplePredictions(eventId, count: 2);
        await _updater.UpdateEventPredictionsAsync(eventId, oldPredictions);

        var newPredictions = CreateSamplePredictions(eventId, count: 5);

        // Act
        var result = await _updater.UpdateEventPredictionsAsync(eventId, newPredictions);

        // Assert
        Assert.Equal(5, result);
        var stored = await _readDbContext.EventPredictions.Where(p => p.EventId == eventId).ToListAsync();
        Assert.Equal(5, stored.Count);
    }

    [Fact]
    public async Task GetEventPredictionsAsync_ReturnsAllForEvent()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var otherEventId = Guid.NewGuid();
        await _updater.UpdateEventPredictionsAsync(eventId, CreateSamplePredictions(eventId, count: 3));
        await _updater.UpdateEventPredictionsAsync(otherEventId, CreateSamplePredictions(otherEventId, count: 2));

        // Act
        var result = await _updater.GetEventPredictionsAsync(eventId);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.All(result, p => Assert.Equal(eventId, p.EventId));
    }

    [Fact]
    public async Task GetEventPredictionsAsync_FiltersByBikeClass()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var predictions = new List<EventPredictionReadModel>
        {
            CreatePrediction(eventId, bikeClass: "Class450"),
            CreatePrediction(eventId, bikeClass: "Class450"),
            CreatePrediction(eventId, bikeClass: "Class250"),
        };
        await _updater.UpdateEventPredictionsAsync(eventId, predictions);

        // Act
        var result = await _updater.GetEventPredictionsAsync(eventId, bikeClass: "Class450");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.Equal("Class450", p.BikeClass));
    }

    [Fact]
    public async Task GetEventPredictionsAsync_ReturnsOrderedByExpectedPointsDescending()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var predictions = new List<EventPredictionReadModel>
        {
            CreatePrediction(eventId, expectedPoints: 30f),
            CreatePrediction(eventId, expectedPoints: 50f),
            CreatePrediction(eventId, expectedPoints: 40f),
        };
        await _updater.UpdateEventPredictionsAsync(eventId, predictions);

        // Act
        var result = await _updater.GetEventPredictionsAsync(eventId);

        // Assert
        Assert.Equal(50f, result[0].ExpectedPoints);
        Assert.Equal(40f, result[1].ExpectedPoints);
        Assert.Equal(30f, result[2].ExpectedPoints);
    }

    [Fact]
    public async Task DeleteEventPredictionsAsync_RemovesAllForEvent()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        await _updater.UpdateEventPredictionsAsync(eventId, CreateSamplePredictions(eventId, count: 5));

        // Act
        var deletedCount = await _updater.DeleteEventPredictionsAsync(eventId);

        // Assert
        Assert.Equal(5, deletedCount);
        var remaining = await _readDbContext.EventPredictions.Where(p => p.EventId == eventId).CountAsync();
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task DeleteEventPredictionsAsync_DoesNotAffectOtherEvents()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var otherEventId = Guid.NewGuid();
        await _updater.UpdateEventPredictionsAsync(eventId, CreateSamplePredictions(eventId, count: 3));
        await _updater.UpdateEventPredictionsAsync(otherEventId, CreateSamplePredictions(otherEventId, count: 2));

        // Act
        await _updater.DeleteEventPredictionsAsync(eventId);

        // Assert
        var remaining = await _readDbContext.EventPredictions.Where(p => p.EventId == otherEventId).CountAsync();
        Assert.Equal(2, remaining);
    }

    // ============================================================================
    // MODEL METADATA TESTS
    // ============================================================================

    [Fact]
    public async Task UpdateModelMetadataAsync_InsertsNewModel()
    {
        // Arrange
        var metadata = CreateModelMetadata("Class450", "FinishPosition", "v1.0.0");

        // Act
        var result = await _updater.UpdateModelMetadataAsync(metadata);

        // Assert
        Assert.True(result);
        var stored = await _readDbContext.ModelMetadata.FirstOrDefaultAsync(m => m.Id == metadata.Id);
        Assert.NotNull(stored);
        Assert.Equal("v1.0.0", stored.Version);
    }

    [Fact]
    public async Task UpdateModelMetadataAsync_DeactivatesPreviousModels()
    {
        // Arrange
        var oldModel = CreateModelMetadata("Class450", "FinishPosition", "v1.0.0", isActive: true);
        await _updater.UpdateModelMetadataAsync(oldModel);

        var newModel = CreateModelMetadata("Class450", "FinishPosition", "v1.1.0", isActive: true);

        // Act
        await _updater.UpdateModelMetadataAsync(newModel);

        // Assert
        var oldStored = await _readDbContext.ModelMetadata.FirstOrDefaultAsync(m => m.Id == oldModel.Id);
        Assert.NotNull(oldStored);
        Assert.False(oldStored.IsActive);  // Old model should be deactivated

        var newStored = await _readDbContext.ModelMetadata.FirstOrDefaultAsync(m => m.Id == newModel.Id);
        Assert.NotNull(newStored);
        Assert.True(newStored.IsActive);   // New model should be active
    }

    [Fact]
    public async Task UpdateModelMetadataAsync_UpdatesExistingVersionInPlace()
    {
        // Arrange
        var originalMetadata = CreateModelMetadata("Class450", "FinishPosition", "v1.0.0");
        await _updater.UpdateModelMetadataAsync(originalMetadata);

        var updatedMetadata = new ModelMetadataReadModel
        {
            Id = Guid.NewGuid(),  // Different ID
            BikeClass = "Class450",
            ModelType = "FinishPosition",
            Version = "v1.0.0",   // Same version
            TrainedAt = DateTimeOffset.UtcNow,
            TrainingSamples = 999,  // Updated value
            ValidationAccuracy = 0.99f,
            RSquared = 0.95f,
            MeanAbsoluteError = 1.5f,
            ModelPath = "/models/updated.zip",
            IsActive = true
        };

        // Act
        await _updater.UpdateModelMetadataAsync(updatedMetadata);

        // Assert - Only one record for this version, and it's updated
        var count = await _readDbContext.ModelMetadata
            .Where(m => m.BikeClass == "Class450" && m.ModelType == "FinishPosition" && m.Version == "v1.0.0")
            .CountAsync();
        Assert.Equal(1, count);

        var stored = await _readDbContext.ModelMetadata
            .FirstAsync(m => m.BikeClass == "Class450" && m.ModelType == "FinishPosition" && m.Version == "v1.0.0");
        Assert.Equal(999, stored.TrainingSamples);
    }

    [Fact]
    public async Task GetActiveModelAsync_ReturnsOnlyActiveModel()
    {
        // Arrange
        var inactiveModel = CreateModelMetadata("Class450", "FinishPosition", "v1.0.0", isActive: false);
        var activeModel = CreateModelMetadata("Class450", "FinishPosition", "v1.1.0", isActive: true);
        _readDbContext.ModelMetadata.AddRange(inactiveModel, activeModel);
        await _readDbContext.SaveChangesAsync();

        // Act
        var result = await _updater.GetActiveModelAsync("Class450", "FinishPosition");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("v1.1.0", result.Version);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task GetActiveModelAsync_ReturnsNullWhenNoActiveModel()
    {
        // Arrange
        var inactiveModel = CreateModelMetadata("Class450", "FinishPosition", "v1.0.0", isActive: false);
        _readDbContext.ModelMetadata.Add(inactiveModel);
        await _readDbContext.SaveChangesAsync();

        // Act
        var result = await _updater.GetActiveModelAsync("Class450", "FinishPosition");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllModelsAsync_FiltersCorrectly()
    {
        // Arrange
        _readDbContext.ModelMetadata.AddRange(
            CreateModelMetadata("Class450", "FinishPosition", "v1.0.0", isActive: false),
            CreateModelMetadata("Class450", "FinishPosition", "v1.1.0", isActive: true),
            CreateModelMetadata("Class450", "Qualification", "v1.0.0", isActive: true),
            CreateModelMetadata("Class250", "FinishPosition", "v1.0.0", isActive: true)
        );
        await _readDbContext.SaveChangesAsync();

        // Act - Filter by class and type
        var result = await _updater.GetAllModelsAsync(bikeClass: "Class450", modelType: "FinishPosition");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, m => Assert.Equal("Class450", m.BikeClass));
        Assert.All(result, m => Assert.Equal("FinishPosition", m.ModelType));
    }

    [Fact]
    public async Task GetAllModelsAsync_ActiveOnlyFilter()
    {
        // Arrange
        _readDbContext.ModelMetadata.AddRange(
            CreateModelMetadata("Class450", "FinishPosition", "v1.0.0", isActive: false),
            CreateModelMetadata("Class450", "FinishPosition", "v1.1.0", isActive: true)
        );
        await _readDbContext.SaveChangesAsync();

        // Act
        var result = await _updater.GetAllModelsAsync(activeOnly: true);

        // Assert
        Assert.Single(result);
        Assert.True(result[0].IsActive);
    }

    // ============================================================================
    // EVENT READ MODEL TESTS
    // ============================================================================

    [Fact]
    public async Task UpdateEventAsync_InsertsNewEvent()
    {
        // Arrange
        var eventReadModel = CreateEventReadModel();

        // Act
        var result = await _updater.UpdateEventAsync(eventReadModel);

        // Assert
        Assert.True(result);
        var stored = await _readDbContext.Events.FirstOrDefaultAsync(e => e.Id == eventReadModel.Id);
        Assert.NotNull(stored);
        Assert.Equal(eventReadModel.Name, stored.Name);
    }

    [Fact]
    public async Task UpdateEventAsync_UpdatesExistingEvent()
    {
        // Arrange
        var originalEvent = CreateEventReadModel();
        _readDbContext.Events.Add(originalEvent);
        await _readDbContext.SaveChangesAsync();

        var updatedEvent = originalEvent with
        {
            Name = "Updated Event Name",
            RiderCount = 99
        };

        // Act
        var result = await _updater.UpdateEventAsync(updatedEvent);

        // Assert
        Assert.True(result);
        var stored = await _readDbContext.Events.FirstOrDefaultAsync(e => e.Id == originalEvent.Id);
        Assert.NotNull(stored);
        Assert.Equal("Updated Event Name", stored.Name);
        Assert.Equal(99, stored.RiderCount);
    }

    [Fact]
    public async Task GetEventByIdAsync_ReturnsCorrectEvent()
    {
        // Arrange
        var event1 = CreateEventReadModel();
        var event2 = CreateEventReadModel();
        _readDbContext.Events.AddRange(event1, event2);
        await _readDbContext.SaveChangesAsync();

        // Act
        var result = await _updater.GetEventByIdAsync(event1.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(event1.Id, result.Id);
    }

    [Fact]
    public async Task GetEventByIdAsync_ReturnsNullForMissingEvent()
    {
        // Act
        var result = await _updater.GetEventByIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetNextUpcomingEventAsync_ReturnsCorrectEvent()
    {
        // Arrange
        var pastEvent = CreateEventReadModel() with
        {
            EventDate = DateTimeOffset.UtcNow.AddDays(-7),
            IsCompleted = true
        };
        var futureEvent1 = CreateEventReadModel() with
        {
            EventDate = DateTimeOffset.UtcNow.AddDays(7),
            IsCompleted = false
        };
        var futureEvent2 = CreateEventReadModel() with
        {
            EventDate = DateTimeOffset.UtcNow.AddDays(14),
            IsCompleted = false
        };

        _readDbContext.Events.AddRange(pastEvent, futureEvent2, futureEvent1);
        await _readDbContext.SaveChangesAsync();

        // Act
        var result = await _updater.GetNextUpcomingEventAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(futureEvent1.Id, result.Id);  // Should return the nearest future event
    }

    [Fact]
    public async Task GetNextUpcomingEventAsync_IgnoresCompletedEvents()
    {
        // Arrange
        var completedFutureEvent = CreateEventReadModel() with
        {
            EventDate = DateTimeOffset.UtcNow.AddDays(1),
            IsCompleted = true
        };
        var upcomingEvent = CreateEventReadModel() with
        {
            EventDate = DateTimeOffset.UtcNow.AddDays(7),
            IsCompleted = false
        };

        _readDbContext.Events.AddRange(completedFutureEvent, upcomingEvent);
        await _readDbContext.SaveChangesAsync();

        // Act
        var result = await _updater.GetNextUpcomingEventAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(upcomingEvent.Id, result.Id);
    }

    // ============================================================================
    // HELPER METHODS
    // ============================================================================

    private static List<EventPredictionReadModel> CreateSamplePredictions(Guid eventId, int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => CreatePrediction(eventId, expectedPoints: 50f - i))
            .ToList();
    }

    private static EventPredictionReadModel CreatePrediction(
        Guid eventId,
        string bikeClass = "Class450",
        float expectedPoints = 35f)
    {
        return new EventPredictionReadModel
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            RiderId = Guid.NewGuid(),
            RiderName = $"Rider {Guid.NewGuid():N}",
            RiderNumber = Random.Shared.Next(1, 999),
            BikeClass = bikeClass,
            IsAllStar = false,
            Handicap = 0,
            ExpectedPoints = expectedPoints,
            PointsIfQualifies = expectedPoints + 5f,
            PredictedFinish = 10,
            LowerBound = expectedPoints - 10f,
            UpperBound = expectedPoints + 10f,
            Confidence = 0.8f,
            ModelVersion = "v1.0.0",
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static ModelMetadataReadModel CreateModelMetadata(
        string bikeClass,
        string modelType,
        string version,
        bool isActive = true)
    {
        return new ModelMetadataReadModel
        {
            Id = Guid.NewGuid(),
            BikeClass = bikeClass,
            ModelType = modelType,
            Version = version,
            TrainedAt = DateTimeOffset.UtcNow,
            TrainingSamples = 500,
            ValidationAccuracy = 0.75f,
            RSquared = 0.82f,
            MeanAbsoluteError = 4.5f,
            ModelPath = $"/models/{bikeClass}_{modelType}_{version}.zip",
            IsActive = isActive
        };
    }

    private static EventReadModel CreateEventReadModel()
    {
        return new EventReadModel
        {
            Id = Guid.NewGuid(),
            Name = $"Test Event {Guid.NewGuid():N}",
            Slug = $"test-event-{Guid.NewGuid():N}",
            Venue = "Test Venue",
            Location = "Test City, ST",
            EventDate = DateTimeOffset.UtcNow.AddDays(7),
            SeriesName = "2025 Monster Energy Supercross",
            SeasonYear = 2025,
            IsCompleted = false,
            LockoutTime = null,
            RiderCount = 40,
            SyncedAt = DateTimeOffset.UtcNow
        };
    }
}
