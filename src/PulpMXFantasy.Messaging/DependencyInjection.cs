using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PulpMXFantasy.Messaging;

/// <summary>
/// Dependency injection configuration for MassTransit messaging.
/// </summary>
/// <remarks>
/// USAGE:
/// ======
/// Web (send-only):
/// <code>
/// builder.Services.AddMessagingSendOnly(builder.Configuration);
/// </code>
///
/// Worker (consumers):
/// <code>
/// builder.Services.AddMessagingWithConsumers(builder.Configuration, x =>
/// {
///     x.AddConsumer&lt;MyConsumer&gt;();
/// });
/// </code>
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Registers MassTransit for send-only (no consumers).
    /// Use this in Web project.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddMessagingSendOnly(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Map endpoint conventions before adding MassTransit
        EndpointConventions.MapAllEndpoints();

        services.AddMassTransit(x =>
        {
            // No consumers registered - send only

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.ConfigureRabbitMqHost(configuration);

                // No ConfigureEndpoints needed for send-only
            });
        });

        return services;
    }

    /// <summary>
    /// Registers MassTransit with consumer registration.
    /// Use this in Worker project.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <param name="configureConsumers">Action to configure consumers</param>
    /// <param name="configureEndpoints">Optional action to customize endpoint configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddMessagingWithConsumers(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator> configureConsumers,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureEndpoints = null)
    {
        // Map endpoint conventions before adding MassTransit
        EndpointConventions.MapAllEndpoints();

        services.AddMassTransit(x =>
        {
            // Register consumers via callback
            configureConsumers(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.ConfigureRabbitMqHost(configuration);

                // Allow custom endpoint configuration
                if (configureEndpoints != null)
                {
                    configureEndpoints(context, cfg);
                }
                else
                {
                    // Default: auto-configure all endpoints
                    cfg.ConfigureEndpoints(context);
                }
            });
        });

        return services;
    }
}
