using MassTransit;
using PulpMXFantasy.Application.Consumers;
using PulpMXFantasy.Contracts.Events;

namespace PulpMXFantasy.Worker.Consumers;

/// <summary>
/// MassTransit consumer for ModelsTrainedEvent messages.
/// </summary>
/// <remarks>
/// WHY THIS CONSUMER EXISTS:
/// =========================
/// MassTransit requires consumer classes to be registered with its DI container.
/// This thin wrapper delegates to the actual event handler in the Application layer.
///
/// EVENT vs COMMAND:
/// =================
/// This is an EVENT consumer (pub/sub), not a command consumer:
/// - Events are published (broadcast to all subscribers)
/// - Commands are sent (point-to-point to single consumer)
/// - ModelsTrainedEvent is published after ML models are trained
/// - Multiple consumers could subscribe to this event in the future
///
/// DESIGN PATTERN:
/// ===============
/// - Consumer (Worker layer): MassTransit integration, message receiving
/// - EventHandler (Application layer): Business logic, orchestration
///
/// This separation allows:
/// 1. Handler can be unit tested without MassTransit dependencies
/// 2. Handler can be reused in other contexts (direct invocation)
/// 3. Consumer handles MassTransit-specific concerns (retries, dead-letter, etc.)
///
/// TRIGGERED BY:
/// =============
/// ModelTrainer publishes ModelsTrainedEvent after training completes.
/// This consumer receives the event and triggers automatic prediction generation.
///
/// FLOW:
/// =====
/// 1. Admin triggers TrainModelsCommand (via API or scheduled job)
/// 2. ModelTrainer trains all 4 models (250/450 x Qualification/FinishPosition)
/// 3. ModelTrainer publishes ModelsTrainedEvent
/// 4. THIS CONSUMER receives event
/// 5. ModelsTrainedEventConsumer generates predictions for next upcoming event
/// 6. Handler writes predictions to read model and publishes PredictionsGeneratedEvent
///
/// REGISTRATION:
/// =============
/// Register in MassTransit configuration:
/// <code>
/// services.AddMassTransit(x =>
/// {
///     x.AddConsumer&lt;ModelsTrainedConsumer&gt;();
///     x.UsingRabbitMq((context, cfg) =>
///     {
///         cfg.ConfigureEndpoints(context);
///     });
/// });
/// </code>
///
/// MESSAGE ROUTING:
/// ================
/// As an event (pub/sub), MassTransit creates an exchange "PulpMXFantasy.Contracts.Events:ModelsTrainedEvent"
/// and binds this consumer's queue to it.
/// Multiple consumers can subscribe to the same event.
/// </remarks>
public class ModelsTrainedConsumer : IConsumer<ModelsTrainedEvent>
{
    private readonly ModelsTrainedEventConsumer _handler;

    /// <summary>
    /// Creates a new ModelsTrainedConsumer instance.
    /// </summary>
    /// <param name="handler">The event handler that contains the actual business logic</param>
    public ModelsTrainedConsumer(ModelsTrainedEventConsumer handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Consumes a ModelsTrainedEvent message and delegates to the handler.
    /// </summary>
    /// <param name="context">MassTransit consume context</param>
    /// <remarks>
    /// The handler implements IConsumer&lt;ModelsTrainedEvent&gt; directly,
    /// so we can just delegate the entire context to it.
    /// </remarks>
    public async Task Consume(ConsumeContext<ModelsTrainedEvent> context)
    {
        await _handler.Consume(context);
    }
}
