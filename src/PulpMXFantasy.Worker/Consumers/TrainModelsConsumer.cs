using MassTransit;
using PulpMXFantasy.Application.Consumers;
using PulpMXFantasy.Contracts.Commands;

namespace PulpMXFantasy.Worker.Consumers;

/// <summary>
/// MassTransit consumer for TrainModelsCommand messages.
/// </summary>
/// <remarks>
/// WHY THIS CONSUMER EXISTS:
/// =========================
/// MassTransit requires consumer classes to be registered with its DI container.
/// This thin wrapper delegates to the actual command handler in the Application layer.
///
/// DESIGN PATTERN:
/// ===============
/// - Consumer (Worker layer): MassTransit integration, message receiving
/// - CommandHandler (Application layer): Business logic, orchestration
///
/// This separation allows:
/// 1. Handler can be unit tested without MassTransit dependencies
/// 2. Handler can be reused in other contexts (HTTP endpoints, CLI)
/// 3. Consumer handles MassTransit-specific concerns (retries, dead-letter, etc.)
///
/// LONG-RUNNING COMMAND:
/// =====================
/// TrainModelsCommand is the longest-running command (60-300 seconds).
/// Consider configuring a longer timeout for this consumer:
/// <code>
/// cfg.ReceiveEndpoint("train-models", e =>
/// {
///     e.Consumer&lt;TrainModelsConsumer&gt;(context);
///     e.PrefetchCount = 1; // Only process one at a time
///     e.ConcurrentMessageLimit = 1;
/// });
/// </code>
///
/// REGISTRATION:
/// =============
/// Register in MassTransit configuration:
/// <code>
/// services.AddMassTransit(x =>
/// {
///     x.AddConsumer&lt;TrainModelsConsumer&gt;();
///     x.UsingRabbitMq((context, cfg) =>
///     {
///         cfg.ConfigureEndpoints(context);
///     });
/// });
/// </code>
///
/// MESSAGE ROUTING:
/// ================
/// MassTransit will automatically create queue: "train-models"
/// Commands sent to this queue will be consumed by this consumer.
/// </remarks>
public class TrainModelsConsumer : IConsumer<TrainModelsCommand>
{
    private readonly TrainModelsCommandConsumer _handler;

    /// <summary>
    /// Creates a new TrainModelsConsumer instance.
    /// </summary>
    /// <param name="handler">The command handler that contains the actual business logic</param>
    public TrainModelsConsumer(TrainModelsCommandConsumer handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Consumes a TrainModelsCommand message and delegates to the handler.
    /// </summary>
    /// <param name="context">MassTransit consume context</param>
    public async Task Consume(ConsumeContext<TrainModelsCommand> context)
    {
        await _handler.Consume(context);
    }
}
