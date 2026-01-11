using MassTransit;
using PulpMXFantasy.Contracts.Commands;

namespace PulpMXFantasy.Messaging;

/// <summary>
/// Centralized endpoint conventions for all MassTransit commands.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS:
/// ================
/// Per Chris Patterson's guidance, IBus.Send() requires endpoint conventions
/// to be mapped before the bus starts. This class centralizes all mappings
/// so both Web and Worker projects use consistent queue names.
///
/// QUEUE NAMING:
/// =============
/// - Commands use descriptive queue names
/// - Consistent with MassTransit kebab-case conventions where possible
/// - Some legacy names retained for compatibility
///
/// USAGE:
/// ======
/// Call MapAllEndpoints() BEFORE AddMassTransit() in Program.cs:
/// <code>
/// EndpointConventions.MapAllEndpoints();
/// builder.Services.AddMassTransit(x => { ... });
/// </code>
/// </remarks>
public static class EndpointConventions
{
    /// <summary>
    /// Maps all command types to their destination queues.
    /// Must be called before AddMassTransit().
    /// </summary>
    public static void MapAllEndpoints()
    {
        // SyncNextEvent - Sync the next upcoming event from PulpMX API
        EndpointConvention.Map<SyncNextEventCommand>(
            new Uri("queue:sync-next-event"));

        // ImportEvents - Import multiple historical events
        EndpointConvention.Map<ImportEventsCommand>(
            new Uri("queue:import-events"));

        // TrainModels - Train all ML models (long-running)
        EndpointConvention.Map<TrainModelsCommand>(
            new Uri("queue:train-models"));
    }

    /// <summary>
    /// Queue name for SyncNextEvent command.
    /// </summary>
    public const string SyncNextEventQueue = "sync-next-event";

    /// <summary>
    /// Queue name for ImportEvents command.
    /// </summary>
    public const string ImportEventsQueue = "import-events";

    /// <summary>
    /// Queue name for TrainModels command.
    /// </summary>
    public const string TrainModelsQueue = "train-models";
}
