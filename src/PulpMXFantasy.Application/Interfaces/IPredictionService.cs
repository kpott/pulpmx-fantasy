using PulpMXFantasy.Domain.Abstractions;

namespace PulpMXFantasy.Application.Interfaces;

/// <summary>
/// Service for generating predictions for fantasy racing events.
/// </summary>
/// <remarks>
/// WHY THIS INTERFACE EXISTS:
/// ==========================
/// Application layer interface for prediction operations:
/// - Abstracts prediction logic from controllers/UI
/// - Coordinates between ML predictor and database
/// - Handles feature extraction from database
/// - Caches predictions for performance
///
/// This is APPLICATION LAYER (not Domain) because:
/// - Coordinates multiple infrastructure services
/// - Implements caching strategy (performance concern)
/// - Extracts features from database queries
/// - NOT core domain logic (that's in IRiderPredictor)
///
/// USAGE:
/// ======
/// Called by:
/// - Controllers (display predictions to users)
/// - Team optimizer (uses predictions for optimization)
/// - Background jobs (pre-generate predictions before race)
/// </remarks>
public interface IPredictionService
{
    /// <summary>
    /// Generates predictions for all riders in the next upcoming event.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of predictions sorted by expected points (highest first)</returns>
    /// <remarks>
    /// Steps:
    /// 1. Find next upcoming event from database
    /// 2. Load all event riders with qualifying data
    /// 3. Extract features for each rider (handicap, qualifying, history)
    /// 4. Call IRiderPredictor to generate predictions
    /// 5. Cache predictions (30 minutes)
    /// 6. Return sorted by expected points
    ///
    /// Features extracted:
    /// - Handicap, All-Star status, injury status
    /// - Qualifying position, lap times, gap to leader
    /// - Pick trend (crowd wisdom)
    /// - Historical averages (last 5 races)
    /// - Season statistics
    ///
    /// Example usage:
    /// <code>
    /// public async Task<IActionResult> Predictions()
    /// {
    ///     var predictions = await _predictionService.GeneratePredictionsForNextEventAsync();
    ///     return View(predictions);
    /// }
    /// </code>
    ///
    /// Returns empty list if no upcoming event or ML model unavailable.
    /// </remarks>
    Task<IReadOnlyList<RiderPrediction>> GeneratePredictionsForNextEventAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates predictions for a specific event by ID.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of predictions sorted by expected points</returns>
    /// <remarks>
    /// Similar to GeneratePredictionsForNextEventAsync but for specific event.
    ///
    /// Use cases:
    /// - Display predictions for historical events
    /// - Compare predictions vs actual results (accuracy tracking)
    /// - Generate predictions for multiple events in parallel
    ///
    /// Throws exception if event not found.
    /// </remarks>
    Task<IReadOnlyList<RiderPrediction>> GeneratePredictionsForEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cached predictions if available, otherwise generates new ones.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached or fresh predictions</returns>
    /// <remarks>
    /// Caching strategy:
    /// - Cache key: "predictions:{eventId}"
    /// - TTL: 30 minutes (predictions don't change frequently)
    /// - Invalidate on: handicap changes, qualifying results published
    ///
    /// Use this for display to users (performance critical).
    /// Use GeneratePredictionsForEventAsync for fresh predictions (accuracy tracking).
    /// </remarks>
    Task<IReadOnlyList<RiderPrediction>> GetOrGeneratePredictionsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears cached predictions for an event.
    /// </summary>
    /// <param name="eventId">Event identifier</param>
    /// <remarks>
    /// Call this when:
    /// - Handicaps change (common before race day)
    /// - Qualifying results published
    /// - Race completes (to avoid showing stale predictions)
    ///
    /// Next GetOrGeneratePredictionsAsync will generate fresh predictions.
    /// </remarks>
    Task InvalidatePredictionCacheAsync(Guid eventId);
}
