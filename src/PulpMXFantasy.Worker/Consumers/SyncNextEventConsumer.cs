using MassTransit;
using PulpMXFantasy.Application.Consumers;
using PulpMXFantasy.Contracts.Commands;

namespace PulpMXFantasy.Worker.Consumers;

/// <summary>
/// MassTransit consumer for SyncNextEventCommand messages.
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
/// REGISTRATION:
/// =============
/// Register in MassTransit configuration:
/// <code>
/// services.AddMassTransit(x =>
/// {
///     x.AddConsumer&lt;SyncNextEventConsumer&gt;();
///     x.UsingRabbitMq((context, cfg) =>
///     {
///         cfg.ConfigureEndpoints(context);
///     });
/// });
/// </code>
///
/// MESSAGE ROUTING:
/// ================
/// MassTransit will automatically create queue: "sync-next-event"
/// Commands sent to this queue will be consumed by this consumer.
/// </remarks>
public class SyncNextEventConsumer : IConsumer<SyncNextEventCommand>
{
    private readonly SyncNextEventCommandConsumer _handler;

    /// <summary>
    /// Creates a new SyncNextEventConsumer instance.
    /// </summary>
    /// <param name="handler">The command handler that contains the actual business logic</param>
    public SyncNextEventConsumer(SyncNextEventCommandConsumer handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Consumes a SyncNextEventCommand message and delegates to the handler.
    /// </summary>
    /// <param name="context">MassTransit consume context</param>
    public async Task Consume(ConsumeContext<SyncNextEventCommand> context)
    {
        await _handler.Consume(context);
    }
}
