using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Domain.Entities;
using PulpMXFantasy.Domain.Enums;
using PulpMXFantasy.Infrastructure.Data;
using PulpMXFantasy.Infrastructure.Services;

namespace PulpMXFantasy.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for PredictionService.
/// </summary>
/// <remarks>
/// Uses EF Core InMemory provider for ApplicationDbContext.
/// Mocks IRiderPredictor for ML predictions.
/// Uses real MemoryCache for caching behavior tests.
/// </remarks>
public class PredictionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRiderPredictor _riderPredictor;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PredictionService> _logger;
    private readonly PredictionService _service;

    public PredictionServiceTests()
    {
        // Create unique database name for test isolation
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _riderPredictor = Substitute.For<IRiderPredictor>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _logger = Substitute.For<ILogger<PredictionService>>();

        _service = new PredictionService(_dbContext, _riderPredictor, _cache, _logger);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _cache.Dispose();
    }

    // ============================================================================
    // GeneratePredictionsForEventAsync TESTS
    // ============================================================================

    [Fact]
    public async Task GeneratePredictionsForEventAsync_WhenModelNotReady_ReturnsEmptyList()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _riderPredictor.IsModelReady().Returns(false);

        // Act
        var result = await _service.GeneratePredictionsForEventAsync(eventId);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GeneratePredictionsForEventAsync_WhenNoRiders_ReturnsEmptyList()
    {
        // Arrange
        var (series, eventEntity) = await SeedEventWithoutRidersAsync();
        _riderPredictor.IsModelReady().Returns(true);

        // Act
        var result = await _service.GeneratePredictionsForEventAsync(eventEntity.Id);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GeneratePredictionsForEventAsync_WithValidRiders_ReturnsPredictions()
    {
        // Arrange
        var (series, eventEntity, riders) = await SeedEventWithRidersAsync(riderCount: 5);
        _riderPredictor.IsModelReady().Returns(true);

        var expectedPredictions = riders.Select((r, i) => new RiderPrediction(
            r.Id,
            BikeClass.Class450,
            false,
            50f - i * 5,      // Expected points
            55f - i * 5,      // Points if qualifies
            i + 1,            // Predicted finish
            40f - i * 5,      // Lower bound
            60f - i * 5,      // Upper bound
            0.8f              // Confidence
        )).ToList();

        _riderPredictor.PredictBatch(Arg.Any<IEnumerable<RiderFeatures>>())
            .Returns(expectedPredictions);

        // Act
        var result = await _service.GeneratePredictionsForEventAsync(eventEntity.Id);

        // Assert
        Assert.Equal(5, result.Count);
        _riderPredictor.Received(1).PredictBatch(Arg.Any<IEnumerable<RiderFeatures>>());
    }

    [Fact]
    public async Task GeneratePredictionsForEventAsync_ExcludesInjuredRiders()
    {
        // Arrange
        var (series, eventEntity, riders) = await SeedEventWithRidersAsync(riderCount: 3);

        // Mark one rider as injured
        var injuredEventRider = await _dbContext.EventRiders.FirstAsync();
        injuredEventRider.IsInjured = true;
        await _dbContext.SaveChangesAsync();

        _riderPredictor.IsModelReady().Returns(true);
        _riderPredictor.PredictBatch(Arg.Any<IEnumerable<RiderFeatures>>())
            .Returns(callInfo =>
            {
                var features = callInfo.Arg<IEnumerable<RiderFeatures>>().ToList();
                return features.Select((f, i) => new RiderPrediction(
                    f.RiderId, f.BikeClass, f.IsAllStar, 30f - i * 5, 35f - i * 5, i + 1, 20f, 40f, 0.8f
                )).ToList();
            });

        // Act
        var result = await _service.GeneratePredictionsForEventAsync(eventEntity.Id);

        // Assert - Should only have 2 riders (not the injured one)
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GeneratePredictionsForEventAsync_ReturnsSortedByExpectedPointsDescending()
    {
        // Arrange
        var (series, eventEntity, riders) = await SeedEventWithRidersAsync(riderCount: 3);
        _riderPredictor.IsModelReady().Returns(true);

        // Return predictions in unsorted order
        var unsortedPredictions = new List<RiderPrediction>
        {
            new(riders[0].Id, BikeClass.Class450, false, 20f, 25f, 3, 15f, 25f, 0.8f),
            new(riders[1].Id, BikeClass.Class450, false, 50f, 55f, 1, 45f, 55f, 0.9f),  // Highest
            new(riders[2].Id, BikeClass.Class450, false, 35f, 40f, 2, 30f, 40f, 0.85f),
        };

        _riderPredictor.PredictBatch(Arg.Any<IEnumerable<RiderFeatures>>())
            .Returns(unsortedPredictions);

        // Act
        var result = await _service.GeneratePredictionsForEventAsync(eventEntity.Id);

        // Assert - Should be sorted by expected points descending
        Assert.Equal(50f, result[0].ExpectedPoints);
        Assert.Equal(35f, result[1].ExpectedPoints);
        Assert.Equal(20f, result[2].ExpectedPoints);
    }

    // ============================================================================
    // GeneratePredictionsForNextEventAsync TESTS
    // ============================================================================

    [Fact]
    public async Task GeneratePredictionsForNextEventAsync_WhenNoUpcomingEvent_ReturnsEmptyList()
    {
        // Arrange - No events seeded

        // Act
        var result = await _service.GeneratePredictionsForNextEventAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GeneratePredictionsForNextEventAsync_FindsNextUpcomingEvent()
    {
        // Arrange
        var (series, pastEvent, _) = await SeedEventWithRidersAsync(riderCount: 2);
        pastEvent.EventDate = DateTimeOffset.UtcNow.AddDays(-7);
        pastEvent.IsCompleted = true;

        // Add a future event
        var futureEvent = new Event
        {
            Id = Guid.NewGuid(),
            SeriesId = series.Id,
            Slug = "future-event",
            Name = "Future Event",
            Venue = "Future Venue",
            Location = "Future City",
            EventDate = DateTimeOffset.UtcNow.AddDays(7),
            RoundNumber = 2,
            SeriesType = SeriesType.Supercross,
            EventFormat = EventFormat.Standard,
            Division = Division.Combined,
            IsCompleted = false
        };
        _dbContext.Events.Add(futureEvent);

        // Add riders to future event
        var rider = new Rider
        {
            Id = Guid.NewGuid(),
            PulpMxId = "future-rider",
            Name = "Future Rider",
            Number = 99
        };
        _dbContext.Riders.Add(rider);

        var eventRider = new EventRider
        {
            Id = Guid.NewGuid(),
            EventId = futureEvent.Id,
            RiderId = rider.Id,
            BikeClass = BikeClass.Class450,
            Handicap = 0,
            IsAllStar = false
        };
        _dbContext.EventRiders.Add(eventRider);
        await _dbContext.SaveChangesAsync();

        _riderPredictor.IsModelReady().Returns(true);
        _riderPredictor.PredictBatch(Arg.Any<IEnumerable<RiderFeatures>>())
            .Returns(new List<RiderPrediction>
            {
                new(rider.Id, BikeClass.Class450, false, 30f, 35f, 1, 25f, 35f, 0.8f)
            });

        // Act
        var result = await _service.GeneratePredictionsForNextEventAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal(rider.Id, result[0].RiderId);
    }

    // ============================================================================
    // GetOrGeneratePredictionsAsync CACHING TESTS
    // ============================================================================

    [Fact]
    public async Task GetOrGeneratePredictionsAsync_ReturnsCachedPredictions_WhenAvailable()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var cachedPredictions = new List<RiderPrediction>
        {
            new(Guid.NewGuid(), BikeClass.Class450, false, 40f, 45f, 1, 35f, 45f, 0.9f)
        };

        // Pre-populate cache
        _cache.Set($"predictions:{eventId}", (IReadOnlyList<RiderPrediction>)cachedPredictions);

        // Act
        var result = await _service.GetOrGeneratePredictionsAsync(eventId);

        // Assert
        Assert.Equal(cachedPredictions, result);
        _riderPredictor.DidNotReceive().PredictBatch(Arg.Any<IEnumerable<RiderFeatures>>());
    }

    [Fact]
    public async Task GetOrGeneratePredictionsAsync_GeneratesAndCaches_WhenNotCached()
    {
        // Arrange
        var (series, eventEntity, riders) = await SeedEventWithRidersAsync(riderCount: 2);
        _riderPredictor.IsModelReady().Returns(true);

        var predictions = riders.Select((r, i) => new RiderPrediction(
            r.Id, BikeClass.Class450, false, 30f - i * 5, 35f - i * 5, i + 1, 25f, 35f, 0.8f
        )).ToList();

        _riderPredictor.PredictBatch(Arg.Any<IEnumerable<RiderFeatures>>())
            .Returns(predictions);

        // Act - First call should generate
        var result1 = await _service.GetOrGeneratePredictionsAsync(eventEntity.Id);

        // Assert
        Assert.Equal(2, result1.Count);
        _riderPredictor.Received(1).PredictBatch(Arg.Any<IEnumerable<RiderFeatures>>());

        // Act - Second call should use cache
        var result2 = await _service.GetOrGeneratePredictionsAsync(eventEntity.Id);

        // Assert - Still only 1 call to predictor (cached)
        _riderPredictor.Received(1).PredictBatch(Arg.Any<IEnumerable<RiderFeatures>>());
        Assert.Equal(result1, result2);
    }

    // ============================================================================
    // InvalidatePredictionCacheAsync TESTS
    // ============================================================================

    [Fact]
    public async Task InvalidatePredictionCacheAsync_RemovesCacheEntry()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var cachedPredictions = new List<RiderPrediction>
        {
            new(Guid.NewGuid(), BikeClass.Class450, false, 40f, 45f, 1, 35f, 45f, 0.9f)
        };
        _cache.Set($"predictions:{eventId}", (IReadOnlyList<RiderPrediction>)cachedPredictions);

        // Verify cache has the entry
        Assert.True(_cache.TryGetValue($"predictions:{eventId}", out _));

        // Act
        await _service.InvalidatePredictionCacheAsync(eventId);

        // Assert
        Assert.False(_cache.TryGetValue($"predictions:{eventId}", out _));
    }

    // ============================================================================
    // FEATURE EXTRACTION TESTS
    // ============================================================================

    [Fact]
    public async Task GeneratePredictionsForEventAsync_ExtractsCorrectFeatures()
    {
        // Arrange
        var (series, eventEntity, riders) = await SeedEventWithRidersAsync(riderCount: 1);
        var eventRider = await _dbContext.EventRiders.FirstAsync();
        eventRider.Handicap = 5;
        eventRider.IsAllStar = true;
        eventRider.CombinedQualyPosition = 3;
        eventRider.PickTrend = 45.5m;
        await _dbContext.SaveChangesAsync();

        _riderPredictor.IsModelReady().Returns(true);

        RiderFeatures? capturedFeatures = null;
        _riderPredictor.PredictBatch(Arg.Do<IEnumerable<RiderFeatures>>(f =>
            capturedFeatures = f.FirstOrDefault()))
            .Returns(new List<RiderPrediction>
            {
                new(riders[0].Id, BikeClass.Class450, true, 30f, 35f, 1, 25f, 35f, 0.8f)
            });

        // Act
        await _service.GeneratePredictionsForEventAsync(eventEntity.Id);

        // Assert
        Assert.NotNull(capturedFeatures);
        Assert.Equal(5, capturedFeatures.Handicap);
        Assert.True(capturedFeatures.IsAllStar);
        Assert.Equal(3, capturedFeatures.QualifyingPosition);
        Assert.Equal(45.5m, capturedFeatures.PickTrend);
    }

    [Fact]
    public async Task GeneratePredictionsForEventAsync_UsesOnlySameSeriesTypeHistory()
    {
        // Arrange - Create historical data in different series types
        var series = new Series
        {
            Id = Guid.NewGuid(),
            Name = "2025 Supercross",
            Year = 2025,
            SeriesType = SeriesType.Supercross,
            StartDate = DateTimeOffset.UtcNow.AddMonths(-2)
        };
        _dbContext.Series.Add(series);

        var rider = new Rider
        {
            Id = Guid.NewGuid(),
            PulpMxId = "test-rider",
            Name = "Test Rider",
            Number = 1
        };
        _dbContext.Riders.Add(rider);

        // Past Supercross event (should be included in history)
        var pastSxEvent = new Event
        {
            Id = Guid.NewGuid(),
            SeriesId = series.Id,
            Slug = "past-sx-event",
            Name = "Past SX Event",
            Venue = "Past Venue",
            Location = "Past City",
            EventDate = DateTimeOffset.UtcNow.AddMonths(-1),
            RoundNumber = 1,
            SeriesType = SeriesType.Supercross,
            EventFormat = EventFormat.Standard,
            Division = Division.Combined,
            IsCompleted = true
        };
        _dbContext.Events.Add(pastSxEvent);

        var pastSxEventRider = new EventRider
        {
            Id = Guid.NewGuid(),
            EventId = pastSxEvent.Id,
            RiderId = rider.Id,
            BikeClass = BikeClass.Class450,
            Handicap = 0,
            IsAllStar = false,
            FinishPosition = 5,
            FantasyPoints = 34
        };
        _dbContext.EventRiders.Add(pastSxEventRider);

        // Past Motocross event (should NOT be included in Supercross history)
        var mxSeries = new Series
        {
            Id = Guid.NewGuid(),
            Name = "2025 Motocross",
            Year = 2025,
            SeriesType = SeriesType.Motocross,
            StartDate = DateTimeOffset.UtcNow.AddMonths(-3)
        };
        _dbContext.Series.Add(mxSeries);

        var pastMxEvent = new Event
        {
            Id = Guid.NewGuid(),
            SeriesId = mxSeries.Id,
            Slug = "past-mx-event",
            Name = "Past MX Event",
            Venue = "MX Venue",
            Location = "MX City",
            EventDate = DateTimeOffset.UtcNow.AddMonths(-2),
            RoundNumber = 1,
            SeriesType = SeriesType.Motocross,
            EventFormat = EventFormat.Motocross,
            Division = Division.Combined,
            IsCompleted = true
        };
        _dbContext.Events.Add(pastMxEvent);

        var pastMxEventRider = new EventRider
        {
            Id = Guid.NewGuid(),
            EventId = pastMxEvent.Id,
            RiderId = rider.Id,
            BikeClass = BikeClass.Class450,
            Handicap = 0,
            IsAllStar = false,
            FinishPosition = 1,
            FantasyPoints = 50  // High points in MX
        };
        _dbContext.EventRiders.Add(pastMxEventRider);

        // Future Supercross event (target for predictions)
        var futureEvent = new Event
        {
            Id = Guid.NewGuid(),
            SeriesId = series.Id,
            Slug = "future-sx-event",
            Name = "Future SX Event",
            Venue = "Future Venue",
            Location = "Future City",
            EventDate = DateTimeOffset.UtcNow.AddDays(7),
            RoundNumber = 2,
            SeriesType = SeriesType.Supercross,
            EventFormat = EventFormat.Standard,
            Division = Division.Combined,
            IsCompleted = false
        };
        _dbContext.Events.Add(futureEvent);

        var futureEventRider = new EventRider
        {
            Id = Guid.NewGuid(),
            EventId = futureEvent.Id,
            RiderId = rider.Id,
            BikeClass = BikeClass.Class450,
            Handicap = 0,
            IsAllStar = false
        };
        _dbContext.EventRiders.Add(futureEventRider);

        await _dbContext.SaveChangesAsync();

        _riderPredictor.IsModelReady().Returns(true);

        RiderFeatures? capturedFeatures = null;
        _riderPredictor.PredictBatch(Arg.Do<IEnumerable<RiderFeatures>>(f =>
            capturedFeatures = f.FirstOrDefault()))
            .Returns(new List<RiderPrediction>
            {
                new(rider.Id, BikeClass.Class450, false, 30f, 35f, 1, 25f, 35f, 0.8f)
            });

        // Act
        await _service.GeneratePredictionsForEventAsync(futureEvent.Id);

        // Assert - Features should use SX history (34 pts), not MX history (50 pts)
        Assert.NotNull(capturedFeatures);
        // AvgFantasyPointsLast5 should be from SX only (34 points average from one race)
        Assert.Equal(34m, capturedFeatures.AvgFantasyPointsLast5);
    }

    // ============================================================================
    // HELPER METHODS
    // ============================================================================

    private async Task<(Series series, Event eventEntity)> SeedEventWithoutRidersAsync()
    {
        var series = new Series
        {
            Id = Guid.NewGuid(),
            Name = "2025 Supercross",
            Year = 2025,
            SeriesType = SeriesType.Supercross,
            StartDate = DateTimeOffset.UtcNow.AddMonths(-1)
        };
        _dbContext.Series.Add(series);

        var eventEntity = new Event
        {
            Id = Guid.NewGuid(),
            SeriesId = series.Id,
            Slug = "test-event",
            Name = "Test Event",
            Venue = "Test Venue",
            Location = "Test City",
            EventDate = DateTimeOffset.UtcNow.AddDays(7),
            RoundNumber = 1,
            SeriesType = SeriesType.Supercross,
            EventFormat = EventFormat.Standard,
            Division = Division.Combined,
            IsCompleted = false
        };
        _dbContext.Events.Add(eventEntity);
        await _dbContext.SaveChangesAsync();

        return (series, eventEntity);
    }

    private async Task<(Series series, Event eventEntity, List<Rider> riders)> SeedEventWithRidersAsync(int riderCount)
    {
        var (series, eventEntity) = await SeedEventWithoutRidersAsync();

        var riders = new List<Rider>();
        for (int i = 0; i < riderCount; i++)
        {
            var rider = new Rider
            {
                Id = Guid.NewGuid(),
                PulpMxId = $"rider-{i}",
                Name = $"Rider {i}",
                Number = i + 1
            };
            _dbContext.Riders.Add(rider);
            riders.Add(rider);

            var eventRider = new EventRider
            {
                Id = Guid.NewGuid(),
                EventId = eventEntity.Id,
                RiderId = rider.Id,
                BikeClass = BikeClass.Class450,
                Handicap = 0,
                IsAllStar = false
            };
            _dbContext.EventRiders.Add(eventRider);
        }

        await _dbContext.SaveChangesAsync();
        return (series, eventEntity, riders);
    }
}
