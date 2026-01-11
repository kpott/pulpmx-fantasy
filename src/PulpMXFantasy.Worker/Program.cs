using MassTransit;
using Microsoft.EntityFrameworkCore;
using PulpMXFantasy.Application.Consumers;
using PulpMXFantasy.Infrastructure;
using PulpMXFantasy.Messaging;
using PulpMXFantasy.ReadModel;
using PulpMXFantasy.Worker.Consumers;

var builder = Host.CreateApplicationBuilder(args);

// ============================================================================
// DEPENDENCY INJECTION - CQRS WORKER (WRITE + PROCESS COMMANDS)
// ============================================================================
// Worker Service follows CQRS pattern:
// - WRITES to database (ApplicationDbContext)
// - PROCESSES commands via MassTransit consumers
// - RUNS ML services (training, prediction)
// - UPDATES read models after command completion

// Add Infrastructure services (write database, ML, API client)
builder.Services.AddInfrastructure(builder.Configuration);

// Add ReadModel services (for updating read models after commands complete)
builder.Services.AddReadModel(builder.Configuration);

// Register Application layer command handlers (business logic)
builder.Services.AddScoped<SyncNextEventCommandConsumer>();
builder.Services.AddScoped<ImportEventsCommandConsumer>();
builder.Services.AddScoped<TrainModelsCommandConsumer>();

// Register Application layer event handlers
builder.Services.AddScoped<ModelsTrainedEventConsumer>();

// ============================================================================
// MASSTRANSIT (CQRS - CONSUME COMMANDS)
// ============================================================================
// Configure MassTransit for consuming commands from Web UI.
// Uses shared Messaging library for consistent RabbitMQ configuration.

builder.Services.AddMessagingWithConsumers(
    builder.Configuration,
    // Register consumers
    configureConsumers: x =>
    {
        // Command consumers (thin wrappers around Application handlers)
        x.AddConsumer<SyncNextEventConsumer>();
        x.AddConsumer<ImportEventsConsumer>();
        x.AddConsumer<TrainModelsConsumer>();

        // Event consumers
        x.AddConsumer<ModelsTrainedConsumer>();
    },
    // Configure endpoints to match queue names from EndpointConventions
    configureEndpoints: (context, cfg) =>
    {
        // SyncNextEvent - must match EndpointConventions.SyncNextEventQueue
        cfg.ReceiveEndpoint(EndpointConventions.SyncNextEventQueue, e =>
        {
            e.ConfigureConsumer<SyncNextEventConsumer>(context);
        });

        // ImportEvents - must match EndpointConventions.ImportEventsQueue
        cfg.ReceiveEndpoint(EndpointConventions.ImportEventsQueue, e =>
        {
            e.ConfigureConsumer<ImportEventsConsumer>(context);
        });

        // TrainModels - long-running operations with special configuration
        cfg.ReceiveEndpoint(EndpointConventions.TrainModelsQueue, e =>
        {
            e.PrefetchCount = 1; // Only process one training job at a time
            e.ConcurrentMessageLimit = 1;
            e.ConfigureConsumer<TrainModelsConsumer>(context);
        });

        // ModelsTrainedConsumer - auto-configured (event, not command)
        cfg.ConfigureEndpoints(context);
    });

var host = builder.Build();

// ============================================================================
// DATABASE MIGRATION
// ============================================================================
// Apply pending migrations on startup.
using (var scope = host.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // Apply ApplicationDbContext migrations (write models)
        var appDbContext = scope.ServiceProvider.GetRequiredService<PulpMXFantasy.Infrastructure.Data.ApplicationDbContext>();
        logger.LogInformation("Applying ApplicationDbContext migrations...");
        await appDbContext.Database.MigrateAsync();
        logger.LogInformation("ApplicationDbContext migrations applied successfully");

        // Apply ReadDbContext migrations (read models)
        var readDbContext = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
        logger.LogInformation("Applying ReadDbContext migrations...");
        await readDbContext.Database.MigrateAsync();
        logger.LogInformation("ReadDbContext migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying database migrations");
        throw;
    }
}

host.Run();
