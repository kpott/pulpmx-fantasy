using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.Web.Models;

/// <summary>
/// View model for predictions page using CQRS read models.
/// </summary>
/// <remarks>
/// Uses denormalized read models from read_model schema.
/// No joins required - all data is embedded in the read models.
/// Web has NO access to write models (enforced at assembly level).
/// </remarks>
public class PredictionsViewModel
{
    /// <summary>
    /// Event for which predictions are displayed (from read_model.events).
    /// </summary>
    public EventReadModel? Event { get; set; }

    /// <summary>
    /// All events that have predictions (for event selector dropdown).
    /// </summary>
    public IReadOnlyList<EventReadModel> AllEvents { get; set; } = Array.Empty<EventReadModel>();

    /// <summary>
    /// Predictions from read model (denormalized with rider data).
    /// </summary>
    public IReadOnlyList<EventPredictionReadModel> Predictions { get; set; } = Array.Empty<EventPredictionReadModel>();

    /// <summary>
    /// Gets predictions grouped by bike class, ordered by points if qualifies.
    /// </summary>
    public IEnumerable<IGrouping<string, EventPredictionReadModel>> GetPredictionsByClass()
    {
        if (!Predictions.Any())
            return Enumerable.Empty<IGrouping<string, EventPredictionReadModel>>();

        return Predictions
            .OrderByDescending(p => p.PointsIfQualifies)
            .GroupBy(p => p.BikeClass)
            .OrderByDescending(g => g.Key); // 450 first, then 250
    }
}
