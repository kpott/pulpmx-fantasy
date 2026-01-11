using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Infrastructure.Data;

namespace PulpMXFantasy.Infrastructure.Services;

/// <summary>
/// Implementation of prediction service that generates rider fantasy point predictions.
/// </summary>
/// <remarks>
/// FEATURE EXTRACTION STRATEGY:
/// ============================
/// Combines data from multiple sources:
/// 1. Event-specific data (from EventRider table)
///    - Handicap, All-Star status, injury status
///    - Qualifying position, lap times
///    - Pick trend (crowd wisdom)
///
/// 2. Historical data (from previous EventRider records)
///    - Average finish position last 5 races
///    - Average fantasy points last 5 races
///    - Finish rate (DNF percentage)
///    - Season points accumulated
///
/// 3. Track-specific data
///    - Historical performance at this venue
///    - Track type (indoor/outdoor, soil characteristics)
///
/// If historical data insufficient (new riders), use series averages.
///
/// CACHING STRATEGY:
/// =================
/// Predictions cached in-memory for 30 minutes:
/// - Key: "predictions:{eventId}"
/// - Reduces ML inference overhead
/// - Cache invalidated when handicaps/qualifying changes
///
/// Memory cache because:
/// - Predictions don't need to survive app restarts
/// - Fast access (no network/disk I/O)
/// - Automatic eviction on memory pressure
/// </remarks>
public class PredictionService : IPredictionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRiderPredictor _riderPredictor;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PredictionService> _logger;

    private const string CacheKeyPrefix = "predictions:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public PredictionService(
        ApplicationDbContext dbContext,
        IRiderPredictor riderPredictor,
        IMemoryCache cache,
        ILogger<PredictionService> logger)
    {
        _dbContext = dbContext;
        _riderPredictor = riderPredictor;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RiderPrediction>> GeneratePredictionsForNextEventAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating predictions for next upcoming event");

        // Find next upcoming event
        var nextEvent = await _dbContext.Events
            .Where(e => !e.IsCompleted && e.EventDate >= DateTimeOffset.UtcNow)
            .OrderBy(e => e.EventDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (nextEvent == null)
        {
            _logger.LogInformation("No upcoming events found");
            return Array.Empty<RiderPrediction>();
        }

        return await GeneratePredictionsForEventAsync(nextEvent.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RiderPrediction>> GeneratePredictionsForEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating predictions for event {EventId}", eventId);

        // Check if ML model is ready
        if (!_riderPredictor.IsModelReady())
        {
            _logger.LogWarning("ML model not ready, cannot generate predictions");
            return Array.Empty<RiderPrediction>();
        }

        // Load event with riders
        var eventRiders = await _dbContext.EventRiders
            .Include(er => er.Rider)
            .Include(er => er.Event)
            .Where(er => er.EventId == eventId && !er.IsInjured)
            .ToListAsync(cancellationToken);

        if (eventRiders.Count == 0)
        {
            _logger.LogWarning("No riders found for event {EventId}", eventId);
            return Array.Empty<RiderPrediction>();
        }

        _logger.LogInformation(
            "Extracting features for {RiderCount} riders",
            eventRiders.Count);

        // Extract features for each rider
        var featuresList = new List<RiderFeatures>();
        var featureCount = 0;

        foreach (var eventRider in eventRiders)
        {
            try
            {
                var features = await ExtractFeaturesAsync(eventRider, cancellationToken);
                featuresList.Add(features);

                // Log first 3 riders' features for debugging
                if (featureCount < 3)
                {
                    _logger.LogInformation(
                        "Features for rider {RiderName}: Handicap={Handicap}, Qualy={Qualy}, AvgFinish={AvgFinish}, AvgPoints={AvgPoints}, IsAllStar={IsAllStar}",
                        eventRider.Rider.Name,
                        features.Handicap,
                        features.QualifyingPosition,
                        features.AvgFinishLast5,
                        features.AvgFantasyPointsLast5,
                        features.IsAllStar);
                }
                featureCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error extracting features for rider {RiderId}, skipping",
                    eventRider.RiderId);
                // Continue with other riders
            }
        }

        if (featuresList.Count == 0)
        {
            _logger.LogWarning("No valid features extracted for event {EventId}", eventId);
            return Array.Empty<RiderPrediction>();
        }

        // Generate predictions using ML model (batch inference)
        _logger.LogInformation("Generating predictions for {FeatureCount} riders", featuresList.Count);
        var predictions = _riderPredictor.PredictBatch(featuresList);

        // Log first 3 predictions for debugging
        var predictionsList = predictions.ToList();
        for (int i = 0; i < Math.Min(3, predictionsList.Count); i++)
        {
            var pred = predictionsList[i];
            _logger.LogInformation(
                "Prediction {Index}: RiderId={RiderId}, ExpectedPoints={Points}, Class={Class}",
                i + 1,
                pred.RiderId,
                pred.ExpectedPoints,
                pred.BikeClass);
        }

        // Sort by expected points (highest first)
        var sortedPredictions = predictionsList.OrderByDescending(p => p.ExpectedPoints).ToList();

        _logger.LogInformation(
            "Successfully generated {PredictionCount} predictions for event {EventId}",
            sortedPredictions.Count,
            eventId);

        return sortedPredictions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RiderPrediction>> GetOrGeneratePredictionsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{eventId}";

        // Try to get from cache
        if (_cache.TryGetValue<IReadOnlyList<RiderPrediction>>(cacheKey, out var cachedPredictions))
        {
            _logger.LogDebug("Returning cached predictions for event {EventId}", eventId);
            return cachedPredictions!;
        }

        // Generate fresh predictions
        var predictions = await GeneratePredictionsForEventAsync(eventId, cancellationToken);

        // Cache predictions
        _cache.Set(cacheKey, predictions, CacheDuration);
        _logger.LogDebug("Cached predictions for event {EventId} (TTL: {Duration})", eventId, CacheDuration);

        return predictions;
    }

    /// <inheritdoc />
    public Task InvalidatePredictionCacheAsync(Guid eventId)
    {
        var cacheKey = $"{CacheKeyPrefix}{eventId}";
        _cache.Remove(cacheKey);
        _logger.LogInformation("Invalidated prediction cache for event {EventId}", eventId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Extracts ML features for a single rider.
    /// </summary>
    /// <remarks>
    /// CRITICAL: Only uses historical data from the SAME series type:
    /// - Supercross predictions use ONLY Supercross history (last 2 years)
    /// - Motocross predictions use ONLY Motocross history (last 2 years)
    /// - SuperMotocross predictions use ONLY SuperMotocross history
    ///
    /// This is essential because rider performance differs significantly between disciplines.
    /// </remarks>
    private async Task<RiderFeatures> ExtractFeaturesAsync(
        Domain.Entities.EventRider eventRider,
        CancellationToken cancellationToken)
    {
        var eventSeriesType = eventRider.Event.SeriesType;
        var twoYearsAgo = DateTimeOffset.UtcNow.AddYears(-2);

        _logger.LogDebug(
            "Extracting features for rider {RiderId}: EventSeriesType={SeriesType}, TwoYearsAgo={Date}",
            eventRider.RiderId,
            eventSeriesType,
            twoYearsAgo);

        // Get historical data for this rider (last 5 races in same class AND series type)
        // CRITICAL: Include ALL races (both made main AND DNQ) to avoid data leakage
        var historicalRaces = await _dbContext.EventRiders
            .Include(er => er.Event)
            .Where(er =>
                er.RiderId == eventRider.RiderId &&
                er.BikeClass == eventRider.BikeClass &&
                er.Event.IsCompleted &&
                er.Event.EventDate >= twoYearsAgo && // Last 2 years only
                er.Event.SeriesType == eventSeriesType) // SAME series type
            .OrderByDescending(er => er.Event.EventDate)
            .Take(5)
            .ToListAsync(cancellationToken);

        // DIAGNOSTIC: Log historical data count
        _logger.LogDebug(
            "Found {Count} historical races for rider {RiderId}, class {Class}, seriesType {SeriesType}",
            historicalRaces.Count,
            eventRider.RiderId,
            eventRider.BikeClass,
            eventSeriesType);

        if (!historicalRaces.Any())
        {
            _logger.LogWarning(
                "No historical data found for rider {RiderId}, class {Class}, seriesType {SeriesType}, twoYearsAgo {Date}",
                eventRider.RiderId,
                eventRider.BikeClass,
                eventSeriesType,
                twoYearsAgo);
        }

        // Calculate historical averages
        // For DNQ races (NULL finish position), treat as position 30 (worse than 22)
        var avgFinishLast5 = historicalRaces.Any()
            ? (decimal)historicalRaces.Average(er => er.FinishPosition ?? 30)
            : (decimal?)null;

        var avgFantasyPointsLast5 = historicalRaces.Any()
            ? (decimal)historicalRaces.Average(er => er.FantasyPoints ?? 0)
            : (decimal?)null;

        var finishRate = historicalRaces.Any()
            ? (decimal)historicalRaces.Count(er => er.FinishPosition.HasValue && er.FinishPosition <= 22) / historicalRaces.Count * 100
            : (decimal?)null;

        // Get season points (sum of fantasy points in current season)
        var seasonPoints = await _dbContext.EventRiders
            .Where(er =>
                er.RiderId == eventRider.RiderId &&
                er.Event.SeriesId == eventRider.Event.SeriesId &&
                er.Event.IsCompleted)
            .SumAsync(er => er.FantasyPoints ?? 0, cancellationToken);

        // Get track history (average finish at this venue, same series type, last 2 years)
        // CRITICAL: Include ALL races (DNQ treated as position 30)
        var trackHistoryRaces = await _dbContext.EventRiders
            .Include(er => er.Event)
            .Where(er =>
                er.RiderId == eventRider.RiderId &&
                er.Event.Venue == eventRider.Event.Venue &&
                er.Event.IsCompleted &&
                er.Event.EventDate >= twoYearsAgo &&
                er.Event.SeriesType == eventSeriesType)
            .ToListAsync(cancellationToken);

        var trackHistory = trackHistoryRaces.Any()
            ? (decimal)trackHistoryRaces.Average(er => er.FinishPosition ?? 30)
            : (decimal?)null;

        // Determine track type (simplified - could be enhanced)
        var trackType = eventRider.Event.SeriesType == Domain.Enums.SeriesType.Supercross
            ? "Indoor"
            : "Outdoor";

        // Determine team quality (simplified - based on historical performance)
        // Top performers likely on factory teams, others on privateer teams
        var teamQuality = avgFantasyPointsLast5 switch
        {
            >= 30 => "Factory",
            >= 15 => "Satellite",
            _ => "Privateer"
        };

        // Recent momentum (last 3 races trend)
        var recentMomentum = CalculateRecentMomentum(historicalRaces);

        return new RiderFeatures(
            RiderId: eventRider.RiderId,
            BikeClass: eventRider.BikeClass,
            Handicap: eventRider.Handicap,
            IsAllStar: eventRider.IsAllStar,
            IsInjured: eventRider.IsInjured,
            PickTrend: eventRider.PickTrend,
            QualifyingPosition: eventRider.CombinedQualyPosition,
            QualifyingLapTime: eventRider.BestQualyLapSeconds,
            QualyGapToLeader: eventRider.QualyGapToLeader,
            AvgFinishLast5: avgFinishLast5,
            AvgFantasyPointsLast5: avgFantasyPointsLast5,
            FinishRate: finishRate,
            SeasonPoints: seasonPoints,
            TrackHistory: trackHistory,
            TrackType: trackType,
            DaysSinceInjury: null, // TODO: Implement injury tracking
            TeamQuality: teamQuality,
            GatePick: null, // TODO: Add gate pick data from API
            RecentMomentum: recentMomentum
        );
    }

    /// <summary>
    /// Calculates recent momentum (improving/declining trend).
    /// </summary>
    /// <param name="historicalRaces">Recent races ordered by date (newest first)</param>
    /// <returns>Positive = improving, negative = declining, 0 = stable</returns>
    private static decimal? CalculateRecentMomentum(List<Domain.Entities.EventRider> historicalRaces)
    {
        if (historicalRaces.Count < 3)
            return null;

        // Compare average of last 3 races vs. previous races
        var recentThree = historicalRaces.Take(3).Average(er => er.FantasyPoints ?? 0);
        var previousRaces = historicalRaces.Skip(3).Average(er => er.FantasyPoints ?? 0);

        // Positive = improving (recent avg higher than previous)
        // Negative = declining (recent avg lower than previous)
        return (decimal)(recentThree - previousRaces);
    }
}
