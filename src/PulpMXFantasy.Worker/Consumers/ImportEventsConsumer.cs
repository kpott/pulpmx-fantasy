using MassTransit;
using PulpMXFantasy.Application.Consumers;
using PulpMXFantasy.Contracts.Commands;

namespace PulpMXFantasy.Worker.Consumers;

/// <summary>
/// MassTransit consumer wrapper for ImportEventsCommand.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS:
/// ================
/// This is a thin wrapper that delegates to ImportEventsCommandConsumer.
/// In MassTransit, consumers are registered with the bus and receive messages.
///
/// ARCHITECTURE:
/// =============
/// - Consumer: Receives message from MassTransit (this class)
/// - Handler: Processes the command (ImportEventsCommandConsumer in Application layer)
///
/// This separation allows:
/// 1. Handler can be unit tested without MassTransit
/// 2. Same handler can be called from HTTP endpoint if needed
/// 3. Clean Architecture - Worker depends on Application, not vice versa
///
/// REGISTRATION:
/// =============
/// <code>
/// services.AddMassTransit(x =>
/// {
///     x.AddConsumer&lt;ImportEventsConsumer&gt;();
///     x.UsingRabbitMq((context, cfg) =>
///     {
///         cfg.ConfigureEndpoints(context);
///     });
/// });
/// </code>
/// </remarks>
public class ImportEventsConsumer : IConsumer<ImportEventsCommand>
{
    private readonly ImportEventsCommandConsumer _handler;

    public ImportEventsConsumer(ImportEventsCommandConsumer handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Processes an ImportEventsCommand message from the queue.
    /// </summary>
    /// <param name="context">MassTransit consume context</param>
    public Task Consume(ConsumeContext<ImportEventsCommand> context)
    {
        return _handler.Consume(context);
    }
}
