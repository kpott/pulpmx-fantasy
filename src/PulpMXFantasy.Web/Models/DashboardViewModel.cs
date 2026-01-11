using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.Web.Models;

/// <summary>
/// View model for the dashboard/home page.
/// </summary>
/// <remarks>
/// CQRS Pattern: Uses EventReadModel from read_model schema.
/// NO access to write models (enforced at assembly level).
/// </remarks>
public class DashboardViewModel
{
    /// <summary>
    /// Next upcoming event (null if no upcoming events).
    /// </summary>
    public EventReadModel? NextEvent { get; set; }

    /// <summary>
    /// Total number of riders across all events.
    /// </summary>
    public int TotalRiders { get; set; }

    /// <summary>
    /// Total number of events in database.
    /// </summary>
    public int TotalEvents { get; set; }
}
