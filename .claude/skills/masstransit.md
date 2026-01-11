# MassTransit Skill

A comprehensive guide for implementing MassTransit messaging patterns in .NET applications, following Chris Patterson's best practices and official documentation.

## Table of Contents

1. [Send vs Publish - The Golden Rule](#send-vs-publish---the-golden-rule)
2. [Message Contracts](#message-contracts)
3. [Consumer Implementation](#consumer-implementation)
4. [Configuration](#configuration)
5. [Error Handling and Retry Policies](#error-handling-and-retry-policies)
6. [Testing Patterns](#testing-patterns)
7. [Saga Patterns](#saga-patterns)
8. [Common Pitfalls](#common-pitfalls)
9. [Quick Reference](#quick-reference)

---

## Send vs Publish - The Golden Rule

**This is the most critical concept in MassTransit. Get this wrong and your system will have unpredictable behavior.**

### Commands = Send() = Point-to-Point

Commands represent a request for something to happen. They are sent to a **specific endpoint** and should be handled by **exactly one consumer**.

```csharp
// CORRECT: Use Send() for commands
await bus.Send(new TrainModelsCommand(
    Timestamp: DateTimeOffset.UtcNow
));

// Also correct: Using ISendEndpointProvider for explicit endpoint control
var endpoint = await sendEndpointProvider.GetSendEndpoint(new Uri("queue:train-models"));
await endpoint.Send(command);
```

**Command Characteristics:**
- Imperative naming: `SubmitOrder`, `TrainModels`, `SyncNextEvent`
- One sender, one receiver
- Expects action to be performed
- May fail (and should have error handling)

### Events = Publish() = Pub/Sub

Events represent something that **has already happened**. They are broadcast to **all interested subscribers** (zero or more).

```csharp
// CORRECT: Use Publish() for events
await publishEndpoint.Publish(new ModelsTrainedEvent(
    CommandId: commandId,
    TrainedAt: DateTimeOffset.UtcNow,
    Models: modelMetadata,
    TotalTrainingSamples: sampleCount
));

// Within a consumer, use ConsumeContext
await context.Publish(new OrderSubmittedEvent { OrderId = orderId });
```

**Event Characteristics:**
- Past-tense naming: `OrderSubmitted`, `ModelsTrainedEvent`, `EventSyncedEvent`
- One publisher, zero or more subscribers
- Indicates something happened
- Cannot fail (already happened)

### NEVER Do This

```csharp
// WRONG: Publishing a command (will send to ALL subscribers!)
await bus.Publish(new TrainModelsCommand(...)); // BAD!

// WRONG: Sending an event (will only go to one queue!)
await bus.Send(new OrderSubmittedEvent(...)); // BAD!
```

### IBus vs ISendEndpointProvider vs IPublishEndpoint

| Interface | Use For | When to Use |
|-----------|---------|-------------|
| `IBus` | Both Send and Publish | **DEFAULT CHOICE** - Controllers, services, background services |
| `IPublishEndpoint` | Publishing events only | Rarely needed - IBus can do this |
| `ISendEndpointProvider` | Sending commands with explicit endpoint | Only when you need custom queue names or endpoint caching |
| `ConsumeContext` | Both (within consumers) | **ALWAYS use this inside consumers** - never inject IBus |

**RECOMMENDATION (per Chris Patterson/phatboyg):** Use `IBus` with endpoint conventions. This is simpler than `ISendEndpointProvider` for most use cases.

### Step 1: Configure Endpoint Conventions (before AddMassTransit)

```csharp
// In Program.cs - configure BEFORE AddMassTransit
using MassTransit;

EndpointConvention.Map<SyncNextEventCommand>(new Uri("queue:SyncNextEvent"));
EndpointConvention.Map<ImportEventsCommand>(new Uri("queue:ImportEvents"));
EndpointConvention.Map<TrainModelsCommand>(new Uri("queue:train-models"));

builder.Services.AddMassTransit(x => { /* ... */ });
```

### Step 2: Use IBus.Send() in Controllers

```csharp
// RECOMMENDED: Use IBus in controllers and services
public class AdminController : Controller
{
    private readonly IBus _bus;

    public AdminController(IBus bus) => _bus = bus;

    [HttpPost]
    public async Task<IActionResult> TrainModels()
    {
        var command = new TrainModelsCommand(Timestamp: DateTimeOffset.UtcNow);
        await _bus.Send(command);  // Convention-based routing - simple!
        return Accepted();
    }
}

// ONLY use ISendEndpointProvider when you need explicit endpoint control:
// - Custom queue names that don't match conventions
// - Caching send endpoints for performance
// - Multi-tenant scenarios with dynamic endpoints
```

---

## Message Contracts

### Design Principles

1. **Use Records (Immutable)**: Messages should be immutable value objects
2. **Use DateTimeOffset**: Always use `DateTimeOffset` instead of `DateTime` for timestamps
3. **DO NOT include CorrelationId**: MassTransit handles CorrelationId in message headers automatically
4. **Keep Contracts in Shared Library**: Reference from both sender and receiver

### Command Contract Template

```csharp
namespace PulpMXFantasy.Contracts.Commands;

/// <summary>
/// Command to train all ML models for predictions.
/// Commands use imperative naming (verb + noun).
/// Note: CorrelationId is handled by MassTransit in message headers - do NOT include it in contracts.
/// </summary>
public record TrainModelsCommand(
    DateTimeOffset Timestamp,
    bool Force = false  // Optional: retrain even if recent models exist
);

/// <summary>
/// Command to sync the next upcoming event from external API.
/// </summary>
public record SyncNextEventCommand(
    DateTimeOffset Timestamp
);

/// <summary>
/// Command to import multiple historical events.
/// </summary>
public record ImportEventsCommand(
    DateTimeOffset Timestamp,
    List<string> EventSlugs
);
```

### Event Contract Template

```csharp
namespace PulpMXFantasy.Contracts.Events;

/// <summary>
/// Event published when ML models have been trained.
/// Events use past-tense naming (noun + verb past tense).
/// Note: CorrelationId is handled by MassTransit in message headers - do NOT include it in contracts.
/// </summary>
public record ModelsTrainedEvent(
    DateTimeOffset TrainedAt,
    List<ModelMetadata> Models,
    int TotalTrainingSamples
);

/// <summary>
/// Event published when an event has been synced from external API.
/// </summary>
public record EventSyncedEvent(
    Guid EventId,
    string EventName,
    string EventSlug,
    DateTimeOffset EventDate,
    int RiderCount
);

/// <summary>
/// Event published when predictions have been generated.
/// </summary>
public record PredictionsGeneratedEvent(
    Guid EventId,
    DateTimeOffset GeneratedAt,
    int PredictionCount,
    string ModelVersion
);

/// <summary>
/// Supporting type for model metadata.
/// </summary>
public record ModelMetadata(
    Guid Id,
    string BikeClass,
    string ModelType,
    string Version,
    DateTimeOffset TrainedAt,
    int TrainingSamples,
    double? ValidationAccuracy,
    double? RSquared,
    double? MeanAbsoluteError
);
```

### Correlation Conventions

**Important:** CorrelationId is automatically handled by MassTransit in message **headers**, not in the message contract itself. Do NOT include CorrelationId as a property in your message contracts.

MassTransit automatically correlates messages by looking for these property names if you need custom correlation:
1. `EventId`
2. `OrderId` (or other business identifiers)

```csharp
// CORRECT: CorrelationId is in headers, not the contract
public record OrderSubmitted(
    Guid OrderId,  // Business identifier, NOT CorrelationId
    DateTimeOffset Timestamp
);

// Access CorrelationId from ConsumeContext in consumers:
public async Task Consume(ConsumeContext<OrderSubmitted> context)
{
    var correlationId = context.CorrelationId; // From headers
}

// Manual correlation configuration (if needed)
GlobalTopology.Send.UseCorrelationId<SubmitOrder>(x => x.OrderId);
```

---

## Consumer Implementation

### Naming Convention

- **Commands**: Use `{CommandName}CommandConsumer` (e.g., `TrainModelsCommandConsumer`)
- **Events**: Use `{EventName}EventConsumer` (e.g., `ModelsTrainedEventConsumer`)

This makes it immediately clear whether a consumer handles a command or an event.

### Command Consumer Template

```csharp
using MassTransit;

namespace PulpMXFantasy.Worker.Consumers;

/// <summary>
/// Consumer for TrainModelsCommand.
/// Handles the command and publishes ModelsTrainedEvent on success.
/// </summary>
public class TrainModelsCommandConsumer : IConsumer<TrainModelsCommand>
{
    private readonly IModelTrainer _modelTrainer;
    private readonly ICommandStatusService _statusService;
    private readonly ILogger<TrainModelsCommandConsumer> _logger;

    public TrainModelsCommandConsumer(
        IModelTrainer modelTrainer,
        ICommandStatusService statusService,
        ILogger<TrainModelsCommandConsumer> logger)
    {
        _modelTrainer = modelTrainer;
        _statusService = statusService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TrainModelsCommand> context)
    {
        var command = context.Message;

        _logger.LogInformation("Processing TrainModelsCommand");

        try
        {
            var correlationId = context.CorrelationId ?? Guid.NewGuid();

            // Update status to running
            await _statusService.UpdateStatusAsync(
                correlationId,
                "Running",
                "Training 250cc Qualification model...");

            // Train models (long-running operation)
            var models = await _modelTrainer.TrainAllModelsAsync();

            // Update status to completed
            await _statusService.UpdateStatusAsync(
                correlationId,
                "Completed",
                "All models trained successfully");

            // IMPORTANT: Publish event (NOT Send) - uses ConsumeContext
            await context.Publish(new ModelsTrainedEvent(
                TrainedAt: DateTimeOffset.UtcNow,
                Models: models,
                TotalTrainingSamples: models.Sum(m => m.TrainingSamples)
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to train models");

            await _statusService.UpdateStatusAsync(
                context.CorrelationId ?? Guid.NewGuid(),
                "Failed",
                ex.Message);

            throw; // Rethrow to trigger retry/error handling
        }
    }
}
```

### Event Consumer Template

```csharp
/// <summary>
/// Consumer for ModelsTrainedEvent.
/// Automatically generates predictions when models are trained.
/// </summary>
public class ModelsTrainedEventConsumer : IConsumer<ModelsTrainedEvent>
{
    private readonly IPredictionService _predictionService;
    private readonly IReadModelUpdater _readModelUpdater;
    private readonly ILogger<ModelsTrainedEventConsumer> _logger;

    public ModelsTrainedEventConsumer(
        IPredictionService predictionService,
        IReadModelUpdater readModelUpdater,
        ILogger<ModelsTrainedEventConsumer> logger)
    {
        _predictionService = predictionService;
        _readModelUpdater = readModelUpdater;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ModelsTrainedEvent> context)
    {
        var @event = context.Message;

        _logger.LogInformation("Models trained, generating predictions");

        // Reload models
        await _predictionService.ReloadModelsAsync();

        // Generate predictions for upcoming events
        var predictions = await _predictionService.GeneratePredictionsForNextEventAsync();

        // Update read model
        await _readModelUpdater.UpdatePredictionsAsync(predictions);

        // Publish event for downstream consumers
        await context.Publish(new PredictionsGeneratedEvent(
            EventId: predictions.EventId,
            GeneratedAt: DateTimeOffset.UtcNow,
            PredictionCount: predictions.Count,
            ModelVersion: @event.Models.First().Version
        ));
    }
}
```

### Consumer Best Practices

1. **Use ConsumeContext for publishing/sending within consumers** - never inject IBus into consumers
2. **Make consumers idempotent** - same message processed twice should have same result
3. **Keep consumers focused** - one consumer, one responsibility
4. **Use naming convention** - `{Name}CommandConsumer` for commands, `{Name}EventConsumer` for events
5. **Update status for long-running operations** - so UI can poll progress

---

## Configuration

### Worker Service Configuration (Full)

```csharp
// Program.cs for Worker Service
var builder = Host.CreateApplicationBuilder(args);

// Add DbContexts
builder.Services.AddDbContext<WriteDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WriteDb")));
builder.Services.AddDbContext<ReadDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ReadDb")));

// Add infrastructure services
builder.Services.AddScoped<IModelTrainer, ModelTrainer>();
builder.Services.AddScoped<IPredictionService, PredictionService>();
builder.Services.AddScoped<IEventSyncService, EventSyncService>();
builder.Services.AddScoped<ICommandStatusService, CommandStatusService>();
builder.Services.AddScoped<IReadModelUpdater, ReadModelUpdater>();

// Add MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    // Auto-register all consumers from this assembly
    x.AddConsumers(typeof(Program).Assembly);

    // Or register individually for explicit control
    // x.AddConsumer<TrainModelsCommandConsumer>();
    // x.AddConsumer<SyncNextEventCommandConsumer>();
    // x.AddConsumer<ImportEventsCommandConsumer>();
    // x.AddConsumer<ModelsTrainedEventConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"]);
            h.Password(builder.Configuration["RabbitMQ:Password"]);
        });

        // Global retry policy (applies to all consumers)
        cfg.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30)
        ));

        // Configure endpoints automatically based on consumer names
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
await host.RunAsync();
```

### Web Application Configuration (Send-Only)

```csharp
// Program.cs for Web Application
var builder = WebApplication.CreateBuilder(args);

// Add ReadDbContext only (CQRS - Web only reads)
builder.Services.AddDbContext<ReadDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ReadDb")));

// Add MassTransit (send-only, no consumers)
builder.Services.AddMassTransit(x =>
{
    // No consumers registered - this is send-only

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"]);
            h.Password(builder.Configuration["RabbitMQ:Password"]);
        });

        // No ConfigureEndpoints needed for send-only
    });
});

builder.Services.AddControllersWithViews();

var app = builder.Build();
// ... middleware configuration
```

### Docker Compose (RabbitMQ + PostgreSQL)

```yaml
version: '3.8'
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: pulpmx_fantasy
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      timeout: 5s
      retries: 5

  rabbitmq:
    image: rabbitmq:3.13-management-alpine
    hostname: pulpmx-rabbitmq
    environment:
      RABBITMQ_DEFAULT_USER: admin
      RABBITMQ_DEFAULT_PASS: admin
    ports:
      - "5672:5672"    # AMQP protocol
      - "15672:15672"  # Management UI (http://localhost:15672)
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "check_port_connectivity"]
      interval: 5s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
  rabbitmq_data:
```

### appsettings.json Example

```json
{
  "ConnectionStrings": {
    "WriteDb": "Host=localhost;Database=pulpmx_fantasy;Username=postgres;Password=postgres",
    "ReadDb": "Host=localhost;Database=pulpmx_fantasy;Username=postgres;Password=postgres"
  },
  "RabbitMQ": {
    "Host": "localhost",
    "Username": "admin",
    "Password": "admin"
  }
}
```

---

## Error Handling and Retry Policies

### Retry Policy Configuration

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    cfg.Host("localhost", "/", h => { ... });

    // Immediate retry (fast failures)
    cfg.UseMessageRetry(r => r.Immediate(5));

    // OR: Incremental backoff
    cfg.UseMessageRetry(r => r.Incremental(5,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2)));

    // OR: Exponential backoff (recommended for transient failures)
    cfg.UseMessageRetry(r => r.Exponential(5,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(5)));

    // OR: Custom intervals
    cfg.UseMessageRetry(r => r.Intervals(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1)
    ));

    cfg.ConfigureEndpoints(context);
});
```

### Delayed Redelivery (Second-Level Retry)

For longer delays between retries (requires RabbitMQ delayed-exchange plugin):

```csharp
x.AddConfigureEndpointsCallback((context, name, cfg) =>
{
    // Second-level retry: requeue with delay
    cfg.UseDelayedRedelivery(r => r.Intervals(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30)
    ));

    // First-level retry: immediate
    cfg.UseMessageRetry(r => r.Immediate(5));
});
```

### Exception Filters

Retry only specific exceptions:

```csharp
cfg.UseMessageRetry(r =>
{
    r.Intervals(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));

    // Only retry these exceptions
    r.Handle<TimeoutException>();
    r.Handle<HttpRequestException>();

    // Never retry these
    r.Ignore<ValidationException>();
    r.Ignore<ArgumentException>();
});
```

### Error Queue Behavior

By default, MassTransit moves failed messages to `{queue-name}_error` after all retries are exhausted.

```csharp
// To use RabbitMQ's native dead-letter queue instead:
cfg.ReceiveEndpoint("my-queue", e =>
{
    e.ThrowOnSkippedMessages();
    e.RethrowFaultedMessages();
    e.ConfigureConsumer<MyConsumer>(context);
});
```

### Fault Events

MassTransit publishes `Fault<T>` events when message processing fails:

```csharp
public class OrderFaultConsumer : IConsumer<Fault<SubmitOrder>>
{
    public async Task Consume(ConsumeContext<Fault<SubmitOrder>> context)
    {
        // Handle the failure (logging, notifications, compensating actions)
        var originalMessage = context.Message.Message;
        var exceptions = context.Message.Exceptions;

        _logger.LogError("Order {OrderId} failed: {Error}",
            originalMessage.OrderId,
            exceptions.FirstOrDefault()?.Message);
    }
}
```

---

## Testing Patterns

### Unit Testing with Test Harness

```csharp
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class TrainModelsCommandConsumerTests
{
    [Fact]
    public async Task TrainModelsCommand_PublishesModelsTrainedEvent()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddSingleton<IModelTrainer, FakeModelTrainer>()
            .AddSingleton<ICommandStatusService, FakeCommandStatusService>()
            .AddLogging()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<TrainModelsCommandConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act
        var command = new TrainModelsCommand(
            Timestamp: DateTimeOffset.UtcNow,
            Force: false
        );

        await harness.Bus.Publish(command);

        // Assert - wait for consumption with timeout
        Assert.True(await harness.Consumed.Any<TrainModelsCommand>());
        Assert.True(await harness.Published.Any<ModelsTrainedEvent>());

        // Assert on consumer specifically
        var consumerHarness = harness.GetConsumerHarness<TrainModelsCommandConsumer>();
        Assert.True(await consumerHarness.Consumed.Any<TrainModelsCommand>());
    }

    [Fact]
    public async Task TrainModelsCommand_WhenTrainingFails_DoesNotPublishEvent()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddSingleton<IModelTrainer, FailingModelTrainer>()
            .AddSingleton<ICommandStatusService, FakeCommandStatusService>()
            .AddLogging()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<TrainModelsCommandConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act
        await harness.Bus.Publish(new TrainModelsCommand(
            Timestamp: DateTimeOffset.UtcNow
        ));

        // Assert
        Assert.True(await harness.Consumed.Any<TrainModelsCommand>());
        Assert.False(await harness.Published.Any<ModelsTrainedEvent>());
    }
}
```

### Integration Testing with Testcontainers

```csharp
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

public class TrainModelsIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RabbitMqContainer _rabbitmq = null!;
    private IHost _worker = null!;
    private IBus _bus = null!;

    public async Task InitializeAsync()
    {
        // Start containers
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("pulpmx_test")
            .Build();

        _rabbitmq = new RabbitMqBuilder()
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await Task.WhenAll(
            _postgres.StartAsync(),
            _rabbitmq.StartAsync()
        );

        // Build and start worker
        _worker = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddDbContext<WriteDbContext>(opt =>
                    opt.UseNpgsql(_postgres.GetConnectionString()));

                services.AddMassTransit(x =>
                {
                    x.AddConsumer<TrainModelsCommandConsumer>();

                    x.UsingRabbitMq((ctx, cfg) =>
                    {
                        cfg.Host(_rabbitmq.Hostname, _rabbitmq.GetMappedPublicPort(5672), "/", h =>
                        {
                            h.Username("test");
                            h.Password("test");
                        });
                        cfg.ConfigureEndpoints(ctx);
                    });
                });

                // Add other services...
            })
            .Build();

        await _worker.StartAsync();
        _bus = _worker.Services.GetRequiredService<IBus>();
    }

    public async Task DisposeAsync()
    {
        await _worker.StopAsync();
        await _postgres.DisposeAsync();
        await _rabbitmq.DisposeAsync();
    }

    [Fact]
    public async Task TrainModels_EndToEnd_UpdatesReadModel()
    {
        // Arrange
        var command = new TrainModelsCommand(
            Timestamp: DateTimeOffset.UtcNow
        );

        // Act
        await _bus.Send(command);

        // Wait for processing (use polling with timeout instead of fixed delay)
        using var scope = _worker.Services.CreateScope();
        var readDb = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        var timeout = DateTimeOffset.UtcNow.AddMinutes(2);
        CommandStatus? status = null;

        while (DateTimeOffset.UtcNow < timeout)
        {
            // Query by CorrelationId from context, not from message
            status = await readDb.CommandStatus
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (status?.Status == "Completed")
                break;

            await Task.Delay(500);
        }

        // Assert
        Assert.NotNull(status);
        Assert.Equal("Completed", status.Status);

        var models = await readDb.ModelMetadata
            .Where(x => x.IsActive)
            .ToListAsync();
        Assert.Equal(4, models.Count);
    }
}
```

### Mocking Dependencies in Consumer Tests

```csharp
public class FakeModelTrainer : IModelTrainer
{
    public Task<List<ModelMetadata>> TrainAllModelsAsync()
    {
        return Task.FromResult(new List<ModelMetadata>
        {
            new(Guid.NewGuid(), "250", "Qualification", "v1.0", DateTimeOffset.UtcNow, 500, 0.85, null, null),
            new(Guid.NewGuid(), "250", "FinishPosition", "v1.0", DateTimeOffset.UtcNow, 500, null, 0.78, 2.1),
            new(Guid.NewGuid(), "450", "Qualification", "v1.0", DateTimeOffset.UtcNow, 600, 0.88, null, null),
            new(Guid.NewGuid(), "450", "FinishPosition", "v1.0", DateTimeOffset.UtcNow, 600, null, 0.81, 1.9),
        });
    }
}

public class FailingModelTrainer : IModelTrainer
{
    public Task<List<ModelMetadata>> TrainAllModelsAsync()
    {
        throw new InvalidOperationException("Insufficient training data");
    }
}
```

---

## Saga Patterns

### When to Use Sagas

- Coordinating long-running processes across multiple services
- Managing transactions that span multiple bounded contexts
- Implementing compensating transactions for failures

### Basic State Machine Saga

```csharp
public class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public DateTime? OrderDate { get; set; }
    public DateTime? PaymentDate { get; set; }
}

public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public State Submitted { get; private set; } = null!;
    public State Paid { get; private set; } = null!;
    public State Shipped { get; private set; } = null!;

    public Event<OrderSubmitted> OrderSubmitted { get; private set; } = null!;
    public Event<PaymentReceived> PaymentReceived { get; private set; } = null!;
    public Event<OrderShipped> OrderShipped { get; private set; } = null!;

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderSubmitted, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentReceived, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => OrderShipped, x => x.CorrelateById(ctx => ctx.Message.OrderId));

        Initially(
            When(OrderSubmitted)
                .Then(ctx => ctx.Saga.OrderDate = ctx.Message.OrderDate)
                .TransitionTo(Submitted)
        );

        During(Submitted,
            When(PaymentReceived)
                .Then(ctx => ctx.Saga.PaymentDate = ctx.Message.PaymentDate)
                .TransitionTo(Paid)
                .Publish(ctx => new StartShipping { OrderId = ctx.Saga.CorrelationId })
        );

        During(Paid,
            When(OrderShipped)
                .TransitionTo(Shipped)
                .Finalize()
        );
    }
}
```

### Saga Registration

```csharp
x.AddSagaStateMachine<OrderStateMachine, OrderState>()
    .EntityFrameworkRepository(r =>
    {
        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
        r.AddDbContext<DbContext, OrderDbContext>();
    });
```

---

## Common Pitfalls

### 1. Using IBus Inside Consumers

```csharp
// WRONG - Never inject IBus in consumers
public class BadConsumer : IConsumer<OrderSubmitted>
{
    private readonly IBus _bus;
    public BadConsumer(IBus bus) => _bus = bus;

    public async Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        await _bus.Publish(new OrderProcessed { ... }); // BAD!
    }
}

// CORRECT - Use ConsumeContext
public class GoodConsumer : IConsumer<OrderSubmitted>
{
    public async Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        await context.Publish(new OrderProcessed { ... }); // GOOD!
    }
}
```

**Why?** ConsumeContext propagates correlation IDs, trace information, and handles transaction scope properly.

### 2. Not Making Consumers Idempotent

MassTransit guarantees at-least-once delivery. Your consumer may receive the same message multiple times.

```csharp
// WRONG - Not idempotent
public async Task Consume(ConsumeContext<CreateOrder> context)
{
    await _db.Orders.AddAsync(new Order { Id = context.Message.OrderId });
    await _db.SaveChangesAsync(); // Will fail on retry!
}

// CORRECT - Idempotent
public async Task Consume(ConsumeContext<CreateOrder> context)
{
    var exists = await _db.Orders.AnyAsync(o => o.Id == context.Message.OrderId);
    if (exists) return; // Already processed

    await _db.Orders.AddAsync(new Order { Id = context.Message.OrderId });
    await _db.SaveChangesAsync();
}
```

### 3. Publishing Before Persistence is Complete

```csharp
// WRONG - Event published before save completes
public async Task Consume(ConsumeContext<CreateOrder> context)
{
    await context.Publish(new OrderCreated { ... }); // Published before save!
    await _db.SaveChangesAsync(); // What if this fails?
}

// CORRECT - Use Outbox pattern or ensure order
public async Task Consume(ConsumeContext<CreateOrder> context)
{
    await _db.Orders.AddAsync(order);
    await _db.SaveChangesAsync();
    await context.Publish(new OrderCreated { ... }); // After save
}
```

### 4. Blocking Async Code

```csharp
// WRONG - Blocking on async
public void SomeMethod()
{
    var result = _bus.Send(command).Result; // Deadlock risk!
}

// CORRECT - Use async all the way
public async Task SomeMethodAsync()
{
    await _bus.Send(command);
}
```

### 5. Not Configuring Timeouts for Long Operations

```csharp
// For long-running consumers (like model training)
cfg.ReceiveEndpoint("train-models", e =>
{
    e.PrefetchCount = 1; // Process one at a time
    e.UseConcurrencyLimit(1); // Limit concurrency

    // Increase consumer timeout for long operations
    e.UseTimeout(t => t.Timeout = TimeSpan.FromMinutes(10));

    e.ConfigureConsumer<TrainModelsCommandConsumer>(context);
});
```

### 6. Not Handling Poison Messages

```csharp
// Configure circuit breaker for repeated failures
cfg.ReceiveEndpoint("my-queue", e =>
{
    e.UseCircuitBreaker(cb =>
    {
        cb.TrackingPeriod = TimeSpan.FromMinutes(1);
        cb.TripThreshold = 15;
        cb.ActiveThreshold = 10;
        cb.ResetInterval = TimeSpan.FromMinutes(5);
    });

    e.ConfigureConsumer<MyConsumer>(context);
});
```

---

## Quick Reference

### Send vs Publish Decision Tree

```
Is the message asking for something to happen?
├── YES → Is it a COMMAND
│         Use: Send() / IBus.Send() / ISendEndpointProvider
│         Naming: Imperative (SubmitOrder, TrainModels)
│         Receivers: Exactly ONE
│
└── NO → It's an EVENT
         Use: Publish() / context.Publish() / IPublishEndpoint
         Naming: Past tense (OrderSubmitted, ModelsTrainedEvent)
         Receivers: Zero or MORE
```

### Required NuGet Packages

**Note:** MassTransit 9.x requires a license. Use 8.3.x for free production use.

```xml
<!-- Contracts project -->
<PackageReference Include="MassTransit.Abstractions" Version="8.3.6" />

<!-- Worker/Application project -->
<PackageReference Include="MassTransit" Version="8.3.6" />
<PackageReference Include="MassTransit.RabbitMQ" Version="8.3.6" />

<!-- Web project -->
<PackageReference Include="MassTransit" Version="8.3.6" />
<PackageReference Include="MassTransit.RabbitMQ" Version="8.3.6" />

<!-- Test projects -->
<PackageReference Include="MassTransit.Testing" Version="8.3.6" />
<PackageReference Include="Testcontainers.RabbitMq" Version="3.*" />
<PackageReference Include="Testcontainers.PostgreSql" Version="3.*" />
```

### Checklist Before Sending a Message

- [ ] Is it a command? Use `Send()`
- [ ] Is it an event? Use `Publish()`
- [ ] Are all timestamps using `DateTimeOffset`?
- [ ] CorrelationId NOT in contract? (MassTransit handles it in headers)
- [ ] Are you inside a consumer? Use `ConsumeContext`
- [ ] Is the consumer idempotent?
- [ ] Consumer named correctly? (`{Name}CommandConsumer` or `{Name}EventConsumer`)
- [ ] Have you configured appropriate retry policies?

---

## References

- [MassTransit Documentation](https://masstransit.io/documentation/concepts)
- [Chris Patterson - MassTransit Creator](https://lostechies.com/chrispatterson/)
- [Send vs Publish Discussion](https://github.com/MassTransit/MassTransit/discussions/3911)
- [MassTransit Testing](https://masstransit.io/documentation/concepts/testing)
- [Saga State Machine](https://masstransit.io/documentation/patterns/saga/state-machine)
- [Error Handling](https://masstransit.io/documentation/concepts/exceptions)
- [SE Radio 654: Chris Patterson on MassTransit (2025)](https://se-radio.net/2025/02/se-radio-654-chris-patterson-on-masstransit-and-event-driven-systems/)
