using MassTransit;
using Microsoft.Extensions.Configuration;

namespace PulpMXFantasy.Messaging;

/// <summary>
/// RabbitMQ configuration helpers for MassTransit.
/// </summary>
/// <remarks>
/// CONFIGURATION:
/// ==============
/// RabbitMQ settings are read from configuration:
/// - RabbitMQ:Host (default: "localhost")
/// - RabbitMQ:Username (default: "admin")
/// - RabbitMQ:Password (default: "admin")
///
/// ENVIRONMENT VARIABLES:
/// ======================
/// In Docker/production, use environment variables:
/// - RabbitMQ__Host=rabbitmq
/// - RabbitMQ__Username=admin
/// - RabbitMQ__Password=admin
/// </remarks>
public static class RabbitMqConfiguration
{
    /// <summary>
    /// Configures RabbitMQ host connection from configuration.
    /// </summary>
    /// <param name="cfg">RabbitMQ bus factory configurator</param>
    /// <param name="configuration">Application configuration</param>
    public static void ConfigureRabbitMqHost(
        this IRabbitMqBusFactoryConfigurator cfg,
        IConfiguration configuration)
    {
        var host = configuration.GetValue<string>("RabbitMQ:Host") ?? "localhost";
        var username = configuration.GetValue<string>("RabbitMQ:Username") ?? "admin";
        var password = configuration.GetValue<string>("RabbitMQ:Password") ?? "admin";

        cfg.Host(host, "/", h =>
        {
            h.Username(username);
            h.Password(password);
        });
    }

    /// <summary>
    /// Gets RabbitMQ host from configuration.
    /// </summary>
    public static string GetHost(IConfiguration configuration)
        => configuration.GetValue<string>("RabbitMQ:Host") ?? "localhost";

    /// <summary>
    /// Gets RabbitMQ username from configuration.
    /// </summary>
    public static string GetUsername(IConfiguration configuration)
        => configuration.GetValue<string>("RabbitMQ:Username") ?? "admin";

    /// <summary>
    /// Gets RabbitMQ password from configuration.
    /// </summary>
    public static string GetPassword(IConfiguration configuration)
        => configuration.GetValue<string>("RabbitMQ:Password") ?? "admin";
}
